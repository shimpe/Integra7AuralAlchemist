# Parameter data memory optimization — design

Date: 2026-06-25
Branch: `parameter_optimization`

## Problem

`Src/Models/Data/Integra7Parameters.cs` (~58k lines, 5.5 MB) models the complete Integra-7 sysex
parameter structure — including data dependencies — as **one giant C# collection initializer of
~14,180 `Integra7ParameterSpec` objects**, plus shared `Repr` lookup tables and an analysis pass.

This is too memory-intensive to run comfortably on a tablet. The costs are:

1. **The giant initializer method** — ~14k `newobj` calls compile to one enormous method. This
   bloats the shipped assembly (multi-MB IL + ~14k interned path string literals) and forces a large
   **JIT spike at startup** whose native code stays resident. Often the dominant, overlooked cost on a
   constrained device.
2. **~14k reference objects** — ~120 B each (header + fields) ≈ ~1.7 MB.
3. **~14k `Address` `byte[]`** — each a separate ~32 B heap object for ≤4 MIDI bytes ≈ ~0.45 MB, plus
   ~14k GC objects.
4. **~14k unique `Path` strings** (~1.7 MB, heavily repeated prefixes) **+ a `Dictionary<string,int>`
   index** (~0.4 MB).

Per-render hotspots: `Ticks` allocates a fresh `AvaloniaList` on every access (slider template reads it
3×); `Name => Path.Split('/')[^1]` allocates on every call.

The specs are a **single template** — per-part addressing comes from the domain's Start/Offset, not 16×
duplication. So ~14k is roughly the irreducible content; the win is in *representation*, not count.

## Goals

- **Tier 1**: remove the giant initializer from the shipped assembly (assembly size + JIT spike).
- **Tier 2**: remove the ~14k heap objects and the per-object `byte[]` address; dedup strings; make
  `Ticks`/`Name` allocation-free.
- **No manual maintenance of binary blobs**: the binary is generated from the C# source of truth at
  build time; developers never hand-edit it.
- **Keep speed**: O(1) `Lookup`, fast range/prefix queries; columnar access is cache-friendlier, so
  this should be neutral-to-faster.

## Key decisions (agreed)

1. **Build-time generation, no committed blob.** The C# parameter definitions remain the editable
   source of truth, moved into a separate generator project. An MSBuild pre-build step runs the
   generator and emits the binary into the app's `Assets/` at build time. The blob is git-ignored.
   Edit C#, rebuild → blob auto-regenerates.
2. **Runtime `Integra7ParameterSpec` is a struct-view over a columnar store** (max memory). A separate
   constructible `ParameterDef` type is used for authoring/generation and in the unit tests.

## Architecture & project split

Separate **authoring** (build-time) from **runtime**, connected only by the binary format (no shared
types).

- **New generator project `Tools/ParameterBlobGenerator`** (console, build tool, *not shipped*):
  - `ParameterDef` — a plain constructible, mutable class with today's `Integra7ParameterSpec` fields.
  - The giant data initializer (the ~55k lines move here ~verbatim, retargeted to `ParameterDef`).
  - The `Repr` lookup tables and the CSV waveform loading (read directly via `File.IO`, no Avalonia
    `AssetLoader`).
  - `Integra7ParameterDatabaseAnalyzer` (moved here): the generator runs
    `MarkAllParentParametersAsIsParentTrue` + `FillInSecondaryDependencies` **once**, so the blob ships
    **pre-analyzed** — the runtime never runs the analysis or mutates specs.
  - The blob **serializer**.
- **App** keeps `Integra7Parameters`, but it now **loads the blob** into a columnar `ParameterStore`.
  The giant initializer, `Repr` tables, analyzer, and CSV loading are **gone from the shipped assembly**
  (the Tier-1 win).
- **Tests** reference the generator project (for `ParameterDef`/analyzer tests) and the app (for
  store/struct tests).

## Binary format & columnar store

`parameters.bin` — versioned, little-endian, embedded as an `AvaloniaResource`:

- **Header** — magic, format version, parameter count.
- **String table** — all distinct strings (paths, units, parent-paths, parvals, precomputed names),
  deduped, referenced by `int` id. (Path *prefix*-factoring is a possible v2; v1 dedups whole strings.)
- **Repr tables** — every distinct `int→string` map (including the CSV waveform maps, folded in at
  generation time), each with an id.
- **Discrete tables** — distinct `(int,string)` lists, by id.
- **Columns** — parallel arrays, one per field:
  `type`, `pathId`, `nameId` (precomputed last path segment → kills `Path.Split`), packed `address` +
  length, `imin`/`imax`, `omin`/`omax`, `bytes`, a `flags` byte (reserved | perNibble | isParent),
  `unitId`, `reprId`, `discreteId`, `parentPathId`, `parvalId`, `parentPath2Id`, `parval2Id`,
  `imin2`/`imax2`/`omin2`/`omax2`.

`ParameterStore` loads these into arrays once and rebuilds the shared `Repr` dictionaries (referenced by
id, so they stay shared). Estimated blob ≈ 400–600 KB vs. multi-MB of IL today.

## Runtime types

- **`Integra7ParameterSpec`** → `readonly struct` = `(ParameterStore store, int idx)`. Exposes the same
  public read-only properties consumers already use, computed from the columns:
  - `Address` rebuilds a `byte[]` on demand (only the handful of sysex/sort readers hit it). A new
    `AddressInt` is exposed so sort comparers can use the packed value instead of
    `ByteUtils.Bytes7ToInt(Address)`.
  - `Repr`/`Discrete` return the shared tables by id.
  - `Name` reads the precomputed `nameId` (no `Split`).
  - `ParentCtrl`/`IsParent`/etc. are **get-only** at runtime (analysis already baked into the blob).
  - `Ticks` computed on demand from the columns; `DataTemplateProvider` computes it once per control
    build (local var) instead of 3× via the property.
  - No per-spec heap object, ever.
- **`ParameterStore`** — owns the columns/tables; `Get(idx)`, plus prefix/range scans over the
  `pathId`/string columns returning struct-views (no materialization).
- **`Integra7Parameters`** — same public API (`Lookup`, `LookupIndex`, `GetParametersFromTo`,
  `GetParametersWithPrefix`), now backed by the store; signatures unchanged (the element type is now a
  struct).

## Build wiring (no committed blob)

MSBuild target in the app `.csproj`: before build, run
`dotnet run --project Tools/ParameterBlobGenerator -- -o <app>/Assets/parameters.bin`, with incremental
skip (regenerate only if the generator's sources changed). `parameters.bin` is git-ignored. The app
build depends on the generator building first.

## Testing & risk

- **Keep existing unit tests green**: tests that construct specs directly now build a small
  `ParameterStore.FromDefs(defs)` helper to obtain struct-views to feed services like
  `ParameterListSysexSizeCalculator` and `DisplayValueToRawValueConverter`. `ParameterDef` itself stays
  directly constructible for analyzer tests.
- **Golden test**: load the generated blob and assert the spec count + a sampling of
  paths/addresses/reprs/dependencies match what the in-code `ParameterDef` definitions produce — proves
  the generator and the loader agree on the format.
- **Risks**:
  - Struct value-semantics across the ~155 `Integra7ParameterSpec` use sites (mostly reads — low risk).
  - `Address` consumers (sysex build + sort) — addressed via on-demand `byte[]` and packed `AddressInt`.
  - Build-time generation step on CI / dev machines.
  - This is hardware-critical code that can't be fully tested here — the golden test plus keeping the
    runtime API identical are the safety net.

## Non-goals / preserved

- Keep the path→index dictionary (O(1) lookup), shared repr tables, and the parent/child dependency
  metadata semantics.
- No per-part templating (the data already uses one template + address offsets).
- Path prefix-factoring and per-section lazy eviction are explicitly deferred (possible future tiers).

## Rough implementation sequence

1. Introduce `ParameterDef` + move the data definitions/Repr tables/CSV loading/analyzer into the
   generator project.
2. Define the binary format + serializer (generator) and deserializer (`ParameterStore`).
3. Implement the struct-view `Integra7ParameterSpec` + `ParameterStore`.
4. Rewire runtime `Integra7Parameters` to load the blob; adapt `Address`/sort consumers.
5. Update tests + add the golden test.
6. Wire the MSBuild build-time generation; git-ignore the blob.

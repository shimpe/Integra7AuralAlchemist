# Known Issues

## Waveform name list ignores the Wave Group Type / Group ID dependency

**Status:** Resolved (2026-06-28). Wave names now resolve per Wave Group Type/ID via the reverse-engineered per-bank lists (`PartialWaveForms_{INT,SRX1..12}.csv`) loaded by the `WaveformBanks` service and surfaced through `FullyQualifiedParameter.EffectiveRepr` in both the advanced parameter tabs and the friendly editors. (Bank-switch-in-editor reactivity + the out-of-range reset land in Phase 2b.)

**Reported:** 2026-06-28 (during PCM Drums editor Phase 3).

### Symptom
The wave names shown in the PCM Drum Kit **WMT** wave pickers (and, by extension, in the raw "Advanced" parameter tabs) are wrong for many waves. The same flat name list is shown regardless of which wave bank is actually selected.

### Root cause
The actual waveform that a "Wave Number" refers to depends on **Wave Group Type** (`INT` / `SRX` / …) **and Wave Group ID** (which expansion/bank). The runtime models the wave name as a single flat number→name map:

- `PARTIAL_WAVEFORMS` — a flat `Dictionary<int,string>` loaded from `Tools/ParameterBlobGenerator/Assets/PartialWaveForms.csv` (see `ParameterDefinitions.cs`, `LoadWaveFormHelper`). It is attached as the `repr` of the "Wave Number L (Mono)" / "Wave Number R" parameters.

A flat list cannot represent a name that varies with Group Type + Group ID, so the displayed name is only correct for one bank (and, per the report, **the flat list itself is not correctly initialized even for the advanced PCM Synth parameters** — i.e. the base data is incomplete/incorrect, not just the drum reuse).

### Scope of impact
- **PCM Drum Kit** `WMT1..4 Wave Number L (Mono)` / `Wave Number R` (friendly Wave tab + Advanced tab). These were pointed at `PARTIAL_WAVEFORMS` in commit `913dcb5` so drums show names consistent with the synth; that reuse is correct *given* the data, but inherits the same flaw.
- **PCM Synth Tone** `Wave Number L (Mono)` / `Wave Number R` (pre-existing; this is where the flat list is also wrong).
- Any other parameter using `PARTIAL_WAVEFORMS`.

### What a proper fix likely requires
1. Reverse-engineer the correct waveform numbering per Group Type / Group ID (INT vs each SRX expansion), i.e. the real per-bank wave lists.
2. Replace the single flat `PARTIAL_WAVEFORMS` map with a Group-Type/ID-aware lookup (the displayed name must be a function of Group Type + Group ID + Wave Number), and make the friendly + advanced UIs resolve names through it (recomputing when Group Type/ID changes — these are discriminator-like dependencies).
3. Re-validate against hardware.

### Deliberately NOT done now
The friendly editors were built to surface names via the existing `repr` mechanism (consistent with the rest of the app). Correcting the underlying data/model is out of scope for the editor work and is tracked here for a future, dedicated effort.

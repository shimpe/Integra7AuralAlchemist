# Seeding the library from the instrument — design

**Goal.** Fill a snapshot library by sweeping the instrument: select each chosen preset or user patch on one
part, capture its temporary tone, write it as a library file. What the library's search, compare, duplicate
and morph features already do then applies to every sound the instrument can make, not only to the ones the
user happened to save by hand.

This was costed on 2026-07-28 and set aside. It is back because two constraints make it practical — skipping
user slots still named `INIT`, and being able to sweep a chosen subset rather than everything — and because
a spike on 2026-07-30 measured the real cost at **about an hour**, not the several hours the earlier costing
guessed.

---

## What the spike established

Every number here was measured against the user's own instrument on 2026-07-30, ~1,600 preset selections and
~1,000 captures. They are load-bearing: the design below is shaped by them, not by expectation.

**The instrument tells you when the tone has loaded, for free.** During a load the device *withholds* the
read reply rather than answering with the outgoing tone. Forty captures started with **zero delay** after the
bank/program writes were byte-identical to captures taken 1.5 s later, across all five engines. So
`CaptureToneAsync` **is** the settle check and there is nothing to poll.

**Do not settle by name.** It works — 804 of 805 patches matched on the first poll, inside 70 ms — but it
inherits a defect it does not need. `Presets.csv` disagrees with the instrument for **1.8–3.2%** of names
(`Power DrumSet` / `PowerDrumSet`, `2 0 8 0` / `2  0  8  0`), 0.8% after aggressive normalisation, and at
least one is genuinely different and unmatchable by any rule: PCMS PRST program 9 is `Ring Piano` in the
table and `Ring E.Piano` on the device. Each of those would burn a full timeout and be reported as a failed
capture of a patch that loaded perfectly.

**Reads do not flake.** Zero unanswered reads in ~17,000 requests against a loaded engine's area. Every
unanswered read observed was deliberately provoked by reading an area whose engine the part was not holding,
and those are **deterministic** — retried three times, they fail three times. So per-patch isolation is
needed for *availability*, not for flakiness.

**Cost per patch, and therefore per sweep:**

| engine | blocks | capture | full per patch | presets | sweep share |
| --- | --- | --- | --- | --- | --- |
| SN-A | 2 | 51 ms | 116 ms | 364 | 42 s |
| SN-S | 5 | 139 ms | 186 ms | 1,109 | 206 s |
| PCMS | 8 | 270 ms | 376 ms | 4,301 | 1,617 s |
| SN-D | 65 | 1,341 ms | 1,380 ms | 33 | 46 s |
| PCMD | 92 | 5,886 ms | 6,018 ms | 216 | **1,300 s** |

Sustained **2.07 patches/s**. Factory sweep ≈ **54 minutes**, user slots ≈ 8. **PCM drum kits are 3.6% of the
presets and 40% of the clock**, and 137 MB of the ~320 MB a full factory sweep writes, because a kit reads all
88 partial blocks whether or not they hold anything.

**Some presets do not answer at all.** In the spike, every GM2 row (265) and every ExPCM row (531) — 796 of
6,023, 13.2% — stored the bank and program in the Studio Set Part and then answered no read from any engine's
temporary area. Identical via sysex and via MIDI program change, so it is the instrument rather than the write
route. **Whether the `HQ GM2 + HQ Pcm` loadout unlocks them is an open question** (see below); the design does
not depend on the answer.

**Writing the preset:**

- The three parameters are `Studio Set Part/Tone Bank Select MSB`, `.../Tone Bank Select LSB` and the tone
  program number. **The program parameter is 0-based; `Presets.csv` is 1-based.** Write `Pc - 1`, confirmed by
  read-back.
- **Write them through the domain, not through `PartViewModel.ChangePresetAsync`**, which posts
  `UpdateResyncPart` to the message bus and would resync the whole part once per patch. The domain writes
  produce no bus traffic at all.
- They are three separate DT1 messages. **Hold one lease across all three**, or an abort leaves the part on a
  mixed bank.
- Take the engine from the preset row and hand it to `ToneDomainNames.For`. Getting it wrong costs 1.5 s per
  block and then fails.

**SRX boards:**

- `GetLoadedSrxAsync` converging on the expected set **is** a completion signal; no fixed wait is needed.
- Two traps: it can return `(0,0,0,0)` mid-load, so poll for the expected values rather than for any reply;
  and **the device rewrites what you send** — `SendLoadSrxAsync(19,0,0,0)` read back as `(19,20,21,22)`.
  Compare against convergence, not against the request.
- Timings: a normal board load ≈ 5 s; unloading one ≈ 2.5 s; restoring three ≈ 14.6 s; the `HQ Pcm` load
  > 33 s.

---

## What the user chose

Asked before the design was drawn:

- **Scope: tick engines and banks per run.** Not an all-or-nothing sweep — a selection screen. "A two-hour job
  you can aim is a job you'll actually run twice", and it is the only answer that lets someone re-sweep one
  board without re-reading 4,301 PCM tones.
- **Safety: capture and restore automatically.** The Studio Set and the four SRX slots are captured before
  anything is written and put back at the end, on cancel and on failure.
- **Layout: one folder, tagged.** No new browsing code; the existing engine, bank and tag filters do the
  narrowing. The cost is accepted below.
- **Interruption: skip what is already there.** Each snapshot is written as it is captured, and a re-run skips
  any patch whose file already exists.

---

## The shape

Four pieces, and the split is the usual one for this codebase: everything that can be got wrong quietly is a
service that a test can reach, and the view model holds only sequencing.

**`SeedSelection`** — what to sweep. Engines, banks, and whether internal presets, user slots or both. A
record, so a selection can be handed to the planner and to a test unchanged.

**`SeedPlan`** *(pure)* — given the preset list, a selection, the file names already in the library folder and
the currently loaded SRX slots, it answers:

- the ordered work list, **grouped into board rounds** so the boards are loaded as few times as possible;
- a skip reason for everything excluded — `already in the library`, `an empty user slot`, `not selected`;
- an estimate, from the measured per-engine costs above plus the board-load time for each round.

This is where the arithmetic lives and it is tested exhaustively. It opens no file and touches no device.

**`SeedRun`** — the loop, behind an `ISeedInstrument` interface (select a patch, capture it, load boards, read
the loaded boards) so the whole of it can be tested against a fake. It owns:

- capture-and-restore of the Studio Set and the SRX slots, in a `finally`;
- per-patch outcome recording;
- cancellation between patches;
- writing each snapshot the moment it is captured.

**`SeedRunViewModel` + view** — the selection screen, a progress line, cancel. Nothing testable.

---

## How a patch is captured

For each patch in the plan, holding one lease:

1. Write `Tone Bank Select MSB`, `Tone Bank Select LSB` and the program number (`Pc - 1`).
2. `CaptureToneAsync(domain, part, preset.ToneTypeStr, name, lease)` immediately. No delay and no poll — the
   device withholds the reply until the tone is in place.
3. Write the snapshot with `SnapshotLibrary.Create`.

**The name in the file is the device's**, not the catalogue's. The temporary tone reports what it actually
holds, and where the two disagree the device is right — `Ring E.Piano` is the sound you get. The catalogue
name is kept as a note when they differ, so the disagreement is visible rather than silently resolved either
way.

**Metadata**: category from the preset row; tags are the bank (`PRST`, `GM2`, `SRX02`, `ExSN1`, `USER`) plus
`factory` or `user`. The tag is what makes "only my own patches" a filter afterwards.

---

## Availability is discovered, never assumed

A patch whose **first block does not answer** is recorded as `unavailable` and the sweep moves to the next
one. That single rule covers every reason a patch cannot be captured — an unloaded SRX or ExSN board, an
engine the part cannot hold, and whatever is true of GM2 and ExPCM — and it is why the open question below
does not block the design. If `HQ GM2` turns out to unlock those 796, they are captured; if not, they are
reported as unavailable with everything else.

The cost of a wrong guess is one 1.5 s reply deadline per patch. For the 796 that is ~20 minutes on a
55-minute sweep, which is the argument for offering the known-unavailable banks **unticked by default with
the reason shown**, rather than for hiding them: the user can tick them and find out for themselves on a unit
that may differ from the one measured.

---

## Failure, and what stops the sweep

**Per patch, recorded and carried past:** unavailable, or a capture that threw. Both go into the outcome list
with the patch named. A sweep of 6,000 patches must not be lost to one of them.

**Fail before starting:** no device; the library folder unwritable; the Studio Set could not be captured (if
it cannot be captured it cannot be restored, and the sweep is about to overwrite it); **Compare is holding
edits** — while comparing, the journal's buffer is the only copy of them.

**On cancel or failure:** the Studio Set and the SRX slots are restored, and the restore is verified by
reading back rather than assumed.

---

## What it costs the library

A full factory sweep writes ~6,000 files and ~320 MB into one folder, 137 MB of it PCM drum kits. Consequences,
stated rather than discovered later:

- **The duplicate scan slows down.** It buckets by engine, so 4,301 PCM tones become ~9M pairwise comparisons.
  It early-outs as soon as a pair passes the threshold, so unlike sounds are cheap, but this is tens of seconds
  rather than the 268 ms measured over 500 files.
- **Deep search reads every candidate file**, so it scales with the folder.
- **The listing itself is fine** — it reads heads only.

The mitigations are the filters that already exist: engine, bank tag, and `factory` versus `user`. If this
turns out to be too slow in practice, the answer is a second library folder, which needs no new code.

---

## Testing

- `SeedPlan`: ordering, board rounds, every skip reason, the estimate, an empty selection, a selection whose
  every patch is already on disk. Pure, so all of it is reachable.
- `SeedRun` against a fake `ISeedInstrument`: a patch that does not answer is recorded and the sweep
  continues; a capture that throws does the same; cancellation stops between patches and still restores;
  restore happens when the run throws; the Studio Set is captured before the first write.
- The view model gets none, which is why so little is in it.

---

## Open questions

1. **Do GM2 and ExPCM presets become capturable under the `HQ GM2 + HQ Pcm` loadout?** The user points out it
   must be loaded in slot 1 and then occupies all four slots. The spike's own read-back —
   `SendLoadSrxAsync(19,0,0,0)` → `(19,20,21,22)` — suggests that is what happened, but its control for the
   test was SRX06 being evicted, and eviction plausibly registers as soon as the load starts, while that load
   takes over 33 seconds. So the probes may have landed in the window where the old boards were gone and the
   new one was not ready. **A re-test is running.** Either answer is absorbed by the availability rule; what
   changes is whether the sweep should rotate through that loadout, and what the selection screen says beside
   those two banks.
2. **Should PCM drum kits be ticked by default?** 22 minutes and 137 MB for 216 patches. The measured cost
   should be shown beside the tick either way.

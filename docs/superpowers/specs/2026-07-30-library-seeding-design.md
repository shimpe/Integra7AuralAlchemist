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

**796 of the 6,023 factory rows cannot be captured on this unit** — every GM2 row (265) and every ExPCM row
(531), 13.2%. The Studio Set Part accepts and stores exactly what is written (verified by read-back, e.g.
msb 121 / lsb 1 / pc 6 reads back as pc 5, 0-based as expected), and then **all five engines' temporary tone
areas stay silent** — not merely the one the preset's row names. The part is left holding a bank and program
selection that exposes no temporary tone over sysex at all. Identical via sysex and via MIDI program change,
so it is the instrument rather than the write route.

**The `HQ GM2 + HQ Pcm` loadout does not unlock them.** The user asked specifically, since that board must be
loaded in slot 1 and then occupies all four slots — and the device confirms their model of it, rewriting
`SendLoadSrxAsync(19, 0, 0, 0)` to `(19, 20, 21, 22)`. Tested with 20 presets spanning both banks and both
engines that have rows in them, at three time points across 180 s past a properly detected convergence: 0
captured. **The positive controls are what make that conclusive** — in the same loadout, with `(19,20,21,22)`
stable, a PRST PCM Synth tone captured in 305 ms and a PRST PCM drum kit in 5,933 ms, both normal times. The
device was fully responsive; these two banks simply are not exposed.

> The first attempt at this test was unsound and its method was rejected: it polled for the values it *sent*
> rather than the values the device settles on, so the loop never matched and became an accidental fixed wait,
> and its control was SRX06 being evicted — which cannot distinguish "not available" from "not ready yet". The
> conclusion survived the re-test; the reasoning behind it did not. It is the worked example of the trap
> recorded above, walked into by the person who had just written it down.

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

**SRX boards.** Re-measured on the evening of 2026-07-30 with a probe that times every read, after a
cancelled sweep left the user's three boards evicted. What is written here replaces the earlier account,
which said to poll until the reading stopped changing — a rule that a device which has not yet acted on the
request satisfies immediately.

- **Nothing may be sent to the slots while the instrument is loading them.** A `SendLoadSrxAsync` that
  arrives during a load is discarded in silence — no acknowledgement, no error, no effect. This is the
  constraint everything else here serves.
- **The instrument answering at all is the completion signal.** Idle, it answers the slot query in **2–5 ms**,
  whatever the slots hold, *including all four Off*. Loading, it answers **nothing**: the read runs out its
  full 1.5 s reply deadline, and `GetLoadedSrxAsync` renders that as `(0,0,0,0)` because there is nothing
  else it can say. So all-zeros is two states wearing one face, and they are told apart by how long the read
  took — 2 ms against 1,510, three hundred times apart.
- **Three traps, not two.** It can read `(0,0,0,0)` mid-load; **the device rewrites what you send** —
  `SendLoadSrxAsync(19,0,0,0)` reads back as `(19,20,21,22)` — so you cannot poll for the values you asked
  for; and *the reading holding still proves nothing on its own*, because an instrument that has not yet
  acted on a request holds just as still as one that has finished. What is waited for is **three agreeing
  readings the instrument actually answered** (`SeedSettling`).
- **A loadout the instrument already holds does nothing at all.** Sending the user's own `(2,13,6,0)` back to
  them produced not one busy poll in 40 s. So a rule that waited for the reading to *change* would hang on
  the case where the right answer is "already done".
- Timings, as quiet time between the request and the first answer: one board replacing three ≈ **6–7.5 s**;
  emptying all four ≈ under 1.5 s (it answered on the first poll); three boards from empty ≈ **13 s**;
  `HQ Pcm` ≈ **19 s**, and it then answers `(19,20,21,22)` immediately and stays there. `HQ Pcm` is therefore
  the same shape as a plain board, only slower — it goes quiet, then comes back — which is worth stating
  because it is the loadout any re-test of the ExPCM question has to wait on.
- **What the slot query cannot tell you** is whether a board's samples are usable at the instant the slots
  become readable again; it reports the slot table, and that is all it reports. Anything that reads a board's
  tones straight after a load is relying on those two moments coinciding, which nothing here establishes.

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
engine the part cannot hold, and whatever is true of GM2 and ExPCM. Nothing in the design encodes *which*
patches are unavailable, which is what kept the HQ GM2 question from ever being a blocker: the answer changed
the selection screen's defaults and nothing else.

The cost of trying one anyway was **timed at 3.00 s** on 2026-07-30 — nine consecutive log lines 3.000 to
3.003 s apart. It is two waits, not one: the 1.5 s reply deadline for the tone that never arrives, and 1.5 s
more to ask the instrument whether it holds anything at all, which is what keeps "your instrument does not
expose these" from being reported as "these failed". For the 796 that is **~40 minutes** — GM2 ~13, ExPCM
~27 — which is the argument for offering the two **unticked by default with the reason shown** rather than
for hiding them. Hiding them would be the wrong call twice over: another unit may differ, and a user who
wants to check should be able to, cheaply, without being told what their instrument can do by a table written
on somebody else's.

**The estimate does not carry that 40 minutes**, and deliberately. `SeedPlan` charges an unavailable row its
engine's capture rate, so a sweep with both banks ticked runs about 32 minutes longer than it predicts.
Teaching the planner which banks are unavailable is the one fix that is not allowed here — availability is
discovered, never assumed — so the number is shown per bank on the selection screen, beside the tick, worded
as what it cost on the unit it was measured on rather than as a property of the bank.

---

## Failure, and what stops the sweep

**Per patch, recorded and carried past:** unavailable, or a capture that threw. Both go into the outcome list
with the patch named. A sweep of 6,000 patches must not be lost to one of them.

**Fail before starting:** no device; the library folder unwritable; the Studio Set could not be captured (if
it cannot be captured it cannot be restored, and the sweep is about to overwrite it); **Compare is holding
edits** — while comparing, the journal's buffer is the only copy of them.

**On cancel or failure:** the Studio Set and the SRX slots are restored, and the restore is verified by
reading back rather than assumed.

**A cancel does not interrupt a board load.** One that has been sent is seen through to settling before
anything else is sent, because an instrument that is loading discards what arrives — so a cancel pressed
during a board round takes about half a minute to take effect (measured at 29 s). The panel says so when
Cancel is pressed while the slots are moving; without that it reads as a hang, and the user's next move is
to close the window on an instrument that has not been put back.

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

## Selection screen defaults

Settled by the measurements above, and every one of them shows its cost beside the tick so the user is
choosing with the number in view:

| banks | default | why |
| --- | --- | --- |
| PRST, SRX, ExSN, user slots | ticked | the sweep's purpose |
| GM2, ExPCM | **unticked** | those tones cannot be edited, so the instrument exposes no temporary tone to capture — see below; 3.00 s a row to find that out again, ~13 minutes for GM2 and ~27 for ExPCM, neither of which the estimate allows for |
| PCM drum kits | **unticked** | 22 minutes and 137 MB for 216 patches — 40% of the clock for 3.6% of the presets |

PCM drum kits are unticked rather than absent for the same reason as GM2: it is a defensible thing to want,
and the cost is the user's to weigh, not this document's to decide for them.

## Why GM2 and ExPCM expose nothing

**Because those tones cannot be edited at all.** Confirmed on 2026-07-30 by the instrument's owner, who
loaded `HQ GM2 + HQ Pcm` and tried to edit an ExPCM tone from the front panel. It is a property of the
instrument rather than of this unit.

Two independent channels agree, and neither has a load in flight — which is what every earlier attempt
lacked. The front panel refuses the edit. And over sysex, with that same board already settled and the slots
answering in 2 ms, all five ExPCM and GM2 rows tried returned nothing while a PRST control captured in
268 ms. No part of that reading depends on a load having finished, because there was no load.

A capture reads the part's **temporary tone** area. A tone type that can never be edited has no editable
temporary area for the device to populate, so there is nothing there to read — which is exactly the observed
behaviour, and the part of it that never had an explanation: the Studio Set Part accepts and stores the bank
and the program (verified by read-back), and then all five engines' temporary areas stay silent. Not the one
the row names — *all five*. That was always the shape of a tone that does not exist to be edited, rather
than of one that failed to load.

**This is worth more than the observation it replaces**, because it says why. The measured version of this
answer could only ever be "not on the unit it was measured on", which is why the selection screen offers the
banks unticked rather than hiding them. The mechanism does not carry that caveat.

### How this was nearly got wrong three times

Kept because the failures are instructive and the next person to doubt this deserves them:

1. The first test **polled for the values it sent** rather than the values the device settles on, so its
   loop never matched and it became an accidental fixed wait.
2. The re-test waited on **"poll until the reading stops changing"**, which fires early — immediately after
   a send the device still reports the previous loadout — and rendered a silent read as `(0,0,0,0)`, so a
   *repeated* silence is itself a reading that does not change. Both are fixed; see `SeedSettling`. They
   were in force when this was decided.
3. What made the re-test look conclusive was a positive control: a PRST tone capturing normally in the same
   loadout. **That control could not see what it was asked to.** PRST tones are internal ROM, and an
   instrument still loading a board goes on answering for its own sounds — so it proved the device was
   responsive, never that the board had finished. ExPCM lives on the board that was loading.

The third fault is the one that reopened this, after the owner watched the front panel during a sweep and
saw requests going out while it still showed the slots loading. **The conclusion was right and the reasoning
could not support it**, which is not the same thing and was worth the day it took to separate. An invalid
control is not a wrong answer; it is an answer nobody had grounds for yet.

That observation was not wasted either: chasing it found that the instrument **silently discards a slot load
sent while it is still loading**, which had been leaving a user's boards evicted whenever a sweep was
cancelled mid-load. See `SeedSettling` and `SeedInstrument.LoadBoardsAsync`.

## Responding normally is the readiness signal

**A board's samples are usable the moment the instrument starts responding normally** — confirmed by the
owner on 2026-07-30. So there is no wait to add after the slots settle, and a board round may begin
capturing as soon as they do, which is what it does.

This is one fact about the instrument rather than three, and it is worth stating that way because the whole
design already rests on it in two other places without having said so. **The INTEGRA-7 withholds replies
while it is working.** That is why `CaptureToneAsync` is its own settle check — during a tone load the
device withholds the read rather than answering with the outgoing tone — and it is why an idle instrument
answers the slot query in 2 to 5 ms while a loading one answers nothing at all and the read runs out its
1.5-second deadline. Silence means busy, everywhere; a normal answer means ready, everywhere.

`SeedSettling` is therefore a readiness signal and not merely a slot-table one: three consecutive readings
the device actually answered cannot happen while it is still loading. Nothing else is needed, and a
readiness poll layered on top of it would be polling for a second time for the thing it already establishes.

**Measured, not only reasoned.** Four loads whose quiet times span 9× — all four slots Off (&lt;1.5 s), ExSN5
alone (~6 s), SRX11 replacing three (~7.5 s), three boards from empty (~13.6 s) — and in every one, the
first capture attempted after the first answered slot read succeeded. There is no lag at any load duration,
so there is no shape to extrapolate to `HQ Pcm`. The sharpest reading caught the transition in the act: on
the ExSN5 load the part's temporary tone area answered a read at 21:47:34.889, **eighteen milliseconds
before** the slot query answered at 21:47:34.907. The samples were usable no later than the slot table
became readable, and if anything marginally earlier.

That also means `SeedRun` has margin it did not know it had: it begins capturing when `SeedSettling`
returns, which is three agreeing answers at a 1.5 s cadence and therefore about 3 s *past* the first moment
a capture would have worked. In all three measurable loads the first successful capture came before
`SeedSettling` declared settled.

**One hazard this uncovered, and it is in the code as a comment where it can do some good.** For the first
few tens of milliseconds after a loadout is sent, the instrument answers the slot query with the loadout it
is *leaving* — five consecutive loads, every one, between 3 and 29 ms, including answering `(19,20,21,22)`
when it had just been asked for `(11,0,0,0)`. `SeedInstrument.SettleAsync` is safe only because it waits a
poll interval *before* its first read; that ordering is load-bearing. A scratch harness written to take
these very measurements read first, believed the stale answer, sent a Studio Set restore into a still-loading
instrument, and left a part holding a tone its owner had not chosen.

## Open questions

None.

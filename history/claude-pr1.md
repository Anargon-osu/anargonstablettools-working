# PR1 - Claude Opus 5.5 (Anthropic), 2026-11-22

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 5.5 (`claude-opus-5-5`), Anthropic. Same conversation as R42-R45. Requests are Anargon's.
PR1 is the V1.0 source (`8d7fc53`, "V1 release!") with one new position fix, Coil Gains, on the branch
`coilswitch-hotfix`. Once merged, the project is **R46**, released as **V1.1** (no release candidates). Tablet runs
were authorised by Anargon for this work; every image flashed passed the twin gate and went through xppen-tools with
its typed confirmation (`experiments/noise45/flash.sh`). After the tests the tablet was set back to the V1.0 ULTRA
build (CRC 0xB34B).

Naming (Anargon, 2026-11-22): after R45 each delivery is a pull request, named after its number (this one PR1), with
one history file per pull request. Each merge moves the project one revision on: PR1 merged is R46, the next merge
R47, and so on. Planned releases: V1.1 = R46; V1.2 = R47 to R56 (low-level changes and improvements across the
project, its end depending on their scope).

## Request

- After V1.0, slanted lines still kept 0.04-0.07 mm of error (up to 0.45 mm peak to peak) that repeats coil by coil
  ("Part 2" of the slanted-line issue). Anargon: "That goes into misc options."
- Find the right strength, and "ideally, we go down to the decimal to tune it down even lower".
- Fit it into the true source (Anargon-workarea), document it, and push it to a new branch `coilswitch-hotfix` of the
  private repository, so Anargon can open the pull request.

## What was found

### Each coil reads the pen with its own strength

The tablet works out the pen position inside a window of five coils from five "line sums" (one number per coil: how
strongly that coil picked up the pen). The stock routine that does this (`0x08005432`, `MultiCoil.Stock` in C#)
assumes that every coil reads the pen equally strongly. On Anargon's tablet they do not:

- Fitted on the R43 strokes' raw line sums (`referencedata/strokes-r43`, pen touching; per frame the pen offset and
  signal, per coil a gain, one shared response shape), the X coils differ by **1.2 % sd** and the Y coils by
  **0.7 %**.
- On X a strong / weak pair comes back every 8 coils (10 / 11, 18 / 19, 26 / 27).
- A coil that reads 1 % high pulls the position in the windows next to it by about 0.03 mm. Along a slanted line the
  pen crosses coil after coil, so the line bends coil by coil.
- The gains are the same along each coil (three zones, r 0.90-0.96) and between recordings (X r 0.94 between the two
  ULTRA recordings). Gains learned only from the ruler lines of 2026-11-13, 11-16 and 11-17 match the strokes' (X r
  0.92, Y r 0.84).
- Per-coil centres and effective ranges were fitted too and predicted nothing. The part shared by every coil changes
  by only 0.004 mm, so Coil Linearization's table stays as it is.

### How much of it shows changes between sessions

On the tablet the firmware took off exactly the predicted error (with minus without, on one line: r -0.87 against the
prediction). But the size of the error the tablet showed changed by session: 0.7-2.4 x the prediction on 2026-11-13,
11-16 and 11-17, and almost none to 0.6 x on 11-22. At full strength that day the lines came out over-corrected.

Checked and ruled out as the cause (offline, on the recordings):

- pen pressure (gains per pressure band),
- a filter lag between the two pass directions,
- pen tilt (it is clamped in every recording),
- where along the coil the pen is.

The cause is still open (see "Open" below), so the correction is applied at a strength below 100 %.

### Strength: 58.9 %

- The table holds `4096 / gain'` per coil, with `gain' = 1 + 0.589 x (gain - 1)`: 58.9 % of the measured
  difference.
- Chosen in 0.1 % steps over every recorded ruler line (four sessions, 13 lines) as the strength that keeps the worst
  session lowest: each session ends at 0.78-0.91 of its error without Coil Gains, and none gets worse.
- The strength with the lowest mean (78.6 %) was rejected: with 2026-11-22 held out of the fit it would have made
  that session worse (1.076 x).
- Separate X / Y strengths (84 % / 65 %) would gain only 0.002; the Q12 rounding of the table costs about 0.001 mm.
- The tablet test ran at 60 %. 58.9 % changes 23 table entries by 1 LSB; the code is the same.

## What was built

- **`atf_coilgain.cs`** (new): the measured gains (`GX`, 32 X coils; `GY`, 19 Y coils; 4096 = not measured, the
  edge coils), `STRENGTH = 589`, the Q12 table, the C# copy of the firmware step (`Scale`) for the twin, the Thumb
  emitter (`EmitScale`) and `Installed(flash)` for the twin's check.
- **Where it runs:** inside Window Scale's wrapper (`ws_x` / `ws_y`), on the five line sums just before the stock
  routine `0x08005432`: `line = ((line - floor) x G_k) >> 8`, with Multi-coil Read's floor. The stock arithmetic only
  uses differences of lines and their ratios, so taking the floor out and scaling by 16 changes nothing but the gain
  (and keeps 4 more bits of each line). About 30 instructions per axis; nothing is kept between frames, so no delay.
- **When:** fast stage only (`VB[0] = 0`, where the strokes were measured), a centre coil 0..N and at least one read.
  ULTRA only (it needs Window Scale); not with Multi-coil Read; not in the Filter Arranger (lab) build.
- **Setting:** "Coil Gains" (key `cgain`, tag `CG`), one of the Misc. Optimizations, default on. Not offered (with
  the reason shown) without ULTRA's pacing, at another report period, with Multi-coil Read, or without Window Scale.
  CLI: `--coil-gains` / `--no-coil-gains`; the build summary and option word (`COIL-GAINS`) name it.
- **`atf_sched.cs`:** allocates the table and passes it to `WindowScale.Emit`. **`atf_winscale.cs`:** the new `Emit`
  overload with the table. **`atf_multicoil.cs`:** the twin board scales the lines the same way when the table is
  installed, so the position-call check covers it. The table's address is found with the existing signature for
  `0x080083BE` (no new firmware addresses).

## Measurements (before -> after)

### Tablet tests

V1.0 ULTRA + ALPI against the same build with Coil Gains, pen touching, both recordings of a pair on one ruler
position. "Fixed error" is the part that repeats in both pass directions; "real error" has the measurement floor
taken out.

| Strength | Line | Fixed error | Real error | Total | r against the prediction |
|---|---|---|---|---|---|
| 100 % | 44-46 deg, 31 mm | 0.031 -> 0.044 mm | 0.057 -> 0.050 mm | 0.059 -> 0.052 mm | 0.52 -> -0.66 (over-corrected) |
| 60 % | 30-31 deg, 70 mm | **0.021 -> 0.009 mm** | **0.037 -> 0.017 mm** | 0.046 -> 0.042 mm | 0.48 -> -0.23 |

At 60 % the firmware applied 0.65 x the prediction.

### Offline, every recorded ruler line, at 58.9 %

Both-way mean sd along the line, floor included.

| Session | Lines | Before | After | Change |
|---|---|---|---|---|
| 2026-11-13 | 1 | 0.091 mm | 0.082 mm | -10 % |
| 2026-11-16 | 3 | 0.110 mm | 0.094 mm | -15 % |
| 2026-11-17 | 3 | 0.055 mm | 0.044 mm | -21 % |
| 2026-11-22 | 6 | 0.051 mm | 0.047 mm | -9 % |

Per line: 6-25 % off every line but one (2026-11-22, 34 deg: +9 %).

## Checks

- Digital twin, all five presets (ULTRA with Coil Gains, RAPID, FAST, QUICK, stock): pass. The position-call check
  compares the stock routine's arguments with the C# copy on every call.
- With `--no-coil-gains` the ULTRA image is the V1.0 image byte for byte, build id aside; RAPID, FAST, QUICK and stock
  do not change.
- Regression suite: 528 checks pass with the February dump and 528 with the December dump.
- Two A/B ruler tests on the tablet (100 % and 60 %), above.

## Open

- Why the size of the coil error changes between sessions (0.0-2.4 x the prediction).
- A hover measurement: the gains were fitted with the pen touching.
- One tablet and pen (Anargon's). Other tablets need their own gains; until then the table is this tablet's.

## Files

Changed: `atf_build.cs`, `atf_cli.cs`, `atf_multicoil.cs`, `atf_sched.cs`, `atf_winscale.cs`; new:
`atf_coilgain.cs`. Docs: `README.md`, `CLAUDE.md`, `AGENTS.md`, `DOCS/CHANGELOG.md`, `DOCS/DESIGN.md`,
`DOCS/FIRMWARE_DESIGN.md`, `DOCS/verification/SOURCE-CONTENTS.sha256`, this file.

Outside the repository (Claude-workarea): analysis scripts in `experiments/precision` (`coilgain*.py`, `coilk.py`,
`coilpk.py`, `coildir.py`, `coilzone.py`, `coilruler.py`, `coilstrength.py`, `coiltune.py`, `coilba.py`), the
before / after write-up in `experiments/precision/ISSUE-slanted-lines-v2.md`, and the tablet recordings in
`rev-output/hw45/coilgain` (`ab` = 100 % test, `ab60` = 60 % test).

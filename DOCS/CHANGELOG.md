# Changelog

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

## PR1: Coil Gains (branch `coilswitch-hotfix`; R46 / V1.1 once merged)

- **Coil Gains, a new part of Misc. Optimizations** (setting `cgain`, tag `CG`, default on; ULTRA, needs Window
  Scale, not with Multi-coil Read). Each coil of Anargon's tablet reads the pen a little stronger or weaker than the
  next (X 1.2 %, Y 0.7 %, fitted on the R43 strokes), which bent slanted lines coil by coil (0.04-0.07 mm). Before
  the tablet's own position arithmetic runs, each of the five line readings has the floor taken out and is scaled by
  its coil's correction, at 58.9 % of the measured difference. No delay, nothing kept between frames.
- **Measured:** on the tablet at 60 %, the slanted-line error that repeats on every pass fell from 0.021 to 0.009 mm
  (30 deg line, 70 mm, against V1.0 on the same ruler position); offline at 58.9 %, 6-25 % came off every recorded
  ruler line but one (2026-11-13 to 11-22). At 100 % it over-corrected on 2026-11-22: how much of the coil
  difference shows in the cursor changed between sessions, so 58.9 % is the strength that lowers the worst session
  most without making any worse.
- `--no-coil-gains` turns it off; with it off the ULTRA build is the V1.0 build byte for byte (build id aside), and
  the other presets do not change.
- Checks: digital twin (all five presets, the position-call check compares the stock routine's arguments with the C#
  copy), 528 regression checks with the February and December dumps, two A/B ruler tests on the tablet (100 % and
  60 %).

## R45-rc6

- **The Bootloader Counter toggle finds the counter on the tablet** (Anargon: scan for it, on if found, off if not,
  so a user can return the bootloader to stock without `--return-to-stock`). The tool checks the backup it builds
  from: the hook in `set_boot_state` (0x08000F40) points at the routine page 0x08002800, and that page holds code.
  When found, Step 6 starts with Bootloader Changes and Bootloader Counter on, and the toggle says "Found: installed
  on this tablet". Turned off, the build removes it on its first start (`-noBC` in the file name): page 0x08000C00
  back to stock while the repair stub still guards it, then the reset boot-jump in page 0x08000000, checked word by
  word, then the routine page erased. The count page 0x08002C00 stays and the firmware's own counter goes on
  counting; the update that brought the removal build is not counted twice. With Bootloader Changes off the counter
  stays as it is. On the command line: `--no-bootloader-counter` (a full backup of that tablet as input); a build
  from such a backup without it prints a note and leaves the counter alone. Measurement, training and lab builds
  never remove it. The DO NOT UNPLUG page follows a removal flash: the page-0 rewrite has the same short window as
  the install.
- **A backup with the counter installed works as input.** Its two counter words are set back to stock in the copy
  the tool keeps, so a counter build, the removal and `--return-to-stock` no longer need the first stock backup.
- **Erased bootloader pages are not erased again** on every start (return-to-stock and the removal).
- **The GitHub workflows need no repository variables.** The stock dumps come from `Anargon-osu/deco640-resources`
  and `gd32emu.cs` from `Anargon-osu/GD32EMU`, checked against the SHA-256 pinned in `tests/prepare-emulator.py`
  (LF or CRLF line ends). A private repository still needs the `ADT_TEST_DUMP_TOKEN` secret. A tag with `-rc` in it
  becomes a pre-release.
- **The test dumps live in `dumps/<firmware version>/ic902-main.bin`** (Anargon: one folder per version, so a new
  firmware is tested by adding its folder). The folder name must be the version the firmware names itself (its USB
  version string, such as `2025-02-14_release001`); a mismatch, a missing or short file, a Git LFS pointer or a dump
  without a version fails with the path named. February and December are still recognised by their firmware CRC and
  get every check; any other version gets the digital-twin checks: compatibility and all five presets. Recovery
  repairs only versions it has fingerprints for, so for any other version it must refuse, which shows as a skip. A
  repository without `dumps/` is still searched by CRC.
- **The host regressions pass on GitHub's runners.** The first GitHub run failed in `host-regressions` on all three
  systems. On Linux and macOS the patcher-packages group expected a package to compile and run, but packages compile
  only with the C# compiler that ships with Windows; there the group now checks that an approved package is refused
  with the reason and nothing runs. On Windows the closed-stdin check of the fake tool now accepts a read that throws
  instead of returning empty, and a failure prints the exit code and output.
- **Docs: the update-mode pad has been used on the tablet** (Anargon): PA15 to ground while plugging in is a tried
  route; only the ROM loader route (BOOT0 and UART) has not been tried yet.
- Checks: digital twin, install, update, removal start, second start, boot from reset, next build: the bootloader up
  to 0x08002800 equals stock, page 0x08002800 is erased, the count goes 2, 2, 3. Regressions: 528 with February and
  with December. Not yet on the tablet.

## R45-rc5

**rc5 (4):** the defaults from Anargon's tablet sessions, and the tools used for them.

- **Misc. Optimizations (new setting `misc`, default on; `--no-misc-optimizations`).** One switch for the four
  position fixes: Switch Match, Window Scale (ULTRA), Coil Linearization and Dynamic Anti-Chatter. The settings
  screen shows only this one; the four flags (`--no-switch-match` and the others) still turn one off on the command
  line. Order in the firmware: Switch Match > Coil Linearization > ALPI > Dynamic Anti-Chatter. File names say
  `noMO` when it is off. The order was chosen in a 20-minute play session with the Filter Arranger switching orders
  at pen lifts, and checked along a ruler (`experiments/precision/RESULTS.md`): Dynamic Anti-Chatter cut flicks to
  0.38x; Hull Mark + Hull Clamp (4.9x flicks) and Jump Rejection (3.0x) are left out.
- **Coil Linearization uses table B**, learned on the three rc5 ruler lines. On a ruler line it was not learned on it
  had the lowest error of the three tables (0.088 / 0.059 mm against 0.107 / 0.073 for the first table), and fewer
  flicks in play (0.40x against 0.81x). The first table is the Filter Arranger's CL-A.
- **Measurement Tools entry 4, Ruler Line Test.** A ruler along a target line (`l N`: either diagonal, across or up
  the middle), recorded through the normal pen reports, scored for the error that follows the coils, forward against
  back, and steps. A recording on another line starts a new session by itself (3 degrees or 3 mm apart). A line
  along an axis needs 0.7 of its length per pass (63 mm up the middle) and shows only the error across it.
- **The pen reader opens afresh for every recording** (Ruler Line Test, Still Pen Noise Test). A report's time counts
  microseconds since the reader opened, in a 32-bit number, so it ran out 35.8 minutes after the open and every
  later recording found no passes. Saved files with such times are read with each time guessed from the report
  before (+1 ms).
- **Filter Arranger: `c` turns the cursor off** (`o c`, or with an order: `o 1 3 6 10 c`). The lab build marks the
  pen reports so the system moves no cursor; the measurement entries still read them.
- **Modified firmware is found by checksum**, with `xppen-tools crc` over three sections (read-only, in update mode).
  For such firmware the 2.5-minute dump can be skipped (`s`) where a backup of the same firmware exists.
- **Patcher packages:** a ZIP of the package folder (one folder around everything) and ZIPs made on a Mac
  (`__MACOSX`, `.DS_Store`) now load. A `.zip` file is not read; the tool says to rename it to `.patcherpkg` (the
  way an osu! `.osz` is a ZIP).

**rc5 (3):** Anargon's Measurement Tools, tool 3 of the first screen ("For testing purposes only"): tests and
measurements on a real tablet, for development. Every tablet measurement now goes in here as a C# entry with a
digital-twin gate, instead of a private script (AGENTS.md rule 18). Three entries:

- **Anargon's Filter Arranger** (moved here from the Tablet Firmware Mod; Anargon: "a test suite type class, so it
  should not be part of the primary tablet patcher"). With a `--lab` build (`-LAB` in the file name: ULTRA + ALPI,
  or FAST) it sets which position filters run and in which order, in the tablet's RAM over USB strings 181-199, no
  flashing. A new order starts at the next pen loss. The screen is laid out like the settings screens; a local model
  (OmniCoder 9B) wrote it from Claude's outline, and it is kept unchanged.
- **Anargon's Still Pen Noise Test (new).** Standard calibration's still hold as a test of its own, and the testing
  step before the lowest-noise defaults are updated. It reads the normal pen reports (what the cursor follows) for
  1 to 60 s with the pen held still, and shows the shake (sd), the widest swing, the typical frame-to-frame step and
  the steps over 0.05 and 0.10 mm, with every spike kept in. Nothing is flashed. On the Filter Arranger firmware each
  hold is labelled with the filter order in use, and `o 1 2 6 10` sets the next one, so orders can be compared hold
  by hold. Every hold is saved under `export/measurements/still-noise` (a .csv of its reports, one line in
  `still-noise.txt`). It ports the scoring of `experiments/noise45/noisept.py`.
- **Tablet signal thresholds (new entry).** Standard calibration's measuring on its own: the measurement firmware,
  the backup before flashing, the three pen positions, and `calibration-PRESET.txt` for `--calibration`. It runs the
  same code as the Tablet Firmware Mod's step 4, which is unchanged; afterwards it offers to flash back the firmware
  the tablet ran before, from that backup.

Also since rc5 (2):

- **First screen:** 1 Tablet Firmware Mod, 2 Recovery, 3 Measurement Tools. A warning that these tools are only
  allowed for the listed tablets (for now only the XPPen Deco 640), and that only the Tablet Firmware Mod has been
  thoroughly tested by a human. The startup backup runs only for tool 1.
- **Step 5 is grouped by where each setting acts:** Reading the coils (no cursor delay), Position corrections (no
  cursor delay), Position filters (noise against delay), Noise-related tuning, then ALPI and Experimental hardware.
  The settings' texts were rewritten.
- **Checks:** a twin gate for the two new entries (34 checks: the scoring against hand-made reports with
  spikes, gaps, lost and calibration readings; the screen in the twin on ULTRA, the ULTRA + ALPI lab build and the
  FAST lab build, its numbers recounted from the saved .csv; the thresholds entry end to end with a fake
  xppen-tools, the measured firmware being the flashed one and the firmware put back byte for byte; and the wizard's
  calibration texts unchanged). The Filter Arranger's gate: 17 twin checks. Suites: core 216, host 112, calibration 200 (528), on the same stock fixture as rc5 (2).

**rc5 (2):** Coil Linearization runs before ALPI instead of after it. ALPI follows a straight move with no delay
but the small coil wave about 5-6 ms late, so a correction made after it landed a little behind a moving pen:
along the ruler, forward and back passes differed by up to about 0.3 mm (5 osu!px), more the faster the pen.
Offline replay of ALPI's C# copy (`referencedata/tools/lagsim.cs`), with the table matching the wave: the
forward-minus-back difference at 100 mm/s falls from 0.08 to 0.01 mm rms (worst 0.22 -> 0.03 mm), at every speed
to the noise floor. On the tablet the table leaves part of the wave, so expect less; not yet measured there.
AGENTS.md has a new section 2a (model advice): local 3.7B-24B models failed this project's tasks, and Anargon sets
a 32B multi-agent session as the minimum.

**rc5 (1):** Coil Linearization is on by default (Anargon); `--no-coil-linearization` turns it off. It is used by
FAST, RAPID and ULTRA; its table was measured at ULTRA on one tablet and pen. (Part of Misc. Optimizations since
rc5 (4).)

Anargon: "switch match is just not the solution", and asked for Dynamic Anti-Chatter: "Prevents chatter by only
applying smoothing on detection of chatter", on by default, and "if theres any way to absolutely minimize the latency
to way lower then that would be nice". Gemini Hydrogen 4's roadmap (`DOCS/roadmap-after-r45.md`) arrived in the same
revision. Dynamic Anti-Chatter was tested offline on Anargon's 45-degree drags (`rev-output/hw45/drag/drag45.csv`), in
the twin and by single-stepping the firmware routine; the tablet then ran it in every ruler test below. rc5 also adds
Coil Linearization, the one idea of five that held up on the tablet.

- **Dynamic Anti-Chatter (new setting `dac`, default on; `--no-anti-chatter` turns it off).** The last stage of
  every frame, after ALPI. For each axis it takes the frame's noise as its second difference,
  d2 = p0 - 2 p1 + p2 (p0 this frame's position, p1 and p2 the two before): how far the frame is off the straight
  line through the two before it. When |d2| is over 0.10 mm the frame is pulled toward the one before,
  out = p0 + ws (p1 - p0); every other frame passes as it is. Nothing is predicted: only past positions are used, and
  the history holds the inputs, never the outputs, so nothing builds up or drifts, and a still pen with no chatter is
  never touched.
- **The pull is soft, for less delay.** ws = w x min(1, (|d2| - 0.10 mm) / 0.10 mm): a frame just over the threshold
  is barely moved, and only a frame 0.20 mm or more off the line gets the full w. w delays that frame by 0.4 ms at its
  frame period (w = 0.4 ms / period, at most 0.5; one value per Steady Hover ladder step). Offline on the drags,
  ULTRA, raw positions before ALPI (`experiments/noise45/dacvariants.py`; chatter = |d2|, 99th percentile; delay at
  1000 frames/s, averaged over all frames):

  | Variant | X chatter 99 % | X frames over 0.10 mm | X mean delay | Y chatter 99 % | Y mean delay |
  |---|---|---|---|---|---|
  | off | 0.514 mm | 45.0 % | - | 0.424 mm | - |
  | full pull (the first rc5 build) | 0.275 mm | 25.8 % | 0.180 ms | 0.237 mm | 0.131 ms |
  | **soft pull (rc5)** | 0.281 mm | 34.8 % | 0.118 ms | 0.246 mm | 0.075 ms |
  | soft pull, at most 0.25 ms | 0.338 mm | 38.9 % | 0.074 ms | 0.291 mm | 0.047 ms |

  The soft pull keeps almost all of the cut in the large jumps at 35 % (X) and 43 % (Y) less average delay. A
  smoothed frame is delayed 0.26 ms on average (X; Y 0.23 ms), at most 0.4 ms. More frames stay over 0.10 mm than
  with the full pull, because the frames just over the threshold are moved only a little. A lower cap cuts the delay
  further but leaves more of the chatter.
- **What it does not fix.** It removes single-frame jumps, not the error that repeats at the same place: the switch
  steps (Window Scale and Switch Match handle those) and the wave across each coil stay as they were
  (`experiments/noise45/compare5.py`, Window Scale + DAC, the two test halves: X switch jump 0.080 / 0.091 mm median,
  coil wave 0.18 / 0.22 mm peak to peak).
- **Firmware.** Routine `dachat` (`atf_antichatter.cs`), called right after ALPI; RAM `0x20001ED0` (16 bytes: the
  last two input positions per axis and a history count, cleared when the pen is lost and in the search restart).
  The threshold is halved in report mode 2 (1/100 mm units). The pull uses the hardware divide
  (w x min(|d2| - thr, thr) / thr). Cost: at most 133 cycles (1.85 us) per frame. Single-stepped on 40000 random
  inputs against its C# copy (`DynamicAntiChatter.Model`): 0 mismatches, both report units, the ladder and the
  hover-stage table, history 0..2, no pen (`experiments/noise45/dacstep.cs`). The twin gate passes at ULTRA (with
  ALPI), RAPID and FAST; the twin's noise is too clean to show the effect.
- **Gemini's PR #1, option A, tested offline and not built.** A correction table over the position inside the coil
  window (offset from the window's centre coil, knots every 0.1 coil), fitted on one half of the drags and tested on
  the other (`compare5.py`): worse than Window Scale alone on the test halves (X switch jump 0.072 -> 0.182 mm median
  on one half; Y coil wave 0.29 -> 0.43 mm), and the tables fitted on the two halves disagree by up to 2 mm at the
  window edges. The drags cannot pin such a table down.
- **Free CPU time inside a frame: measured, nothing moved.** Gemini warned that processing during the scan adds noise
  (R45 found it for the LED). In the twin at ULTRA (`experiments/noise45/cpuprof.cs`, the rc5 ULTRA + ALPI image,
  300 ms) the CPU never sleeps: per 1 ms frame, 245 us run in the main loop and 782 us in interrupts, most of it the
  scan's own interrupt code (the largest blocks: 201 us per frame at `0x080098E0`, 179 us in the step timer's handler
  at `0x08005D04`, 62 us in the sampling loop at `0x080046C4`). How much of that is waiting that could be used is not
  traced. ULTRA's frame budget leaves 25 us to spare. Gemini's 96 MHz clock is left for later.

### Coil Linearization (new setting `clin`, on by default, `--no-coil-linearization`)

- **What it does.** It corrects the position inside each coil with a fixed table, so the cursor moves evenly across
  every coil. The table comes from Anargon's ruler recording: a pen moving at a steady speed spends equal time over
  every part of a coil, so where the reports bunch up the stock arithmetic squeezes the position, and where they thin
  out it stretches it. Per axis: the position's phase inside its coil (the R43 coil grid), then a correction from 64
  knots per coil. Nothing is remembered between frames: no delay, nothing ahead of the pen, and a still pen cannot
  drift. It runs right before ALPI (after ALPI, as first built, it failed for a moving pen: rc5 (2) above).
- **Firmware.** Routine `clin` (`atf_coillin.cs`), a 132-byte table, about 60 cycles per axis. Single-stepped against
  its C# copy: 0 mismatches. The twin gate passes at ULTRA (with ALPI) and FAST.
- The table was learned on one tablet and pen (Anargon's), at ULTRA; on another tablet it may help less.

### The ruler test on the tablet (2026-11-16)

Each build was flashed in turn, and Anargon slid the pen along a clipped 15 cm ruler for 90 s. The cursor reports
were recorded read-only through the normal firmware (the recordings, analyses and readers are in `referencedata/`;
scored with `referencedata/tools/ruler6.py`). "Coil-locked" is the error that repeats at the same place of every
coil, given for each axis; "steps" are jumps of more than 0.3 mm that stay.

| Line | rc5 without Coil Linearization (X / Y) | With it (X / Y) | Steps per second |
|---|---|---|---|
| 33 degrees, matched speeds (where the table was learned) | 0.26 / 0.14 mm | 0.15 / 0.06 mm | 0.1 -> 0.1 |
| -34 degrees (not learned there) | 0.20 / 0.14 mm | 0.08 / 0.05 mm | 0.8 -> 0.5 |
| -10 degrees (not learned there; X cannot be measured on it) | - / 0.08 mm | - / 0.065 mm | 0.5 -> 0.2 |

Noise along the line is not worse. What is left is 0.05-0.17 mm of repeatable error that follows neither axis's
coils (worn spots on the tablet's surface, the ruler, the hand); Coil Linearization does not change it.

### Four other ideas, tested on the tablet and removed

Pen-Anchored Crossfade (the lines chosen by the pen, two parabolas crossfaded), Midpoint Gaussian (a log ratio of four
lines), Cross-Axis Table (X corrected by the pen's place inside a Y coil, and back) and Alternating Blend (near a coil
midpoint, the two coil windows read in turn and blended; one frame late) were built as test firmware. Each equalled
its C# copy and passed the twin gate. On the tablet (the 33-degree line, each build at its own pass speed):

| Build | X coil-locked | Y coil-locked | Steps per second |
|---|---|---|---|
| rc5 without Coil Linearization | 0.29 mm | 0.14 mm | 0.1 |
| Coil Linearization | 0.12 mm | 0.08 mm | 0.1 |
| Pen-Anchored Crossfade | 0.42 mm | 0.32 mm | 1.1 |
| Midpoint Gaussian | 0.57 mm | 0.59 mm | 11.9 |
| Cross-Axis Table | 0.20 mm | 0.15 mm | 0.4 |
| Alternating Blend | 0.51 mm | 0.34 mm | 15.0 |

Three of them make real steps of 1.3-2 mm and double the coil-locked error; the Cross-Axis Table changes nothing
measurable. Only the best idea is kept (Anargon), so the four are no longer in the program.

### How Coil Linearization compares with every other method

Every method so far that changes where the cursor is (as opposed to how much it shakes). The numbers come from
different tests, so compare within a row's own test: "drags" are Anargon's 45-degree freehand drags with every coil
reading recorded, replayed offline; "ruler" is the tablet test above.

| Method | Revision | What it changes | Tested on | Result | In rc5 |
|---|---|---|---|---|---|
| Stock arithmetic (3-line parabola + 4-line ratio) | XP-Pen | - | drags | switch step X 0.24 mm (median); wave across each coil X 0.16 / Y 0.20 mm | underneath everything |
| Multi-coil Read (five-line fit) | R43 | replaces the arithmetic | tablet, pen held still and hovering | 0.62-0.85x the noise at hover; with 12-bit Readings X worse at low hover (0.34 against 0.19 mm); no gain with ALPI | off (experimental) |
| Switch Match | rc4 | matches each switch to the last 16 positions | drags | switch step X 0.24 -> 0.036 mm; drifted at a still pen until gated in rc4 (2) | on |
| Line gain fixes (cold-line gain, no cold line, Gaussian fit, centre gain, learned slot gains) | rc4 (2) | the coil readings themselves | drags | no gain: 1 % on one line moves the step 0.15 mm | not built |
| Window Scale | rc4 (2) | the stretch inside the coil window, per pressure | drags | switch step X 0.24 -> 0.08 mm (0.04 with Switch Match); the wave unchanged | on (ULTRA) |
| Window-offset table (Gemini, PR #1 option A) | rc5 | a correction by the offset from the window's centre | drags | worse: X step 0.072 -> 0.182 mm, Y wave 0.29 -> 0.43 mm | not built |
| Dynamic Anti-Chatter | rc5 | pulls single-frame jumps back | drags; on the tablet in every ruler run | X chatter (99 %) 0.51 -> 0.28 mm at 0.12 ms average delay; the repeatable error unchanged | on |
| Centre gain per pressure | rc5 | the centre line's gain | simulation, then drags | slightly better than Window Scale in the simulation, worse on the drags (X step 0.245 against 0.072 mm) | not built |
| Pen-Anchored Crossfade | rc5 | lines chosen by the pen | ruler | X 0.42 / Y 0.32 mm, 1.1 steps/s | removed |
| Midpoint Gaussian | rc5 | a log ratio of four lines | ruler | X 0.57 / Y 0.59 mm, 11.9 steps/s | removed |
| Cross-Axis Table | rc5 | each axis by the other axis's coil phase | ruler | X 0.20 / Y 0.15 mm: no measurable change | removed |
| Alternating Blend | rc5 | both windows near a midpoint, blended | simulation (the best there), then ruler | X 0.51 / Y 0.34 mm, 15 steps/s, one frame late | removed |
| rc5 without it (Window Scale, Switch Match, Dynamic Anti-Chatter) | rc5 | - | ruler, three lines | X 0.20-0.26 / Y 0.08-0.14 mm coil-locked, 0.1-0.8 steps/s | rc5 default |
| **Coil Linearization** (on top of the defaults) | rc5 | a fixed correction per place inside a coil | ruler, three lines | **X 0.08-0.15 / Y 0.05-0.07 mm coil-locked, 0.1-0.5 steps/s** | on |

The stages that work on noise rather than on position stay as they were and work together with Coil Linearization:

| Stage | Revision | Result | In rc5 |
|---|---|---|---|
| 12-bit Readings | R43 | tablet: cursor noise at low hover 0.38x (Y) and 0.86x (X) | on |
| Frequency Hold and Coil Hold | rc1, rc2 | tablet, pen held still: X 0.183 -> 0.116 mm, Y 0.119 -> 0.083 mm | on |
| ALPI (Tracker) | rc1 | tablet, pen held still: 0.08x (X) and 0.06x (Y) the shake with no filter | with Full calibration |
| Bootloader Timer Off | rc2 | removes the cause of the bursts at coil midpoints (a leftover 1 ms bootloader timer) | on |
| Tilt Frame Skip | rc3 | the tilt frames' 0.2-0.44 mm hover offsets (tablet) gone in the twin | on |
| Steady Hover (dynamic) | rc4 | pen on a 3 mm stand: 0 stage switches in 60 s (rc3: 3660, 0.12-0.2 mm each) | on |

- Defaults: Window Scale, Switch Match, Dynamic Anti-Chatter and Coil Linearization on.

## R45-rc4

Anargon: "1000hz is WAY too low; it should test the upper extremes", the rate should be "ridiculously dynamic, as in
it holds 1000hz" and fall only slowly, as far as the hover height needs, keeping the noise of a low hover (about
7 mm) at 1000 Hz. Two bugs: "the cursor also jumps when switching between hover states" and "the stepping of the
coils are too noticable if you draw 45* lines, by a lot". Tablet: the first rc4 build (below) and the drag and lift
recordings. The fine ladder and Switch Match are tested in the twin and offline on the tablet recordings; Switch
Match is on the tablet (ULTRA + ALPI), not yet judged by hand.

- **Dynamic Steady Hover: the rate falls in small steps, only as far as the pen's height needs.** The fast stage
  keeps the preset's full rate down to 1.12 x its own drop level, the top of what it can hold (on the tablet ULTRA's
  fast stage kept the pen down to level 76). The noise there is the target: ULTRA's fast stage at that level, raw
  positions before ALPI. Below it the firmware moves one step at a time down a ladder of reads, each a little longer
  than the last, and each kept until its own noise reaches the target. ULTRA's ladder: 1000/s (4 samples x 16
  pulses), 930, 909, 870, 833, 800, 755, 714, 678, 635, 597, 556, 519, 482/s (44 x 26), 426/s (44 x 40), then the reach
  stage (60/s). RAPID and FAST get their own ladders the same way. The first rc4 build had only 1000, 500, 333/s and
  reach, and left 1000/s at level 82: on the tablet's 3 mm stand that build ran all at 1000/s with no switches (rc3:
  3660 switches a minute, 1830 of them fast tries 0.36 mm off), but it halved the rate in one step higher up.
- **The step back up uses the ratio measured on the way down.** Two frames after a step down the firmware compares
  the new read's level with the level it left at, and steps back up only at that ratio x the faster step's level x
  1.25. So like is compared with like: a still pen does not switch back and forth (rc3 did: 24 fast + 3 Steady Hover
  frames, about 37 times a second). The probe frames of rc3 (a fast try every 16 frames) are gone.
- **A found pen at 800 / 1000 Hz starts at the first step with Steady Hover's 40 pulses** and stays there until one
  whole frequency sweep is done: from a shorter read the first sweep picked a drive step 2 too high in the twin, and
  every report then showed pressure in the air.
- **rc4 (2): Window Scale (new setting, default on, ULTRA) and Switch Match with two gates (default on).**
  Anargon, on Switch Match: "X will not report position consistently" without it; the target is 0.1 mm of true
  position error.
  - **What the switch step is.** At a coil-window switch, the position inside the old window and inside the new one
    are both off by a share of their distance from the window's centre coil, as if the window were stretched. A
    stretch k makes every switch jump by k coils along the switch, whichever way the pen goes and wherever the switch
    happens. On Anargon's 45-degree drags k follows the pen's frequency, which the pen sets from its pressure: X about
    +0.043 below pressure 10000 and about -0.047 above (where the cold first reading changes sign), Y -0.03 to +0.09.
  - **Tried first, offline on the drags** (`experiments/noise45/gainfix.py`, `slotlearn.py`): dividing the cold
    line by a gain (the stand-measured gains, or one fitted), leaving the cold line out of the arithmetic, a Gaussian
    peak fit, a gain on the centre line, and per-line gains learned from the switches: the same jumps or worse. A 1 %
    change of one line's gain moves the jump by about 0.15 mm, finer than the drags can pin down line by line.
  - **Window Scale** (`atf_winscale.cs`, `--no-window-scale`): after the stock position routine, position = r +
    (r - centre coil x 1018) x k / 4096, with k from 9 knots at pressure 0, 2048, ..., 16384 and straight between
    them, so a pressure change never makes a step. Hover uses the lightest touch's value (the drags' only hover
    switches agree: X +0.068 from 1, Y -0.011 from 3). Only in the fast stage and only at ULTRA, where it was
    measured. Nothing is kept from one frame to the next, so it cannot drift. Offline on all drags: X switch jumps
    0.238 -> 0.083 mm (median; 90 %: 0.463 -> 0.279), Y 0.091 -> 0.050. Fitted on half the drags and tested on the
    other half: X 0.15-0.17 mm. *(hardware recordings, offline, the firmware's integer arithmetic)*
  - **Switch Match's gates** (`atf_switchmatch.cs`): a coil-window switch is matched only while the pen moves at
    15 mm/s or more and when that axis did not switch in the 16 frames before. Drive-step and ladder-step switches need
    only the second, so the hover-step jump stays fixed. A simulated still pen whose window flips: the same as without
    Switch Match at every flip rate (slow part 0.017-0.055 mm; ungated 0.06-0.20 mm). Back on by default
    (`--no-switch-match` turns it off). *(simulation, offline)*
  - **Both, offline on the drags:** switch jumps X 0.037 mm median (90 % 0.17), Y 0.036 (0.17); without either,
    0.24 / 0.09.
  - **Not fixed:** the wave across each coil (on the drags X 0.16 mm and Y 0.2 mm peak to peak, on the ruler up to
    0.35 mm from the line) is left: neither change touches it. Window Scale's table comes from one tablet and pen.
  - **Twin:** the gate passes, and the firmware equals `WindowScale.Apply` on every position call (1600, 1460
    changed). The twin has no cold first reading, so its windows agree at a switch and Window Scale adds a step
    there instead of removing one: still pen (it sits where X flips windows) X 0.098 -> 0.122 mm. Only the tablet
    can show the fix. Suites: February and December, 528 checks each, pass. *(twin)*
- **rc4 (2): a ruler measurement of the true position error.** Anargon asked for the true
  position error, with 0.1 mm as the target. They slid the pen along a 15 cm ruler laid corner to corner (32 degrees),
  about 5 passes each way, lifting at each end. Recorded read-only through the cursor of the firmware on the tablet
  (ULTRA, Switch Match on): 80109 reports in 90 s. A ruler is straight, so the sideways distance from one fitted line
  is the tablet's error (plus changes in pen angle and pressure). On the middle 114 mm, with the first and last 150 ms
  of each pass left out:
  - the error that comes back at the same spot in every pass is 0.68-0.79 mm peak to peak;
  - 0.25-0.32 mm of it is slow (over 10 mm or more: the tablet's own warp, or the ruler bending under the hand);
  - the rest is on the scale of a coil: up to 0.33-0.35 mm, sd 0.09-0.11 mm;
  - forward and back differ by up to 0.30 mm at the same spot (rms 0.07);
  - short sideways steps: median 0.014 mm, 99 % 0.21 mm, 4.4 % of points over 0.1 mm.
  The passes scatter by 0.16 mm sd at the same spot, so the hand limits how exact this is, and it is one line only,
  so other areas are a guess. The 0.1 mm target is not met. The earlier freehand drags, compared offline with no
  switch handling, with Switch Match and with the halfway point between two windows, all show the same wave across
  each coil (0.15 mm X, 0.20 mm Y): how the switch is handled does not remove it. *(hardware, cursor only; one line)*
- **Switch Match without gates failed: it adds drift at a still pen.** Anargon found a noisy point: pen
  still on the stand, just above the surface, X exactly at a coil midpoint. Recorded read-only through the cursor
  (no flash; 29992 reports in 30 s, all at 1000/s, so the fast stage throughout): X wandered with an sd of 0.30 mm
  and 2.7 mm peak to peak, Y 0.14 mm (coil phase 0.79), as a slow drift (strongest periods 160-380 ms) with small
  frame-to-frame steps (0.04 mm rms). Switch Match explains that kind of drift: at a midpoint the coil window flips
  often, and each flip turns a few frames of noise (the line fit and the 4-frame match) into an offset that lasts up
  to 500 ms. ALPI removes fast noise but passes slow offsets. Simulated (a still pen, the window flipping every 3-100
  frames, 0.1 mm frame noise, a slow filter after it): sd 0.01-0.08 mm without Switch Match, 0.08-0.13 mm with it,
  about 1 mm peak to peak. In the twin the still pen's Y went from 0.005 to 0.020 mm with it and back to 0.005
  without. It hides the switch step but does not remove the offset, and it adds drift where the pen is held still,
  the case that matters most for aiming. The gates below keep a still pen out of it.
- **Switch Match** (new setting, on by default, with two gates that leave a still pen alone): no position
  step when the tablet changes how it reads the pen.
  - **The cause.** Each reading drives the pen with a short burst through the window's centre coil and then
    listens. The pen is still ringing from the last burst when the next starts, except before the first line read
    in a window, so the five lines are not read alike: on Anargon's tablet, fast stage, pen still, the first line
    read came out 44 % low on X, the next four -5, +3, +7 and +8 %. The pattern depends on the read length (the hover
    step), the drive frequency step and the pen's frequency (pressure). So each coil window, read length and drive
    step carries its own small position bias, smooth inside it, and the position jumps when one of them changes.
  - **Measured on the tablet:** drags at 45 degrees (`rev-output/hw45/drag/drag45.csv`): a jump of 0.24 mm on X
    (median; 90 % up to 0.46 mm) and 0.09 mm on Y at every coil-window switch, against 0.04 mm between two frames
    elsewhere. Hover lifts: fast stage to Steady Hover 0.67 mm on X, back 0.48 mm.
  - **What it does.** At a switch the new reading is compared with a straight line through the last 16 positions,
    the difference is taken off (refined over the next 4 frames), and the correction fades out as the pen moves away
    from the switch (gone after half a coil, 2.5 mm) or with time (gone after 500 ms). It is left out when the pen
    moves faster than 150 mm/s or the line bends (the pen turning), and it is never more than 1.5 mm. It uses only
    past positions: nothing is placed ahead of the pen. Outside the half coil after a switch the position is the raw
    one, unchanged.
  - **Offline on the tablet recordings, with the firmware's integer arithmetic:** jumps at coil-window switches
    0.24 -> 0.036 mm (X) and 0.09 -> 0.040 mm (Y), used at 69 of 75 switches; fast stage -> Steady Hover 0.67 ->
    0.05 mm, the other step changes 0.02-0.05 mm. In 64 simulated flicks 62 were untouched, two up to 0.15 mm more
    off for a moment.
  - **What it does not do.** It removes the step, not the bias itself: for the half coil after a switch the position
    keeps the old window's bias, fading to the new one's, so the step becomes a ramp of the same size over 2.5 mm.
    The fix at the source would be to correct the first-read line's gain per read length, drive step and pressure;
    that needs those gains measured on the tablet first (not done). It costs about 3 us a frame, 15 us at a switch;
    ULTRA's frame budget is 975 of 1000 us.
- **misctests/** (Anargon: harnesses other agents can reuse): `laddertest`, `steptrace`, `hoverpressure` and
  `run.py`, which builds and runs them with safe settings. AGENTS.md section 5 points to it.
- **Tests:** the core suite's no-holds RAPID hover-pressure case (Frequency Hold and Coil Hold off) is now reported,
  not checked: in the twin it flips between no pressure and pressure in almost every report on changes of code
  layout alone (Switch Match's routine built in but never called: 779 of 801). With the holds (default) it stays at
  0 and is still checked. Suites: February 528 checks, December 529, all pass.
- Not yet on the tablet: the fine ladder at real hover heights (the rc4 lift recordings were mostly outside the LED
  zone), and a hands-on judgement of Switch Match. RAPID and FAST builds with it pass the twin gate (still pen X 0.028 /
  Y 0.017 and 0.004 / 0.005 mm); only ULTRA is on the tablet. Not fixed: the tilt frames' own first-read offset (the outer line
  is read first there).

## R45-rc3

Anargon: low hover "still shakes too much", and the hover height felt lower with each revision. Everything below is
tested in the digital twin only; the tablet is on another PC for now.

- **Renamed Anargon's Tablet Tools** (folder `anargons-tablet-tools`, `AnargonsTabletTools.exe`, namespace
  `AnargonsTabletTools`): the tool is meant for more than one tablet. The tablet itself is still called the Deco 640.
  The saved flash count and the warnings already shown are still read from the old `AnargonsDeco640Tools` folder
  until the new one exists.
- **The hover stages no longer flip back and forth at low hover.** On Anargon's tablet the firmware ran 24 fast
  frames, then 3 Steady Hover frames, about 37 times a second, and each switch moved the cursor 0.1-0.2 mm. A slower
  stage reads the pen stronger (Steady Hover about 2.3x on the tablet, reach about 6x), but it was compared with the
  fast stage's own level, so a pen too weak for the fast stage read "strong" and went straight back. Each stage now
  has its own step-up level (the fast stage's level as that stage reads it, +25 %). In the twin: no flapping; fast to
  Steady Hover at 7.4 mm and back at 6.5 mm; the pen is held up to 22.0 mm again (rc2: 16.8 mm, where the reach
  stage stepped up and lost the pen).
- **The reach stage keeps a pen it already tracks down to 60 % of its old drop level** (Anargon: "pushing the hover
  limits for the 60hz stage again"). Picking a pen up still needs the full level, so nothing is picked up without a
  pen. Twin at ULTRA: held up to 25.8-26.4 mm (22.0 before). Longer reach readings were tried and made it worse
  (48 samples: 20.4 mm).
- **Tilt Frame Skip** (new setting, default on; `--no-tilt-skip`): 4 frames of every 16 also measure the pen's
  tilt. On Anargon's tablet, hovering close in the fast stage, their positions came out off (Y +0.21 / +0.44 /
  -0.20 mm on three of them, X -0.17 mm on the first), a shake about 60 times a second. Hovering in the fast stage,
  those frames now read the position like the others, and tilt keeps its last value until the pen touches or goes
  higher. Touching, Steady Hover and reach measure tilt as before. Letting ALPI carry on along its own path on those
  frames failed (twin: still pen 0.004 -> 0.034 mm). Not yet on a tablet.
- **ALPI comes only with Full calibration** (Anargon: "ALPI is not and should not be on without selecting full").
  Skipping the ALPI training in Full now means the defaults: Anargon's training, the pick-up levels from Anargon's
  own measurements (RAPID, ULTRA) or the preset, and the stock pen frequency; the wizard says they were not
  measured on this tablet. Before, `s` built without ALPI and Anargon's training was a separate choice (`f`). To
  build without ALPI, turn it off in step 5. On the command line ALPI is off unless `--apip FILE` is given, as in
  rc2 (an rc3 draft had turned it on by default; it never shipped). Pen frequency tuning and ALPI do not go
  together: ALPI shows as not available with it.
- **Bootloader Timer Off is a setting** (default on; `--no-timer-off`): rc2's SysTick fix, shown in the noise
  settings with what it does, so every firmware change is disclosed.
- **Porting guide** (`DOCS/porting-guide.md`) and `tools/survey.py`: how to study another tablet's firmware from a
  full dump, step by step, with how to cross-check each answer; the worked example is a G640 Rev B dump (a different
  chip family). General, for people and AI agents.
- **Revisions after R45 are named after their pull request** (`PR #X`, from a GitHub account linked to a person;
  AGENTS.md section 4).
- **Rate limit, built and run in the twin** (Anargon: "without pressure or pen buttons or tilt"): frames with only
  the position (no frequency sweep, no tilt) run at 1140 a second with 5 + 5 lines and 1545 with 3 + 3 lines; the
  earlier model said about 2000. USB still carries 1000 reports a second. Nothing built from this.
- **Developer documents, written out in full** (Anargon: for "a university student in cybersecurity who has basic
  programming language understanding but not one who knows assembly"; AGENTS.md rule 17 and section 3b, documents
  only, the program's screens stay short): `FIRMWARE_DESIGN.md` (how the tablet works, from what a microcontroller
  is to the flash counter and recovery), `porting-guide.md` (now with a step 0 for the instruction set and a worked
  G430S example: a Microchip PIC32 with a MIPS core), and two new tutorials, `tutorial-self-modding.md` (a Soft
  Pressure curve added to the source, built, measured in the twin) and `tutorial-patcherpkg.md` (the same mod as a
  patcher package). `otd-plugin.md` proposes running the filters on the PC as an OpenTabletDriver plugin (design
  only, nothing built).
- **Patcher packages can change the firmware** without replacing the tool: `IPatch`, `Build.AddPatch` /
  `ClearPatches` (each in its own `Package: <name>` group after the tool's own changes and before the undo record,
  named in the options text as `PACKAGE=<name>`, left out of measurement, training and return-to-stock builds) and
  `Layout.AddSignatures` for new firmware locations.
- **Patcher packages fit the options to their tablet** (Anargon: a package handles "patching steps for each tablet,
  alongside allowing for the removal, addition, etc. of options depending on the tablet"):
  - `Build.AddSetting(Setting, IPatch)` adds an option, shown under "Package options" at the end of the Extra QOL
    Patches screen; its patch runs only when it is on (`Package option: <name>` group, `PACKAGE-OPTION=<KEY>` in the
    options text, its tag in the file name).
  - `Build.HideSetting(key, why)` removes one of the tool's options: not shown, and every build treats it as off.
    Volatile Changes cannot be hidden (RAPID and ULTRA need it).
  - `Build.Option(key)` changes one (name, text, default, tag).
  - `Build.IsOn(key)` and `Build.BuildingPreset()` tell a patch what the build being made has chosen.
  - `Build.ClearPatches()` undoes all of it, so the built-in tool is unchanged when the package returns.
- **Package ZIPs made by Windows PowerShell 5.1** (`Compress-Archive` stores `src\X.cs` with a backslash) are read:
  `\` counts as a folder mark, then the usual path checks apply. R44 to R45-rc2 refused them as "unsafe file path".
- **Recovery messages link to the [`recovery` folder](https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery) of Anargon-osu/deco640-resources**
  unless `ADT_TEARDOWN_URL` points elsewhere; so do the README, AGENTS.md and the docs. The README lists the three
  repositories.
- **Tools:** `tools/mksig.py` takes Windows paths (`C:\...`) for its dumps and expands `*.cs` itself (cmd and
  PowerShell pass it through as it is); `tools/survey.py` has `--isa thumb|mips32` and `--base`.
- **Tests run side by side and capped** (Anargon: "there could be like 20-100 consecutive executables"; "capped to
  50% of a cpus total usage collectively, unless ran with --uncapped"; "Github tests should always be --uncapped"):
  `tests/run-regressions.py` starts every test group as its own process at once, each in its own folder, so several
  runs (one per stock build, or several agents) never share a file; `tests/cpucap.py` keeps every capped run on the
  computer together at 50 % of the CPU; `--uncapped`, `--only` (short suite names work: `core/fw-boot,host`) and
  `--whole`. `tests/run-tests.py` tests February and December at the same time (`--build feb|dec|all`) inside the
  same cap. The slowest group (`cli-firmware`, 67 s) is four groups now.
- **Test dumps from a git repository** (Anargon: GitHub needs "an .env that always clones the directory with February
  and December builds"; it "should git clone/checkout the selected link to the required directory"):
  `ADT_TEST_DUMPS_REPO` (+ `ADT_TEST_DUMPS_REF`) is cloned, or fetched again, into `tests/work/dumps-repo/`, and each
  build's dump is picked by its firmware CRC. The token is sent as a header, never stored in the clone; several runs
  share the clone through a lock file. `.env.example` names the resources repository.
- **GitHub workflow:** every run `--uncapped`; the stock-firmware checks also run on pull requests from this
  repository (forks have no secrets); `ADT_TEST_DUMPS_REPO` / `_REF` variables; `GD32EMU_TOKEN` for a private
  emulator repository; the dumps clone is kept out of the uploaded logs.
- **Shorter test folder names** (`core`, `host`, `cal`, `cal-work`): with the old names the calibration suite's files
  passed Windows' 260-character path limit when `run-tests.py` ran in a source folder as deep as Anargon's.

## R45-rc2

R45-rc1 had mistakes in calibration and in the order of the patch screens (Anargon), so the release candidates go on.

- **Full calibration with ALPI measures Standard's values in the background** (Anargon: "ALPI should combine all
  calibration steps as necessary, running them in the background"). The training firmware records each sample's
  detection signal and pen frequency, and keeps the pen down to the measurement firmware's levels (40 / 24). The
  light touch, the centre hold (the normal hover) and the weakest 5 % of the fast-stage hover readings (the highest
  hover) give the fast stage's pick-up and drop levels by the same rule as Standard, never above the preset's. The
  results also show the pen's frequency at a light touch and hovering. The levels are saved in the ALPI training file
  and used again with it (`--apip FILE` too). Twin only so far.
- **Standard (without ALPI) has three steps:** a light touch, a normal hover and the highest hover.
  - The no-pen step is gone: the firmware sends readings only while it tracks the pen, so it always showed LOST.
  - The slow lift is gone: it looked for the same limit as the highest hover, fed nothing into a build, and ended at
    the coils' floor on Anargon's tablet.
  - The readiness check and the first step asked for the same light touch back to back; the first step now measures
    right after the check.
  - Calibration files no longer have `signal_away` and `signal_lift_min`.
- **Noise-Related Patches shows ALPI first,** then noise tuning, the position filters and Multi-coil Read (Anargon:
  "Calibration (with or without ALPI), Noise related settings (from ALPI to Multicoil, etc.), then other settings").
  The step 4 levels are named "Standard (without ALPI)" and "Full (with ALPI)".
- **Frequency Hold waits for 8 sweeps in a row, not 3** (Anargon's sideways shake on the final ULTRA + ALPI build).
  With the pen resting between two drive steps, the sweep picked the other step in about a third of frames, so 3 in a
  row still came several times a second; each hop moved X about 0.2-0.3 mm, and ALPI followed it as a real move.
  Tablet A/B (silent study build, 4 s blocks of 3 / 5 / 8 / 12 sweeps): 6.6 / 1.1 / 0 / 0 hops a second, X spread
  0.098 / 0.085 / 0.055 / 0.056 mm. The final ULTRA + ALPI build with 8: the tablet's reports spread 0.013 mm in X
  (median 4 s block; the build with 3 at the first spot, about 1 mm away: 0.120). A real one-step frequency change
  now waits up to 8 sweeps (about 65 ms at ULTRA).
- **The bootloader's 1 ms SysTick interrupt is switched off** (Anargon's noise at coil midpoints on ULTRA). The vendor
  bootloader starts SysTick at 1 ms with the highest priority and leaves it running into the firmware, which never
  uses it. Every 1 ms of the tablet's own clock it paused the scan for a moment, and the coil being read then came out
  about 15 high; at a coil midpoint that moves X or Y about 0.4 mm. At ULTRA the frames follow the PC's USB clock, so
  that moment drifts through the frame (a full pass about every 74 s on Anargon's tablet) and each coil gets a few
  seconds of noise in turn. The scheduler now clears SysTick at boot and on every pen loss (12 bytes, nothing per
  frame); every scheduler level gets it. Tablet A/B (silent study build, 4 s blocks with the interrupt on / off, 240 s
  at a midpoint): with it on, bursts every 74 s of up to 0.195 mm frame to frame; with it off none (worst block
  0.075 mm, the quiet level). The final ULTRA + ALPI build with it off, 160 s of its reports: worst 4 s block 0.018 mm
  in X and 0.014 in Y (the rc2 build before it: 0.106 / 0.106), no noisy second.
- The build options text calls the measurement and training builds' levels `MEASUREMENT-LEVELS`, not `MEASURED`.
- Percentiles are sorted with `Array.Sort` (a training has hundreds of thousands of readings).

## R45-rc1

R45 is the last planned revision before the project goes public. Everything below is in R45-rc1.

### Calibration levels and ALPI

- **Step 4 is Calibration, with three levels.** Then step 5 Noise-Related Patches and step 6 Extra QOL Patches
  (`b` goes back). The settings check runs after step 6.
  - **Fast Pass:** Pen frequency tuning (experimental). Nothing to do on the PC. The build keeps the preset's own
    pick-up levels, because measured ones need the stock scan frequency.
  - **Standard:** the measurement firmware and its five pen positions. It sets the fast stage's pick-up and drop
    levels and Jump Rejection's margin, and unlocks Multi-coil Read.
  - **Full:** ALPI training after the patches are chosen. Unlocks ALPI (its Tracker is on by default there) and
    Multi-coil Read.
- **Full with ALPI skips Standard's measurement.** Standard only puts three numbers into a build: the fast stage's
  pick-up and drop levels and Jump Rejection's margin. ALPI replaces Jump Rejection, and the pick-up levels come from
  Anargon's measurements (RAPID, ULTRA) or the preset. That saves one firmware flash and the time with no cursor. If
  ALPI ends up off (switched off in step 5, or not usable), the measurement is offered before the build.
- **Fallback:** what a run does not measure uses Anargon's own measurements where there are some, else the preset's
  values. RAPID: pick-up 135, drop 90, Jump Rejection margin 1.293 mm. ULTRA: 93 / 64. A skipped or failed ALPI
  training uses Anargon's ULTRA training (2026-11-11, 602479 readings).
- **ALPI** (Algorithmic Position Interpolation, called APIP in R44): a training firmware records the pen in normal use;
  the PC fits a filter per height band (an alpha-beta tracker with still gains, a coil-distance correction, gates);
  the firmware runs it. It replaces Position Interpolation, Movement Smoothing and Jump Rejection. Positions are in
  the XP-Pen driver's report mode 4 units (1/200 mm), so profile format 1 files are refused. On Anargon's tablet at
  ULTRA, with the pen held still in the middle, the Tracker's cursor shake was 0.08x (X) and 0.06x (Y) of no filter;
  the R43 build's was 0.28x / 0.21x. The Robust filters (2-4 trackers) are experimental: 0.18x / 0.20x there.
- **Fast Pass keeps the fallback Jump Rejection margin** (RAPID 1.293 mm). Before, it fell back to 1.0 mm, although
  the margin does not depend on the scan frequency.
- **`--startup-calibration` with `--apip` is refused** on the command line, as the wizard already kept them apart.

### Noise

- **Frequency Hold** (on by default, paced presets): after a sweep, the scan's drive frequency moves at once when
  the strongest step is 2 or more steps away, and by one step only after 3 sweeps in a row agree. ULTRA's short sweep readings made it hop at random, and
  each hop moved the position about 0.5 mm. On the tablet (still pen, touching, ULTRA): X spread 0.175 -> 0.125 mm,
  Y 0.102 -> 0.080 mm.
- **Coil Hold** (on by default): the reading window moves to the next coil only when that coil's line is stronger by
  3/32 of the centre line's height above the weakest line. The scans drive the pen through the centre coil, so its
  line reads 2.5-2.8 % low, and between two coils the window switched in most frames. Twin: switches at a coil
  midpoint 50 % -> 0.
- **Both holds on the tablet** (silent build, ULTRA, still pen): X 0.116 mm, Y 0.083 mm, against 0.183 / 0.119 mm
  with neither.
- **Hover pressure:** with both holds, RAPID's short frequency readings show no pressure while the pen hovers in the
  twin (0 of 800 reports; 800 of 800 with both off).
- **The calibration and training LED pulses** (360 ms: 60 ms full, 120 ms down, 60 ms at 10 %, 120 ms up). At FAST,
  RAPID and ULTRA the LED changes only when a frame starts again: switching it during a scan step made readings
  1.2-2.2x noisier on the tablet.

### Firmware made by other mods, and by this tool

- **Mei's and Mily's mods are recognised by name,** and the notice says what each change was and how it was made
  (which instruction or data byte, at which address, the stock and the found value). Each is set back to its stock
  value, and the result must be exact stock firmware.
- **A build of this tool is set back to stock too.** Every build now carries an undo record (the stock value of each
  byte it changed, which modification changed it, CRC-32 checked). A build from before R45-rc1 is set back from a
  stock copy of the same firmware near it. The notice lists what each part of the build changed and where. A
  damaged record is never used.
- **Build options as text:** the last bytes of every image name the build and its settings, readable in a `.bin` or
  a dump. Nothing reads it.

### Checked, not built

- **Multi-coil Read with ALPI:** no noise benefit over the stock position arithmetic with the holds on (offline
  replay of tablet line recordings). After ALPI the spread is about the same (X 0.97-1.04x, Y 0.79-0.99x) and the
  frame-to-frame jitter is slightly worse (X 1.11-1.16x, Y 1.02-1.11x). It stays experimental.
- **Background training for the shake near a coil midpoint:** not built. On the training data (recorded before the
  holds) both axes are 2.4-2.9x noisier just before a coil midpoint. A noise model per coil position on top of ALPI
  gave no gain in replay, real or synthetic, and readings between the training states would add at most 0.7 % more
  data. The shake matches the window switching that Coil Hold stops: a simulation with the centre line 2.7 % low
  gives the same 2.6-2.9x peak, about 0.4 mm wide, and Coil Hold brings it down to about 1.3x.
- **Report rates above 1000/s:** raw noise is flat from 300 to 1000/s with the pen touching; scan timing caps the
  rate at about 1700-2500/s.

## R44

### Simpler calibration (Anargon's spec)

- **The LED marks where to hold the pen.** In the measurement firmware the LED is off with no pen, or with the pen
  more than 7 mm from the middle of the drawing area on either axis (a 14 x 14 mm box); on inside the box; and it
  blinks only while a step is recording. The no-pen step has no target. Before R44 the LED stayed lit without a pen
  and blinked whenever the pen was seen.
- **The tool tells the tablet the target and when it records** through USB string requests 176-179, which only the
  measurement firmware answers (with `ADTZ1:` and the values it took). Every other build ignores them as before.
- **No cursor:** before flashing the measurement firmware, and during calibration, the tool says there is no cursor
  and not to press the pen's buttons or use the mouse.
- **Minimal pressure is under 4096** (was 2048, too light).
- **Highest hover** uses Anargon's wording: move the pen up until the LED stops shining, bring it down until it
  shines again, and hold there.
- **The slow lift is not timed:** it measures from the surface until the tablet loses the pen (at most 60 s).
- **Tablet driver:** "Exit your tablet driver: close the OpenTabletDriver window, or right-click XP-Pen's icon
  (XPPenTablet) in the system tray, next to the clock, and exit it."

### Folders, patcher packages and wording (Anargon's flow)

- **Folders made at start**, next to the program: `import`, `export`, `dumps` and `patcherpkgs`. Step 1 looks in
  `import` first and also takes a folder; Step 2 saves to `export` by default.
- **Patcher packages come back, with guards** (`atf_packages.cs`, [patcher packages](patcher-packages.md)). When
  `patcherpkgs` holds one, the first screen is "Choose the patcher:". Only C# source is accepted. It is compiled with
  Windows' own `csc.exe` (never from PATH) and needs the typed phrase `I know what I am doing.` the first time each
  exact content runs. Packages are not signed, and they can flash without this tool's checks; the screen says so.
- **New wording** on every wizard step, after Anargon's examples: "Input File Directory", "Output Folder",
  "Enter = <path>", a short paragraph on what each step does, and plainer preset names (for example
  "RAPID (800) - low delay").
- **A Volatile Changes warning** on every step: they change how the tablet connects (a USB report every 1 ms), sit in
  the part of the firmware an incomplete flash loses, and a stock update undoes them.
- **The warnings keep one order** on every step (the update count no longer moves to the end).

## R43

### Stock ULTRA measured; window width

- **Measured on the tablet at ULTRA, pen hovering low, against stock ULTRA** (rounded readings, stock arithmetic,
  Position Interpolation; 30 s each, per-second medians):
  - 12-bit Readings alone: the cursor shook 0.38x as much sideways and 0.26x as much up and down (3.0x less
    overall).
  - 12-bit Readings with Multi-coil Read: 0.59x and 0.35x (2.0x less overall).
  - Multi-coil Read only helps with rounded readings (0.68x and 0.92x). It stays off by default, and its text now
    gives these results instead of "Not yet tried on a tablet".
- **Window width:** every screen first checks that the console window is wider than 96 columns. If it is not, it
  asks you to zoom out (Ctrl and -, or Ctrl + mouse wheel) or widen the window, and continues by itself once
  everything fits. The Terminology lines were rewrapped to 96 columns.

### 12-bit Readings

- **New: 12-bit Readings** (noise section, on by default, every preset but STOCK).
  - The stock firmware rounds every coil reading down to a multiple of 8 (it clears the lowest 3 of the ADC's 12
    bits). This setting keeps all 12.
  - The pick-up, drop and boost levels go up by 4 to match (the cleared bits average 3.5), so the hover range and
    saved calibration files stay as they were. New calibration files are measured with rounded readings too.
  - On the tablet at ULTRA, pen hovering low: the cursor shook 0.38x as much on Y and 0.86x on X compared with the
    same build with rounded readings. No extra frame time.
- **Multi-coil Read's floor follows it:** `73 x reads - 62` with 12-bit readings, `69 x reads - 60` without.
- **The twin's ADC gives all 12 bits;** the stock firmware's own loop still rounds them.
- **Signatures:** 1 added (the rounding instruction). Every other one is unchanged.

### Tablet tests and Multi-coil Read

- **Tested on Anargon's tablet with line-recorder builds** (QUICK, FAST, RAPID, ULTRA). Results are in
  `rev-output/hw43`:
  - still: tip resting, hover, low hover at the edge, highest hover, held in the hand (30 s each);
  - moving: ruler strokes side to side and up and down, two passes each (ULTRA, RAPID, FAST).
- **The coils sit on an even 0.2 inch (5.08 mm) grid** on both axes, the same at every speed (within 0.24 mm).
- **Every line reading has a floor** of about 72 per conversion: 216 at ULTRA, 288 at RAPID, 632 at FAST.
- **The coils' response was measured, with no shape assumed:** it falls to half at 1.34 coil pitches (6.8 mm) with
  the pen touching, and to zero by about 2.5.
- **Two problems found, both from a weak signal:**
  - ULTRA, hovering: false touches, and about 1.7 mm off;
  - FAST, at the weakest hover: gaps (6.5 s of 30 s).
- **New: Multi-coil Read** (experimental, off by default, FAST, RAPID and ULTRA). It replaces Dual-coil Mode.
  - It works out the position with a fit of the five lines around the pen to the measured response, with the floor
    removed. It replaces the stock arithmetic; the stock routine still runs where the fit does not apply.
  - On the recordings it was steadier in 27 of 30 still cases (0.69 x stock overall):
    - RAPID resting: 0.42 -> 0.19 mm;
    - RAPID hovering: 0.76 -> 0.38 mm;
    - ULTRA hovering: 1.16 -> 0.74 mm;
    - RAPID held in the hand: 0.27 -> 0.14 mm.

    It was at most 15 % worse in 3 cases. Ruler strokes came out as straight as stock or straighter.
  - Not yet tried on the tablet.
  - Every build that has it is checked in the twin: the firmware must give exactly what its C# copy gives on every
    position call ("Multi-coil: ok" in the check).
  - It takes less time per frame than the stock arithmetic (at most 282 cycles against 544), so it fits ULTRA.
    It is not offered below 500 reports per second.
  - Running it after the stock routine with a tracked effective range per axis failed: up to 64 us per frame, too
    much for ULTRA, and the tracked range was no better on the recordings than a fixed one.
- **Dual-coil Mode is removed** (2-4x shakier on the tablet in R42), with its calibration probe and gains.
  - `--dual-coil` now says so.
  - Old calibration files still load; their Dual-coil gains are ignored.
- **The twin's pen uses the measured coil response** instead of a bell curve. Coils are described by their centre and
  effective range only (AGENTS.md rule 16).
- **Signatures:** 3 added (the two position calls and the stock position routine), 5 removed (only Dual-coil used
  them). Every other one is unchanged.

### Review of R38-R43

R38-R43 had no independent review (`AUDIT.md` covers R36 and R37). Fixes from this one:

- **A dump changed in an unknown place is refused again.** R42 let a verified build through with no warning when
  a code page differed from stock and no known edit was present (one changed bit was enough). R41 refused it, and
  R42's own design says such a dump is refused. Now it is, with or without known edits. Code pages past the end of
  a truncated input are no longer counted as changed (the truncation warning covers them).
- **A restore image that cannot be renamed no longer fails the backup.** If a cut `code.bin` / `stock.hex` was
  locked or read-only, the rename threw and a good backup was reported as "Backup failed". Now the backup counts
  and the message says the file could not be renamed and must not be flashed.
- **The firmware layout cache compares the bytes.** It matched images by CRC-32, length and build string only, so
  two different images with the same CRC-32 got the first one's addresses. A real collision is very unlikely, but
  the test suite's fake image made one.
- **Test suite:** the host suite never passed with the December fixture (R41-R43: its fake tablet ran the February
  build) or with no fixture (R42 and R43: its fake tablet dumped zeros). It passes now with both fixtures and with
  none.

## R42

### Part 3 (same day): no XP-Pen firmware in the source

- **Signatures keep working with no firmware bytes stored.** Each one is a mask plus a SHA-256 fingerprint. All 178
  firmware files on Anargon's PC resolve exactly as before.
- **The stored firmware end (3,220 bytes) is removed;** only its checksum is kept. The recovery tool restores an
  erased end from your own copy (an older dump, a backup or a built `.hex` of the same build, near the dump). With
  none, it stops and says what to add. Anargon's and Anomy's damaged dumps repair exactly as before.
- **What is left:** 29 one-instruction checks and 6 short search patterns (about 140 bytes, 1-7 instructions each).
  Before: about 4.7 KB.
- AGENTS.md rule 15: no XP-Pen firmware bytes in the source or docs.
- README: "Firmware files" (do not share dumps or built images; not affiliated with XP-Pen).
- Stray dumps, built images and logs moved out of the R42 source folder (`rev-output/R42-strays/`).
- The Java cross-check (`tools/cs2java.py`) is removed: the project depends on nothing but Windows' C# compiler.

### Part 2 (same day): wording, names, multi-coil study

- **Flash count shown as `N/100,000`** (GD32F350 datasheet minimum). AGENTS.md rule 13.
- **Hover Boost is now Steady Hover.** It trades report rate for a steadier pen high up. The setting key and
  command-line flags are unchanged.
- **File names:**
  - `[VOLATILE]_rapid.hex` (prefix separated by `_`);
  - `-rawhover` when every hover helper this speed has is off (Steady Hover, Reach Stage, Long-range Pickup);
  - Steady Hover off alone is `-noSH`.
- **Explanations rewritten for a normal person:**
  - Settings: one line on what it does, one `Cost:` line.
  - Twin measurements use Anargon's format: one block per firmware with cursor delay, report rate and frame time,
    ending with the Terminology note.
  - Calibration, recovery, flashing and backup screens are shorter too.
  - Style guide and example: AGENTS.md section 3a, rule 14.
- **Mei's four hex mods and Mily's dump are accepted as tablet dumps.** Each is undone to exact stock and builds
  normally (checked with the real files).
- **Line recorder** (measurement builds only, not in the wizard), for the multi-coil study:
  - copies all five X and five Y line sums of a frame at frame end;
  - sends them on the following even frames as pages `0xF0`-`0xF4`;
  - every copy matched a real frame in the twin, at all four paced speeds.
- **Dual-coil Mode on Anargon's tablet:**
  - no more coil-pitch jumps;
  - but 2-4x shakier than without it, so it stays off;
  - its measured gains are the default (`DUAL_DEFAULT`).
- **Model labels:**
  - R36 and R37: Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]);
  - revisions 12-23 and 24-30: Claude Opus 4.8 (Max effort);
  - model tables ordered by date.

### Part 1: "DAMAGED" on a dump with another mod's changes

- **A firmware changed by another mod is no longer called "DAMAGED".** Up to R41 any difference from stock in the firmware end (from 0x0800C188) counted as damage, and any changed code page as "damaged code bytes". So a complete, working dump with another mod's Movement Smoothing off and 1 ms report interval (Mily's) was refused with "its end is missing or wrong, so the firmware cannot start" and "THIS DUMP CANNOT BE REPAIRED". Now only an erased end is damage.
- Changes another mod made at this tool's own patch sites are recognised and undone: Movement Smoothing, ADC samples, excitation pulses, step poll and tick, AGC cap, position averages, USB report interval. They are accepted only when the result matches the verified stock firmware exactly (stored page CRCs, then the whole-image CRC-32). The flasher then builds from that stock firmware and says what it undid. A build from Mily's dump is byte-identical to one from a stock dump, apart from the per-build ID.
- A verified build changed anywhere else (not erased) is refused with what was found and why ("changed by another mod in places this tool does not know... not erased"), instead of the damage message.
- The recovery tool lists another mod's changes as "not damage", reports such a dump as not damaged, and still writes the exact stock firmware. It now also repairs an erased end on a firmware with such changes, without needing a stock copy.
- **How the erased firmware ends happen.** The tablet's own stock firmware, written only up to 0x0800C188 after the erase, leaves exactly the damage seen on Anargon's and Anomy's tablets (rev 35); reproduced byte for byte, all 51200 application bytes. Flashing a 35208-byte restore image does this: an older xppen-tools' `deco01v3-code.bin` / `deco01v3-stock.hex`, cut at its fixed 35208-byte Deco 01 V3 size, or `xppen-tools ihex --ic 902` of that code.bin. So does an older xppen-tools `update` that stopped there (rev 3's flash log). xppen-tools' full `main.bin` dumps are complete, and its current `update` writes whole images (checked on a real tablet).
- After every backup, a `code.bin` / `stock.hex` that xppen-tools wrote next to the dump and that stops before the firmware end is renamed to `*.INCOMPLETE-DO-NOT-FLASH`, and the backup says so.
- The damage message names the likely cause (a 35208-byte restore image).

## R41

- **Windows reads the tablet again.** Windows keeps the tablet's pen collection (interface 1, report ID 7) for its own pen input, so the old reader could never open it and only read the silent vendor collection ("no reports at all from 1 open collection(s)" with every firmware). The pen collection is now read through Windows' Raw Input, which needs no driver. Confirmed on a real tablet: about 400 ID 7 reports per second.
- `--hid-check` lists every tablet collection on Windows and how it is read, or why not (held by Windows, too short for pen reports). No collection is skipped silently any more.
- The HID helper functions' one-byte return values are read correctly.
- **Settings step: every filter now shows the cursor delay it adds**, while drawing and while hovering, in ms (for example "about 5 ms more cursor delay while drawing, about 10 ms more while hovering (RAPID)"). Before, it showed CPU cycles per frame in microseconds with six decimals.
- The cursor-delay measurement was wrong. Its result depended on where the simulated slide started: up to 100 ms of swing, and impossible values such as 0.1 ms. It now compares both slide directions position by position, and stays within 0.6 ms whatever the start position.
- The settings check shows drawing and hovering delay for each preset and the stock baseline (your input file), with frame and processing time in ms. Hover Boost's lower report rate is shown when it applies.
- Aggressive Position Interpolation shows its window lengths and speed thresholds for the chosen preset.
- **New setting, on by default: Accurate Pressure Readings.** It is a fix for pressure shown while the pen hovers (reported at 800 reports/s). The frequency sweep, which gives pressure, tip and buttons, now uses stock's reading length, spread over frames: QUICK the whole sweep per frame, FAST 3 steps, RAPID 1. With the short readings the pen kept ringing from one sweep step into the next. In the twin that read a hovering pen as pressed and missed light presses at RAPID; stock-length readings did neither. The cost is fewer pressure updates per second (RAPID 100 instead of 300, FAST 188 instead of 375; stock about 220), shown on the Settings screen. ULTRA's 1 ms frame has no room for it. Turn it off with `--no-accurate-pressure`. Not yet checked on a real tablet.
- A frequency sweep that crosses a hover-stage switch is now dropped instead of mixing two reading lengths (RAPID and ULTRA; twin: up to a quarter of weak-hover sweeps at ULTRA).
- Tablet signal thresholds also report the pen frequency while hovering and any pressure shown with the pen in the air, measured with the final build's timing. This is the hardware check for the frequency readings.
- Aggressive Position Interpolation can no longer make one stray RAM write after power-up (its slot index was used before it was masked).
- Checked on a real tablet (RAPID): calibration through the Windows reader at 800 reports/s; no pressure shown in the air, with a pen that hovers about 2 kHz under the pressure threshold; pressure updated 100 times a second; 702 reports/s at close hover.
- Replacing this tool's measurement firmware no longer makes a new 3-minute backup: while its calibration readings are arriving, the folder's first backup of the real firmware is used for the checks.
- Calibration steps are worded more plainly: "no pen" for the first step, "middle of the drawing area" (below the button bar), an everyday reference for hover height, and the LED cue for the detection limit.
- Dual-coil Mode's description now gives the real-tablet result: less X jitter, but the Y position jumps by about one coil pitch (5 mm).

## R40

- The calibration readiness check no longer ends calibration when no readings arrive. It reopens the tablet and checks again by itself (three times), then asks: Enter = check again, `f` = flash the calibration build again (one more update; this run's backup is reused), `d` = continue with the preset defaults. A tablet that cannot be opened gets the same choice.
- The readiness message names what was actually seen: no reports at all, other report IDs, ordinary pen reports, or a reader failure. It no longer blames ID 2 when nothing arrived.

## R39

- Step 1 of the wizard offers this run's tablet backup as the firmware file (`b`). Choosing it waits for a backup that is still being read; a failed or missing backup is refused with a reason.
- Make the host check for separate stdout/stderr lines independent of which stream arrives first (it failed once under load).

## R38

- Restore the AI DISCLOSURE box, shown at the top of every screen and first in every command-line run. AGENTS.md rule 12 forbids removing it.
- Let the startup dump run in the background while the wizard steps continue. It is waited for only when needed: its own file chosen as the input, calibration, flashing, the recovery tool, and exit.
- Make one backup per run. A flash uses the run's successful backup and waits for a running startup backup instead of starting a second one. Return-to-stock keeps its verification read.
- Run interactive xppen-tools commands in the program's own console, so the typed UPDATE confirmation can be answered. On Windows the previous hidden console made that prompt unanswerable.
- Keep helper stdout and stderr on separate lines and show a waiting prompt last, below log lines flushed after it.

## R37

- Use forward-only prompt, warning and live-measurement output to prevent cursor redraws from overwriting menu text or interfering with input. The exact repeated backup message reported against the previous binary was not reproduced from the RC36B source.
- Own xppen-tools as a child process and clean it up when the application exits or receives Ctrl+C.
- Add read-only `--hid-check` and `--pen-resonance` diagnostics. Windows HID uses built-in system APIs, without WinUSB or a replacement driver.
- Name generated firmware by speed, omit enabled defaults, abbreviate changed options, and add `[VOLATILE]` whenever Volatile Changes is enabled.
- Separate Tablet signal thresholds, tracked pen resonance measurement and experimental Pen frequency tuning. Threshold tuning makes no dual-coil or multi-coil recommendation.
- Consolidate documentation and preserved model history under `DOCS/`; keep source handoffs free of prebuilt executables. Users build locally or download a complete ZIP from GitHub Releases.
- Correct R36 model attribution to Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), from the user's confirmation. Record R37 as Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), verified in the coordinator and delegate session metadata.

## R36B-RC

- Require the packaged runtime-license version to match the verified runtime version.
- Resolve package paths before boundary checks to prevent staging inside source or verification inputs.
- Require complete regression receipts from the current invocation, with matching logs, output hashes and counts; isolate each run's scratch directory.
- Validate the tablet's USB endpoints and HID reports in both standalone checks and the build gate. Poll only advertised interrupt IN endpoints in the host model.
- Reflow documentation paragraphs and list items, correct design wording and update release examples.

## Revision 36 candidate

- Select the telemetry report route in calibration firmware and require fresh signal, position and pressure data before timed measurements.
- Keep stock scan frequency by default. Validate saved calibration profiles and cap measured pickup/drop thresholds at their preset defaults.
- Track possibly installed measurement firmware before an update attempt, retain that state after failure and clear it after successful normal restoration. Classify measurement updates explicitly instead of inferring from paths.
- Defer bootloader maintenance from measurement firmware to the final normal build.
- Capture a complete startup backup in the background, retain every full capture and require a fresh successful capture before flashing. Preserve the previous valid dump when a new capture fails.
- Separate dry-run previews from device commands; preserve external process status, quote full paths and reject closed-input acknowledgements.
- Reject malformed HEX input, enforce short-branch bounds, isolate emulated flash and fail required checks when measurements are missing.
- Verify recovery/reset restoration before removing its stub or replacing the restoration application.
- Limit emulator profiles to implemented 64 KB flash / 8 KB SRAM configurations and strengthen reader-session cleanup.
- An intermediate packaging attempt consolidated the repositories. The corrected R37 handoff restores the approved separate-project structure while retaining the behavioral fixes.

The counter page, record format and USB string 173 protocol are unchanged. Physical tablet qualification, the full December fixture run, native Linux/macOS execution and hosted CI remain separate verification requirements.

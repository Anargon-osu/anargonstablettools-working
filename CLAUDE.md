## PR1: Coil Gains (branch `coilswitch-hotfix`; R46 / V1.1 once merged) - read first

<img src="misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

Claude Opus 5.5 (Anthropic), 2026-11-22, on the V1.0 source. Pull request PR1; merged, the project becomes R46 (V1.1)
(AGENTS.md section 4). Full record: `history/claude-pr1.md`. Analysis scripts and recordings: `Claude-workarea/experiments/precision` (coilgain*.py,
coilk.py, coilpk.py, coildir.py, coilzone.py, coilruler.py, coilstrength.py, coiltune.py, coilba.py) and
`Claude-workarea/rev-output/hw45/coilgain` (ab = 100 % test, ab60 = 60 % test).

- **The slanted-line error is each coil's own reading strength** (Anargon: slanted lines keep 0.04-0.07 mm, up to
  0.45 mm p-p). Fitted on the R43 strokes' raw line sums: X coils differ 1.2 % sd (a strong / weak pair every 8
  coils: 10 / 11, 18 / 19, 26 / 27), Y 0.7 %; the same along each coil (three zones r 0.90-0.96) and between
  recordings. Per-coil centres and effective ranges predict nothing. Gains learned from the 11-13 / 16 / 17 ruler lines
  match the strokes' (X r 0.92, Y r 0.84). *(hardware recordings offline)*
- **Coil Gains** (`atf_coilgain.cs`, `cgain`, in Misc. Optimizations, default on; ULTRA, needs Window Scale, not with
  Multi-coil Read or in the lab build): inside `ws_x` / `ws_y` each line `((line - floor) x G_k) >> 8` before the
  stock routine. Twin: the stock routine's arguments equal the C# copy on every call; `--no-coil-gains` gives the V1.0
  image byte for byte (build id aside); other presets unchanged; 528 regression checks pass with both dumps. *(twin)*
- **On the tablet it takes off exactly the predicted error, but the size of that error changes by session**: 0.7-2.4
  x the prediction on 11-13 / 16 / 17, almost none to 0.6 x on 11-22. At 100 % that day it over-corrected (fixed error
  0.031 -> 0.044 mm on one line); at 60 % (applied 0.65 x) fixed 0.021 -> 0.009 mm, real 0.037 -> 0.017 mm on a 30 deg
  line. Not explained by pressure, a filter lag between the pass directions, tilt or position along the coil.
  Strength 58.9 % (`CoilGains.STRENGTH` = 589 per mille): in 0.1 % steps the lowest worst session (each 0.78-0.91 of
  its error, none worse); the lowest mean (78.6 %) would have made 11-22 worse when that session was held out. Left:
  why the size changes between sessions; a hover measurement (the gains were fitted with the pen touching).
  *(hardware, cursor; recordings offline)*

## R45 - read next

R45 (Claude Opus 5.5, Anthropic) is the last planned revision before the project goes public; everything is in
R45-rc6. After R45 each delivery is a pull request (PR1, PR2, ...) and each merge is the next revision (PR1 -> R46; AGENTS.md section 4). Full record: `history/claude-rev45.md`; data and tools in `Claude-workarea/rev-output/hw45` and
`Claude-workarea/experiments/noise45`. Findings:

- **R45-rc5 (4): Misc. Optimizations, from Anargon's tablet sessions** (`Claude-workarea/experiments/precision/RESULTS.md`).
  One setting (`misc`, default on) switches Switch Match, Window Scale, Coil Linearization and Dynamic Anti-Chatter;
  firmware order SWM > CL > ALPI > DAC. Chosen in a 20-minute osu! session with the Filter Arranger switching orders at
  pen lifts (DAC 0.38x the flicks; Jump Rejection 3.0x and Hull Mark + Hull Clamp 4.9x failed and are left out), then
  a ruler session. Coil Linearization uses table B (learned on the three rc5 ruler lines): on a line it was not learned
  on 0.088 / 0.059 mm (sd, forward / back) against 0.107 / 0.073 for the first table; along an axis 0.02-0.04 mm. One
  table per axis is enough (the cross-axis part is 0.01-0.02 mm). Left: Coil Linearization makes hover worse (stand,
  about 3 mm: 0.26 / 0.32-0.35 mm with it, 0.21-0.24 / 0.26-0.30 without; fix: off at hover or a hover table), and
  light pressure doubles the X coil wave. *(hardware, cursor)*
- **A report's time is an int of microseconds since the reader opened** (`P.HidCollect`), so it runs out 35.8
  minutes after the open: a reader stays open for one recording only (`PenReader.Open` reopens). A reader kept open
  all evening cost Anargon a session of ruler recordings (scored afterwards with guessed times). Test a recorder for
  longer than a real session before handing it over. *(hardware)*
- **R45-rc5: Coil Linearization holds up on the tablet** (history Part 17). A fixed table per place inside a coil
  (64 knots per axis, learned by code density from ruler recordings), memoryless, before ALPI (after ALPI it failed:
  ALPI delays the coil wave about 5-6 ms, so it missed a moving pen; offline replay, forward-minus-back at 100 mm/s
  0.08 -> 0.01 mm rms); setting `clin`, part of Misc. Optimizations (one tablet and pen, measured at ULTRA). Ruler through the cursor, ULTRA + ALPI, coil-locked error X / Y: the
  learned line (matched speeds) 0.26 / 0.14 -> 0.15 / 0.06 mm; two lines it was not learned on: X 0.20 -> 0.08 mm,
  Y 0.14 -> 0.05 and 0.08 -> 0.065 mm; steps and noise not worse. Left: 0.05-0.17 mm sd that follows neither axis's
  coils (surface wear, ruler, hand). *(hardware, cursor)*
- **Local models are not enough for this project** (AGENTS.md section 2a). On Anargon's 8 GB GPU (vibeslop swarm)
  OmniCoder-9B, Devstral Small 2 24B, Qwen2.5-Coder-14B and seventeen other 3.7B-9B models failed the Dynamic Anti-Chatter
  routine (one pass in four tries for OmniCoder; one pass for Devstral taking over a stuck task) and the ALPI replay
  (compile errors, wrong structure: Claude rewrote it). Anargon: a 32B multi-agent session is the minimum.
  **Interface code is the exception:** Devstral (after OmniCoder failed two rounds) wrote the Filter Arranger screen
  (`atf_arranger.cs`) that passed all 15 twin checks, against Claude's guide and `ArrangerOps`; OmniCoder alone wrote its
  second layout (like the settings screens) in one round. In the product unchanged. *(swarm runs, 2026-11-15 and 16)*
- **R45-rc5: four other position ideas fail on the tablet** and are removed (Anargon: keep only the best one):
  Pen-Anchored Crossfade, Midpoint Gaussian and Alternating Blend make real 1.3-2 mm steps (MG and AB 12-15 a second)
  and double the coil-locked error; the Cross-Axis Table changes nothing measurable. Alternating Blend was the best in
  simulation, and a per-pressure centre gain beat Window Scale in simulation but not on the drags: the simulation's
  single cause (a low centre line) is too simple. *(hardware, cursor; simulation; drags offline)*
- **R45-rc5 Dynamic Anti-Chatter** (`atf_antichatter.cs`, `dac`, default on): a frame more than 0.10 mm off the line
  through the two before is pulled back, softly up to 0.20 mm; drags X chatter (99 %) 0.51 -> 0.28 mm at 0.12 ms mean
  delay; the repeatable error is unchanged. Gemini's PR #1 option A (a window-offset table) was worse than Window
  Scale on the drags (X step 0.072 -> 0.182 mm). At ULTRA the CPU never sleeps (twin). *(drags offline; twin)*
- **Reference recordings:** every pen-movement recording (ruler lines, drags, R43 strokes, the ALPI training) is in
  `referencedata/` with its format, firmware and readers.
- **R45-rc4: the cursor jumps at a switch because the first line read is cold** (history Part 16). Each reading
  drives the pen through the centre coil and listens; every line but the first is read while the pen still rings
  from the line before. Tablet, fast stage, pen still: per line in the order read X -0.44 / -0.05 / +0.03 / +0.07 /
  +0.08, Y -0.16 / -0.06 / +0.01 / +0.06 / +0.06; it changes with the read length, the drive step and pressure. So
  each coil window, read length and drive step has its own position bias: 45-degree drags jump 0.24 mm (X) at window
  switches, hover lifts 0.67 mm fast -> Steady Hover. *(hardware)*
- **R45-rc4 (2): the switch step is a stretch of the position inside the window** (history 16.8). Both windows at a
  switch are off by k times the pen's distance from their centre coil, so every switch jumps k coils. On the drags
  k follows pressure: X +0.043 up to 10000, -0.047 above; Y -0.03 .. +0.09. Line-level fixes (cold-line gain, no
  cold line, Gaussian fit, centre gain, learned slot gains) did not help: 1 % on one line moves the jump 0.15 mm.
  **Window Scale** (`atf_winscale.cs`, `wscale`, ULTRA, fast stage) multiplies the offset from the
  centre coil by 1 + k(pressure), memoryless: X jumps 0.24 -> 0.08 mm offline (0.15-0.17 cross-validated); with
  Switch Match 0.04. Switch Match is gated (15 mm/s for a window switch; no switch of that axis in the 16 frames
  before), so a still pen is left alone. The twin cannot show Window Scale (no cold reading there).
  Not fixed: the wave across each coil. *(hardware recordings offline; simulation; twin gate)*
- **R45-rc4 (2): true position error, ruler test** (target 0.1 mm, not met): one 15 cm ruler line, 5 passes each
  way, through the cursor (ULTRA, Switch Match on), `experiments/noise45/ruler.py`: repeatable error 0.68-0.79 mm p-p
  over 114 mm (slow part 0.25-0.32, coil-scale part up to 0.35 mm, sd 0.1), forward vs back up to 0.30 mm, steps 99 %
  0.21 mm. Freehand drags offline: the coil-scale wave is the same with no switch handling, Switch Match or the
  halfway point (0.15 mm X, 0.20 mm Y p-p). Hand scatter 0.16 mm sd limits it; one line only. *(hardware, cursor)*
- **Switch Match without gates failed: slow drift at a still pen where the window flips** (a coil midpoint): each
  flip turned a few frames of noise into an offset lasting up to 500 ms, which ALPI passes. Tablet, read-only through
  the cursor, X at a midpoint: sd 0.30 mm, 2.7 mm p-p, periods 160-380 ms; simulation 0.01-0.08 -> 0.08-0.13 mm sd.
  The gates above keep a still pen out of it. *(hardware, cursor only; simulation; twin)*
- **R45-rc4 Switch Match** (`atf_switchmatch.cs`, setting `smatch`, part of Misc. Optimizations): at a switch the reading is matched
  to a straight line through the last 16 positions (past only), the match fading over half a coil or 500 ms; not
  at over 150 mm/s or on a bend; at most 1.5 mm. Offline on the recordings: switch jumps 0.24 -> 0.036 mm (X),
  hover 0.67 -> 0.05 mm. It removes the step, not the bias (the step becomes a ramp); the source fix (per-line gain
  correction per read length, drive step and pressure) needs measured gains. On the tablet at ULTRA + ALPI.
  *(hardware recordings offline; twin gate)*
- **R45-rc4 dynamic Steady Hover:** the rate falls in 25 us steps of frame time (ULTRA 1000, 930, 909 ... 482, 426/s,
  reach), each step kept until its noise reaches ULTRA's fast stage at 1.12 x its drop level; the step back up uses
  the ratio measured on the way down. Probe frames are gone. *(twin; the first coarse rc4 ladder on the tablet: no
  switches with a still pen)*
- **R45-rc4: without the holds, the twin's RAPID hover-pressure case is chaotic** (code layout alone flips it 0 <->
  779 of 801 reports); the suite reports it instead of checking it. *(twin)*
- **ULTRA's still-pen noise has two causes, both "re-centre with no stay-put margin".**
  - **Drive-step hop (main cause, both axes):** after every sweep the firmware moves the scan's drive step to the
    strongest sweep step (`0x08007840`, C[3] += peak - 2). ULTRA's short sweep readings are polluted, so the pick
    hops at random; each hop changes pen strength about 30 % and moves the position about 0.5 mm.
  - **Coil-window flip:** the scans drive the pen through the centre coil, so the centre line reads 2.5-2.8 % low
    (both axes), and between two coils the window re-centres in most frames.
  - **Fixes (settings, default on):** Frequency Hold (`fhold`: one-step moves only after 8 sweeps agree,
    `Sched.HOLD_AGREE`) and Coil Hold (`chold`: a one-coil re-centre needs a margin of 3/32,
    `Sched.COIL_MARGIN`). Both on, silent build on the tablet: X 0.116 / Y 0.083 mm against 0.183 / 0.119.
  - *(hardware: experiments/noise45, lines-ab.csv, lines-hold.csv; twin: Y midpoint flips 50 % -> 0)*
  - **Bootloader Timer Off (SysTick off):** the vendor bootloader leaves a 1 ms SysTick interrupt at priority 0 running
    into the firmware; `Sched` clears `SYST_CSR` in its search-restart routine (boot and every pen loss). It caused
    ULTRA's midpoint bursts (history Part 13). The twin starts at the firmware, so it never has SysTick running.
- **ALPI (APIP renamed) on the tablet:** Anargon's training at ULTRA (602479 readings + a 3597-reading hold, report
  mode 4) is `Apip.Fallback`. Still pen at the centre, cursor shake against no filter: Tracker 0.08x / 0.06x,
  Robust 2 trackers 0.18x / 0.20x, the R43 build 0.28x / 0.21x. The Tracker is the default with Full calibration.
  *(hardware)*
- **Switching the LED during a scan step adds noise** at ULTRA (X 1.20x, Y 1.38x) and FAST (2.0x / 2.2x), not at
  QUICK; changing it only at the frame restart is clean (0.96x / 1.03x). *(hardware)*
- **Raw noise is flat from 300 to 1000 reports/s** with the pen touching; above 1000/s scan timing is the cap
  (about 1700-2500/s). *(hardware, calculation)*
- **Multi-coil Read gives no noise benefit with ALPI** in R45's hold setup: spread after ALPI X 0.97-1.04x,
  Y 0.79-0.99x of the stock arithmetic, jitter X 1.11-1.16x, Y 1.02-1.11x. *(hardware recordings, offline replay)*
- **The shake just before a coil midpoint** (Anargon saw it as vertical, Y; it is on both axes) is the coil-window
  flip. On the training data, recorded before the holds, it is 2.4-2.9x the quiet noise and 0.3-0.4 mm wide. A
  simulation with the centre line 2.7 % low reproduces it (2.6-2.9x, about 0.4 mm), and Coil Hold brings it to about
  1.3x. A per-position noise model on top of ALPI gave no gain in replay, so the background training Anargon
  proposed was not built. *(hardware recordings offline; simulation)*
- **Calibration with ALPI:** Standard puts only three values into a build (the fast stage's pick-up and drop, and
  Jump Rejection's margin); ALPI replaces Jump Rejection, so Full with ALPI skips Standard's guided steps. *(code
  analysis)* **R45-rc2:** the ALPI training measures the pick-up and drop levels in the background instead
  (`Cli.FromTraining`): its firmware records each sample's detection signal and frequency (`Apip.SAMPLE` = 12 bytes,
  ring at `0x20001100`), and keeps the fast stage down to 40 / 24. The weakest fast-stage hover readings are where
  Steady Hover takes over, not the detection limit, so the levels can be higher than Standard's, never above the
  preset's. *(twin; not yet on the tablet)*
- **Standard has three steps** (light touch, normal hover, highest hover). A no-pen step and a slow lift failed: the
  first measured nothing (no readings without tracking), the second fed nothing into a build and read the coils'
  floor (72) in both of Anargon's RAPID runs. The readiness check's light touch is the first step's. *(code analysis,
  hardware logs)*
- **Input firmware made by other mods or by this tool is set back to exact stock.** Mei's and Mily's mods are named,
  with each change's instruction or data byte, address and values (`Repair.HowLines`, `MOD_EDITS`). Every build
  carries an undo record (`atf_revert.cs`, "ADTU"); older builds are set back from a stock copy near them.
  *(core suite)*
- **`NiceAesth/xppen-dumps` (R42 note below):** its maintainer only wants the December dump, so Anargon is removing
  the February files there. Do not point users to it for a February dump. *(Anargon's report, 2026-11-12)*
- **R45-rc3: the low hover shake had two causes** (history Part 14).
  - **Hover-stage flapping:** at low hover the firmware ran 24 fast frames and 3 Steady Hover frames, about 37 times
    a second, 0.12-0.2 mm at each switch. A slower stage reads the pen stronger (Steady Hover 2.3x on the tablet,
    reach about 6x) but was compared with the fast stage's level. Each stage now steps up at its own level
    (`Build.StepUpLevel`: the fast stage's level x ringup(stage) / ringup(fast) x 1.25, ringup(n) =
    1 - exp(-n pi / 300)). *(tablet recordings; fix twin only: no flapping, hover hold 16.8 -> 22.0 mm)*
  - **Tilt frames:** at hover in the fast stage, 3 of the 4 tilt frames in every 16 move Y +0.21 / +0.44 / -0.20 mm
    (X -0.17 on the first). Tilt Frame Skip (`tiltskip`, default on) plans them as normal frames there; tilt keeps
    its last value. *(tablet recordings; fix twin only)*
- **R45-rc3: the reach stage keeps a tracked pen down to 0.6 of its drop level** (`Build.REACH_HOLD`; pick-up
  unchanged). Hover hold at ULTRA 22.0 -> 25.8-26.4 mm, no pick-ups without a pen; more reach samples were worse
  (48: 20.4 mm). *(twin)*
- **R45-rc3: the rate without pressure, buttons or tilt, built:** 1140 frames/s (5 + 5 lines) and 1545 (3 + 3), not
  the modelled 2000; USB carries 1000 reports/s. Build and run a limit before quoting it. *(twin)*
- **R45-rc3: the G640 Rev B dump is another chip family** (flash at address 0, application at 0x6000, 59
  interrupts, none of the GD32F3x0 peripheral addresses); the worked example of `DOCS/porting-guide.md`.
  *(code analysis, `tools/survey.py`)*
- **R45-rc3: ALPI comes only with Full** (Anargon). Skipping the training in Full = the defaults (Anargon's
  training via `ApipCli.Skip`, the fallback levels, the stock frequency), labelled as not measured on this tablet;
  without 12-bit Readings there is no fallback training and the build is made without ALPI. The setting stays off by
  default; on the command line only `--apip FILE` turns it on. *(code; scratch harness for the skip choice)*
- **R45-rc3 rename:** Anargon's Tablet Tools (`anargons-tablet-tools`, `AnargonsTabletTools`); the state folder
  `AnargonsDeco640Tools` is still read until the new one exists.
- **R45-rc3, Part 15:** the tablet runs the final rc3 ULTRA + ALPI build (counter 86). Developer documents are
  written out in full for a reader who programs but knows no assembly (AGENTS.md rule 17): `FIRMWARE_DESIGN.md`,
  `porting-guide.md` (G430S example), the two tutorials, `otd-plugin.md` (proposal). Packages can add firmware changes
  (`IPatch`, `Build.AddPatch`, `Layout.AddSignatures`), and a package fits the options to its tablet
  (`Build.AddSetting`, `Build.HideSetting`, `Build.Option`): a package is a tablet's patcher, raw C# source. Tests: every group its own process, all capped together at
  50 % of the CPU unless `--uncapped` (always on GitHub); test dumps cloned from `ADT_TEST_DUMPS_REPO` into
  `tests/work/dumps-repo/`.
- Not yet on the tablet: Coil Hold with the pen at a coil midpoint (in the A/B the pen sat off the flip zone), and an
  ALPI training made with both holds on.

## R44 - read next

R44 (Claude Opus 5.5, Anthropic) makes Tablet Calibration simpler, to Anargon's spec. Full record:
`history/claude-rev44.md`. Findings:

- **The measurement firmware's LED marks each step's target.** The PC sets the target and "recording" through USB
  strings 176-179 (`176 + 2 x target + recording`), answered only by the calibration-pass build with
  `ADTZ1:<target><recording>`. State at RAM `0x20000F54` (state, LED, state ^ 0xA5). Each pen report decides the
  LED before its bytes are replaced by telemetry: off without a pen or outside 7 mm of (8000, 4500) on either axis,
  on inside, blinking while recording. *(twin: all four fast presets, pen at 0, 5 and 9 mm from the centre)*
- **The stock GET_DESCRIPTOR handler does not use strings 176-179** (its index chain: 4, 5, 20-31, 98, 100-105,
  109-111, 120-124, 131, 168, 200). *(code analysis)*
- **The twin's USB host must start a request from the main loop.** `VirtualHost` runs the USB interrupt to
  completion at once; started inside another interrupt, the emulator finishes that one too outside its bookkeeping
  and takes no more interrupts. `Cli.TwinSource.ToMainLoop`. *(twin)*
- **The slow lift ends when the firmware loses the pen** (`CalSource.CollectUntilLost`, at most 60 s), the light
  touch is under 4096 (`Cli.LIGHT_TOUCH`), and the steps warn that there is no cursor.
- **Folders and patcher packages (R44 Part 2, Anargon's flow).** `Folders.Make` creates `import`, `export`, `dumps`
  and `patcherpkgs` next to the program at start. `Packages.Select` shows "Choose the patcher:" only when
  `patcherpkgs` holds a package. Guards: C# source only (`package.ini`, `src/**.cs`, README; anything else is
  skipped with the reason), the ZIP read in-house (stored and deflate, CRC-checked, no ZIP64 or encryption,
  512 files, 16 MB), `csc.exe` from the running .NET Framework's folder (never PATH), and a typed approval per
  content SHA-256 (`Store`: `package_<sha>`). Not done: signing; and a package runs in-process, so this tool cannot
  force it through the gate. `DOCS/patcher-packages.md` asks authors to use `Cli.Gate`, `Cli.BeforeFlash` and
  `Cli.FlashSteps`. *(host suite)*
- **Anargon's tablet result (2026-11-10):** ULTRA with 12-bit Readings, without Multi-coil Read, is stable and
  matches the earlier 500 Hz performance. *(hardware, Anargon's report)*
- Not yet on the tablet: the LED zone. Not built: Algorithmic Position Interpolation (APIP), from the same spec
  (R43 history Part 12).

## R43 - read next

R43 (Claude Opus 5.5, Anthropic) ran Anargon's tablet tests with the R42 line recorder, measured how the coils respond,
and replaced Dual-coil Mode with Multi-coil Read. Full record: `history/claude-rev43.md`; data and tools in
`Claude-workarea/rev-output/hw43` and `Claude-workarea/experiments`. Findings:

- **The coils sit on an even 0.2 inch (5.08 mm) grid**, the same at ULTRA, RAPID and FAST within 0.24 mm, in reported
  mm (X = 0 left, Y = 0 top):
  - X: 32 coils, coil k at about 163.53 - 5.080 k (coil 1 at the right);
  - Y: 19 coils, 96.42 - 5.083 k (coil 1 at the bottom).

  The second coil from each edge is pulled 0.3-0.6 mm; the outermost is at or past the reported area's edge.
  *(hardware: ruler strokes, coil centre = where both neighbours read the same)*
- **Every line sum has a floor of about 72 per conversion** (216 at ULTRA's 4 reads, 288 at RAPID's 5, 632 at FAST's
  10; the sum holds one conversion fewer than the reads). It was read from lines of coils that do not exist, past the
  edges: stable to 0 (RAPID, ULTRA) and +-8 (FAST). *(hardware)*
- **The coils' response, measured with no shape assumed** (Anargon: the coils may be multi-layered and not circular;
  AGENTS.md rule 16): 1 at the centre, 0.92 at 0.5 pitch, 0.70 at 1, 0.40 at 1.5, 0.115 at 2, about 0 from 2.5.
  - The effective range (half response) is 1.34 pitches (6.8 mm) at touch, the same on both axes and within 5 % at
    FAST, RAPID and ULTRA.
  - A bell curve fits it badly: it pushed the fitted floor negative.

  *(hardware: strokes, each copy normalised by its five-line sum)*
- **Line noise depends on where the pen sits, not only on the line's size.** Weights from still-pen noise made strokes
  noisier, so equal weights are used. *(hardware)*
- **RAPID and ULTRA barely see a hovering pen:** their lines sit on the floor with only 20-50 on top. ULTRA then
  reports false touches (pressure up to about 5000) and is about 1.7 mm off. FAST, at the weakest hover, lost 6.5 s
  of 30 s in gaps. *(hardware)*
- **Multi-coil Read** (setting `multicoil`, experimental, off by default; FAST, RAPID and ULTRA; `atf_multicoil.cs`).
  - **What it does:** replaces the stock position arithmetic with a fit of the five lines to the measured response,
    with the floor removed and the effective range fixed at 17/16 of the measured one. Where the fit does not apply
    (peak off the centre line, a neighbour above the centre line, a centre line less than 8 above the floor) it
    hands the call to the stock routine unchanged.
  - **Offline on the recordings, with the firmware's exact integer arithmetic** (`experiments/mcint.py`):
    frame-to-frame noise 0.69 x stock (geometric mean of 30 still axis recordings), lower in 27, at most 1.15 x stock
    in 3; the biggest gains while hovering (RAPID X 0.76 -> 0.38 mm, ULTRA X 1.16 -> 0.74 mm). Strokes as straight
    or straighter. *(hardware recordings, offline)*
  - **Cost: less than stock, so it fits ULTRA.** Single-stepped alone on 6000 inputs: the fit path at most 282
    cycles, the stock routine 503 mean / 544 most; the gates add at most 31 cycles per axis where the stock routine
    runs. Charged 1 us. Running it after the stock routine failed: 59.2 us per frame (charged 64), too much for
    ULTRA. *(emulator; the core suite checks it)*
  - **A tracked effective range failed to gain:** per-axis tracking gave 0.73 x stock, tracking in the new arithmetic
    0.72 x, the fixed 17/16 range 0.69 x. *(hardware recordings, offline)*
  - **Twin check:** the firmware equals its C# copy on every position call at FAST, RAPID and ULTRA, with February
    and December (the gate runs this on every such build), and on all 6000 single-stepped inputs. *(twin)*
  - **On the tablet at ULTRA** (`history/claude-rev43.md` Part 7): the firmware runs it (equals the C# copy on
    100 % of copies away from the edges). On the same frames, quieter than stock in 8 of 10 still axis results
    and on both axes at every hover (0.62-0.85x); higher at the tip in the middle, Y (1.40x), and at the edge, X
    (1.17x). Strokes and RAPID not yet tested. *(hardware)*
  - **With 12-bit Readings its X is the limit at a low hover** (`history/claude-rev43.md` Part 9): 0.34 mm per
    second against the stock arithmetic's 0.19 on the same lines; its X results fall into clusters 0.2-0.5 mm apart.
    Y is better than stock (0.13 against 0.15). *(hardware)*
- **12-bit Readings** (R43 Part 9): the stock sampling loop clears the low 3 bits of every reading (`bic #7`,
  0x08004720 February); the setting replaces it, and raises every pick-up, drop and boost level by 4
  (`Build.FULLRES_BIAS`) because all levels, calibration files included, are in rounded readings. On the tablet at
  ULTRA, low hover: cursor noise 0.38x (Y) and 0.86x (X) against rounded readings, per-second medians. *(hardware)*
- **The stock position routine's C# copy (`MultiCoil.Stock`) differs from the firmware on degenerate input.** With all
  lines 0 the firmware gives the centre coil, the copy 0. On random large sums the divides differ in sign: 3392 of
  200000. On real line sums they agree. So the C# copy of Multi-coil Read (`MultiCoil.Calc`) never needs it: where
  the fit does not apply it says so (`MultiCoil.STOCK`) and the twin checks that the firmware's stock routine runs.
  *(emulator)*
- **Dual-coil Mode is removed** (R42: 2-4x shakier on the tablet), with its probe, gains and calibration keys.
  *(decision, Anargon's direction)*
- **The twin's pen uses the measured response** at an effective range (1.34 pitches at touch), not a bell curve. The
  gate passes on every preset. With the bell curve, Multi-coil Read looked 4x shakier at FAST in the twin; with the
  measured response it is as steady as stock or steadier. *(twin)*
- **Review of R38-R43** (none had an independent review; `history/claude-rev43.md` Part 6). Fixed:
  - **R42 let a dump with an unknown code change through** when no known edit was present (one flipped bit was
    enough), with no warning; R41 refused it and R42's design says it is refused. `Build.ReadInput` now refuses any
    `pagesOther`. *(offline: `unkpage.cs` on February stock; core suite)*
  - **A restore image that could not be renamed failed the whole backup** (the rename threw). Now reported, and the
    backup counts. *(core suite)*
  - **`Layout.For`'s cache matched by CRC-32 only;** it now compares the bytes. *(host suite: its fake image shares
    the stock CRC-32)*
  - **The host suite never passed with the December fixture or with none** (its fake tablet ran February, or
    dumped zeros); it does now. *(host suite)*

  Checked and found right: the R43 assembler encodings, the emulator instructions Multi-coil Read uses, Multi-coil
  Read's gates, fit, twin check and cost, the twin's pen model, `Repair.Examine` / `FindEnd`, the fingerprint
  search, and the R42 line recorder (RAM `0x20001040`-`0x2000106F` overlaps nothing; pages match their layout).
  *(code analysis)*

## R42 - read next

R42 (Claude Opus 5.5, Anthropic) found why a complete, working dump with another mod's changes was called "DAMAGED",
and how the erased firmware ends of rev 35 happen. Full record: `history/claude-rev42.md`. Findings:

- **"DAMAGED" was this tool's false positive.** Mily's February dump differs from stock in 32 bytes, all of them
  normal: the settings page (normal after an update), the dead-band coefficients set to 0 and the two bInterval
  literals 3 -> 1 (byte for byte this tool's own Movement Smoothing off and 1 ms patches). R41 treated any difference
  in the end (from `0x0800C188`) as damage and any changed code page as "damaged code bytes". *(dump analysis,
  reproduced with R41)*
- **xppen-tools' `main.bin` dumps are complete; its restore images may not be.** The current xppen-tools flashed and
  read back the R41 measurement build in full, 3756 bytes past `0x0800C188`, CRC-checked against the device.
  - **The four dump files:** `xppen-tools dump` also writes `ic902-code.bin` and `ic902-stock.hex` ("application
    restore image").
  - **Older releases** (`deco01v3-*` names) cut both at a fixed 35208 bytes (`0x0800C188`), the only Deco 01 V3 size
    its `patch` command accepts. `ihex --ic 902` converts such a code.bin and refuses a `main.bin`.
  - **Current release:** cuts at the last non-0xFF byte, so on a damaged tablet the restore image repeats the damage.

  *(R41 hardware logs, xppen-tools strings and offline runs)*
- **The rev 35 damage is the tablet's own stock firmware written only to `0x0800C188`.** Erase plus a 35208-byte image
  of that build's code reproduces Anomy's and Anargon's damaged dumps in all 51200 application bytes. Two causes give
  these bytes: flashing a cut restore image (the old bundles on Anargon's PC hold several), or an older xppen-tools
  `update` that stopped there (rev 3's log). Mei's four hex files are complete. xppen-tools' verification passes either
  way. *(simulation against the dumps)*
- **Fix: edits vs damage.** `Repair.Examine` undoes changes at this tool's own patch sites (stock values per build in
  `EDIT_AT` / `EDIT_STOCK`, checked against both stock dumps by the core suite).
  - **Edited:** a page counts as edited only if it then matches its stored CRC. The flasher builds from the
    reconstructed stock only when the whole-image CRC-32 matches.
  - **Damaged:** only an erased end.
  - **Changed elsewhere:** refused with an accurate message.
  - **Recovery tool:** reports such changes as "not damage".

  *(core suite with February and December stock)*
- **Cut restore images are renamed after every backup.** A `code.bin` / `stock.hex` next to a dump that stops before
  the firmware end is renamed to `*.INCOMPLETE-DO-NOT-FLASH`. *(host and core suites)*
- **How stock computes position (R42).** `0x08005432` takes the five line sums around the peak (centre-2..centre+2),
  the centre coil and 1018. It averages:
  - a 3-line parabola (centre-1, centre, centre+1);
  - a 4-line ratio that adds centre-2 or centre+2 on the pen's side.

  If the two differ by more than one coil, it uses the parabola alone. A C# copy matches the firmware on every frame
  (`experiments/linesnap.cs`). *(code analysis, twin)*
- **Line recorder (R42):** measurement builds can send every line of a frame (`cfg.lineRec`, pages `0xF0`-`0xF4`,
  `Sched.LR_ADDR`). It copies at the `tilt` hook (frame end), because at report time the X lines are already cleared
  in about 37% of reports. *(twin)*
- **The flash count is shown as `N/100,000`** (the GD32F350 datasheet minimum). AGENTS.md
  rule 13. *(Anargon's request)*
- **Patcher packages:** rev36b-rc (Codex) ran `.patcherpkg` files (C# compiled with `csc` or `dotnet build` from PATH,
  loaded in-process, no signature, no twin gate on what they flash); R37 dropped that loader. R44's loader (Anargon's
  request) has no PATH lookups, takes C# source only and needs a typed, hash-pinned approval; not signed, and the gate
  is asked for, not enforced (see R44 above). *(code review of rev36b-rc)*
- **No XP-Pen firmware bytes in the source (R42 Part 4).** Anargon asked to remove copyrighted material, not hide it.
  AGENTS.md rule 15.
  - **Signatures are fingerprints.** Each window is stored as its mask, a 16-bit quick check of its first four fixed
    bytes and the first 64 bits of its SHA-256 (`atf_symbols.cs`, `tools/mksig.py`, `Layout.Resolve`, `Sha256`).
    All 178 firmware files on Anargon's PC resolve exactly as before, in C# and Java. *(offline comparison)*
  - **The stored firmware end is gone** (`StockRef.TAIL`, 3220 bytes); only its CRC-32 is kept (`END_CRC`). The
    recovery tool restores an erased end from the user's own copy (`Repair.FindEnd`): any 64 KB dump, application
    `.bin` or `.hex` of the same build whose end matches once this tool's known edits are undone. It looks in the
    dump's folder and the one above, the output folder, the current folder, and the program's folder and the one
    above, 3 levels deep. With no copy it stops and says what to add. *(core suite)*
  - **Why that is enough:** every older xppen-tools `dump` wrote a complete `main.bin` next to its cut `code.bin` /
    `stock.hex`, and every flash since rev 34 keeps a backup. On Anargon's PC 161 files hold a usable end. Anargon's
    and Anomy's damaged dumps repair byte for byte as before. *(file survey, real dumps)*
  - **Left:** 29 single-instruction checks (87 bytes, mostly `movs` with a setting value) and 6 short search
    patterns (50 bytes) in the patcher and twin. gd32emu holds none. Earlier total: about 4.7 KB.
- **The public dump repository `NiceAesth/xppen-dumps`, folder `stock/Deco 640`** (checked by git blob hash from the
  GitHub API listing, nothing downloaded):
  - `ic902-main.bin` is Anargon's February dump, complete;
  - `ic902-bootloader.bin` is its bootloader area;
  - `ic902-code.bin` and `ic902-stock.hex` are the cut 35208-byte restore image, byte for byte the quarantined
    files. Flashing that hex causes the rev 35 damage.

  It holds no December Deco 640 dump. Anargon plans a pull request there. *(GitHub API, local hashes)*

## R41 - read next

R41 (Claude Opus 5.5, Anthropic) found and fixed why this program read nothing from the tablet on Windows (open item
11; Anargon: "no reports at all from 1 open collection(s)" with stock, modded and calibration firmware, while the same
logic worked in a Linux VM with USB passthrough). Full record: `history/claude-rev41.md`. Findings:

- **Windows holds the collection that carries the pen reports.** Windows lists the Deco 640 (28BD:2904) as interface 0
  = two mice (0001:0002) + a keyboard (0001:0006), interface 1 = **HID-compliant pen (000D:0002), 10-byte reports,
  report ID 7**, interface 2 = vendor-defined (FF0A:0001), 12-byte reports. `CreateFile(GENERIC_READ)` fails on
  interface 0 with ACCESS_DENIED (5) and on the pen with **SHARING_VIOLATION (32)**: Windows' own pen input owns it.
  *(hardware: read-only probe, `Claude-workarea/experiments/hidprobe.cs`, on Anargon's PC with the tablet)*
- **So the reader up to R40 only ever read interface 2**, which is silent until a host selects report mode 4 (this
  program never does), and it skipped the failed opens without saying so. ID 7 (mode 2: normal pen reports and the
  calibration telemetry) never reached it on Windows. Linux's hidraw has no exclusive opens, which is why Linux worked.
  Opening "every collection" (rev 33) and re-checking (R40) could not help. *(hardware + code analysis)*
- **Raw Input delivers the pen collection's reports unchanged, with no driver:** `RegisterRawInputDevices` for
  000D:0002 with `RIDEV_INPUTSINK` on a message-only window. Probe: 3199 ID-7 reports in 8 s (400/s, 10 bytes,
  e.g. `07 a0 36 12 5b 12 00 00 3c c4`) where ReadFile got 0. R41's `--hid-check`: 2006 ID-7 reports in 5 s. Four
  open/read/close cycles in one process: ~400/s each, close 1 - 3 ms, nothing collected after close. *(hardware)*
- **`HidD_*` return a one-byte BOOLEAN**; they were declared as 4-byte `bool`. Now `MarshalAs(U1)`. *(code analysis)*
- The XP-Pen driver service (`PenService_*`) was running during these tests and did not stop Raw Input. Not yet run on
  hardware: the calibration build's telemetry through Raw Input (same collection, same 10-byte ID 7 reports, so it is
  expected to arrive the same way), and a PC without the XP-Pen driver.
- **The twin's cursor latency** is the median, over 0.1-coil position bins, of the backward-minus-forward difference
  of a slide; it stays within 0.6 ms over 8 start phases. A straight line fitted to each slide direction failed: the
  position jumps where the tracked coil switches, so the result moved by up to 100 ms with the start position.
  Values: RAPID default 5.9 ms drawing, 11.8 ms hovering; stock 16.6 / 22.7 ms. *(twin; `history/claude-rev41.md` Part 2)*
- **Hovering roughly doubles every report-averaging filter's delay,** because Hover Boost halves the report rate
  (RAPID 800 -> 400/s). The Settings step now shows drawing and hovering delay separately. *(twin)*
- **Hover pressure spikes: fixed in the twin, not yet confirmed on the tablet.**
  - Hardware: on `AnargonSS727_20261101` at 800/s, the tip bit flickered on and off every 10-40 ms at low pressure
    while the pen was close. Higher up there were none, but there were strictly periodic dropouts every 60 / 92 ms.
    *(read-only Raw Input log)*
  - **Root cause: the short frequency-sweep readings.** The pen is still ringing from one sweep step when the next
    starts, so neighbouring frequencies leak into each other. RAPID's 5-sample steps read a hovering pen up to 9 kHz
    high (Q 150-300); at f0 506-507 kHz that is a pen pressed all the time, and light presses were missed or
    flickered. *(twin, `res-int` pen model; Part 3 subagent analysis + Part 4)*
  - **Fix: Accurate Pressure Readings** (new setting, default on): stock-length steps (45 samples, 40 pulses), as
    many per frame as fit - QUICK all 6, FAST 3, RAPID 1; ULTRA has no room. One step per frame never showed
    pressure in the air at any Q / f0 tried. Cost: RAPID 100 pressure updates/s instead of 300 (stock ~220), and
    light presses register about 20 ms later than stock. *(twin)*
  - **A sweep that crosses a hover-stage switch is dropped** (flag `VQ` at `0x20000F52`, set by `setmode` while
    `V[3]` != 0). ULTRA: 9 -> 1 false episodes in 2 s. *(twin)*
  - Rejected: an entry gate before the tip band (it blocked real presses at RAPID and ULTRA) and a 20 ms filter
    (Anargon preferred none; it left most episodes). *(twin)*
  - **Hardware check:** Tablet signal thresholds now report the pen frequency while hovering and any pressure shown
    with the pen in the air, with the final build's timing (Anargon's suggestion; replaces a `--pen-resonance` run).
    Detail: `history/claude-rev41.md` Part 4.
  - **On the real tablet (Part 5):** Anargon's pen hovers at 507.0 kHz median, 508.99 kHz highest, about 2 kHz under
    the threshold. With R41 RAPID (calibrated): no pressure in the air during calibration or 20 s of close hover,
    pressure updated in exact 10 ms steps (100/s), 702 reports/s at close hover (old firmware: 111/s, mostly Reach
    Stage). Not yet run: a same-height A/B against short readings. *(hardware)*
- **Aggressive Interpolation's slot index `VF[1]` was never initialised** (only the count is reset), so the first
  write after power-up could land anywhere up to `0x20001367`. Fixed: the index is masked before the write.
  *(code analysis)*

## R40 - read next

R40 (Claude, Anthropic) stops the calibration readiness check from ending calibration when no readings arrive
(Anargon's report: it went straight to "continue with the preset defaults"). `Cli.ReadyCheck` re-checks by itself, then
offers check again / flash the calibration build again / defaults; see `history/claude-rev40.md`. (The cause, no pen
reports on Anargon's PC, was found in R41.) The R39 and R38 notes below still apply.

## R39 - read next

R39 (Claude, Anthropic) adds the Step 1 `b` option (build from this run's tablet backup) and fixes an order-dependent
host check; see `history/claude-rev39.md`. It is the first revision whose suites ran with both verified stock fixtures
(February and December). The R38 notes below still apply.

## R38 - read next

Claude (Anthropic) returned for R38; the full record is `history/claude-rev38.md`. Findings, how each was established:

- **The xppen-tools update prompt could not be answered on Windows** because every helper was started with
  `CreateNoWindow = true`, which gives a console child its own hidden console. Interactive helpers now share this
  program's console and input; background helpers keep the hidden console and closed stdin. *(code analysis of the
  .NET/Win32 process model; the Linux pty runs confirm input inheritance, the Windows console case still needs one
  check on Windows)*
- **stdout and stderr shared one partial-line buffer**, which glued `[INFO]` log lines into the prompt line.
  *(code analysis, reproduced in a pty with a fake helper, fixed and re-run)*
- **The wizard blocked on the startup dump at its first line**, and **every flash took another full backup**. Both
  removed: the dump runs alongside the steps, and one backup per run is used (return-to-stock keeps its verification
  read). *(host regressions, pty walkthrough of the real program)*
- **The AI DISCLOSURE box was missing from the R37-rc sources.** Restored on every screen. AGENTS.md rule 12: never
  remove it.

> Historical record preserved from the R36 audit checkout. Paths, packaging and repository descriptions below describe that earlier checkout; current instructions are in the root AGENTS.md and DOCS guides. See [model provenance](DOCS/model-provenance.md) for corrected R36 attribution.

# CLAUDE.md - Claude's findings and project record for Anargon's Tablet Tools (folder `anargons-tablet-tools`; `deco640` up to rev 33)

This file is kept by **Claude, an AI model made by Anthropic**, working on this project with **Anargon**, its
maintainer. It is one of the per-model files described in `AGENTS.md`: **every model and contributor should read it**,
whichever model they are. Section 0 lists Claude's discoveries for any reader; the sections after it are the running
record (the hardware, what has been done, what is open, what to test, notes for every revision 1 - 33), and
section 8 lists every potential brick vector. Each
revision adds to it rather than replacing it. The per-revision notes of rev 1 - 34 and the rev 23 summary are in
`history/claude-rev01-34-notes.md`. (Until rev 32 this file was named AGENTS.md.)

## 0. Findings

**These discoveries were made by Claude, an AI model made by Anthropic**, across revisions 1 - 33, by reading the
firmware's machine code and running it in the project's digital twin, unless marked otherwise. Each says how it was
established: *code analysis*, *twin* (shown in the emulator), *hardware* (confirmed on a real tablet), or
*calculation*.

### The chip and its two programs
- The controller is a **GigaDevice GD32F350C8T6** (C8 = 48 pins, 64 KB flash; T6 = LQFP package, -40 to +85 C),
  **not an STM32**. It reads the GD32 flash-size register, uses the GD32 USB core, and has no ST strings, ST USB vendor
  ID or ST-only registers anywhere. *(code analysis)*
- Flash holds a **bootloader** (`0x08000000`-`0x080037FF`) and the **firmware** (from `0x08003800`). The update mode
  (DFU) erases `0x08003800`-`0x0800FFFF` and never erases itself, so a bad firmware can always be reflashed.
  *(code analysis, hardware)*
- The bootloader keeps a **boot state** in the word at `0x08003400` (0 run, 1 update requested, 2 update in progress),
  changed only by `set_boot_state` at `0x08000F40`. *(code analysis, twin)*
- The **reset path** is vector (`0x08000004`) -> handler (`0x08000164`) -> trampoline (`0x08000150`) -> boot code
  (`0x08000C38`). One word at `0x0800015C` decides where the trampoline jumps, and it sends every reset into page
  `0x08000C00`. *(code analysis, twin)*

### Report rate and `.data`
- The 500 reports/s ceiling is a 3 ms USB report interval stored inside the firmware's **compressed `.data` block**,
  at `0x0800C63B` / `0x0800C658`. Rates above 500/s need it patched to 1 ms. *(code analysis, twin; hardware: FAST works)*
- A dump that ends before `.data` (truncated) cannot produce a working build; the tool refuses it. *(twin)*
- **The first brick (revisions 3 - 5) was an erased `.data`.** xppen-tools only programmed up to `0x0800C188` (its
  hard-coded IC-902 size), so after the bootloader's erase the compressed `.data` (`0x0800C188`-`0x0800C7CC`) was left
  all `0xFF`. Proven by checksum: the device's CRC-16/XMODEM `0x7F73` was reproduced exactly from "code + erased
  `.data`". Claude cut the stock `.data` out (1604 bytes), and built a self-writing restorer on the firmware's own
  flash routines, checked in emulation with a flash-controller model (802 halfword writes reproduce `.data`
  exactly). Anargon reported the tablet working again. *(code analysis, emulation, hardware)*
- **xppen-tools' own Deco 01 V3 bInterval edit targets a different firmware build.** Its offsets (`0x0800C07B` /
  `0x0800C098`) land on a GPIO address in this build, and the twin faults at startup. A likely way an earlier
  bInterval change broke the tablet (revision 14). *(code analysis, twin)*
- **Two stock firmware builds are known:** `2025-02-14_release001` (Anargon's tablet, the one this tool is made for)
  and `2025-12-23_release001` (a second dump, marked with the December date). Since revision 33 the tool supports
  both (and any later build whose code only moved - see the next-but-one finding).
- **How the December build differs** (full tables in `FIRMWARE_VERSIONS.md`; by Claude Opus 5.5 from Anargon's
  December dumps): the bootloader area, its config page, the RAM layout and the USB device are **identical**. XP-Pen
  made three functional changes: a **stronger dead-band** (coefficients 70/60/50 -> 80/80/60), **easier pen pick-up**
  in one search state (threshold lowered by 30), and one **search probe frequency** one step higher (index 21 -> 22).
  Everything else is the same code moved: -4 bytes up to `0x08005866`, **+12 bytes from `0x0800589C`** (including
  `.data`). *(code analysis, twin)*
- **The third-party brick, re-assessed:** a modder's hex built on Anargon's February firmware was flashed onto a
  tablet that probably ran the December build. Since nothing in flash distinguishes the boards, a *complete* February
  image should run there; the likelier causes are an incomplete image (xppen-tools' fixed size drops `.data` - and on
  December also the last 12 bytes of code) or February offsets applied to December firmware. Unconfirmed: the bricked
  tablet's dump is not available. *(Anargon's report; analysis)*
- **Firmware locations can be found by their code, not their address (rev 33).** Every one of the 82 firmware
  locations the tool uses (patch sites, called functions, tables, literals, the `.data` bytes it edits, the twin's
  hooks) has a byte signature that is unique in both stock builds once build-dependent bytes are masked: call and
  long-branch encodings, PC-relative literal offsets, words that point into flash, and the bytes the tool itself
  rewrites. On December all 82 resolve, and the shifts they show (0 / -2 / -4 / +12) reproduce the hand-made offset
  map exactly. *(code analysis, twin)*
- **Signature windows must stay inside one function and be decoded in sync.** Two lessons from simulated future
  builds (December relinked with code inserted at four places): a window that starts inside a 32-bit call's second
  half was left with its target bits unmasked, and a window that reached back across a function start broke when
  code was inserted between the functions. The generator now decodes from 64 bytes before a window and prefers windows
  inside the symbol's own function (4 of 82 still cross a boundary; none of them failed). *(twin, rev 33)*
- **The firmware's real end is in its scatter-load table.** The start-up code copies `.data` using a table of
  `{load, RAM, size, routine}` entries; the zero-init entry's load address is the end of the image (`0x0800C7CC`
  February, `0x0800C7D8` December). A dump without that table is cut before it - truncated. *(code analysis)*
- **December builds behave like February builds in the twin:** all five presets pass every check, with the same frame
  timing and nearly the same cursor latency (within 0.4 ms; STOCK preset 16.0 vs 17.9 ms). *(twin, rev 33)*
- **The max-pressure fix does not lower the pressure ceiling (rev 33 check, at Anargon's request).** It clamps the
  *index* into the 76-entry table that turns the coil measurement into the pen's resonance frequency (`0x20000410`),
  so the lookup never reads past the table; above the last entry the last segment is extended, not capped. Pressure
  is derived later from that frequency. In the twin, pressure reaches 16383 in our builds exactly as in stock, and
  matches stock within about 5 units across the range (e.g. 9467 vs 9464). *(code analysis, twin)*
- **Aggressive Position Interpolation counts reports, not milliseconds** (rev 33, prompted by Anargon not feeling the
  claimed delay). Its window is 16 / 8 / 4 *reports*, chosen by how far the pen moved since the previous report
  (< 0.2 mm still, < 0.8 mm slow). So at FAST (500/s) it is 32 / 16 / 8 ms, and anything slower than about 100 mm/s
  counts as "still": the twin's slide test measured 24.4 ms delay against 8.6 ms for standard interpolation. With
  much more simulated noise the per-report jumps of a resting pen pass 0.2 mm, so it picks the short window while
  the pen is still: steady while moving, shaky when held still - Anargon's hardware observation. Its still-pen spread
  stays better than standard at every noise level tried (0.004 / 0.016 / 0.076 / 0.25 mm against 0.005 / 0.033 /
  0.110 / 0.43 mm at noise 6 / 60 / 200 / 600). Proposed fix, not built: windows in milliseconds, and the speed
  measured over several reports so noise cannot pass for movement. *(code analysis, twin)*
- **The stock LED blinks while the pen hovers.** Besides "lit while the pen is in range", a routine at `0x0800868C`
  toggles it every 500 ms during hover, and start-up and pen-leave paths switch it off. *(code analysis, twin with a
  GPIO output model added in rev 33)*
- **Two more tablets damaged the rev 3 - 5 way (rev 35; dumps from Anargon: Anargon's own February tablet and
  Anomy's December tablet).** Both: every code byte intact, everything from `0x0800C188` to the firmware end erased
  (1599 of 1604 / 1611 of 1616 bytes `0xFF`; the rest happen to match), and the bootloader settings page `0x08003400`
  erased except its boot-state word (0). Exactly what a flasher that stops at xppen-tools' IC-902 size leaves.
  *(dump analysis)*
- **The settings page `0x08003400` words 1 - 6** (`FFFFFFFF 201 FF 32 32 0`) are written by the stock firmware
  (`0x080090D0` Dec / `0x080090C4` Feb: read 13 words, set word 0 = 1, erase, write back; followed at `0x0800B078` by a
  system reset - its DFU entry). Identical in all four healthy dumps. **Every bootloader update leaves the page with
  only its boot state** (`set_boot_state` keeps just that word on the way through state 2), so an "erased" settings
  page is normal after any flash, not damage; the stock firmware runs fine with it (defaults for `0xFF` fields) and
  only rewrites it when it enters DFU itself. The erased firmware end is what stops a damaged tablet.
  *(code analysis, twin)*
- **How the bootloader decides between update mode and the firmware** (rev 35, found by the independent verifier):
  with boot state 1 or 2 it checks the reset cause, `RCU_RSTSCR` bit 28 (SWRSTF, `0x08000EAC(0x91C)`). After a
  **software reset** (the firmware's own DFU entry, or our patcher) it stays in update mode and waits for the PC; it
  only gives up if USB is never configured. After a **power-on** with state 1 it clears the state and starts the
  firmware. The emulator sets SWRSTF on a software reset and clears it on RSTFC (without it the twin showed a wrong
  "waits ~2.3 s, then starts the firmware").
  *(code analysis, twin)*
- **USB strings: the stock firmware keeps its string-descriptor table in RAM** (`0x20000284`, 36 entries, built from
  `.data`; index 3 points to a RAM string made from the bootloader data page) and answers GET_DESCRIPTOR through one
  long chain of index compares (`0x0800AEE6`...); an unknown index falls through to a stall (`bl 0x0800AB18` at
  `0x0800AF72`). **Index 200 is the stock DFU entry** (`0x0800B06C` -> `0x080090C4`): reading string 200 puts the
  tablet into update mode. Stock never touches RAM above `0x20000E70` (the stack top). *(code analysis, twin)*
- **Lesson from a soft-brick Claude caused (revision 8):** two diagnostic builds lengthened the scan steps without
  lengthening their timers, so steps overran. The builder's timing guard and the twin gate exist because of this.

### More findings from revisions 1 - 30 (full detail and corrections in `history/`)
- **Recovery path:** BOOT0 must be pulled to **3.3 V** (through ~1 kOhm), not to ground, for the GD32 ROM bootloader;
  UART is PA9 (pin 30, D_TX) / PA10 (pin 31, D_RX). PA15 to ground at power-on enters the vendor DFU. *(board photo,
  datasheet; the PA15 route has been used on the tablet, the BOOT0/UART route not yet)*
- **USB frame numbers wrap at 2048** at full speed, not 16384; the old pacing stalled ~5 ms every ~2 s. *(twin)*
- **The pen search needs a line sum above a fixed 100**, which is why very short readings (3 samples) never found the
  pen at ULTRA; 4 samples is the floor. *(code analysis, twin)*
- **Pen state by resonance:** 457-480 and 480-500 kHz carry button states, 500-540 kHz the tip; anything else is
  invalid and stops reports, so a full-pressure overshoot past 540 kHz used to stop input. *(code analysis)*
- **Pressure** comes from a piecewise-linear table of 76 breakpoints (0 - 180000). The index is now clamped.
  *(code analysis, emulation)*
- **Active area** 160 x 90 mm at exactly 100 report units per mm. **LED** = PC13, active-low, lit while the pen is in
  range. *(code analysis)*
- **Excite All Coils could not work:** the coils sit on 8-way analog switches, so two coils can only be on together
  if they are exactly 8 apart. *(code analysis)*
- **The search and frequency tables are linked:** moving the search list into RAM once made the search run at
  megahertz, because the firmware finds one table from the other. *(twin, revision 24)*
- **The bootloader never reads the two empty pages** (`0x08002800`, `0x08002C00`), so no checksum covers them, and it
  rewrites `0x08003400` on every boot. *(twin, revision 28)*

### Pen sensing
- The receiver **integrates** the pen's ring-down, so shorter readings (needed at high rates) read proportionally
  weaker. Pick-up and drop thresholds must scale with reading length; this fixed "needs contact" at 800/1000 Hz in the
  twin. *(twin)*
- The firmware only searches 461-533 kHz and treats a resonance under 457 kHz as "no pen". An earlier claim of
  "435 kHz pickup" is not supported. *(code analysis)*

### Brick vectors
- Every known way the tablet could stop working, with recovery tiers and guards, is in **section 8** at the end of
  this file. In short: normal flashes are recoverable by reflashing; the only writes that can need ROM recovery are
  the opt-in Bootloader Counter's reset-vector page rewrite (~85 ms, first install only) and return-to-stock.

### What the twin cannot tell us
- Its signal strengths and noise are relative, not absolute. Hover height, real noise and real flash timing need a
  real tablet.

## 1. The device
- XPPen Deco 640, controller **GigaDevice GD32F350C8T6** (Cortex-M4F, 64 KB flash, 8 KB SRAM, 72 MHz). **Not an STM32:**
  it reads the GD32F3x0 flash-size register (0x1FFFF7E0) and uses the GD32 USBFS core at 0x50000000, and there is no
  ST string, ST USB vendor ID or ST-only register anywhere in its bootloader or firmware.
- Flash: bootloader 0x08000000-0x080037FF (code to 0x080027FF, empty pages 0x08002800 and 0x08002C00, bootloader
  data 0x08003000-0x0800341B), firmware from 0x08003800 (stock firmware "deco01v3", build 2025-02-14_release001).
- Bootloader: UGTABLET TABLET_DFU200718 (XP-Pen IC 902), USB 28BD:FFF1 in DFU, 28BD:2904 normal. Boot state = word 0
  of 0x08003400, changed only by its `set_boot_state` (0x08000F40): 0 run, 1 DFU requested, 2 update/erase in progress
  (only that word is written). It rewrites that page at power-on and requests a system reset afterwards.
- Coils: X 1-32 (four 8-way switches, address PB12-14, enables PA6/PA7/PB0/PB2), Y 1-19 (three switches, address
  PA0-2, enables PA3-5). Two coils can be on together only 8 apart. Receiver integrates (DISC_ADC / DISC_OPA; PB8/PB9
  release it before each reading): shorter readings read proportionally smaller.
- Pins from the board photo: PA9 = D_TX (pin 30), PA10 = D_RX (pin 31), BOOT0 marked (GD32 ROM bootloader over UART
  for recovery).
- **Knowledge gap (Anargon's statement):** This chip can reflash sectors of itself to bytes of your choosing, so long as
  you dump an hex file dedicated to doing this task. Confirmed in code: both the bootloader and the stock firmware
  carry the GD32 flash routines (firmware: unlock 0x08008314, program word 0x08008334, erase page 0x08008298;
  helpers erase 0x08009A1C, program N words 0x08009A40; bootloader: erase 0x080009B4, program word 0x08000A50,
  unlock 0x08000A30).

## 2. What has been done to the firmware (up to rev 35; later work is in the R38-R45 sections above)
- **Firmware builds:** the stock 2025-02-14 and 2025-12-23 builds are verified; any other build is patched where
  its code is found by signature, and modifications whose code is not found are reported and left out (rev 33).
- **Rates:** STOCK 220, QUICK 300, FAST 500, RAPID 800, ULTRA 1000 reports/s. USB reports every 1 ms (bInterval 1)
  unless Discreet.
- **Scheduler:** frame pacing, sweep spread over frames, and a tilt-line cache.
- **Pen pick-up and drop:** thresholds scale per stage with reading length relative to FAST; they were 300/200 for
  45-sample readings. This is what fixed "needs contact" at 800/1000 Hz in the twin.
- **Hover:** three stages (fast, Hover Boost, Reach Stage 60 Hz), switched by probe frames and touch. Long-range
  Pickup searches with strong pulses. Quick searches use FAST's readings.
- **Other always-on fixes:** hardware divide, max-pressure fix, pressure band-edge hold, and longer sweep readings
  where pressure needs them (ULTRA).
- **Filters:**
  - Position Interpolation (stock 3/5 average).
  - Aggressive Position Interpolation (rev 30): our own moving average of 16 / 8 / 4 positions by speed. In the twin
    its still-pen spread is about 1/3 of standard and it does not wobble more while moving.
  - Movement Smoothing (dead zone, off by default).
  - Jump Rejection.
- **Tablet Calibration:**
  - Pass 1 telemetry build: signal, position, frequency and counter pages rotate over 8 frames in the pen report.
  - Five standard steps, then per-tablet thresholds, boost level and jump margin.
  - Pass 2 calibrated build.
- **Flash counter:**
  - Kept in page 0x08002C00 ("AnSC", base, records). It survives stock reflashes.
  - Our firmware counts its first boot of each new build.
  - With **Bootloader Counter** (opt-in; **confirmed working on Anargon's tablet**, rev 33), the bootloader counts itself: hook at `set_boot_state`, routine
    at 0x08002800, +1 only when the state leaves 2, so normal boots never count. Installed once, on the first boot right
    after flashing, by rewriting page 0x08000C00 (verified, repaired if interrupted, unknown bootloaders untouched).
    rev 32 adds (bundled with it) a **reset-vector self-heal**: the reset boot-jump (literal at 0x0800015C) is
    redirected to a repair stub in the never-erased page 0x08002800, so the stub repairs page 0x08000C00 on every boot
    before the boot code runs - closing the one interrupted-install gap rev 31 could not (see open issue 3).
  - rev 32 also keeps a **hidden PC-side copy** of the count (`Store`, `%APPDATA%\AnargonsTabletTools\state.ini`), which
    since rev 35 only counts the flashes that PC performed.
  - **rev 35 - read from the tablet first, over USB, no dump:** every build answers **USB string 173** with
    `ADTC1:<B|F|N>:<count>` (`Patches.CounterString`; B = Bootloader Counter installed, F = firmware counter, N = no
    counter). The PC reads it with `HidD_GetIndexedString` (Windows), usbfs (Linux) or IOKit (macOS); if that fails, from the dumps. Order: live
    string > calibration telemetry > the tablet's dumps > local PC, shown as `[According to Bootloader]`,
    `[According to Firmware]` or `[According to local PC]`; the tablet always wins, since one PC may flash several
    tablets. `--flash-count` prints it. **The page, the record format and string 173 must never change: AGENTS.md
    rule 11.** Twin: firmware-counter build `ADTC1:F:000001`; Bootloader Counter build `B:000001`, after one update
    `B:000002`.
- **Compatibility check (rev 32):** before building, every modification's stock code site is probed against the loaded
  firmware. A mismatch stops the build; no confirmation bypasses the byte checks (a forced override, tried in rev 32,
  is not offered).
- **Warranty and Firmware Discreetness (rev 33, replaces Discreet):** **Volatile Changes** (default on, required for
  RAPID and ULTRA) allows the `.data` edit (1 ms report interval) - `.data` is part of the firmware, so a stock
  reflash undoes it; **Bootloader Changes** (default off) allows writes to the bootloader area (the flash counter) -
  permanent, voids the warranty; the Bootloader Counter needs it. `--discreet` still turns both off.
- **LED (rev 34):** stock behaviour, except a fixed 2.5 Hz blink (200 ms on / off, from the millisecond counter) while
  Startup Calibration runs; the calibration-pass build keeps it lit and blinks it while a step is measured (pen
  tracked). Twin: calibration-pass on with no pen, blinking 200 ms with the pen, on again after; normal build the
  same as stock.
- **Return to stock:** `--return-to-stock` writes return-to-stock.hex (restores page 0x08000C00, erases 0x08002800 and
  0x08002C00) and stock-app.hex.

## 3. Tooling
- History: the patcher and twin started as Python (rev 1 - 23, written by Claude: the patcher from rev 1, the digital
  twin from rev 14) and were converted to C# in rev 24 for interoperability (one Windows build with no installs; the
  same code translates to Java for testing). Python was kept as a reference in rev 24 and retired in rev 25.
- `anargons-tablet-tools` (this repository): the patcher. It contains:
  - wizard;
  - checks in the digital twin at full PC speed;
  - measurements paced to 72 MHz (1 cycle = 13888.889 ps);
  - backups via xppen-tools (`dfu`, `dump`);
  - flashing via xppen-tools: this program runs the command, xppen-tools asks for its typed confirmation, and commands
    run in the patcher's folder without paths.
- `gd32emu`: general-purpose GD32F3x0 emulator, a **separate repository** since rev 32 (its own project since rev 29).
  This repository finds it as the `gd32emu` submodule, a sibling clone, or `GD32EMU_DIR` (see `EMULATOR.md`). GD32 only: no STM32 parts, ST-fingerprinted images
  refused, strict GD32F3x0 memory map. It runs the tablet's whole stock flash from reset (models system reset and the
  vector-table register). The tablet is a `Board` on it; results were identical before and after the split.
- Builds: `build.cmd` (Windows, nothing to install) and `build.sh` (Linux / macOS) at the top of each repository; the
  executable is written next to them. Each repository has its own `.github/workflows/build.yml`.
- Testing here: the C# is translated to Java (tools/cs2java.py) and run; Windows-only parts (HID, console, process
  runner) are only compiled on Windows. Since rev 35 also built with the real .NET 8 SDK and tested with
  `tests/run-tests.sh` (the same script GitHub Actions runs, `.github/workflows/tests.yml`); settings come from `.env`.
- rev 32 additions (anargons-tablet-tools): a per-modification **compatibility check** (`Compat`, `AnargonsTabletTools --compat IMAGE`) feeding
  aggressive full-screen warning pages; a hidden per-user **flash-count store** (`Store`) with Tablet/PC provenance; and
  **one-time-only warnings**. See section 7 (rev 32) for the detail.
- Docs (this repository): `AGENTS.md` (read first: models, rules, model files), `DESIGN.md` (the patcher and twin),
  `EMULATOR.md` (how it connects to gd32emu), `FIRMWARE_DESIGN.md` (how the tablet firmware works), and this file
  (findings, running record, per-revision design detail in section 7). gd32emu has its own README, DESIGN, AGENTS and
  CLAUDE files.

## 4. Open issues
1. **Hover at RAPID/ULTRA - done:** the pen hovers at ULTRA on Anargon's tablet (R41-R45: hover stages, Steady Hover
   ladder, reach hold).
2. **Aggressive Position Interpolation** (rev 30 rewrite) is unconfirmed on hardware. Latency is about half the
   window: up to ~8 frames when still (8 ms at 1000 Hz), 2 when moving fast.
3. **Bootloader Counter - confirmed working on hardware by Anargon (rev 33).** rev 31 verified the whole lifecycle in the twin (install, count only on leaving DFU state 2,
   idempotent re-boot, interrupted-install repair incl. a fully-erased page, write-protect safe-fail, return-to-stock)
   and hardened the install (full-routine check before touching the boot page). Remaining caveats:
   - The 2 -> other transition was confirmed by disassembly (set_boot_state at 0x08000F40 writes only the state word),
     but the real DFU command sequence on hardware was not traced.
   - Install still rewrites the boot-critical page 0x08000C00 once. rev 31 added a bootloader-side self-heal: the stock
     copy of that page is kept in flash and the wrapper, on every DFU-state boot, fills any erased word of the page
     from it (0xFF -> stock only, anchor-guarded, no erase), restoring the set_boot_state body it returns into; and
     `pwrite` commits the hook word LAST so that step is atomic and any earlier interruption leaves a bootable stock
     page the firmware then finishes. This repairs every boot that reaches the firmware OR set_boot_state.
   - **rev 32 - reset-vector self-heal (closes the rev 31 gap), opt-in with the counter.** rev 31's residual was: the
     reset vector jumps straight to the boot code at 0x08000c38 inside page 0x08000C00, so a power loss while that page's
     body is (re)written during install leaves the boot code erased and the chip faults from reset before any self-heal
     could run. rev 32 redirects the reset boot-jump (literal at 0x0800015C, stock 0x08000C39) to a repair stub kept in
     the never-erased page 0x08002800; the stub repairs page 0x08000C00 from the stored stock copy on every boot BEFORE
     the boot code runs, so an interrupted C00 rewrite now self-heals. Twin (`--bl-boot`) confirms: with the redirect
     installed, erasing the C00 boot body still boots (stub repairs it); the same erasure on stock faults. The install
     order (wrapper+stub -> redirect page 0x08000000 -> C00 hook) means the C00 hook write is now fully recoverable.
   - **Residual, now relocated and smaller (confirmed by twin):** installing the redirect requires one rewrite of the
     reset-vector page 0x08000000; a power loss during THAT single page program is the only remaining unrecoverable
     window (ROM recovery: BOOT0+UART or SWD) - erasing page 0 in the installed image faults from reset, as expected.
     This does NOT eliminate the irreducible window (no single flash page can be updated atomically); it moves it from
     the whole C00 rewrite to one page-0 rewrite of pure-stock bytes, and makes every other interruption self-heal. Per
     Anargon's decision it is **opt-in**: bundled automatically with `--bootloader-counter`, and a normal build never
     writes page 0x08000000. Feature stays EXPERIMENTAL; advise keeping power stable for ~1 s after the first boot
     following a flash, and only use it with SWD/BOOT0 recovery at hand.
   - **Stub-page rewrite exception (found after rev 32 shipped; fix proposed, not yet built).** The redirect and the
     C00 hook are written once. The repair stub's own page 0x08002800 is rewritten whenever a new counter build's copy
     of the routine differs from the one on the chip, and with the redirect live that rewrite is the same ~85 ms
     window. The routine embeds the address of the build's stock-page copy (`stockAt`), so it differs if a future
     layout moves that copy. Today all four counter presets produce a byte-identical routine (tested: a RAPID build
     booted on a FAST-installed chip changed 0 bytes in all four bootloader pages), so it does not occur now. Fix:
     store the stock copies at one fixed address (e.g. the end of flash) so the routine never changes across builds
     or revisions; then the page is written exactly once. Until then, every counter flash shows the unplug page.
4. **Option bytes** may write-protect bootloader pages on some units; the calibration readout then says "NOT written".
5. **xppen-tools:** running `dfu` while already in DFU errors out (reported as a hard crash). The patcher now skips
   `dfu` when it put the tablet in DFU itself and continues if `dfu` fails. Needs confirming on hardware.
6. **Not run on real hardware here:** cursor positioning under the question, and the tablet readers on every
   system - Windows HID (`HidD_*`), Linux hidraw + usbfs (`LinuxUsbString`, rev 35), macOS IOKit (`HidOpenMac`,
   `MacUsbString`, rev 35). The .NET 8 builds compile for linux-x64, osx-arm64 and win-x64 (rev 35); the Linux and
   macOS code was checked against the kernel and Apple headers by independent verifiers, not run.
7. **Twin limits:** the twin's heights and noise are relative, not absolute. The integrating-receiver model is
   inferred from the board.
8. **Done in rev 34:** every flash starts with a backup into `dumps/`, and the build the tablet runs is checked against
   the image's source build (brick vector 13).
9. **Done in rev 33:** December is supported; every firmware location is found by signature, so a later build whose
   code only moved needs no change. A build that changes the code at a patch site leaves that modification out.
10. **Flashing one stock build onto a tablet that shipped with the other** has not been done on hardware. Both builds
   share the bootloader, its configuration, the RAM layout and the USB device, so it is expected to work.
11. **Resolved in R41 (hardware):** Tablet Calibration read no pen reports on Windows; the pen collection is now read
   through Raw Input.
12. **Aggressive Position Interpolation** windows count reports and its speed test is fooled by noise - see the finding
   in section 0; fix proposed.
13. **Recovery tool (rev 35): the first Anomy-specific-patcher.hex worked on Anomy's tablet (Anargon's report;
   hardware).** That first version triggered on "settings differ from stock" rather than "settings page erased"; the
   shipped tool uses "erased" so a tablet with other legitimate settings is never sent into update mode every start.
   Not yet confirmed on hardware: whether the tablet visibly restarted into update mode, and the real bootloader's
   update window. How the two tablets got damaged: R42 reproduced both dumps byte for byte as the tablet's own stock
   firmware written only to `0x0800C188` (a 35208-byte restore image from an older xppen-tools dump, or an older
   xppen-tools `update` that stopped there); which of the two is not known.

## 5. Test on the tablet (in order)
1. `build.cmd`; FAST default build should behave as before.
2. RAPID / ULTRA: hover without contact. R41: with Accurate Pressure Readings (default) RAPID should show no pressure
   in the air; ULTRA keeps short readings and may still show some.
3. Aggressive Position Interpolation on vs off: hold still at hover, compare shake.
4. Calibration on RAPID; send calibration-rapid.txt and main-*.log.
5. Counter: flash, dump: "Current flash count" in step 1. Bootloader Counter only with recovery at hand.
6. Return to stock, then dump and compare with the first backup (0x08003000-0x080037FF may differ).

## 6. Revision log
- rev 1 - 11 (Python patcher; Claude Sonnet 5.5 at max effort and Claude Opus 4.8; detail in
  `history/claude-rev01-11.md`): the first patcher (speed matrix, wizard), the
  discovery that xppen-tools did not write `.data`, **recovery of the bricked tablet** (`.data` restored, self-writing
  restorer), drop-test builds (one soft-bricked the tablet - steps lengthened without their timers), the pressure-index
  clamp, and the first summary.
- rev 12 - 23 (Python digital twin; Claude Opus 4.8, Max effort; detail in `history/claude-rev11-23.md`): bInterval 3 in `.data` found as the
  500/s cap, the AGC underflow / hard-fault, the digital USB twin and real-time scan twin, frame pacing locked to USB,
  11-bit frame-number wrap, the STOCK/QUICK/FAST/RAPID/ULTRA levels, search-threshold fix for ULTRA, hover boost,
  wiggle calibration with LED, the oddity detector (now Jump Rejection), and the summary uploaded as revision 23.
- rev 23 (its summary is in `history/claude-rev01-34-notes.md`): boot-window calibration, the oddity detector, all five levels passing the twin
  gate; on hardware FAST works, RAPID has ~7 mm hover jitter, ULTRA gave no input.
- rev 24 - 30 (C# patcher and twin; Claude Opus 4.8, Max effort; every finding is in `history/claude-rev24-30.md`). In brief:
- rev 24: C# patcher + twin, 3-stage hover, ULTRA pressure fix, long-range pickup, dual-coil search, physical pen model.
- rev 25: thresholds scaled to reading length (integrating receiver), REACH preset removed, aggressive interpolation
  (first version), measurements at 72 MHz, new screens.
- rev 26: two-pass Tablet Calibration with firmware telemetry.
- rev 27: input order (ic902-main first), AI notice, warnings under the question, xppen-tools backup, multi-platform.
- rev 28: flash counter in the bootloader area, Discreet, warranty warning.
- rev 29: Bootloader Counter (erase hook), --return-to-stock, gd32emu project.
- rev 30:
  - Bootloader Counter moved to `set_boot_state`: it counts only when leaving DFU state 2.
  - Aggressive Position Interpolation rewritten.
  - The patcher runs xppen-tools again (xppen-tools asks for its typed confirmation), commands shown without paths,
    and an already-in-DFU tablet continues straight to the next command.
  - summary.md renamed to AGENTS.md (renamed again to CLAUDE.md in rev 32).
- rev 31:
  - **Wizard reordered:** Tablet Calibration is now step 4 (before Settings, step 5), so its per-tablet thresholds
    are in hand when settings are chosen.
  - **Jump Rejection rewritten as a momentum model:** accept inside the L1 corridor between the stop point and the
    coast point (last + 1.5*velocity), widened by noise + margin; a spike is replaced by the plain coast, not frozen;
    after 3 rejections it is let through. A sudden stop is inside the corridor (the old constant-velocity predictor
    rejected stops). Moved beside Movement Smoothing (both under "Noise-related tuning").
  - **Filters fit every frame:** the fictional 35 us charge for Movement Smoothing was removed (the stock dead-band
    runs regardless, ~1 us), so Smoothing now fits ULTRA; Aggressive Interp charged ~1 us; each build logs its frame
    budget. At 1000 Hz Smoothing and Aggressive Interp fit singly but not together (the menu marks one N/A).
  - **Stock-vs-chosen cursor latency** measured in the twin and shown before building and in each build's measurements.
  - **Settings grouped into categories** (Noise / Latency / Hover and pickup / Experimental / Storage).
  - **Version string locked** to `AnargonSS727_<build date>` on every build (Discreet included); no longer an option
    (`--no-mark` removed, descriptor resized to fit).
  - **800/1000 Hz hover:** a found pen is acquired into Hover Boost so a weak hover is held instead of dropped, and a
    strong/close pen steps straight to the fast stage (full rate). Gated to RAPID/ULTRA; FAST/QUICK unchanged.
    Addresses Anargon's hardware report that 800/1000 still needed contact. Needs hardware confirmation.
  - **Bootloader Counter verified end-to-end in the twin** (install, count on leaving DFU state 2 only, idempotent
    re-boot, interrupted-install repair, write-protect safe-fail, return-to-stock) and **hardened:** `blinst` now
    checks the whole routine at 0x08002800 before touching the boot page, closing a partial-wrapper brick path.
    rev 31 also added a **bootloader-side self-heal** (wrapper fills any erased word of the hook page from a stored
    stock copy on every DFU-state boot; hook word committed last) so every reachable boot repairs an interrupted
    install. One residual window remains irreducible (reset runs the boot code in that page directly) - see open issue 3.
  - **Code-quality pass:** fixed stale `0x08000800` comments (the page is `0x08000C00`), the `--aggressive-interp`
    help, the AI notice (checks are run), and other stale text. The full C# is still translated to Java and run; all
    presets and option combos gate green against the stock `deco01v3-main.bin` (CRC-32 B65DB693).
- rev 32:
  - **Reset-vector self-heal** bundled (opt-in) with the Bootloader Counter: a repair stub reached from the redirected
    reset boot-jump repairs page 0x08000C00 on every boot, closing rev 31's interrupted-C00-install gap; the irreducible
    window is relocated to one reset-vector-page rewrite (opt-in only). Twin-verified end to end.
  - **Per-modification firmware compatibility check** before building, with loud full-screen SOME / ALL incompatibility
    pages and a caught-error "firmware may be unstable" page; continuing is an explicit forced build.
  - **Hidden PC flash-count store** with `[According to Tablet]` / `[According to PC]` provenance; one-time-only warnings.
  - **Docs restructured:** DESIGN.md -> overlay; per-project `anargons-tablet-tools/DESIGN.md` + `gd32emu/DESIGN.md`; new
    `FIRMWARE_DESIGN.md`; per-revision design detail moved into this file (section 7).
  - AGENTS.md -> CLAUDE.md (this file). The new AGENTS.md lists the models that worked on the project, the rules
    for forks and PRs, and requires every contributor to read every model's file; findings live in each model's file
    (section 0 here).
  - **Repositories split:** anargons-tablet-tools (this) and gd32emu are separate repositories, each with its own `.github`,
    `build.cmd` / `build.sh` at the top and the executable built next to them. `EMULATOR.md` explains how this one
    finds and uses the emulator (`GD32EMU_DIR`, `./gd32emu` submodule, or `../gd32emu` sibling clone).
  - **"DO NOT UNPLUG" page** after flashing a Bootloader Counter build (wizard and `--flash`): the first start rewrites
    the boot pages; estimated window ~85 ms per page (erase ~60 ms + 256 words; datasheet-typical figures, not
    measured), so the page asks for 10 s of uninterrupted power. Replugging right after a flash is the realistic way
    to hit the window. The setting description was corrected (it said "one page, about 50 ms").
  - All five presets and the bootloader-counter build gate green in the twin; the full bootloader lifecycle (install,
    self-heal, counting, write-protect, return-to-stock) re-verified with `--bl-boot` / `--bl-state`.
- rev 33 (Claude Opus 5.5; detail in `history/claude-rev33.md`): **any firmware build** - every firmware location is
  found by a masked byte signature instead of a fixed February address (`atf_layout.cs`, generated
  `atf_symbols.cs`, `tools/mksig.py`), for the patcher and the twin; the December build is supported and verified;
  the compatibility check covers every called function, table and literal; the max-pressure fix reads the table
  pointers of the firmware it patches; the firmware end comes from the scatter-load table; patch groups roll back
  completely when a location is missing. Follow-ups in the same revision: Discreet split into Volatile Changes /
  Bootloader Changes (section "Warranty and Firmware Discreetness", one-line tagline on every setting); calibration steps can be redone (Enter) or passed (Shift+Enter); "You are
  done!" after a flash; Tablet Calibration reads every HID collection; pressure and interpolation checks (section 0);
  project renamed "Anargon's Deco 640 Tools".
- rev 35 (Claude Opus 5.5; `history/claude-rev35.md`): project renamed **Anargon's Deco 640 Tools**
  (`anargons-deco640-tools`, `AnargonsDeco640Tools`, namespace `AnargonsDeco640Tools`; renamed again in R45-rc3,
  to Anargon's Tablet Tools); first screen "Choose the
  tool:" 1 **Anargon's Tablet Firmware Mod** / 2 **Anargon's Basic Tablet Recovery** (`atf_repair.cs`,
  `atf_stockref.cs`; `--recovery`); damaged dumps refused as flasher input; Anomy's patcher and stock files;
  signatures regenerated (88; the 86 earlier ones byte-identical); all settings in `.env` (`atf_config.cs`,
  `.env.example`), recovery instructions point to the teardown document (`ADT_TEARDOWN_URL`); GitHub tests
  (`tests.yml`, `tests/run-tests.sh`) with the emulator and test dumps set by repository variables; first real .NET 8
  build of the project (until now only the Java translation ran here).
  Follow-ups in rev 35: WTFPL license; the flash count read from the tablet over USB string 173 (Bootloader /
  Firmware / local PC labels, tablet first); the USB IDs are constants (no `--pid` / `--runtime-pid`); the calibration
  step's HID warning removed; Windows builds and tests on GitHub use `build.cmd` (no .NET SDK); verifier fixes: the
  emulator models SWRSTF, the patcher programs the boot state last and triggers on the last settings word, an erased
  settings page is not damage.
  Later in rev 35: Linux (hidraw for every tablet node + usbfs string 173) and macOS (IOKit HID manager + USB device
  interface) tablet readers, `--setup` (udev rule / Input Monitoring); a tablet whose Bootloader Counter was installed
  by rev 33 or 34 keeps it: a rev 35 build (with or without the counter) writes 0 bytes in the bootloader area and
  reports it (twin: count 2 -> 3 by the update, string 173 `ADTC1:B:000003`).
- rev 34 (Claude Opus 5.5; `history/claude-rev34.md`): folder, executable and sources renamed (`anargons-deco640-tools`,
  `AnargonsDeco640Tools`, `atf_*.cs`, namespace `AnargonsDeco640Tools`, state dir `AnargonsDeco640Tools` with the
  old `deco640` one still read); LED as stock (calibration-pass build: lit, blinking while measuring); open issue
  8 closed: backup before every flash into `dumps/`, tablet build checked before flashing.

## 7. Revision notes, rev 1 - 34
The per-conversation notes of revisions 1 - 34 (deliveries, headlines, design detail, each conversation's corrections)
and the rev 23 summary that used to be Appendix A are in `history/claude-rev01-34-notes.md`; the full records are the
`history/claude-rev*.md` files.

## 8. Potential brick vectors
Everything known that could leave the tablet not working, how bad each is, and what guards against it. Plain-English
version: `DOCS/FIRMWARE_DESIGN.md`, section 16.

**Recovery tiers**

| Tier | What recovery takes | Why |
|---|---|---|
| A - reflash | run xppen-tools again (`dfu`, then `update` with a good image) | the bootloader and its update mode are untouched |
| B - vendor DFU pad | open the tablet, hold PA15 to ground while plugging in, then reflash | the bootloader still works, but the firmware no longer answers on USB, so it cannot be asked to enter DFU |
| C - ROM recovery | BOOT0 to 3.3 V (~1 kOhm) + UART on PA9/PA10, or SWD; write a full 64 KB dump | the bootloader itself is damaged; only the GD32's built-in ROM loader can still write flash |

Tier B and C pins come from the board photo, the bootloader code and the GD32 datasheet. Tier B has been used on this
tablet; tier C has not yet.

**Vectors**

| # | Vector | When it can happen | Tier | Guard | Evidence / status |
|---|---|---|---|---|---|
| 1 | Power lost during a normal firmware update | any flash | A | the boot state stays 2 (update in progress), so the bootloader stays in DFU | code analysis; not traced on hardware |
| 2 | Image without its `.data` (truncated dump, or a flasher that skips `.data`) | any flash | A - B | truncated inputs refused; only complete images built; the twin gate fails them | **happened in rev 3 - 5** (xppen-tools wrote only to `0x0800C188`); recovered with the `.data` restorer |
| 3 | Patching a firmware whose layout differs (wrong offsets) | building from a different firmware | B | every location found by signature (never a fixed address); unfound ones disable their modification, even when forced; compatibility check; twin gate | xppen-tools' own bInterval edit faults this build (rev 14); closed for moved code in rev 33 |
| 4 | Added code that hangs or overruns (timers, RAM overlap, assembler slips) | first boot of a bad build | A if USB still enumerates, else B | timing guard; twin gate (boot, USB, gain control, pen); assembler range and label checks | **rev 8 drop-test soft-brick**; rev 20 RAM overlap and rev 22 label collision caught |
| 5 | Forced build after the incompatibility warning | not offered | - | a mismatch stops the build; no confirmation bypasses the byte checks | rev 32 had an override; removed |
| 6 | Runtime hard fault (AGC reading past RAM) | while in use | not a brick: replug | AGC cap = samples - 2 | found rev 13, fixed rev 16 |
| 7 | **Bootloader Counter: reset-vector page rewrite** | first boot after the first counter flash | **C**, ~85 ms typical / ~150 ms worst | opt-in only; written before the boot-page hook; "DO NOT UNPLUG" page | rev 32; window estimated, not measured |
| 8 | Bootloader Counter: boot-code page rewrite | same boot | none: self-heals | repair stub runs before the boot code | twin-verified (rev 32) |
| 9 | Bootloader Counter: stub page rewritten by a later counter build | a later counter flash whose stub differs | **C**, ~85 ms | unplug page; fix proposed (fixed backup address) | open; does not occur today (0 bytes changed) |
| 10 | Return to stock | first boot of return-to-stock | **C** windows (page 0 and the boot page are rewritten) | redirect removed before the stub page is erased | twin-verified (rev 32) |
| 11 | Write-protected bootloader pages | counter install | none: nothing is written | verify after every write | twin-verified |
| 12 | Unknown bootloader version | building a counter build | none: refused | stock-word and boot-jump checks at build time | build-time check |
| 13 | **Image built from one firmware build, flashed onto a tablet running another** (e.g. a February-based image on a December tablet) | flashing without a dump of this tablet | A - B if the image is complete (bootloader, config, RAM and USB are identical between builds); worse only if the boards differ | input-file build warning; the tablet's own dump is preferred as input; since rev 34 a backup before every flash and a check of the tablet's build (string or code layout) against the image's source, full-screen stop on mismatch | Anargon's report of a third-party mod bricking a probably-December tablet; `FIRMWARE_VERSIONS.md`; open issue 8 |
| 14 | **Building from a December dump** (forced past the warning) | building | none since rev 33 | December is a verified build: every location found by signature and checked against the stored December addresses; builds pass the twin | rev 32 gap; fixed in rev 33 (open issue 9) |
| 15 | **A signature matching the wrong place** in an unknown firmware build | building from an unverified build | B | a location is used only if its signature matches exactly once; known builds are checked against stored addresses; BL sites must call the resolved target; the twin gate runs the result | rev 33; not seen in 2 real + 4 simulated builds |
| 17 | **Firmware end erased by a flasher that stops at `0x0800C188`** (rev 3 - 5, and two tablets in rev 35) | flashing with such a tool or file | A - B | flasher builds complete images and refuses damaged dumps; the repair tool restores it; since R42 restore images next to a dump that stop before the firmware end are renamed `*.INCOMPLETE-DO-NOT-FLASH` | rev 35: Anargon's and Anomy's dumps; R42: reproduced byte for byte by a 35208-byte restore image (older xppen-tools `deco01v3-stock.hex`) |
| 16 | Bootloader Changes / Bootloader Counter left on unintentionally | building | none (warranty only) | both default OFF since rev 33; Bootloader Counter needs Bootloader Changes | rev 33 |

**Builds with Bootloader Changes off (the default) avoid vectors 7 - 10 entirely**; with Volatile Changes off they
also never change `.data`.

**What happens when a tablet is flashed:** the firmware is told to enter DFU; the bootloader sets state 2, erases
`0x08003800`-`0x0800FFFF`, writes the new image, checks it, sets state 0 and resets into the new firmware. The
bootloader area (`0x08000000`-`0x080037FF`), including both counter pages, is never touched by an update. On the first
start afterwards a non-Discreet build adds one record to the firmware flash counter; with the Bootloader Counter
installed the bootloader adds one record itself when it leaves state 2; a counter build installs (first time only)
or verifies its bootloader routine. The PC store adds one for every flash this tool performs.

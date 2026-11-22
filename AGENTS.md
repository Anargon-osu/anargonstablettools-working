# AGENTS.md - read this first (every AI model and every contributor)

<img src="misc/AIdisclosure-header-docs.png" width="100%" alt="Note: This project is entirely VIBECODED, without any code quality checks performed by a human. You may find the project hard to work with.">

<img src="misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

This repository is **Anargon's Tablet Tools** (folder `anargons-tablet-tools`, executable `AnargonsTabletTools`; called `deco640` up to rev 33), a firmware builder - so far for the XPPen Deco 640 drawing tablet. It is maintained by
**Anargon** and has been developed with AI models. The chip emulator it depends on, **gd32emu**, is a separate
repository; see `DOCS/EMULATOR.md` for how the two connect.

You are probably working in a fork or on a branch for a pull request, together with a person: **your partner** (the
contributor you are working for). This page tells you who has worked here before, the rules every change follows,
and where the accumulated knowledge is.

---

## 1. Before you change anything: read every model file

Each AI model that has worked on this project keeps its own notes file at the top of the repository, named after the
model family (`CLAUDE.md`, and for example `GEMINI.md`, `GPT.md` or `CODEX.md` if those models contribute). **Each
file holds that model's discoveries, what it verified and how, and what it left open.** The findings are not repeated
here on purpose, so no single file goes stale.

- **Read all of them**, not just the one with your own name. Other models have found things you will otherwise
  rediscover the hard way, or contradict without knowing.
- If two files disagree, the more recent revision wins. Say so in your PR description so your partner and the
  maintainers can settle it.

Current model files: **`CLAUDE.md`** (history in `history/claude-rev*.md`; latest `claude-pr1.md`), **`CODEX.md`** (rev 36 audit, R37) and **`GEMINI.md`** (R45-rc5: the roadmap after R45).
Human contributors can start with `DOCS/CONTRIBUTING.md`; `DOCS/AUDIT.md` records the current release assessment.

---

## 2. Models that have worked on this project

| Revisions | Model | Made by | Notes / history | What it did |
|---|---|---|---|---|
| 1 - 11 | Claude Sonnet 5.5 (max effort) and Claude Opus 4.8 | Anthropic | `history/claude-rev01-11.md` | the first Python patcher and wizard; found that xppen-tools left `.data` erased and **recovered the bricked tablet** (`.data` restore, self-writing restorer); pressure clamp; first summary |
| 12 - 23 | Claude Opus 4.8 (Max effort) | Anthropic | `history/claude-rev11-23.md` | Python digital twin (USB, then real-time scan); bInterval and AGC root causes; frame pacing; the five levels; hover boost; wiggle calibration; oddity detector (now Jump Rejection) |
| 24 - 30 | Claude Opus 4.8 (Max effort) | Anthropic | `history/claude-rev24-30.md` | conversion to C# (one Windows build, no installs) with the twin; integrating-receiver thresholds; Tablet Calibration; flash counter; Bootloader Counter; emulator split |
| 31 - 32 | Claude Opus 4.8 | Anthropic | `history/claude-rev31-32.md` | Jump Rejection momentum model, calibration before settings, compatibility check and warning pages, reset-vector self-heal, PC flash-count store |
| 32 follow-ups | Claude Sonnet 5.5, Claude Opus 5.5 | Anthropic | `history/claude-rev31-32.md` | brick-window calculation, risk review, unplug warning, documentation and repository split |
| 33 | Claude Opus 5.5 | Anthropic | `history/claude-rev33.md` | any firmware build: every firmware location found by signature (patcher and twin), December build supported and verified, compatibility check covers every call and table, real firmware end |
| 34 | Claude Opus 5.5 | Anthropic | `history/claude-rev34.md` | renamed to `anargons-deco640-tools` / `AnargonsDeco640Tools` (R45-rc3: `anargons-tablet-tools`); LED back to stock (calibration-pass build blinks while measuring); backup before every flash into `dumps/` and the tablet's firmware build checked before flashing (open issue 8) |
| 35 | Claude Opus 5.5 | Anthropic | `history/claude-rev35.md` | anargons-basic-tablet-recovery (repairs a damaged tablet from its main.bin; Anomy's patcher and stock files), start-up menu, plural name |
| 36 candidate | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]) | OpenAI | `CODEX.md`, `history/codex-rev36.md` | calibration routing/profile and stock frequency defaults; startup full backups; dry-run/exit-status safety; strict inputs, emulator isolation and fail-closed checks; OS/test/release audit |
| 37 | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]) | OpenAI | `DOCS/model-provenance.md` | console/input and subprocess fixes; native HID diagnostics; resonance readout; naming and separate-project delivery |
| 38 | Claude (exact version not recorded) | Anthropic | `CLAUDE.md`, `history/claude-rev38.md` | AI disclosure restored on every screen (rule 12); startup dump runs alongside the steps again; one backup per run; xppen-tools' typed UPDATE confirmation can be answered (no hidden console); helper output layout |
| 39 | Claude (exact version not recorded) | Anthropic | `CLAUDE.md`, `history/claude-rev39.md` | Step 1 option to build from this run's tablet backup; order-independent output-layout check; first full runs with both stock fixtures |
| 40 | Claude (exact version not recorded) | Anthropic | `CLAUDE.md`, `history/claude-rev40.md` | calibration readiness check retries (automatic re-checks, then check again / flash again / defaults) instead of ending calibration; accurate no-readings message |
| 41 | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-rev41.md` | **found why the PC read nothing on Windows** (Windows holds the pen collection; read-only probe on the real tablet) and rewrote the Windows reader: pen collection through Raw Input, every collection listed with its result; confirmed on hardware |
| 42 | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-rev42.md` | **found why a complete dump with another mod's changes was called damaged** and how the rev 35 tablets lost their firmware end (a 35208-byte xppen-tools restore image, reproduced byte for byte); edits at known patch sites are undone and checked against stock, only an erased end is damage, cut restore images next to backups are renamed |
| 43 | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-rev43.md` | tablet tests with the line recorder: coil grid, line floor and the coils' measured response; Multi-coil Read replaces Dual-coil Mode; **found that stock rounds every ADC reading to a multiple of 8** (12-bit Readings: 3.0x less cursor shake than stock ULTRA at a low hover, on the tablet); window-width check |
| 44 | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-rev44.md` | calibration made simpler (Anargon's spec): the LED marks each step's target (on within 7 mm of the middle, blinking while recording) through new USB strings in the measurement firmware; no-cursor warning; 4096 light touch; LED-worded detection limit; untimed slow lift; plain tablet-driver wording |
| 45 | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-rev45.md` | calibration levels (Fast Pass, Standard, Full); ALPI trained on the tablet, measuring the pick-up levels in the background; Frequency Hold and Coil Hold for ULTRA's still-pen noise; the bootloader's SysTick switched off; other mods' and this tool's builds set back to exact stock; hover stages that do not flap, a reach stage that keeps a tracked pen higher, Tilt Frame Skip; Misc. Optimizations (Switch Match, Window Scale, Coil Linearization, Dynamic Anti-Chatter); Measurement Tools; renamed Anargon's Tablet Tools, `DOCS/porting-guide.md` |
| 45-rc5 | Gemini Hydrogen 4 (xHigh) | Google | `GEMINI.md`, `DOCS/roadmap-after-r45.md` | the roadmap of pull requests after R45 (relayed by Anargon): source-level cold-read and coil-wave calibration, a 3 + 3 line fast stage with ALPI, gd32emu clock modelling, an in-spec 96 / 108 MHz option, more than 1000 positions per second with batched USB and an OpenTabletDriver plugin; with its warnings against a +0.4 ms filter, mid-scan processing and 120 MHz |
| PR1 (R46, V1.1) | Claude Opus 5.5 | Anthropic | `CLAUDE.md`, `history/claude-pr1.md` | Coil Gains in Misc. Optimizations (ULTRA): **found that slanted lines bend because each coil reads the pen a little stronger or weaker than the next** (X 1.2 %, Y 0.7 %) and evened them out before the position, at 58.9 % of the measured difference; on the tablet the error that repeats on every pass fell from 0.021 to 0.009 mm |

All of these keep their findings in **`CLAUDE.md`** (one file per model family); the `history/` files are the full
per-revision record each Claude session wrote, including its own corrections.

**When you contribute:** add a row (revisions, your model name and maker, your notes file, what you did), create or
extend your own notes file, and add a `history/<model>-rev<from>-<to>.md` for the revisions you delivered (after R45: one `history/<model>-pr<X>.md` per pull request).
**Revision names:** up to R45 they count deliveries across the whole project (Anargon's numbering). After R45 a
revision is named after its pull request, `PR #X` (section 4); use that name in your row and history file. Each merge moves the project one revision on: PR1 merged is R46, the next merge R47, and so on. Planned releases: V1.0 = R45, V1.1 = R46 (no release candidates), V1.2 = R47 to R56 (low-level changes and improvements across the project; where it ends depends on their scope). If you are unsure of your exact model version, say so instead of guessing. Human
contributors are welcome to add themselves too.

---

## 2a. Model advice: what a model needs for this project (Anargon, R45-rc5)

**Bare minimum: a 32B-class model working as a multi-agent session** (one model plans, writes the hard parts and
reviews; others work on pieces). Anargon set this after the local-model tests below (2026-11-16). The reason is the
project's structure, not its size: the firmware changes are C# that emits Thumb-2 instructions (`Asm`), each one
checked against a C# copy in the digital twin, in a C# 5 subset, with fixed RAM addresses, firmware signatures and
one shared frame budget. A change is rarely one file on its own. Every model of 3.7B to 24B parameters tried here
failed at that, even with an exact written guide, the real function signatures and a gate that compiled and ran its
code. The largest model tried was 24B; 32B is Anargon's floor, not a tested result.

**What was tried.** The vibeslop local swarm (Anargon's own tool: llama.cpp on an 8 GB Radeon RX 5700 XT, 4-5 bit
models, several answers per round, every answer built and tested by gates before it is kept). Claude (Opus 5.5)
wrote each task's guide and gates.

| Model | Task | Result |
|---|---|---|
| OmniCoder-9B ("Opencode") | Dynamic Anti-Chatter routine (one firmware file, about 200 lines) | first real run: compile errors (211 lines); in the bake-off 1 pass in 4 tries (round 1, 5 min), the other 3 stuck on the same failure (compiles, 1-2 of 3 test gates) |
| OmniCoder-9B | `experiments/noise45/lagsim.cs`, an offline replay of ALPI (about 150 lines of C#) | out of time (60 min); one answer rebuilt the ALPI model every frame, so each check took about 100 s |
| Devstral Small 2 24B (partly in system RAM) | Dynamic Anti-Chatter routine | alone: compiles, 2 of 3 test gates after 54 min; taking over when OmniCoder was stuck: passed once (round 2, 43 min) |
| Devstral Small 2 24B | the ALPI replay | 2 rounds in 31 min, compile errors, and the structure wrong (direction of travel mixed up with the axis, one axis per direction, wrong averages); Claude kept its argument parsing and rewrote the rest |
| Qwen2.5-Coder-14B (partly in system RAM) | Dynamic Anti-Chatter routine | did not compile in 60 min |
| Qwen3.5-9B, Ministral 3 8B, Qwen3-8B | Dynamic Anti-Chatter routine (30 min each) | compile, 2 of 3 test gates |
| Qwen2.5-Coder-7B, Ornith-1.5-9B | same | compile, 1 of 3 test gates |
| Seed-Coder-8B-Instruct, LFM2.5-8B-A1B, rnj-1 8B, Ministral 3 8B Reasoning, K2-Horizon-3.7B | same | did not compile |
| Seed-Coder-8B-Reasoning, Granite 4.2 8B, Gemma 4 E4B, Falcon-H1R-7B, Nemotron Nano 9B v2, DeepSeek-R1-0528-Qwen3-8B, K2-Horizon-7B | same | no usable file (repeating itself, looping, or thinking until the token limit) |
| OmniCoder-9B, then Devstral Small 2 24B (multi-agent: OmniCoder first, Devstral after its failures) | interface only: the Filter Arranger screen (`atf_arranger.cs`, about 100 lines of menus and text; every tablet action is a call into `ArrangerOps`, which Claude wrote) | OmniCoder: 2 rounds, compile error (one local variable name used twice); Devstral: **passed** in its second round (15.8 min), all 15 checks of the gate, which runs the screen against the digital twin. |
| OmniCoder-9B | interface only: the same screen laid out like the settings screens (layout v2, 2026-11-17; the outline now said to declare the result variable once) | **passed** in its first round (4.3 min), all 17 cases of the new gate. In the product unchanged. |

**How they failed:**
- **Compile errors they could not fix:** undeclared names, clashing variable names, and the same mistake again
  round after round.
- **Right syntax, wrong structure:** code that compiles but misreads what a value is (an axis taken for a
  direction), does work in the wrong place (a model rebuilt every frame), or computes a different measurement than
  the one asked for.
- **Reasoning models** spent their whole answer thinking or repeated themselves, and wrote no file.

**Interface code is the exception (Anargon: local models MAY be enough for the interface).** The one local-model
success is a screen, not firmware: the architect wrote the functions that do the work (`ArrangerOps`: reading the
tablet, sending, checking, the error texts), an exact outline of the screen, and a gate that types answers into it
in the twin; the model only had to show things and route each answer to the right function. That split is the way
to use them for interface work: the model never writes anything that talks to the tablet or emits instructions.

**How to use local models here, if at all:**
- Only for a small, self-contained piece (a screen that calls finished functions, an analysis tool, a test helper),
  with an exact guide and a gate that runs the code and checks its results, not just a compile.
- The architect (a 32B+ multi-agent session or a large cloud model) writes firmware routines and anything that
  spans several files, and reads every answer that passed before it is kept: a passed gate proves the code runs,
  not that it is right.
- Local models never touch the tablet (the swarm refuses gates that flash it or read from it).

---

## 3. Rules for every change (Anargon's zero-trust model)

Follow these in your fork and in every pull request:

1. **Never patch blind.** Every change to the firmware first checks that the exact stock code is at that spot
   (`Image.Expect`). A mismatch stops the build. The interactive tool does not override byte checks or timing limits.
2. **Never flash unchecked.** Every build boots in the digital twin and passes the boot / USB / gain-control /
   pen-detection checks before it can be offered for flashing. Do not weaken or skip this gate.
3. **Do not touch the bootloader unless your partner explicitly asks.** Normal builds change only the firmware area
   (`0x08003800` and up). Bootloader writes exist only in the opt-in Bootloader Counter and in return-to-stock.
4. **Verify before claiming.** "Works" means shown in the twin or on a real tablet, and you say which. Anything not run
   on a real tablet is labelled that way, in your notes file and in your PR description.
5. **State the risk plainly.** If something cannot be made fully safe, say so and give the size of what remains. Do
   not hide it from your partner.
6. **Keep the shared C# subset** (C# 5: no generics, LINQ, `out`/`ref` or unsigned types). It must compile with
   Windows' built-in `csc.exe`. The project needs nothing else: no Java, no packages (R42 removed the Java
   cross-check, `tools/cs2java.py`).
7. **Keep console output ASCII**, worded for a casual user.
8. **Record what you learn in your own model file**: each finding, who found it, and how it was verified (code
   analysis, twin, or hardware). Mention the new findings in your PR description.
9. **Changes to the emulator belong in the gd32emu repository**, as a PR there. Do not copy it into this repository.
10. **Never hard-code a firmware address.** Write it as its February address through the layout - `Fw.A(0x0800....)`,
   or `Fw.Ptr(...)` for a word stored there - and regenerate `atf_symbols.cs` with `tools/mksig.py` (see
   `DOCS/DESIGN.md`). The layout finds the location in whichever firmware build is loaded and refuses when it cannot;
   an address without a signature stops the build. If you verify a new XP-Pen build, add it to `Layout.KNOWN` and as a
   reference dump to `mksig.py`.
11. **NEVER change where or how the flash count is kept and read - not in any revision, fork or PR.** Tablets in the
   field carry it, and every older and newer copy of this tool reads it:
   - the counter page **`0x08002C00`** in the bootloader area: `+0 "AnSC"` (0x43536E41), `+4 base`, `+8 ~base`, then
     8-byte records `{id, ~id}` from `+16` (count = base + valid records); bootloader records use id `0xB007B007`;
   - the Bootloader Counter's routine page **`0x08002800`** and its hook at `set_boot_state` (`0x08000F40`);
   - the USB readout: **string descriptor index 173**, text **`ADTC1:<B|F|N>:<6 digits>`** (B Bootloader Counter
     installed, F firmware counter, N no counter), built in RAM at **`0x20001F80`** (`Patches.CounterString`).
   If something new is needed, add it next to these (a new string index, a new record id) and keep the old ones
   working; never move, reformat or reuse them. The PC side prefers the tablet (Bootloader, then Firmware) over the
   local PC copy, which counts only the flashes that PC performed.
12. **NEVER remove the AI DISCLOSURE box - not in any revision, fork or PR.** Anargon requires it on screen at all times
   through all steps. `Screen.AI_DISCLOSURE` / `Screen.Disclosure()` (atf_cli.cs) print it at the top of every
   `Screen.Step` and `Screen.DangerAsk` screen and first in every other run (only `--help`, `--version`, `--setup`
   and `twin` commands print without it). Do not shorten, reword,
   restyle, hide or make it optional, and keep `HostRegressions.Disclosure` passing. New screens must go through
   `Screen.Step` / `Screen.DangerAsk` (or call `Screen.Disclosure()`) so they show it too.
13. **Always show the flash count against the chip's rated minimum: `N/100,000`.** The GD32F350 datasheet rates its
   flash for at least 100,000 cycles; that is the figure, not one tested on these builds. Anargon asks for it as a
   standard courtesy of any firmware tool. Keep `Cli.CounterText` and the
   calibration readout showing it, with the source label.
14. **Write every on-screen explanation for a normal person: short, plain words, formatted.** See section 3a.
15. **Never put XP-Pen firmware bytes in the source or the docs.** Store what the tool needs as checksums or
   fingerprints: `atf_stockref.cs` holds CRC-32s, `atf_symbols.cs` holds masks and SHA-256 fingerprints (written by
   `tools/mksig.py`). A repair takes firmware bytes from the user's own files and checks them against those
   checksums. Never commit dumps or built `.hex` files. What is left (R42): single-instruction checks and search
   patterns of 1-7 instructions.
16. **Describe a coil by its centre and its effective range, never by a shape.** Anargon's coils may be multi-layered
   and not circular. The effective range is the distance from a coil's centre, in coil pitches, where its response
   falls to half of the response at the centre. Responses are measured, not assumed: no bell curve, sigma or loop
   width in code, docs or output. R43 measured them (`MultiCoil.RESP`, `rev-output/hw43`).
17. **Write every document for a cybersecurity student who does not know assembly.** Documents (everything under
   `DOCS/`, the guides and tutorials) explain in full; the program's screens stay short (rule 14). See section 3b.
18. **Every tablet measurement goes into Measurement Tools (tool 3), not into a script of your own.** Anargon
   (2026-11-17): models are "required to contribute to the measurement suite instead of having them run their own
   py's". A test or measurement of a real tablet (still-pen noise, ruler lines, hover height, drags, comparing
   filters or builds), and the scoring that turns its readings into a result, is an entry in `atf_measure.cs`
   (`IMeasurement`, listed in `MeasurementTools.ALL`) with its own gate in the digital twin, under every rule above.
   The Python scripts in `Claude-workarea/experiments` are the record of earlier work: when you need one again, port
   it into Measurement Tools rather than running or copying it. Offline simulations that never read a tablet may
   stay scripts. Testing tools stay out of the patcher (tool 1): the Filter Arranger lives in Measurement Tools.

---

## 3a. How the program should explain things (Anargon, R42)

Explain how it works as you would to a normal person. Keep it as short as possible, and lean on formatting
(headings, one fact per line, short notes in brackets) instead of long sentences. No register names, no
"signal-threshold measurement profile": say what the person sees ("the cursor shakes less", "the pen is found from
higher up"). Each setting is one line of what it does and one `Cost:` line.

Anargon's example of the right style (measurement output; `Cli.Block`, `Cli.Terminology`):

```
Measurements

STOCK
  Cursor delay - about 17 ms while drawing, about 23 ms while hovering (220 RPS)
  (Hovering is slower mostly because of stock Movement Smoothing, partly Position Interpolation)
  One frame every 4.55 ms

500 Hz [FAST] with Steady Hover
  Cursor delay - about 10 ms while drawing, about 18 ms while hovering
  (Steady Hover: 250 RPS)
  One frame every 1.99 ms, processed for 0.09 ms per frame

500 Hz [FAST] without Steady Hover
  Cursor delay - about 10 ms while drawing, about 10 ms while hovering
  One frame every 1.99 ms, processed for 0.09 ms per frame

800 Hz [RAPID] with Steady Hover
  Cursor delay - about 6 ms while drawing, about 12 ms while hovering
  (Steady Hover: 398 RPS)
  One frame every 1.25 ms, processed for 0.09 ms per frame
```

Every measurement output ends with the terminology:

```
Terminology:
    These numbers come from a pen sliding steadily on a digital copy of Anargon's tablet. It copies how
    the tablet behaves; it is not a physical test or a full 3D physics model of the tablet.

    Drawing = the pen touching the tablet (any pressure). Hovering = the pen at the farthest point
    where it is still detected.
```

Names: **Steady Hover** (was Hover Boost until R42) trades report rate for a steadier pen high up. With every
hover helper off (Steady Hover, Reach Stage, Long-range Pickup) a file is named `-rawhover`, for players who hover as
a skill. Files that change `.data` start with `[VOLATILE]_`, e.g. `[VOLATILE]_rapid.hex`.

---

## 3b. How the documentation should explain things (Anargon, R45-rc3)

The documents have a different reader from the program's screens. Section 3a is for what the program prints; this
section is for `DOCS/` (the firmware description, the guides, the tutorials, the design notes) and for the technical
parts of the README files.

**Write for a university student in cybersecurity** who can read and write an ordinary programming language (C, C#,
Python), knows what a pointer, a bit mask and a hexadecimal number are, but has never read assembly and has never
worked with a microcontroller. Anargon's words: "semi complex and verbose explanations".

- **Explain, do not only state.** Say what a thing is, why it is there and what would go wrong without it. A paragraph
  of reasoning is welcome; the reader wants the mechanism, not only the result.
- **Define every hardware or assembly term where it first appears**, in a sentence or a bracket: register, vector
  table, interrupt, Thumb code, flash page, ADC, DMA, USB endpoint, descriptor, bootloader. After that, use the term
  freely.
- **Translate machine code into ideas the reader already has.** When an instruction matters, say what it does in
  C-like terms next to it; for example `ldr r0, [r1, #4]` reads the 32-bit value at address `r1 + 4` into `r0`, like
  `r0 = *(uint32_t *)(r1 + 4)` in C. Prefer C-like pseudocode to listings, and never copy XP-Pen bytes (rule 15).
- **Use worked examples with this project's real numbers** (addresses, timings, measured values) and say how each was
  established: code analysis, the digital twin, or the real tablet.
- **Keep the structure:** headings, short sections, tables for reference data, and a "why it matters" note on anything
  that can stop a tablet from starting.
- **Verbose means complete, not padded.** Every sentence should teach something; cut filler.

---

## 4. Pull requests

**Every revision after R45 comes as a pull request from a GitHub account linked to a person** (Anargon: for
identity). An AI model works through its partner's account: when you start, look up that account's pull requests in
`Anargon-osu/anargons-tablet-tools` and the revisions already named in `history/`, and name your revision `PR #X`,
where X is the number of the pull request it goes into (for example `history/claude-pr12.md`); merged, it becomes the next revision (PR1 -> R46). Do not push, post or
open pull requests yourself unless your partner asks you to.

A good PR description for this project says:
- what changed, and which presets or settings it affects;
- how it was verified: twin checks run (all five presets gate?), hardware tested or not;
- any new risk and its size;
- which model files you updated.

---

## 5. Where to start

| You want to... | Read |
|---|---|
| understand how the tablet firmware works | `DOCS/FIRMWARE_DESIGN.md` |
| know how the February and December stock builds differ, with every offset, and how the tool handles any build | `DOCS/FIRMWARE_VERSIONS.md` |
| change the builder or the digital twin | `DOCS/DESIGN.md`, then the code |
| study another tablet's firmware (any Cortex-M tablet with a coil grid), or check this one's from scratch | `DOCS/porting-guide.md`, `tools/survey.py` |
| understand or change how this repository uses the emulator | `DOCS/EMULATOR.md`; the emulator itself is [Anargon-osu/GD32EMU](https://github.com/Anargon-osu/GD32EMU) |
| add your own firmware change, step by step | `DOCS/tutorial-self-modding.md` |
| write a patcher package (a tablet's patching steps and options, as raw C# source), or ship a change as one instead of a rebuilt tool | `DOCS/patcher-packages.md`, `DOCS/tutorial-patcherpkg.md` |
| recover a tablet that does not start | the [`recovery` folder](https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery) of Anargon-osu/deco640-resources, `DOCS/FIRMWARE_DESIGN.md` section 16 |
| look at one firmware behaviour in the twin without writing a new harness (ladder steps by height, drive steps per frame, hover pressure) | `misctests/README.md`, `python -I misctests/run.py --list` |
| get the stock dumps the tests use | [Anargon-osu/deco640-resources](https://github.com/Anargon-osu/deco640-resources) (`ADT_TEST_DUMPS_REPO` clones it) |
| know what has been found, done, left open, and what to test on hardware | every model file (`CLAUDE.md`, ...) |
| build and run | `README.md` |

R37 keeps these repositories separate. Each project has its own root README.md and AGENTS.md; CLAUDE.md and CODEX.md remain root model entrypoints, history/ remains the durable revision record, and other documentation is under DOCS/. See [model provenance](DOCS/model-provenance.md) for the confirmed R36 and R37 model/effort record.

This is a rolling release. Revision labels such as R37 identify project deliveries up to R45; after R45 a revision is named after its pull request (`PR #X`, section 4). Each merge is the next revision (PR1 merged = R46, then R47, ...); release names group revisions (V1.0 = R45, V1.1 = R46, V1.2 = R47 to R56) and never renumber them. Every future model must read all root model files and the latest files under the tablet repository's root history/ before changing anything. Add a complete history entry for each delivery so future work does not depend on persistent chat context.

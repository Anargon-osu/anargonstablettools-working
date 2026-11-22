<img src="misc/anargonstablettools-logo.png" width="371" alt="Anargon's Tablet Tools">

<img src="misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

![Tablet](https://img.shields.io/badge/tablet-XPPen%20Deco%20640-0a7bbb)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-555555)
![Language](https://img.shields.io/badge/C%23-5-239120)
![License](https://img.shields.io/badge/license-DHMA--FTW--PL-lightgrey)

**Custom firmware for the XPPen Deco 640, built and checked on your own PC.**
Up to 1000 pen reports a second, a steadier cursor, a pen that holds higher in the air, and calibration to your own
tablet. Every build is tested in a digital twin of the tablet before it can be flashed.

<img src="misc/AIdisclosure-header-docs.png" width="100%" alt="Note: This project is entirely VIBECODED, without any code quality checks performed by a human. You may find the project hard to work with.">

## Contents

- [Contents](#contents)
- [Features](#features)
- [Supported tablets](#supported-tablets)
- [Getting started](#getting-started)
  - [Requirements](#requirements)
  - [Build](#build)
  - [Run](#run)
- [The three tools](#the-three-tools)
  - [Tablet Firmware Mod](#tablet-firmware-mod)
  - [Measurement Tools](#measurement-tools)
- [Presets](#presets)
- [Settings](#settings)
  - [Step 5: Noise-Related Patches](#step-5-noise-related-patches)
    - [Reading the coils](#reading-the-coils)
    - [Position corrections](#position-corrections)
    - [Position filters](#position-filters)
    - [Noise-related tuning, ALPI and experimental hardware](#noise-related-tuning-alpi-and-experimental-hardware)
  - [Step 6: Extra QOL Patches](#step-6-extra-qol-patches)
    - [Hover](#hover)
    - [What the build may change on the tablet](#what-the-build-may-change-on-the-tablet)
- [Calibration](#calibration)
- [Backups and flashing](#backups-and-flashing)
- [Recovery](#recovery)
- [Command line](#command-line)
- [Configuration](#configuration)
- [Documentation](#documentation)
- [Repositories](#repositories)

## Features

| Feature | What you get |
|---|---|
| **Higher report rates** | Five presets, from stock (about 220 reports/s) to ULTRA (1000 reports/s). |
| **A steadier cursor** | Frequency Hold, Coil Hold, 12-bit Readings and Misc. Optimizations remove the main causes of still-pen shake and of the small steps between coils. |
| **Better hover** | Steady Hover, Reach Stage and Long-range Pickup keep the pen tracked higher in the air. Variable rate depending on noise |
| **Calibration** | Three different variants of calibration, including ALPI (Algorithmic Position Interpolation), trained on your tablet while you use it normally. |
| **Tested builds** | Every build is run in a digital twin of the tablet ([`Anargon-osu/GD32EMU`](https://github.com/Anargon-osu/GD32EMU)) before it can be flashed. |
| **Back to stock** | Firmware modified through known prior patches, or by this tool, is set back to exact stock before a build. `--return-to-stock` undoes every change. |
| **Recovery** | Anargon's Basic Tablet Recovery repairs the known damage patterns. |
| **Patcher packages** | Patchers written for other tablets, as raw C# source, each approved by you before it runs. |
| **Measurement Tools** | Still-pen noise, ruler lines and filter order, measured on a real tablet. |

## Supported tablets

<img src="misc/disclosure-firmwarerev-docs.png" width="100%" alt="Note: Your firmware revision may be different to the firmware this primarily targets. Firmware versions tested for are listed below in this README. This project will warn if any changes are incompatible, so this may not matter to you.">

| Tablet | Firmware build | Released | Tested |
|---|---|---|---|
| XPPen Deco 640 | `2025-02-14_release001` | February 2025 | digital twin and hardware |
| XPPen Deco 640 | `2025-12-23_release001` | December 2025 | digital twin (every preset passes) |

> [!NOTE]
> Other firmware builds are patched wherever their code is found by signature. A change whose code is not found is
> left out, and the tool says so.

> [!WARNING]
> The tools work only with the tablets listed here. Recovery and Measurement Tools have had less testing than the
> Tablet Firmware Mod.

## Getting started

<img src="misc/disclosure-GD32dependency-docs.png" width="100%" alt="Note: This project depends on the GD32EMU project, which allows for the digital testing of your tablet through emulation, including both software and hardware tests. Because it is a dependency, builds in this repository bundle this project.">

### Requirements

| | Windows | Linux and macOS |
|---|---|---|
| **Compiler** | the .NET Framework 4 C# compiler that ships with Windows (nothing to install) | the .NET 10 SDK |
| **Build script** | `build.cmd` | `bash build.sh` |
| **Program** | `AnargonsTabletTools.exe` | `AnargonsTabletTools` |
| **Tablet access** | built in, no extra driver | run `AnargonsTabletTools --setup` once (udev rule or Input Monitoring) |

Also needed:

- **GD32EMU** (`Anargon-osu/GD32EMU`), placed beside this folder as `../gd32emu/`, or set with `GD32EMU_DIR`. The
  build compiles it into the program.
- **xppen-tools**, for backups and flashing. Builds can be found under
  [`NiceAesth/xppen-tools-builds`](https://github.com/NiceAesth/xppen-tools-builds); place one next to the program,
  or set `ADT_XPPEN_TOOLS`.

> [!NOTE]
> Windows is the main platform, and the one tested with a real tablet.

> [!CAUTION]
> No testing has been done on MacOS and Linux as of yet.

### Build

From this folder:

```bat
build.cmd
```

```bash
bash build.sh
```

Both scripts build the program next to themselves, then run the digital twin's self-test.
`AnargonsTabletTools --version` shows which build you have.

### Run

```text
AnargonsTabletTools
```

With no arguments the program opens its menu. In PowerShell type `.\AnargonsTabletTools`, on Linux and macOS
`./AnargonsTabletTools`.

On start it makes four folders next to itself:

| Folder | What goes there |
|---|---|
| `import/` | a stock firmware file of your own, if you want to build from one |
| `export/` | built firmware and logs |
| `dumps/` | the tablet backups this program makes |
| `patcherpkgs/` | patcher packages; when one is there, the first screen lets you choose it instead of this tool |

> [!CAUTION]
> A patcher package runs with your account's full access. Read [patcher packages](DOCS/patcher-packages.md) before
> you approve one.

## The three tools

| # | Tool | What it does |
|:-:|---|---|
| 1 | **Anargon's Tablet Firmware Mod** | Builds, tests and flashes custom firmware, step by step. |
| 2 | **Anargon's Basic Tablet Recovery** | Repairs a tablet whose firmware was damaged. See [Recovery](#recovery). |
| 3 | **Anargon's Measurement Tools** | Tests on a real tablet, for development. |

### Tablet Firmware Mod

| Step | Screen | What you choose |
|:-:|---|---|
| 1 | Stock firmware file | the backup read from the tablet at start (`b`), or a file of your own |
| 2 | Output folder | where the builds go |
| 3 | Preset | how often the tablet reads the pen: see [Presets](#presets) |
| 4 | Calibration | Fast Pass, Standard or Full: see [Calibration](#calibration) |
| 5 | Noise-Related Patches | how positions are read, corrected and filtered: see [Settings](#settings) |
| 6 | Extra QOL Patches | hover, and what the build may change on the tablet |

The build is then tested in the digital twin, and can be flashed.

<details>
<summary><b>Firmware modified by another mod, or by this tool</b></summary>

<br>

- Certain prior modded files of the IT640 are recognised by their name, as long as
  every change is at a place this tool knows.
- For each change the tool shows what it was and how it was made: the instruction or data byte, its address, the
  stock value and the value found.
- Each change is set back to its stock value. The result must be exact stock firmware (every code page's checksum
  and the whole firmware's CRC-32), and the build is made from that. The other mod's settings are not carried over.
- **A build of this tool** is set back the same way, from the undo record every build carries. Older builds are set
  back from a stock copy of the same firmware near them (your first backup, or `deco01v3-main.bin`).
- Firmware changed anywhere else, and firmware with an erased end, are refused with the reason. For an erased end,
  use Anargon's Basic Tablet Recovery first.
- Compatibility and timing failures stop a build. No confirmation bypasses the byte checks.

</details>

### Measurement Tools

| # | Entry | What it measures |
|:-:|---|---|
| 1 | **Filter Arranger** | Which position filters run, and in which order, on a tablet running a `--lab` build. Kept in RAM; nothing is flashed. `c` turns the cursor off. |
| 2 | **Still Pen Noise Test** | Holds of 1 to 60 s with the pen still: the shake, the widest swing and the steps, with every spike kept in. |
| 3 | **Tablet signal thresholds** | Standard calibration's measuring on its own. Afterwards it offers to flash back the firmware the tablet ran before. |
| 4 | **Ruler Line Test** | A ruler along a target line: the error that follows the coils, forward against back, and steps. |

> [!IMPORTANT]
> Measurement Tools are for testing only. Only Tablet signal thresholds flashes the tablet, and only after you
> confirm.

## Presets

| Preset | Reports/second | Timing |
|---|--:|---|
| STOCK | about 220 | the vendor's scan timing, unpaced |
| QUICK | 300 | the full frequency sweep every frame |
| FAST | 500 | a 2 ms frame |
| RAPID | 800 | the sweep spread over two frames |
| ULTRA | 1000 | the sweep spread over six frames |

The names describe target timing, not a guaranteed result on every tablet.

> [!NOTE]
> RAPID and ULTRA need **Volatile Changes**, which changes how the tablet enumerates over USB (see
> [Settings](#settings)). `--discreet` and `--no-volatile` never turn it on silently: choose FAST or slower to keep
> the stock USB settings.

## Settings

Defaults suit most players. Some settings exist only on some presets; the screens show which. Every setting has a
command-line flag (see [Command line](#command-line)). The tag is what a build's file name shows when the setting
differs from its default.

### Step 5: Noise-Related Patches

#### Reading the coils

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| Accurate Pressure Readings | on | `AP` | Full-length frequency readings: no pressure shows while hovering, and light presses are not missed. | Fewer pressure updates (RAPID: 100/s instead of 300); light presses register about 20 ms later. Not available at ULTRA. |
| 12-bit Readings | on | `12B` | Keeps the 3 low bits the stock firmware drops from every coil reading. | Every pick-up and drop level rises by 4. Multi-coil Read is noisier with it at low hover. |
| Frequency Hold | on | `FH` | The scan's drive step moves only after 8 sweeps agree. | A real change in the pen's signal is followed up to 8 sweeps later. |
| Coil Hold | on | `CH` | The coil window moves by one coil only past a margin. | None measured. |
| Bootloader Timer Off | on | `TO` | Stops a 1 ms timer the vendor bootloader leaves running, which caused ULTRA's noise bursts. | None measured. |
| Tilt Frame Skip | on | `TS` | No tilt measurement while hovering close (those frames shake the cursor). | Tilt keeps its last value while hovering close. |

#### Position corrections

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| **Misc. Optimizations** | on | `MO` | Switch Match, Window Scale (ULTRA), Coil Gains (ULTRA), Coil Linearization and Dynamic Anti-Chatter, together. | Up to 0.4 ms of delay on the frames Dynamic Anti-Chatter pulls back (about 0.1 ms on average). Coil Gains and Coil Linearization are tuned on one tablet and pen, and Coil Linearization is slightly less accurate at hover. |

#### Position filters

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| Position Interpolation | on | `PI` | The stock 3/5 average of positions. | A little cursor delay. |
| Aggressive Position Interpolation | off | `AI` | An extra moving average of 16, 8 or 4 positions, by speed. | Much more delay (24 ms against 9 ms at FAST, in the digital twin). |
| Jump Rejection | on | `JR` | Rejects sudden jumps off the pen's path. | A real sudden move can lag by up to 3 frames. |
| Movement Smoothing | off | `SM` | A dead zone around the last position. | Very small movements are lost. |

#### Noise-related tuning, ALPI and experimental hardware

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| Pen frequency tuning | off | `FT` | Keeps the steadiest scan frequency, found by pressing hard within 3 s of plugging in. | **Experimental.** Needs the press after every plug-in; cannot be combined with a saved calibration. |
| ALPI: Tracker | off | `APIP` | The trained position filter. Replaces Position Interpolation, Movement Smoothing and Jump Rejection. | Needs Full calibration; without your own training it uses a reference training. |
| ALPI: Robust, 2 to 4 trackers | off | `APIPR2` to `APIPR4` | ALPI with more trackers. | More shake on a still pen than the Tracker. |
| Multi-coil Read | off | `MC` | The position from a fit of the five coils around the pen. Needs Standard calibration. | **Experimental.** No gain with ALPI; noisier on X at low hover with 12-bit Readings. |

### Step 6: Extra QOL Patches

#### Hover

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| Steady Hover | on | `SH` | Lowers the rate step by step while hovering, so the pen stays steady. | Fewer reports a second in the air. |
| Reach Stage | on | `RS` | A slow stage that keeps a tracked pen at the highest hover. | A low report rate at that height. |
| Long-range Pickup | on | `LP` | Searches with strong pulses, so the pen is found higher up. | None measured. |

#### What the build may change on the tablet

| Setting | Default | Tag | What it does | Trade-off |
|---|:-:|---|---|---|
| Volatile Changes | on | `[VOLATILE]_` | Changes the firmware's USB data: a 1 ms report interval instead of 3 ms. Needed for RAPID and ULTRA. | **The tablet enumerates over USB with a different report interval**, so drivers and other software can see it differently. A stock flash undoes it. |
| Bootloader Changes | off (on when the counter is found) | `BL` | Allows writes to the bootloader area (the flash counter). | **Voids the warranty.** These changes survive a stock flash; `--return-to-stock` removes them. |
| Bootloader Counter | off (on when found) | `BC`, `noBC` | The bootloader counts every update. Needs Bootloader Changes. Turned off while found, the flash removes it. | **Experimental.** On the first start after the flash, the bootloader rewrites its boot pages (to install it or to remove it): losing power then can need hardware recovery. Keep the tablet plugged in for 10 s. |

> [!NOTE]
> The tool checks the backup it builds from for the Bootloader Counter. **Found:** Bootloader Changes and Bootloader
> Counter start on, and the toggle says so. Turn the counter off to remove it with that flash (`-noBC`): the
> bootloader goes back to stock and the firmware keeps the count. Turn Bootloader Changes off to leave it as it is.

<details>
<summary><b>Misc. Optimizations, one by one</b></summary>

<br>

| Part | Tag | What it does |
|---|---|---|
| Switch Match | `SWM` | Removes the cursor step when the pen crosses into the next coil window (not while the pen is still). |
| Window Scale | `WS` | Corrects the stretch of the position inside a coil window, by pressure (ULTRA). |
| Coil Gains | `CG` | Evens out how strongly each coil reads before the position is worked out (ULTRA, needs Window Scale). Set to 58.9 % of the measured difference. No delay. |
| Coil Linearization | `CL` | A fixed correction of the position inside each coil, learned along a ruler. No delay. |
| Dynamic Anti-Chatter | `DAC` | Pulls a frame back only where the noise is over 0.10 mm. |

Order in the firmware: Switch Match > Coil Linearization > ALPI > Dynamic Anti-Chatter; Window Scale and Coil Gains
work inside the tablet's own position routine. Measured on a Deco 640 (ULTRA with ALPI, along a ruler): 0.02-0.04 mm
of error left along an axis, 0.06-0.09 mm on a slanted line without Coil Gains. Coil Gains on the same tablet: the
slanted-line error that repeats on every pass fell from 0.021 to 0.009 mm (at 60 %), and 6-25 % of the error came off
every recorded ruler line but one (offline, at 58.9 %). How much of the coil difference shows changed between
sessions there (from almost none to 2.4 x the measured one), so another tablet may gain less. Each part can be turned off on its own on the command
line.

</details>

<details>
<summary><b>How build files are named</b></summary>

<br>

`[VOLATILE]_` (when Volatile Changes is on) + the preset + `-TAG` for each setting turned on that is off by default,
or `-noTAG` for each default turned off.

| Example | Meaning |
|---|---|
| `[VOLATILE]_ultra.hex` | ULTRA, every setting at its default |
| `[VOLATILE]_ultra-noMO.hex` | ULTRA without Misc. Optimizations |
| `[VOLATILE]_rapid-MC.hex` | RAPID with Multi-coil Read |
| `[VOLATILE]_ultra-BL-noBC.hex` | ULTRA that removes the Bootloader Counter |
| `fast.hex` | FAST with Volatile Changes off |

| Ending | Meaning |
|---|---|
| `-rawhover` | every hover helper off (Steady Hover, Reach Stage, Long-range Pickup), for players who hover as a skill |
| `-custom` | expert values given on the command line |
| `-measurement` | measurement firmware |
| `-LAB` | the Filter Arranger's lab build (testing only) |

</details>

## Calibration

| Level | What happens | Unlocks |
|---|---|---|
| **Fast Pass** | Turns on Pen frequency tuning (experimental). The build keeps the preset's own pick-up levels. | - |
| **Standard** (without ALPI) | A measurement firmware reads three pen positions: a light touch, a normal hover and the highest hover. Sets the fast stage's pick-up and drop levels and Jump Rejection's margin. | Multi-coil Read |
| **Full** (with ALPI) | Trains ALPI after the patches are chosen: a training firmware (the cursor keeps working), normal use until every 20 x 20 mm square has enough readings at every pen height, and a 4-second hold in the middle. The same readings measure Standard's levels and the pen's frequency in the background. | ALPI |

> [!TIP]
> Skipping the training (`s`), or a training that fails, uses built-in defaults: a reference ULTRA training,
> reference pick-up levels (RAPID and ULTRA) or the preset's, and the stock pen frequency. They are not measured on
> your tablet, so they may not match it. The reference training needs 12-bit Readings; without them the build is made
> without ALPI.

```text
AnargonsTabletTools --hid-check                                    # read the tablet's reports for 5 s
AnargonsTabletTools --pen-resonance                                # pen frequency, from installed measurement firmware
AnargonsTabletTools main.bin output/ --rate fast --calibrate      # measure, then build with the measured thresholds
AnargonsTabletTools main.bin output/ --rate fast --calibration output/calibration-fast.txt
AnargonsTabletTools main.bin output/ --rate ultra --apip apip-ultra.txt
```

- `--hid-check` and `--pen-resonance` only read. Windows needs no WinUSB or replacement driver.
- A saved calibration cannot be combined with custom scan timing or Pen frequency tuning. Multi-coil Read can.
- `--apip FILE` builds with a saved ALPI training; it cannot be combined with `--startup-calibration`.
- The [calibration guide](DOCS/calibration.md) has the full procedure.

> [!IMPORTANT]
> A measurement firmware can be on the tablet even when its update reported a failure. Finish with a normal flash.
> If the program exits with a restoration warning, flash a tested normal build or return to stock.

## Backups and flashing

- **A backup at start.** The wizard reads the whole tablet through xppen-tools in the background while you go
  through the steps, and waits for it only where it is needed. A failed backup is never used, and the last good one
  is kept.
- **A backup before every flash.** Every flash needs a complete backup from the same run, compared with the build's
  source. One backup per run is enough.
- **xppen-tools asks first.** A real `--flash` runs xppen-tools in this console, as if you typed the command; it asks
  for its typed confirmation before writing. `--flash-dry-run` prints the commands and runs nothing on the tablet.
- **Flash count.** `--flash-count` shows the count and where it was read: the Bootloader, the Firmware or the local
  PC. It is shown against the chip's rated minimum, `N/100,000` (GD32F350 datasheet).

> [!IMPORTANT]
> Keep your backups: they are tablet-specific. A backup from an earlier run is for recovery, not for a new flash.

> [!NOTE]
> Close other tablet drivers during calibration. On macOS, check Input Monitoring if access is denied.

## Recovery

```text
AnargonsTabletTools --recovery main.bin recovery-output --name Tablet
```

- Takes a complete 64 KB backup (`main.bin`) of the damaged tablet.
- Recognises an erased firmware end and erased startup data, and checks the code against the registered build.
- Restores an erased end from your own complete backup or build of the same firmware, kept next to it (checked by
  checksum). Damaged code needs a verified stock copy of your own. Unknown or modified firmware is never repaired by
  guessing.
- Writes a patcher, a stock application and a report, and tests the repair in the digital twin. Follow its
  commands, and keep the original file and the report.

> [!WARNING]
> Bootloader maintenance is opt-in. Losing power while it rewrites a reset page can need hardware recovery, so keep
> the tablet plugged in for the wait it asks for.

Hardware recovery can be found under
[`Anargon-osu/deco640-resources/recovery`](https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery).
How the firmware behaves: [firmware behaviour](DOCS/FIRMWARE_DESIGN.md).

## Command line

This tool also supports building by directly passing commands (mainly for compatibility reasons)

```text
AnargonsTabletTools --help
AnargonsTabletTools main.bin output/ --rate fast
AnargonsTabletTools main.bin output/ --all
AnargonsTabletTools main.bin output/ --rate ultra --flash
AnargonsTabletTools --compat main.bin
```

| Option | What it does |
|---|---|
| `--rate PRESET` | stock, quick, fast, rapid or ultra (or 220, 300, 500, 800, 1000) |
| `--all` | builds every preset |
| `--flash` / `--flash-dry-run` | backs up and flashes one tested build / prints the commands only |
| `--calibrate`, `--calibration FILE`, `--apip FILE` | calibration (see [Calibration](#calibration)) |
| `--no-misc-optimizations` | Misc. Optimizations off |
| `--no-switch-match`, `--no-window-scale`, `--no-coil-gains`, `--no-coil-linearization`, `--no-anti-chatter` | one part of Misc. Optimizations off |
| `--no-frequency-hold`, `--no-coil-hold`, `--no-timer-off`, `--no-tilt-skip`, `--no-accurate-pressure` | one coil-reading setting off |
| `--no-boost`, `--no-reach`, `--no-longrange`, `--no-odd` | Steady Hover, Reach Stage, Long-range Pickup or Jump Rejection off |
| `--interp on\|off`, `--smoothing on\|off`, `--aggressive-interp` | position filters |
| `--multi-coil` | Multi-coil Read (experimental) |
| `--no-volatile`, `--bootloader-changes`, `--bootloader-counter`, `--discreet` | what the build may change on the tablet |
| `--no-bootloader-counter` | removes the Bootloader Counter in that flash (input: a full backup of that tablet) |
| `--return-to-stock` | makes the files that put the tablet fully back to stock |
| `--recovery`, `--hid-check`, `--pen-resonance`, `--flash-count`, `--setup` | the tools above |
| `--compat IMAGE`, `--check IMAGE` | compatibility report / digital-twin test of any image |
| `--bin` | also writes a `.bin` |

`AnargonsTabletTools --help` lists every option, including the expert tuning values.

## Configuration

Copy `.env.example` to `.env`, next to the program or in the source folder. Environment variables win over `.env`.
Firmware settings are chosen in the menu or on the command line; `.env` holds paths and links.

| Variable | Purpose |
|---|---|
| `ADT_XPPEN_TOOLS` | where xppen-tools is ([`NiceAesth/xppen-tools-builds`](https://github.com/NiceAesth/xppen-tools-builds)) |
| `ADT_STATE_DIR` | the per-user folder for the flash count and one-time notes |
| `ADT_TEARDOWN_URL` | link to the hardware recovery instructions (default: `Anargon-osu/deco640-resources/recovery`) |
| `ADT_SUPPORT_URL`, `ADT_SUPPORT_WHERE` | where to ask for help |
| `GD32EMU_DIR` | the GD32EMU source, when it is not beside this folder |

The test settings are described in `.env.example`.

## Documentation

| For | Document |
|---|---|
| Players | [Calibration guide](DOCS/calibration.md) &middot; [Changelog](DOCS/CHANGELOG.md) &middot; [Patcher packages](DOCS/patcher-packages.md) |
| Modders | [Tutorial: modding the firmware yourself](DOCS/tutorial-self-modding.md) &middot; [Tutorial: turning a mod into a patcher package](DOCS/tutorial-patcherpkg.md) &middot; [Porting to another tablet](DOCS/porting-guide.md) |
| Contributors | [AGENTS.md](AGENTS.md) (read first) &middot; [Contributing and tests](DOCS/CONTRIBUTING.md) &middot; [Builder and scheduler design](DOCS/DESIGN.md) |
| Firmware | [Firmware behaviour](DOCS/FIRMWARE_DESIGN.md) &middot; [Registered firmware layouts](DOCS/FIRMWARE_VERSIONS.md) &middot; [Emulator integration](DOCS/EMULATOR.md) |
| Verification | [Release verification](DOCS/verification.md) |

## Repositories

| Repository | What it holds |
|---|---|
| [`Anargon-osu/anargons-tablet-tools`](https://github.com/Anargon-osu/anargons-tablet-tools) | this tool |
| [`Anargon-osu/GD32EMU`](https://github.com/Anargon-osu/GD32EMU) | the chip emulator the digital twin runs on |
| [`Anargon-osu/deco640-resources`](https://github.com/Anargon-osu/deco640-resources) | hardware recovery, under `recovery/` |
| (External) [`Niceaesth/xppen-tools`](https://github.com/NiceAesth/xppen-tools-builds) | tool that dumps and flashes firmwares to xppen-tablets
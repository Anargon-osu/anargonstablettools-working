# How the Deco 640 works: the hardware and its firmware

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

This document explains how the XPPen Deco 640 drawing tablet works inside, from the chip that runs it to the moment
a pen report reaches your PC, and where Anargon's Tablet Tools changes that behaviour. It is written for a reader
who can program in an ordinary language such as C, C# or Python and knows what a pointer, a bit mask and a
hexadecimal number are, but who has never read assembly language and never programmed a microcontroller. Every
hardware or assembly term is explained where it first appears.

**How to read the numbers.**

- Addresses are hexadecimal (`0x08003800`) and belong to the **February stock build** (`2025-02-14_release001`). The
  December build (`2025-12-23_release001`) has the same code moved by a few bytes; `FIRMWARE_VERSIONS.md` lists the
  shifts, and section 14 explains how the tool finds every location in either build.
- Each finding says how it was established: *code analysis* (reading the firmware's machine code), *twin* (running
  the unmodified firmware in the project's emulator, the "digital twin"), *tablet* (seen on Anargon's real tablet),
  or *model* (a calculation). A twin or model figure is not a hardware measurement.
- No XP-Pen firmware bytes appear in this document (AGENTS.md rule 15). Where machine code matters it is described
  in C-like pseudocode.

---

## 0. Five ideas you need first

### 0.1 A microcontroller is a whole computer on one chip

A desktop computer has a processor, separate memory chips, a disk and an operating system that loads programs. A
**microcontroller** puts a small processor, its memory and a set of hardware helpers on one chip, and runs a single
program forever, with no operating system at all. That program is called **firmware**, because it is stored in the
chip's permanent memory and is rarely changed. Everything the tablet does (finding the pen, measuring pressure,
talking USB) is this one program.

### 0.2 Everything has an address

The processor sees one large numbered space of bytes, the **memory map**. Different ranges of numbers lead to
different things:

- reading address `0x08000000` returns the first byte of the permanent program memory (flash);
- reading or writing `0x20000000` and up uses the working memory (RAM);
- writing to an address in the `0x40000000` range does not store anything: it **talks to a piece of hardware**, for
  example a timer or the converter that measures voltages. This is called *memory-mapped I/O*.

In C you would do this with a pointer to a fixed address. For example, the Arm core has a timer called SysTick
whose control register sits at `0xE000E010`; the line

```c
*(volatile uint32_t *)0xE000E010 = 0;   // write 0 to SysTick's control register: stop SysTick
```

stops it. (`volatile` tells the C compiler that the access has a side effect and must really happen.) This exact
write is one of the fixes in R45 (section 3.3).

### 0.3 Registers and instructions: what "assembly" means

The processor does not run C. It runs **instructions**, each a tiny step encoded in 2 or 4 bytes. It keeps the
values it is working on in 16 **registers**, named `r0` to `r15`: think of them as 16 local variables that are
always there and are much faster than memory. Three have special jobs: `r13` is the **stack pointer** (`sp`, where
the current function keeps its saved values in RAM), `r14` the **link register** (`lr`, the address to go back to
when the current function returns) and `r15` the **program counter** (`pc`, the address of the instruction being
run). *Assembly language* is the human-readable spelling of these instructions.

The Deco 640's processor is an Arm **Cortex-M4**, which uses the **Thumb-2** instruction set: a compact mix of
16-bit and 32-bit instructions. You only need a handful of instruction kinds to follow this project:

| Instruction (example) | What it does, in C-like terms |
|---|---|
| `movs r1, #45` | `r1 = 45;` (a small constant stored inside the instruction itself) |
| `ldr r0, [r1, #4]` | `r0 = *(uint32_t *)(r1 + 4);` (load a 32-bit word from memory) |
| `str r0, [r1]` | `*(uint32_t *)r1 = r0;` (store a word) |
| `adds r0, r0, #1` | `r0 = r0 + 1;` |
| `cmp r0, r1` then `bhi label` | `if (r0 > r1) goto label;` (unsigned compare, then a conditional branch) |
| `bl function` | `function();` (call: `lr` = the next instruction, then jump) |
| `bx lr` | `return;` |
| `b.w label` | `goto label;` (a long jump; the tool uses these to reach its own code) |

Two consequences shape the whole project:

- **Settings are often constants inside instructions.** The stock firmware takes 45 measurements per reading
  because one instruction says, in effect, `samples = 45`. Changing the setting means rewriting the constant inside
  that instruction (section 14). Nothing else in the program needs to know.
- **Adding behaviour means a detour.** To add new code, the tool replaces one instruction with a jump (`b.w`) to new
  code placed in empty flash; the new code does its work, repeats what the replaced instruction did, and jumps back.
  This is called a **hook** or a trampoline.

### 0.4 Interrupts: hardware that can pause the program

An **interrupt** is a hardware event (a timer ran out, a USB packet arrived) that makes the processor stop what it
is doing, run a short function called the **interrupt handler**, and then continue where it was, as if nothing
happened. It is similar to a signal handler in a desktop program. The processor finds the handler through the
**vector table**: an array of function pointers at a fixed place, one entry per kind of interrupt. In C terms:

```c
typedef void (*handler_t)(void);
const handler_t vector_table[] = {
    (handler_t)0x20000E70,   // entry 0: not a function but the starting stack pointer
    reset_handler,           // entry 1: where the program starts after power-on
    nmi_handler, hard_fault_handler, /* ... */ systick_handler, /* ... one per hardware interrupt ... */
};
```

Each interrupt also has a **priority**; a higher-priority one can interrupt a lower-priority handler. The time an
interrupt takes is stolen from whatever was running. That matters here because the pen measurement is
timing-sensitive (section 9).

### 0.5 The hardware helpers ("peripherals") this firmware uses

| Peripheral | What it is | What the Deco 640 uses it for |
|---|---|---|
| **GPIO** (general-purpose input/output) | pins the program can switch on or off, or read | choosing which coil is connected; the LED (pin PC13) |
| **Timers** | counters that run on the chip's clock and can raise an interrupt when they reach a value | pacing each pen reading (one timer ticks every 1 us); generating the drive pulses |
| **ADC** (analog-to-digital converter) | turns a voltage into a number, 0 to 4095 (12 bits) | measuring the pen's signal |
| **DMA** (direct memory access) | copies data between hardware and RAM without the processor | referenced by the firmware's measurement code |
| **USB** (full-speed device) | the connection to the PC, in 1 ms time slots called frames | sending pen reports, receiving commands |
| **SysTick** | a simple countdown timer built into every Cortex-M core | not used by the firmware; left running by the bootloader (section 3.3) |

---

## 1. The chip and its memory

Inside the Deco 640 is one chip, a **GigaDevice GD32F350C8T6** microcontroller. The name decodes as: GD32F350
family; C = 48 pins; 8 = 64 KB of flash; T = LQFP package; 6 = rated for -40 to +85 C. It is a close relative of
ST's STM32 chips but it is **not** one: it reads GigaDevice's flash-size register, uses GigaDevice's USB core at
`0x50000000`, and holds no ST strings, ST USB IDs or ST-only registers anywhere. The difference matters because flash
and USB hardware differ in detail between the two makers. *(code analysis)*

| Part | Size | Address range | What it is for |
|---|---|---|---|
| Processor | Arm Cortex-M4F, 72 MHz | - | runs the firmware; one clock cycle is 13.9 ns |
| **Flash** | 64 KB | `0x08000000`-`0x0800FFFF` | permanent storage for the two programs (section 2); kept with the power off |
| **RAM** (SRAM) | 8 KB | `0x20000000`-`0x20001FFF` | working memory; its contents are lost at every power-off |
| Peripherals | - | `0x40000000` and up; USB at `0x50000000`; pins at `0x48000000` | the hardware helpers of section 0.5 |
| Core registers | - | `0xE000E000`-`0xE000EFFF` | the Arm core's own controls: SysTick, the interrupt controller, the vector table register |

**Flash has rules, and they shape everything this tool does.**

- Flash is divided into **pages of 1 KB** (1024 bytes); there are 64.
- A byte cannot simply be overwritten. A whole page must first be **erased**, which sets every bit to 1 (every byte
  then reads `0xFF`). After that, words (4 bytes) can be **programmed**, and programming can only turn 1 bits into 0
  bits. To change one byte you must erase and rewrite its whole page.
- **Rewriting a page is not atomic.** If power is lost between the erase and the end of programming, the page is left
  half-written. For most pages that only breaks the program until it is flashed again; for the two pages the chip
  needs to start (section 3) it can leave the tablet unable to start at all. That is why this tool never writes them
  in a normal build.
- **Flash wears out slowly.** The GD32F350 datasheet rates it for at least 100,000 erase cycles per page. The tool
  shows the number of updates as `N/100,000` (AGENTS.md rule 13). Each firmware update erases each firmware page once.

---

## 2. Flash layout: two programs and some data

The 64 KB of flash hold two separate programs, the **bootloader** and the **firmware**, plus a few pages of data:

```
0x08000000 +---------------------------+  page 0: bootloader's vector table + start-up code   <- most critical page
           |  BOOTLOADER code          |
0x08000C00 |   (boot decision, update  |  page with the boot code and set_boot_state          <- second most critical
           |    mode over USB)         |
0x08002800 +---------------------------+  free page (used only by the opt-in Bootloader Counter)
0x08002C00 +---------------------------+  free page (this tool's flash counter)
0x08003000 +---------------------------+  bootloader data; boot-state word at 0x08003400
0x08003800 +===========================+  <- an update erases everything from here down
           |  FIRMWARE ("deco01v3")    |  its own vector table, code, constant tables,
           |                           |  compressed start-up data (.data, section 5)
0x0800C7CC +---------------------------+  end of the February firmware (December: 0x0800C7D8)
           |  free ("the cave")        |  where this tool places its added code
0x0800FFFF +---------------------------+
```

- **The bootloader** (`0x08000000`-`0x080037FF`, 14 KB) runs first after every power-on or reset. It decides
  whether to start the firmware or to stay in **update mode**, also called **DFU** (Device Firmware Update). In
  update mode the tablet shows up on USB as `28BD:FFF1` (vendor ID 28BD = XP-Pen, product ID FFF1) and accepts new
  firmware. The bootloader is never erased by an update, which is why a tablet with broken firmware can usually be
  flashed again.
- **The firmware** (from `0x08003800`) is the program you normally use: it reads the pen and shows up on USB as
  `28BD:2904`. XP-Pen calls it "deco01v3".
- **The two free pages** at `0x08002800` and `0x08002C00` are not used by the stock bootloader and are not covered by
  any of its checksums *(twin, rev 28)*, so they are the only place where data survives every firmware update. This
  tool keeps its flash counter there (section 15).
- **The cave** is the unused flash after the firmware's end. Every feature this tool adds lives there, reached
  through hooks (section 0.3).

---

## 3. What happens from power-on to the first pen report

### 3.1 The bootloader starts

1. **Reset.** The processor reads the first two words of flash, which are the first two entries of the bootloader's
   vector table: the starting stack pointer, and the **reset vector**, the address of the first instruction
   (`0x08000165`; Thumb addresses are odd by convention, so the code itself starts at `0x08000164`).
2. **Start-up code.** The reset handler prepares the bootloader's RAM and jumps through a short trampoline
   (`0x08000150`). The trampoline reads one word at `0x0800015C`, which holds the address of the real boot code
   (`0x08000C38`), and jumps there. In C terms: `((void (*)(void)) *(uint32_t *)0x0800015C)();`.
3. **Boot decision.** The boot code reads the **boot-state word** at `0x08003400`:
   - `0` means "run the firmware";
   - `1` means "an update was requested";
   - `2` means "an update is in progress".

   With state 1 or 2 it also checks *why* the chip was reset, using a flag in the clock unit (`RCU_RSTSCR` bit 28,
   SWRSTF, "the last reset was a software reset"). After a **software reset**, which is what the firmware does when
   asked to enter update mode, it stays in update mode and waits for the PC. After a **power-on** with state 1 it
   clears the request and starts the firmware instead, so unplugging the tablet gets you out of an abandoned
   request. *(code analysis; the emulator models SWRSTF since rev 35)*

   The state word is changed in exactly one place, a small function the project calls `set_boot_state`
   (`0x08000F40`), which rewrites only that word.
4. **Hand-over.** To start the firmware, the bootloader writes the firmware's vector table address (`0x08003800`)
   into the core's **vector table offset register** (VTOR, `0xE000ED08`), so that from now on interrupts use the
   firmware's handlers, then loads the firmware's stack pointer and jumps to its reset handler.

If either of the first two steps finds damaged code, the chip cannot start at all, and only the chip's built-in ROM
loader (section 16) can bring it back. That is why page 0 and the boot-code page are the two most dangerous pages
to write, and why a normal build never touches them.

### 3.2 The firmware starts

The firmware's reset handler first fills its RAM: zeros for some variables and stored starting values for others
(section 5). It then sets up the clocks, timers, ADC and USB, and **enumerates** on USB: it answers the PC's
questions about what kind of device it is (section 6). Then it enters its main loop of **frames** (section 8), one
measurement and one report after another, for as long as it has power.

### 3.3 What the bootloader leaves running: SysTick (R45)

A program that starts another program should leave the hardware as it found it. The Deco 640's bootloader does
not: it uses SysTick as a 1 ms clock and leaves it running, at priority 0 (the highest), when it starts the
firmware. The firmware never uses SysTick, and its SysTick handler is a function that only returns. So once per
millisecond the scan was interrupted for a few hundred nanoseconds by a handler that did nothing.

That sounds harmless, but the pen measurement is a timed process (section 7): an interruption at the wrong moment
made the coil being read come out about 15 units high, which moved the reported position by about 0.4 mm when the
pen sat between two coils. Because the fastest preset's frames follow the PC's USB clock and SysTick follows the
tablet's own crystal, the tick slowly drifted through the frame: on Anargon's tablet the crystal is 13.45 parts per
million off, so the tick passed the sensitive moment once every 74 seconds, producing a short burst of shake about
once a minute. *(tablet: a firmware build stamped each frame with the tick's position, and an on/off comparison
cut the worst step from 0.195 to 0.075 mm)*

The fix is the one line of C in section 0.2: the tool's frame code writes 0 to SysTick's control register whenever
it restarts its pen search (at start-up and after every pen loss). The setting is called **Bootloader Timer Off**.
The digital twin starts directly at the firmware and never runs the bootloader, so it never had SysTick running
and could not show the problem; when the tablet and the twin disagree, look first for something the bootloader
leaves behind.

---

## 4. Firmware updates, and how they go wrong

### 4.1 What an update does

The PC-side program **xppen-tools** performs updates; this tool only prepares the image and runs xppen-tools.

1. The firmware is asked (over USB) to enter update mode. It sets the boot state to 1 and resets itself.
2. The bootloader sees a software reset with state 1, sets the state to 2 (update in progress) and **erases every
   page from `0x08003800` to `0x0800FFFF`**.
3. It receives the new image and programs it. The PC then compares a checksum the bootloader computes over what it
   wrote (a CRC, CRC-16/XMODEM) with the image's own. The bootloader sets the state back to 0 and resets into the new
   firmware.

A power cut in the middle leaves state 2, so the next start goes straight back to update mode; the firmware can
then be flashed again. *(code analysis)*

### 4.2 Why the image must be complete

The erase in step 2 covers the whole firmware area, so whatever the image does not contain is left as `0xFF`.
**An image that is missing its end leaves the firmware's start-up data erased**, and the firmware cannot start
properly. This has happened twice in this project's history:

- **Revisions 3-5:** an older xppen-tools only programmed up to `0x0800C188`, a size hard-coded for this chip, and
  left the firmware's compressed start-up data (section 5) erased. The tablet stopped working. Proven by checksum: the
  device's reported CRC was reproduced exactly from "the code plus an erased end". It was recovered with a small
  self-writing image that restored the missing bytes. *(code analysis, emulation, tablet)*
- **Revision 35 and R42:** two more tablets were found with everything from `0x0800C188` erased. R42 reproduced both
  dumps byte for byte as "the tablet's own stock firmware written only up to `0x0800C188`": the restore image that
  older xppen-tools releases saved next to every dump (`*-stock.hex`) is cut at exactly that size. Flashing it causes
  this damage. *(simulation against the dumps)*

So this tool builds only complete images, refuses dumps that end too early, renames cut restore images to
`*.INCOMPLETE-DO-NOT-FLASH`, and its recovery tool restores an erased end from the user's own complete files.

---

## 5. `.data`, `.bss`, and why the start-up data is the delicate part

A program has **instructions**, which never change and stay in flash, and **variables**, which change while it runs
and must live in RAM. RAM holds random values after power-on, so the start-up code must give every variable its
first value. Compilers sort variables into two groups:

- **`.bss`**: variables whose first value is zero. Nothing needs to be stored; start-up writes zeros over that part
  of RAM.
- **`.data`**: variables with a specific first value, for example "send a USB report every 3 ms" or the USB
  descriptors (the tablet's self-description, section 6). These first values are **stored in flash and copied into
  RAM** at every power-on. On this firmware the stored copy is **compressed** to save space and unpacked by the
  start-up code.

The start-up code learns what to copy from a small table the linker (the tool that assembles a program from its
parts) placed in flash, the **scatter-load table**. In C terms:

```c
struct region { const uint8_t *load; uint8_t *ram; uint32_t size; void (*copy)(const uint8_t *, uint8_t *, uint32_t); };
for (each struct region r in the table)
    r.copy(r.load, r.ram, r.size);   // unpack .data into RAM, or fill .bss with zeros
```

**Why it matters here.** The limit that caps the stock tablet at 500 reports per second is the **USB report
interval** (`bInterval`, 3 ms for the pen's endpoints, section 6). It is not in the code but inside the compressed
`.data`, at `0x0800C63B` and `0x0800C658`. To reach 800 or 1000 reports per second the tool changes those two bytes
to 1 ms. *(code analysis; twin: the firmware enumerates with 1 ms afterwards)*

Changing bytes inside compressed data is only safe if they are exactly the bytes expected. On a firmware whose
layout differs even slightly, the same change would corrupt the compressed stream, the unpacker would produce wrong
start-up values at every power-on, and the tablet would misbehave or not start. That is the reason for the tool's
**compatibility check**: before any change it confirms that the exact stock bytes are where it is about to write,
and it stops when they are not. The setting **Volatile Changes** allows `.data` changes at all; with it off, the
build leaves `.data` alone (and keeps the 500/s limit; RAPID and ULTRA need it on). Because `.data` is part of the
firmware, flashing stock firmware undoes it either way.

**Where the firmware ends.** The last entry of the scatter-load table (the one that fills `.bss` with zeros) points
at the first byte after the firmware, so the tool reads the firmware's end from it: `0x0800C7CC` in February,
`0x0800C7D8` in December. A dump that ends before that, or has no such table, is **truncated** and cannot produce a
working build; the tool refuses it. *(code analysis)*

---

## 6. USB: how the tablet talks to the PC

### 6.1 Enumeration and descriptors

When a USB device is plugged in, the PC asks it a series of questions and the device answers with **descriptors**:
small records that describe it. The **device descriptor** gives the vendor and product IDs (`28BD:2904`), the
**configuration descriptor** lists its **interfaces** (separate functions of one device) and their **endpoints**
(one-way data channels, numbered; `0x81` means "endpoint 1, device to PC"). **String descriptors** are numbered text
answers, such as the product name. This question-and-answer phase is **enumeration**.

The Deco 640's firmware presents three interfaces, all of the **HID** class (human interface device, the standard
class for mice, keyboards and pens, which needs no special driver). As Windows lists them *(tablet, R41)*:

| Interface | What Windows sees | Reports |
|---|---|---|
| 0 | two mice and a keyboard | the tablet's shortcut functions |
| 1 | **HID-compliant pen** | pen reports with report ID 7, 10 bytes |
| 2 | vendor-defined device | 12-byte reports in XP-Pen's own format (report mode 4) |

A pen report holds, in order: the report ID, a status byte (pen in range, tip touching, side buttons), X and Y
positions (16 bits each, in 0.01 mm), pressure (16 bits) and two tilt values. The active area is 160 x 90 mm, so X
runs from 0 to 16000 and Y from 0 to 9000. *(code analysis, tablet)*

**Report modes.** The firmware can send its reports in two formats: mode 2 (the standard ID 7 pen report on
interface 1) and mode 4 (XP-Pen's 12-byte format on interface 2, which XP-Pen's driver selects and which counts
position in 1/200 mm units instead of 1/100 mm). The calibration firmware carries its measurements inside the
mode 2 (ID 7) reports; ALPI's training also works while XP-Pen's driver holds the tablet in mode 4, and halves its
positions to match. *(code analysis)*

**Why the PC read nothing on Windows until R41.** Windows' own pen support opens interface 1 for itself and refuses
to share it, so a program that opens it the usual way gets "sharing violation" and only ever saw the silent
interface 2. Since R41 the tool reads interface 1 through Windows' **Raw Input** service, which delivers a copy of
every report without taking the device away. *(tablet, read-only probe)*

### 6.2 Polling and the 1000-reports-per-second ceiling

A USB device cannot send whenever it likes. The PC **polls** each interrupt endpoint at the interval in its endpoint
descriptor, **bInterval**, in 1 ms USB frames at full speed. The stock pen endpoints say 3 ms; hosts round that down
to 2 ms, which is the 500 reports per second limit of stock. With bInterval 1 the PC asks every millisecond, so
**1000 reports per second is the ceiling of any full-speed USB device**, however fast the tablet measures. *(code
analysis, twin; the tablet ran FAST, RAPID and ULTRA)*

Every USB frame starts with a **start-of-frame** (SOF) signal carrying an 11-bit frame number, so it counts 0 to 2047
and wraps. The tool's scheduler locks its frames to SOF at 500 and 1000 per second (section 9); an early version
assumed a 14-bit counter and stalled for about 5 ms every 2 seconds when the number wrapped. *(twin, rev 17)*

### 6.3 String descriptors this project uses, and one to avoid

The firmware keeps its table of string descriptors in RAM (`0x20000284`, 36 entries) and answers a request by
walking a long chain of `if (index == ...)` comparisons; an unknown index ends in a USB "stall" (a refusal).

| Index | Answer | Note |
|---|---|---|
| 5 | the version string | every build of this tool sets it to `AnargonSS727_<build date>` |
| 173 | `ADTC1:<B\|F\|N>:<6 digits>` | this tool's flash counter readout (section 15); never change it (AGENTS.md rule 11) |
| 176-179 | `ADTZ1:<target><recording>` | calibration firmware only: the PC sets the LED's target zone (R44) |
| **200** | - | **reading it puts the tablet into update mode.** It is XP-Pen's own entry to DFU. Never request it from a test or a probe. |

*(code analysis, twin)*

---

## 7. Sensing the pen

### 7.1 The coils and the pen

Under the drawing surface is a grid of flat wire loops, called **coils**: 32 running one way (they locate X) and 19
the other (Y), on an even 5.08 mm (0.2 inch) grid. *(tablet, R43: coil centres measured from ruler strokes)* The
chip cannot connect to 51 coils at once. The coils sit on **analog switches** (multiplexers: a switch with 8 inputs
and one output, controlled by a 3-bit address): X uses four 8-way switches, Y three. One consequence: two coils can
be active together only if they are exactly 8 apart, because they then share an address on different switches.
*(code analysis)*

The pen has **no battery**. It contains a coil and a capacitor that together form a **resonant circuit**, which
behaves like a tuning fork for radio waves: give it energy at about the right frequency and it keeps oscillating by
itself for a short while afterwards, at its own frequency (about 500 kHz, set by the coil and capacitor:
f = 1 / (2 pi sqrt(L C))). Pressing the tip changes the pen's coil slightly, which shifts that frequency; the side
buttons switch extra parts in and shift it further. So **the pen's frequency carries the pressure and the buttons**,
and **the strength of its signal on each coil carries its position**.

### 7.2 One reading

A **reading** (a "step" in the code) has two phases:

1. **Drive.** The tablet sends a burst of **pulses** at about the pen's frequency through a coil. The pen absorbs
   energy and starts to ring.
2. **Listen.** The tablet stops driving and measures the **ring-down**, the pen's fading oscillation, through the
   coils, taking a number of **ADC samples**.

| | Stock | FAST (500/s) | ULTRA (1000/s) |
|---|---|---|---|
| pulses per reading | 40 | 20 | 16 |
| samples per reading | 45 | 10 | 4 |

The receiving circuit **integrates**: it adds up the ring-down over the listening time. So a shorter reading gives a
proportionally smaller number for the same pen. This is why the fast presets need pick-up and drop thresholds scaled
to their reading length: the stock thresholds (pick up above 300, drop below 200, for 45-sample readings) would
never be reached by a 4-sample reading, and early high-rate builds only found the pen when it touched the surface.
*(twin, rev 25; tablet)*

**The drive frequency comes in steps.** The pulses come from a timer counting the 72 MHz clock, so the period is a
whole number of clock cycles: 72 MHz / 144 = 500.0 kHz, 72 MHz / 143 = 503.5 kHz. Neighbouring **drive steps** near
the pen's frequency are about 3.5 kHz apart. The firmware keeps the step it uses for X and Y in a variable the
project calls `C[3]` (section 8.3).

### 7.3 From samples to a "line sum"

For each coil, the firmware adds the samples of one reading into a **line sum** (the coil is a "line" of the grid).
Several details, all from R43's measurements, are needed to interpret these numbers *(tablet)*:

- **Every line sum has a floor** of about 72 per conversion added, even with no pen: 216 at ULTRA's 4 samples (the sum
  holds one conversion fewer than the samples taken), 288 at RAPID's 5, about 632 at FAST's 10. It was read from
  lines past the tablet's edges, where no coil can see the pen.
- **A coil's response falls with distance.** Normalised to 1 at the coil's centre: 0.92 at half a coil pitch, 0.70 at
  one pitch, 0.40 at 1.5, 0.115 at 2, about 0 from 2.5. The **effective range**, where the response falls to half,
  is 1.34 pitches (6.8 mm), the same on both axes and within 5 % at FAST, RAPID and ULTRA. The project describes a
  coil only by its centre and this measured effective range, never by an assumed shape (AGENTS.md rule 16): the
  coils may be multi-layered and not circular, and a bell curve fitted them badly.
- **Stock rounds every reading to a multiple of 8.** The sampling loop clears the low 3 bits of each 12-bit ADC
  value (`value & ~7` in C). The **12-bit Readings** setting keeps all 12 bits and raises every threshold by 4 to
  match (the cleared bits are worth 3.5 on average). On the tablet at ULTRA, low hover, cursor shake fell to 0.38x
  (Y) and 0.86x (X). *(tablet, R43)*
- **AGC** (automatic gain control): when the signal is very strong the firmware skips the first few samples of a
  reading so a close pen does not overload the converter. Stock allows a skip of up to 30. With a 4-sample reading
  that could skip every sample, or read past the end of the sample buffer and crash, so the tool caps the skip at
  "samples minus 2". *(twin, rev 13)*

---

## 8. One frame, from start to report

A **frame** is one complete measurement that ends in one report to the PC. The report rate is frames per second.
Stock runs about 220 frames per second (one every 4.4-4.5 ms), not paced to anything: each frame starts when the
last one ends.

### 8.1 Finding the pen (search)

When no pen is tracked, the firmware runs a **search**: it drives and listens across the whole tablet at each
frequency of a list (461-533 kHz) until some coil's line sum passes a fixed level of 100. Two consequences:

- a resonance under 457 kHz means "no pen" *(code analysis)*;
- a reading of 3 samples can never sum past 100, so 4 samples per reading is the floor for every preset *(code
  analysis, twin)*.

### 8.2 Position: the five-line window

Once tracked, each frame reads a **window** of coils around the pen on each axis. Position comes from the five line
sums around the strongest one (call them `s[-2]` to `s[+2]`, centre `s[0]`). The stock routine (`0x08005432`)
averages two estimates *(code analysis; a C copy matches the firmware on every frame in the twin)*:

```c
// a: a parabola through the centre line and its two neighbours
a = centre + (s[+1] - s[-1]) / (2 * (2 * s[0] - s[-1] - s[+1]));
// b: a four-line ratio that adds s[-2] or s[+2] on the side the pen is on
b = centre + four_line_ratio(s, side);
position = (fabs(a - b) > 1 coil) ? a : (a + b) / 2;   // if they disagree by more than a coil, trust the parabola
```

(The real code works in integers, with the result scaled to 0.01 mm.)

**The coil-window flip.** The scan drives the pen through the window's centre coil, and that coil's own line reads
2.5-2.8 % low. When the pen sits halfway between two coils, the two candidates for "strongest line" are nearly
equal, and that small bias makes the window re-centre back and forth from one frame to the next; each switch moves
the reported position slightly. **Coil Hold** (R45) keeps the current window unless the new line beats the centre
by a margin of 3/32 of the centre line's height above the weakest of the five lines. *(tablet recordings; twin: flips
at a midpoint 50 % -> 0)*

**The switch step and Switch Match (R45-rc4).** Each reading drives the pen with a short burst through the
window's centre coil and then listens. The pen keeps ringing for a while after a burst, so every line except the
first one read in a window is measured while the pen still rings from the line before; the first is "cold". On
Anargon's tablet (fast stage, pen still) the first line read came out 44 % low on X and 16 % low on Y, the others
within 8 %. How big that is depends on the read length, the drive frequency step and the pen's frequency
(pressure). The result is a small position bias that is smooth while the same window, read length and drive step are
used, and that changes all at once when any of them changes: on 45-degree drags the position jumped 0.24 mm on X at
every coil-window switch, and 0.67 mm when the hover stage changed. Reading one extra line first to warm the pen up
would cost 76 us a frame, more than ULTRA has left. **Switch Match** (`atf_switchmatch.cs`) works on the position
instead: at a switch it fits a straight line through the last 16 positions (only past ones), takes the difference
between that line and the new reading off (refined over 4 frames), and lets that correction fade as the pen moves
away (gone after half a coil) or as time passes (gone after 500 ms). It does nothing when the pen moves faster than
150 mm/s or the line bends, where a straight line is a poor guess. Outside the half coil after a switch it changes
nothing. It removes the step but not the bias: right after a switch the position keeps the old window's bias and
moves to the new one's gradually. *(hardware recordings, offline with the firmware's integer arithmetic: 0.24 ->
0.036 mm on X; twin gate)* Matching every switch failed: with the pen held still at a coil midpoint the window
flips often, and every match turned a few frames of noise into an offset lasting up to half a second, which ALPI
passes, so the cursor drifted (on the tablet 0.30 mm sd at a midpoint). *(hardware, cursor only; simulation)* So two
gates keep a still pen out of it: a coil-window switch is matched only while the pen moves at 15 mm/s or more (the slope of the fitted line),
and only when that axis has not switched in the 16 frames before (a pen resting between two coils makes the window
flip every few frames, and those flips would also fake a slope in the fitted line). Each axis keeps a count of the
frames since its last switch for this. Drive-step and ladder-step switches need only the second gate, so the jump
when the hover step changes is still matched at a still pen. In a simulation of a still pen with a flipping window
the output is then the same as without Switch Match. *(simulation)*

**Window Scale (R45-rc4 (2)).** Switch Match hides the step at a switch; Window Scale removes most of it where it is
made. The firmware works out the position inside the five-coil window from the five line readings, as the centre
coil's number times 1018 plus an offset. Because the first line is read cold and the others are read while the pen
still rings, that offset comes out stretched or squeezed by a few percent: the position inside the window is as far
from the centre coil as it should be, times (1 - k). When the window moves by one coil, the pen's distance from the
new centre is about one coil less than from the old one, so the position jumps by k coils. That is why the jump has
the same size wherever the switch happens and whichever way the pen goes. On Anargon's 45-degree drags k followed the
pen's pressure (the pen sets its own frequency from the pressure, and the frequency decides how fast it rings up):
on X about +0.043 up to pressure 10000 and about -0.047 above it; on Y between -0.03 and +0.09. The setting replaces
the two calls of the stock position routine (one for X, one for Y) with a small routine that calls the stock routine
itself and then multiplies the offset from the centre coil by (1 + k), with k read from a table of 9 values at
pressure 0, 2048, ..., 16384 and taken on a straight line between the two nearest (so the correction never jumps
when the pressure changes). With the pen in the air the pressure is 0 and the lightest touch's value is used. It
acts only in the fast stage (the slower hover steps read longer and were not measured) and only at ULTRA. It keeps
nothing from one frame to the next, so it cannot drift. Offline on the drags it cut the X jump from 0.24 to 0.08 mm
(0.15-0.17 mm when the table is fitted on half of the drags and tested on the other half), and to 0.04 mm with
Switch Match after it. It does not touch the smooth wave across each coil (0.16-0.35 mm). The digital twin has no
cold reading, so there it adds a step instead; its check only proves that the firmware computes exactly what the C#
copy (`WindowScale.Apply`) computes. *(hardware recordings offline; twin)*

**Coil Gains (PR1).** The stock arithmetic that works out the position inside the five-coil window uses only
differences and ratios of the five line readings, so it assumes every coil picks up the pen equally strongly. On
Anargon's tablet they do not. Fitted on the raw line readings of the R43 stroke recordings (pen touching, each frame's
pen offset and signal fitted together with one strength per coil, one per window line and one shared response
shape), the X coils differ by 1.2 % (standard deviation) and the Y coils by 0.7 %, the same in every recording and
along the whole length of each coil; on X a strong / weak pair comes back every 8 coils (10 / 11, 18 / 19, 26 / 27).
A coil that reads 1 % strong pulls the position in the windows next to it by about 0.03 mm, so a slanted line bends
coil by coil. This is the 0.04-0.07 mm slanted-line error that Coil Linearization (one table for every coil) cannot
remove. The setting runs inside Window Scale's routine, before the stock routine is called: each of the five line
readings has the electronic floor taken out (the same floor as Multi-coil Read: a constant per read sample) and is
multiplied by its coil's correction from a table of 40 entries per axis (`4096 / gain`, Q12), and the result is
shifted so it keeps 4 more bits. The stock arithmetic is the same; only the inputs are evened out. It acts only in
the fast stage and only at ULTRA (where the gains were measured), keeps nothing between frames and costs about 30
instructions per axis. On the tablet the firmware removed exactly the predicted error (with minus without on one
ruler line: r 0.83-0.87 against the prediction), but the size of the coil error in the cursor changed from session
to session (from almost none to 2.4 times the measured one), so the table is applied at 58.9 % of the measured
difference: the strength, in 0.1 % steps, that lowers the worst of the four recorded sessions most (each session
keeps 0.78-0.91 of its error, none is made worse). On the tablet at 60 % the error that repeats on every pass of a
slanted line fell from 0.021 to 0.009 mm. *(hardware recordings offline; hardware, cursor; twin)*

**Dynamic Anti-Chatter (R45-rc5).** Even while the pen moves smoothly, a single frame now and then lands a little
off its path: one noisy reading, seen as "chatter". Dynamic Anti-Chatter looks at each new position together with
the two before it. If the pen moved in a straight line at a steady speed, the new position would continue the line
through the two before; how far it is from where that line points (the "second difference", p0 - 2 p1 + p2) says how
far the frame is off. Up to 0.10 mm the frame passes unchanged. Above that it is pulled part of the way back toward
the previous position: barely just over 0.10 mm, by the full amount from 0.20 mm on. The full amount is chosen per
report rate so that it costs at most 0.4 ms of delay on the frame it moves; averaged over all frames the delay is
about 0.1 ms. Only past positions are used, and the history keeps the raw positions rather than the smoothed ones, so
nothing builds up and a still pen is never moved. It removes one-frame jumps; it does not fix an error that repeats
at the same place. *(hardware recordings offline; twin)*

**Coil Linearization (R45-rc5).** Between two coil centres the stock arithmetic does not move the reported
position evenly: in some parts of each coil it moves too slowly, in others too fast, so a straight line drawn at a
steady speed comes out with a small wave that repeats every coil (5.08 mm). Coil Linearization measures that wave
from a ruler recording. A pen sliding at a steady speed spends the same time over every part of a coil, so its
reports should be spread evenly across the coil; where they bunch up the arithmetic is squeezing the position, where
they thin out it is stretching it. Counting the reports gives, for each reported place inside a coil, where the pen
really was. The firmware keeps this as a table of 64 corrections per coil for each axis (the same for every coil),
works out where the reported position sits inside its coil from the measured coil grid, and adds the correction,
taken on a straight line between the two nearest table entries. It keeps nothing between frames, so it adds no delay
and cannot drift. It runs before ALPI, the cursor filter: ALPI follows a straight move at once but the small coil
wave about 5-6 ms late, so a correction made after the filter landed a little behind a moving pen (forward and back
strokes differed; that order failed). On Anargon's tablet, along a ruler on three lines (two of them lines it was not learned on), the
part of the error that repeats every coil fell from 0.20-0.26 to 0.08-0.15 mm on X and from 0.08-0.14 to 0.05-0.07 mm
on Y. The table in use (table B) was learned again on those three lines; on a later ruler line it was not learned on
it left 0.088 / 0.059 mm (sd, forward / back) against 0.107 / 0.073 for the first table. The table belongs to one
tablet and pen (measured at ULTRA), so on another tablet it may help less. *(hardware, cursor)*

**Misc. Optimizations (R45).** Switch Match, Window Scale, Coil Gains (added in PR1), Coil Linearization and
Dynamic Anti-Chatter are one setting, on by default; in the firmware the order is Switch Match > Coil Linearization >
ALPI > Dynamic Anti-Chatter, with Window Scale and Coil Gains inside the stock position routine. Anargon's tablet chose it: a 20-minute osu! session with the
Filter Arranger switching the order at pen lifts, then a ruler session (`experiments/precision/RESULTS.md`). Jump
Rejection (3.0x the flicks) and Hull Mark + Hull Clamp (4.9x) failed there and are left out. *(hardware)*

### 8.3 Pressure and buttons: the frequency sweep

To find the pen's frequency, the firmware reads at several frequencies (a **sweep**, 6 steps in stock) and finds
where the pen answers strongest. The frequency then says which state the pen is in *(code analysis)*:

| Pen frequency | Meaning |
|---|---|
| under 457 kHz | no pen |
| 457-480 kHz, 480-500 kHz | a side button is held (one range per button) |
| 500-540 kHz | the tip band: a piecewise-linear table with 76 breakpoints turns the frequency into pressure |
| above that | invalid: no report |

Pressure starts a little above the pen's resting frequency (Anargon's pen rests at about 507 kHz; pressure begins
about 2 kHz higher, *tablet*, R41) and reaches its maximum near 540 kHz.

**The drive-step hop.** After every sweep (twice in every 16 frames) the stock firmware moves the X/Y drive step
`C[3]` to the step where the sweep read the pen strongest (in C terms, `C[3] += peak - 2` at `0x08007840`). At ULTRA
the sweep's readings are short and polluted, so this choice hopped at random between two neighbouring steps; each
hop changed the pen's apparent strength by about 30 % and moved the position by about 0.5 mm, on both axes. It was
the main cause of ULTRA's still-pen shake. **Frequency Hold** (R45) moves the drive step by one only after 8 sweeps
in a row agree, and still takes a bigger jump at once, because a bigger jump is a real change (a press). On the
tablet, with Coil Hold: X spread 0.116 mm and Y 0.083 mm against stock's 0.183 and 0.119. *(tablet)*

### 8.4 Tilt

On some frames the firmware also reads the outer coils of the window, where the pen's signal spreads further on the
side it leans towards; the spread gives the tilt. The tool measures tilt on 4 frames of every 16 and caches the outer
lines between them. *(code analysis, twin)*

### 8.5 Processing, filters and the report

After the readings, the firmware computes position, pressure and tilt, runs its filters (section 12), and copies the
result into the report buffer at `0x200005F0`, which the USB code sends at the next poll. The tool replaced the
firmware's software division routine (`0x0800398C`) with the chip's hardware divide instruction, which gives the
same results faster.

---

## 9. Timing: why the fast presets are hard

### 9.1 The cost of a reading

From the firmware's code, one reading takes about

```
2 + 2 x (pulses + 2) + 15 + 2.15 x samples   microseconds
```

which gives stock (40 pulses, 45 samples) 197 us, just under its 200 us step timer. The step timer is a hardware
timer ticking every 1 us; each reading must finish before its tick, or it **overruns** into the next one.
*(code analysis, twin)*

**Why this matters, from revision 8:** two diagnostic builds made the readings longer without making the step
timer longer, the readings overran, and the tablet stopped responding until it was flashed again. Since then every
build passes a timing check, and every image runs in the digital twin before it can be flashed.

### 9.2 The frame budget

A frame at 1000 per second has 1000 us. The ULTRA build on Anargon's tablet spends them like this *(the builder's
budget, from that build's log; the builder charges processing at a fixed 130 us, while the twin measured about
82 us)*:

| Part | Time |
|---|---|
| scan (search or window readings, one sweep step, tilt lines) | 844 us |
| processing (position, pressure, tilt, USB) | 130 us |
| ALPI filter | 1 us charged (the routine takes about 662 clock cycles, 9.2 us, for both axes; the twin checks that the real frames still fit) |
| spare | 25 us |

To make this fit, the tool's **scheduler** spreads the sweep over several frames (one step per frame at ULTRA),
reads tilt only on some frames, and starts every frame on a fixed beat: locked to USB's start-of-frame at 500 and
1000 per second, or to a timer at 300 and 800. Reports then leave evenly spaced. *(twin)*

### 9.3 Rate limits, measured

Without pressure, buttons or tilt, the scan could run faster than USB can carry: 1140 frames per second with 5 + 5
window lines and 1545 with 3 + 3, built and run in the twin, against the 2000 the timing model predicted. USB carries
at most 1000 reports per second (section 6.2). *(twin, R45-rc3)*

---

## 10. Hover: keeping the pen when it is above the surface

The higher the pen, the weaker its ring-down, and short readings make that worse. The tool adds a small state
machine with three **hover stages** *(code analysis, twin; tablet for the behaviour noted)*:

| Stage | Frame rate | Readings | Entered when |
|---|---|---|---|
| fast | the preset's rate | short | the pen is close or touching |
| **Steady Hover** | R45-rc4: a ladder of rates (ULTRA 930/s down to 426/s) | from 5 x 16 up to 44 x 40 | the signal is under the noise target (below) |
| **Reach** | 60 per second | 24 samples, 254 pulses | the signal is still too weak in Steady Hover |

**The ladder (R45-rc4).** Steady Hover is not one rate but a ladder of small steps. The fast stage keeps the full
rate as long as the pen is strong enough to read with the noise of ULTRA's fast stage near the edge of its range;
below that the firmware goes one step slower, where each frame takes 25 us longer and reads the pen with a few more
samples. Each step is kept until its own noise reaches the same target, so the noise stays about the same while the
rate falls only as far as the pen's height needs. Going back up, it uses the ratio between the two steps' readings
that it measured on the way down, so a still pen does not flip between two steps. Up to rc3 a **probe frame** tried
the faster stage every 16 frames; on the tablet those frames were 0.36 mm off and ran 29 times a second with a still
pen, so they are gone. Touching the surface returns to the fast stage at once. *(twin; tablet for the probe
frames)* **Long-range Pickup** searches with stronger pulses, so a pen high up
is found again (each long search takes about 0.1 s).

**Comparing like with like (R45-rc3).** A slower stage drives the pen longer, so the same pen reads stronger there:
the pen's ringing builds up as about `1 - exp(-n x pi / Q)` of its full value after `n` pulses, with Q (the pen's
quality factor) about 300. Up to rc2 the firmware compared every stage's signal against the fast stage's level, so
in Steady Hover the pen looked strong enough to step up too early; it stepped up, read weak, stepped down, about 37
times a second at low hover, and each switch moved the cursor by 0.12-0.2 mm. Now each stage steps up at its own
level, the fast stage's level scaled by the ratio of the two ring-ups, plus 25 % (`Build.StepUpLevel`). *(tablet
recordings; the fix twin-tested: no flapping, hover held 16.8 -> 22.0 mm)*

**Holding a tracked pen (R45-rc3).** The reach stage now keeps a pen it already tracks down to 0.6 of its drop level
(`Build.REACH_HOLD`); picking up a new pen still needs the full level, so noise is not mistaken for a pen. Hover hold
at ULTRA 22.0 -> 25.8-26.4 mm. *(twin)*

**Tilt frames while hovering (R45-rc3).** In the fast stage at hover, 3 of the 4 tilt frames in every 16 moved Y by
+0.21, +0.44 and -0.20 mm, a 60 Hz shake. **Tilt Frame Skip** plans those frames as normal frames while the pen
hovers in the fast stage; tilt keeps its last value. *(tablet recordings; fix twin-tested)*

---

## 11. Pressure, and two crashes it used to cause

- **The pressure table could be read past its end.** The lookup finds the table entry for the pen's frequency; a
  frequency above the last entry produced an index past the end of the 76-entry table. The tool clamps the index to
  74, so the last segment is extended rather than read out of bounds. This does not lower the maximum pressure: in
  the twin pressure still reaches 16383, and matches stock within about 5 units across the range. *(code analysis,
  twin)*
- **Full pressure could stop all reports.** A frequency a little above 540 kHz counted as invalid, so pressing hard
  made the pen disappear. The tool holds 540-545 kHz at 539.999 kHz. *(code analysis)*
- **Accurate Pressure Readings (R41, on by default).** With short sweep readings the pen is still ringing from one
  sweep step when the next one starts, so neighbouring frequencies leak into each other. In the twin, RAPID's
  5-sample sweep steps read a hovering pen up to 9 kHz too high, which can look like a press. The setting uses stock's
  reading length for the sweep (45 samples, 40 pulses), as many steps per frame as fit (QUICK all 6, FAST 3, RAPID 1;
  ULTRA has no room). Cost: fewer pressure updates (RAPID 100 per second). On the tablet: no pressure in the air.
  *(twin, tablet)*

---

## 12. Filters: turning noisy positions into a steady cursor

A **filter** here is code that takes the raw position of each frame and outputs a calmer one. Every filter that
averages over time adds delay, because the output lags behind the pen; the project's rule is to remove noise at its
cause first (sections 3.3, 8.2, 8.3) and filter only what is left (AGENTS.md, "noise first").

| Filter | Who made it | What it does |
|---|---|---|
| **Position Interpolation** | XP-Pen (stock) | averages the last 3-5 positions; part of stock's cursor delay |
| **Movement Smoothing** | XP-Pen (stock "dead band") | ignores movements smaller than a small threshold, so a resting pen does not move the cursor (December made it stronger: coefficients 70/60/50 -> 80/80/60) |
| **Jump Rejection** | this tool | predicts where the pen should be from its recent speed; a single reading far outside the corridor between "stopped" and "kept going" is replaced by the prediction; after 3 rejections in a row the new place is accepted |
| **Aggressive Position Interpolation** | this tool | a longer average, 16 / 8 / 4 positions depending on speed (counted in reports, so its delay depends on the rate) |
| **Multi-coil Read** (experimental) | this tool | replaces the five-line arithmetic of section 8.2 with a fit of all five lines to the coils' measured response (section 7.3), floor removed; costs less time than the stock routine |
| **ALPI** (ALgorithmic pen Position Interpolation) | this tool, Anargon's design | a tracker trained on your own tablet; replaces Position Interpolation, Movement Smoothing and Jump Rejection |

**How ALPI works, in outline** (`atf_apip.cs`). It has three parts, kept separately for each axis and for each
**band** of signal strength (touching, low hover, high hover: the weaker the signal, the noisier the readings):

1. **Coil-distance correction.** The stock arithmetic's position bends slightly between two coils, the same way at
   every coil. A pen moving over the tablet spends equal time at every distance from the coil centres, so the share
   of readings at each distance shows the bend, and a 16-segment table undoes it. This adds no delay.
2. **An alpha-beta tracker.** ALPI keeps an estimate of the pen's position and speed, predicts the next reading, and
   moves towards each actual reading by a fraction **alpha** (and its speed by **beta**). In C terms, per reading:

   ```c
   predicted = pos + speed;
   error     = reading - predicted;
   pos       = predicted + alpha * error;
   speed     = speed + beta * error;
   ```

   For a pen moving at a steady speed the prediction is exact, so unlike a moving average it adds no steady delay.
   A reading far off the prediction (three in a row on the same side, more than 2.5 times the current noise) opens a
   **gate** towards quick gains, so a sudden turn is followed at once; a single spike is not.
3. **Still-pen gains.** While the pen rests or moves steadily, the gains slide within 20 ms to much smaller values,
   so a resting pen shakes far less; any change of movement brings the quick gains back at once.

A **training firmware** records every frame's position, signal level, pressure and hover stage while you use the
tablet normally, until every 20 x 20 mm area has enough readings in every band, then a 4-second hold in the centre.
The PC fits the gains and the table, and the build carries them. Every ALPI build is checked in the twin: the
firmware's filter must equal its C# copy on every frame. On Anargon's tablet, still pen at the centre: cursor shake
0.08x (X) and 0.06x (Y) of the unfiltered position. *(tablet)*

---

## 13. The LED

The LED is on pin PC13, **active-low**: writing 0 to the pin lights it, 1 turns it off. Stock lights it while the pen
is in range, and a routine at `0x0800868C` toggles it every 500 ms while the pen hovers. This tool keeps that
behaviour in normal builds. The calibration firmware uses the LED to show whether the pen is inside the target box
of the current step (section 6.3). *(code analysis, twin)*

**Switching the LED during a reading adds noise** at the fast presets: changing it at the start of each reading,
about 53-61 us before the samples, made ULTRA's readings 1.20x (X) and 1.38x (Y) noisier and FAST's 2.0x / 2.2x, but
not QUICK's. Switching it only between frames is clean. So the tool's LED effects change the LED once per frame at
the fast presets. *(tablet)*

---

## 14. Where this tool changes the firmware

Every change first checks that the exact stock code is at that spot (`Image.Expect`), then either rewrites a
constant in place or replaces one instruction with a jump to added code in the cave (section 0.3).

### 14.1 Finding each location in any firmware build

The addresses below are the February build's. When XP-Pen releases a new build, most code is unchanged but
**moved**: if one function grows by 12 bytes, everything after it sits 12 bytes later, which is what happened in
December. So the tool does not store addresses. For every location it stores a **fingerprint** of the machine code
around it, like a pattern with wildcards: the bytes that change when code moves (the encoded distance of a call, the
offset to a constant, a stored address) are masked out, and only a short check value and a hash of the remaining
bytes are kept, never the bytes themselves (`atf_symbols.cs`, written by `tools/mksig.py`). At build time the tool
searches the loaded firmware for each fingerprint:

- found **exactly once**: the location is known;
- found **zero or several times**: the location is unknown, and every change that needs it is left out or the build
  stops. Nothing is ever guessed.

On December all locations resolve, and the shifts they show (0, -2, -4 and +12 bytes) reproduce the hand-made offset
map exactly. The twin's own hooks (coil switching, timing, the LED) are found the same way. *(code analysis, twin)*

### 14.2 The changes

| Change | Where (February) | How |
|---|---|---|
| hardware divide | `0x0800398C` | software division routine replaced in place |
| reading length (samples, pulses, step timing) | `0x08005302`, `0x0800710E`, `0x08005DA2`, `0x08005DAC` | constants rewritten |
| AGC cap | `0x080075D6`, `0x080075DA` | constants rewritten |
| 12-bit Readings | the sampling loop at `0x08004720` | the "clear the low 3 bits" instruction removed |
| 1 ms USB report interval | `0x0800C63B`, `0x0800C658` (inside compressed `.data`) | 3 -> 1 (Volatile Changes) |
| pressure-table index clamp | the pressure lookup | jump to a bounds check |
| frequency band-edge hold | `0x08007656` | jump to added code |
| Movement Smoothing / Position Interpolation off | the dead-band setup at `0x080041C0`, `0x08005BDA` | constants set to "off" |
| scheduler: frame pacing, hover stages, search, thresholds, Frequency Hold, Coil Hold, Tilt Frame Skip, Bootloader Timer Off, Jump Rejection, ALPI, Multi-coil Read | about 30 sites in the scan and frame code | jumps to the added scheduler |
| version string | USB string descriptor 5 | `AnargonSS727_<build date>` |
| LED in the calibration firmware | the four places the firmware switches PC13 | measurement hooks; normal builds keep the stock LED |
| flash counter (Bootloader Changes) | free page `0x08002C00` | a record per new build |
| Bootloader Counter (opt-in, experimental) | bootloader pages `0x08000000`, `0x08000C00`, `0x08002800` | see `DESIGN.md` and `CLAUDE.md` section 8 |

The added code keeps its variables in RAM the stock firmware never uses: the stock stack starts at `0x20000E70` and
everything above it is free. The tool's map (from `0x20000E80`, plus a few blocks higher up such as the counter
string at `0x20001F80`) is in `DESIGN.md`.

---

## 15. The flash counter

Page `0x08002C00` is empty in stock firmware. With the counter on, the firmware stores there *(code analysis, twin;
the Bootloader Counter confirmed on Anargon's tablet, rev 33)*:

- the 4 bytes `AnSC` (a marker),
- a base count, and its bit-inverted copy as a check,
- then one 8-byte record per new build that booted: an ID and its bit-inverted copy.

The count is the base plus the valid records. A record cut off by a power loss fails the inverted-copy check and is
not counted. A firmware update erases from `0x08003800` up, so this page survives every update, including stock
ones.

The opt-in **Bootloader Counter** adds a record from inside the bootloader each time it leaves state 2 (an update
finished), so it also counts stock updates and interrupted attempts. The PC reads the count from USB string 173
(section 6.3) and labels it `[According to Bootloader]`, `[According to Firmware]` or `[According to local PC]`; the
PC's own copy counts only the updates that PC performed. The page, the record format and string 173 must never
change (AGENTS.md rule 11): tablets in the field carry them.

---

## 16. When an update fails: recovery

Recovery depends on which part was damaged and which ways into the chip still work. The step-by-step instructions
for opening the tablet and using the board's pads are in the [`recovery` folder](https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery)
of the resources repository, Anargon-osu/deco640-resources. Every recovery message of the program
links to that folder, unless `ADT_TEARDOWN_URL` in `.env` points elsewhere.

| Damage | Way back | What it needs |
|---|---|---|
| firmware incomplete, bootloader fine | flash a complete image through update mode | a working USB connection; the right stock build |
| firmware cannot be asked to enter update mode | hold the update-mode pad (PA15) to ground while plugging in, then flash | opening the tablet |
| bootloader or reset code damaged | the chip's own ROM loader: BOOT0 pulled to 3.3 V (about 1 kOhm) and the serial pins PA9 / PA10, or an SWD debug probe | hardware tools and a full 64 KB backup |

*(board photo, bootloader code, GD32 datasheet; the update-mode pad has been used on this tablet, the ROM loader route
not yet)*

| Failure | Guard | What remains |
|---|---|---|
| power lost during an ordinary update | the boot state stays 2, so the tablet restarts in update mode; a fresh backup is taken before every flash | update mode must still be reachable over USB |
| image without its end, or changed patch sites | truncation detection, fingerprint lookup, expected-byte checks | a source the tool cannot verify is refused |
| timing or memory error in added code | timing check and the twin gate (start-up, USB, gain control, pen detection) | a model cannot rule out every hardware behaviour |
| image made from a different firmware build than the tablet runs | fresh backup and build comparison before every flash | overriding the warning is untested on hardware |
| first install of the Bootloader Counter | opt-in only, "do not unplug" page | rewriting the reset page has a power-loss window of about 85 ms |

The full list of everything that could stop the tablet, with the evidence for each, is `CLAUDE.md` section 8.

---

## 17. Further reading

- [The builder and the scheduler](DESIGN.md): how the tool assembles its added code, the RAM map, the twin.
- [Firmware builds](FIRMWARE_VERSIONS.md): how February and December differ, and the offset map.
- [Porting guide](porting-guide.md): how to study another tablet's firmware with the same methods.
- [Self-modding tutorial](tutorial-self-modding.md) and [packaging a mod](tutorial-patcherpkg.md).
- [The emulator](EMULATOR.md) and [verification](verification.md).
- `CLAUDE.md`: every finding with its history, and section 8, the potential brick vectors.

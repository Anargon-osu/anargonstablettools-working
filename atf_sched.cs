// atf_sched.cs - the added frame scheduler, hover stages, search, calibration and jump rejection (firmware code).
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class Stage { public int periodUs, period, samples, burst, tick, swSamples, swBurst, swTick, pick, drop, swPick, swDrop; }
    public class CalCfg { public int span, frames, skip, hold, enterP, nearP, blink, windowMs; }
    public class OddCfg { public int margin, maxRun; }
    public class Cfg
    {
        public Stage[] modes = new Stage[3];
        public int ledTarget = -1;           // set by Patch: the LED gate (or -1: plain LED-on)
        public int nModes = 0;
        public bool boost, reach, longSearch, aggressive, telemetry, counter, blCounter, cleanup;
        public bool keepCount;               // R45: cleanup removes only the Bootloader Counter (a normal build): page 0x08002C00 kept
        public bool multiCoil;               // R43: Multi-coil Read (atf_multicoil.cs) replaces both position calls
        public bool fullRes;                 // R43: 12-bit Readings (Multi-coil Read's floor depends on it)
        public bool holdHover;               // 800/1000 Hz: acquire into Hover Boost and hold a weak hover (see Patch)
        public bool freqHold;                // R45: Frequency Hold: the X / Y scans keep their drive step (HOLD_ADDR)
        public bool coilHold;                // R45: Coil Hold: a one-coil re-centre needs a margin (xdone / ydone)
        public bool systickOff = true;       // R45-rc3 setting "Bootloader Timer Off" (rc2: always): SYST_CSR cleared in "search"
        public bool tiltSkip;                // R45-rc3 setting "Tilt Frame Skip": no tilt frames while hovering in the fast stage
        public bool switchMatch;             // R45-rc4 setting "Switch Match" (atf_switchmatch.cs): no position step at a coil-window, drive-step or ladder-step switch
        public bool antiChatter;             // R45-rc5 setting "Dynamic Anti-Chatter" (atf_antichatter.cs): a 2-frame average, at most +0.4 ms, only on frames whose noise is over 0.10 mm
        public bool coilLin;                 // R45-rc5 setting "Coil Linearization" (atf_coillin.cs, default on since rc5 (1)): a fixed correction of the final position by its phase inside the coil, no history
        public bool winScale;                // R45-rc4 (2) setting "Window Scale" (atf_winscale.cs): the position inside a coil window corrected for pressure (not with Multi-coil Read)
        public bool coilGains;               // 2026-11-22 setting "Coil Gains" (atf_coilgain.cs): each coil's reading strength evened out inside Window Scale's wrapper
        public bool lab;                     // Filter Arranger build (atf_lab.cs): every position filter built in, their order from RAM
        public int labDefault, labDefaultFlags;   // its order and flags when the PC set none (Lab.DefaultOrder: the normal build's)
        public ApipModel apip = null;        // R44: Algorithmic Position Interpolation (atf_apip.cs): the trained model
        public bool apipTrain;               // R44: APIP training build: records every frame for USB string 180, LED zone
        public byte[] apipK = null;          // R45: APIP's robust filter (atf_apipr.cs): its constants (null = APIP's tracker)
        public int buildId = 1;
        public byte[] stockPage = null;      // stock content of bootloader page 0x08000C00 (from a full dump)
        public byte[] stock0Page = null;     // stock content of reset-vector page 0x08000000 (from a full dump)
        public int[] quickSearch = new int[] { 10, 20 };       // samples, pulses: FAST's search on every preset
        public int enter, freeRunPolls, enterUp1, enterUp2;    // R45-rc3: enterUp = Build.StepUpLevel per slower stage
        public bool lineRec;                 // R42: line recorder (measurement builds): every line of a frame, see LR_ADDR
        public CalCfg cal = null;
        public OddCfg odd = null;
        public int[] phases;
        // R45-rc4 dynamic Steady Hover (Build.BuildLadder): the read-length ladder, the fast stage first and the reach
        // stage last; per step its stage (0 fast, 1 Steady Hover, 2 reach), step-down level, frames below it before
        // stepping down, step-up levels to every faster step (U[p, j]) and frames above one before stepping up
        public Stage[] lad = null;
        public int nLad = 0;
        public int[] ladStage, ladT, ladDown, ladUp;
        public int ladAcq = 1;                   // R45-rc4: the ladder step a newly found pen starts at (Build.BuildLadder)
        public int[,] ladU;
        public bool[] ladTimer;                  // R45-rc4: per step, paced by TIMER16 (not a whole number of USB frames)
    }

    public static class Sched
    {
        public const int S_ADDR = 0x20000698, C_ADDR = 0x200006B8, D_ADDR = 0x20000664, T_ADDR = 0x200006A4;
        public const int XBUF = 0x20000754, YBUF = 0x200007CC, PRESSURE = 0x200005F0, MS_COUNTER = 0x20000408, DSTAT = 0x50000808;
        public const int SYST_CSR = unchecked((int)0xE000E010);      // SysTick control (R45-rc2: switched off, see "search")
        public const int REPORT = 0x200005F0, REPORT_MODE = 0x20000325, AGC = 0x200003E8, RESONANCE = 0x20000410;
        public const int V_ADDR = 0x20000E80, VB_ADDR = V_ADDR + 64, VC_ADDR = V_ADDR + 128, VBX_ADDR = V_ADDR + 160, VS_ADDR = V_ADDR + 176, VP_ADDR = V_ADDR + 192;
        public const int VF_ADDR = V_ADDR + 224;     // aggressive interpolation: +0 count, +1 next slot, +2 last raw x, +4 y, +8 16 x (x, y)
        // R41: 1 = the hover stage changed while a sweep spread over frames was half done (set by setmode, cleared at
        // each sweep start). 0x20000F52, after VP's 18 bytes; stock and every block of ours leave 0x20000F52-5F unused.
        public const int VQ_ADDR = VP_ADDR + 18;
        // R44: the calibration-pass build's LED zone, set by the PC through USB strings 176-179 (Patches.CounterString):
        // 176 + 2 x target + recording. 0x20000F54 (free, see VQ above):
        //   +0 state: bit 0 = a step is recording, bits 1-3 = target (0 none, 1 the drawing area's centre)
        //   +1 LED: 0 off, 1 on, 2 calibrating (R45: a fade, see LED_FADE); worked out from each pen report (tsend, or the
        //      training recorder), 0 on search restart
        //   +2 state ^ ZONE_CHECK: the state counts only when this matches (RAM survives an update and is not
        //      cleared at power-up); until the PC sets it, the LED is on while the pen is tracked
        // On with a target: the report's position within ZONE_HALF of it on both axes (7 mm each way, 14 x 14 mm).
        public const int ZONE_ADDR = VQ_ADDR + 2, ZONE_CHECK = 0xA5, ZONE_STRING = 176, ZONE_TARGETS = 2;
        public const int ZONE_X = 8000, ZONE_Y = 4500, ZONE_HALF = 700;    // centre of the 160 x 90 mm area, 100 units per mm
        // R45 Frequency Hold: the drive step the X / Y scans use (0x20000F58, free, see VQ above):
        //   +0 D, the held step (index into the scan-frequency table, like C[3]); 255 = none (set at search restart)
        //   +1 how many full sweeps in a row picked the step one away from D
        // Stock re-picks C[3] after every sweep with no margin, and ULTRA's short sweep readings carry over from step
        // to step, so the pick hops between two steps at random. Each hop changes the pen strength about 30 % and
        // moves the position about 0.5 mm (the lines do not scale evenly). D moves to a step one away only after
        // HOLD_AGREE sweeps in a row pick it, and at once to a step 2 or more away. The scans use D while it is within
        // 2 steps of C[3], else C[3] (stock).
        // R45-rc2: HOLD_AGREE 3 -> 8 (Anargon's sideways shake on the final ULTRA + ALPI build). With the pen resting
        // between two steps (the sweep picked the other one in about 38 % of frames), 3 in a row still came about 6.6
        // times a second, and each hop moved X about 0.2-0.3 mm, which ALPI follows as a real move. Tablet A/B (silent
        // study build, 4 s blocks, 3 / 5 / 8 / 12 in a row): 6.6 / 1.1 / 0 / 0 hops a second, X spread 0.098 / 0.085 /
        // 0.055 / 0.056 mm (median block). A real one-step change now waits up to 8 sweeps (about 65 ms at ULTRA).
        public const int HOLD_ADDR = ZONE_ADDR + 4, HOLD_AGREE = 8;
        // R45 LED fade (LED zone builds: calibration pass and ALPI training), 0x20000F5A (free, see VQ above):
        //   +0 error diffusion: the part of the level not yet shown
        //   +1 level: 0 = not fading (off, on, or no zone), else the share of time lit / 256
        // While the LED zone says "calibrating" (ZONE_ADDR + 1 = 2), each frame restart sets the level from the
        // millisecond counter and LedTable (Anargon: down to 10 %, holding full and low longer than the moves; 500 ms
        // with 150 ms holds and then 440 ms with 100 ms holds were both still too slow, the holds make it feel slower;
        // 360 ms with 80 ms holds was the right speed but "a touch too aggressive"): 360 ms per pulse (2.8 Hz) - 60 ms
        // full, 120 ms down, 60 ms at 10 %, 120 ms up, the moves eased and even to the eye (brightness ^ 2.2). The millisecond counter wraps at 65530 (65530 mod 360 = 10), so the jump at the
        // wrap lands inside the full hold and cannot be seen. PC13 has no timer output, so this is software PWM by
        // error diffusion: the level is added to +0 and the LED is lit when the sum passes 255 (the remainder is kept).
        // Where the LED may change (tablet, still pen, 2026-11-11): switching it during a scan step made ULTRA's readings
        // noisier (X 1.20x, Y 1.38x, the five-line signal 1.5x; the switch came 53 us before the step's readings), and
        // calibration and ALPI training measure exactly that noise; at FAST (61 us) it was worse (X 2.0x, Y 2.2x). At
        // QUICK (81 us before the readings) it did no harm (0.89x / 0.92x), and changing it only between frames did
        // none at ULTRA either (0.96x / 1.03x); the LED's own on / off state does not matter (1.01x). So, per hover
        // stage: where every step (scan and sweep, with the burst this build really uses) switches it at least
        // LED_SCAN_US before its readings, the step timer drives the PWM on every tick (every scan step and every
        // 10 us between frames: about 2 kHz at 10 %) - QUICK, and Steady Hover and Reach Stage at every preset (40 and
        // more pulses). In the other stages (the fast stage of FAST, RAPID and ULTRA) the frame restart does, once per
        // frame (at 10 %: 100 Hz at ULTRA, 80 Hz at RAPID, 50 Hz at FAST; faster when brighter). Per frame in Steady
        // Hover (2-4 ms frames) or Reach Stage (16.7 ms) would flicker or blink at 10 %.
        public const int LED_FADE = HOLD_ADDR + 2, LED_FADE_MS = 360, LED_SLOT_MS = 10, LED_HOLD = 6, LED_RAMP = 12, LED_SCAN_US = 81;
        public const double LED_LOW_DUTY = 0.10;
        // the level for each 10 ms of the pulse (Anargon's shape, see LED_FADE)
        public static byte[] LedTable()
        {
            int n = LED_FADE_MS / LED_SLOT_MS, hold = LED_HOLD, ramp = LED_RAMP;   // 2 x hold + 2 x ramp = n
            double lo = Math.Pow(LED_LOW_DUTY, 1 / 2.2);
            byte[] t = new byte[n];
            for (int i = 0; i < n; i++)
            {
                double b;                                      // brightness to the eye, 0..1
                if (i < hold) b = 1;
                else if (i < hold + ramp) b = 1 - Ease((i - hold + 0.5) / ramp) * (1 - lo);
                else if (i < 2 * hold + ramp) b = lo;
                else b = lo + Ease((i - 2 * hold - ramp + 0.5) / ramp) * (1 - lo);
                int v = (int)Math.Floor(Math.Pow(b, 2.2) * 256 + 0.5);
                t[i] = (byte)(v > 255 ? 255 : v < 1 ? 1 : v);
            }
            return t;
        }
        static double Ease(double x) { return x * x * (3 - 2 * x); }
        // true when a step of this many pulses switches the LED early enough for the step timer to drive the pulse
        // (see LED_FADE): the step's switch-to-readings time
        public static bool LedInScan(int burst) { return 2 + 2 * (burst + 2) + 15 >= LED_SCAN_US; }
        // the hover stages (bit i = cfg.modes[i]) whose every step, scan and sweep, may switch the LED (LedInScan)
        public static int LedStages(Level lv, Cfg cfg)
        {
            if (cfg.nModes == 0) return LedInScan(lv.burst) ? 1 : 0;    // vendor timing (no stages): its own steps
            int m = 0;
            for (int i = 0; i < cfg.nModes; i++)
            {
                Stage st = cfg.modes[i];
                if (LedInScan(st.burst < st.swBurst ? st.burst : st.swBurst)) m |= 1 << i;
            }
            return m;
        }
        // R45-rc4: per Steady Hover ladder step, 1 when every scan and sweep step of it switches the LED early enough
        // (LedInScan), padded to a whole word
        public static byte[] LedStepTable(Cfg cfg)
        {
            byte[] t = new byte[(cfg.nLad + 3) & ~3];
            for (int i = 0; i < cfg.nLad; i++)
            {
                Stage st = cfg.lad[i];
                t[i] = (byte)(LedInScan(st.burst < st.swBurst ? st.burst : st.swBurst) ? 1 : 0);
            }
            return t;
        }
        // R45 Coil Hold: a re-centre by one coil is undone unless the new peak line beats the centre line by more
        // than COIL_MARGIN / 32 of the centre line's height above the weakest of the five lines. The scans drive the
        // pen through the centre coils, so the centre line reads 2.5-2.8 % low (tablet, both axes) and between two
        // coils the window flipped every frame.
        public const int COIL_MARGIN = 3;
        // R42 line recorder (measurement builds, multi-coil study): one frame's five X and five Y line sums, the
        // firmware's own X / Y positions and their context, copied at frame end and sent in place of the signal page
        // on the next five even frames (pages 0xF0-0xF4; odd frames keep the position, frequency and counter pages, so
        // the twin gate and the live readout still work). +0 pages left to send (0 = a new copy may be taken), +1 copy number, then five
        // 8-byte pages from +8, each sent as report bytes [2..8]:
        //   F0: X lines c-2, c-1, c (sum, 16 bits each), copy number     F1: X c+1, X c+2, Y c-2, X centre coil
        //   F2: Y c-1, c, c+1, Y centre coil                              F3: Y c+2, X position, Y position, peaks X | Y << 4
        //   F4: pressure and status byte of the report it replaces, AGC delay X, Y, hover stage, samples per line
        // Line sums are stored / 4 when the hover stage is not 0 (longer reads), saturated at 0xFFFF.
        // Stock and every other block leave 0x20001040-0x2000106F unused. (R43: Multi-coil Read keeps no RAM; its
        // first version's block at 0x20001080 is gone.)
        public const int LR_ADDR = 0x20001040, LR_POS = 0x200008BC;
        public const int COLD_MARK = 0x7277A5C3, T16 = 0x40014800;
        // bootloader counter: hook at the entry of the bootloader's set_boot_state (0x08000F40, page 0x08000C00); the
        // routine lives in the free page 0x08002800. Boot state (word 0 of 0x08003400): 0 run, 1 DFU requested, 2 update
        // or erase in progress (set_boot_state(2) writes only that word). A count is a change 2 -> anything else: the
        // moment the tablet leaves DFU after an update or erase. Boots (0 -> 0, 1 -> 0) and repeats (2 -> 2) never count.
        public const int BL_PAGE = 0x08000C00, BL_ERASE = 0x08000F40, BL_WRAP = 0x08002800, BL_PROGRAM = 0x08000A50, BL_UNLOCK = 0x08000A30;
        public const int BL_STATE = 0x08003400, BL_STOCK_WORD = 0x4D0EB570, BL_ID = unchecked((int)0xB007B007);
        // reset-vector self-heal (bundled with the counter): the bootloader's reset path is vector(0x08000004) ->
        // handler(0x08000164) -> trampoline(0x08000150), which does `ldr r0,[0x0800015C]; bx r0` into the boot code at
        // 0x08000C38. That literal word (stock value 0x08000C39) is the ONLY thing steering reset into page 0x08000C00.
        // Redirecting it to a repair stub (placed after the counter routine in the never-erased page 0x08002800) makes
        // the stub run on every boot, repairing page 0x08000C00 from the stock copy BEFORE the boot code runs - closing
        // the one gap the firmware self-heal cannot (an interrupted C00 rewrite that erases the boot code itself).
        public const int BL_PAGE0 = 0x08000000, BL_BOOT = 0x08000C38, BL_BOOT_WORD = 0x08000C39, BL_BOOT_LIT = 0x0800015C;
        public static int BlPatchWord() { int[] e = Asm.EncBl(BL_ERASE, BL_WRAP, false); return (e[0] & 0xFFFF) | (e[1] << 16); }

        // runs inside the BOOTLOADER, at set_boot_state(r0 = new state): when the state leaves 2, add one counter record
        // runs inside the BOOTLOADER at set_boot_state. stockAt = where the stock copy of page 0x08000C00 lives (app
        // flash); anchor = that page's first word, used to confirm the copy is really the stock page before trusting it.
        public static byte[] BlWrapper(int stockAt, int anchor)
        {
            Asm w = new Asm(BL_WRAP);
            // ---- self-heal: on every entry, finish any erased words of the hook page 0x08000C00 from the stock copy.
            // set_boot_state only runs on a DFU-state boot, and a normal boot runs the firmware (which also repairs),
            // so between the two every reachable boot repairs an interrupted hook-page rewrite. Only turns 0xFFFFFFFF
            // into the stock value (never erases), and only if the stock copy is present and matches its anchor, so it
            // can never corrupt a good page. The hook word itself is non-erased whenever this code runs (we got here
            // through it), so it is left as the hook. Crucially this also restores the set_boot_state body that the
            // wrapper is about to return into.
            w.PushW(new string[] { "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7" }, true);
            w.Mov32("r5", stockAt); w.Ldr("r1", "r5", 0); w.Mov32("r2", anchor); w.CmpR("r1", "r2"); w.Bcc("ne", "sh_skip");
            w.BlA(BL_UNLOCK);
            w.Mov32("r4", BL_PAGE); w.Mov32("r6", BL_PAGE + 1024);
            w.Label("sh_l");
            w.Ldr("r1", "r4", 0); w.AddsI3("r2", "r1", 1); w.Bcc("ne", "sh_n");       // page word written -> leave it
            w.Ldr("r1", "r5", 0); w.AddsI3("r2", "r1", 1); w.Bcc("eq", "sh_n");       // stock word erased -> nothing to put
            w.Mov("r0", "r4"); w.Ldr("r1", "r5", 0); w.BlA(BL_PROGRAM);               // fill page word from the stock copy
            w.Label("sh_n"); w.AddsI("r4", 4); w.AddsI("r5", 4);
            w.CmpR("r4", "r6"); w.Bcc("cc", "sh_l");
            w.Label("sh_skip");
            w.PopW(new string[] { "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7" }, true);
            // ---- count: when the DFU state leaves 2, add one counter record
            w.Mov32("r1", BL_STATE); w.Ldr("r1", "r1", 0); w.Uxtb("r1", "r1"); w.CmpI("r1", 2); w.Bcc("ne", "orig");
            w.CmpI("r0", 2); w.Bcc("eq", "orig");
            w.Push(new string[] { "r0", "r4", "r5", "r6", "r7" }, true);
            w.BlA(BL_UNLOCK);
            w.Mov32("r4", Build.COUNTER_PAGE); w.Ldr("r0", "r4", 0); w.Mov32("r1", Build.COUNTER_MAGIC); w.CmpR("r0", "r1"); w.Bcc("ne", "done");
            w.Mov("r5", "r4"); w.AddsI("r5", 16); w.Mov32("r6", Build.COUNTER_PAGE + 0x400);
            w.Label("scan"); w.CmpR("r5", "r6"); w.Bcc("cs", "done");
            w.Ldr("r0", "r5", 0); w.AddsI3("r1", "r0", 1); w.Bcc("ne", "next");
            w.Ldr("r0", "r5", 4); w.AddsI3("r1", "r0", 1); w.Bcc("eq", "slot");
            w.Label("next"); w.AddsI("r5", 8); w.B("scan");
            w.Label("slot");
            w.Mov("r0", "r5"); w.Mov32("r1", BL_ID); w.BlA(BL_PROGRAM);
            w.AddsI3("r0", "r5", 4); w.Mov32("r1", ~BL_ID); w.BlA(BL_PROGRAM);
            w.Label("done"); w.PopW(new string[] { "r0", "r4", "r5", "r6", "r7" }, true);
            w.Label("orig"); w.Push(new string[] { "r4", "r5", "r6" }, true); w.Mov32("r5", BL_STATE); w.BWA(BL_ERASE + 4);
            return w.Finish();
        }

        // the reset-vector repair stub: assembled to run at `at` (inside page 0x08002800, never erased by DFU). Reached
        // from reset once the page-0 literal is redirected to it. SP is already set and the bootloader's own init has
        // run (the trampoline did both before `bx`). It fills any erased word of page 0x08000C00 from the stock copy
        // (0xFFFFFFFF -> stock only, never erases, only if the stock copy matches its anchor), then jumps to the real
        // boot code. On a normal boot nothing is erased, so it finds nothing, touches no flash, and falls straight
        // through to the boot code.
        public static byte[] BlStub(int stockAt, int anchor, int at)
        {
            Asm s = new Asm(at);
            s.Mov32("r5", stockAt); s.Ldr("r1", "r5", 0); s.Mov32("r2", anchor); s.CmpR("r1", "r2"); s.Bcc("ne", "st_boot");
            // pass 1: does any page word need filling? (page word erased while the stock word is present)
            s.Mov32("r4", BL_PAGE); s.Mov32("r6", BL_PAGE + 1024); s.Movs("r3", 0);
            s.Label("st_s");
            s.Ldr("r1", "r4", 0); s.AddsI3("r2", "r1", 1); s.Bcc("ne", "st_sn");
            s.Ldr("r1", "r5", 0); s.AddsI3("r2", "r1", 1); s.Bcc("eq", "st_sn");
            s.Movs("r3", 1);
            s.Label("st_sn"); s.AddsI("r4", 4); s.AddsI("r5", 4); s.CmpR("r4", "r6"); s.Bcc("cc", "st_s");
            s.CmpI("r3", 0); s.Bcc("eq", "st_boot");
            // pass 2: unlock once, then fill the erased words from the stock copy
            s.BlA(BL_UNLOCK);
            s.Mov32("r4", BL_PAGE); s.Mov32("r5", stockAt); s.Mov32("r6", BL_PAGE + 1024);
            s.Label("st_r");
            s.Ldr("r1", "r4", 0); s.AddsI3("r2", "r1", 1); s.Bcc("ne", "st_rn");
            s.Ldr("r1", "r5", 0); s.AddsI3("r2", "r1", 1); s.Bcc("eq", "st_rn");
            s.Mov("r0", "r4"); s.Ldr("r1", "r5", 0); s.BlA(BL_PROGRAM);
            s.Label("st_rn"); s.AddsI("r4", 4); s.AddsI("r5", 4); s.CmpR("r4", "r6"); s.Bcc("cc", "st_r");
            s.Label("st_boot"); s.Mov32("r0", BL_BOOT | 1); s.Bx("r0");
            return s.Finish();
        }

        // the counter routine + the reset-vector repair stub as one blob, laid out for page 0x08002800. stubEntryOut[0]
        // receives the stub's run address (the page-0 literal is redirected there).
        public static byte[] BlBlob(int stockAt, int anchor, int[] stubEntryOut)
        {
            byte[] wrap = BlWrapper(stockAt, anchor);
            int stubAt = (BL_WRAP + wrap.Length + 3) & ~3;
            byte[] stub = BlStub(stockAt, anchor, stubAt);
            stubEntryOut[0] = stubAt;
            Bytes b = new Bytes();
            for (int i = 0; i < wrap.Length; i++) b.Add(wrap[i] & 0xFF);
            while (BL_WRAP + b.n < stubAt) b.Add(0);
            for (int i = 0; i < stub.Length; i++) b.Add(stub[i] & 0xFF);
            return b.Take(b.n);
        }
        public const int BOOST_TOUCH = 50, LONG_AFTER = 2;
        public static readonly int[] LONG_SEARCH = new int[] { 24, 254 };

        static string[] Rg(string a) { return new string[] { a }; }
        static string[] Rg(string a, string b) { return new string[] { a, b }; }
        static string[] Rg(string a, string b, string c) { return new string[] { a, b, c }; }
        static string[] Rg(string a, string b, string c, string d) { return new string[] { a, b, c, d }; }
        static readonly string[] NONE = new string[0];

        static void H16(Bytes o, int v) { o.Add(v & 0xFF); o.Add((v >> 8) & 0xFF); }

        // rd = |rn - rm| (signed), using a unique local label
        static void AbsDiff(Asm a, string rd, string rn, string rm, string lbl) { a.SubsR(rd, rn, rm); a.Bcc("pl", lbl); a.Rsbs(rd); a.Label(lbl); }

        // Jump Rejection (the "odd" block, R31 momentum model): inline in the frame restart, or a routine in the Filter
        // Arranger build (atf_lab.cs). Keeps r4-r7.
        public static void EmitJumpRejection(Asm a, OddCfg odd)
        {
            // Jump Rejection with a momentum model. From the last two positions the firmware knows the pen's
            // velocity v. A real pen can carry on (next ~ last + v), slow down, or stop (next ~ last), but it
            // cannot teleport. So the accepted region is the L1 corridor between the STOP point S = last and the
            // COAST point E = last + 1.5*v (velocity plus a little room for acceleration), widened by the running
            // noise and the margin: a reading with distance(S) + distance(E) <= |S->E| + slack is on the pen's
            // path and is kept untouched. A reading outside it is a spike; it is replaced by the plain coast
            // C = last + v (where the pen was heading), not frozen, and the state keeps coasting so a real move is
            // not stalled. After maxRun rejections in a row the reading is taken as a real jump and let through.
            // VC: +0/2 last x,y; +4/6 previous x,y; +8 noise; +10 warmup (0..2); +11 reject run; +12 reject count;
            //     +16/20/24/28 scratch ex,ey,cx,cy (signed).
            a.Push(Rg("r4", "r5", "r6", "r7"), false);
            a.Mov32("r5", VC_ADDR); a.Mov32("r6", D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("eq", "o_rst");
            a.Ldrh("r0", "r6", 8); a.Ldrh("r1", "r6", 10);            // r0 = x, r1 = y (held across the block)
            a.Ldrb("r2", "r5", 10); a.CmpI("r2", 2); a.Bcc("cs", "o_run");
            a.AddsI("r2", 1); a.Strb("r2", "r5", 10); a.B("o_acc");   // warming up: just record the reading
            a.Label("o_run");
            // cx = px + vx ; ex = cx + vx/2   (vx = px - ppx), likewise y
            a.Ldrh("r2", "r5", 0); a.Ldrh("r3", "r5", 4); a.SubsR("r3", "r2", "r3");
            a.AddsR("r4", "r2", "r3"); a.Str("r4", "r5", 24);
            a.Asrs("r7", "r3", 1); a.AddsR("r4", "r4", "r7"); a.Str("r4", "r5", 16);
            a.Ldrh("r2", "r5", 2); a.Ldrh("r3", "r5", 6); a.SubsR("r3", "r2", "r3");
            a.AddsR("r4", "r2", "r3"); a.Str("r4", "r5", 28);
            a.Asrs("r7", "r3", 1); a.AddsR("r4", "r4", "r7"); a.Str("r4", "r5", 20);
            // lhs = |x-px| + |y-py| + |x-ex| + |y-ey|
            a.Ldrh("r2", "r5", 0); AbsDiff(a, "r2", "r0", "r2", "o_s1");
            a.Ldrh("r3", "r5", 2); AbsDiff(a, "r3", "r1", "r3", "o_s2"); a.AddsR("r2", "r2", "r3");
            a.Ldr("r3", "r5", 16); AbsDiff(a, "r3", "r0", "r3", "o_e1");
            a.Ldr("r4", "r5", 20); AbsDiff(a, "r4", "r1", "r4", "o_e2"); a.AddsR("r3", "r3", "r4");
            a.AddsR("r2", "r2", "r3");                                 // r2 = lhs
            // rhs = |ex-px| + |ey-py| + 6*noise + 2*margin
            a.Ldr("r3", "r5", 16); a.Ldrh("r4", "r5", 0); AbsDiff(a, "r3", "r3", "r4", "o_g1");
            a.Ldr("r4", "r5", 20); a.Ldrh("r7", "r5", 2); AbsDiff(a, "r4", "r4", "r7", "o_g2"); a.AddsR("r3", "r3", "r4");
            a.Ldrh("r4", "r5", 8); a.Movs("r7", 6); a.Muls("r4", "r7"); a.AddsR("r3", "r3", "r4");
            a.Movw("r7", 2 * odd.margin); a.AddsR("r3", "r3", "r7");   // r3 = rhs
            a.CmpR("r2", "r3"); a.Bcc("ls", "o_ok");
            // reject
            a.Ldrb("r7", "r5", 11); a.CmpI("r7", odd.maxRun); a.Bcc("cs", "o_give");
            a.AddsI("r7", 1); a.Strb("r7", "r5", 11);
            a.Ldr("r7", "r5", 12); a.AddsI("r7", 1); a.Str("r7", "r5", 12);
            a.Movs("r4", 0);
            a.Ldr("r0", "r5", 24); a.CmpR("r0", "r4"); a.Bcc("ge", "o_cx"); a.Mov("r0", "r4"); a.Label("o_cx");
            a.Ldr("r1", "r5", 28); a.CmpR("r1", "r4"); a.Bcc("ge", "o_cy"); a.Mov("r1", "r4"); a.Label("o_cy");
            a.Strh("r0", "r6", 8); a.Strh("r1", "r6", 10);            // emit the coast position
            a.B("o_acc");
            a.Label("o_give"); a.Movs("r7", 0); a.Strb("r7", "r5", 11); a.B("o_acc");   // give up: take the reading
            a.Label("o_ok");
            a.Movs("r7", 0); a.Strb("r7", "r5", 11);
            a.Ldr("r3", "r5", 24); AbsDiff(a, "r3", "r0", "r3", "o_r1");
            a.Ldr("r4", "r5", 28); AbsDiff(a, "r4", "r1", "r4", "o_r2"); a.AddsR("r3", "r3", "r4");
            a.Ldrh("r4", "r5", 8); a.SubsR("r3", "r3", "r4"); a.Asrs("r3", "r3", 3); a.AddsR("r4", "r4", "r3");
            a.CmpI("r4", 5); a.Bcc("cs", "o_ef"); a.Movs("r4", 5);
            a.Label("o_ef"); a.Strh("r4", "r5", 8);
            a.Label("o_acc");
            a.Ldrh("r2", "r5", 0); a.Strh("r2", "r5", 4); a.Ldrh("r2", "r5", 2); a.Strh("r2", "r5", 6);
            a.Strh("r0", "r5", 0); a.Strh("r1", "r5", 2); a.B("o_end");
            a.Label("o_rst"); a.Movs("r0", 0); a.Strb("r0", "r5", 10); a.Strb("r0", "r5", 11);
            a.Label("o_end"); a.Pop(Rg("r4", "r5", "r6", "r7"), false);
        }

        // Aggressive Position Interpolation (the VF block, R30): inline in the frame restart, or a routine in the Filter
        // Arranger build. Keeps r4-r7.
        public static void EmitAggressive(Asm a)
        {
            // Aggressive Position Interpolation: a plain moving average of the last positions (16 when still, 8 when
            // slow, 4 when fast), after the firmware's own 3/5 average and Jump Rejection. An average settles exactly
            // on the real pen position (no dead zone, unlike Movement Smoothing); it only adds delay.
            a.Push(Rg("r5", "r6", "r7"), false);
            a.Mov32("r5", VF_ADDR); a.Mov32("r6", D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "af_go");
            a.Movs("r0", 0); a.Strb("r0", "r5", 0); a.B("af_end");
            a.Label("af_go");
            a.Ldrh("r0", "r6", 8); a.Ldrh("r1", "r6", 10);
            a.Movs("r3", 0); a.Ldrb("r2", "r5", 0); a.Cbz("r2", "af_sp");
            a.Ldrh("r3", "r5", 2); a.SubsR("r3", "r0", "r3"); a.Bcc("pl", "af_ax"); a.Rsbs("r3");
            a.Label("af_ax"); a.Ldrh("r7", "r5", 4); a.SubsR("r7", "r1", "r7"); a.Bcc("pl", "af_ay"); a.Rsbs("r7");
            a.Label("af_ay"); a.AddsR("r3", "r3", "r7");
            a.Label("af_sp"); a.Strh("r0", "r5", 2); a.Strh("r1", "r5", 4);
            // R41: mask the slot before the write too. VF[1] is never cleared at power-up, so its first use could
            // write anywhere up to VF + 8 + 4 x 255 (0x20001367); the read loop below already masks it.
            a.Ldrb("r2", "r5", 1); a.Movs("r7", 15); a.Ands("r2", "r7");
            a.Lsls("r7", "r2", 2); a.AddsR("r7", "r7", "r5"); a.Strh("r0", "r7", 8); a.Strh("r1", "r7", 10);
            a.AddsI("r2", 1); a.Movs("r7", 15); a.Ands("r2", "r7"); a.Strb("r2", "r5", 1);
            a.Ldrb("r2", "r5", 0); a.CmpI("r2", 16); a.Bcc("cs", "af_c"); a.AddsI("r2", 1); a.Strb("r2", "r5", 0);
            a.Label("af_c");
            a.Movs("r7", 16); a.CmpI("r3", 20); a.Bcc("cc", "af_n"); a.Movs("r7", 8); a.CmpI("r3", 80); a.Bcc("cc", "af_n"); a.Movs("r7", 4);
            a.Label("af_n"); a.CmpR("r7", "r2"); a.Bcc("ls", "af_n2"); a.Mov("r7", "r2");
            a.Label("af_n2");
            a.Push(Rg("r4"), false);
            a.Movs("r0", 0); a.Movs("r1", 0); a.Ldrb("r2", "r5", 1); a.Mov("r4", "r7");
            a.Label("af_l"); a.SubsI("r2", 1); a.Movs("r3", 15); a.Ands("r2", "r3");
            a.Lsls("r3", "r2", 2); a.AddsR("r3", "r3", "r5");
            a.Ldrh("r6", "r3", 8); a.AddsR("r0", "r0", "r6"); a.Ldrh("r6", "r3", 10); a.AddsR("r1", "r1", "r6");
            a.SubsI("r4", 1); a.Bcc("ne", "af_l");
            a.Pop(Rg("r4"), false);
            a.Lsrs("r3", "r7", 1); a.AddsR("r0", "r0", "r3"); a.AddsR("r1", "r1", "r3");
            a.Udiv("r0", "r0", "r7"); a.Udiv("r1", "r1", "r7");
            a.Mov32("r6", D_ADDR); a.Strh("r0", "r6", 8); a.Strh("r1", "r6", 10);
            a.Label("af_end"); a.Pop(Rg("r5", "r6", "r7"), false);
        }

        // +0 three stage entries of 16 bytes: period, scan pulses/tick/samples, sweep pulses/tick/samples,
        //    scan pick-up/drop and sweep pick-up/drop thresholds; +48 quick search, +64 long search, +96 tilt frames
        // R45-rc4 dynamic Steady Hover: +0 one 16-byte entry per ladder step (as ModeTable's), then halfwords: the
        // step-down levels, the step-up levels (to the next faster step, until measured), then bytes: stage,
        // frames to step down, frames to step up, pacing (0 USB frames, 1 TIMER16). RAM (VBX): +8 the running step,
        // +9 frames above the step-up level, +10 the level the last step down left at, +12 1 = no step up yet (a found
        // pen before its first full sweep), +14 frames until the step down's ratio is measured, +15 the running step's pacing (builds that mix both, see "arm"); VL_ADDR: the
        // measured step-up levels of steps 1.. (halfwords, 0 = not measured yet; the first rc4 build kept 4 at VBX+0).
        // Steps: at most LAD_MAX.
        public const int LAD_MAX = 24;
        // free: stock stays below 0x20000E70 and the other blocks end at 0x20000FA7 (VF), the line recorder starts at
        // 0x20001040; cleared at cold boot
        public const int VL_ADDR = 0x20000FA8, VL_BYTES = 48;
        public static int LadTOff(Cfg c) { return 16 * c.nLad; }
        public static int LadUOff(Cfg c) { return LadTOff(c) + 2 * c.nLad; }
        public static int LadStOff(Cfg c) { return LadUOff(c) + 2 * c.nLad; }
        public static int LadDnOff(Cfg c) { return LadStOff(c) + c.nLad; }
        public static int LadUpOff(Cfg c) { return LadDnOff(c) + c.nLad; }
        public static int LadPmOff(Cfg c) { return LadUpOff(c) + c.nLad; }
        public static byte[] LadderTable(Cfg cfg)
        {
            Bytes o = new Bytes();
            int n = cfg.nLad;
            for (int i = 0; i < n; i++)
            {
                Stage m = cfg.lad[i];
                H16(o, m.period);
                o.Add(m.burst); o.Add(m.tick); o.Add(m.samples); o.Add(m.swBurst); o.Add(m.swTick); o.Add(m.swSamples);
                H16(o, m.pick); H16(o, m.drop); H16(o, m.swPick); H16(o, m.swDrop);
            }
            for (int i = 0; i < n; i++) H16(o, cfg.ladT[i]);
            for (int i = 0; i < n; i++) H16(o, i > 0 ? cfg.ladU[i, i - 1] : 0);
            for (int i = 0; i < n; i++) o.Add(cfg.ladStage[i]);
            for (int i = 0; i < n; i++) o.Add(cfg.ladDown[i]);
            for (int i = 0; i < n; i++) o.Add(cfg.ladUp[i]);
            for (int i = 0; i < n; i++) o.Add(cfg.ladTimer[i] ? 1 : 0);
            while (o.n % 4 != 0) o.Add(0);
            return o.Take(o.n);
        }

        public static byte[] ModeTable(Cfg cfg)
        {
            Bytes o = new Bytes();
            for (int i = 0; i < 3; i++)
            {
                if (i >= cfg.nModes) { for (int k = 0; k < 16; k++) o.Add(0); continue; }
                Stage m = cfg.modes[i];
                H16(o, m.period);
                o.Add(m.burst); o.Add(m.tick); o.Add(m.samples); o.Add(m.swBurst); o.Add(m.swTick); o.Add(m.swSamples);
                H16(o, m.pick); H16(o, m.drop); H16(o, m.swPick); H16(o, m.swDrop);
            }
            int[] qs = cfg.quickSearch;
            o.Add(0); o.Add(0); o.Add(qs[1]); o.Add(Build.TickFor(qs[1], qs[0])); o.Add(qs[0]); for (int k = 0; k < 11; k++) o.Add(0);
            if (cfg.longSearch) { o.Add(0); o.Add(0); o.Add(LONG_SEARCH[1]); o.Add(Build.TickFor(LONG_SEARCH[1], LONG_SEARCH[0])); o.Add(LONG_SEARCH[0]); for (int k = 0; k < 11; k++) o.Add(0); }
            else for (int k = 0; k < 16; k++) o.Add(0);
            o.Add(cfg.buildId & 0xFF); o.Add((cfg.buildId >> 8) & 0xFF); o.Add((cfg.buildId >> 16) & 0xFF); o.Add((cfg.buildId >> 24) & 0xFF);
            for (int k = 0; k < 12; k++) o.Add(0);
            for (int k = 0; k < cfg.phases.Length; k++) o.Add(cfg.phases[k]);
            while (o.n % 4 != 0) o.Add(0);
            return o.Take(o.n);
        }

        // TIMER16 free-running at 1 MHz (prescaler 71, top 0xFFFF): the frame clock of the timer-paced presets
        static void EmitT16Init(Asm a)
        {
            a.Movw("r0", 0x612); a.BlA(Fw.A(0x08008E94));
            a.Mov32("r0", T16); a.Movs("r1", 71); a.Str("r1", "r0", 0x28);
            a.Movw("r1", 0xFFFF); a.Str("r1", "r0", 0x2C);
            a.Movs("r1", 1); a.Str("r1", "r0", 0x14); a.Str("r1", "r0", 0);
        }

        public static void Patch(Image im, Level lv, Cfg cfg)
        {
            int periodUs = lv.period;
            bool sof = periodUs % 1000 == 0;
            for (int i = 0; i < cfg.nModes; i++) if (cfg.modes[i].periodUs % 1000 != 0) sof = false;
            int tiltEvery = lv.tilt, split = lv.split, chunk = lv.chunk, sweepLen = lv.sweepLen;
            int[] phases = split == 2 ? new int[] { 3, 12 } : new int[] { 1, 2, 4, 8 };
            int sb = 0;
            if (!(chunk <= 0 || chunk >= sweepLen)) for (int i = chunk; i < sweepLen; i += chunk) sb += 1 << i;
            int CX0 = V_ADDR + 16, CX6 = V_ADDR + 28, CY0 = V_ADDR + 40, CY6 = V_ADDR + 52;
            bool boost = cfg.boost, reach = cfg.reach, longS = cfg.longSearch;
            CalCfg cal = cfg.cal; OddCfg odd = cfg.odd;
            int top = reach ? 2 : 1;
            cfg.phases = phases;
            int tab = im.AllocData(ModeTable(cfg), "scheduler tables: hover-stage step profiles, tilt frames");
            int nl = boost ? cfg.nLad : 0;
            if (boost && (cfg.lad == null || nl < 2 || nl > LAD_MAX || 2 * (nl - 1) > VL_BYTES)) throw new Stop("internal: Steady Hover ladder missing or too long");
            // R45-rc4: a USB-frame-locked preset whose ladder has steps that are not a whole number of frames paces
            // those by TIMER16 and the others by the USB frames (VBX+15, set by setlad)
            bool hyb = false;
            if (sof && boost) for (int i = 0; i < nl; i++) if (cfg.ladTimer[i]) hyb = true;
            int ltab = boost ? im.AllocData(LadderTable(cfg), "Steady Hover (dynamic): the read-length ladder (fast stage, Steady Hover steps, reach), step-down and step-up levels") : 0;
            int mctab = 0;
            if (cfg.multiCoil)
                mctab = im.AllocData(MultiCoil.Table(), "Multi-coil Read: coil response T and its change E, 1/16 pitch steps over -2.8..2.8 pitches (Q12, measured R43, effective range 17/16)");
            int wstab = 0;
            if (cfg.winScale && !cfg.multiCoil)
                wstab = im.AllocData(WindowScale.Table(), "Window Scale: the stretch of the position inside a coil window per pressure, X then Y, 9 knots each (Q12, measured rc4 (2))");
            int cgtab = 0;
            if (cfg.coilGains && wstab != 0 && !cfg.lab)
                cgtab = im.AllocData(CoilGains.Table(), "Coil Gains: 4096 / each coil's gain at " + F.Fx(CoilGains.STRENGTH / 10.0, 1) + " % of the measured difference, X then Y, 40 slots each (coil k at slot k + 2; Q12, measured on the R43 strokes)");
            int aptab = 0;
            if (cfg.apip != null)
                aptab = im.AllocData(Apip.Table(cfg.apip, cfg.apipK), cfg.apipK == null
                    ? "ALgorithmic pen Position Interpolation: level thresholds, per band and axis gains (moving and still), starting noise and coil-distance correction (trained " + cfg.apip.trained + ")"
                    : "ALgorithmic pen Position Interpolation, robust filter with " + F.Str(cfg.apipK[ApipRobust.K_M]) + " trackers: level thresholds, per band and axis starting noise and coil-distance correction (trained " + cfg.apip.trained + "), then the trackers' step noise per band and the filter's tables");
            if (cfg.apipTrain) im.AllocData(Apip.TrainMarker(), "ALPI training build marker (samples on USB string " + F.Str(Apip.STRING) + ")");
            bool zled = cfg.telemetry || cfg.apipTrain;        // R44: the LED zone (calibration pass, APIP training)
            // R45: the stages where the step timer drives the pulse's PWM (LED_FADE); the others: once per frame. The
            // gate tells them apart by the running stage (VB[0]), so the mix must be "all but the fast stage".
            int ledStages = zled ? LedStages(lv, cfg) : 0, allStages = cfg.nModes > 0 ? (1 << cfg.nModes) - 1 : 1;
            bool ledAll = zled && ledStages == allStages, ledSome = zled && ledStages != 0 && !ledAll;
            // R45-rc4: with the dynamic Steady Hover ladder the stage no longer says how long a step's pulses are (its
            // fast Steady Hover steps keep the fast stage's 16-24 pulses), so the choice is made per ladder step: a
            // table byte per step, 1 = the step timer drives the pulse in it (LedStepTable), read with the running
            // step (VBX+8)
            int ledsteps = 0;
            if (zled && boost)
            {
                byte[] lf = LedStepTable(cfg);
                int nOn = 0;
                for (int i = 0; i < nl; i++) nOn += lf[i];
                ledAll = nOn == nl; ledSome = nOn > 0 && !ledAll;
                if (ledSome) ledsteps = im.AllocData(lf, "LED pulse: per Steady Hover ladder step, 1 = the step timer drives the PWM (every scan and sweep step switches it at least " + F.Str(LED_SCAN_US) + " us before its readings), 0 = once per frame");
            }
            else if (ledSome && ledStages != (allStages & ~1)) throw new Stop("internal: LED pulse: unexpected stage mix " + F.Str(ledStages));
            int ledtab = zled ? im.AllocData(LedTable(), "LED pulse: the share of time lit (/256) for each 10 ms of the " + F.Str(LED_FADE_MS) + " ms pulse") : 0;
            int stockAt = 0, wrapAt = 0, nWrap = 0, stock0At = 0, stubEntry = 0;
            int pidx = (BL_ERASE - BL_PAGE) / 4, p0idx = (BL_BOOT_LIT - BL_PAGE0) / 4, pw = BlPatchWord();
            if (cfg.blCounter || cfg.cleanup)
            {
                // the stock page is allocated FIRST so its address (stockAt) is known when the routine is assembled -
                // both the wrapper's self-heal and the reset-vector stub read the stock page from there to finish an
                // interrupted hook-page rewrite.
                Bytes sp = new Bytes();
                for (int k = 0; k < 1024; k++) sp.Add(cfg.stockPage[k]);
                stockAt = im.AllocData(sp.Take(sp.n), "stock bootloader page 0x08000C00 (for install + self-heal)");
                int anchor = Machine.LE(cfg.stockPage, 0, 4);
                int[] se = new int[1];
                byte[] wrap = BlBlob(stockAt, anchor, se);
                wrapAt = im.AllocData(wrap, "bootloader counter + self-heal + reset-vector repair stub");
                nWrap = (wrap.Length + 3) / 4;
                stubEntry = se[0];
                // stored copy of reset-vector page 0x08000000, with the boot-jump literal at 0x0800015C redirected to
                // the stub. p0write programs page 0 from this copy, so the whole page is pure stock except that one word.
                byte[] p0b = new byte[1024];
                for (int k = 0; k < 1024; k++) p0b[k] = cfg.stock0Page[k];
                int rv = stubEntry | 1;
                p0b[p0idx * 4] = (byte)(rv & 0xFF); p0b[p0idx * 4 + 1] = (byte)((rv >> 8) & 0xFF);
                p0b[p0idx * 4 + 2] = (byte)((rv >> 16) & 0xFF); p0b[p0idx * 4 + 3] = (byte)((rv >> 24) & 0xFF);
                stock0At = im.AllocData(p0b, "stock reset-vector page 0x08000000, boot-jump redirected to the repair stub");
            }
            // R45-rc4 Switch Match: each ladder step's (or hover stage's) frame period, in 25 us ticks
            int smtab = 0, smN = 0;
            if (cfg.switchMatch)
            {
                smN = boost ? nl : Math.Max(1, cfg.nModes);
                int[] per = new int[smN];
                for (int i = 0; i < smN; i++)
                {
                    int p = boost ? cfg.lad[i].periodUs : cfg.nModes > 0 ? cfg.modes[i].periodUs : 0;
                    per[i] = p > 0 ? p : periodUs;
                }
                smtab = im.AllocData(SwitchMatch.TickTable(per), "Switch Match: frame period per " + (boost ? "Steady Hover ladder step" : "hover stage") + " (25 us ticks)");
            }
            // R45-rc5 Coil Linearization (Misc. Optimizations): the correction inside each coil, 64 knots per axis (atf_coillin.cs)
            // (the Filter Arranger build: its CL-A is the first table, rc5; its CL-B is this table)
            int cltab = cfg.coilLin ? im.AllocData(cfg.lab ? CoilLinearization.Table(CoilLinearization.AX, CoilLinearization.AY) : CoilLinearization.Table(), "Coil Linearization: correction per 1/64 coil, X then Y (1/200 mm + 128)") : 0;
            int labB = 0, labC = 0;
            if (cfg.lab)
            {
                labB = im.AllocData(CoilLinearization.Table(Lab.BX, Lab.BY), "Filter Arranger: Coil Linearization table B (relearned on the rc5 ruler lines)");
                labC = im.AllocData(CoilLinearization.Table(Lab.CX, Lab.CY), "Filter Arranger: Coil Linearization table C (table B with ALPI's delay taken out)");
            }
            // R45-rc5 Dynamic Anti-Chatter: the previous frame's weight per ladder step (or hover stage), 0.4 ms / frame period
            int dactab = 0, dacN = 0;
            if (cfg.antiChatter)
            {
                dacN = boost ? nl : Math.Max(1, cfg.nModes);
                int[] dper = new int[dacN];
                for (int i = 0; i < dacN; i++)
                {
                    int p = boost ? cfg.lad[i].periodUs : cfg.nModes > 0 ? cfg.modes[i].periodUs : 0;
                    dper[i] = p > 0 ? p : periodUs;
                }
                dactab = im.AllocData(DynamicAntiChatter.WeightTable(dper), "Dynamic Anti-Chatter: the previous frame's weight per " + (boost ? "Steady Hover ladder step" : "hover stage") + " (256 = 1; at most 0.4 ms of delay)");
            }
            Asm a = new Asm(im.BeginAlloc());
            a.literalAsMov32 = true;
            // ---- fine-scan line selection per frame type
            a.Label("start_x"); a.SubsI("r1", 156);
            if (cfg.switchMatch) SwitchMatch.EmitCapture(a, boost);    // R45-rc4: this frame's reading conditions
            a.Mov32("r0", V_ADDR); a.Ldrb("r0", "r0", 2);
            a.Movs("r2", 1); a.Ands("r0", "r2"); a.Eors("r0", "r2"); a.BxLr();
            a.Label("start_y"); a.Mov32("r0", V_ADDR); a.Ldrb("r0", "r0", 2); a.Lsrs("r0", "r0", 2);
            a.Movs("r2", 1); a.Ands("r0", "r2"); a.Eors("r0", "r2"); a.Strb("r0", "r1", 1); a.BxLr();
            string[] axs = new string[] { "x", "y" };
            int[] bits = new int[] { 2, 8 };
            for (int q = 0; q < 2; q++)
            {
                string ax = axs[q];
                a.Label("inc_" + ax);
                a.Str("r0", "r2", 4);
                a.AddsI("r1", 1); a.CmpI("r1", 6); a.Bcc("ne", "ir_" + ax);
                a.Mov32("r3", V_ADDR); a.Ldrb("r3", "r3", 2); a.Movs("r0", bits[q]); a.Tst("r3", "r0");
                a.Bcc("ne", "ir_" + ax); a.Movs("r1", 7);
                a.Label("ir_" + ax); a.BxLr();
            }
            int[] bufs = new int[] { XBUF, YBUF }, c0s = new int[] { CX0, CY0 }, c6s = new int[] { CX6, CY6 };
            int[] sh0s = new int[] { 31, 29 }, sh6s = new int[] { 30, 28 }, tails = new int[] { Fw.A(0x080078E4), Fw.A(0x080079A8) };
            for (int q = 0; q < 2; q++)
            {
                string ax = axs[q];
                a.Label(ax + "done");
                a.PushW(Rg("r4"), true); a.Mov32("r4", V_ADDR); a.Ldrb("r4", "r4", 2);
                a.Lsls("r2", "r4", sh0s[q]); a.Bcc("pl", ax + "d1");
                a.Mov32("r0", bufs[q]); a.Mov32("r1", c0s[q]); a.Bl("cp12");
                a.Label(ax + "d1"); a.Lsls("r2", "r4", sh6s[q]); a.Bcc("pl", ax + "d2");
                a.Mov32("r0", bufs[q] + 72); a.Mov32("r1", c6s[q]); a.Bl("cp12");
                a.Label(ax + "d2"); a.PopW(Rg("r4"), true);
                if (!cfg.coilHold) { a.BWA(tails[q]); continue; }
                // Coil Hold (see COIL_MARGIN): the stock scan-complete code runs first (peak line, re-centre, AGC). When
                // it found the pen and moved the window by one coil, the move is undone (centre coil, peak line = 3,
                // its sum and last sample) unless the new peak line beats the centre line by the margin.
                a.Push(Rg("r4", "r5", "r6"), true);
                a.Mov32("r4", C_ADDR); a.Ldrb("r5", "r4", 6 + q);
                a.BlA(tails[q]); a.Mov("r6", "r0");
                a.CmpI("r0", 6); a.Bcc("ne", ax + "h_end");
                a.Mov32("r1", bufs[q]); a.Mov("r0", "r1"); a.AddsI("r0", 108); a.Ldrb("r2", "r0", 0);
                a.CmpI("r2", 2); a.Bcc("eq", ax + "h_n"); a.CmpI("r2", 4); a.Bcc("ne", ax + "h_end");
                a.Label(ax + "h_n");
                a.Movs("r3", 12); a.Muls("r3", "r2"); a.AddsR("r3", "r3", "r1"); a.Ldr("r3", "r3", 4);   // new peak line's sum
                a.Ldr("r0", "r1", 16);                                                                  // weakest of lines 1-5
                for (int k = 2; k <= 5; k++)
                {
                    string nk = ax + "h_m" + F.Str(k);
                    a.Ldr("r2", "r1", 4 + 12 * k); a.CmpR("r2", "r0"); a.Bcc("cs", nk); a.Mov("r0", "r2"); a.Label(nk);
                }
                a.Ldr("r2", "r1", 40); a.SubsR("r0", "r2", "r0");                                       // centre above the weakest
                a.Movs("r2", COIL_MARGIN); a.Muls("r0", "r2"); a.Lsrs("r0", "r0", 5);
                a.Ldr("r2", "r1", 40); a.AddsR("r0", "r0", "r2");
                a.CmpR("r3", "r0"); a.Bcc("hi", ax + "h_end");
                a.Strb("r5", "r4", 6 + q);
                a.AddsI("r1", 108); a.Movs("r0", 3); a.Strb("r0", "r1", 0); a.Str("r2", "r1", 4);
                a.Mov32("r0", bufs[q]); a.Ldrh("r0", "r0", 44); a.Strh("r0", "r1", 8);
                a.Label(ax + "h_end"); a.Mov("r0", "r6"); a.Pop(Rg("r4", "r5", "r6"), true);
            }
            a.Label("pen");
            if (cfg.telemetry)
            {
                // The host can select the vendor's alternate report format (mode 4, endpoint 3).
                // Calibration hooks the mode-2 ID-7 sends, so select that format before reports are built.
                // Normal firmware keeps the host's report-mode choice.
                a.Mov32("r0", REPORT_MODE); a.Movs("r1", 2); a.Strb("r1", "r0", 0);
            }
            a.Mov32("r0", V_ADDR); a.Ldrb("r0", "r0", 1); a.Cbz("r0", "pen_skip");
            a.Push(NONE, true); a.BlA(Fw.A(0x08007B30)); a.BlA(Fw.A(0x08007674)); a.Pop(NONE, true);
            a.Label("pen_skip"); a.BxLr();
            a.Label("freq");
            a.Mov32("r0", V_ADDR); a.Ldrb("r0", "r0", 1); a.Cbz("r0", "freq_skip");
            if (cfg.freqHold)
            {
                // Frequency Hold (see HOLD_ADDR): after each full sweep, D follows C[3] (the sweep's pick) only when
                // it is 2 or more steps away, or one step away for HOLD_AGREE sweeps in a row
                a.Mov32("r3", C_ADDR); a.Ldrb("r2", "r3", 3);
                a.Mov32("r1", HOLD_ADDR); a.Ldrb("r0", "r1", 0);
                a.SubsR("r0", "r0", "r2"); a.AddsI("r0", 1); a.CmpI("r0", 2); a.Bcc("hi", "fh_set");
                a.CmpI("r0", 1); a.Bcc("eq", "fh_same");
                a.Ldrb("r3", "r1", 1); a.AddsI("r3", 1); a.CmpI("r3", HOLD_AGREE); a.Bcc("cs", "fh_set");
                a.Strb("r3", "r1", 1); a.B("fh_done");
                a.Label("fh_same"); a.Movs("r3", 0); a.Strb("r3", "r1", 1); a.B("fh_done");
                a.Label("fh_set"); a.Strb("r2", "r1", 0); a.Movs("r3", 0); a.Strb("r3", "r1", 1);
                a.Label("fh_done");
            }
            a.BWA(Fw.A(0x08007604));
            a.Label("freq_skip"); a.BxLr();
            a.Label("tilt");
            if (cfg.lineRec)
            {
                // R42 line recorder: both positions are computed and the line buffers are still whole here (the next
                // scan clears them; at report time X is often cleared already). Copy unless the last copy is unsent.
                a.Push(Rg("r4", "r5"), false);
                a.Mov32("r3", LR_ADDR); a.Ldrb("r0", "r3", 0); a.CmpI("r0", 0); a.Bcc("eq", "lr_go"); a.B("lr_end");
                a.Label("lr_go"); a.Mov32("r5", 0xFFFF);
                // Steady Hover and Reach read longer, so their sums are stored / 4 (F4 carries the stage)
                a.Mov32("r1", VB_ADDR); a.Ldrb("r4", "r1", 0);
                int[] lrAt = new int[] { 8, 10, 12, 16, 18, 20, 24, 26, 28, 32 };
                for (int q = 0; q < 10; q++)
                {
                    if (q == 0) a.Mov32("r1", XBUF);
                    if (q == 5) a.Mov32("r1", YBUF);
                    a.Ldr("r0", "r1", 16 + 12 * (q % 5)); a.Cbz("r4", "lr_n" + F.Str(q)); a.Lsrs("r0", "r0", 2);
                    a.Label("lr_n" + F.Str(q)); a.CmpR("r0", "r5"); a.Bcc("ls", "lr_s" + F.Str(q)); a.Mov("r0", "r5");
                    a.Label("lr_s" + F.Str(q)); a.Strh("r0", "r3", lrAt[q]);
                }
                a.Mov32("r1", LR_POS); a.Ldr("r0", "r1", 4); a.Strh("r0", "r3", 34); a.Ldr("r0", "r1", 8); a.Strh("r0", "r3", 36);
                a.Mov("r2", "r3"); a.AddsI("r2", 32);
                a.Movs("r4", 15);
                a.Mov32("r1", XBUF); a.Ldr("r0", "r1", 108); a.Ands("r0", "r4");
                a.Mov32("r1", YBUF); a.Ldr("r1", "r1", 108); a.Ands("r1", "r4"); a.Lsls("r1", "r1", 4); a.Orrs("r0", "r1"); a.Strb("r0", "r2", 6);
                a.Mov32("r1", C_ADDR); a.Ldrb("r0", "r1", 6); a.Strb("r0", "r3", 22); a.Ldrb("r0", "r1", 7); a.Strb("r0", "r3", 30);
                a.Mov32("r1", AGC); a.Ldrb("r0", "r1", 1); a.Strb("r0", "r2", 11); a.Ldrb("r0", "r1", 2); a.Strb("r0", "r2", 12);
                a.Mov32("r1", VB_ADDR); a.Ldrb("r0", "r1", 0); a.Strb("r0", "r2", 13);
                a.Mov32("r1", S_ADDR); a.Ldrb("r0", "r1", 5); a.Strb("r0", "r2", 14);
                a.Ldrb("r0", "r3", 1); a.AddsI("r0", 1); a.Strb("r0", "r3", 1); a.Strb("r0", "r3", 14);
                a.Movs("r0", 5); a.Strb("r0", "r3", 0);
                a.Label("lr_end"); a.Pop(Rg("r4", "r5"), false);
            }
            a.Mov32("r0", V_ADDR); a.Ldrb("r1", "r0", 2); a.CmpI("r1", 0xF); a.Bcc("eq", "t_go");
            a.CmpI("r1", phases[phases.Length - 1]); a.Bcc("ne", "t_skip");
            a.Label("t_go"); a.PushW(NONE, true);
            int[] srcs = new int[] { CX0, CX6, CY0, CY6 }, dsts = new int[] { XBUF, XBUF + 72, YBUF, YBUF + 72 };
            for (int q = 0; q < 4; q++) { a.Mov32("r0", srcs[q]); a.Mov32("r1", dsts[q]); a.Bl("cp12"); }
            a.PopW(NONE, true); a.BWA(Fw.A(0x08005E84));
            a.Label("t_skip"); a.BxLr();
            a.Label("chunk");
            a.Mov32("r1", V_ADDR); a.Ldrb("r1", "r1", 2); a.CmpI("r1", 0xF); a.Bcc("eq", "cont");
            a.Ldrb("r0", "r4", 1); a.Movs("r1", 1); a.LslsR("r1", "r0"); a.Movs("r2", sb); a.Ands("r1", "r2"); a.Bcc("eq", "cont");
            a.Mov32("r1", V_ADDR); a.Strb("r0", "r1", 3);
            a.PopW(Rg("r4", "r5", "r6"), true); a.BWA(Fw.A(0x08009B58));
            a.Label("cont");
            a.Mov32("r1", C_ADDR); a.Movs("r0", 1); a.SubsI("r1", 32); a.Strb("r0", "r1", 7); a.Pop(Rg("r4", "r5", "r6"), true);
            a.Label("fin");
            a.Mov32("r0", V_ADDR); a.Movs("r1", 0); a.Strb("r1", "r0", 3);
            // R41: a sweep whose hover stage changed part-way mixes reading lengths, so its peak and frequency are
            // wrong (RAPID/ULTRA, twin: 45 of 527 / 57 of 201 weak-hover sweeps). Drop it whole: no peak tracking,
            // in-range, AGC or pen-state update, only the start of the next scan phase that 0x08007840 would make.
            a.Mov32("r1", VQ_ADDR); a.Ldrb("r1", "r1", 0); a.Cbnz("r1", "fin_mix");
            a.Movs("r1", 1); a.Strb("r1", "r0", 1);
            if (boost) { a.Mov32("r0", VBX_ADDR); a.Movs("r1", 0); a.Strb("r1", "r0", 12); }   // R45-rc4: see "m_air"
            a.BWA(Fw.A(0x08007840));
            a.Label("fin_mix");
            a.Push(Rg("r4"), true); a.BlA(Fw.A(0x08009B58)); a.Movs("r0", 0); a.Pop(Rg("r4"), true);

            // ================================================================ frame restart (main loop)
            a.Label("restart");
            a.Push(Rg("r4"), true); a.Bl("ledtick"); a.Mov32("r4", V_ADDR);
            if (boost)
            {
                a.Mov32("r1", VS_ADDR); a.Ldrb("r0", "r1", 3); a.Cbz("r0", "m_nf");
                a.Movs("r0", 0); a.Strb("r0", "r1", 3);
                a.Movs("r0", 2); a.Bl("setmode"); a.B("m_done");
                a.Label("m_nf");
                if (cal != null)
                {
                    a.Mov32("r1", VB_ADDR); a.Ldrb("r1", "r1", 13); a.CmpI("r1", 1); a.Bcc("ne", "m_lv");
                    a.B("m_done");
                }
                a.Label("m_lv");
                a.Mov32("r0", XBUF); a.Ldr("r1", "r0", 108); a.CmpI("r1", 6); a.Bcc("ls", "m_lv2"); a.B("m_done");
                a.Label("m_lv2");
                a.Movs("r2", 12); a.Muls("r2", "r1"); a.AddsR("r2", "r2", "r0"); a.Ldr("r2", "r2", 4);
                a.Mov32("r3", S_ADDR); a.Ldrb("r0", "r3", 8); a.Ldrb("r3", "r3", 5); a.SubsR("r3", "r3", "r0");
                a.Bcc("gt", "m_cnt"); a.Movs("r3", 1);
                a.Label("m_cnt"); a.Udiv("r2", "r2", "r3");
                a.Mov32("r1", VB_ADDR); a.Str("r2", "r1", 8);
                a.Mov32("r3", PRESSURE); a.Ldrh("r3", "r3", 6); a.CmpI("r3", BOOST_TOUCH); a.Bcc("ls", "m_air");
                a.Movs("r0", 0); a.Strb("r0", "r1", 6);
                a.Ldrb("r0", "r1", 0); a.Cbz("r0", "m_t0"); a.Movs("r0", 0); a.Bl("setmode");
                a.Label("m_t0"); a.B("m_done");
                a.Label("m_air");
                // R45-rc4 dynamic Steady Hover (Build.BuildLadder; Anargon: a speed limiter that holds the raw noise of
                // low hover and lowers the rate only as far as it must). r2 = this frame's level, in the units of the
                // running ladder step p (VBX[8]). Down: below this step's level (noise target, or near its drop level)
                // for LadDown frames in a row -> one step slower; the level it left at is kept (VBX+10), and 2 frames
                // later the new step's level gives the ratio of the two reads as they really are at this pen and
                // height: the step back up is taken at that ratio x the faster step's level x LAD_MARGIN (VL+2(p-1)).
                // Until a step has been measured, the table's (Build.Gain). Up: at or above it for LadUp frames in a
                // row -> one step faster. No probe frames (rc3's every 16 frames read 0.36 mm off and repeated 29 times
                // a second with a still pen), and like is compared with like, so a still pen does not switch (rc3:
                // 26 fast + 3 Steady Hover frames at low hover in the twin; a fixed ratio flapped too: the twin's
                // fast / Steady Hover ratio is 9 at 1 mm and 14 at 7 mm, the tablet's 10.7-11.6 where they switch).
                a.Push(Rg("r5", "r6"), false);
                a.Mov32("r3", VBX_ADDR); a.Ldrb("r0", "r3", 8);
                a.Ldrb("r1", "r3", 14); a.Cbz("r1", "l_nc");
                a.SubsI("r1", 1); a.Strb("r1", "r3", 14); a.Cbnz("r1", "l_nc");
                a.Ldrh("r1", "r3", 10); a.Cbz("r1", "l_nc");
                a.Cbz("r0", "l_nc");
                a.Lsls("r1", "r1", 2);
                a.Lsls("r5", "r0", 1); a.Mov32("r6", ltab + LadTOff(cfg) - 2); a.AddsR("r5", "r5", "r6"); a.Ldrh("r5", "r5", 0);
                a.Lsls("r6", "r5", 2); a.AddsR("r5", "r5", "r6");
                a.Muls("r5", "r2"); a.Udiv("r5", "r5", "r1");
                a.Mov32("r6", 65535); a.CmpR("r5", "r6"); a.Bcc("ls", "l_c1"); a.Mov("r5", "r6");
                a.Label("l_c1"); a.Cbnz("r5", "l_c2"); a.Movs("r5", 1);
                a.Label("l_c2"); a.Lsls("r1", "r0", 1); a.Mov32("r6", VL_ADDR - 2); a.AddsR("r1", "r1", "r6"); a.Strh("r5", "r1", 0);
                a.Label("l_nc");
                a.CmpI("r0", 0); a.Bcc("eq", "l_nup");
                a.Ldrb("r1", "r3", 12); a.Cbnz("r1", "l_nup");        // a found pen: no step up before its first full sweep
                a.Lsls("r1", "r0", 1); a.Mov32("r6", VL_ADDR - 2); a.AddsR("r1", "r1", "r6"); a.Ldrh("r6", "r1", 0);
                a.Cbnz("r6", "l_ul");
                a.Lsls("r1", "r0", 1); a.Mov32("r6", ltab + LadUOff(cfg)); a.AddsR("r1", "r1", "r6"); a.Ldrh("r6", "r1", 0);
                a.Label("l_ul"); a.CmpR("r2", "r6"); a.Bcc("cc", "l_nup");
                a.Ldrb("r6", "r3", 9); a.AddsI("r6", 1); a.Strb("r6", "r3", 9);
                a.Mov32("r1", ltab + LadUpOff(cfg)); a.LdrbR("r1", "r1", "r0"); a.CmpR("r6", "r1"); a.Bcc("cc", "l_end");
                a.SubsI("r0", 1); a.Bl("setlad"); a.B("l_end");
                a.Label("l_nup"); a.Movs("r6", 0); a.Strb("r6", "r3", 9);
                a.CmpI("r0", nl - 1); a.Bcc("cs", "l_end");
                a.Cbnz("r0", "l_dn0");
                a.Mov32("r1", S_ADDR); a.Ldrb("r1", "r1", 8); a.Cbnz("r1", "l_ok");          // the AGC delaying: strong
                a.Label("l_dn0");
                a.Lsls("r1", "r0", 1); a.Mov32("r5", ltab + LadTOff(cfg)); a.AddsR("r1", "r1", "r5"); a.Ldrh("r1", "r1", 0);
                a.CmpR("r2", "r1"); a.Bcc("cs", "l_ok");
                a.Mov32("r1", VB_ADDR); a.Ldrb("r6", "r1", 6); a.AddsI("r6", 1); a.Strb("r6", "r1", 6);
                a.Mov32("r1", ltab + LadDnOff(cfg)); a.LdrbR("r1", "r1", "r0"); a.CmpR("r6", "r1"); a.Bcc("cc", "l_end");
                a.AddsI("r0", 1); a.Mov("r5", "r2"); a.Bl("setlad");
                a.Mov32("r3", VBX_ADDR); a.Strh("r5", "r3", 10); a.Movs("r1", 2); a.Strb("r1", "r3", 14);
                a.B("l_end");
                a.Label("l_ok"); a.Mov32("r1", VB_ADDR); a.Movs("r6", 0); a.Strb("r6", "r1", 6);
                a.Label("l_end"); a.Pop(Rg("r5", "r6"), false);
                a.Label("m_done");
            }
            if (cal != null)
            {
                int S_ = cal.span, N_ = cal.frames, K_ = cal.skip;
                a.Push(Rg("r5", "r6", "r7"), false);
                a.Mov32("r5", VB_ADDR); a.Mov32("r6", D_ADDR);
                a.Mov32("r0", PRESSURE); a.Ldrh("r0", "r0", 6);
                a.Ldrb("r1", "r5", 13); a.CmpI("r1", 1); a.Bcc("eq", "c_run");
                a.CmpI("r1", 0); a.Bcc("ne", "c_end");
                a.Mov32("r2", MS_COUNTER); a.Ldr("r2", "r2", 0); a.Movw("r3", cal.windowMs);
                a.CmpR("r2", "r3"); a.Bcc("cc", "c_win");
                a.Movs("r1", 3); a.Strb("r1", "r5", 13); a.B("c_end");
                a.Label("c_win");
                a.Movw("r2", cal.enterP); a.CmpR("r0", "r2"); a.Bcc("cs", "c_hi");
                a.Movs("r1", 0); a.Strb("r1", "r5", 15); a.B("c_end");
                a.Label("c_hi");
                a.Ldrb("r2", "r6", 7); a.CmpI("r2", 255); a.Bcc("eq", "c_end");
                a.Ldrb("r2", "r5", 15); a.AddsI("r2", 1); a.Strb("r2", "r5", 15);
                a.CmpI("r2", cal.hold); a.Bcc("cc", "c_end");
                a.Movs("r1", 1); a.Strb("r1", "r5", 13);
                a.Movs("r1", 0); a.Strb("r1", "r5", 14); a.Strh("r1", "r5", 16); a.Str("r1", "r5", 24);
                a.Movs("r1", (-S_) & 0xFF); a.Strb("r1", "r5", 12); a.Movs("r1", 0); a.Strb("r1", "r5", 23);
                a.Ldr("r1", "r6", 28); a.Strh("r1", "r5", 18); a.Ldr("r1", "r6", 32); a.Strh("r1", "r5", 20);
                a.B("c_end");
                a.Label("c_run");
                // LED: blinks at a fixed 2.5 Hz (200 ms on / 200 ms off, from the firmware's millisecond counter) the
                // whole time calibration runs - also while it waits for the pen - so the rate never depends on frames
                a.Mov32("r2", MS_COUNTER); a.Ldr("r2", "r2", 0); a.Movw("r3", 200); a.Udiv("r2", "r2", "r3");
                a.Movs("r3", 1); a.Ands("r2", "r3"); a.Strb("r2", "r5", 23);
                a.Push(Rg("r0", "r1"), false);
                a.Mov32("r0", 0x48000800); a.Movw("r1", 0x2000);
                a.Cbz("r2", "c_ledon"); a.Str("r1", "r0", 24); a.B("c_leddn");
                a.Label("c_ledon"); a.Str("r1", "r0", 40);
                a.Label("c_leddn"); a.Pop(Rg("r0", "r1"), false);
                a.Movw("r2", cal.nearP); a.CmpR("r0", "r2"); a.Bcc("cs", "c_wait");
                a.Ldrb("r2", "r6", 7); a.CmpI("r2", 255); a.Bcc("ne", "c_go");
                a.Label("c_wait"); a.Ldr("r1", "r6", 28); a.Strh("r1", "r5", 18); a.Ldr("r1", "r6", 32); a.Strh("r1", "r5", 20);
                a.Ldrb("r1", "r5", 22); a.Strb("r1", "r5", 12);
                a.B("c_end");
                a.Label("c_go");
                a.Ldrb("r1", "r5", 14); a.SubsI("r1", S_); a.Strb("r1", "r5", 12);
                a.Ldr("r0", "r6", 28); a.Ldrh("r1", "r5", 18); a.Strh("r0", "r5", 18); a.SubsR("r0", "r0", "r1");
                a.Bcc("pl", "c_ax"); a.Rsbs("r0");
                a.Label("c_ax"); a.Ldr("r2", "r6", 32); a.Ldrh("r1", "r5", 20); a.Strh("r2", "r5", 20);
                a.SubsR("r2", "r2", "r1"); a.Bcc("pl", "c_ay"); a.Rsbs("r2");
                a.Label("c_ay"); a.AddsR("r0", "r0", "r2");
                a.Ldrh("r1", "r5", 16); a.AddsI("r1", 1); a.Strh("r1", "r5", 16);
                a.Movw("r3", K_); a.CmpR("r1", "r3"); a.Bcc("ls", "c_end");
                a.Ldr("r2", "r5", 24); a.AddsR("r2", "r2", "r0"); a.Str("r2", "r5", 24);
                a.Movw("r3", K_ + N_); a.CmpR("r1", "r3"); a.Bcc("cc", "c_end");
                a.Ldrb("r3", "r5", 14); a.Lsls("r1", "r3", 2); a.AddsR("r1", "r1", "r5"); a.Str("r2", "r1", 28);
                a.Movs("r2", 0); a.Str("r2", "r5", 24); a.Strh("r2", "r5", 16);
                a.AddsI("r3", 1); a.Strb("r3", "r5", 14);
                a.CmpI("r3", 2 * S_ + 1); a.Bcc("cs", "c_pick");
                a.SubsI("r3", S_); a.Strb("r3", "r5", 12); a.B("c_end");
                a.Label("c_pick"); a.Bl("cpick");
                a.Label("c_end"); a.Pop(Rg("r5", "r6", "r7"), false);
            }
            if (cfg.lab) a.Bl("labchain");      // Filter Arranger build (atf_lab.cs): the order comes from RAM
            else
            {
                // R45-rc4 Switch Match (atf_switchmatch.cs): before Jump Rejection and ALPI, so both see a position with no
                // step at a coil-window, drive-step or ladder-step switch
                if (cfg.switchMatch) a.Bl("swm");
                if (odd != null) EmitJumpRejection(a, odd);
                if (cfg.aggressive) EmitAggressive(a);
                // R45-rc5 Coil Linearization (default on since rc5 (1)): memoryless. Since rc5 (2) it runs BEFORE ALPI: ALPI
                // follows a straight move with no delay but the coil wave about 5-6 ms late, so a correction after it was
                // applied at the wrong place for a moving pen (forward and back passes differed; offline replay
                // referencedata/tools/lagsim.cs: 100 mm/s forward-minus-back 0.08 -> 0.01 mm rms)
                if (cfg.coilLin) a.Bl("clin");
                // R44 APIP: the frame's final position (after Jump Rejection) through the trained filter; the training
                // build records it instead (atf_apip.cs)
                if (cfg.apip != null) a.Bl("apfilt");
                // R45-rc5 Dynamic Anti-Chatter: the last stage, on the final position
                if (cfg.antiChatter) a.Bl("dachat");
            }
            if (cfg.apipTrain) Apip.EmitRecorder(a, boost);
            // plan the next frame
            a.Ldrb("r0", "r4", 0); a.AddsI("r0", 1); a.Strb("r0", "r4", 0);
            a.Movs("r3", tiltEvery - 1); a.Ands("r3", "r0"); a.Movs("r2", 0);
            a.CmpI("r3", phases.Length); a.Bcc("cs", "r_ty");
            if (cfg.tiltSkip)
            {
                // R45-rc3 Tilt Frame Skip (Anargon's low hover shake, rev-output/hw45/noise/lines-hover.csv): a tilt
                // frame (the stock tilt scan, mode 5) moves the position at hover in the fast stage: at ULTRA Y +0.21 /
                // +0.44 / -0.20 mm on three of the four, X -0.17 mm on the first; 66 % of the worst one's positions were
                // over 0.5 mm off, and each one opened ALPI's still gains. Hovering in the fast stage, those frames are
                // planned as normal frames instead (tilt keeps its last value; the tilt routine runs only on the last
                // tilt frame of a cycle). Touching, Steady Hover and reach keep them (Steady Hover, lift recording:
                // every phase within 0.03 mm). Carrying ALPI on along its own path on those frames was tried first: in
                // the twin a still pen shook 0.034 mm instead of 0.004 and QUICK's hover delay went from 2 to 23 ms.
                a.Mov32("r1", VB_ADDR); a.Ldrb("r1", "r1", 0); a.Cbnz("r1", "r_tt");
                a.Mov32("r1", PRESSURE); a.Ldrh("r1", "r1", 6); a.Lsrs("r1", "r1", 2); a.Cbz("r1", "r_ty");
                a.Label("r_tt");
            }
            a.Mov32("r1", tab + 96); a.LdrbR("r2", "r1", "r3");
            a.Label("r_ty"); a.Strb("r2", "r4", 2);
            a.Movs("r1", 0); a.Strb("r1", "r4", 1);
            if (!lv.sweepOnTilt)
            {
                a.Cbz("r2", "sweep");
                a.Mov32("r0", XBUF); a.Movs("r1", 120); a.BlA(Fw.A(0x08003A12));
                a.Ldrb("r2", "r4", 2); a.Movs("r1", 1); a.Ands("r2", "r1"); a.Eors("r2", "r1");
                a.Mov32("r0", C_ADDR); a.Strb("r2", "r0", 1);
                a.SubsI("r0", 32); a.Movs("r1", 5); a.Strb("r1", "r0", 0);
                a.B("arm");
            }
            a.Label("sweep");
            int half = sweepLen == 6 ? 2 : 1;
            a.Mov32("r0", C_ADDR); a.Ldrb("r2", "r4", 3); a.Strb("r2", "r0", 1); a.Cbnz("r2", "seta");
            a.Mov32("r3", VQ_ADDR); a.Strb("r2", "r3", 0);           // a new sweep: no stage change yet (r2 = 0)
            a.Ldrb("r1", "r0", 3); a.CmpI("r1", half); a.Bcc("ls", "zero4");
            a.SubsI("r1", half); a.Strb("r1", "r0", 4); a.B("seta");
            a.Label("zero4"); a.Strb("r2", "r0", 4);
            a.Label("seta"); a.SubsI("r0", 32); a.Movs("r1", 4); a.Strb("r1", "r0", 0);
            a.Label("arm");
            a.Mov32("r0", S_ADDR); a.Movs("r1", 0); a.Strb("r1", "r0", 7);
            // the next frame's start: the last one's (V+10) + the period (VB+4), in USB frames (11-bit frame number)
            // or TIMER16 counts (us); now if that has passed. R45-rc4: a build mixing both reads VBX+15 per frame.
            if (hyb) { a.Mov32("r0", VBX_ADDR); a.Ldrb("r0", "r0", 15); a.Cbnz("r0", "arm_t"); }
            if (sof)
            {
                a.Mov32("r2", DSTAT); a.Ldr("r2", "r2", 0); a.Lsls("r2", "r2", 13); a.Lsrs("r2", "r2", 21);
                a.Ldrh("r1", "r4", 10); a.Mov32("r3", VB_ADDR); a.Ldrh("r3", "r3", 4); a.AddsR("r1", "r1", "r3");
                a.Lsls("r1", "r1", 21); a.Lsrs("r1", "r1", 21);
                a.SubsR("r3", "r2", "r1"); a.Lsls("r3", "r3", 21); a.Bcc("mi", "keep");
            }
            if (hyb) { a.B("arm_now"); a.Label("arm_t"); }
            if (!sof || hyb)
            {
                a.Mov32("r2", T16 + 0x24); a.Ldr("r2", "r2", 0); a.Uxth("r2", "r2");
                a.Ldrh("r1", "r4", 10); a.Mov32("r3", VB_ADDR); a.Ldrh("r3", "r3", 4); a.AddsR("r1", "r1", "r3"); a.Uxth("r1", "r1");
                a.SubsR("r3", "r2", "r1"); a.Lsls("r3", "r3", 16); a.Bcc("mi", "keep");
            }
            if (hyb) a.Label("arm_now");
            a.Mov("r1", "r2");
            a.Label("keep"); a.Strh("r1", "r4", 8);
            a.Movs("r1", 0); a.Strh("r1", "r4", 12); a.Movs("r1", 1); a.Strb("r1", "r4", 4);
            if (hyb)
            {
                a.Mov32("r0", VBX_ADDR); a.Ldrb("r0", "r0", 15); a.Cbnz("r0", "arm_d");
                a.Strb("r1", "r4", 5); a.B("arm_e");
                a.Label("arm_d"); a.Mov32("r0", D_ADDR); a.Movs("r1", 2); a.Strb("r1", "r0", 6);
                a.Label("arm_e");
            }
            else if (sof) a.Strb("r1", "r4", 5);
            else { a.Mov32("r0", D_ADDR); a.Movs("r1", 2); a.Strb("r1", "r0", 6); }
            a.Pop(Rg("r4"), true);
            // ================================================================ step-timer gate (TIMER15 ISR)
            a.Label("gate");
            if (ledAll || ledSome)
            {
                // R45 LED fade (LED_FADE): error diffusion on every tick, about 0.3 us; nothing while the level is 0, or
                // (ledSome) in the fast stage, where the frame restart drives it
                a.Mov32("r2", LED_FADE); a.Ldrb("r1", "r2", 1); a.Cbz("r1", "g_led");
                if (ledSome && ledsteps != 0) { a.Mov32("r0", VBX_ADDR); a.Ldrb("r0", "r0", 8); a.Mov32("r3", ledsteps); a.LdrbR("r0", "r3", "r0"); a.Cbz("r0", "g_led"); }
                else if (ledSome) { a.Mov32("r0", VB_ADDR); a.Ldrb("r0", "r0", 0); a.Cbz("r0", "g_led"); }
                a.Ldrb("r0", "r2", 0); a.AddsR("r0", "r0", "r1"); a.Strb("r0", "r2", 0);
                a.Mov32("r2", 0x48000800); a.Movw("r1", 0x2000);
                a.CmpI("r0", 255); a.Bcc("hi", "g_led_on");
                a.Str("r1", "r2", 24); a.B("g_led");
                a.Label("g_led_on"); a.Str("r1", "r2", 40);
                a.Label("g_led");
            }
            a.Mov32("r0", S_ADDR); a.Ldrb("r0", "r0", 7); a.CmpI("r0", 0); a.BccW("ne", "g_ret");
            a.Mov32("r2", V_ADDR); a.Ldrb("r1", "r2", 4); a.CmpI("r1", 0); a.BccW("eq", "g_ret");
            if (hyb) { a.Mov32("r0", VBX_ADDR); a.Ldrb("r0", "r0", 15); a.Cbnz("r0", "g_t"); }
            if (sof)
            {
                a.Mov32("r3", DSTAT); a.Ldr("r3", "r3", 0); a.Lsls("r3", "r3", 13); a.Lsrs("r3", "r3", 21);
                a.Ldrh("r1", "r2", 8);
                a.Ldrb("r0", "r2", 5); a.Cbz("r0", "g_chk");
                a.SubsR("r0", "r3", "r1"); a.AddsI("r0", 1); a.Lsls("r0", "r0", 21); a.Bcc("mi", "g_chk");
                a.Movs("r0", 0); a.Strb("r0", "r2", 5); a.Mov32("r0", D_ADDR); a.Movs("r1", 2); a.Strb("r1", "r0", 6);
                a.Ldrh("r1", "r2", 8);
                a.Label("g_chk");
                a.SubsR("r1", "r3", "r1"); a.Lsls("r1", "r1", 21); a.Bcc("pl", "g_go");
            }
            if (hyb) { a.B("g_wait"); a.Label("g_t"); }
            if (!sof || hyb)
            {
                a.Mov32("r3", T16 + 0x24); a.Ldr("r3", "r3", 0); a.Uxth("r3", "r3");
                a.Ldrh("r1", "r2", 8);
                a.SubsR("r1", "r3", "r1"); a.Lsls("r1", "r1", 16); a.Bcc("pl", "g_go_t");
            }
            if (hyb) a.Label("g_wait");
            a.Ldrh("r1", "r2", 12); a.AddsI("r1", 1); a.Strh("r1", "r2", 12);
            a.Movw("r0", cfg.freeRunPolls); a.CmpR("r1", "r0"); a.Movs("r0", 0); a.Bcc("cc", "g_ret");
            if (hyb) { a.Mov32("r0", VBX_ADDR); a.Ldrb("r0", "r0", 15); a.Cbz("r0", "g_go"); }
            if (!sof || hyb) { a.Label("g_go_t"); a.Ldrh("r3", "r2", 8); }
            a.Label("g_go");
            a.Ldrb("r0", "r2", 5); a.Cbz("r0", "g_go2");
            a.Movs("r0", 0); a.Strb("r0", "r2", 5); a.Mov32("r0", D_ADDR); a.Movs("r1", 2); a.Strb("r1", "r0", 6);
            a.Label("g_go2");
            a.Strh("r3", "r2", 10); a.Movs("r1", 0); a.Strb("r1", "r2", 4);
            a.Mov32("r0", S_ADDR); a.Movs("r1", 1); a.Strb("r1", "r0", 7); a.Movs("r0", 1);
            a.Label("g_ret"); a.BxLr();

            // ================================================================ search restart (pen lost / boot)
            a.Label("search");
            a.Raw16(0xE92D); a.Raw16(0x41F0);
            a.Bl("ledsearch");                                 // calibration-pass build: LED steady while no pen is tracked
            // R45-rc2: SysTick off (the midpoint bursts). The vendor bootloader starts SysTick at 1 ms (clock / 1000) with
            // the highest priority (0) and jumps to the application with it still running; the application's handler is
            // empty and it never uses SysTick (its deep-sleep code saves and restores the state, which now stays off).
            // Every 1 ms of the tablet's own clock the interrupt cut into the scan for a moment, and the coil being read
            // then came out about 15 high. The frames follow the PC's USB clock, so that moment drifts through the frame
            // (a full pass every 1 / crystal offset: about 74 s on Anargon's tablet, 13.5 ppm), giving each coil a few
            // seconds of noise in turn; at a coil midpoint one such reading moves X or Y about 0.4 mm. Tablet A/B (silent
            // study build, 4 s blocks with the interrupt on / off, 240 s at a midpoint): with it on, bursts every 74 s of
            // up to 0.195 mm frame to frame; with it off none (worst block 0.075 mm, the quiet level). Runs at boot (the
            // firmware calls this routine before its main loop) and on every pen loss; no cost per frame.
            if (cfg.systickOff) { a.Mov32("r1", SYST_CSR); a.Movs("r0", 0); a.Str("r0", "r1", 0); }   // R45-rc3: setting "Bootloader Timer Off"
            a.Mov32("r1", VB_ADDR); a.Ldr("r0", "r1", 60); a.Mov32("r2", COLD_MARK); a.CmpR("r0", "r2");
            a.Bcc("eq", "cs_warm");
            a.Str("r2", "r1", 60); a.Movs("r0", 0); a.Strb("r0", "r1", 22); a.Strb("r0", "r1", 13);
            a.Mov32("r1", VBX_ADDR);
            for (int off = 0; off < 32; off += 4) a.Str("r0", "r1", off);
            if (boost) { a.Mov32("r1", VL_ADDR); for (int off = 0; off < VL_BYTES; off += 4) a.Str("r0", "r1", off); }
            if (cfg.cleanup) a.Bl("blclean");                           // return to stock: bootloader area back to stock
            if (cfg.blCounter) a.Bl("blinst");                          // bootloader counter: install / repair
            if (cfg.counter) a.Bl("fcount");                            // cold boot: flash counter
            a.Label("cs_warm");
            a.Mov32("r2", C_ADDR); a.Mov32("r3", VS_ADDR);
            a.Ldrb("r0", "r2", 3); a.Cbz("r0", "sr_fail");
            a.Strb("r0", "r3", 1); a.Ldrb("r0", "r2", 6); a.Strb("r0", "r3", 2); a.Movs("r0", 0); a.Strb("r0", "r3", 0);
            a.B("sr_dec");
            a.Label("sr_fail"); a.Ldrb("r0", "r3", 0); a.CmpI("r0", 255); a.Bcc("eq", "sr_dec");
            a.AddsI("r0", 1); a.Strb("r0", "r3", 0);
            a.Label("sr_dec"); a.Movs("r1", 0);
            if (longS)
            {
                a.Ldrb("r0", "r3", 0); a.CmpI("r0", LONG_AFTER); a.Bcc("cc", "sr_set");
                a.Lsls("r2", "r0", 30); a.Lsrs("r2", "r2", 30); a.CmpI("r2", 3); a.Bcc("eq", "sr_set");
                a.Ldrb("r2", "r3", 1); a.Cbz("r2", "sr_set");
                a.Movs("r1", 1);
            }
            a.Label("sr_set"); a.Strb("r1", "r3", 3);
            a.Mov32("r2", VP_ADDR); a.Mov32("r3", tab + 48);
            if (longS)
            {
                a.Cbz("r1", "sr_norm");
                a.AddsI("r3", 16);
                a.Label("sr_norm");
            }
            for (int k = 0; k < 3; k++) { a.Ldrb("r0", "r3", 2 + k); a.Strb("r0", "r2", 6 + k); }
            a.Label("sr_pd");
            a.Mov32("r1", V_ADDR); a.Movs("r0", 0);
            a.Strb("r0", "r1", 1); a.Strb("r0", "r1", 3); a.Strb("r0", "r1", 4); a.Strb("r0", "r1", 5);
            a.Strh("r0", "r1", 10); a.Movs("r0", 0xF); a.Strb("r0", "r1", 2);
            a.Movs("r0", tiltEvery - 1); a.Strb("r0", "r1", 0);
            // Frequency Hold: no held step until the first full sweep of the next pen (also clears power-up RAM)
            if (cfg.freqHold) { a.Mov32("r1", HOLD_ADDR); a.Movs("r0", 255); a.Strb("r0", "r1", 0); }
            if (cfg.switchMatch) SwitchMatch.EmitReset(a);      // R45-rc4: no history for the next pen
            if (cfg.antiChatter) DynamicAntiChatter.EmitReset(a);      // R45-rc5: likewise
            if (cfg.lab) Lab.EmitReset(a);                             // Filter Arranger: a requested order starts here
            // Where a newly-acquired pen starts. At 800/1000 Hz (holdHover) the fast stage is too weak to hold a hover,
            // so a found pen is handed to Hover Boost (strong, half-rate): a touching or close pen steps straight down
            // to the fast stage (touch / the strong-signal step-up above), a weak hover stays boosted and is held. At
            // the lower rates the fast stage holds a hover, so a found pen starts there, exactly as before. Either way
            // a pen found from far away (long-range pass) starts at the top stage.
            // R45-rc4: a found pen stays at the step it starts at (Cfg.ladAcq, Steady Hover's pulses) until one frequency
            // sweep has finished whole there (VBX+12, cleared by "fin"). The ladder would climb to the fast stage within
            // about 50 ms, every sweep on the way dropped as mixed, so the first frequency came from the fast stage's
            // short readings (twin, RAPID, Q 150 pen 3 kHz below where pressure starts, holds on: every report pressed).
            if (boost) { a.Mov32("r1", VBX_ADDR); a.Movs("r0", 1); a.Strb("r0", "r1", 12); }
            if (hyb)
            {
                // R45-rc4: TIMER16 runs before the first step is loaded (setlad reads it), and an unknown pacing makes
                // setlad start the next frame at once in the step's own clock
                EmitT16Init(a);
                a.Mov32("r1", VBX_ADDR); a.Movs("r0", 255); a.Strb("r0", "r1", 15);
            }
            a.Movs("r0", 0);
            if (boost && cfg.holdHover) a.Movs("r0", 1);
            if (longS && boost) { a.Mov32("r1", VS_ADDR); a.Ldrb("r1", "r1", 3); a.Cbz("r1", "sr_m0"); a.Movs("r0", 2); a.Label("sr_m0"); }
            a.Bl("setmode");
            if (odd != null)
            {
                a.Mov32("r1", VC_ADDR); a.Movs("r0", 0); a.Strb("r0", "r1", 10); a.Strb("r0", "r1", 11);
                a.Ldrh("r0", "r1", 8); a.CmpI("r0", 200); a.Bcc("ls", "od_k"); a.Movs("r0", 20); a.Strh("r0", "r1", 8);
                a.Label("od_k");
            }
            if (cal != null)
            {
                a.Mov32("r1", VB_ADDR);
                a.Ldrb("r0", "r1", 13); a.CmpI("r0", 1); a.Bcc("ne", "cs_nc");
                a.Ldrb("r3", "r1", 14); a.Cbz("r3", "cs_ab");
                a.Mov("r5", "r1"); a.Bl("cpick"); a.Mov32("r1", VB_ADDR); a.B("cs_nc");
                a.Label("cs_ab"); a.Movs("r0", 0); a.Strb("r0", "r1", 13);
                a.Label("cs_nc");
                a.Ldrb("r0", "r1", 22); a.Strb("r0", "r1", 12); a.Movs("r0", 0); a.Strb("r0", "r1", 15);
            }
            if (!sof)
            {
                EmitT16Init(a);
                a.Mov32("r0", T16); a.Ldr("r1", "r0", 0x24); a.Mov32("r0", V_ADDR); a.Strh("r1", "r0", 10);
            }
            a.BWA(Fw.A(0x08006F54));
            if (cfg.multiCoil)
                MultiCoil.Emit(a, mctab, Fw.A(0x08005432), XBUF, YBUF, S_ADDR, AGC, cfg.fullRes);
            if (wstab != 0)
                WindowScale.Emit(a, wstab, Fw.A(0x08005432), cfg.lab, cgtab, cfg.fullRes);
            // ---- small helpers
            a.Label("setmode");
            if (boost)
            {
                // R45-rc4: a stage (0 fast, 1 Steady Hover's fastest step, 2 the slowest step: reach, or Steady Hover's
                // slowest without it) -> its ladder step; setlad(r0 = step) loads it and starts its counts over
                // (stage 1 = the step a found pen starts at, Cfg.ladAcq)
                a.CmpI("r0", 2); a.Bcc("ne", "sm_l"); a.Movs("r0", nl - 1); a.B("setlad");
                a.Label("sm_l"); a.CmpI("r0", 1); a.Bcc("ne", "setlad"); a.Movs("r0", cfg.ladAcq);
                a.Label("setlad");
                // R41: another read length while a sweep is paused at a chunk boundary (V[3] = its next step) marks
                // that sweep as mixed; fin drops it
                a.Mov32("r1", VBX_ADDR); a.Ldrb("r2", "r1", 8); a.CmpR("r2", "r0"); a.Bcc("eq", "sl_same");
                a.Mov32("r2", V_ADDR); a.Ldrb("r2", "r2", 3); a.Cbz("r2", "sl_same");
                a.Mov32("r2", VQ_ADDR); a.Movs("r3", 1); a.Strb("r3", "r2", 0);
                a.Label("sl_same");
                a.Strb("r0", "r1", 8); a.Movs("r2", 0); a.Strb("r2", "r1", 9); a.Strb("r2", "r1", 14);
                a.Mov32("r1", VB_ADDR); a.Strb("r2", "r1", 6);
                a.Mov32("r2", ltab + LadStOff(cfg)); a.LdrbR("r2", "r2", "r0"); a.Strb("r2", "r1", 0);
                a.Mov32("r2", ltab); a.Lsls("r3", "r0", 4); a.AddsR("r2", "r2", "r3");
                a.Ldrh("r3", "r2", 0); a.Strh("r3", "r1", 4);
                a.Mov32("r1", VP_ADDR);
                for (int k = 0; k < 6; k++) { a.Ldrb("r3", "r2", 2 + k); a.Strb("r3", "r1", k); }
                for (int k = 0; k < 4; k++) { a.Ldrh("r3", "r2", 8 + 2 * k); a.Strh("r3", "r1", 10 + 2 * k); }
                if (hyb)
                {
                    // R45-rc4: a step in the other clock (USB frames / TIMER16): the next frame starts at once in the
                    // new one (V+10 = now - period, so "arm" finds its start passed)
                    a.Mov32("r2", ltab + LadPmOff(cfg)); a.LdrbR("r2", "r2", "r0");
                    a.Mov32("r1", VBX_ADDR); a.Ldrb("r3", "r1", 15); a.CmpR("r2", "r3"); a.Bcc("eq", "sl_pm");
                    a.Strb("r2", "r1", 15);
                    a.Mov32("r3", VB_ADDR); a.Ldrh("r3", "r3", 4);
                    a.Cbz("r2", "sl_sof");
                    a.Mov32("r1", T16 + 0x24); a.Ldr("r1", "r1", 0); a.B("sl_v");
                    a.Label("sl_sof"); a.Mov32("r1", DSTAT); a.Ldr("r1", "r1", 0); a.Lsls("r1", "r1", 13); a.Lsrs("r1", "r1", 21);
                    a.Label("sl_v"); a.SubsR("r1", "r1", "r3"); a.Mov32("r2", V_ADDR); a.Strh("r1", "r2", 10);
                    a.Label("sl_pm");
                }
                a.BxLr();
            }
            else
            {
                a.Mov32("r1", VB_ADDR);
                // R41: a different stage while a sweep is paused at a chunk boundary (V[3] = its next step) marks that
                // sweep as mixed; fin drops it
                a.Ldrb("r2", "r1", 0); a.CmpR("r2", "r0"); a.Bcc("eq", "sm_same");
                a.Mov32("r2", V_ADDR); a.Ldrb("r2", "r2", 3); a.Cbz("r2", "sm_same");
                a.Mov32("r2", VQ_ADDR); a.Movs("r3", 1); a.Strb("r3", "r2", 0);
                a.Label("sm_same");
                a.Strb("r0", "r1", 0);
                a.Mov32("r2", tab); a.Lsls("r3", "r0", 4); a.AddsR("r2", "r2", "r3");
                a.Ldrh("r3", "r2", 0); a.Strh("r3", "r1", 4);
                a.Mov32("r1", VP_ADDR);
                for (int k = 0; k < 6; k++) { a.Ldrb("r3", "r2", 2 + k); a.Strb("r3", "r1", k); }
                for (int k = 0; k < 4; k++) { a.Ldrh("r3", "r2", 8 + 2 * k); a.Strh("r3", "r1", 10 + 2 * k); }
                a.BxLr();
            }
            // pen pick-up / drop thresholds of the running stage (the stock code compares with 300 / 200, tuned for
            // 45-sample readings; the receiver integrates, so shorter readings need proportionally lower ones)
            a.Label("thr_sw");                                          // sweep: flags = r7 vs pick-up, ip = drop
            a.Push(Rg("r0"), false); a.Mov32("r0", VP_ADDR); a.Ldrh("r0", "r0", 16); a.Mov("ip", "r0");
            a.Mov32("r0", VP_ADDR); a.Ldrh("r0", "r0", 14); a.CmpR("r7", "r0"); a.Pop(Rg("r0"), false); a.BxLr();
            a.Label("thr_x");                                           // X scan: flags = r2 vs pick-up, ip = drop
            a.Push(Rg("r0"), false); a.Mov32("r0", VP_ADDR); a.Ldrh("r0", "r0", 12); a.Mov("ip", "r0");
            a.Mov32("r0", VP_ADDR); a.Ldrh("r0", "r0", 10); a.CmpR("r2", "r0"); a.Pop(Rg("r0"), false); a.BxLr();
            a.Label("thr_y");                                           // Y scan: r2 = last sample, flags = r2 vs drop
            a.Ldrh("r2", "r6", 8); a.Push(Rg("r0"), false); a.Mov32("r0", VP_ADDR); a.Ldrh("r0", "r0", 12);
            a.CmpR("r2", "r0"); a.Pop(Rg("r0"), false); a.BxLr();

            a.Label("bsel");
            a.Ldrb("r3", "r1", 0); a.Push(Rg("r0"), false);
            a.Mov32("r0", VP_ADDR);
            a.CmpI("r3", 4); a.Bcc("ne", "bs_1"); a.AddsI("r0", 3); a.B("bs_go");
            a.Label("bs_1"); a.CmpI("r3", 3); a.Bcc("hi", "bs_go"); a.AddsI("r0", 6);
            a.Label("bs_go"); a.Ldrb("r2", "r0", 2); a.Strb("r2", "r1", 5); a.Ldrb("r2", "r0", 0);
            a.Pop(Rg("r0"), false); a.BxLr();
            a.Label("tsel");
            a.Mov32("r1", S_ADDR); a.Ldrb("r2", "r1", 0); a.Mov32("r1", VP_ADDR);
            a.CmpI("r2", 4); a.Bcc("ne", "ts_1"); a.Ldrb("r0", "r1", 4); a.B("ts_w");
            a.Label("ts_1"); a.CmpI("r2", 3); a.Bcc("hi", "ts_2"); a.Ldrb("r0", "r1", 7); a.B("ts_w");
            a.Label("ts_2"); a.Ldrb("r0", "r1", 1);
            a.Label("ts_w"); a.Str("r0", "r4", 0); a.BxLr();
            // LED (PC13, active-low). Stock behaviour is kept (the firmware lights it while the pen is in range), except:
            //   - while Startup Calibration runs: a fixed 2.5 Hz blink (200 ms on / 200 ms off, from the firmware's
            //     millisecond counter), so the rate never depends on frames;
            //   - in the calibration-pass build (R44) and the ALPI training build: the LED zone byte (ZONE_ADDR + 1, set
            //     from each pen report by tsend, or each frame by the training recorder) decides: off (no pen, or the
            //     pen outside the step's target box), on, or (R45) the fade while calibrating (LED_FADE). "ledtick"
            //     applies it every frame, "ledsearch" (search restart: no pen) clears it.
            // Keeps r0-r3.
            a.Label("ledsearch");
            a.Push(new string[] { "r0", "r1", "r2", "r3", "r4" }, true);
            a.Mov32("r0", 0x48000800); a.Movw("r1", 0x2000);
            if (zled)
            {
                a.Mov32("r2", ZONE_ADDR); a.Movs("r3", 0); a.Strb("r3", "r2", 1);
                a.Mov32("r2", LED_FADE); a.Strb("r3", "r2", 1); a.B("lt_off");
            }
            else a.B("lt_body");
            a.Label("ledtick");
            a.Push(new string[] { "r0", "r1", "r2", "r3", "r4" }, true);
            a.Mov32("r0", 0x48000800); a.Movw("r1", 0x2000);
            a.Label("lt_body");
            if (zled)
            {
                // R45: off, on, or the fade (LED_FADE: the level from LedTable, error diffusion here or in the step timer)
                a.Mov32("r2", ZONE_ADDR); a.Ldrb("r3", "r2", 1);
                a.Mov32("r2", LED_FADE); a.CmpI("r3", 2); a.Bcc("eq", "lt_fade");
                a.Movs("r4", 0); a.Strb("r4", "r2", 1);
                a.CmpI("r3", 0); a.Bcc("ne", "lt_on");
                a.Label("lt_off"); a.Str("r1", "r0", 24); a.B("lt_done");
                // level = LedTable[(ms mod LED_FADE_MS) / LED_SLOT_MS]
                a.Label("lt_fade");
                a.Mov32("r3", MS_COUNTER); a.Ldr("r3", "r3", 0); a.Movw("r4", LED_FADE_MS);
                a.Udiv("r1", "r3", "r4"); a.Mls("r3", "r1", "r4", "r3");
                a.Movs("r4", LED_SLOT_MS); a.Udiv("r3", "r3", "r4");
                a.Mov32("r4", ledtab); a.LdrbR("r3", "r4", "r3");
                a.Strb("r3", "r2", 1);
                if (ledAll) a.B("lt_done");
                else
                {
                    // per frame: in every stage, or (ledSome) only in the fast stage (VB[0] = 0) / the ladder steps whose
                    // table byte is 0 (r1 is set again below)
                    if (ledSome && ledsteps != 0) { a.Mov32("r4", VBX_ADDR); a.Ldrb("r4", "r4", 8); a.Mov32("r1", ledsteps); a.LdrbR("r4", "r1", "r4"); a.CmpI("r4", 0); a.Bcc("ne", "lt_done"); }
                    else if (ledSome) { a.Mov32("r4", VB_ADDR); a.Ldrb("r4", "r4", 0); a.CmpI("r4", 0); a.Bcc("ne", "lt_done"); }
                    a.Ldrb("r4", "r2", 0); a.AddsR("r4", "r4", "r3"); a.Strb("r4", "r2", 0);
                    a.Movw("r1", 0x2000); a.CmpI("r4", 255); a.Bcc("hi", "lt_on"); a.B("lt_off");
                }
            }
            else if (cal != null)
            {
                a.Mov32("r2", VB_ADDR); a.Ldrb("r3", "r2", 13); a.CmpI("r3", 1); a.Bcc("ne", "lt_done");
                a.Mov32("r2", MS_COUNTER); a.Ldr("r2", "r2", 0); a.Movw("r3", 200); a.Udiv("r2", "r2", "r3");
                a.Lsls("r2", "r2", 31); a.Bcc("eq", "lt_on");
                a.Str("r1", "r0", 24); a.B("lt_done");
            }
            else a.B("lt_done");
            a.Label("lt_on"); a.Str("r1", "r0", 40);
            a.Label("lt_done"); a.Pop(new string[] { "r0", "r1", "r2", "r3", "r4" }, true);
            if (cal != null || zled)
            {
                // LED (PC13) gate, called where the firmware lights the LED (and, in the calibration-pass build, also
                // where it switches it off): on, except in the "off" half of the 2.5 Hz blink while Startup Calibration
                // runs - or, in the calibration-pass build, whatever the LED zone byte says (as ledtick)
                a.Label("ledgate");
                if (zled)
                {
                    // R45: while fading the PWM drives the LED, so the firmware's own call changes nothing
                    a.Mov32("r2", ZONE_ADDR); a.Ldrb("r3", "r2", 1); a.Cbz("r3", "lg_off"); a.CmpI("r3", 2); a.Bcc("ne", "lg_on");
                    a.BxLr();
                }
                else
                {
                    a.Mov32("r2", VB_ADDR); a.Ldrb("r3", "r2", 13); a.CmpI("r3", 1); a.Bcc("ne", "lg_on");
                    a.Mov32("r2", MS_COUNTER); a.Ldr("r2", "r2", 0); a.Movw("r3", 200); a.Udiv("r2", "r2", "r3");
                    a.Lsls("r2", "r2", 31); a.Bcc("eq", "lg_on");
                }
                a.Label("lg_off"); a.BWA(Fw.A(0x080084C8));
                a.Label("lg_on"); a.BWA(Fw.A(0x080084C4));
            }
            if (cal != null)
            {
                int S_ = cal.span;
                a.Label("cpick");
                a.Push(Rg("r4"), true); a.Mov("r4", "r3");
                a.Movs("r3", 0); a.Ldr("r2", "r5", 28); a.Movs("r1", 1);
                a.Label("pk_l"); a.CmpR("r1", "r4"); a.Bcc("cs", "pk_d");
                a.Lsls("r0", "r1", 2); a.AddsR("r0", "r0", "r5"); a.Ldr("r0", "r0", 28);
                a.CmpR("r0", "r2"); a.Bcc("cs", "pk_n"); a.Mov("r2", "r0"); a.Mov("r3", "r1");
                a.Label("pk_n"); a.AddsI("r1", 1); a.B("pk_l");
                a.Label("pk_d"); a.SubsI("r3", S_); a.Strb("r3", "r5", 12); a.Strb("r3", "r5", 22);
                a.Movs("r1", 2); a.Strb("r1", "r5", 13); a.Pop(Rg("r4"), true);
            }
            if (cal != null || cfg.freqHold)
            {
                // the X / Y scans' drive step (r0 = C, r1 = the frequency table, kept; r2 = the step out; r3 free):
                // C[3], or with Frequency Hold the held step D while it is within 2 steps of C[3]; then the
                // calibrated offset when Pen frequency tuning is on
                a.Label("fsel");
                a.Strb("r6", "r7", 0); a.Ldrb("r2", "r0", 3);
                if (cfg.freqHold)
                {
                    a.Mov32("r3", HOLD_ADDR); a.Ldrb("r3", "r3", 0);
                    a.SubsR("r3", "r3", "r2"); a.AddsI("r3", 2); a.CmpI("r3", 4); a.Bcc("hi", "f_base");
                    a.Mov32("r3", HOLD_ADDR); a.Ldrb("r2", "r3", 0);
                    a.Label("f_base");
                }
                if (cal != null)
                {
                    a.Mov32("r3", VB_ADDR); a.Ldrb("r3", "r3", 12); a.Sxtb("r3", "r3");
                    a.AddsR("r2", "r2", "r3"); a.Bcc("mi", "f_lo"); a.CmpI("r2", 39); a.Bcc("ls", "f_ok"); a.Movs("r2", 39); a.B("f_ok");
                    a.Label("f_lo"); a.Movs("r2", 0);
                    a.Label("f_ok");
                }
                a.BxLr();
            }
            if (cfg.telemetry)
            {
                // calibration pass: the pen report (report ID 7) carries the firmware's own readings instead.
                // [1] = 0xC0 (out of range, so drivers ignore it), [9] = 0xE0 | page:
                //   0    signal:    [2] strongest X line last sample, [4] strongest Y, [6] level (sum / samples), [8] AGC X
                //   1    position:  [2] x, [4] y, [6] pressure (as built), [8] the real status byte
                //   2    frequency: [2] resonance / 16, [4] C[3], [5] AGC Y, [6] AGC sweep, [7] calibration offset, [8] stage
                //   3    counter:   [2..5] flash count, [6] write status, [7] counter enabled
                a.Label("tsend");
                a.Push(new string[] { "r0", "r1", "r2", "r4", "r5", "r6", "r7" }, true); a.Mov32("r4", V_ADDR);
                // 8-frame rotation: even frames signal, 1 and 5 position, 3 frequency, 7 flash counter
                a.Mov32("r5", REPORT);
                // R44 LED zone: the report still holds the firmware's own status and position here (the pages below
                // replace them). Off unless in range (status bit 5); with a valid state and a target, off outside the
                // target's box; blinking while recording.
                a.Mov32("r7", ZONE_ADDR); a.Movs("r6", 0);
                a.Ldrb("r0", "r5", 1); a.Lsls("r0", "r0", 26); a.Bcc("pl", "z_set");
                a.Movs("r6", 1);
                a.Ldrb("r0", "r7", 0); a.Ldrb("r1", "r7", 2); a.Eors("r1", "r0"); a.CmpI("r1", ZONE_CHECK); a.Bcc("ne", "z_set");
                a.Lsrs("r1", "r0", 1); a.Cbz("r1", "z_rec");
                a.Ldrh("r1", "r5", 2); a.Movw("r2", ZONE_X - ZONE_HALF); a.SubsR("r1", "r1", "r2");
                a.Movw("r2", 2 * ZONE_HALF); a.CmpR("r1", "r2"); a.Bcc("hi", "z_out");
                a.Ldrh("r1", "r5", 4); a.Movw("r2", ZONE_Y - ZONE_HALF); a.SubsR("r1", "r1", "r2");
                a.Movw("r2", 2 * ZONE_HALF); a.CmpR("r1", "r2"); a.Bcc("ls", "z_rec");
                a.Label("z_out"); a.Movs("r6", 0); a.B("z_set");
                a.Label("z_rec"); a.Lsls("r0", "r0", 31); a.Bcc("eq", "z_set"); a.Movs("r6", 2);
                a.Label("z_set"); a.Strb("r6", "r7", 1);
                if (cfg.lineRec)
                {
                    // R42 line recorder: while a copy is pending, each even frame's report carries its next page (0xF0 | page)
                    a.Mov32("r7", LR_ADDR); a.Ldrb("r6", "r7", 0); a.CmpI("r6", 5); a.Bcc("ls", "tr_ok");
                    a.Movs("r6", 0); a.Strb("r6", "r7", 0);                                   // power-up RAM: nothing pending
                    a.Label("tr_ok"); a.CmpI("r6", 0); a.Bcc("eq", "tr_no");
                    a.Ldrb("r0", "r4", 0); a.Lsrs("r0", "r0", 1); a.Bcc("cs", "tr_no");      // odd frames: position, frequency, counter as usual
                    a.SubsI("r6", 1); a.Strb("r6", "r7", 0); a.Movs("r0", 4); a.SubsR("r6", "r0", "r6");   // page 0-4
                    a.CmpI("r6", 4); a.Bcc("ne", "tr_cp");
                    a.Mov("r1", "r7"); a.AddsI("r1", 32); a.Ldrh("r0", "r5", 6); a.Strh("r0", "r1", 8); a.Ldrb("r0", "r5", 1); a.Strb("r0", "r1", 10);
                    a.Label("tr_cp"); a.Lsls("r1", "r6", 3); a.AddsR("r1", "r1", "r7"); a.AddsI("r1", 8);
                    a.Mov("r2", "r5"); a.AddsI("r2", 2); a.Movs("r3", 7);
                    a.Label("tr_l"); a.Ldrb("r0", "r1", 0); a.Strb("r0", "r2", 0); a.AddsI("r1", 1); a.AddsI("r2", 1); a.SubsI("r3", 1); a.Bcc("ne", "tr_l");
                    a.Movs("r0", 0xF0); a.Orrs("r0", "r6"); a.Strb("r0", "r5", 9); a.Movs("r0", 0xC0); a.Strb("r0", "r5", 1);
                    a.B("tl_out");
                    a.Label("tr_no");
                }
                a.Ldrb("r6", "r4", 0); a.Movs("r0", 7); a.Ands("r6", "r0");
                a.Lsrs("r0", "r6", 1); a.Bcc("cc", "tl_0");
                a.CmpI("r6", 3); a.Bcc("eq", "tl_2"); a.CmpI("r6", 7); a.Bcc("eq", "tl_3");
                a.Movs("r6", 1); a.Ldrb("r0", "r5", 1); a.Strb("r0", "r5", 8); a.B("tl_end");
                a.Label("tl_2"); a.Movs("r6", 2);
                a.Mov32("r0", RESONANCE); a.Ldr("r0", "r0", 0); a.Lsrs("r0", "r0", 4); a.Strh("r0", "r5", 2);
                a.Mov32("r0", C_ADDR); a.Ldrb("r0", "r0", 3); a.Strb("r0", "r5", 4);
                a.Mov32("r1", AGC); a.Ldrb("r0", "r1", 2); a.Strb("r0", "r5", 5); a.Ldrb("r0", "r1", 3); a.Strb("r0", "r5", 6);
                a.Mov32("r1", VB_ADDR); a.Ldrb("r0", "r1", 12); a.Strb("r0", "r5", 7); a.Ldrb("r0", "r1", 0); a.Strb("r0", "r5", 8);
                a.B("tl_end");
                a.Label("tl_3"); a.Movs("r6", 3);
                a.Mov32("r1", VS_ADDR); a.Ldr("r0", "r1", 8); a.Strh("r0", "r5", 2); a.Lsrs("r0", "r0", 16); a.Strh("r0", "r5", 4);
                a.Ldrb("r0", "r1", 12); a.Strb("r0", "r5", 6); a.Movs("r0", cfg.counter ? 1 : 0); a.Strb("r0", "r5", 7);
                a.Movs("r0", 0); a.Strb("r0", "r5", 8); a.B("tl_end");
                a.Label("tl_0"); a.Movs("r6", 0);
                a.Mov32("r7", XBUF); a.Ldr("r1", "r7", 108); a.Movs("r0", 0); a.CmpI("r1", 6); a.Bcc("hi", "tl_x");
                a.Movs("r2", 12); a.Muls("r2", "r1"); a.AddsR("r2", "r2", "r7"); a.Ldrh("r0", "r2", 8);
                a.Ldr("r2", "r2", 4);
                a.Mov32("r3", S_ADDR); a.Ldrb("r1", "r3", 8); a.Ldrb("r3", "r3", 5); a.SubsR("r3", "r3", "r1");
                a.Bcc("gt", "tl_c"); a.Movs("r3", 1);
                a.Label("tl_c"); a.Udiv("r2", "r2", "r3"); a.Mov32("r3", 0xFFFF); a.CmpR("r2", "r3"); a.Bcc("ls", "tl_l"); a.Mov("r2", "r3");
                a.Label("tl_l"); a.Strh("r2", "r5", 6);
                a.Label("tl_x"); a.Strh("r0", "r5", 2);
                a.Mov32("r7", YBUF); a.Ldr("r1", "r7", 108); a.Movs("r0", 0); a.CmpI("r1", 6); a.Bcc("hi", "tl_y");
                a.Movs("r2", 12); a.Muls("r2", "r1"); a.AddsR("r2", "r2", "r7"); a.Ldrh("r0", "r2", 8);
                a.Label("tl_y"); a.Strh("r0", "r5", 4);
                a.Mov32("r1", AGC); a.Ldrb("r0", "r1", 1); a.Strb("r0", "r5", 8);
                a.Label("tl_end");
                a.Movs("r0", 0xC0); a.Strb("r0", "r5", 1); a.Movs("r0", 0xE0); a.Orrs("r0", "r6"); a.Strb("r0", "r5", 9);
                a.Label("tl_out");
                a.PopW(new string[] { "r0", "r1", "r2", "r4", "r5", "r6", "r7" }, true); a.BWA(Fw.A(0x08009CB0));
                        }
            if (cfg.counter)
            {
                // flash counter in the free page 0x08002C00 of the bootloader area, through the firmware's own flash
                // routines (erase page 0x08009A1C, program words 0x08009A40). Counts once per new build: a reboot of
                // the same build finds its own id in the last record and changes nothing.
                a.Label("fcount");
                a.Push(Rg("r4", "r5", "r6", "r7"), true); a.SubSp(16);
                a.Mov32("r4", Build.COUNTER_PAGE); a.Mov32("r0", tab + 80); a.Ldr("r5", "r0", 0); a.Movs("r3", 0);
                a.Ldr("r0", "r4", 0); a.Mov32("r1", Build.COUNTER_MAGIC); a.CmpR("r0", "r1"); a.Bcc("ne", "fc_init");
                a.Ldr("r0", "r4", 4); a.Ldr("r1", "r4", 8); a.Mvns("r1", "r1"); a.CmpR("r0", "r1"); a.Bcc("ne", "fc_init");
                a.Mov("r7", "r0"); a.Mov("r6", "r4"); a.AddsI("r6", 16);
                a.Label("fc_scan");
                a.Mov32("r0", Build.COUNTER_PAGE + 0x400); a.CmpR("r6", "r0"); a.Bcc("cs", "fc_full");
                a.Ldr("r0", "r6", 0); a.Ldr("r1", "r6", 4);
                a.AddsI3("r2", "r0", 1); a.Bcc("ne", "fc_rec");
                a.AddsI3("r2", "r1", 1); a.Bcc("eq", "fc_free");
                a.Label("fc_rec");
                a.Mvns("r2", "r1"); a.CmpR("r0", "r2"); a.Bcc("ne", "fc_next");        // half-written: ignored
                a.AddsI("r7", 1); a.Mov("r3", "r0");
                a.Label("fc_next"); a.AddsI("r6", 8); a.B("fc_scan");
                a.Label("fc_free");
                a.CmpR("r3", "r5"); a.Bcc("eq", "fc_done");
                if (cfg.keepCount) { a.Mov32("r0", BL_ID); a.CmpR("r3", "r0"); a.Bcc("eq", "fc_done"); }   // R45: the bootloader counted this update
                a.Mov32("r0", BL_ERASE); a.Ldr("r0", "r0", 0); a.Mov32("r1", BlPatchWord()); a.CmpR("r0", "r1"); a.Bcc("eq", "fc_done");
                a.StrSp("r5", 0); a.Mvns("r0", "r5"); a.StrSp("r0", 4);
                a.Mov("r0", "r6"); a.Mov("r1", "sp"); a.Movs("r2", 2); a.BlA(Fw.A(0x08009A40));
                a.Ldr("r0", "r6", 0); a.CmpR("r0", "r5"); a.Bcc("ne", "fc_err");
                a.AddsI("r7", 1); a.B("fc_done");
                a.Label("fc_full"); a.Mov32("r0", BL_ERASE); a.Ldr("r0", "r0", 0); a.Mov32("r1", BlPatchWord()); a.CmpR("r0", "r1"); a.Bcc("eq", "fc_cmp");
                a.CmpR("r3", "r5"); a.Bcc("eq", "fc_done");
                if (cfg.keepCount) { a.Mov32("r0", BL_ID); a.CmpR("r3", "r0"); a.Bcc("eq", "fc_done"); }
                a.B("fc_write");
                a.Label("fc_cmp");                                      // bootloader counts: compact, no record
                a.Mov("r0", "r4"); a.BlA(Fw.A(0x08009A1C));
                a.Mov32("r0", Build.COUNTER_MAGIC); a.StrSp("r0", 0); a.StrSp("r7", 4); a.Mvns("r0", "r7"); a.StrSp("r0", 8);
                a.Mov("r0", "r4"); a.Mov("r1", "sp"); a.Movs("r2", 3); a.BlA(Fw.A(0x08009A40)); a.B("fc_done");
                a.Label("fc_init"); a.Movs("r7", 0);
                a.Label("fc_write");                                    // erase, header (base = count), our record
                a.Mov("r0", "r4"); a.BlA(Fw.A(0x08009A1C));
                a.Mov32("r0", Build.COUNTER_MAGIC); a.StrSp("r0", 0); a.StrSp("r7", 4); a.Mvns("r0", "r7"); a.StrSp("r0", 8);
                a.Mov("r0", "r4"); a.Mov("r1", "sp"); a.Movs("r2", 3); a.BlA(Fw.A(0x08009A40));
                a.StrSp("r5", 0); a.Mvns("r0", "r5"); a.StrSp("r0", 4);
                a.Mov("r0", "r4"); a.AddsI("r0", 16); a.Mov("r1", "sp"); a.Movs("r2", 2); a.BlA(Fw.A(0x08009A40));
                a.Ldr("r0", "r4", 16); a.CmpR("r0", "r5"); a.Bcc("ne", "fc_err");
                a.AddsI("r7", 1);
                a.Label("fc_done"); a.Movs("r1", 0); a.B("fc_st");
                a.Label("fc_err"); a.Movs("r1", 1);                    // not written (e.g. write-protected)
                a.Label("fc_st"); a.Mov32("r0", VS_ADDR); a.Str("r7", "r0", 8); a.Strb("r1", "r0", 12);
                a.AddSp(16); a.Pop(Rg("r4", "r5", "r6", "r7"), true);
            }
            if (cfg.blCounter || cfg.cleanup)
            {
                // pcmp: r0 = 1 to compare page 0x08000C00 against stock+patch, 0 against stock. returns r0 = 0 equal,
                //       1 differs but is a clean prefix followed by erased words (interrupted rewrite), 2 differs
                a.Label("pcmp");
                a.Push(Rg("r4", "r5", "r6", "r7"), true);
                a.Mov("r7", "r0"); a.Mov32("r4", BL_PAGE); a.Mov32("r5", stockAt); a.Movs("r6", 0); a.Movs("r3", 0);
                a.Label("pc_l");
                a.Ldr("r1", "r5", 0); a.Cbz("r7", "pc_e");
                a.Mov32("r2", BL_PAGE + pidx * 4); a.CmpR("r4", "r2"); a.Bcc("ne", "pc_e"); a.Mov32("r1", pw);
                a.Label("pc_e"); a.Ldr("r0", "r4", 0); a.CmpR("r0", "r1"); a.Bcc("eq", "pc_ok");
                a.AddsI3("r2", "r0", 1); a.Bcc("ne", "pc_bad"); a.Movs("r3", 1); a.B("pc_n");        // erased word
                a.Label("pc_ok"); a.Cbz("r3", "pc_n"); a.Label("pc_bad"); a.Movs("r6", 2); a.B("pc_r");  // data after a gap
                a.Label("pc_n"); a.AddsI("r4", 4); a.AddsI("r5", 4);
                a.Mov32("r2", BL_PAGE + 1024); a.CmpR("r4", "r2"); a.Bcc("cc", "pc_l");
                a.Mov("r6", "r3");
                a.Label("pc_r"); a.Mov("r0", "r6"); a.Pop(Rg("r4", "r5", "r6", "r7"), true);
                // pwrite: r0 = 1 writes stock+patch, 0 writes stock: erase page 0x08000C00 and program it from the blob
                // pwrite erases page 0x08000C00 and reprograms it, writing the hook word at pidx LAST. Until that final
                // single-word commit, the page holds the full stock bootloader with set_boot_state's entry still erased
                // - and a normal boot never executes that entry, so the tablet still boots and the firmware retries. So
                // an interruption is only unrecoverable while the stock body itself is incomplete (the boot code the CPU
                // runs from reset); the commit of the hook itself is atomic.
                a.Label("pwrite");
                a.Push(Rg("r4", "r5"), true); a.SubSp(8); a.Mov("r4", "r0");
                a.Mov32("r0", BL_PAGE); a.BlA(Fw.A(0x08009A1C));
                a.Mov32("r0", BL_PAGE); a.Mov32("r1", stockAt); a.Movs("r2", pidx); a.BlA(Fw.A(0x08009A40));              // [0..pidx)
                a.Mov32("r0", BL_PAGE + (pidx + 1) * 4); a.Mov32("r1", stockAt + (pidx + 1) * 4); a.Movs("r2", 255 - pidx); a.BlA(Fw.A(0x08009A40));  // (pidx..256)
                a.Mov32("r1", stockAt + pidx * 4); a.Ldr("r0", "r1", 0); a.Cbz("r4", "pw_s"); a.Mov32("r0", pw);
                a.Label("pw_s"); a.StrSp("r0", 0);
                a.Mov32("r0", BL_PAGE + pidx * 4); a.Mov("r1", "sp"); a.Movs("r2", 1); a.BlA(Fw.A(0x08009A40));           // commit hook word last
                a.AddSp(8); a.Pop(Rg("r4", "r5"), true);
                // p0cmp: reads the live boot-jump literal at 0x0800015C. r0 = 0 already redirected to the stub,
                //        1 stock value (0x08000C39, redirect not applied yet), 2 anything else (unknown page 0: skip).
                a.Label("p0cmp");
                a.Mov32("r1", BL_BOOT_LIT); a.Ldr("r0", "r1", 0);
                a.Mov32("r1", stubEntry | 1); a.CmpR("r0", "r1"); a.Bcc("ne", "p0c_1"); a.Movs("r0", 0); a.BxLr();
                a.Label("p0c_1"); a.Mov32("r1", BL_BOOT_WORD); a.CmpR("r0", "r1"); a.Bcc("ne", "p0c_2"); a.Movs("r0", 1); a.BxLr();
                a.Label("p0c_2"); a.Movs("r0", 2); a.BxLr();
                // p0write: erase page 0x08000000 and program it from the stored copy (pure stock except the one
                //          redirected literal), ascending so the reset-path words are restored first. This single page
                //          rewrite is the one irreducible interrupted-install window (a power loss here is ROM recovery);
                //          it is done BEFORE the C00 hook so that, once it completes, the stub guards every later boot.
                a.Label("p0write");
                a.Push(NONE, true);
                a.Mov32("r0", BL_PAGE0); a.BlA(Fw.A(0x08009A1C));
                a.Mov32("r0", BL_PAGE0); a.Mov32("r1", stock0At); a.Movs("r2", 128); a.BlA(Fw.A(0x08009A40));
                a.Mov32("r0", BL_PAGE0 + 512); a.Mov32("r1", stock0At + 512); a.Movs("r2", 128); a.BlA(Fw.A(0x08009A40));
                a.Pop(NONE, true);
                // p0clean: restore page 0x08000000 to pure stock (boot-jump literal back to 0x08000C39), removing the
                //          redirect. Used by return-to-stock, BEFORE the stub's page is erased.
                a.Label("p0clean");
                a.Push(NONE, true); a.SubSp(8);
                a.Mov32("r0", BL_PAGE0); a.BlA(Fw.A(0x08009A1C));
                a.Mov32("r0", BL_PAGE0); a.Mov32("r1", stock0At); a.Movs("r2", p0idx); a.BlA(Fw.A(0x08009A40));
                a.Mov32("r0", BL_PAGE0 + (p0idx + 1) * 4); a.Mov32("r1", stock0At + (p0idx + 1) * 4); a.Movs("r2", 255 - p0idx); a.BlA(Fw.A(0x08009A40));
                a.Mov32("r0", BL_BOOT_WORD); a.StrSp("r0", 0);
                a.Mov32("r0", BL_PAGE0 + p0idx * 4); a.Mov("r1", "sp"); a.Movs("r2", 1); a.BlA(Fw.A(0x08009A40));
                a.AddSp(8); a.Pop(NONE, true);
                if (cfg.blCounter)
                {
                    // install / repair: routine into the empty page 0x08002800, then the 4-byte hook in page 0x08000C00,
                    // only if that page is stock (or a rewrite of ours that was interrupted). VS+13: 0 installed,
                    // 2 page 0x08002800 holds something else, 3 page 0x08000C00 is not the expected bootloader, 4 verify failed
                    a.Label("blinst");
                    a.Push(Rg("r4", "r5"), true);
                    // Make sure the whole routine at 0x08002800 is present and correct BEFORE touching anything boot-
                    // critical. A wrapper write interrupted after its first word used to pass a word-0-only check, so the
                    // hook could be installed pointing at a half-written routine (brick). 0x08002800 is a dedicated empty
                    // page, so a full compare + erase-and-reprogram on any mismatch is safe (nothing points at it yet).
                    a.Bl("wchk"); a.CmpI("r0", 0); a.Bcc("eq", "bi_wok");
                    a.Mov32("r0", BL_WRAP); a.BlA(Fw.A(0x08009A1C));
                    a.Mov32("r0", BL_WRAP); a.Mov32("r1", wrapAt); a.Movs("r2", nWrap); a.BlA(Fw.A(0x08009A40));
                    a.Bl("wchk"); a.Movs("r4", 2); a.CmpI("r0", 0); a.Bcc("ne", "bi_st");              // still bad: leave stock, status 2
                    a.Label("bi_wok");
                    // ---- reset-vector redirect: point the boot jump at the repair stub. Done now (routine+stub present),
                    //      before the C00 hook, so the stub guards the C00 rewrite that follows. VS+14: 0 redirected now,
                    //      1 already redirected, 2 page 0 is not the expected stock (skipped), 3 redirect verify failed.
                    a.Bl("p0cmp"); a.Movs("r5", 1); a.CmpI("r0", 0); a.Bcc("eq", "bi_rd");             // already redirected
                    a.Movs("r5", 2); a.CmpI("r0", 1); a.Bcc("ne", "bi_rd");                            // r0==2 unknown page 0: skip
                    a.Bl("p0write");
                    a.Bl("p0cmp"); a.Movs("r5", 0); a.CmpI("r0", 0); a.Bcc("eq", "bi_rd"); a.Movs("r5", 3);
                    a.Label("bi_rd"); a.Mov32("r0", VS_ADDR); a.Strb("r5", "r0", 14);
                    // ---- C00 hook install (now recoverable: the stub repairs an interruption on the next boot) ----
                    a.Movs("r0", 1); a.Bl("pcmp"); a.Movs("r4", 0); a.CmpI("r0", 0); a.Bcc("eq", "bi_st");  // already installed
                    a.CmpI("r0", 1); a.Bcc("eq", "bi_go");                                             // interrupted: repair
                    a.Movs("r0", 0); a.Bl("pcmp"); a.Movs("r4", 3); a.CmpI("r0", 0); a.Bcc("ne", "bi_st"); // not stock: leave it
                    a.Label("bi_go"); a.Movs("r0", 1); a.Bl("pwrite");
                    a.Movs("r0", 1); a.Bl("pcmp"); a.Movs("r4", 0); a.CmpI("r0", 0); a.Bcc("eq", "bi_st"); a.Movs("r4", 4);
                    a.Label("bi_st"); a.Mov32("r0", VS_ADDR); a.Strb("r4", "r0", 13);
                    a.Pop(Rg("r4", "r5"), true);
                    // wchk: r0 = 0 if the routine at 0x08002800 fully matches the built wrapper (nWrap words), else 1
                    a.Label("wchk");
                    a.Push(Rg("r4", "r5", "r6"), false);
                    a.Mov32("r4", BL_WRAP); a.Mov32("r5", wrapAt); a.Movs("r6", nWrap);
                    a.Label("wc_l");
                    a.Ldr("r0", "r4", 0); a.Ldr("r1", "r5", 0); a.CmpR("r0", "r1"); a.Bcc("ne", "wc_bad");
                    a.AddsI("r4", 4); a.AddsI("r5", 4); a.SubsI("r6", 1); a.Bcc("ne", "wc_l");
                    a.Movs("r0", 0); a.B("wc_r");
                    a.Label("wc_bad"); a.Movs("r0", 1);
                    a.Label("wc_r"); a.Pop(Rg("r4", "r5", "r6"), false); a.BxLr();
                }
                if (cfg.cleanup)
                {
                    // return to stock: page 0x08000C00 back to stock (if it carries our hook or a broken rewrite), then the
                    // routine and counter pages erased. VS+13: 0 done, 3 page 0x08000C00 is not ours to restore, 4 verify failed
                    a.Label("blclean");
                    a.Push(Rg("r4"), true);
                    a.Bl("p0cmp"); a.Movs("r4", 3); a.CmpI("r0", 2); a.Bcc("eq", "bc_err");             // unknown reset redirect: preserve everything
                    a.Movs("r0", 0); a.Bl("pcmp"); a.Movs("r4", 0); a.CmpI("r0", 0); a.Bcc("eq", "bc_p0");  // C00 already stock
                    a.Movs("r0", 1); a.Bl("pcmp"); a.Movs("r4", 3); a.CmpI("r0", 2); a.Bcc("eq", "bc_err"); // C00 not ours: leave everything
                    a.Movs("r0", 0); a.Bl("pwrite");                                                        // restore C00 while the stub still guards it
                    a.Movs("r0", 0); a.Bl("pcmp"); a.Movs("r4", 0); a.CmpI("r0", 0); a.Bcc("eq", "bc_p0"); a.Movs("r4", 4); a.B("bc_err");
                    a.Label("bc_p0");                                                                       // remove the reset-vector redirect
                    a.Bl("p0cmp"); a.CmpI("r0", 1); a.Bcc("eq", "bc_verify");                               // already stock
                    a.Movs("r4", 3); a.CmpI("r0", 0); a.Bcc("ne", "bc_err");                              // unknown reset redirect: keep its target
                    a.Bl("p0clean");                                                                        // restore page 0 BEFORE erasing the stub's page
                    // Verify every reset-page word before erasing its former target. A failed page-0 write must
                    // leave the repair stub available, including when only page 0 is write-protected.
                    a.Label("bc_verify");
                    a.Mov32("r4", BL_PAGE0); a.Mov32("r1", stock0At);
                    a.Label("bc_vl");
                    a.Ldr("r2", "r1", 0); a.Mov32("r0", BL_BOOT_LIT); a.CmpR("r4", "r0"); a.Bcc("ne", "bc_vcmp");
                    a.Mov32("r2", BL_BOOT_WORD);
                    a.Label("bc_vcmp"); a.Ldr("r3", "r4", 0); a.CmpR("r2", "r3"); a.Bcc("ne", "bc_vbad");
                    a.AddsI("r4", 4); a.AddsI("r1", 4); a.Mov32("r0", BL_PAGE0 + 1024); a.CmpR("r4", "r0"); a.Bcc("cc", "bc_vl");
                    a.Movs("r4", 0); a.B("bc_pg");
                    a.Label("bc_vbad"); a.Movs("r4", 4); a.B("bc_err");
                    a.Label("bc_pg");                                                                       // R45: erased pages are not erased again (every cold boot)
                    a.Mov32("r0", BL_WRAP); a.Bl("pgclr");
                    if (!cfg.keepCount) { a.Mov32("r0", Build.COUNTER_PAGE); a.Bl("pgclr"); }               // R45: the removal keeps the count page
                    a.Label("bc_err");
                    a.Mov32("r0", VS_ADDR); a.Strb("r4", "r0", 13);
                    a.Pop(Rg("r4"), true);
                    // pgclr: r0 = page; erases it only if a word of it is not erased
                    a.Label("pgclr");
                    a.Push(NONE, true);
                    a.Mov("r1", "r0"); a.Mov32("r2", 1024); a.AddsR("r2", "r2", "r0");
                    a.Label("pgc_l"); a.Ldr("r3", "r1", 0); a.AddsI3("r3", "r3", 1); a.Bcc("ne", "pgc_e");
                    a.AddsI("r1", 4); a.CmpR("r1", "r2"); a.Bcc("cc", "pgc_l"); a.B("pgc_r");
                    a.Label("pgc_e"); a.BlA(Fw.A(0x08009A1C));
                    a.Label("pgc_r"); a.Pop(NONE, true);
                }
            }
            a.Label("pr_r0");
            a.Push(Rg("r1", "r2"), false);
            if (longS)
            {
                a.Mov32("r1", VS_ADDR); a.Ldrb("r2", "r1", 3); a.Cbz("r2", "pr_st");
                a.Ldrb("r1", "r1", 1); a.AddsR("r0", "r0", "r1"); a.SubsI("r0", 3);
                a.CmpI("r0", 1); a.Bcc("ge", "pr_1"); a.Movs("r0", 1);
                a.Label("pr_1"); a.CmpI("r0", 39); a.Bcc("le", "pr_2"); a.Movs("r0", 39);
                a.Label("pr_2"); a.Pop(Rg("r1", "r2"), false); a.BxLr();
                a.Label("pr_st");
            }
            // the firmware's search-probe frequency list, through the firmware's own pointer to it
            a.Mov32("r1", Fw.Ptr(0x08005E38)); a.Lsls("r0", "r0", 1); a.AddsR("r1", "r1", "r0"); a.Ldrh("r0", "r1", 0);
            a.Pop(Rg("r1", "r2"), false); a.BxLr();
            a.Label("pr_r1");
            a.Push(Rg("r0"), true); a.Mov("r0", "r1"); a.Bl("pr_r0"); a.Mov("r1", "r0"); a.Pop(Rg("r0"), true);
            int apM = cfg.apip != null && cfg.apipK != null ? cfg.apipK[ApipRobust.K_M] : 0;
            if (cfg.apip != null || cfg.apipTrain) Apip.EmitSubs(a, cfg.apip != null, aptab + Apip.TAB_BYTES, apM);
            if (cfg.apip != null) Apip.EmitFilter(a, aptab, boost, apM > 0);
            if (cfg.switchMatch) SwitchMatch.Emit(a, smtab, smN);
            if (cfg.coilLin) CoilLinearization.Emit(a, cltab);
            if (cfg.antiChatter) DynamicAntiChatter.Emit(a, dactab, dacN, boost);
            if (cfg.lab)
            {
                CoilLinearization.Emit(a, labB, "labclb", "clb_"); CoilLinearization.Emit(a, labC, "labclc", "clc_");
                Lab.EmitChain(a, cfg);
            }
            a.Label("cp12");
            a.Movs("r3", 0);
            a.Label("cp_l"); a.LdrbR("r2", "r0", "r3"); a.StrbR("r2", "r1", "r3"); a.AddsI("r3", 1);
            a.CmpI("r3", 12); a.Bcc("ne", "cp_l"); a.BxLr();

            string stages = "fast" + (boost ? " + boost" : "") + (reach ? " + reach" : "");
            im.EndAlloc(a, (hyb ? "SOF-locked (ladder steps between whole frames timer-paced)" : sof ? "SOF-locked" : "timer-paced") + " scheduler: every " + F.Str(periodUs) + " us, hover stages " + stages
                + ", tilt over " + F.Str(phases.Length) + "/" + F.Str(tiltEvery) + " frames, sweep " + (sb != 0 ? "chunk " + F.Str(chunk) : "full")
                + (longS ? ", long-range search" : "") + (cfg.multiCoil ? ", Multi-coil Read" : "") + (cfg.freqHold ? ", Frequency Hold" : "") + (cfg.coilHold ? ", Coil Hold" : "") + (cfg.tiltSkip ? ", Tilt Frame Skip" : "") + (cfg.switchMatch ? ", Switch Match" : "") + (wstab != 0 ? ", Window Scale" : "") + (cgtab != 0 ? ", Coil Gains" : "") + (cfg.coilLin ? ", Coil Linearization" : "") + (cfg.antiChatter ? ", Dynamic Anti-Chatter" : "") + (cfg.apip != null ? (cfg.apipK != null ? ", ALPI robust " + F.Str(cfg.apipK[ApipRobust.K_M]) : ", ALPI") : "") + (cfg.apipTrain ? ", ALPI TRAINING" : "") + (cfg.lab ? ", FILTER ARRANGER (lab build)" : "") + (cfg.telemetry ? ", CALIBRATION TELEMETRY" : "") + (cfg.counter ? ", flash counter" : "") + (cfg.blCounter ? ", BOOTLOADER COUNTER" : "") + (cfg.cleanup ? (cfg.keepCount ? ", BOOTLOADER COUNTER REMOVAL" : ", RETURN TO STOCK") : "") + (cfg.systickOff ? ", bootloader's SysTick off" : ""));
            StrIntMap L = a.labels;
            if (cfg.lab) Lab.labels = L;       // the lab build's twin tests (experiments/precision/labstep.cs) call its routines
            if (cfg.apip != null)
            {
                int fa = L.Get("apfilt") | 1;
                im.Wr(aptab + Apip.TAB_BYTES - 4, new byte[] { (byte)fa, (byte)(fa >> 8), (byte)(fa >> 16), (byte)(fa >> 24) }, "ALPI: where its routine is (the twin times it)");
            }
            im.Branch(Fw.A(0x08009B64), Image.Hw2(0x2000, 0x399C), L.Get("start_x"), true, "X fine-scan start: line set per frame type");
            im.Branch(Fw.A(0x08009B88), Image.Hw2(0x2000, 0x7048), L.Get("start_y"), true, "Y fine-scan start: line set per frame type");
            im.Branch(Fw.A(0x0800BBCE), Image.Hw2(0x1C49, 0x6050), L.Get("inc_x"), true, "X line step: outer line 6 only when scheduled");
            im.Branch(Fw.A(0x0800BC12), Image.Hw2(0x1C49, 0x6050), L.Get("inc_y"), true, "Y line step: outer line 6 only when scheduled");
            im.Branch(Fw.A(0x0800BBDE), Image.Hw2(0xF7FB, 0xBE81), L.Get("xdone"), false, "X scan complete: cache outer lines" + (cfg.coilHold ? ", Coil Hold" : ""));
            im.Branch(Fw.A(0x0800BC22), Image.Hw2(0xF7FB, 0xBEC1), L.Get("ydone"), false, "Y scan complete: cache outer lines" + (cfg.coilHold ? ", Coil Hold" : ""));
            im.Branch(Fw.A(0x0800597E), Image.Hw2(0x4902, 0x2001), L.Get("chunk"), false, "sweep: pause at chunk boundary");
            im.Branch(Fw.A(0x08005966), Image.Hw2(0xF001, 0xBF6B), L.Get("fin"), false, "sweep complete: pen state fresh");
            im.Branch(Fw.A(0x08007084), Image.Hw2(0x4808, 0x2200), L.Get("restart"), false, "frame restart: hover stage, plan, arm");
            im.Branch(Fw.A(0x08005D9C), Image.Hw2(0xF003, 0xFE76), L.Get("gate"), true, "step timer: start the armed frame on time");
            im.Branch(Fw.A(0x080040F4), Image.Hw2(0xF003, 0xFD1C), L.Get("pen"), true, "pen-state processing only after a full sweep");
            im.Expect(Fw.A(0x080040F8), Image.Hw2(0xF003, 0xFABC)); im.Wr(Fw.A(0x080040F8), Image.Hw2(0xF3AF, 0x8000), "(folded into the gate above)");
            im.Branch(Fw.A(0x0800412C), Image.Hw2(0xF003, 0xFA6A), L.Get("freq"), true, "pressure only after a full sweep");
            im.Branch(Fw.A(0x08004136), Image.Hw2(0xF001, 0xFEA5), L.Get("tilt"), true, "tilt from cached outer lines, once per cycle");
            im.Branch(Fw.A(0x08006F50), Image.Hw2(0xE92D, 0x41F0), L.Get("search"), false, "search restart: stages, long-range, probes");
            im.Expect(Fw.A(0x0800416A), Image.Hw(0x2002)); im.Wr(Fw.A(0x0800416A), Image.Hw(0x2000), "frame done: report released by the scheduler");
            if (cal != null || cfg.freqHold)
            {
                string fwhat = (cfg.freqHold ? "held drive step" : "resonance") + (cal != null ? " + calibrated offset" : "");
                im.Branch(Fw.A(0x08005DF6), Image.Hw2(0x703E, 0x78C2), L.Get("fsel"), true, "X scan frequency: " + fwhat);
                im.Branch(Fw.A(0x08005E08), Image.Hw2(0x703E, 0x78C2), L.Get("fsel"), true, "Y scan frequency: " + fwhat);
            }
            if (cal != null || zled)
                im.Branch(Fw.A(0x08004814), Image.Hw2(0xF003, 0xFE56), L.Get("ledgate"), true, zled ? "LED (PC13): pulses (" + F.Fx(1000.0 / LED_FADE_MS, 1) + " Hz, down to 10 %, PWM " + (ledAll ? "every step-timer tick" : ledSome && ledsteps != 0 ? "every step-timer tick in the Steady Hover steps of 30 pulses or more, once per frame in the others" : ledSome ? "every step-timer tick in Steady Hover and Reach Stage, once per frame in the fast stage" : "once per frame") + ") while calibrating" : "LED (PC13): blinks (2.5 Hz) while calibrating");
            cfg.ledTarget = cal != null || zled ? L.Get("ledgate") : -1;
            Stage f = cfg.modes[0];
            im.Branch(Fw.A(0x0800710C), Image.Hw2(0x780B, 0x2200 | f.burst), L.Get("bsel"), true, "pulses + samples per step kind (scan/sweep/search)");
            im.Branch(Fw.A(0x08005DAC), Image.Hw2(0x2000 | f.tick, 0x6020), L.Get("tsel"), true, "step tick per step kind (scan/sweep/search)");
            if (cfg.multiCoil)
            {
                // R43: both calls of the stock position routine go to Multi-coil Read, which fits the five lines itself
                // and hands the call to the stock routine, untouched, where the fit does not apply
                im.Branch(Fw.A(0x080083BE), Image.Hw2(0xF7FD, 0xF838), L.Get("mc_x"), true, "X position: Multi-coil Read (five-line fit; stock routine where it does not apply)");
                im.Branch(Fw.A(0x0800840A), Image.Hw2(0xF7FD, 0xF812), L.Get("mc_y"), true, "Y position: Multi-coil Read (five-line fit; stock routine where it does not apply)");
            }
            else if (wstab != 0)
            {
                // R45-rc4 (2): the same two calls go through Window Scale, which calls the stock routine and corrects its answer
                im.Branch(Fw.A(0x080083BE), Image.Hw2(0xF7FD, 0xF838), L.Get("ws_x"), true, "X position: stock routine, then Window Scale (stretch inside the coil window per pressure)");
                im.Branch(Fw.A(0x0800840A), Image.Hw2(0xF7FD, 0xF812), L.Get("ws_y"), true, "Y position: stock routine, then Window Scale (stretch inside the coil window per pressure)");
            }
            im.Branch(Fw.A(0x08007876), Image.Hw2(0xF5B7, 0x7F96), L.Get("thr_sw"), true, "sweep pick-up threshold 300 -> per stage (drop in ip)");
            im.Expect(Fw.A(0x08007888), Image.Hw(0x2FC8)); im.Wr(Fw.A(0x08007888), Image.Hw(0x4567), "sweep drop threshold 200 -> per stage (cmp r7, ip)");
            im.Branch(Fw.A(0x08007928), Image.Hw2(0xF5B2, 0x7F96), L.Get("thr_x"), true, "X pick-up threshold 300 -> per stage (drop in ip)");
            im.Expect(Fw.A(0x0800793E), Image.Hw(0x2AC8)); im.Wr(Fw.A(0x0800793E), Image.Hw(0x4562), "X drop threshold 200 -> per stage (cmp r2, ip)");
            im.Branch(Fw.A(0x080079DE), Image.Hw2(0x8932, 0x2AC8), L.Get("thr_y"), true, "Y drop threshold 200 -> per stage");
            if (cfg.telemetry)
            {
                im.Branch(Fw.A(0x08004B32), Image.Hw2(0xF005, 0xF8BD), L.Get("tsend"), true, "pen report send: carries the firmware's readings (calibration pass)");
                im.Branch(Fw.A(0x08004B66), Image.Hw2(0xF005, 0xF8A3), L.Get("tsend"), true, "pen report send: carries the firmware's readings (calibration pass)");
            }
            else if (cfg.lab)
            {
                im.Branch(Fw.A(0x08004B32), Image.Hw2(0xF005, 0xF8BD), L.Get("labsend"), true, "pen report send: Filter Arranger shadow bytes 10-11 (lab build)");
                im.Branch(Fw.A(0x08004B66), Image.Hw2(0xF005, 0xF8A3), L.Get("labsend"), true, "pen report send: Filter Arranger shadow bytes 10-11 (lab build)");
            }
            int[] sites = new int[] { Fw.A(0x08005DD8), Fw.A(0x08005DE6), Fw.A(0x08006FDE), Fw.A(0x0800704A) };
            int[][] exps = new int[][] { new int[] { 0xF833, 0x0010 }, new int[] { 0xF833, 0x0010 }, new int[] { 0xF832, 0x1011 }, new int[] { 0xF813, 0x0010 } };
            string[] lbls = new string[] { "pr_r0", "pr_r0", "pr_r1", "pr_r0" };
            for (int i = 0; i < 4; i++)
                im.Branch(sites[i], Image.Hw2(exps[i][0], exps[i][1]), L.Get(lbls[i]), true, "search probe -> frequency index (stock list, or last frequency -3..+3)");
        }
    }
}

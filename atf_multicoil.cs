// atf_multicoil.cs (R43) - Multi-coil Read: the pen's position from all five lines around it.
//
// The stock position routine (0x08005432) combines a 3-line parabola with a 4-line ratio. Multi-coil Read replaces
// that arithmetic with a fit of the five line sums (centre-2..centre+2) to the coils' measured response, and leaves
// the stock routine to run untouched wherever the fit does not apply. Because it replaces the stock arithmetic rather
// than adding to it, a frame takes less time with it than without (second R43 version; the first ran after the stock
// routine and cost 59 us per frame, too much for ULTRA).
// A coil is described only by its centre and its effective range (the distance from its centre, in coil pitches, where
// its response falls to half of the response at the centre): Anargon's coils may be multi-layered and not circular,
// so no coil shape is assumed. Everything here was measured on Anargon's tablet in R43 (rev-output/hw43):
//   - the coil grid is an even 0.2 inch (5.08 mm) pitch on both axes, the same at every speed;
//   - the response RESP: R(d) = 1, 0.92 at 0.5 pitch, 0.70 at 1, 0.40 at 1.5, 0.115 at 2, 0 from about 2.5;
//     effective range 1.34 pitches at touch, the same on both axes and at FAST, RAPID and ULTRA (within 5 %);
//   - a floor under every line sum: FLOOR_A * reads + FLOOR_B (216 at 4 reads, 288 at 5, 632 at 10; read from the
//     lines of coils that do not exist, past the edges), stable to 0 (RAPID, ULTRA) and +-8 (FAST);
//   - equal line weights: weights from measured per-line noise made strokes noisier, because the noise of a line
//     depends on where the pen is.
// Per frame and axis (MultiCoil.Calc is the C# copy the firmware is checked against in the twin, on every call; the
// specification is Claude-workarea/experiments/mcint.py):
//   0. the stock routine runs instead when the peak is not on the centre line, a neighbour line is above the centre
//      line (or both equal it), the reads per line are not positive, or the centre line is less than 8 above the
//      floor (or absurdly large).
//   1. v_j = line - floor, limited to 0..v_centre and scaled so v_centre is 2048..4095; lines of coils that do not
//      exist are left out (the centre line always counts).
//   2. start: the 3-line parabola, rounded to 1/16 pitch. Then two Gauss-Newton steps on the pen offset (equal
//      weights, the pen signal eliminated, at most 0.3 pitch each), each from a 1/16-pitch grid point whose response
//      and slope come from one table word per line: no interpolation and no divide per line.
//   3. the effective range is fixed at ROW/16 of the measured one (17/16: best of the fixed and tracked choices tried
//      on the recordings, see mcint.py).
//   4. result cc * W + x, in the stock routine's units, so everything after it (linearisation, filters) is unchanged.
// Measured on the recordings (offline, rev-output/hw43): frame-to-frame noise 0.69 x stock (geometric mean of 30
// still axis recordings), lower in 27 of 30, at most 1.15 x stock in the other 3. Not yet run on the tablet.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class McResult
    {
        public long calls, match, miss, fit, maxFrame;
        public long labOff;                       // Filter Arranger build: calls with Window Scale switched off in RAM
        public string first;
    }

    // cycles of the position call (the stock routine's, or Multi-coil Read's with whatever it hands on), measured
    // alone with no interrupts on a fixed set of inputs: real-looking lines, every gate, the edges
    public class McCost
    {
        public long n, stockMax, entryMax, fitMax, gateMax;
        public double stockMean, entryMean;
        public string worst;
        public long fit, miss;                      // inputs the fit took; results that differ from Calc (fit) or stock
        public string firstMiss;
    }

    public class McTwinBoard : TabletBoard
    {
        public const int H_CX = 50, H_RX = 51, H_CY = 52, H_RY = 53, H_ST = 54, H_SR = 55;
        public long from = long.MaxValue;
        public bool installed, full, ws;          // ws: R45-rc4 (2) Window Scale on the position calls instead
        public bool wsLab;                        // the Filter Arranger build: a RAM flag can switch Window Scale off (atf_lab.cs)
        public bool cg;                           // Coil Gains (atf_coilgain.cs): Window Scale's wrapper scales the lines first
        int[] exp = new int[5];                   // the lines the stock routine should get (scaled with Coil Gains)
        public int retX, retY;
        McResult res;
        double vx = 0.0031, vy = 0.0023;
        int[] arg = new int[6];
        int pk, reads, axis, spCall;
        bool inCall, stockRan, stockSame, stockArgs, stockDone;
        int pres, stage, stockOut;
        long t0 = 0, lastX = 0;
        public McTwinBoard(Pen p, McResult r) : base(p) { res = r; }

        public override bool Hook(Machine mc, int hk)
        {
            if (hk == H_CX || hk == H_CY)
            {
                if (mc.cpu.cycles < from) return false;
                axis = hk == H_CX ? 0 : 1; spCall = mc.cpu.r[13];
                for (int i = 0; i < 4; i++) arg[i] = mc.cpu.r[i];
                arg[4] = mc.Peek(spCall, 4); arg[5] = mc.Peek(spCall + 4, 4);
                pk = mc.Peek((axis == 0 ? Sched.XBUF : Sched.YBUF) + 108, 1);
                reads = mc.Peek(Sched.S_ADDR + 5, 1) - mc.Peek(Sched.AGC + 1 + axis, 1);
                pres = mc.Peek(Sched.PRESSURE + 6, 2); stage = mc.Peek(Sched.VB_ADDR, 1);
                if (cg) exp = CoilGains.Scale(axis, arg, arg[5], reads, full, stage);
                else for (int i = 0; i < 5; i++) exp[i] = arg[i];
                inCall = true; stockRan = false; stockSame = false; stockArgs = false; stockDone = false; t0 = mc.cpu.cycles;
                return false;
            }
            if (hk == H_SR)
            {
                // R45-rc5: the stock routine returned into ws_x / ws_y; its own answer, for the Window Scale check
                if (inCall && stockRan) { stockOut = mc.cpu.r[0]; stockDone = true; }
                return false;
            }
            if (hk == H_ST)
            {
                // the stock routine starts: with exactly the caller's arguments, stack and return address?
                if (!inCall) return false;
                int sp = mc.cpu.r[13];
                stockRan = true;
                stockSame = sp == spCall && mc.Peek(sp, 4) == arg[4] && mc.Peek(sp + 4, 4) == arg[5] && mc.Peek(sp + 8, 4) == MultiCoil.W
                    && (mc.cpu.r[14] & ~1) == (axis == 0 ? retX : retY);
                stockArgs = mc.Peek(sp, 4) == exp[4] && mc.Peek(sp + 4, 4) == arg[5] && mc.Peek(sp + 8, 4) == MultiCoil.W;
                for (int i = 0; i < 4; i++) if (mc.cpu.r[i] != arg[i]) stockSame = false;
                for (int i = 0; i < 4; i++) if (mc.cpu.r[i] != exp[i]) stockArgs = false;
                if (ws) mc.SetHook(mc.cpu.r[14] & ~1, H_SR);       // where it returns into ws_x / ws_y
                return false;
            }
            if (hk == H_RX || hk == H_RY)
            {
                if (!inCall) return false;
                inCall = false;
                long c = mc.cpu.cycles - t0;
                if (axis == 0) lastX = c; else { if (lastX + c > res.maxFrame) res.maxFrame = lastX + c; lastX = 0; }
                if (ws)
                {
                    // Window Scale: the stock routine with the caller's arguments, then its correction. The correction
                    // is checked on the stock routine's own answer (R45-rc5): its C# copy differs from it on
                    // degenerate lines (the firmware never returns a negative position, and with a flat parabola it
                    // keeps an earlier value; 3392 of 200000 random line sets), and a frame like that once failed the
                    // ULTRA gate (X lines 186,1088,1137,1190,868 centre 10: stock 2799, copy -2799). The stock
                    // routine is XP-Pen's own, unchanged; the copy is still used if its answer was not seen.
                    int st = stockDone ? stockOut : MultiCoil.Stock(exp[0], exp[1], exp[2], exp[3], exp[4], arg[5]);
                    int wv = WindowScale.Apply(axis, st, arg[5], pres, stage), gv = mc.cpu.r[0];
                    if (wsLab && (mc.Peek(Lab.RAM + Lab.ACTF, 1) & Lab.F_WS_OFF) != 0) { wv = st; res.labOff++; }   // switched off in RAM
                    bool wok = stockRan && stockArgs && stockDone && gv == wv;
                    res.calls++;
                    if (wv != st) res.fit++;
                    if (wok) res.match++;
                    else
                    {
                        res.miss++;
                        if (res.first == null)
                            res.first = (axis == 0 ? "X" : "Y") + " lines " + arg[0] + "," + arg[1] + "," + arg[2] + "," + arg[3] + "," + arg[4] + " centre " + arg[5]
                                + " pressure " + pres + " stage " + stage + ": firmware " + gv + (stockRan ? (stockArgs ? "" : " (stock routine ran with other arguments)") : " (stock routine did not run)")
                                + ", C# " + wv + " (stock " + st + ")";
                    }
                    return false;
                }
                if (!installed) return false;
                int want = MultiCoil.Calc(arg[0], arg[1], arg[2], arg[3], arg[4], arg[5], pk, reads, axis, full);
                int got = mc.cpu.r[0];
                bool ok = want == MultiCoil.STOCK ? stockRan && stockSame : !stockRan && got == want;
                res.calls++;
                if (want != MultiCoil.STOCK) res.fit++;
                if (ok) res.match++;
                else
                {
                    res.miss++;
                    if (res.first == null)
                        res.first = (axis == 0 ? "X" : "Y") + " lines " + arg[0] + "," + arg[1] + "," + arg[2] + "," + arg[3] + "," + arg[4] + " centre " + arg[5]
                            + " peak " + pk + " reads " + reads + ": firmware " + got + (stockRan ? " (stock routine ran" + (stockSame ? ")" : " with other arguments)") : "")
                            + ", C# " + (want == MultiCoil.STOCK ? "the stock routine" : F.Str(want));
                }
                return false;
            }
            bool r = base.Hook(mc, hk);
            if (hk == H_FRAME && mc.cpu.cycles >= from)
            {
                pen.x += vx; if (pen.x > 30 || pen.x < 3) vx = -vx;
                pen.y += vy; if (pen.y > 17 || pen.y < 3) vy = -vy;
            }
            return r;
        }
    }

    public static class MultiCoil
    {
        public const int W = 1018;                  // raw position units per coil pitch (the stock routine's scale)
        public const int NSTEP = 16, NTAB = 3 * NSTEP + 1;
        // R(d) at d = 0, 1/16, ..., 3 coil pitches, Q12 (4096 = 1): R43 measurement, mean of FAST, RAPID and ULTRA,
        // both axes, pen touching (experiments/coilfit.py, mcint.py). Our measurement data, not firmware bytes.
        public static readonly int[] RESP = new int[] {
            4096, 4096, 4083, 4047, 4002, 3956, 3919, 3867, 3782, 3699, 3610, 3500, 3390, 3281, 3162, 3038, 2908, 2771,
            2628, 2473, 2316, 2159, 1997, 1839, 1664, 1458, 1287, 1139, 987, 841, 717, 611, 512, 428, 351, 274, 211, 138,
            77, 47, 15, 0, 0, 0, 0, 0, 0, 0, 0 };
        public const int FLOOR_A = 69, FLOOR_B = -60;
        // with 12-bit Readings the cleared low bits (3.5 per reading on average) are kept: the floor is 3.5 per read
        // higher (exact at 4 reads, within 2.5 at 5..10)
        public const int FLOOR12_A = 73, FLOOR12_B = -62;
        public static int FloorA(bool full) { return full ? FLOOR12_A : FLOOR_A; }
        public static int FloorB(bool full) { return full ? FLOOR12_B : FLOOR_B; }
        public const int X_COILS = 32, Y_COILS = 19;
        public const int ROW = 17;                  // effective range = ROW / 16 of the measured one
        public const int UMAX = 45;                 // table: grid points u = -UMAX..UMAX (1/16 pitch); |u| <= 32 + 13
        public const int VC_MIN = 8, LIM = 3 * W / 10;
        public const int STOCK = int.MinValue;      // Calc's answer when the stock routine runs instead
        // frame cost: none. Measured alone (Cost, no interrupts): the fit path takes far less than the stock routine,
        // and the stock path adds at most the gate checks (Cost's gateMax, about 32 cycles per axis; two axes 0.9 us
        // at 72 MHz). Charged 1 us. The core suite checks Cost against this.
        public const int MC_US = 1;

        public static int Floor(int reads, bool full) { return FloorA(full) * reads + FloorB(full); }

        // the measured response at d coil pitches from a coil's centre (1 at the centre), for the twin's pen model
        public static double Response(double d)
        {
            if (d < 0) d = -d;
            double k = d * NSTEP;
            int i = (int)k;
            if (i >= NTAB - 1) return 0.0;
            double f = k - i;
            return (RESP[i] * (1 - f) + RESP[i + 1] * f) / 4096.0;
        }
        // RESP's own effective range: where it falls to half (1.355 pitches)
        public static readonly double HALF = HalfOf();
        static double HalfOf()
        {
            for (int i = 1; i < NTAB; i++)
                if (RESP[i] <= 2048) return (i - 1 + (RESP[i - 1] - 2048) / (double)(RESP[i - 1] - RESP[i])) / NSTEP;
            return 3.0;
        }

        // the stock routine (0x08005432), C# copy (R42, experiments/linesnap.cs: equal to the firmware on real line
        // sums; not on degenerate ones such as all zero, R43 experiments/stockcall.cs)
        public static int Stock(int a, int b, int c, int d, int e, int cc)
        {
            int p1 = 0, p2 = 0;
            if (b - c - d + e != 0 && a - b - c + d != 0)
                p1 = d >= b ? cc * W + (b - d) * W / (b - c - d + e) : cc * W + (b - d) * W / (a - b - c + d);
            int den2 = 2 * (2 * c - b - d);
            if (den2 != 0) p2 = cc * W - (b - d) * W / den2;
            int diff = p2 - p1;
            if ((diff < 0 ? -diff : diff) > W) return p2;
            return (p1 + p2) / 2;
        }

        // the response u/16 pitch from a coil centre at the effective range ROW/16 x the measured one
        public static int RespAt(int u)
        {
            int q = ((u < 0 ? -u : u) * 4096) / ROW;
            int i = q >> 8, f = q & 255;
            return i >= NTAB - 1 ? 0 : RESP[i] + (((RESP[i + 1] - RESP[i]) * f) >> 8);
        }
        // the table: per grid point u the response T and its change over 1/8 pitch E = T(u - 1) - T(u + 1);
        // in flash one word each, T << 16 | E (16 bits, two's complement), so one load feeds the 16-bit multiplies
        public static readonly int[] TAB_T = MakeTab(true), TAB_E = MakeTab(false);
        static int[] MakeTab(bool t)
        {
            int[] o = new int[2 * UMAX + 1];
            for (int u = -UMAX; u <= UMAX; u++) o[u + UMAX] = t ? RespAt(u) : RespAt(u - 1) - RespAt(u + 1);
            return o;
        }
        public static byte[] Table()
        {
            byte[] b = new byte[4 * (2 * UMAX + 1)];
            for (int i = 0; i <= 2 * UMAX; i++)
            {
                int w = (TAB_T[i] << 16) | (TAB_E[i] & 0xFFFF);
                b[4 * i] = (byte)(w & 0xFF); b[4 * i + 1] = (byte)((w >> 8) & 0xFF); b[4 * i + 2] = (byte)((w >> 16) & 0xFF); b[4 * i + 3] = (byte)((w >> 24) & 0xFF);
            }
            return b;
        }

        // one Gauss-Newton step from grid point g; false = it cannot be solved
        static bool Step(int[] v, bool[] ok, int g, out int x)
        {
            int stt = 0, svt = 0, ste = 0, see = 0, sve = 0;
            for (int j = 0; j < 5; j++)
            {
                if (!ok[j]) continue;
                int T = TAB_T[(j - 2) * 16 - g + UMAX], E = TAB_E[(j - 2) * 16 - g + UMAX];
                stt += T * T; svt += v[j] * T; ste += T * E; see += E * E; sve += v[j] * E;
            }
            x = 0;
            int s12 = stt >> 12;
            int A = (int)((uint)svt / (uint)s12);
            int q = ste / s12;                       // C# truncates toward zero, like sdiv
            int t = ste >> 12;
            int num = sve - A * t;
            int pn = see - q * t;
            if (pn <= 0) return false;
            int P = (A * (pn >> 6)) >> 9;
            if (P <= 0) return false;
            int dx = ((num >> 6) * W) / P;
            if (dx > LIM) dx = LIM;
            if (dx < -LIM) dx = -LIM;
            x = dx + ((g * W + 8) >> 4);
            return true;
        }

        // the firmware routine's result for one axis and frame, or STOCK when it hands the call to the stock routine;
        // axis 0 X, 1 Y; reads = samples - this axis's AGC delay
        public static int Calc(int a, int b, int c, int d, int e, int cc, int pk, int reads, int axis, bool full)
        {
            if (pk != 3) return STOCK;
            if (b > c || d > c || b + d == 2 * c) return STOCK;
            if (reads <= 0) return STOCK;
            int F = Floor(reads, full);
            int vc = c - F;
            if ((uint)(vc - VC_MIN) >= (1u << 20)) return STOCK;
            int nCoils = axis == 0 ? X_COILS : Y_COILS;
            int den = 2 * c - b - d;
            int g = (int)((uint)(16 * (d - b) + 17 * den) / (uint)(2 * den)) - 8;
            int sh = 0;
            while (sh < 32 && (vc << sh) >= 0 && ((vc << sh) & (1 << 20)) == 0) sh++;   // clz(vc) - 11
            int[] L = new int[] { a, b, c, d, e };
            int[] v = new int[5];
            bool[] ok = new bool[5];
            for (int j = 0; j < 5; j++)
            {
                ok[j] = j == 2 || (uint)(cc + j - 3) < (uint)nCoils;
                int u = L[j] < F ? 0 : L[j] - F;
                if (u > vc) u = vc;
                v[j] = (u << sh) >> 9;
            }
            int x, x2;
            if (!Step(v, ok, g, out x)) return cc * W + ((d - b) * W) / (2 * den);
            if (!Step(v, ok, (x * 1030 + 32768) >> 16, out x2)) x2 = x;
            return cc * W + x2;
        }

        // ---- the firmware routine. Entries mc_x / mc_y replace the two calls of the stock routine (X 0x080083BE, Y
        // 0x0800840A), with its arguments: r0..r3 = lines centre-2..centre+1, [sp] = centre+2, [sp+4] = centre coil,
        // [sp+8] = 1018. tab = the table (2 UMAX + 1 words), stockFn = the stock routine.
        // The gates use only r12 (and r0, saved): when one fails, the stock routine is entered with the caller's
        // registers, stack and return address, as if it had been called directly.
        public static void Emit(Asm a, int tab, int stockFn, int xbuf, int ybuf, int sAddr, int agc, bool full)
        {
            int offS5 = sAddr + 5 - agc;
            int[] offPk = new int[] { xbuf + 108 - agc, ybuf + 108 - agc };
            if (offS5 < 0 || offS5 > 4095 || offPk[0] < 0 || offPk[0] > 4095 || offPk[1] < 0 || offPk[1] > 4095)
                throw new Stop("internal: Multi-coil Read's RAM is out of reach of one base register");
            string[] hi = new string[] { "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11" };
            string[] r0 = new string[] { "r0" };
            for (int ax = 0; ax < 2; ax++)
            {
                string s = ax == 0 ? "x" : "y", st = "mc_s" + s;
                a.Label("mc_" + s);
                a.Push(r0, false);
                a.Mov32("r0", agc);
                a.LdrbW("r12", "r0", offPk[ax]);                                     // the peak line must be the centre line
                a.CmpIW("r12", 3); a.Bcc("ne", st);
                a.CmpR("r1", "r2"); a.Bcc("gt", st);                                 // b <= c, d <= c, not both equal to c
                a.CmpR("r3", "r2"); a.Bcc("gt", st);
                a.DpRegW(Asm.OP_ADD, false, "r12", "r1", "r3", Asm.SH_LSL, 0);
                a.CmpRW("r12", "r2", 1); a.Bcc("eq", st);
                a.LdrbW("r12", "r0", offS5); a.Ldrb("r0", "r0", 1 + ax);             // reads = samples - this axis's AGC delay
                a.DpRegW(Asm.OP_SUB, true, "r12", "r12", "r0", Asm.SH_LSL, 0); a.Bcc("le", st);
                a.Movs("r0", FloorA(full)); a.MulW("r12", "r12", "r0");                  // vc - 8 = c - floor - 8
                a.DpRegW(Asm.OP_SUB, false, "r12", "r2", "r12", Asm.SH_LSL, 0);
                a.DpImmW(Asm.OP_ADD, false, "r12", "r12", -FloorB(full) - VC_MIN);
                a.Pop(r0, false);
                a.CmpIW("r12", 1 << 20); a.BccWA("cs", stockFn);                     // vc < 8 or absurdly large
                a.PushW(hi, true); a.SubSp(8);
                a.Movs("r4", ax == 0 ? X_COILS : Y_COILS);
                a.B("mc_fit");
                a.Label(st); a.Pop(r0, false); a.BWA(stockFn);
            }
            // r0..r3 = a, b, c, d; r12 = vc - 8; r4 = coil count; [sp] d - b, [sp+4] den (then x after step 1);
            // r4-r11, lr pushed at sp+8; the caller's e at [sp+44], centre coil at [sp+48]
            a.Label("mc_fit");
            a.Mov("r10", "r4");
            a.DpImmW(Asm.OP_ADD, false, "r12", "r12", VC_MIN);                      // vc
            a.DpRegW(Asm.OP_SUB, false, "r11", "r2", "r12", Asm.SH_LSL, 0);         // floor F
            a.SubsR("r5", "r3", "r1");                                               // d - b
            a.Lsls("r6", "r2", 1); a.SubsR("r6", "r6", "r1"); a.SubsR("r6", "r6", "r3");   // den = 2c - b - d
            a.StrSp("r5", 0); a.StrSp("r6", 4);
            a.Lsls("r7", "r5", 4); a.AddsR("r7", "r7", "r6"); a.DpRegW(Asm.OP_ADD, false, "r7", "r7", "r6", Asm.SH_LSL, 4);
            a.Lsls("r5", "r6", 1); a.Udiv("r7", "r7", "r5"); a.SubsI("r7", 8); a.Mov("lr", "r7");   // g
            a.Clz("r5", "r12"); a.SubsI("r5", 11);                                   // scale shift
            a.LslRW("r2", "r12", "r5"); a.Lsrs("r2", "r2", 9);                       // v2
            string[] bd = new string[] { "r1", "r3" };
            for (int i = 0; i < 2; i++)                                              // v1, v3: b, d <= c, so no upper limit
            {
                a.DpRegW(Asm.OP_SUB, true, bd[i], bd[i], "r11", Asm.SH_LSL, 0); a.It("lt"); a.Movs(bd[i], 0);
                a.LslsR(bd[i], "r5"); a.Lsrs(bd[i], bd[i], 9);
            }
            a.LdrSp("r4", 44);
            string[] ae = new string[] { "r0", "r4" };
            for (int i = 0; i < 2; i++)                                              // v0, v4: limited to 0..vc
            {
                a.DpRegW(Asm.OP_SUB, true, ae[i], ae[i], "r11", Asm.SH_LSL, 0); a.It("lt"); a.Movs(ae[i], 0);
                a.CmpR(ae[i], "r12"); a.It("gt"); a.Mov(ae[i], "r12");
                a.LslsR(ae[i], "r5"); a.Lsrs(ae[i], ae[i], 9);
            }
            // which outer lines have a coil: bit 1 j = 0, 2 j = 1, 8 j = 3, 16 j = 4 (coil cc + j - 2 in 1..N)
            a.LdrSp("r7", 48);
            a.DpImmW(Asm.OP_ORR, false, "r12", "pc", 0);
            int[] dj = new int[] { -3, -2, 0, 1 }, bit = new int[] { 1, 2, 8, 16 };
            for (int i = 0; i < 4; i++)
            {
                if (dj[i] < 0) { a.SubsI3("r6", "r7", -dj[i]); a.CmpR("r6", "r10"); }
                else if (dj[i] == 0) a.CmpR("r7", "r10");
                else { a.AddsI3("r6", "r7", dj[i]); a.CmpR("r6", "r10"); }
                a.It("cc"); a.DpImmW(Asm.OP_ORR, false, "r12", "r12", bit[i]);
            }
            // two steps: v0..v4 = r0..r4, sums r5..r9, table pointer r10, table word r11, line mask r12, grid point lr
            for (int k = 1; k <= 2; k++)
            {
                a.Mov32("r10", tab + 4 * (UMAX - 32)); a.DpRegW(Asm.OP_SUB, false, "r10", "r10", "lr", Asm.SH_LSL, 2);
                EmitSums(a, "mc_" + k);
                EmitSolve(a, "mc_f" + k);
                if (k == 1)
                {
                    a.StrW("r11", "sp", 0);                                          // x after step 1
                    a.Movw("r10", 1030); a.MulW("lr", "r11", "r10");                 // the next grid point
                    a.DpImmW(Asm.OP_ADD, false, "lr", "lr", 32768); a.AsrW("lr", "lr", 16, false);
                }
            }
            a.B("mc_res");
            a.Label("mc_f1");                                                        // step 1 failed: the plain parabola
            a.LdrSp("r5", 0); a.LdrSp("r6", 4); a.Movw("r10", W); a.MulW("r5", "r5", "r10");
            a.Lsls("r6", "r6", 1); a.Sdiv("r11", "r5", "r6"); a.B("mc_res");
            a.Label("mc_f2");                                                        // step 2 failed: step 1's result
            a.LdrW("r11", "sp", 0);
            a.Label("mc_res");
            a.LdrSp("r5", 48); a.Movw("r10", W); a.Mla("r0", "r5", "r10", "r11");   // cc W + x
            a.AddSp(8);
            a.PopW(new string[] { "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11", "pc" }, false);
        }

        // the five sums at the grid point whose table words start at r10 (line j at +64 j); the centre line first
        static void EmitSums(Asm a, string p)
        {
            a.LdrW("r11", "r10", 128);
            a.Smla("r5", "r11", true, "r11", true, null);                            // stt = T T
            a.Smla("r6", "r2", false, "r11", true, null);                            // svt = v T
            a.Smla("r7", "r11", true, "r11", false, null);                           // ste = T E
            a.Smla("r8", "r11", false, "r11", false, null);                          // see = E E
            a.Smla("r9", "r2", false, "r11", false, null);                           // sve = v E
            int[] js = new int[] { 0, 1, 3, 4 }, bit = new int[] { 1, 2, 8, 16 };
            string[] vr = new string[] { "r0", "r1", "r3", "r4" };
            for (int i = 0; i < 4; i++)
            {
                string skip = p + "_n" + js[i];
                a.DpImmW(Asm.OP_AND, true, "pc", "r12", bit[i]); a.Bcc("eq", skip);  // no coil there
                a.LdrW("r11", "r10", 64 * js[i]);
                a.Smla("r5", "r11", true, "r11", true, "r5");
                a.Smla("r6", vr[i], false, "r11", true, "r6");
                a.Smla("r7", "r11", true, "r11", false, "r7");
                a.Smla("r8", "r11", false, "r11", false, "r8");
                a.Smla("r9", vr[i], false, "r11", false, "r9");
                a.Label(skip);
            }
        }

        // one step's solve from the sums: x (raw units from the centre coil) in r11, or to `fail`
        static void EmitSolve(Asm a, string fail)
        {
            a.Lsrs("r5", "r5", 12);                                                  // s12 = stt >> 12
            a.Udiv("r6", "r6", "r5");                                                // A = svt / s12
            a.Sdiv("r5", "r7", "r5");                                                // q = ste / s12
            a.Asrs("r7", "r7", 12);                                                  // t = ste >> 12
            a.Mls("r9", "r6", "r7", "r9");                                           // num = sve - A t
            a.Mls("r8", "r5", "r7", "r8");                                           // pn = see - q t
            a.CmpIW("r8", 0); a.BccW("le", fail);
            a.AsrW("r8", "r8", 6, false); a.MulW("r8", "r6", "r8"); a.AsrW("r8", "r8", 9, true);   // P = A (pn >> 6) >> 9
            a.BccW("le", fail);                                                      // (V is clear from the compare)
            a.AsrW("r9", "r9", 6, false); a.Movw("r10", W); a.MulW("r9", "r9", "r10"); a.Sdiv("r9", "r9", "r8");   // dx
            a.Movw("r11", LIM);
            a.CmpR("r9", "r11"); a.It("gt"); a.Mov("r9", "r11");
            a.CmnRW("r9", "r11"); a.It("lt"); a.DpImmW(Asm.OP_RSB, false, "r9", "r11", 0);
            a.MulW("r11", "lr", "r10"); a.DpImmW(Asm.OP_ADD, false, "r11", "r11", 8);
            a.DpRegW(Asm.OP_ADD, false, "r11", "r9", "r11", Asm.SH_ASR, 4);           // x = dx + ((g W + 8) >> 4)
        }

        // ---- digital twin: is Multi-coil Read installed in this image, and does it compute what Calc computes?
        public static bool Installed(byte[] flash)
        {
            Layout ly = Twin.LayoutOf(flash);
            int site = ly.A(0x080083BE), fn = ly.A(0x08005432), o = site - 0x08000000;
            if (o < 0 || o + 4 > flash.Length) return false;
            byte[] b = new byte[] { flash[o], flash[o + 1], flash[o + 2], flash[o + 3] };
            return Asm.IsBl(b) && Asm.DecBl(site, b) != fn && !WindowScale.IsWrapper(flash, Asm.DecBl(site, b));
        }

        static int CallTarget(byte[] flash, int site)
        {
            int o = site - 0x08000000;
            return Asm.DecBl(site, new byte[] { flash[o], flash[o + 1], flash[o + 2], flash[o + 3] });
        }

        // The pen sweeps across the coils at each height (the twin's own noise). At every position call the arguments,
        // peak line and reads are captured; Calc says what should happen. Where it gives a position, the firmware's
        // result must equal it and the stock routine must not run; where it says STOCK, the stock routine must start
        // with the caller's own registers, stack and return address. Also the costliest frame (X and Y calls, with
        // whatever interrupts landed in them), cycles. Works on a build without Multi-coil Read too (cost only).
        public static McResult TwinCheck(byte[] flash, double[] heights, int settleMs, int ms)
        {
            McResult res = new McResult();
            Layout ly = Twin.LayoutOf(flash);
            int cx = ly.A(0x080083BE), cy = ly.A(0x0800840A), fn = ly.A(0x08005432);
            bool inst = Installed(flash), full = Patches.FullResIn(flash);
            for (int hi = 0; hi < heights.Length; hi++)
            {
                Pen pen = new Pen(11 + hi);
                pen.x = 9.3; pen.y = 6.6; pen.f0 = 505000; pen.Q = 150; pen.strength = 60000;
                pen.hasHeight = true; pen.height = heights[hi]; pen.resonator = true; pen.integrate = true;
                McTwinBoard b = new McTwinBoard(pen, res);
                b.installed = inst; b.full = full; b.retX = cx + 4; b.retY = cy + 4; b.ws = WindowScale.Installed(flash); b.wsLab = b.ws && Lab.WsGated(flash); b.cg = CoilGains.Installed(flash);
                Machine mc = new Machine(flash, true, b, false);
                mc.SetHook(cx, McTwinBoard.H_CX); mc.SetHook(cx + 4, McTwinBoard.H_RX); mc.SetHook(cy, McTwinBoard.H_CY); mc.SetHook(cy + 4, McTwinBoard.H_RY);
                mc.SetHook(fn, McTwinBoard.H_ST);
                mc.Boot(3000000); mc.AttachUsb(true);
                for (int t = 0; t < settleMs; t++) mc.RunMs(1);
                b.from = mc.cpu.cycles;
                for (int t = 0; t < ms; t++) mc.RunMs(1);
            }
            return res;
        }

        // one call of fn as the firmware makes it, single-stepped with no interrupts; returns its cycles
        static long Run(Machine mc, int fn, int[] L, int cc, int axis, int pk, int samples, int agcDelay, out int result)
        {
            Cpu cpu = mc.cpu;
            int sp = 0x20001600;
            mc.Poke((axis == 0 ? Sched.XBUF : Sched.YBUF) + 108, 1, pk);
            mc.Poke(Sched.S_ADDR + 5, 1, samples);
            mc.Poke(Sched.AGC + 1 + axis, 1, agcDelay);
            cpu.r[0] = L[0]; cpu.r[1] = L[1]; cpu.r[2] = L[2]; cpu.r[3] = L[3]; cpu.r[13] = sp;
            mc.Poke(sp, 4, L[4]); mc.Poke(sp + 4, 4, cc); mc.Poke(sp + 8, 4, W);
            cpu.r[14] = 0x0800FFF1; cpu.r[15] = fn & ~1;
            long c0 = cpu.cycles;
            for (int n = 0; n < 100000 && cpu.r[15] != 0x0800FFF0; n++) cpu.Step();
            result = cpu.r[0];
            if (cpu.r[15] != 0x0800FFF0 || cpu.r[13] != sp) throw new Stop("internal: a position call did not return cleanly");
            return cpu.cycles - c0;
        }

        // Cycles of one position call with and without Multi-coil Read, on the same inputs: real-looking lines at
        // every reads count, the peak on and off the centre line, weak and strong signals, every coil (edges too),
        // neighbours equal to or above the centre line. For each input both the stock routine and the build's entry
        // run; gateMax = the most the entry adds where it hands on to the stock routine. Each result is checked too:
        // Calc's where the fit applies, the stock routine's (run just before on the same input) where it does not.
        public static McCost Cost(byte[] flash)
        {
            McCost r = new McCost();
            Layout ly = Twin.LayoutOf(flash);
            int fn = ly.A(0x08005432);
            bool full = Patches.FullResIn(flash);
            int[] entry = new int[] { CallTarget(flash, ly.A(0x080083BE)), CallTarget(flash, ly.A(0x0800840A)) };
            Machine mc = new Machine(flash, true, new TabletBoard(new Pen(1)), false);
            Random rng = new Random(7);
            long sumS = 0, sumE = 0;
            int[] L = new int[5];
            for (int t = 0; t < 6000; t++)
            {
                int axis = t & 1, nc = axis == 0 ? X_COILS : Y_COILS;
                int samples = 4 + rng.Next(7), agcDelay = rng.Next(samples - 1), reads = samples - agcDelay;
                int fl = Floor(reads, full), kind = (t >> 1) % 6;
                int pk = kind == 0 ? 1 + rng.Next(5) : 3;
                int sig = kind == 1 ? rng.Next(12) : 20 + rng.Next(t % 3 == 0 ? 200 : 9000);
                int c = fl + sig;
                L[2] = c; L[1] = fl + rng.Next(sig + 1); L[3] = fl + rng.Next(sig + 1);
                L[0] = fl + rng.Next(L[1] - fl + 1); L[4] = fl + rng.Next(L[3] - fl + 1);
                if (kind == 2) { L[1] = c; L[3] = c; }
                if (kind == 3) L[3] = c + 1 + rng.Next(50);
                if (kind == 4) { L[0] = rng.Next(c + 1); L[4] = rng.Next(c + 1); }
                int cc = 1 + rng.Next(nc);
                if (t % 7 == 0) cc = (t % 2 == 0) ? 1 + rng.Next(2) : nc - rng.Next(2);
                int rs, re;
                long cs = Run(mc, fn, L, cc, axis, pk, samples, agcDelay, out rs);
                long ce = Run(mc, entry[axis], L, cc, axis, pk, samples, agcDelay, out re);
                int want = Calc(L[0], L[1], L[2], L[3], L[4], cc, pk, reads, axis, full);
                // (a build without Multi-coil Read: its entry is the stock routine, so it must give the stock result)
                bool fit = want != STOCK && entry[axis] != fn;
                if (fit) r.fit++; else want = rs;
                if (re != want)
                {
                    r.miss++;
                    if (r.firstMiss == null)
                        r.firstMiss = (axis == 0 ? "X" : "Y") + " lines " + L[0] + "," + L[1] + "," + L[2] + "," + L[3] + "," + L[4] + " centre " + cc + " peak " + pk
                            + " reads " + reads + ": firmware " + re + ", " + (fit ? "C# " : "stock routine ") + want;
                }
                r.n++; sumS += cs; sumE += ce;
                if (cs > r.stockMax) r.stockMax = cs;
                if (ce > r.entryMax) { r.entryMax = ce; r.worst = (fit ? "fit" : "stock") + " path, lines " + L[0] + "," + L[1] + "," + L[2] + "," + L[3] + "," + L[4] + " centre " + cc + " peak " + pk + " reads " + reads; }
                if (fit) { if (ce > r.fitMax) r.fitMax = ce; }
                else if (ce - cs > r.gateMax) r.gateMax = ce - cs;
            }
            r.stockMean = (double)sumS / r.n; r.entryMean = (double)sumE / r.n;
            return r;
        }
    }
}

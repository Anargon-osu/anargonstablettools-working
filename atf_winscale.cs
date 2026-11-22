// atf_winscale.cs (R45-rc4 (2)) - Window Scale: the position inside a coil window, corrected for the pen's pressure.
//
// Why (Anargon: "X will not report position consistently"; target: 0.1 mm true position error). The tablet works
// out the position from five coils around the pen (a window) and moves the window by one coil when the pen passes
// the middle between two coils. At that switch X jumped 0.24 mm (median, 90 % 0.46 mm) and Y 0.09 mm on Anargon's
// tablet (rev-output/hw45/drag/drag45.csv, 45-degree drags). The jumps are not noise: at each switch the position
// inside the old window and inside the new one are both off by a fraction of their distance from the window's
// centre coil, as if the window were stretched or squeezed. A stretch k makes every switch jump by k coils along the
// switch, whichever way the pen goes. k follows the pen's frequency, which the pen sets from its pressure: on X
// about +0.043 below pressure 10000 and about -0.047 above (the cold first reading, 16.3 of history/claude-rev45.md,
// flips sign there), on Y smaller (-0.03 .. +0.09). Tested offline on the drags: guessing the cold reading's gain,
// leaving it out of the arithmetic, a Gaussian peak fit and a gain on the centre coil all made the jumps the same or
// worse (experiments/noise45/gainfix.py); a stretch per pressure did not (scalelearn.py).
//
// What: after the stock position routine, position = r + (r - centre x 1018) x k(p) / 4096, with r its answer
// (1018 units per coil), centre the window's centre coil and k(p) from 9 knots at pressure 0, 2048, ..., 16384,
// straight between them, so a pressure change never makes a step. Hover (pressure 0) uses the lightest touch's
// value: the drags' only hover switches agree (X +0.068 from one switch, Y -0.011 from three). Only at the fast step
// (hover stage VB[0] = 0: with Steady Hover, its ladder step 0): the slower steps read longer and were not measured. Nothing
// is remembered from frame to frame, so a still pen cannot drift. Offline, all drags: switch jumps X 0.238 -> 0.083
// mm (median), 0.463 -> 0.279 (90 %); Y 0.091 -> 0.050, 0.228 -> 0.218. Fitted on half the drags and tested on the
// other half (bins, no knots): X 0.24 -> 0.15-0.17. With Switch Match after it: X 0.037 / Y 0.036 mm.
//
// Firmware: the two calls of the stock position routine (X 0x080083BE, Y 0x0800840A; the same calls Multi-coil Read
// takes over, so the two never run together) go to ws_x / ws_y with its arguments: r0..r3 = lines centre-2..
// centre+1, [sp] = centre+2, [sp+4] = centre coil, [sp+8] = 1018. They copy the stack arguments, call the stock
// routine, then correct its answer. The table: X knots then Y knots, 9 signed halfwords each (Q12).
namespace AnargonsTabletTools
{
    public static class WindowScale
    {
        // knots at pressure 0, 2048, ..., 16384 (Q12: 4096 = 1): from experiments/noise45/scalelearn.py --table,
        // the medians of jump / W per 2048-wide pressure bin, each knot the mean of the two bins either side
        public static readonly int[] KX = new int[] { 176, 176, 178, 190, 180, -16, -150, -150, -196 };
        public static readonly int[] KY = new int[] { -32, -32, 22, 83, 50, 1, 7, -53, -127 };
        public const int NK = 9, PMAX = 16383;
        static string[] Rg(params string[] r) { return r; }

        public static byte[] Table()
        {
            byte[] t = new byte[4 * NK];
            for (int i = 0; i < NK; i++)
            {
                int x = KX[i] & 0xFFFF, y = KY[i] & 0xFFFF;
                t[2 * i] = (byte)(x & 0xFF); t[2 * i + 1] = (byte)(x >> 8);
                t[2 * NK + 2 * i] = (byte)(y & 0xFF); t[2 * NK + 2 * i + 1] = (byte)(y >> 8);
            }
            return t;
        }

        // C# copy: r = the stock routine's answer, cc = centre coil, p = pressure (PRESSURE+6), stage = VB[0]
        public static int K(int q, int p)
        {
            int[] k = q == 0 ? KX : KY;
            if (p < 0) p = 0;
            if (p > PMAX) p = PMAX;
            int i = p >> 11;
            return k[i] + (((k[i + 1] - k[i]) * (p & 2047)) >> 11);
        }
        public static int Apply(int q, int r, int cc, int p, int stage)
        {
            if (stage != 0) return r;
            return r + (((r - cc * 1018) * K(q, p)) >> 12);
        }

        // ws_x / ws_y in place of the stock routine's two calls; stockFn = the stock routine
        public static void Emit(Asm a, int tab, int stockFn) { Emit(a, tab, stockFn, false); }
        // lab: the Filter Arranger build, where a RAM flag switches the correction off (atf_lab.cs)
        public static void Emit(Asm a, int tab, int stockFn, bool lab) { Emit(a, tab, stockFn, lab, 0, false); }
        // cgtab: Coil Gains' table (atf_coilgain.cs), 0 = none; full: 12-bit Readings (its floor)
        public static void Emit(Asm a, int tab, int stockFn, bool lab, int cgtab, bool full)
        {
            for (int q = 0; q < 2; q++)
            {
                string s = q == 0 ? "x" : "y";
                a.Label("ws_" + s);
                a.Push(Rg("r4", "r5", "r6"), true);                                   // 16 bytes with lr
                a.SubSp(16);
                a.LdrSp("r4", 32); a.StrSp("r4", 0);                                   // line centre+2
                a.LdrSp("r4", 36); a.StrSp("r4", 4);                                   // centre coil
                a.LdrSp("r4", 40); a.StrSp("r4", 8);                                   // 1018
                if (cgtab != 0) CoilGains.EmitScale(a, cgtab, q, full);              // each coil's gain matched
                a.BlA(stockFn);
                a.AddSp(16);
                a.Mov32("r6", tab + 2 * NK * q);
                a.B("ws_k");
            }
            // r0 = the stock answer, r6 = this axis's knots; the caller's centre coil at [sp+20], 1018 at [sp+24]
            a.Label("ws_k");
            a.Mov32("r1", Sched.VB_ADDR); a.Ldrb("r1", "r1", 0);
            a.Cbnz("r1", "ws_e");                                                      // the fast stage only
            if (lab) Lab.EmitWsGate(a, "ws_e");
            a.Mov32("r1", Sched.PRESSURE); a.Ldrh("r1", "r1", 6);
            a.Movw("r2", PMAX); a.CmpR("r1", "r2"); a.Bcc("ls", "ws_p"); a.Mov("r1", "r2");
            a.Label("ws_p");
            a.Lsrs("r2", "r1", 11); a.Lsls("r2", "r2", 1); a.AddsR("r2", "r2", "r6");
            a.Ldrh("r3", "r2", 0); a.Sxth("r3", "r3");                                // K[i]
            a.Ldrh("r4", "r2", 2); a.Sxth("r4", "r4"); a.SubsR("r4", "r4", "r3");     // K[i+1] - K[i]
            a.Lsls("r1", "r1", 21); a.Lsrs("r1", "r1", 21);                           // p & 2047
            a.Muls("r4", "r1"); a.Asrs("r4", "r4", 11); a.AddsR("r3", "r3", "r4");    // k
            a.LdrSp("r1", 20); a.LdrSp("r2", 24); a.Muls("r1", "r2");                 // centre x 1018
            a.SubsR("r1", "r0", "r1"); a.Muls("r1", "r3"); a.Asrs("r1", "r1", 12); a.AddsR("r0", "r0", "r1");
            a.Label("ws_e");
            a.Pop(Rg("r4", "r5", "r6"), true);
        }

        // ---- digital twin: does the X position call go to Window Scale? (its entry: push {r4, r5, r6, lr})
        public const int ENTRY = 0xB570;
        public static bool IsWrapper(byte[] flash, int target)
        {
            int o = (target & ~1) - 0x08000000;
            return o >= 0 && o + 2 <= flash.Length && (flash[o] | (flash[o + 1] << 8)) == ENTRY;
        }
        public static bool Installed(byte[] flash)
        {
            Layout ly = Twin.LayoutOf(flash);
            int site = ly.A(0x080083BE), fn = ly.A(0x08005432), o = site - 0x08000000;
            if (o < 0 || o + 4 > flash.Length) return false;
            byte[] b = new byte[] { flash[o], flash[o + 1], flash[o + 2], flash[o + 3] };
            if (!Asm.IsBl(b)) return false;
            int t = Asm.DecBl(site, b);
            return t != fn && IsWrapper(flash, t);
        }
    }
}

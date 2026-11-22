// atf_coilgain.cs (PR1) - Coil Gains: each coil's own reading strength, matched before the position.
//
// Why (Anargon, 2026-11-22: slanted lines keep 0.04-0.07 mm of error, up to 0.45 mm peak to peak, that repeats coil by
// coil). The tablet works out the position inside a coil window from five line sums (the stock routine 0x08005432,
// MultiCoil.Stock). That arithmetic assumes every coil reads the pen equally strongly. On Anargon's tablet they do not:
// fitted on the R43 strokes' raw line sums (referencedata/strokes-r43, pen touching; experiments/precision/coilgain2.py,
// per frame the pen offset and signal, per coil a gain, per window line a gain, one shared response shape), the X
// coils differ by 1.2 % sd and the Y coils by 0.7 %, the same in every recording (X gain r 0.94 between the two ULTRA
// recordings, 0.81-0.94 against RAPID; Y 0.83). On X a high / low pair comes back every 8 coils (10 / 11, 18 / 19,
// 26 / 27). A coil reading 1 % high pulls the position in the windows next to it by about 0.03 mm.
// Those gains alone (measured on 2026-11-09) predict the slanted-line error of 2026-11-13, 11-16 and 11-17 at
// r 0.46-0.80 (six lines, three days, other builds); taking the prediction off: Part A -26 deg 0.054 -> 0.037 mm sd,
// D3 +26 deg 0.063 -> 0.043, 33 deg 0.073 -> 0.056, -34 deg 0.173 -> 0.132, -10 deg 0.034 -> 0.027 (the measured sd
// includes the 0.02-0.04 mm measurement floor). Per-coil centres and effective ranges were fitted too and predicted
// nothing. The part shared by every coil changes by 0.004 mm, so Coil Linearization's table stays as it is.
//
// What: per axis, on the five line sums the stock routine gets (r0..r3 = centre-2..centre+1, [sp] = centre+2), before
// it runs: line = ((line - floor) x G_k) >> 8, k = that line's coil, G_k = 4096 / gain (Q12; 4096 on coils that were
// not measured: X 1, 2, 31, 32 and Y 1, 2, 18, 19), floor = Multi-coil Read's (FLOOR_A x reads + FLOOR_B, reads =
// samples - the axis's AGC delay). The stock arithmetic only uses differences of lines and their ratios, so taking the
// floor out and scaling by 16 changes nothing but the gain (and keeps 4 bits more of each line). Only at the fast
// stage (VB[0] = 0, where the strokes were measured), with a centre coil 0..N and at least one read. Nothing is
// remembered between frames. Inside Window Scale's wrapper (ws_x / ws_y), so ULTRA only; not in the Filter Arranger
// build. Cost: about 30 instructions per axis.
// Setting "Coil Gains" (key cgain), one of the Misc. Optimizations (Anargon: "That goes into misc options."), so on by
// default at ULTRA with Window Scale; --no-coil-gains leaves it out. One tablet and pen (Anargon's), measured at
// ULTRA, pen touching.
//
// Strength (tablet test 2026-11-22, rev-output/hw45/coilgain/ab): at full strength the firmware took off exactly the
// predicted error (with - without, on one line: r -0.87 against the prediction), but that day the tablet showed only
// about half of it (0.0-0.6 x on four lines), so the lines came out over-corrected (0.047 -> 0.052 mm). On 2026-11-13,
// 11-16 and 11-17 the lines showed 0.7-2.4 x. Not explained by pressure (gains per pressure band, coilpk.py), a filter
// lag between the two pass directions (coildir.py), pen tilt (clamped in every recording) or where along the coil the
// pen is (coilzone.py: X r 0.90-0.96 between three zones); gains learned from the 11-13 / 16 / 17 ruler lines match
// the strokes' (X r 0.92, Y r 0.84, coilruler.py), today's do not. So the table is corrected at STRENGTH per mille
// of the measured difference: 60 % lowered the slanted-line error of every recorded session (coilstrength.py,
// offline: 11-13 -9 %, 11-16 -15 %, 11-17 -22 %, 11-22 -9 %), 100 % made 11-22's 7 % worse. On the tablet (same
// afternoon, new ruler line, ab60) 60 % took the error that repeats in both directions from 0.021 to 0.009 mm and
// applied 0.65 x the prediction. In 0.1 % steps over every line, 2026-11-22's included (coiltune.py), 58.9 % keeps
// the worst session lowest (each session 0.78-0.91 of its error without Coil Gains, none worse); the lowest mean is
// at 78.6 % but leaves 2026-11-22 at 0.97, and with that session held out it would have been made worse (1.08).
// Separate X / Y strengths (84 / 65 %) gain 0.002 of the mean: one strength for both.
using System;

namespace AnargonsTabletTools
{
    public static class CoilGains
    {
        public const int STRENGTH = 589;          // per mille of the measured difference that is corrected (see above)
        public const int NX = 32, NY = 19, SLOTS = 40;   // table: per axis 40 halfwords, slot k + 2 for coil k (-2..37)
        // measured G_k = 4096 / gain (full strength), coils 1..N: ULTRA, mean of the ultra-h1 + v1 and ultra-h2 + v2
        // fits (coilgain3.py, "U inner")
        public static readonly int[] GX = new int[] { 4096, 4096, 4173, 4041, 4126, 4070, 4093, 4096, 4116, 4011, 4196,
            4038, 4122, 4059, 4121, 4103, 4118, 4038, 4172, 4039, 4130, 4057, 4102, 4108, 4102, 4033, 4169, 4051, 4148, 4071,
            4096, 4096 };
        public static readonly int[] GY = new int[] { 4096, 4096, 4110, 4111, 4068, 4154, 4061, 4077, 4102, 4122, 4062,
            4099, 4066, 4142, 4068, 4118, 4083, 4096, 4096 };

        public static int Measured(int q, int k)
        {
            int[] g = q == 0 ? GX : GY;
            return k >= 1 && k <= g.Length ? g[k - 1] : 4096;
        }

        // the firmware's G_k: gain' = 1 + STRENGTH per mille of (gain - 1), G_k = 4096 / gain' (rounded)
        public static int G(int q, int k)
        {
            int m = Measured(q, k);
            int g = (4096 * 4096 + m / 2) / m;                       // the measured gain, Q12
            int gs = 4096 + (g - 4096) * STRENGTH / 1000;
            return (4096 * 4096 + gs / 2) / gs;
        }

        public static byte[] Table()
        {
            byte[] t = new byte[4 * SLOTS];
            for (int q = 0; q < 2; q++)
                for (int s = 0; s < SLOTS; s++)
                {
                    int v = G(q, s - 2);
                    t[2 * (SLOTS * q + s)] = (byte)(v & 0xFF); t[2 * (SLOTS * q + s) + 1] = (byte)(v >> 8);
                }
            return t;
        }

        // C# copy: the five lines as the stock routine gets them (a..e), centre coil cc, reads, 12-bit Readings, VB[0]
        public static int[] Scale(int q, int[] L, int cc, int reads, bool full, int stage)
        {
            int[] o = new int[] { L[0], L[1], L[2], L[3], L[4] };
            if (stage != 0) return o;
            if (cc < 0 || cc > (q == 0 ? NX : NY)) return o;                          // the firmware: unsigned compare
            if (reads <= 0) return o;
            int f = MultiCoil.FloorA(full) * reads + MultiCoil.FloorB(full);
            for (int j = 0; j < 5; j++) o[j] = ((L[j] - f) * G(q, cc + j - 2)) >> 8;
            return o;
        }

        // inside ws_x / ws_y, after the stack arguments were copied to [sp], [sp+4], [sp+8] and before the stock
        // routine is called: r0..r3 and [sp] scaled; uses r4, r5, r6 (saved by the wrapper); the caller's centre coil
        // at [sp+36]
        public static void EmitScale(Asm a, int tab, int q, bool full)
        {
            string e = "cg_e" + (q == 0 ? "x" : "y");
            int offS5 = Sched.S_ADDR + 5 - Sched.AGC;
            a.Mov32("r5", Sched.VB_ADDR); a.Ldrb("r5", "r5", 0);
            a.Cbnz("r5", e);                                                             // the fast stage only
            a.LdrSp("r4", 36);                                                           // centre coil
            a.CmpI("r4", q == 0 ? NX : NY); a.Bcc("hi", e);
            a.Mov32("r5", Sched.AGC); a.LdrbW("r6", "r5", offS5); a.Ldrb("r5", "r5", 1 + q);
            a.SubsR("r6", "r6", "r5"); a.Bcc("le", e);                                   // reads = samples - AGC delay
            a.Movs("r5", MultiCoil.FloorA(full)); a.Muls("r6", "r5"); a.SubsI("r6", -MultiCoil.FloorB(full));   // floor
            a.Mov32("r5", tab + 2 * SLOTS * q); a.AddsR("r5", "r5", "r4"); a.AddsR("r5", "r5", "r4");   // slot of coil cc - 2
            string[] lr = new string[] { "r0", "r1", "r2", "r3" };
            for (int j = 0; j < 4; j++)
            {
                a.Ldrh("r4", "r5", 2 * j); a.SubsR(lr[j], lr[j], "r6"); a.Muls(lr[j], "r4"); a.Asrs(lr[j], lr[j], 8);
            }
            a.LdrSp("r4", 0); a.SubsR("r4", "r4", "r6"); a.Ldrh("r6", "r5", 8); a.Muls("r4", "r6"); a.Asrs("r4", "r4", 8);
            a.StrSp("r4", 0);
            a.Label(e);
        }

        // ---- digital twin: does the X position wrapper scale the lines? (after push, sub sp and the three copies, the
        // wrapper without Coil Gains calls the stock routine at once)
        public static bool Installed(byte[] flash)
        {
            if (!WindowScale.Installed(flash)) return false;
            Layout ly = Twin.LayoutOf(flash);
            int site = ly.A(0x080083BE), o = site - 0x08000000;
            int t = Asm.DecBl(site, new byte[] { flash[o], flash[o + 1], flash[o + 2], flash[o + 3] }) & ~1;
            int p = t + 16 - 0x08000000;
            if (p < 0 || p + 4 > flash.Length) return false;
            return !Asm.IsBl(new byte[] { flash[p], flash[p + 1], flash[p + 2], flash[p + 3] });
        }
    }
}

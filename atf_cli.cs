// atf_cli.cs - screens, digital-twin gate, tablet calibration, flashing, wizard and command line of AnargonsTabletTools.exe.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class DescribeJob : Job
    {
        public byte[] img; public string label; public Out o = new Out(); public bool boot; public int agc, binterval;
        public override void Run() { o.buf = new StringBuilder(); boot = Twin.Describe(img, label, o, binterval); agc = boot ? Twin.AgcCheck(img, o) : 0; }
    }

    // one build's averaged cursor delay (and, for the settings check, its frame timing), run in parallel with others
    public class LatencyJob : Job
    {
        public byte[] img; public bool frames, hover = true; public FrameStats fs; public Latency lat;
        public override void Run()
        {
            if (img == null) return;
            if (frames) fs = Twin.MeasureFrames(img, 120, true);
            lat = Twin.MeasureDelay(img, true, hover);
        }
    }

    // R43: Multi-coil Read's routine against its C# copy, on a pen swept across the coils at touch and at 5 mm
    public class McJob : Job
    {
        public byte[] img; public McResult r; public bool ws;      // ws: R45-rc4 (2) Window Scale instead of Multi-coil Read
        public override void Run() { r = MultiCoil.TwinCheck(img, new double[] { 0, 5 }, 300, 400); }
    }

    // R44: APIP's filter against its C# copy (a pen sliding and turning at touch and hover), and the training
    // firmware's read-out (USB string 180 against the positions the reports carried)
    public class ApJob : Job
    {
        public byte[] img; public ApipCheck r;
        public override void Run() { r = Apip.TwinCheck(img, 600); }
    }
    public class ApTrainJob : Job
    {
        public byte[] img; public TrainCheckResult r;
        public override void Run() { r = Apip.TrainCheck(img); }
    }

    public class SensJobT : Job
    {
        public byte[] img; public double f; public bool rt; public SensResult r;
        public override void Run() { r = Twin.SensJob(img, f, rt); }
    }

    // ------------------------------------------------------------------------------------ screens
    public static class Screen
    {
        public static StrList warnings = new StrList();       // listed below the prompt on every screen
        public static bool wipe = true;

        public static void Warn(string text) { for (int i = 0; i < warnings.Count(); i++) if (P.Eq(warnings.Get(i), text)) return; warnings.Add(text); }
        // a warning shown only once, ever: after the first run that shows it, a flag in the hidden PC store suppresses
        // it on every later run (for informational notices like "no tablet dump found", not for safety warnings).
        public static void WarnOnce(string key, string text) { if (Store.WarnedOnce(key)) return; Store.MarkWarned(key); Warn(text); }
        public static void SetWarn(string prefix, string text)
        {
            // R44: replaced where it was, so the warnings keep one order on every step
            StrList n = new StrList();
            bool placed = false;
            for (int i = 0; i < warnings.Count(); i++)
            {
                if (!P.StartsWith(warnings.Get(i), prefix)) n.Add(warnings.Get(i));
                else if (!placed) { n.Add(text); placed = true; }
            }
            if (!placed) n.Add(text);
            warnings = n;
        }

        public static string Rule(char c, int n) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(c); return sb.ToString(); }

        // a boxed notice; a paragraph that starts with spaces keeps that indent
        public static void Box(string title, string text)
        {
            int w = 76;
            P.Line("    " + Rule('#', w));
            P.Line("    #" + Rule(' ', w - 2) + "#");
            P.Line("    #    " + Txt.PadR(title, w - 6) + "#");
            P.Line("    #" + Rule(' ', w - 2) + "#");
            string[] paras = P.Split(text, '\n');
            for (int k = 0; k < paras.Length; k++)
            {
                int ind = 0;
                while (ind < P.Len(paras[k]) && P.CharAt(paras[k], ind) == ' ') ind++;
                StrList l = Txt.Wrap(P.Sub(paras[k], ind, P.Len(paras[k]) - ind), w - 14 - ind);
                if (l.Count() == 0) P.Line("    #" + Rule(' ', w - 2) + "#");
                for (int i = 0; i < l.Count(); i++) P.Line("    #        " + Rule(' ', ind) + Txt.PadR(l.Get(i), w - 10 - ind) + "#");
            }
            P.Line("    #" + Rule(' ', w - 2) + "#");
            P.Line("    " + Rule('#', w));
            P.Line("");
        }

        public static int WarnLines()
        {
            if (warnings.Count() == 0) return 0;
            int n = 2;
            for (int i = 0; i < warnings.Count(); i++) n += Txt.Wrap(warnings.Get(i), 88).Count() + 1;
            return n;
        }

        public static void ShowWarnings()
        {
            if (warnings.Count() == 0) return;
            P.Line("");
            P.Line("  Warnings:");
            for (int i = 0; i < warnings.Count(); i++)
            {
                StrList l = Txt.Wrap(warnings.Get(i), 88);
                for (int k = 0; k < l.Count(); k++) P.Line((k == 0 ? "    - " : "      ") + l.Get(k));
                P.Line("");
            }
        }

        // AI DISCLOSURE: required by the maintainer on every screen, at every step, in every revision.
        // NEVER remove, shorten, reword or hide it (AGENTS.md rule 12; tests/host-regressions.cs checks it).
        public static readonly string[] AI_DISCLOSURE = new string[] {
            "This program is vibecoded. There are basic protections,",
            "like emulation-based checking and firmware compatibility",
            "checking, but no amount of checks can guarantee a brick-free",
            "process.",
            "",
            "No human has also approved the codebase, so reliability is",
            "NOT GUARANTEED, however, several code and structure passes",
            "have been done to make this project... Kind of bearable to use.",
            "",
            "You hate it, I hate it, we all do, however, the one who is",
            "flashing this onto your tablet is you, and only you.",
            "Nobody else can be held liable for any issues",
            "caused from your attempt at flashing this." };

        public static void Disclosure()
        {
            int w = 76;
            P.Line("");
            P.Line("    " + Rule('#', w));
            P.Line("    #" + Rule(' ', w - 2) + "#");
            P.Line("    #    " + Txt.PadR("AI DISCLOSURE", w - 6) + "#");
            P.Line("    #" + Rule(' ', w - 2) + "#");
            for (int i = 0; i < AI_DISCLOSURE.Length; i++) P.Line("    #        " + Txt.PadR(AI_DISCLOSURE[i], w - 10) + "#");
            P.Line("    #" + Rule(' ', w - 2) + "#");
            P.Line("    " + Rule('#', w));
        }

        // R43 (Anargon): the screens are laid out for 96 columns. In a narrower window every long line wraps, so each
        // screen first waits until the window is wider than that: zoom out with Ctrl and - (or Ctrl + mouse wheel), or
        // widen the window. 97, not 96: some consoles add a blank line after a line that fills the whole width.
        public const int COLUMNS = 96;
        public static void FitWidth()
        {
            int w = P.WindowWidth();
            if (w < 0 || w > COLUMNS) return;
            P.Clear();
            P.Line("");
            P.Line("  This window is too narrow:");
            P.Line("  " + F.Str(w) + " columns, needs " + F.Str(COLUMNS + 1) + ".");
            P.Line("");
            P.Line("  Zoom out: press Ctrl and -");
            P.Line("  (or hold Ctrl and scroll down),");
            P.Line("  or make the window wider.");
            P.Line("");
            P.Line("  This continues by itself");
            P.Line("  once the window is wide enough.");
            while (true)
            {
                P.Sleep(0.25);
                int now = P.WindowWidth();
                if (now < 0 || now > COLUMNS) break;
            }
        }

        public static void Step(string title)
        {
            FitWidth();
            if (wipe) P.Clear();
            Disclosure();
            StartupBackup.Poll();      // the background dump never prints; its result is picked up here, between screens
            P.Line("");
            P.Line("  " + title);
            P.Line("  " + Rule('-', P.Len(title)));
            P.Line("");
        }

        // Keep terminal output forward-only: wrapping, scrolling and window resizing must not
        // move the input cursor back into an earlier warning or leave stale text behind.
        public static string Ask(string prompt)
        {
            ShowPrompt(prompt);
            return ReadAnswer();
        }
        // R45: Ask in two parts (show the prompt, then read the answer)
        public static void ShowPrompt(string prompt)
        {
            StartupBackup.Poll();
            ShowWarnings();
            P.Write(prompt);
        }
        public static string ReadAnswer()
        {
            string answer = P.ReadLine();
            if (P.InputEnded()) throw new Stop("Input ended. Run this command in a terminal to answer the prompts.");
            return answer;
        }

        public static void Para(string text, int indent)
        {
            StrList l = Txt.Wrap(text, 96 - indent);
            for (int i = 0; i < l.Count(); i++) P.Line(Rule(' ', indent) + l.Get(i));
        }

        // ANSI bold, only when the output is an interactive terminal (so piped output and the tests stay clean ASCII)
        public static string Bold(string s) { if (!P.StdinIsTty()) return s; string e = "" + (char)27; return e + "[1m" + s + e + "[0m"; }

        // a full-screen warning clears the regular screen and warning list,
        // and spells the message out between heavy rules, in bold. Returns the line the user types.
        public static string DangerAsk(StrList lines, string prompt)
        {
            FitWidth();
            P.Clear();
            Disclosure();
            string bar = Rule('#', 92);
            P.Line(""); P.Line("  " + bar); P.Line("  " + bar); P.Line("");
            for (int i = 0; i < lines.Count(); i++)
            {
                if (P.Len(lines.Get(i)) == 0) { P.Line(""); continue; }
                StrList w = Txt.Wrap(lines.Get(i), 84);
                for (int k = 0; k < w.Count(); k++) P.Line("    " + Bold(w.Get(k)));
            }
            P.Line(""); P.Line("  " + bar); P.Line("  " + bar); P.Line("");
            P.Write("    " + Bold(prompt));
            string answer = P.ReadLine();
            if (P.InputEnded()) throw new Stop("Input ended before this acknowledgement. Run the command in a terminal.");
            return answer;
        }
    }

    public static class Cli
    {
        // ---- digital-twin gate: boot, USB, AGC and pen detection at full PC speed (parallel), then measurements paced
        //      to the real 72 MHz chip
        public static string gateHeading = null;          // the measurement heading for the build being gated
        public static int Gate(string outpath, int binterval, bool truncated, bool realtime)
        {
            RunLog LOG = Build.LOG;
            Build.Say("");
            Build.Say("    Digital-twin check (" + (realtime ? "paced to the simulated 72 MHz clock" : "full PC speed") + ")");
            Build.Say("");
            double t0 = P.NowSec();
            byte[] img = Twin.LoadImage(outpath);
            string mh = Twin.MissingHooks(img);
            if (mh != null)
            {
                Build.Say("        The twin cannot find this firmware's coil and timing code (" + mh + "), so it cannot check");
                Build.Say("        this build. Unchecked builds are never offered for flashing.");
                Build.Say(""); Build.Say("    The build FAILED the digital-twin check.");
                return 0;
            }
            DescribeJob d = new DescribeJob(); d.img = img; d.label = outpath; d.binterval = binterval;
            McJob mj = null;
            try { if (MultiCoil.Installed(img) || WindowScale.Installed(img)) { mj = new McJob(); mj.img = img; mj.ws = WindowScale.Installed(img); } }
            catch (Stop) { mj = null; }
            ApJob aj = null; ApTrainJob tj = null;
            if (Apip.FindTable(img) >= 0) { aj = new ApJob(); aj.img = img; }
            if (Apip.IsTraining(img)) { tj = new ApTrainJob(); tj.img = img; }
            Job[] jobs = new Job[1 + Twin.SENS_FREQS.Length + (mj != null ? 1 : 0) + (aj != null ? 1 : 0) + (tj != null ? 1 : 0)];
            jobs[0] = d;
            SensJobT[] sj = new SensJobT[Twin.SENS_FREQS.Length];
            for (int i = 0; i < sj.Length; i++) { sj[i] = new SensJobT(); sj[i].img = img; sj[i].f = Twin.SENS_FREQS[i]; sj[i].rt = realtime; jobs[1 + i] = sj[i]; }
            int nj = 1 + sj.Length;
            if (mj != null) jobs[nj++] = mj;
            if (aj != null) jobs[nj++] = aj;
            if (tj != null) jobs[nj++] = tj;
            P.RunParallel(jobs, P.Cpus());
            string[] dl = P.Lines(d.o.buf.ToString());
            for (int i = 0; i < dl.Length; i++) if (P.Len(dl[i]) > 0) LOG.Verbose(dl[i]);
            if (!d.boot)
            {
                for (int i = 0; i < dl.Length; i++) if (P.StartsWith(dl[i], "   ")) Build.Say("        " + P.Trim(dl[i]));
                if (truncated) Build.Say("        (expected: the input file is cut short, so this build is missing its start-up data)");
                Build.Say(""); Build.Say("    The build FAILED the digital-twin check.");
                return 0;
            }
            string bootLine = "";
            for (int i = 0; i < dl.Length; i++)
            {
                if (P.StartsWith(dl[i], "RESULT boot ok ")) bootLine = P.Sub(dl[i], 15, P.Len(dl[i]) - 15);
            }
            Build.Say("        Starts up:      ok");
            LOG.Verbose("boot: " + bootLine + " instructions to the main loop");
            Build.Say("        USB:            ok (same device as stock: 28BD:2904, 3 interfaces)");
            if (binterval > 0) Build.Say("                        one report every " + F.Str(UsbConfiguration.ModelPollInterval(binterval)) + " ms at most (bInterval " + F.Str(binterval) + ")");
            string cline = null;
            for (int i = 0; i < dl.Length; i++) if (P.StartsWith(dl[i], "RESULT countstring ")) cline = P.Sub(dl[i], 19, P.Len(dl[i]) - 19);
            if (cline != null && P.StartsWith(cline, "ADTC1:")) Build.Say("        Flash count:    readable over USB (string " + F.Str(Patches.COUNT_STRING) + ": " + cline + ")");
            if (d.agc < 0) Build.Say("        Gain control:   NOT CHECKED (its code was not found)");
            else if (d.agc > 0) Build.Say("        Gain control:   ok");
            else { Build.Say("        Gain control:   FAILED"); for (int i = 0; i < dl.Length; i++) if (P.StartsWith(dl[i], "   AGC delay")) Build.Say("                       " + P.Trim(dl[i])); }
            int lost = 0;
            for (int i = 0; i < sj.Length; i++)
            {
                SensResult r = sj[i].r;
                string nt = r.n >= 0 ? F.Str(r.n) : r.why;
                LOG.Verbose("   Q 300 strength 6200 at " + F.Fx(sj[i].f / 1000, 0) + " kHz: " + nt + " in-range reports");
                if (r.n <= 0) lost++;
            }
            Build.Say("        Finds the pen:  " + (lost == 0 ? "ok at all " + F.Str(sj.Length) + " test frequencies" : "MISSED at " + F.Str(lost) + " of " + F.Str(sj.Length) + " test frequencies"));
            bool mcOk = true;
            if (mj != null)
            {
                McResult mr = mj.r;
                bool wsOff = mj.ws && mr != null && mr.calls > 0 && mr.labOff == mr.calls;   // Filter Arranger: off in RAM
                mcOk = mr != null && mr.calls > 0 && mr.miss == 0 && (mr.fit > 0 || wsOff);
                string mcName = mj.ws ? "Window Scale:   " : "Multi-coil:     ";
                if (mr == null) Build.Say("        " + mcName + "FAILED (not run)");
                else if (mcOk && wsOff) Build.Say("        " + mcName + "ok (switched off in RAM by the Filter Arranger's starting order: the stock answer on all " + F.Str(mr.calls) + " position calls)");
                else if (mcOk && mj.ws) Build.Say("        " + mcName + "ok (same as its C# copy on all " + F.Str(mr.calls) + " position calls; it changed " + F.Str(mr.fit) + ")");
                else if (mcOk) Build.Say("        Multi-coil:     ok (same as its C# copy on all " + F.Str(mr.calls) + " position calls; the fit was used on " + F.Str(mr.fit) + ")");
                else Build.Say("        " + mcName + "FAILED (" + F.Str(mr.miss) + " of " + F.Str(mr.calls) + " position calls differ from its C# copy" + (mr.fit == 0 ? (mj.ws ? "; it never changed a position" : "; the fit was never used") : "") + ")");
                if (mr != null)
                {
                    LOG.Verbose("multi-coil: " + mr.calls + " calls, " + mr.match + " same, " + mr.miss + " differ, fit used " + mr.fit + ", costliest frame " + mr.maxFrame + " cycles (" + F.Fx(mr.maxFrame / 72.0, 1) + " us)");
                    if (mr.first != null) { LOG.Verbose("multi-coil first difference: " + mr.first); Build.Say("                        first: " + mr.first); }
                }
            }
            bool apOk = true;
            if (aj != null)
            {
                ApipCheck ar = aj.r;
                apOk = ar != null && ar.Ok();
                if (ar == null) Build.Say("        ALPI:           FAILED (not run)");
                else if (apOk) Build.Say("        ALPI:           ok (same as its C# copy on all " + F.Str((int)ar.filtered) + " filtered frames; " + F.Fx(ar.CostMedian() / 72.0, 1) + " us per frame, frames no longer than without it)");
                else Build.Say("        ALPI:           FAILED (" + F.Str((int)ar.miss) + " of " + F.Str((int)ar.frames) + " frames differ from its C# copy" + (ar.filtered <= 100 ? "; too few frames filtered" : "") + (ar.changed == 0 ? "; it never changed a position" : "")
                    + (!ar.PeriodOk() ? "; frames " + F.Fx(ApipCheck.MeanUs(ar.period), 1) + " us on average (" + F.Str(ar.period.n) + "), against " + F.Fx(ApipCheck.MeanUs(ar.basePeriod), 1) + " us (" + F.Str(ar.basePeriod.n) + ") without it" : "") + ")");
                if (ar != null)
                {
                    LOG.Verbose("ALPI: " + ar.frames + " frames, " + ar.filtered + " filtered, " + ar.held + " held, " + ar.idle + " without pen, " + ar.miss + " differ, " + ar.changed + " changed, bands used 0x" + F.Hx(ar.bands, 2)
                        + "; routine " + ar.CostMedian() + " cycles (median), " + ar.CostMax() + " (most without an interrupt); frames " + F.Fx(ApipCheck.MeanUs(ar.period), 2) + " us mean over " + ar.period.n
                        + ", without it " + F.Fx(ApipCheck.MeanUs(ar.basePeriod), 2) + " over " + ar.basePeriod.n);
                    if (ar.first != null) { LOG.Verbose("ALPI first difference: " + ar.first); Build.Say("                        first: " + ar.first); }
                }
            }
            if (tj != null)
            {
                TrainCheckResult tr = tj.r;
                bool tOk = tr != null && tr.ok;
                if (tOk) Build.Say("        ALPI training:  ok (read " + F.Str(tr.samples) + " frames through USB string " + F.Str(Apip.STRING) + " in " + F.Str(tr.reads) + " reads, none missing)");
                else Build.Say("        ALPI training:  FAILED (" + (tr == null ? "not run" : tr.why) + ")");
                apOk = apOk && tOk;
            }
            bool passed = d.agc == 1 && lost == 0 && mcOk && apOk;
            Build.Say("");
            Build.Say("    Checks " + (passed ? "passed" : "FAILED") + ".");
            if (passed)
            {
                Build.Say("");
                Build.Say("    Measurements");
                Build.Say("");
                FrameStats fs = Twin.MeasureFrames(img, 200, true);
                Precision pr = Twin.MeasurePrecision(img, true);
                Latency lat = Twin.MeasureDelay(img, true, true), slat = Build.StockDelay();
                if (slat.ok) Block(true, "STOCK (your input file)", slat, null, true);
                Block(true, gateHeading != null ? gateHeading : "This build", lat, fs, false);
                if (fs.ok) LOG.Verbose("frame " + Twin.Ms(fs.periodMean) + " (" + Twin.Ms(fs.periodMin) + " .. " + Twin.Ms(fs.periodMax) + "), work " + Twin.Ms(fs.workMean) + " (max " + Twin.Ms(fs.workMax) + "), processing max " + Twin.Ms(fs.procMax));
                if (lat.ok) LOG.Verbose("cursor delay: drawing " + F.Fx(lat.drawMs, 2) + " ms (spread " + F.Fx(lat.drawSpread, 2) + "), hovering " + F.Fx(lat.hoverMs, 2) + " ms (spread " + F.Fx(lat.hoverSpread, 2) + ", " + F.Fx(lat.hoverRate, 0) + " reports/s)");
                if (pr.ok)
                {
                    Build.Say("    Still pen (made-up twin noise: compare builds with it, not with a real tablet)");
                    Build.Say("      Shakes " + F.Fx(pr.sdX, 3) + " mm (X) / " + F.Fx(pr.sdY, 3) + " mm (Y), " + F.Fx(pr.p2p, 3) + " mm at most");
                    Build.Say("      Sliding across 2 coils: off by up to " + F.Fx(pr.maxErr, 2) + " mm");
                    Build.Say("");
                }
                Terminology(true);
                Build.Say("");
                if (!fs.ok || !pr.ok || !lat.ok)
                {
                    Build.Say("        Required frame, precision or latency measurements could not be verified.");
                    passed = false;
                }
            }
            Build.Say("    Digital-twin check " + (passed ? "passed" : "FAILED") + " (" + F.Fx(P.NowSec() - t0, 1) + " s)");
            return passed ? 1 : 0;
        }

        // ---- what each filter costs on the user's firmware: cursor delay (R41; until R40 this showed CPU cycles per
        //      frame, the difference of two noisy runs clipped at 0, in microseconds with six decimals)
        public static Latency[] delays = null;        // filters off, smoothing, interpolation, interpolation + aggressive, APIP (neutral): tracker, robust 2-4
        const int NCOST = 8;
        public static string costLevel = "";

        public static FrameStats Measure(byte[] app, InputInfo info, Level lv, bool[] st, Options o, StringBuilder why)
        {
            Build.quiet = true;
            Image im;
            try { im = Build.BuildOne(app, info, null, lv, st, true, false, o); }
            catch (Stop e) { why.Append(e.msg); return null; }
            finally { Build.quiet = false; }
            return Twin.MeasureFrames(Twin.FromApp(im.Bytes()), 120, true);
        }

        public static void MeasureCosts(byte[] app, InputInfo info, Level lv)
        {
            P.Line("    Measuring cursor delay on " + P.Upper(lv.name) + " in the digital twin (simulated 72 MHz; a few seconds)...");
            LatencyJob[] lj = new LatencyJob[NCOST + 1];
            Job[] jobs = new Job[NCOST + 1];
            lj[NCOST] = new LatencyJob(); jobs[NCOST] = lj[NCOST];         // the stock baseline, measured alongside
            if (Build.stockApp != null && !Build.StockDelayKnown()) lj[NCOST].img = Twin.FromApp(Build.stockApp);
            for (int c = 0; c < NCOST; c++)
            {
                lj[c] = new LatencyJob(); jobs[c] = lj[c];
                if (c == 1 && !Build.SmoothingFits(lv)) continue;          // no image: "does not fit"
                bool[] st = Defaults();
                st[Build.SetIndex("smoothing")] = c == 1; st[Build.SetIndex("interp")] = c >= 2 && c <= 3; st[Build.SetIndex("interp2")] = c == 3;
                // R44: APIP with the neutral model (no output file); R45: its tracker (4) and robust filters (5-7)
                string ak = c >= 4 ? Build.ApipKey(c == 4 ? 1 : c - 3) : null;
                if (ak != null) st[Build.SetIndex(ak)] = true;
                if (ak != null && Build.Unavailable(ak, lv, st) != null) continue;
                Build.quiet = true;
                try { lj[c].img = Twin.FromApp(Build.BuildOne(app, info, null, lv, st, true, false, new Options()).Bytes()); }
                catch (Stop) { lj[c].img = null; }
                finally { Build.quiet = false; }
            }
            P.RunParallel(jobs, P.Cpus());
            delays = new Latency[NCOST];
            for (int c = 0; c < NCOST; c++) delays[c] = lj[c].img == null ? null : lj[c].lat;
            if (lj[NCOST].img != null && lj[NCOST].lat != null) Build.CacheStockDelay(lj[NCOST].lat);
            costLevel = lv.name;
        }

        // "adds about 5 ms of cursor delay while drawing; about 10 ms while hovering (RAPID)", or why it is not known
        public static string DelayLine(string key)
        {
            int on = -1, off = -1;
            if (P.Eq(key, "smoothing")) { on = 1; off = 0; }
            if (P.Eq(key, "interp")) { on = 2; off = 0; }
            if (P.Eq(key, "interp2")) { on = 3; off = 2; }
            if (Build.IsApip(key)) { int k = Build.ApipKindOf(key); on = k == 1 ? 4 : k + 3; off = 0; }
            if (on < 0 || delays == null) return null;
            if (delays[on] == null && (on == 1 || on >= 4)) return "does not fit " + P.Upper(costLevel) + "'s frame.";
            if (on >= 4 && delays[on].ok && delays[0] != null && delays[0].ok)
                return Twin.AddedDelayText(delays[on].drawMs - delays[0].drawMs) + " cursor delay while drawing, " + Twin.AddedDelayText(delays[on].hoverMs - delays[0].hoverMs) + " while hovering (" + P.Upper(costLevel) + ", untrained model; the twin's pen noise is made up, so the trained delay differs).";
            if (delays[on] == null || delays[off] == null || !delays[on].ok || !delays[off].ok) return "the twin could not measure it on " + P.Upper(costLevel) + ".";
            Latency a = delays[on], b = delays[off];
            return Twin.AddedDelayText(a.drawMs - b.drawMs) + " cursor delay while drawing, " + Twin.AddedDelayText(a.hoverMs - b.hoverMs) + " while hovering (" + P.Upper(costLevel) + ").";
        }

        // "(Steady Hover: 400 RPS)" when the hover measurement ran at a lower report rate
        public static string RateNote(Latency l)
        {
            if (!l.hasHover || l.hoverRate >= l.drawRate * 0.9) return "";
            return "(Steady Hover: " + F.Str((int)P.Rint(l.hoverRate)) + " RPS)";
        }
        public const string DELAY_CONDITIONS = "Cursor delay = how far behind the pen the cursor is.";

        // R42: every measurement output ends with this (Anargon's wording)
        public static readonly string[] TERMINOLOGY = new string[] {
            "Terminology:",
            "    These numbers come from a pen sliding steadily on a digital copy of Anargon's tablet.",
            "    It copies how the tablet behaves; it is not a physical test or a full 3D physics model",
            "    of the tablet.",
            "",
            "    Drawing = the pen touching the tablet (any pressure). Hovering = the pen at the farthest",
            "    point where it is still detected." };

        // R42 (Anargon's format): one block per firmware. "800 Hz [RAPID] with Steady Hover"
        public static string Heading(Level lv, bool[] st)
        {
            string h = lv.key + " Hz [" + P.Upper(lv.name) + "]";
            if (st != null && Build.Unavailable("boost", lv, st) == null) h += Build.On(st, "boost") ? " with Steady Hover" : " without Steady Hover";
            return h;
        }
        static void Out(bool say, string s) { if (say) Build.Say(s); else P.Line(s); }
        // stock: the unmodified firmware (not paced: its frame is one report)
        public static void Block(bool say, string heading, Latency lat, FrameStats fs, bool stock)
        {
            Out(say, "    " + heading);
            if (lat != null && lat.ok)
            {
                Out(say, "      Cursor delay - " + Twin.DelayText(lat.drawMs) + " while drawing, " + Twin.DelayText(lat.hoverMs) + " while hovering" + (stock ? " (" + F.Str((int)P.Rint(lat.drawRate)) + " RPS)" : ""));
                // twin, R42: stock 16.9 / 22.7 ms; without its smoothing 14.6 / 17.2; without both filters 2.1 / 2.8
                if (stock && lat.hoverMs - lat.drawMs > 2) Out(say, "      (Hovering is slower mostly because of stock Movement Smoothing, partly Position Interpolation)");
                string rn = RateNote(lat);
                if (P.Len(rn) > 0) Out(say, "      " + rn);
            }
            else Out(say, "      Cursor delay - could not be measured");
            if (fs != null && fs.ok) Out(say, "      One frame every " + Twin.Ms(fs.periodMean) + ", processed for " + Twin.Ms(fs.procMean) + " per frame");
            else if (stock && lat != null && lat.ok && lat.drawRate > 0) Out(say, "      One frame every " + F.Fx(1000.0 / lat.drawRate, 2) + " ms");
            Out(say, "");
        }
        public static void Terminology(bool say) { for (int i = 0; i < TERMINOLOGY.Length; i++) Out(say, P.Len(TERMINOLOGY[i]) > 0 ? "    " + TERMINOLOGY[i] : ""); }

        // the chosen settings, built and run at the real 72 MHz: null if they fit, else why not
        public static string CheckChoice(byte[] app, InputInfo info, Level[] levels, bool[] st)
        {
            return CheckChoice(app, info, levels, st, new Options());
        }

        public static string CheckChoice(byte[] app, InputInfo info, Level[] levels, bool[] st, Options options)
        {
            P.Line("");
            P.Line("    Checking your settings in the digital twin (simulated 72 MHz)...");
            LatencyJob[] lj = new LatencyJob[levels.Length];
            Job[] jobs = new Job[levels.Length];
            for (int k = 0; k < levels.Length; k++)
            {
                Build.quiet = true;
                Image im;
                try { im = Build.BuildOne(app, info, null, levels[k], st, true, false, options); }
                catch (Stop e) { Build.quiet = false; return P.Upper(levels[k].name) + ": " + P.Replace(e.msg, "refusing to build:\n  - ", ""); }
                finally { Build.quiet = false; }
                lj[k] = new LatencyJob(); lj[k].img = Twin.FromApp(im.Bytes()); lj[k].frames = true; jobs[k] = lj[k];
            }
            P.RunParallel(jobs, P.Cpus());
            for (int k = 0; k < levels.Length; k++)
            {
                if (lj[k].fs == null || !lj[k].fs.ok) return P.Upper(levels[k].name) + ": frame timing could not be verified because the simulated pen was not tracked.";
                if (lj[k].lat == null || !lj[k].lat.ok) return P.Upper(levels[k].name) + ": cursor delay could not be verified.";
            }
            P.Line("");
            P.Line("    Measurements");
            P.Line("");
            Block(false, "STOCK (your input file)", Build.StockDelay(), null, true);
            for (int k = 0; k < levels.Length; k++)
            {
                Latency lat = lj[k].lat;
                Block(false, Heading(levels[k], st), lat, lj[k].fs, false);
                Build.LOG.Verbose("cursor delay " + levels[k].name + ": drawing " + F.Fx(lat.drawMs, 2) + " ms (spread " + F.Fx(lat.drawSpread, 2) + ", " + F.Fx(lat.drawRate, 0) + "/s), hovering " + F.Fx(lat.hoverMs, 2) + " ms (spread " + F.Fx(lat.hoverSpread, 2) + ", " + F.Fx(lat.hoverRate, 0) + "/s)");
            }
            Terminology(false);
            return null;
        }

        // the window lengths of Aggressive Position Interpolation at LV's report rate (they are counted in reports)
        // R41: what Accurate Pressure Readings costs at one preset, in pressure updates per second
        public static string PressureText(Level lv, bool[] st)
        {
            Level a = Build.Accurate(lv, st);
            if (lv.period <= 0 || a == null) return "";
            return P.Upper(lv.name) + ": about " + F.Fx(Build.SweepRate(a), 0) + " pressure updates per second ("
                + F.Fx(Build.SweepRate(lv), 0) + " with this off, stock about " + F.Str((int)P.Rint(Build.SweepRate(Build.Lv("220")) / 10) * 10) + ").";
        }

        public static string WindowText(Level lv)
        {
            if (lv.period <= 0) return "";
            double rate = 1000000.0 / lv.period;
            return P.Upper(lv.name) + " (" + F.Str((int)P.Rint(rate)) + " RPS): averages over " + F.Str((int)P.Rint(16000.0 / rate)) + " / " + F.Str((int)P.Rint(8000.0 / rate)) + " / "
                + F.Str((int)P.Rint(4000.0 / rate)) + " ms. Still = under " + F.Str((int)P.Rint(0.2 * rate)) + " mm/s, slow = under " + F.Str((int)P.Rint(0.8 * rate))
                + " mm/s. Longer while Steady Hover lowers the report rate (it counts reports, not time).";
        }

        // "Label: text" wrapped to the 96-column screen, continuation lines under the text
        public static void Hanging(string label, string text, int indent)
        {
            StrList l = Txt.Wrap(text, 96 - indent - P.Len(label));
            for (int k = 0; k < l.Count(); k++) P.Line(Screen.Rule(' ', indent) + (k == 0 ? label : Screen.Rule(' ', P.Len(label))) + l.Get(k));
        }

        static void Heading(string head) { P.Line("  " + head); P.Line("  " + Screen.Rule('-', P.Len(head))); P.Line(""); }
        static void DelayNote()
        {
            if (delays == null) return;
            P.Line("");
            Screen.Para("Measured on " + P.Upper(costLevel) + ". " + DELAY_CONDITIONS + " " + Build.CALC_TIME, 4);
            P.Line("");
            Terminology(false);
        }

        // R45 (Anargon): the wizard's settings come in two screens, Noise-Related Patches and Extra QOL Patches; Pen
        // frequency tuning is in the Calibration step (Fast Pass). The settings of CATS, numbered from 1: map[number - 1]
        // is the setting's index. Returns how many.
        // R45-rc2 (Anargon: "Calibration (with or without ALPI), Noise related settings (from ALPI to Multicoil, etc.),
        // then other settings"): the categories show in the order of CATS (ALPI first, Multi-coil Read last), not in the
        // list order, which stays the order of the file-name tags
        // R45, 2026-11-17 (Anargon: fix the "arrangements, and option positions"): the old "Noise-related tuning" mixed how the
        // coils are read, corrections of the position and filters; each is its own group now, in the order they act:
        // reading the coils, then position corrections, then the filters that trade noise for delay ("noise" is left for
        // package settings). Inside a group the settings show in SCREEN_ORDER order, then any others in list order.
        public static readonly string[] NOISE_CATS = new string[] { "apip", "scan", "corr", "latency", "noise", "exp" }, QOL_CATS = new string[] { "hover", "storage", Build.PKG_CAT };
        public static readonly string[] SCREEN_ORDER = new string[] { "fullres", "fhold", "chold", "systick", "tiltskip", "accp",
            "misc", "interp", "interp2", "smoothing", "odd" };
        public static int ShowGroup(bool[] st, Level[] levels, string[] cats, int calLevel, int[] map)
        {
            int n = 0;
            for (int c = 0; c < cats.Length; c++)
            {
                bool head = false;
                int[] order = ScreenOrder();
                for (int j = 0; j < order.Length; j++)
                {
                    int i = order[j];
                    Setting s = Build.SETTINGS[i];
                    if (!P.Eq(s.cat, cats[c]) || P.Eq(s.key, "cal") || Build.HiddenWhy(s.key) != null) continue;   // R45-rc3: hidden by a package
                    if (Build.InMisc(s.key)) continue;                           // R45: switched by Misc. Optimizations
                    if (!head)
                    {
                        Heading(Build.CategoryName(s.cat)); head = true;
                        if (P.Eq(s.cat, "apip")) { Screen.Para(Build.APIP_NOTE, 4); P.Line(""); }   // R45
                    }
                    map[n] = i; n++;
                    ShowSetting(i, n, st, levels, LockedBy(s.key, calLevel));
                }
            }
            return n;
        }
        // every setting index once: SCREEN_ORDER first, then the rest in list order
        static int[] ScreenOrder()
        {
            int[] r = new int[Build.SETTINGS.Length]; int n = 0;
            for (int k = 0; k < SCREEN_ORDER.Length; k++) { int i = Build.SetIndex(SCREEN_ORDER[k]); if (i >= 0) r[n++] = i; }
            for (int i = 0; i < Build.SETTINGS.Length; i++) if (!InList(SCREEN_ORDER, Build.SETTINGS[i].key)) r[n++] = i;
            return r;
        }
        static bool InList(string[] l, string k) { for (int i = 0; i < l.Length; i++) if (P.Eq(l[i], k)) return true; return false; }
        // switches the numbered settings of one screen; what could not be switched, or null
        static string Toggle(string sel, bool[] st, int[] map, int n, int calLevel)
        {
            string why = "";
            string[] toks = P.Split(P.Replace(sel, ",", " "), ' ');
            for (int i = 0; i < toks.Length; i++)
            {
                if (P.Len(toks[i]) == 0) continue;
                int k = Digits(toks[i]) ? (int)F.ParseInt(toks[i]) : -1;
                if (k < 1 || k > n) continue;
                Setting s = Build.SETTINGS[map[k - 1]];
                string lk = LockedBy(s.key, calLevel);
                if (lk != null) { why = why + s.name + " " + lk + ". "; continue; }
                if (!st[map[k - 1]] && Build.IsApip(s.key)) Build.ApipOff(st);       // R45: one APIP filter at a time
                st[map[k - 1]] = !st[map[k - 1]];
                if (P.Eq(s.key, "misc")) Build.SetMisc(st, st[map[k - 1]]);      // R45: its four settings follow
            }
            return P.Len(why) > 0 ? P.Trim(why) : null;
        }

        // R45 (Anargon): the input backup carries the Bootloader Counter, so the toggle starts on
        public const string COUNTER_FOUND = "installed on this tablet. On keeps it. Off removes it with this flash: the bootloader goes back to stock and the firmware keeps counting. With Bootloader Changes off it stays installed.";
        // one setting as the settings screens show it; LOCKED = why the wizard does not offer it yet (R45), or null
        static void ShowSetting(int i, int number, bool[] st, Level[] levels, string locked)
        {
            {
                Setting s = Build.SETTINGS[i];
                StringBuilder na = new StringBuilder();
                int nna = 0;
                for (int k = 0; k < levels.Length; k++) if (Build.Unavailable(s.key, levels[k], st) != null) { if (nna > 0) na.Append(", "); na.Append(P.Upper(levels[k].name)); nna++; }
                StringBuilder rq = new StringBuilder();
                int nrq = 0;
                for (int k = 0; k < levels.Length; k++) if (Build.Required(s.key, levels[k]) != null) { if (nrq > 0) rq.Append(", "); rq.Append(P.Upper(levels[k].name)); nrq++; }
                string state = locked != null ? "[LOCKED]" : levels.Length == 1 && nna > 0 ? "[ N/A ]" : st[i] || (levels.Length == 1 && nrq > 0) ? "[ ON  ]" : "[ OFF ]";
                string num = Txt.PadL(F.Str(number), 2);
                P.Line("    " + num + ".  " + Txt.PadR(s.name + " ", 34) + state);
                if (nrq > 0) P.Line("            (required for " + rq.ToString() + ": always on)");
                string[] parts = P.Split(s.what, '|');
                Screen.Para(parts[0], 12);
                if (parts.Length > 1) Hanging("Cost: ", parts[1], 12);
                if (P.Eq(s.key, "blcounter") && Build.counterFound) Hanging("Found: ", COUNTER_FOUND, 12);   // R45
                if (locked != null) P.Line("            (" + locked + ")");
                else if (levels.Length == 1 && nna > 0) P.Line("            (" + Build.Unavailable(s.key, levels[0], st) + ")");
                else if (nna > 0) P.Line("            (not used by " + na.ToString() + ")");
                if (P.Eq(s.key, "interp2"))
                    for (int k = 0; k < levels.Length; k++) if (P.Len(WindowText(levels[k])) > 0) Screen.Para(WindowText(levels[k]), 12);
                if (P.Eq(s.key, "accp"))
                    for (int k = 0; k < levels.Length; k++) if (P.Len(PressureText(levels[k], st)) > 0) Screen.Para(PressureText(levels[k], st), 12);
                string measured = DelayLine(s.key);
                if (measured != null) Hanging("Measured: ", measured, 12);
                P.Line("");
            }
        }

        // ---- R45 (Anargon): calibration in three levels ("the more complex, the better"); what a level does not
        //      measure comes from Anargon's own measurements where there are some (the fallback: Build.FB_*,
        //      Apip.Fallback), else the preset's defaults
        public const int CAL_SKIP = 0, CAL_FAST = 1, CAL_STANDARD = 2, CAL_FULL = 3;
        public static readonly string[] CAL_LEVELS = new string[] { "Skipped", "Fast Pass", "Standard", "Full" };
        // the settings a calibration level unlocks in the wizard (Anargon's "Unlockable options"): null = offered
        public static string LockedBy(string key, int calLevel)
        {
            if (P.Eq(key, "multicoil") && calLevel < CAL_STANDARD) return "needs Standard or Full calibration (step 4)";
            if (Build.IsApip(key) && calLevel < CAL_FULL) return "needs Full calibration (step 4)";
            return null;
        }
        // one calibration level, laid out like a setting
        static void CalEntry(int number, string name, string what, string cost, string unlocks, string na)
        {
            P.Line("    " + Txt.PadL(F.Str(number), 2) + ".  " + name);
            Screen.Para(what, 12);
            Hanging("Cost: ", cost, 12);
            if (unlocks != null) Hanging("Unlocks: ", unlocks, 12);
            if (na != null) P.Line("            (" + na + ")");
            P.Line("");
        }
        // what Anargon measured, for the screens
        static string FallbackText()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Build.FB_KEY.Length; i++) { if (i > 0) sb.Append(" and "); sb.Append(P.Upper(Build.Lv(Build.FB_KEY[i]).name)); }
            return sb.ToString();
        }

        // step 4: the level, and its measuring that comes before the patches (Full's APIP training comes after them,
        // because it learns the noise of the build as chosen). Returns the level.
        static int CalibrationStep(byte[] app, InputInfo info, Level[] levels, bool[] st, string d, Options o, bool realtime)
        {
            bool one = levels.Length == 1 && levels[0].period > 0;
            int nFast = 0;
            for (int k = 0; k < levels.Length; k++) if (Build.Unavailable("cal", levels[k], st) == null) nFast++;
            string apipNa = one ? Build.Unavailable("apip", levels[0], st) : null;
            string[] ft = P.Split(Build.SETTINGS[Build.SetIndex("cal")].what, '|');       // the setting's own text (Anargon: "formatting in the same way")
            string problem = null;
            while (true)
            {
                Screen.Step("Step 4/6: Calibration");
                Screen.Para("Choose how much to measure on your tablet: the more complete, the better. What is not measured uses " + Build.FB_WHO + " where there are some (the fallback: " + FallbackText() + "), else the preset's defaults.", 4);
                P.Line("");
                Heading("Fast Pass");
                CalEntry(1, "Pen frequency tuning (experimental)", ft[0] + " Nothing to do on this PC.",
                    "Can lower hover height. The build keeps the preset's own pick-up levels: measured ones (Anargon's fallback too) need the stock scan frequency.",
                    null, nFast == 0 ? "not used by STOCK" : null);
                Heading("Standard (without ALPI)");
                CalEntry(2, "Tablet signal thresholds", "This tool flashes a measurement firmware, then walks you through three pen positions (a light touch, a normal hover and the highest hover), so the tablet picks up the pen as high as your pen allows (never lower than the defaults). It also shows your pen's frequency, and whether pressure shows while hovering.",
                    "A measurement firmware to flash and a few minutes. It measures at the stock scan frequency, so it does not use Pen frequency tuning.",
                    "Multi-coil Read (Noise-Related Patches).", one ? null : "needs one preset other than STOCK (step 3)");
                Heading("Full (with ALPI)");
                CalEntry(3, "ALPI training", "After you choose the patches: a training firmware (the cursor keeps working), normal use of the tablet until every part of it has enough readings at every pen height, and a 4-second hold in the middle. The same readings measure, in the background, what Standard measures: how high your tablet picks up the pen, and your pen's frequency. There is no measurement firmware and no separate steps. ALPI replaces Jump Rejection, so its margin is not needed. If ALPI ends up off, Standard's measurement is offered before the build.",
                    "A training firmware to flash, and normal use until the training's coverage table is complete. If you skip the training or it stops, ALPI uses the defaults: Anargon's training, pick-up levels from " + Build.FB_WHO + " (" + FallbackText() + ") or the preset, and the stock pen frequency. They were not measured on your tablet, so they may not match it.",
                    "Multi-coil Read, and ALPI (Noise-Related Patches): its Tracker (on by default) and the experimental Robust filters. On Anargon's tablet at ULTRA, pen still at the centre, the Tracker shook 13x less sideways and 16x less up and down than no filter, and adds no cursor delay of its own (Position Interpolation adds about 4 ms).",
                    !one ? "needs one preset other than STOCK (step 3)" : apipNa != null ? "ALPI " + apipNa + ", so Full is the same as Standard here" : null);
                P.Line("    Enter = skip: nothing is measured.");
                if (problem != null) { P.Line(""); Screen.Para(problem, 4); }
                P.Line("");
                string c = P.Trim(Ask("    Calibration (1-3, Enter = skip): > "));
                problem = null;
                if (P.Len(c) == 0) return CAL_SKIP;
                int lvl = Digits(c) ? (int)F.ParseInt(c) : -1;
                if (lvl == CAL_FAST)
                {
                    if (nFast == 0) { problem = "Fast Pass: Pen frequency tuning is not used by STOCK."; continue; }
                    st[Build.SetIndex("cal")] = true;
                    return CAL_FAST;
                }
                if (lvl == CAL_STANDARD || lvl == CAL_FULL)
                {
                    if (!one) { problem = CAL_LEVELS[lvl] + " needs one preset other than STOCK: choose it in step 3 (start again), or skip."; continue; }
                    st[Build.SetIndex("cal")] = false;
                    // R45 (Anargon: calibration "mostly skippable with ALPI selected"): Standard's measurement puts only
                    // the fast stage's pick-up levels and Jump Rejection's margin into a build. ALPI replaces Jump
                    // Rejection, and (R45-rc2) its training measures the levels in the background (Cli.FromTraining), so
                    // Full runs the guided steps only when ALPI cannot be used here; if ALPI ends up off after all, they
                    // are offered before the build
                    if (lvl == CAL_STANDARD || apipNa != null) Calibrate(app, info, levels[0], st, d, o, realtime);
                    return lvl;
                }
            }
        }
        // where the build's pick-up levels come from, for the patches screen
        static string LevelsText(Level[] levels, bool[] st, Options o)
        {
            if (o.calPick > 0) return "pick-up levels measured on your tablet";
            if (levels.Length == 1) return Build.FallbackFor(levels[0], st, o) >= 0 ? "pick-up levels: " + Build.FB_WHO + " (fallback)" : "pick-up levels: the preset's own";
            for (int k = 0; k < levels.Length; k++)
                if (Build.FallbackFor(levels[k], st, o) >= 0) return "pick-up levels: " + Build.FB_WHO + " for " + FallbackText() + " (fallback), the preset's own for the others";
            return "pick-up levels: the presets' own";
        }

        // ---- tablet calibration: pass 1 flashes a build whose pen reports carry the firmware's own readings; the app
        //      guides you through standard steps, derives per-tablet values, and pass 2 builds and flashes with them
        public static bool calibrationFirmwareActive = false; // a measurement update may have reached the tablet
        public static bool measurementInstallRequested = false; // an explicit measurement-only update completed successfully
        public static bool labWizard = false;                   // --lab: the wizard makes the Filter Arranger build
        // R45-rc5 (3): Measurement Tools' Tablet signal thresholds (atf_thresholds.cs) runs Calibrate with no build after
        // it, so a few texts say so; its gate replaces the tablet reader (null: the tablet). The wizard sets neither.
        public static bool calStandalone = false;
        public static CalSource calSource = null;
        // R41 wording (Anargon, on the real tablet): "middle" is the middle of the drawing area, below the button bar;
        // hover heights get an everyday reference; the LED shows tracking in this build
        // R44 (Anargon): the LED marks the step's target (on within 7 mm of the middle, blinking while measuring), the
        // light touch is under 4096, and the detection limit is found with the LED (Anargon's wording).
        // R45-rc2 (Anargon): no no-pen step ("you cant measure anything there anyways; its always lost": the firmware
        // sends readings only while it tracks the pen); the readiness check's light touch is the first step's, which
        // asked for the same position again right after it; and no slow lift: it looked for the same detection limit
        // as the highest hover, no build value came from it, and on Anargon's tablet it ended at the coils' floor (72)
        // both times.
        public static readonly string[] CAL_TITLES = new string[] { "minimal pressure", "normal hover", "highest hover" };
        public static readonly string[] CAL_ASK = new string[] {
            "Rest the tip on the middle of the drawing area (below the button bar) with almost no weight: under 4096 on the readout. The LED shines when the tip is within 7 mm of the middle.",
            "Hold the pen still about 5 mm above the middle of the drawing area: half a phone's thickness (3 to 8 mm is fine). Keep the phone beside the tablet, not under the pen. The LED shines when the pen is over the middle.",
            "Start at normal hover over the middle of the drawing area. Move the pen up until the point where the LED stops shining, then bring it down until it starts shining again. This is your tablet's detection limit. Hold it at that position during calibration." };
        public static readonly double[] CAL_SECS = new double[] { 4, 4, 4 };
        public const long LIGHT_TOUCH = 4096;             // R44: "almost no weight" = pressure below this (was 2048)
        public const int ZONE_NONE = 0, ZONE_CENTRE = 1;  // LED targets (Sched.ZONE_*): every step aims at the middle
        // the measurement firmware's fast-stage levels (R45-rc2: also the ALPI training firmware's), so the readings show
        // how high the pen can be tracked
        public const int MEASURE_PICK = 40, MEASURE_DROP = 24;
        public const string NO_CURSOR = "There is no cursor during calibration. Do not press the pen's buttons (they change the readings) and do not use the mouse (a click can take the keys away from this window). Use only the keyboard.";
        public const string LED_LEGEND = "LED: off = no pen, or the pen is not over the target; on = the pen is within 7 mm of the target; pulsing = measuring.";
        // R45 (Anargon, 2026-11-17): also apps and games with OpenTabletDriver built in (osu!lazer was running, no tray icon,
        // and switched the tablet to its own reports at every plug-in)
        public const string DRIVER_HOW = "close OpenTabletDriver (or applications and games with built-in OpenTabletDriver support, such as osu!lazer), or right-click XP-Pen's icon (XPPenTablet) in the system tray, next to the clock, and exit it.";
        public const string DRIVER_EXIT = "Exit your tablet driver: " + DRIVER_HOW;

        public abstract class CalSource
        {
            public abstract string Open();
            public abstract int[] Collect(int step, double seconds);
            public abstract void Close();
            public abstract string Why();       // "" or what arrived instead of pen reports (P.HidWhy for the HID reader)
            // R44: the LED zone (calibration-pass firmware): target ZONE_NONE / ZONE_CENTRE, recording or not. True when
            // the tablet confirmed it; a source without an LED takes it as done.
            public virtual bool Zone(int target, bool recording) { return true; }
        }

        // the USB string that sets the LED zone, and the answer that confirms it (Patches.CounterString)
        public static int ZoneString(int target, bool recording) { return Sched.ZONE_STRING + 2 * target + (recording ? 1 : 0); }
        public static string ZoneReply(int target, bool recording) { return "ADTZ1:" + F.Str(target) + (recording ? "1" : "0"); }

        public class HidSource : CalSource
        {
            public override string Open()
            {
                string err = "";
                for (int i = 0; i < 20; i++) { err = P.HidOpenPen(); if (P.Len(err) == 0) return ""; P.Sleep(1.0); }
                return err;
            }
            public override bool Zone(int target, bool recording)
            {
                return P.Eq(P.HidIndexedString(0x28BD, 0x2904, ZoneString(target, recording)), ZoneReply(target, recording));
            }
            public override int[] Collect(int step, double seconds) { return Run(seconds); }
            int[] Run(double seconds)
            {
                // short chunks with a live readout of the newest readings
                IntBuf all = new IntBuf();
                double started = P.NowSec(), elapsed = 0; long lastSig = -1, lastP = -1, lastFreq = -1;
                int lastPositionTime = -1; bool inRange = false;
                while (elapsed < seconds)
                {
                    double chunk = seconds - elapsed; if (chunk > 0.25) chunk = 0.25;
                    int[] r = P.HidCollect(chunk);
                    elapsed = P.NowSec() - started;
                    bool signalSeen = false, frequencySeen = false;
                    for (int i = 0; i < r.Length; i++) all.Add(r[i]);
                    for (int i = 0; i + 10 < r.Length; i += 11)
                    {
                        if (!TelemetryRecord(r, i)) continue;
                        int pg = r[i + 9] & 3;
                        if (pg == 1)
                        {
                            lastP = Lo(r[i + 6], r[i + 7]); inRange = (r[i + 8] & 0x20) != 0;
                            if (lastP > 16383 || Lo(r[i + 2], r[i + 3]) > 16000 || Lo(r[i + 4], r[i + 5]) > 9000) inRange = false;
                            lastPositionTime = r[i + 10];
                        }
                        if (pg == 2 && inRange && lastP < LIGHT_TOUCH && lastPositionTime >= 0 && r[i + 10] >= lastPositionTime && r[i + 10] - lastPositionTime <= 100000)
                        {
                            long hz = Lo(r[i + 2], r[i + 3]) * 16;
                            if (hz >= 457000 && hz <= 540000) { lastFreq = hz; frequencySeen = true; }
                        }
                        if (pg == 0 && inRange && lastPositionTime >= 0 && r[i + 10] >= lastPositionTime && r[i + 10] - lastPositionTime <= 100000)
                        {
                            lastSig = Lo(r[i + 2], r[i + 3]) < Lo(r[i + 4], r[i + 5]) ? Lo(r[i + 2], r[i + 3]) : Lo(r[i + 4], r[i + 5]);
                            signalSeen = true;
                        }
                    }
                    double left = seconds - elapsed; if (left < 0) left = 0;
                    P.Line("        signal " + Txt.PadL(signalSeen ? F.Str(lastSig) : "LOST", 6) + "   pressure " + Txt.PadL(signalSeen && lastP >= 0 ? F.Str(lastP) : "-", 6)
                        + "   resonance " + (frequencySeen ? F.Fx(lastFreq / 1000.0, 3) + " kHz" : "-") + "   "
                        + F.Fx(left, 1) + " s left");
                }
                P.Line("");
                // no pen report in the whole step: say what the PC saw instead (wrong collection, other report ID...)
                if (all.n == 0) { string why = P.HidWhy(); if (P.Len(why) > 0) P.Line("        (no pen reports: " + why + ")"); }
                return all.Take();
            }
            public override void Close() { P.HidClose(); }
            public override string Why() { return P.HidWhy(); }
        }

        // the same steps played by a scripted pen in the digital twin (integrating receiver), to test the pipeline
        public class TwinSource : CalSource
        {
            public byte[] img; public Machine mc; public Pen pen; public VirtualHost host;
            public StrList zones = new StrList();       // R44: every LED zone the tablet confirmed ("target recording")
            public override string Open()
            {
                pen = new Pen(1); pen.f0 = 505000.0; pen.Q = 300.0; pen.hasHeight = true; pen.height = 0; pen.integrate = true; pen.present = false;
                StringBuilder why = new StringBuilder();
                mc = Twin.ScanMachine(img, pen, false, why);
                if (mc != null) host = new VirtualHost(mc.usb);
                return mc == null ? "twin: " + why.ToString() : "";
            }
            // The virtual host runs a USB interrupt to completion at once. Start it from the main loop with interrupts
            // enabled, as the real interrupt would: from inside another interrupt the emulator would finish that one
            // too, outside its own bookkeeping, and stop taking interrupts (seen: a frozen millisecond counter).
            public void ToMainLoop() { for (int i = 0; i < 1000000 && (mc.cpu.excDepth > 0 || mc.cpu.primask != 0); i++) mc.RunMs(0.0002); }
            public override bool Zone(int target, bool recording)
            {
                string got = "";
                try
                {
                    ToMainLoop();
                    byte[] d = host.GetDescriptor(3, ZoneString(target, recording), 255, 0x0409, 0x80);
                    for (int i = 2; i + 1 < d.Length && i < (d[0] & 0xFF); i += 2) got += (char)((d[i] & 0xFF) | ((d[i + 1] & 0xFF) << 8));
                }
                catch (Fault) { got = ""; }
                catch (Stop) { got = ""; }
                bool ok = P.Eq(got, ZoneReply(target, recording));
                if (ok) zones.Add(F.Str(target) + (recording ? " rec" : " hold"));
                return ok;
            }
            public override int[] Collect(int step, double seconds)
            {
                int r0 = mc.reports.n;
                double[] h = new double[] { 0, 5, 12 };     // R45-rc2: light touch, normal hover, highest hover
                pen.present = true; pen.height = h[step];
                int ms = (int)(seconds * 1000);
                for (int t = 0; t < ms; t++) mc.RunMs(1);
                return Records(r0);
            }
            int[] Records(int r0)
            {
                IntBuf b = new IntBuf();
                for (int i = r0; i < mc.reports.n; i++)
                {
                    Report r = mc.reports.a[i];
                    if (r.ep != 2) continue;
                    for (int k = 0; k < 10; k++) b.Add(r.data[k] & 0xFF);
                    b.Add((int)((r.cycle - mc.reports.a[r0 < mc.reports.n ? r0 : i].cycle) / 72));
                }
                return b.Take();
            }
            public override void Close() { }
            public override string Why() { return ""; }
        }

        public class IntBuf
        {
            public int[] a = new int[1024]; public int n = 0;
            public void Add(int v) { if (n == a.Length) { int[] g = new int[n * 2]; P.Copy(a, g, n); a = g; } a[n++] = v; }
            public int[] Take() { int[] o = new int[n]; P.Copy(a, o, n); return o; }
        }

        static long Lo(int lo, int hi) { return (lo & 0xFF) | ((hi & 0xFF) << 8); }

        // ID 2 uses the vendor's alternate pen format. Ordinary pen reports are not calibration data either.
        public static bool TelemetryRecord(int[] rec, int i)
        {
            if (i < 0 || i + 10 >= rec.Length || rec[i] != 7 || rec[i + 1] != 0xC0 || rec[i + 9] < 0xE0 || rec[i + 9] > 0xE4) return false;     // pages 0-4 (R42 adds 4)
            for (int k = 0; k < 10; k++) if (rec[i + k] < 0 || rec[i + k] > 255) return false;
            return rec[i + 10] >= 0;
        }

        // decoded telemetry of one step
        public class Tele
        {
            public long[] sig = new long[65536], sigMax = new long[65536], lvl = new long[65536], px = new long[65536], py = new long[65536], pp = new long[65536], freq = new long[65536];
            public int nSig = 0, nPos = 0, nRep = 0, nTouch = 0, nFreq = 0;
            // R41, for the hover steps: every fresh in-range frequency reading whatever the pressure (airFreq), and the
            // in-range position reports, with how many of them carried pressure (with the pen in the air: false presses)
            public long[] airFreq = new long[65536];
            public int nAirFreq = 0, nIn = 0, nPress = 0;
            public long flashCount = -1; public int flashStatus = -1;
            public double seconds = 0;
        }

        public static Tele Decode(int[] rec, bool lightOnly)
        {
            Tele t = new Tele();
            long lastP = -1; bool lastIn = false;
            int positionTime = -1, firstTime = -1, lastTime = -1;
            for (int i = 0; i + 10 < rec.Length; i += 11)
            {
                if (!TelemetryRecord(rec, i)) continue;
                int now = rec[i + 10];
                if (lastTime >= 0 && now < lastTime) { lastIn = false; positionTime = -1; continue; }
                if (firstTime < 0) firstTime = now;
                lastTime = now;
                t.nRep++;
                int pg = rec[i + 9] & 7;
                if (pg == 4) continue;      // R42 Dual-coil probe page (old measurement builds; Dual-coil Mode is gone in R43)
                if (pg == 1)
                {
                    lastP = Lo(rec[i + 6], rec[i + 7]); lastIn = (rec[i + 8] & 0x20) != 0;
                    positionTime = now;
                    if (lastP > 16383 || Lo(rec[i + 2], rec[i + 3]) > 16000 || Lo(rec[i + 4], rec[i + 5]) > 9000) lastIn = false;
                    if (lastIn && lastP >= LIGHT_TOUCH) t.nTouch++;
                    if (lastIn) { t.nIn++; if (lastP > 0) t.nPress++; }
                    if (lastIn && (!lightOnly || lastP < LIGHT_TOUCH) && t.nPos < 65536) { t.px[t.nPos] = Lo(rec[i + 2], rec[i + 3]); t.py[t.nPos] = Lo(rec[i + 4], rec[i + 5]); t.pp[t.nPos] = lastP; t.nPos++; }
                }
                if (pg == 3)
                {
                    if (rec[i + 7] == 1) { t.flashCount = Lo(rec[i + 2], rec[i + 3]) | (Lo(rec[i + 4], rec[i + 5]) << 16); t.flashStatus = rec[i + 6]; }
                    continue;
                }
                // Page 2 reports the firmware's tracked resonance estimate, quantized in 16 Hz steps.
                // Pressure shifts the pen's resonance, so only fresh light-contact/hover readings are summarized.
                if (pg == 2 && t.nFreq < 65536 && lastIn && lastP < LIGHT_TOUCH && positionTime >= 0 && now - positionTime <= 100000)
                {
                    long hz = Lo(rec[i + 2], rec[i + 3]) * 16;
                    if (hz >= 457000 && hz <= 540000) t.freq[t.nFreq++] = hz;
                }
                if (pg == 2 && t.nAirFreq < 65536 && lastIn && positionTime >= 0 && now - positionTime <= 100000)
                {
                    long hz = Lo(rec[i + 2], rec[i + 3]) * 16;
                    if (hz >= 457000 && hz <= 540000) t.airFreq[t.nAirFreq++] = hz;
                }
                if (pg == 0 && t.nSig < 65536)
                {
                    if (!lastIn || positionTime < 0 || now - positionTime > 100000) continue;
                    if (lightOnly && lastP >= LIGHT_TOUCH) continue;
                    long x = Lo(rec[i + 2], rec[i + 3]), y = Lo(rec[i + 4], rec[i + 5]);
                    if (x == 0 || y == 0) continue;                 // a line set not measured this frame
                    t.sig[t.nSig] = x < y ? x : y; t.sigMax[t.nSig] = x > y ? x : y; t.lvl[t.nSig] = Lo(rec[i + 6], rec[i + 7]); t.nSig++;
                }
            }
            if (lastTime > firstTime && firstTime >= 0) t.seconds = (lastTime - firstTime) / 1e6;
            return t;
        }

        public static string ResonanceText(Tele t) { return ResonanceText(t.freq, t.nFreq); }
        public static string ResonanceText(long[] f, int n)
        {
            if (n == 0) return "Pen frequency (resting): no readings.";
            return "Pen frequency (resting): " + F.Fx(Pct(f, n, 0.5) / 1000.0, 3) + " kHz ("
                + F.Fx(Pct(f, n, 0) / 1000.0, 3) + " to " + F.Fx(Pct(f, n, 1) / 1000.0, 3)
                + " kHz, " + F.Str(n) + " readings, as the firmware tracks it).";
        }

        // R45-rc2: sorted with Array.Sort (an ALPI training has hundreds of thousands of readings)
        public static long Pct(long[] a, int n, double q)
        {
            if (n == 0) return 0;
            long[] b = new long[n];
            P.Copy(a, b, n);
            Array.Sort(b);
            int k = (int)(q * (n - 1));
            return b[k];
        }

        public class CalResult
        {
            public string preset = "";
            public string resonance = "Pen frequency (resting): not measured.";
            public int pick, drop, boostAt;
            public long airMed, airMax;                       // R41: pen frequency in the air (normal + highest hover)
            public int nAir, nAirHigh, airIn, airPress;
            public double oddMm, noiseMm, rate, pressureRest;
            public long away, contact, hover, edge, levelHover, flashCount = -1;
            public int flashStatus = -1;
            public StrList notes = new StrList();
            public bool ok = false;
            public bool background = false;                   // R45-rc2: from an ALPI training's readings (FromTraining)
        }


        // R41: the firmware's pen frequency while the pen is in the air (normal and highest hover). Pressure starts at
        // PRESS_HZ, so a reading at or above it, or a report with pressure, means this build can show pressure while the
        // pen hovers. This is the hardware check for short frequency readings (it replaces a separate --pen-resonance run).
        public const long PRESS_HZ = 509000;
        static void AirFrequency(CalResult r, Tele a, Tele b)
        {
            int n = a.nAirFreq + b.nAirFreq;
            r.nAir = n; r.airIn = a.nIn + b.nIn; r.airPress = a.nPress + b.nPress; r.nAirHigh = 0;
            if (n == 0) return;
            long[] all = new long[n];
            for (int i = 0; i < a.nAirFreq; i++) all[i] = a.airFreq[i];
            for (int i = 0; i < b.nAirFreq; i++) all[a.nAirFreq + i] = b.airFreq[i];
            r.airMed = Pct(all, n, 0.5); r.airMax = Pct(all, n, 1);
            for (int i = 0; i < n; i++) if (all[i] >= PRESS_HZ) r.nAirHigh++;
        }
        public static bool AirPressRisk(CalResult r) { return r.airPress > 0 || r.nAirHigh > 0; }
        public static string AirText(CalResult r)
        {
            if (r.nAir == 0) return "Pen frequency (hovering): no readings.";
            // R45-rc2: a training's reading with pressure cannot be told from a light touch, so it says nothing about
            // pressure in the air
            if (r.airIn < 0)
                return "Pen frequency (hovering): " + F.Fx(r.airMed / 1000.0, 3) + " kHz, highest " + F.Fx(r.airMax / 1000.0, 3)
                    + " kHz. Pressure starts at " + F.Fx(PRESS_HZ / 1000.0, 0) + " kHz (" + F.Str(r.nAirHigh) + " of " + F.Str(r.nAir)
                    + " readings without pressure at or above it).";
            return "Pen frequency (hovering): " + F.Fx(r.airMed / 1000.0, 3) + " kHz, highest " + F.Fx(r.airMax / 1000.0, 3)
                + " kHz. Pressure starts at " + F.Fx(PRESS_HZ / 1000.0, 0) + " kHz (" + F.Str(r.nAirHigh) + " of " + F.Str(r.nAir)
                + " readings at or above it). Pressure in the air: "
                + (r.airPress == 0 ? "none" : F.Str(r.airPress) + " of " + F.Str(r.airIn) + " reports") + ".";
        }
        // what to do when the hover readings reached the pressure band, for this preset and these settings
        public static string AirAdvice(CalResult r, Level lv, bool[] st)
        {
            if (!AirPressRisk(r)) return null;
            string s = "Hovering readings reached the pressure range, so this build can show pressure with the pen in the air. ";
            if (Build.Unavailable("accp", lv, st) != null)
                return s + P.Upper(lv.name) + "'s frame is too short for full-length readings: use RAPID or slower with Accurate Pressure Readings.";
            if (!Build.On(st, "accp")) return s + "Accurate Pressure Readings was off: turn it on in the Noise-Related Patches.";
            return s + "This was with Accurate Pressure Readings on, so your pen sits close to where pressure starts. Please report these numbers.";
        }

        public static CalResult Derive(Tele[] t) { return Derive(t, Build.Lv("500")); }

        // the guided steps' readings (R45-rc2: light touch, normal hover, highest hover)
        public static CalResult Derive(Tele[] t, Level lv)
        {
            CalResult r = new CalResult();
            Tele rest = t[0], hov = t[1], edge = t[2];
            r.resonance = ResonanceText(rest);
            AirFrequency(r, hov, edge);
            r.away = -1;       // telemetry is sent only while tracking: silence does not measure the receiver's noise
            r.contact = Pct(rest.sig, rest.nSig, 0.5);
            r.hover = Pct(hov.sig, hov.nSig, 0.5);
            r.edge = edge.nSig > 0 ? Pct(edge.sig, edge.nSig, 0.5) : 0;
            r.levelHover = Pct(hov.lvl, hov.nSig, 0.5);
            r.pressureRest = rest.nPos > 0 ? Pct(rest.pp, rest.nPos, 0.5) : 0;
            r.rate = rest.seconds > 0 ? rest.nRep / rest.seconds : 0;
            for (int k = 0; k < t.Length; k++) if (t[k].flashCount >= 0) { r.flashCount = t[k].flashCount; r.flashStatus = t[k].flashStatus; }
            if (hov.nPos > 4) { double sx = Twin.Sd(hov.px, hov.nPos), sy = Twin.Sd(hov.py, hov.nPos); r.noiseMm = (sx > sy ? sx : sy) / 100.0; }
            if (rest.nSig < 5 || rest.nPos < 5) { r.notes.Add(rest.nRep == 0 ? "No calibration reports while resting lightly. Check the calibration firmware. " + DRIVER_EXIT : "Not enough tracked position and signal readings below pressure " + F.Str(LIGHT_TOUCH) + " while resting lightly; repeat the step."); return r; }
            if (hov.nSig < 5 || hov.nPos < 5) { r.notes.Add("Not enough readings at 5 mm. Check the live readout and repeat the step."); return r; }
            if (r.noiseMm > 2.5) { r.notes.Add("The pen moved more than a stationary hover should. Repeat the 5 mm hold before applying measured thresholds."); return r; }
            Thresholds(r, lv);
            if (r.edge == 0) r.notes.Add("The highest hover position was not tracked. Keeping the preset's pickup and drop thresholds.");
            r.boostAt = 0;       // keep the preset's stage transitions: fast-stage samples do not validate hover stages
            r.oddMm = 4 * r.noiseMm > 0.5 ? 4 * r.noiseMm : 0.5;
            r.notes.Add("These reports carry no no-pen noise, so they say nothing about signal-to-noise.");
            r.notes.Add("Only the pick-up and drop levels were measured; everything else stays at its default. Check the hover height on the tablet.");
            r.ok = true;
            return r;
        }

        // the fast stage's pick-up and drop levels from the highest steady hover's signal (r.edge, 0 = not seen) and the
        // normal hover's (r.hover): one rule for the guided steps and (R45-rc2) the ALPI training. Never above the preset's
        // own, so the pickup range never shrinks.
        public static void Thresholds(CalResult r, Level lv)
        {
            int pickCap = Build.Pick(lv.samples, lv.burst, 1.0), dropCap = Build.Drop(lv.samples, lv.burst, 1.0);
            long floorv = 24;
            long target = r.edge > 0 ? r.edge : r.hover / 2;
            long drop = (long)(0.75 * target);
            if (drop < floorv) drop = floorv;
            long pick = (long)(1.3 * drop);
            if (pick < drop + 16) pick = drop + 16;
            long pmax = (long)(0.9 * r.hover);
            if (pick > pmax && pmax > drop + 8) pick = pmax;
            if (drop > dropCap) drop = dropCap;
            if (pick > pickCap) pick = pickCap;
            if (pick <= drop) pick = drop + 1;
            r.drop = (int)drop; r.pick = (int)pick;
            if (r.edge == 0) { r.pick = pickCap; r.drop = dropCap; }
        }

        // R45-rc2 (Anargon: "ALPI should combine all calibration steps as necessary, running them in the background"):
        // what the guided steps measure, from an ALPI training's readings. The training firmware keeps the fast stage
        // down to MEASURE_DROP, like the measurement firmware. Light touch: fast-stage readings with pressure under
        // LIGHT_TOUCH. Normal hover: the centre hold (else every fast-stage hover reading). Highest steady hover: the
        // weakest EDGE_PCT of the fast-stage hover readings (the training asks for a high hover "while the cursor still
        // follows" over the whole area). The pen's frequency at a light touch and hovering. Not measured: pressure in
        // the air (here a reading with pressure cannot be told from a light touch) and the report rate.
        public const double EDGE_PCT = 0.05;
        public const int EDGE_MIN = 1000;                 // fast-stage hover readings needed for the highest hover
        public static CalResult FromTraining(ApipData d, ApipData hold, Level lv, bool fullRes)
        {
            CalResult r = new CalResult();
            r.background = true; r.preset = lv.key; r.away = -1; r.airIn = -1; r.airPress = -1;
            int bias = fullRes ? Build.FULLRES_BIAS : 0;         // levels are kept in rounded readings, like a calibration file
            long[] rest = new long[d.n + 1], pr = new long[d.n + 1], hov = new long[d.n + 1], fr = new long[d.n + 1], fa = new long[d.n + 1];
            int nr = 0, nh = 0, nfr = 0, nfa = 0;
            for (int i = 0; i < d.n; i++)
            {
                int p = d.Pressure(i), sg = d.sig[i] - bias;
                long hz = d.Hz(i);
                bool fast = d.Stage(i) == 0, inBand = hz >= 457000 && hz <= 540000;
                if (p > 0 && p < LIGHT_TOUCH)
                {
                    if (fast && sg > 0) { rest[nr] = sg; pr[nr] = p; nr++; }
                    if (inBand) fr[nfr++] = hz;
                }
                else if (p == 0)
                {
                    if (fast && sg > 0) hov[nh++] = sg;
                    if (inBand) { fa[nfa++] = hz; if (hz >= PRESS_HZ) r.nAirHigh++; }
                }
            }
            r.resonance = ResonanceText(fr, nfr);
            r.nAir = nfa;
            if (nfa > 0) { r.airMed = Pct(fa, nfa, 0.5); r.airMax = Pct(fa, nfa, 1); }
            r.contact = Pct(rest, nr, 0.5);
            r.pressureRest = nr > 0 ? Pct(pr, nr, 0.5) : 0;
            int hn = hold != null ? hold.n : 0, nhs = 0;
            long[] hs = new long[hn + 1], hl = new long[hn + 1], hx = new long[hn + 1], hy = new long[hn + 1];
            for (int i = 0; i < hn; i++)
            {
                hx[i] = hold.x[i]; hy[i] = hold.y[i];
                int sg = hold.sig[i] - bias;
                if (hold.Stage(i) == 0 && sg > 0) { hs[nhs] = sg; hl[nhs] = hold.lvl[i]; nhs++; }
            }
            if (hn > 4) { double sx = Twin.Sd(hx, hn), sy = Twin.Sd(hy, hn); r.noiseMm = (sx > sy ? sx : sy) / (100.0 * Apip.FINE); }
            r.hover = nhs >= 20 ? Pct(hs, nhs, 0.5) : Pct(hov, nh, 0.5);
            r.levelHover = nhs >= 20 ? Pct(hl, nhs, 0.5) : 0;
            r.edge = nh >= EDGE_MIN ? Pct(hov, nh, EDGE_PCT) : 0;
            if (r.edge >= r.hover) r.edge = 0;                   // nothing weaker than the normal hover
            if (nr < 20) r.notes.Add("Few light-touch readings (" + F.Str(nr) + "): rest the tip lightly now and then while training.");
            if (r.hover <= 0) { r.notes.Add("No hover readings in the fast stage, so nothing was measured."); return r; }
            Thresholds(r, lv);
            if (r.edge == 0) r.notes.Add("Too few readings at a high hover in the fast stage (" + F.Str(nh) + " hover readings): the build keeps the preset's pick-up and drop levels.");
            r.boostAt = 0;
            r.oddMm = 4 * r.noiseMm > 0.5 ? 4 * r.noiseMm : 0.5;
            r.ok = true;
            return r;
        }

        public static StrList Describe(CalResult r)
        {
            StrList l = new StrList();
            l.Add(r.resonance);
            l.Add(AirText(r));
            if (r.background)
            {
                l.Add("Signal strength: light touch " + F.Str(r.contact) + ", centre hold " + F.Str(r.hover) + ", highest hover " + (r.edge > 0 ? F.Str(r.edge) + " (the weakest " + F.Fx(EDGE_PCT * 100, 0) + "% of the hover readings)" : "not seen"));
                l.Add("Light-touch pressure " + F.Fx(r.pressureRest, 0) + ", shake in the centre hold " + F.Fx(r.noiseMm, 3) + " mm");
            }
            else
            {
                l.Add("Signal strength: resting " + F.Str(r.contact) + ", 5 mm " + F.Str(r.hover) + ", highest hover " + (r.edge > 0 ? F.Str(r.edge) : "not seen"));
                l.Add("Report rate " + F.Fx(r.rate, 1) + " RPS, light-rest pressure " + F.Fx(r.pressureRest, 0) + ", shake at 5 mm " + F.Fx(r.noiseMm, 3) + " mm");
            }
            if (r.flashCount >= 0) l.Add("Flash counter in the bootloader area: " + F.Str(r.flashCount) + "/100,000" + (r.flashStatus == 0 ? " (written)" : " (NOT written: the bootloader area looks write-protected)"));
            if (r.ok)
            {
                l.Add("Values for your build:");
                l.Add("    Pick up above " + F.Str(r.pick) + ", drop below " + F.Str(r.drop) + " (never above this speed's default)");
                if (r.boostAt > 0) l.Add("    Steady Hover starts below level " + F.Str(r.boostAt) + " (60% of your 5 mm reading)");
                if (!r.background) l.Add("    Jump Rejection margin " + F.Fx(r.oddMm, 2) + " mm (4x the shake, at least 0.5 mm)");
            }
            for (int i = 0; i < r.notes.Count(); i++) l.Add("Note: " + r.notes.Get(i));
            return l;
        }

        // Pen-only telemetry cannot measure the untracked receiver's noise or the second-coil gain.
        public static string ProfileHint(CalResult r)
        {
            if (!r.ok || r.hover <= 0) return null;
            return "Measured levels need the stock scan frequency (Pen frequency tuning off).";
        }

        public static void SaveCal(string path, CalResult r)
        {
            StringBuilder sb = new StringBuilder("# Anargon's Tablet Tools signal thresholds " + P.Now("stamp") + "\r\n");
            sb.Append("# " + r.resonance + "\r\n");
            sb.Append("format=2\r\nprofile=default-fast-stock-frequency\r\n");
            sb.Append("preset=" + r.preset + "\r\npick=" + F.Str(r.pick) + "\r\ndrop=" + F.Str(r.drop) + "\r\nboost_at=" + F.Str(r.boostAt) + "\r\nodd_mm=" + F.Fx(r.oddMm, 3) + "\r\n");
            sb.Append("signal_rest=" + F.Str(r.contact) + "\r\nsignal_5mm=" + F.Str(r.hover) + "\r\nsignal_highest=" + F.Str(r.edge) + "\r\n");
            sb.Append("level_5mm=" + F.Str(r.levelHover) + "\r\nnoise_mm=" + F.Fx(r.noiseMm, 3) + "\r\nreport_rate=" + F.Fx(r.rate, 1) + "\r\npressure_rest=" + F.Fx(r.pressureRest, 0) + "\r\n");
            sb.Append("air_freq_median=" + F.Str(r.airMed) + "\r\nair_freq_max=" + F.Str(r.airMax) + "\r\nair_freq_readings=" + F.Str(r.nAir) + "\r\nair_freq_in_press_band=" + F.Str(r.nAirHigh)
                + "\r\nair_reports=" + F.Str(r.airIn) + "\r\nair_press_reports=" + F.Str(r.airPress) + "\r\n");
            P.AtomicWriteAllBytes(path, Build.Encode(sb.ToString()));
        }

        static int CalInteger(string v, string name)
        {
            if (P.Len(v) > 5 || !Digits(v)) throw new Stop("Invalid calibration " + name + ": expected a decimal number from 0 to 65535.");
            long n = F.ParseInt(v);
            if (n > 65535) throw new Stop("Invalid calibration " + name + ": value exceeds 65535.");
            return (int)n;
        }

        // Parse and validate the entire file before applying any values to the build.
        public static string LoadCal(string path, Options o)
        {
            string[] lines;
            try { lines = P.Lines(P.Ascii(P.ReadAllBytes(path))); }
            catch (Exception) { throw new Stop("Cannot read calibration file: " + path); }
            string preset = "", format = "", profile = "";
            int pick = -1, drop = -1, boost = -1; double odd = -1;
            StrList seen = new StrList();
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = P.Trim(lines[i]);
                int eq = P.IndexOf(ln, "=");
                if (P.Len(ln) == 0 || P.StartsWith(ln, "#")) continue;
                if (eq <= 0) throw new Stop("Invalid calibration line " + F.Str(i + 1) + ": expected name=value.");
                string k = P.Trim(P.Sub(ln, 0, eq)), v = P.Trim(P.Sub(ln, eq + 1, P.Len(ln) - eq - 1));
                for (int j = 0; j < seen.Count(); j++) if (P.Eq(seen.Get(j), k)) throw new Stop("Duplicate calibration value: " + k);
                seen.Add(k);
                if (P.Eq(k, "preset")) preset = v;
                else if (P.Eq(k, "format")) format = v;
                else if (P.Eq(k, "profile")) profile = v;
                else if (P.Eq(k, "pick")) pick = CalInteger(v, k);
                else if (P.Eq(k, "drop")) drop = CalInteger(v, k);
                else if (P.Eq(k, "boost_at")) boost = CalInteger(v, k);
                // R42 files may hold dual_gain_* / dual_readings: Dual-coil Mode is gone (R43), so they are ignored
                else if (P.Eq(k, "odd_mm"))
                {
                    try { odd = P.ParseDouble(v); }
                    catch (Exception) { throw new Stop("Invalid calibration odd_mm: expected a number from 0.5 to 10."); }
                }
            }
            if (!P.Eq(format, "2") || !P.Eq(profile, "default-fast-stock-frequency"))
                throw new Stop("This calibration file does not record a verified measurement profile. Recalibrate with this version of the tool.");
            Level lv = null;
            for (int i = 0; i < Build.LEVELS.Length; i++) if (P.Eq(Build.LEVELS[i].key, preset) && Build.LEVELS[i].period > 0) lv = Build.LEVELS[i];
            if (lv == null || drop <= 0 || pick <= drop || boost != 0 || !(odd >= 0.5 && odd <= 10))
                throw new Stop("Invalid calibration values: use one non-STOCK preset, pick > drop > 0, boost_at=0, and odd_mm from 0.5 to 10.");
            if (pick > Build.Pick(lv.samples, lv.burst, 1.0) || drop > Build.Drop(lv.samples, lv.burst, 1.0))
                throw new Stop("Calibration thresholds would reduce this preset's pickup range. Recalibrate with this version of the tool.");
            o.calPick = pick; o.calDrop = drop;
            if (o.oddMm < 0) o.oddMm = odd;
            return preset;
        }

        // the calibration-pass build: the chosen preset, fast stage only, low thresholds, telemetry on
        public static string BuildPass1(byte[] app, InputInfo info, Level lv, bool[] stUser, string folder, bool realtime)
        {
            string profileError = Build.CalibrationProfileError(stUser, new Options());
            if (profileError != null) throw new Stop(profileError);
            bool[] st = new bool[stUser.Length];
            P.Copy(stUser, st, stUser.Length);
            // Bootloader maintenance belongs to the final normal flash, with its power-on wait. R43: 12-bit Readings off,
            // so the measured thresholds are in rounded readings like every saved file (a 12-bit build adds the bias).
            string[] off = new string[] { "boost", "reach", "longrange", "cal", "odd", "bootloader", "blcounter", "fullres" };
            for (int i = 0; i < off.Length; i++) st[Build.SetIndex(off[i])] = false;
            Options o1 = new Options(); o1.telemetry = true; o1.calPick = MEASURE_PICK; o1.calDrop = MEASURE_DROP;
            string outp = Build.MarkVolatile(P.PathJoin(folder, lv.name + "-measurement.hex"), st, lv);
            Build.BuildOne(app, info, outp, lv, st, true, false, o1);
            gateHeading = Heading(lv, st) + " - measurement build";
            int gm = Gate(outp, Build.VolatileOn(st, lv) ? 1 : 3, info.truncated, realtime);
            gateHeading = null;
            return gm == 1 ? outp : null;
        }

        public static CalResult RunSteps(CalSource src, bool interactive) { return RunSteps(src, interactive, Build.Lv("500"), false); }
        public static CalResult RunSteps(CalSource src, bool interactive, Level lv) { return RunSteps(src, interactive, lv, false); }

        // RESTING: the readiness check has just had the tip resting lightly on the middle, so (R45-rc2) the first step
        // measures at once instead of asking for the same position again
        public static CalResult RunSteps(CalSource src, bool interactive, Level lv, bool resting)
        {
            int ns = CAL_TITLES.Length;
            Tele[] t = new Tele[ns];
            bool zoneWarned = false;
            for (int k = 0; k < ns; k++)
            {
                // R44: the LED marks the step's target (the middle) and blinks while recording
                int target = ZONE_CENTRE;
                bool zoned = src.Zone(target, false);
                if (interactive)
                {
                    Screen.Step("Calibration " + F.Str(k + 1) + "/" + F.Str(ns) + ": " + CAL_TITLES[k]);
                    Screen.Para(CAL_ASK[k], 4);
                    P.Line("");
                    Screen.Para(k == 0 ? NO_CURSOR : "No cursor: do not press the pen's buttons or use the mouse.", 4);
                    Screen.Para(LED_LEGEND, 4);
                    if (!zoned && !zoneWarned)
                    {
                        Screen.Para("The tablet did not take the LED target, so the LED only shows whether it sees the pen. Use the readout to check the position.", 4);
                        zoneWarned = true;
                    }
                    if (k == 2)
                    {
                        string position;
                        do
                        {
                            P.Line("");
                            Ask("    Press Enter for a 10 s live preview to find the detection limit > ");
                            src.Collect(k, 10.0);       // positioning only; keep these readings out of the timed hold
                            position = P.Lower(P.Trim(Ask("    Keep the pen steady. Enter = measure this height for 4 s; r = repeat positioning > ")));
                        } while (P.Eq(position, "r"));
                    }
                    else if (k == 0 && resting) { P.Line(""); P.Line("    The tip is already resting there: measuring now (" + F.Fx(CAL_SECS[k], 0) + " s)."); }
                    else { P.Line(""); Ask("    Press Enter to start (" + F.Fx(CAL_SECS[k], 0) + " s) > "); }
                }
                resting = false;                                // a repeat asks again
                src.Zone(target, true);
                src.Collect(k, 0.3);                            // discard the transition after moving to a new hold
                int[] rec = src.Collect(k, CAL_SECS[k]);
                src.Zone(target, false);
                t[k] = Decode(rec, k == 0);
                if (interactive && t[k].nRep == 0)
                {
                    P.Line("        No calibration reports during this step. The pen may be outside the detection area.");
                    Screen.Para("If reports with another ID arrive, check the calibration firmware. " + DRIVER_EXIT, 8);
                }
                else if (interactive && k == 0 && t[k].nSig < 5)
                    P.Line(t[k].nTouch > 0 ? "        Pressure reached " + F.Str(LIGHT_TOUCH) + " or more. Rest the tip more lightly and repeat this step." : "        Not enough tracked light-contact readings. Keep the tip on the tablet and repeat this step.");
                if (interactive)
                {
                    P.Line("");
                    string answer = P.Lower(P.Trim(Ask("    Enter = " + (k < ns - 1 ? "next step" : "finish") + "; r = repeat this step > ")));
                    if (P.Eq(answer, "r")) k--;
                }
            }
            return Derive(t, lv);
        }

        public static bool SimCalibrate(byte[] img, string label)
        {
            TwinSource src = new TwinSource(); src.img = img;
            P.Line("== calibration steps played in the digital twin (integrating receiver): " + label);
            try
            {
                string err = src.Open();
                if (P.Len(err) > 0) { P.Line("   " + err); return false; }
                CalResult r = RunSteps(src, false);
                StrList l = Describe(r);
                for (int i = 0; i < l.Count(); i++) P.Line("   " + l.Get(i));
                if (r.ok)
                {
                    r.preset = "sim";
                    SaveCal(label + ".calibration.txt", r);
                    P.Line("   saved " + label + ".calibration.txt");
                }
                P.Line("RESULT calibrate pick=" + F.Str(r.pick) + " drop=" + F.Str(r.drop) + " boost_at=" + F.Str(r.boostAt) + " odd_mm=" + F.Fx(r.oddMm, 3) + " ok=" + (r.ok ? "1" : "0"));
                return r.ok;
            }
            finally { src.Close(); }
        }

        public const int USE_DEFAULTS = 0, READY = 1, REFLASH = 2;
        public const int READY_AUTO = 3;                    // automatic re-checks before the user is asked
        public static double readyPause = 1.0;              // seconds between an automatic re-check's close and reopen

        // Before the timed steps: the calibration firmware must be sending signal and position readings. A failed check
        // reopens the tablet and checks again by itself (READY_AUTO times; Windows can list the tablet's collections a
        // moment after it reconnects), then asks: Enter = check again, f = flash the calibration build again,
        // d = continue with the preset defaults. It never falls through to the defaults without that answer.
        public static int ReadyCheck(CalSource src)
        {
            P.Line(""); P.Line("    Waiting for the tablet...");
            string err = src.Open();
            if (P.Len(err) == 0)
            {
                src.Zone(ZONE_CENTRE, false);          // R44: the LED shines when the tip is within 7 mm of the middle
                P.Line("");
                Screen.Para(NO_CURSOR, 4);
                P.Line("");
                Screen.Para("First, rest the tip lightly on the middle of the drawing area, with almost no weight (under 4096 on the readout). The LED shines when the tip is within 7 mm of the middle. This checks that the calibration firmware is sending readings, then measures this light touch straight away: keep the tip there.", 4);
                Ask("    Press Enter with the tip resting lightly on the tablet > ");
            }
            int auto = 0;
            while (true)
            {
                string problem;
                if (P.Len(err) == 0)
                {
                    src.Collect(0, 0.3);
                    Tele ready = Decode(src.Collect(0, 1.0), true);
                    if (ready.nSig >= 5 && ready.nPos >= 5)
                    {
                        if (auto > 0) P.Line("    Calibration readings are arriving.");
                        return READY;
                    }
                    problem = ReadyProblem(ready, src.Why());
                    // only a readable tablet is re-checked automatically; a failed open already waited 20 s
                    if (auto < READY_AUTO)
                    {
                        auto++;
                        if (auto == 1) Screen.Para(problem, 4);
                        P.Line("    Reopening the tablet and checking again (" + F.Str(auto) + " of " + F.Str(READY_AUTO) + ")...");
                        src.Close(); P.Sleep(readyPause); err = src.Open();
                        continue;
                    }
                }
                else problem = "The tablet could not be opened for calibration: " + err;
                P.Line("");
                Screen.Para(problem, 4);
                if (P.Len(err) > 0)
                {
                    // the setup help has its own line breaks (Linux udev rule): one paragraph per line
                    string[] help = P.Lines(SetupHelp());
                    P.Line("");
                    for (int i = 0; i < help.Length; i++) { if (P.Len(P.Trim(help[i])) == 0) P.Line(""); else Screen.Para(help[i], 4); }
                }
                P.Line("");
                P.Line("    Enter = check again (keep the tip resting lightly on the tablet's middle)");
                P.Line("    f     = flash the calibration build again (one more update), then check again");
                P.Line(calStandalone ? "    d     = stop measuring (nothing is saved)" : "    d     = skip calibration; the final build uses the default pick-up levels");
                string a;
                while (true)
                {
                    a = P.Lower(P.Trim(Ask("    Enter, f or d > ")));
                    if (P.Len(a) == 0 || P.Eq(a, "f") || P.Eq(a, "d")) break;
                }
                if (P.Eq(a, "d")) return USE_DEFAULTS;
                if (P.Eq(a, "f")) return REFLASH;
                src.Close(); err = src.Open();
            }
        }

        // why the readiness check failed, from the readings and from what the reader saw instead (CalSource.Why)
        public static string ReadyProblem(Tele ready, string why)
        {
            if (ready.nRep > 0)
                return ready.nTouch > 0 ? "Readings are arriving, but pressure is " + F.Str(LIGHT_TOUCH) + " or more. Rest the tip more lightly."
                    : "Readings are arriving, but not enough with the pen in range. Keep the tip resting lightly on the middle of the drawing area.";
            if (P.IndexOf(why, "reports arrive with ID") >= 0)
                return "The tablet is sending other reports, not calibration readings (" + why + "). The calibration firmware may not be running: flash it again (f).";
            if (P.Len(why) == 0)
                return "The tablet sends normal pen reports, not calibration readings, so the calibration firmware is probably not running: flash it again (f).";
            if (P.IndexOf(why, "no reports at all") >= 0)
                return "No reports are arriving from the tablet (" + why + "). " + DRIVER_EXIT + " Keep the pen tip resting on the tablet's middle.";
            return "Reading the tablet failed: " + why + ".";
        }

        // the whole calibration: pass 1 build + flash, guided steps, results into O; true if values were applied
        public static bool Calibrate(byte[] app, InputInfo info, Level lv, bool[] st, string folder, Options o, bool realtime)
        {
            string profileError = Build.CalibrationProfileError(st, o);
            if (profileError != null) throw new Stop(profileError);
            bool dfu = false;
            Screen.Step(calStandalone ? "Tablet signal thresholds: measurement firmware" : "Signal thresholds pass 1/2: measurement firmware");
            string pass1 = BuildPass1(app, info, lv, st, folder, realtime);
            if (pass1 == null) { P.Line(""); P.Line("    The calibration build failed its check; calibration skipped."); Ask("    Press Enter to continue > "); return false; }
            P.Line("");
            Screen.Para("Next: flash the calibration firmware. While it runs there is no cursor: the tablet sends readings to this app instead. Do not press the pen's buttons or use the mouse until calibration is done; use only the keyboard.", 4);
            P.Line("");
            Screen.Para("Before flashing, exit your tablet driver: " + DRIVER_HOW, 4);
            P.Line("");
            if (!BeforeFlash(folder, info)) { P.Line(""); Ask("    Calibration skipped. Press Enter to continue > "); return false; }
            dfu = backupLeftDfu;
            if (!FlashSteps(pass1, dfu, true)) { P.Line("    Calibration stopped because the measurement update did not complete successfully; some firmware may already have been written."); return false; }
            CalSource src = calSource != null ? calSource : new HidSource();
            CalResult r;
            try
            {
                // A failed check does not end calibration: it checks again, and the user can flash the same checked
                // calibration build again (this run's backup is reused) or choose the preset defaults.
                while (true)
                {
                    int next = ReadyCheck(src);
                    if (next == READY) break;
                    if (next == USE_DEFAULTS) { P.Line(""); P.Line(calStandalone ? "    Measuring stopped; nothing was saved." : "    Calibration skipped; the final build uses the default pick-up levels."); return false; }
                    src.Close();
                    if (!BeforeFlash(folder, info)) { P.Line(""); Ask("    Calibration skipped. Press Enter to continue > "); return false; }
                    if (!FlashSteps(pass1, backupLeftDfu, true)) { P.Line("    Calibration stopped because the measurement update did not complete successfully; some firmware may already have been written."); return false; }
                }
                r = RunSteps(src, true, lv, true);
            }
            finally { src.Close(); }
            r.preset = lv.key;
            string airAdvice = AirAdvice(r, lv, st);
            if (airAdvice != null) r.notes.Add(airAdvice);
            if (r.flashCount >= 0) SetCounterWarn(folder, (int)r.flashCount);
            Screen.Step("Tablet signal threshold results");
            StrList l = Describe(r);
            for (int i = 0; i < l.Count(); i++) { P.Line("    " + l.Get(i)); Build.LOG.Main("   calibration: " + l.Get(i)); }
            if (!r.ok) { P.Line(""); Ask(calStandalone ? "    Nothing was saved. Press Enter to continue > " : "    Press Enter to build with the default pick-up levels > "); return false; }
            string cp = P.PathJoin(folder, "calibration-" + lv.name + ".txt");
            SaveCal(cp, r);
            o.calPick = r.pick; o.calDrop = r.drop;
            if (o.boostAt < 0 && r.boostAt > 0) o.boostAt = r.boostAt;
            if (o.oddMm < 0) o.oddMm = r.oddMm;
            o.note = ProfileHint(r);
            P.Line(""); P.Line("    Saved to " + cp + " (reuse it with --calibration)");
            P.Line(""); Screen.Ask(calStandalone ? "    Press Enter to continue > " : "    Press Enter for pass 2/2: build the calibrated firmware > ");
            return true;
        }

        // ---- flashing with xppen-tools (erase -> program -> CRC verify -> vector verify -> finalize)
        public static string FindTool()
        {
            string set = Conf.XppenTools();
            if (P.Len(set) > 0) return P.FileExists(set) ? P.FullPath(set) : null;
            string[] dirs = new string[] { P.ExeDir(), "." };
            string[] names = new string[] { "xppen-tools.exe", "xppen-tools" };
            for (int i = 0; i < dirs.Length; i++) for (int k = 0; k < names.Length; k++) if (P.FileExists(P.PathJoin(dirs[i], names[k]))) return P.FullPath(P.PathJoin(dirs[i], names[k]));
            return null;
        }

        // xppen-tools is expected next to this program, alongside the backups and exports: commands run in that folder
        // and are shown without paths
        static string Tool() { return "xppen-tools"; }
        static string ToolDir() { string t = FindTool(); return t != null ? P.DirName(t) : "."; }
        static string Rel(string path)
        {
            // Resolve in the caller's working directory, before xppen-tools changes its working directory.
            return P.FullPath(path);
        }
        static bool AlreadyDfu(string output)
        {
            string o = P.Lower(output);
            return P.IndexOf(o, "already in dfu") >= 0 || P.IndexOf(o, "bootloader detected") >= 0;
        }
        // puts the tablet in DFU mode; an error because it already is in DFU just continues
        static bool EnterDfu(string tool)
        {
            P.Line("        $ " + Tool() + " dfu " + XPPEN_IDS);
            string[] result = P.RunInResult(ToolDir(), tool, "dfu " + XPPEN_IDS, "        ", true);
            if (!P.Eq(result[0], "0") && !AlreadyDfu(result[1])) { P.Line("    Could not enter update mode (exit status " + result[0] + ")."); return false; }
            P.Sleep(2.0);
            return true;
        }

        // Complete backups are kept for every capture; never combine one tablet's app with another's bootloader.
        // last-dump/ic902-main.bin is the latest successful capture; dumps/<date-time>/ keeps immutable full copies.
        public static string DumpsDir(string folder) { return P.PathJoin(folder, "dumps"); }
        public static string PrimaryDump(string folder) { return P.PathJoin(P.PathJoin(DumpsDir(folder), "primary-dump"), "ic902-main.bin"); }
        // the tablet's firmware as read by the latest backup of this run (null: no backup yet)
        public static byte[] tabletApp = null;
        public static byte[] tabletFlash = null;         // the whole flash as the latest backup read it
        public static string tabletBuild = null;
        public static bool tabletModded = false;
        // this run's successful complete backup (startup or before a flash); null: none yet. One backup per run:
        // later flashes in the same run use it instead of making a second one.
        public static string sessionBackup = null;
        // the last BeforeFlash left the tablet in update mode (its own fresh capture) - FlashSteps then skips `dfu`
        public static bool backupLeftDfu = false;
        // R45-rc5 (4), atf_modcheck.cs: what the latest firmware check found (null: none since the start or the last
        // flash); this run uses the backup on file instead of a dump (modified firmware); return to stock's bootloader
        // area was checked by CRC and is stock (instead of reading the whole flash again)
        public static FwState tabletState = null;
        public static bool dumpSkipped = false, bootRestoredByCrc = false;

        // makes a backup; returns the file kept, or null (no backup: nothing may be flashed)
        public static string Backup(string folder) { return Backup(folder, false, false); }
        // inDfu: the tablet is in update mode already (no `dfu`); toRuntime: back to normal mode afterwards (`exit-dfu`)
        public static string Backup(string folder, bool inDfu, bool toRuntime)
        {
            StartupBackup.Wait();
            tabletApp = null; tabletFlash = null; tabletBuild = null; tabletModded = false;
            BackupCapture capture = new BackupCapture();
            capture.folder = P.FullPath(folder); capture.tool = FindTool(); capture.echo = true; capture.inDfu = inDfu; capture.restoreRuntime = toRuntime;
            capture.Run();
            if (capture.flash == null || capture.error != null) { P.Line("    " + capture.error); return null; }
            Note(capture.folder, capture.flash, capture.path, "whole flash (before update)");
            if (capture.incomplete != null) P.Line("    " + capture.incomplete);
            sessionBackup = capture.path; dumpSkipped = false;
            P.Line("    Backup saved: " + capture.path + "   (list: " + P.PathJoin(DumpsDir(capture.folder), "dumps.txt") + ")");
            if (toRuntime) P.Line(capture.restored ? "    The tablet is back in normal mode." : "    The tablet may still be in update mode (leave it with: " + Tool() + " exit-dfu --pid " + PID_DFU + ").");
            else P.Line("    The tablet is in DFU mode now (to leave without flashing: " + Tool() + " exit-dfu --pid " + PID_DFU + ").");
            return capture.path;
        }

        // one line in dumps/dumps.txt, and what the tablet runs (for the check before flashing)
        public static void Note(string folder, byte[] flash, string kept, string kind)
        {
            byte[] app = new byte[0x10000 - 0x3800];
            P.Copy(flash, 0x3800, app, 0, app.Length);
            tabletApp = app; tabletFlash = flash;
            tabletBuild = Build.ReadBuildString(app, app.Length);
            tabletModded = (tabletBuild != null && P.StartsWith(tabletBuild, Build.VERSION)) || (app[0x18C] & 0xFF) != 0x30;
            FwCheck ck = Repair.Examine(app, app.Length);
            if (ck.Damaged())
            {
                P.Line("");
                Screen.Box("DAMAGED FIRMWARE ON THE TABLET", "The end of the tablet's firmware is erased (from 0x0800C188). Flashing any complete image fixes it; " + Repair.TITLE + " (option 2 at start-up) also restores the settings page. Keep this backup: " + kept);
            }
            else if (ck.Edited())
            {
                P.Line("    " + Repair.TabletText(ck));
                for (int i = 0; i < ck.how.Count(); i++) P.Line("      - " + ck.how.Get(i));
            }
            int cnt = Build.ReadFlashCount(flash);
            string list = P.PathJoin(DumpsDir(folder), "dumps.txt");
            if (!P.FileExists(list)) P.AppendText(list, "# Anargon's Tablet Tools: backup history, oldest first. New captures keep the whole flash.\r\n# last-dump/ic902-main.bin is the latest complete capture; dumps/<date-time>/ keeps each full copy.\r\n");
            P.AppendText(list, P.Now("stamp") + "  " + Rel(kept) + "  " + kind + "  firmware " + (tabletBuild != null ? tabletBuild : "(no build string)")
                + (tabletModded ? " (a build of this tool)" : "") + "  crc32 " + F.HexDigits(Txt.Crc32(app, 0, app.Length), 8, true) + "  counter " + (cnt >= 0 ? F.Str(cnt) + " (" + SourceOf(flash) + ")" : "none") + "\r\n");
        }

        public static bool HasBackup(string folder)
        {
            if (P.FileExists(PrimaryDump(folder))) return true;
            string b = P.PathJoin(folder, "backups");
            if (!P.DirExists(b)) return false;
            string[] d = P.ListDirs(b);
            for (int i = 0; i < d.Length; i++) if (P.FileExists(P.PathJoin(d[i], "ic902-main.bin"))) return true;
            return false;
        }

        // open issue 8: the image must come from the firmware build the tablet runs (read by the backup just made).
        // A stock tablet is compared by its build string; a tablet running a build of this tool (whose build string is
        // replaced) by its code layout. Returns true if flashing may go on.
        public static bool TabletMatches(InputInfo info)
        {
            if (tabletApp == null) return false;
            string have;
            bool same;
            if (!tabletModded)
            {
                have = tabletBuild != null ? tabletBuild : "(no build string)"; same = tabletBuild != null && P.Eq(tabletBuild, info.build);
                if (dumpSkipped) have = "modified firmware made from " + have + " (your backup on file; the dump was skipped)";
            }
            else
            {
                Layout lt = Layout.For(tabletApp, 0, tabletApp.Length, null);
                string kb = lt.KnownByCode();
                have = "a build of this tool made from " + (kb != null ? kb : "an unknown firmware build");
                same = info.layout != null && lt.SameCode(info.layout);
            }
            if (same) return true;
            StrList L = new StrList();
            L.Add("WARNING:");
            L.Add("");
            L.Add("THIS IMAGE WAS BUILT FROM A DIFFERENT FIRMWARE THAN THE ONE ON YOUR TABLET.");
            L.Add("");
            L.Add("Your tablet runs: " + have + ". This image was built from: " + (info.build != null ? info.build : "(no build string)") + ".");
            L.Add("");
            L.Add("Build from your tablet's own dump instead (" + (sessionBackup != null ? sessionBackup : "last-dump/ic902-main.bin") + "). Flashing this one anyway is untested: the tablet may not start until stock firmware is flashed back.");
            string ans = Screen.DangerAsk(L, "Type \"I know what I am doing.\" verbatim to flash anyway, or press Enter to stop > ");
            return P.Eq(P.Trim(ans), "I know what I am doing.");
        }

        // backup + firmware check before a flash; true if the flash may go on. Pass backupLeftDfu to FlashSteps.
        // One backup per run: a complete backup already made in this run (the startup one included) is used, and a
        // startup backup that is still running is waited for - no second backup is started.
        // R41: measurement firmware is rebuilt from the firmware file at any time, so replacing it needs no new backup
        // (Anargon: back up the real firmware, not reconstructable measurement builds). This applies only while this
        // tool's calibration telemetry is arriving right now and this folder keeps a complete earlier backup (the
        // first one, dumps/primary-dump); that backup then stands in for the checks. Anything else is backed up as before.
        public static CalSource measurementProbe = null;      // tests replace the tablet reader
        public static bool MeasurementRunning()
        {
            int[] r;
            if (measurementProbe != null)
            {
                if (P.Len(measurementProbe.Open()) > 0) return false;
                try { r = measurementProbe.Collect(1, 1.0); } finally { measurementProbe.Close(); }
            }
            else
            {
                if (P.Len(P.HidOpenPen()) > 0) return false;
                try { r = P.HidCollect(1.0); } finally { P.HidClose(); }
            }
            return Decode(r, false).nRep > 0;
        }
        public static bool MainBackupForMeasurement(string folder)
        {
            string main = PrimaryDump(folder);
            if (!P.FileExists(main)) return false;
            byte[] flash = P.ReadAllBytes(main);
            if (flash.Length != 0x10000 || !MeasurementRunning()) return false;
            byte[] app = new byte[0x10000 - 0x3800];
            P.Copy(flash, 0x3800, app, 0, app.Length);
            tabletApp = app; tabletFlash = flash;
            tabletBuild = Build.ReadBuildString(app, app.Length);
            tabletModded = (tabletBuild != null && P.StartsWith(tabletBuild, Build.VERSION)) || (app[0x18C] & 0xFF) != 0x30;
            sessionBackup = main;
            return true;
        }

        public static bool BeforeFlash(string folder, InputInfo info) { return BeforeFlash(folder, info, false); }
        // fresh: read the tablet again even if this run has a backup (return-to-stock must verify what its first
        // image did to the bootloader before it flashes the second)
        public static bool BeforeFlash(string folder, InputInfo info, bool fresh)
        {
            Screen.Step(fresh ? "Reading the tablet again before flashing" : "Backup before flashing");
            if (!fresh && StartupBackup.Running())
            {
                P.Line("    The startup backup is still running. Waiting for it instead of starting a second backup.");
                StartupBackup.Wait();
            }
            backupLeftDfu = false; bootRestoredByCrc = false;
            if (!fresh && sessionBackup != null && tabletApp != null)
            {
                Screen.Para((dumpSkipped ? "Using your backup on file again (this run skipped the dump of the modified firmware): "
                    : "Using this run's complete backup (no second backup): ") + sessionBackup + ". Permanent copies are in dumps/ and listed in dumps/dumps.txt.", 4);
            }
            else if (!fresh && MainBackupForMeasurement(folder))
            {
                Screen.Para("The tablet runs this tool's measurement firmware, which can be rebuilt any time, so it is not backed up again. Using your earlier backup: " + sessionBackup + ".", 4);
            }
            else
            {
                // R45-rc5 (4): modified firmware need not be dumped (atf_modcheck.cs). The firmware check runs here when it
                // can lead to a skip (a backup on file) and none ran since the start or the last flash.
                bool inDfu = false, skip = false;
                FwState fs = fresh ? null : tabletState;
                string tool = FindTool();
                if (fs == null && tool != null && ModCheck.BackupOnFile(folder) != null)
                {
                    Screen.Para("Checking the tablet's firmware first (a few CRCs in update mode, seconds): modified firmware need not be dumped.", 4);
                    P.Line("");
                    if (EnterDfu(tool))
                    {
                        inDfu = true;
                        fs = ModCheck.Query(new ShownRunner(tool), folder);
                        tabletState = fs;
                        if (fs.kind != FwState.MODDED) { P.Line("    The tablet runs " + fs.Text() + "."); P.Line(""); }   // modified: named just below
                    }
                }
                if (fs != null && fs.kind == FwState.MODDED)
                {
                    string no = ModCheck.NoSkip(folder, fs);
                    if (no != null) Screen.Para("The tablet runs " + fs.Text() + ". It is dumped anyway: " + no + ".", 4);
                    else if (ModCheck.AskSkip(folder, fs, fresh))
                    {
                        if (!fresh) skip = true;
                        else
                        {
                            // return to stock: the whole flash was read only to check the bootloader area
                            int b = inDfu ? ModCheck.BootRestored(new ShownRunner(tool), info.boot) : -1;
                            if (b >= 0)
                            {
                                skip = true; bootRestoredByCrc = b == 1;
                                P.Line(b == 1 ? "    The bootloader area is stock again and the counter pages are erased." : "    The bootloader area is NOT back to stock.");
                            }
                            else P.Line("    The bootloader area could not be checked by CRC, so the whole flash is read instead.");
                        }
                    }
                    P.Line("");
                }
                if (skip) { ModCheck.UseBackupOnFile(folder, fs); backupLeftDfu = inDfu; }
                else
                {
                    Screen.Para(fresh ? "Reading the tablet's complete flash again (about 3 minutes), to check the result of the previous step before flashing."
                        : "Backing up the tablet first (about 3 minutes, once per run). Latest: last-dump/ic902-main.bin. Every copy is kept in dumps/ (list: dumps/dumps.txt).", 4);
                    P.Line("");
                    if (Backup(folder, inDfu, false) == null) { P.Line(""); Screen.Ask("    No backup, no flashing. Press Enter to continue > "); return false; }
                    backupLeftDfu = true;
                }
            }
            P.Line("");
            SetCounterWarn(folder, -1);                     // the backup read the tablet's own counter
            if (!TabletMatches(info))
            {
                P.Line(""); P.Line("    Not flashing." + (backupLeftDfu ? " Leave DFU mode with: " + Tool() + " exit-dfu --pid " + PID_DFU : ""));
                return false;
            }
            return true;
        }

        // flashing: this program runs xppen-tools, and xppen-tools itself asks you to type its confirmation
        // (UPDATE 28BD:FFF1 RUNTIME=28BD:2904) before it writes anything
        public static bool FlashSteps(string outpath, bool inDfu, bool measurementUpdate)
        {
            P.Line("");
            Screen.Box("FLASHING", "This replaces the tablet's firmware. The image passed the twin checks, and your backup is kept. Keep the tablet plugged in until the update finishes. xppen-tools asks you to type a confirmation first.");
            string tool = FindTool();
            string upd = "update " + BackupCapture.Quote(P.FullPath(outpath)) + " " + XPPEN_IDS;
            if (tool == null)
            {
                P.Line("    xppen-tools was not found next to this program. Put it there, or run these yourself in its folder:");
                if (!inDfu) P.Line("        " + Tool() + " dfu " + XPPEN_IDS);
                P.Line("        " + Tool() + " dump " + BackupCapture.Quote(P.FullPath(P.PathJoin("last-dump", P.Now("stamp")))) + "      (keep the complete dump)");
                P.Line("        " + Tool() + " " + upd);
                P.Line("    No update was run by this program.");
                return false;
            }
            if (!inDfu && !EnterDfu(tool)) return false;
            P.Line("        $ " + Tool() + " " + upd);
            // A failing update can have programmed the image before verification or reconnect failed.
            if (measurementUpdate) calibrationFirmwareActive = true;
            string[] result = P.RunInResult(ToolDir(), tool, upd, "        ", true);
            bool ok = P.Eq(result[0], "0") && P.IndexOf(result[1], "[ERROR]") < 0;
            P.Line(ok ? "    xppen-tools completed the update successfully." : "    Update failed (exit status " + result[0] + "). See the tool output above.");
            if (ok) Store.BumpFlashCount();                 // successful updates performed by this PC
            if (ok) tabletState = null;                     // the tablet runs other firmware now: check it again
            if (ok) calibrationFirmwareActive = measurementUpdate;
            return ok;
        }

        public static void PreviewFlash(string outpath)
        {
            P.Line("    Flash dry run: commands only; no tablet commands were run.");
            P.Line("        xppen-tools dfu " + XPPEN_IDS);
            P.Line("        xppen-tools dump " + BackupCapture.Quote(P.FullPath("last-dump")));
            P.Line("        xppen-tools update " + BackupCapture.Quote(P.FullPath(outpath)) + " " + XPPEN_IDS);
        }

        public static bool Flash(string outpath, string pid, string runtimePid, int retries, bool dry)
        {
            if (dry) { PreviewFlash(outpath); return true; }
            return FlashSteps(outpath, false, false);
        }

        // flash counter: kept by the firmware in the bootloader area, read back from the tablet's dumps
        public static int Flashes(string folder) { int n = FlashesOrNeg(folder); return n > 0 ? n : 0; }
        // the flash count read from the tablet's own dumps, or -1 if no dump carries the tablet's flash counter
        public static int FlashesOrNeg(string folder) { return DumpCount(folder).n; }

        // A missing or changed stock site cannot be made compatible by confirming a warning.
        public static bool DangerIncompat(CompatResult cr)
        {
            Screen.Box("FIRMWARE COMPATIBILITY CHECK FAILED", "These modifications do not match the input firmware: "
                + Compat.List(cr) + ". Building stopped. Use an untouched stock dump, or have this firmware looked at so it can be supported.");
            return false;
        }

        // the last warning after flashing a Bootloader Counter build: its first start rewrites the bootloader's start-up
        // pages (about 0.1 s each); losing power in that moment needs BOOT0/UART or SWD recovery
        public static void UnplugWarning() { PowerWait(false); }
        // R45: after flashing a build that removes the Bootloader Counter (same first-start rewrite)
        public static void RemovalWarning() { PowerWait(true); }
        static void PowerWait(bool cleanup)
        {
            StrList L = new StrList();
            L.Add("DO NOT UNPLUG THE TABLET.");
            L.Add("");
            L.Add("On its first start, this firmware " + (cleanup ? "removes the Bootloader Counter and restores the start-up pages" : "installs or verifies the Bootloader Counter") + ". That takes a fraction of a second; losing power during it can need a hardware repair (see " + Conf.Teardown() + ").");
            L.Add("");
            L.Add("Leave it plugged in for 10 seconds after flashing. No unplugging, restarting or sleeping the PC until then.");
            double waitUntil = P.NowSec() + 10.0;
            try { Screen.DangerAsk(L, "Press Enter once 10 seconds have passed > "); }
            finally
            {
                double remaining = waitUntil - P.NowSec();
                if (remaining > 0) { P.Line("    Keeping the update session open for the remaining power-on wait..."); P.Sleep(remaining); }
            }
        }

        // shown after a caught major error during flashing: the tablet's firmware may now be unstable
        public static void DangerCritical()
        {
            StrList L = new StrList();
            L.Add("A CRITICAL ERROR HAS OCCURRED THAT MAY MAKE THIS FIRMWARE UNSTABLE.");
            L.Add("");
            L.Add("Keep your backups. If xppen-tools still reaches the tablet, make a fresh full dump first.");
            L.Add("");
            L.Add("Run " + Repair.TITLE + " (option 2 at start-up) on that dump. If xppen-tools cannot reach the tablet, follow " + Conf.Teardown() + ".");
            Screen.DangerAsk(L, "Press Enter to acknowledge > ");
        }

        // Read-only diagnostics: no startup backup, DFU request, or firmware update.
        public static int CheckHid(bool resonance)
        {
            P.Line("Reading the tablet through the operating system HID driver (no WinUSB installation).");
            P.Line(resonance ? "Hold the pen still with no tip pressure for five seconds. Measurement firmware must already be installed."
                : "Move the pen over the tablet for five seconds. Stock or modified firmware can be checked.");
            try
            {
                string error = P.HidOpenPen();
                // Windows: every collection of the tablet and how it is read, including the ones Windows keeps to itself
                string[] how = P.HidDescribe();
                if (how.Length > 0) { P.Line("Tablet HID collections:"); for (int i = 0; i < how.Length; i++) P.Line("  " + how[i]); }
                if (P.Len(error) > 0) { P.Line(error); P.Line(SetupHelp()); return 1; }
                int[] records = P.HidCollect(5.0);
                int[] counts = P.HidReportCounts();
                bool received = false;
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0) { received = true; P.Line("  Report ID " + F.Str(i) + ": " + F.Str(counts[i]) + " received"); }
                if (!received) { P.Line("No input reports received. " + P.HidWhy()); return 1; }
                P.Line("PC HID communication confirmed by received reports.");
                Tele measured = Decode(records, true);
                if (!resonance)
                {
                    P.Line(measured.nRep > 0 ? "Measurement telemetry is present."
                        : "Measurement telemetry is absent; ordinary pen reports still confirm the HID connection.");
                    return 0;
                }
                P.Line(ResonanceText(measured));
                P.Line("This is the firmware's tracked resonance estimate, not an independent resonance-peak sweep.");
                if (measured.nFreq == 0)
                    P.Line("Use a checked --calibration-pass build, then retry with the pen in range and no tip pressure. Restore normal firmware afterwards.");
                return measured.nFreq > 0 ? 0 : 1;
            }
            finally { P.HidClose(); }
        }

        // Native tablet access depends on OS permissions and whether another tablet driver holds the device.
        public static string SetupHelp()
        {
            if (P.IsWindows()) return "Windows: nothing to install. If reading fails, close OpenTabletDriver and the XP-Pen driver, replug the tablet and retry. Dumping and flashing need xppen-tools next to this program.";
            if (P.IsMac()) return "macOS: tablet reads use IOKit. You may need to allow your terminal app in System Settings > Privacy & Security > Input Monitoring, then restart the terminal and retry; a permission prompt is not guaranteed. Close OpenTabletDriver and the XP-Pen driver while calibrating. Dumping and flashing also require xppen-tools.";
            return "Linux: use a supported distribution with the native libraries required by .NET. Your user also needs read access to the tablet's device files. As root, save these two lines as /etc/udev/rules.d/70-deco640.rules:\n\n" + P.UdevRule()
                + "\nthen run:  sudo udevadm control --reload-rules && sudo udevadm trigger   and replug the tablet.\n(Or run this program with sudo.) Close OpenTabletDriver while calibrating.";
        }

        public static string CounterText(int n, string src)
        {
            return "Recorded updates: " + F.Str(n) + "/100,000 [According to " + src + "]. The chip is rated for at least 100,000 flashes (GD32F350 datasheet minimum)."
                + (P.Eq(src, "Firmware") ? " The Firmware count adds one per new build of this tool, so other updates are not in it." : P.Eq(src, "local PC") ? " The local PC count is this PC's updates and can include more than one tablet." : "");
        }

        // The flash count and where it came from, best source first. The tablet itself always wins over the PC: one PC
        // may flash several tablets, each with its own counter.
        //   1. Bootloader / Firmware, live: the tablet answers USB string 173 (Patches.CounterString, every build of this
        //      tool since rev 35) - no dump needed;
        //   2. Bootloader / Firmware, from what the tablet itself reported this run (calibration telemetry: tabletNow);
        //   3. Bootloader / Firmware, from the tablet's own dumps (dumps/, older backups);
        //   4. local PC: the hidden store, which counts the flashes this PC performed.
        // "Bootloader" = the Bootloader Counter is installed and counts every update; "Firmware" = the firmware's own
        // counter (one record per new build).
        public class CountReading { public int n = -1; public string src = null; }
        public static CountReading LiveCount()
        {
            CountReading c = new CountReading();
            string t = Conf.Get("ADT_TEST_COUNT_STRING", "");                  // tests only: stands in for the tablet
            if (P.Len(t) == 0) t = P.HidIndexedString(0x28BD, 0x2904, Patches.COUNT_STRING);
            if (t == null || P.Len(t) != 14 || !P.StartsWith(t, "ADTC1:") || P.CharAt(t, 7) != ':') return c;
            int s = P.CharAt(t, 6);
            string num = P.Sub(t, 8, 6);
            if (!Digits(num)) return c;
            if (s == 'N') { c.src = "N"; return c; }                                  // the tablet answers: no counter
            if (s == 'B') c.src = "Bootloader"; else if (s == 'F') c.src = "Firmware"; else return c;
            c.n = (int)F.ParseInt(num);
            return c;
        }
        public static string SourceOf(byte[] flash64k)
        {
            return flash64k.Length >= 0x10000 && Machine.LE(flash64k, Sched.BL_ERASE - 0x08000000, 4) == Sched.BlPatchWord() ? "Bootloader" : "Firmware";
        }
        public static CountReading DumpCount(string folder)
        {
            CountReading c = new CountReading();
            StrList files = new StrList();
            files.Add(P.PathJoin(P.PathJoin(folder, "last-dump"), "ic902-main.bin"));
            files.Add(P.PathJoin(folder, "ic902-main.bin")); files.Add("ic902-main.bin"); files.Add(PrimaryDump(folder));
            string b = P.PathJoin(folder, "backups");
            if (P.DirExists(b)) { string[] d = P.ListDirs(b); for (int i = 0; i < d.Length; i++) files.Add(P.PathJoin(d[i], "ic902-main.bin")); }
            string fullSrc = null;
            for (int i = 0; i < files.Count(); i++)
            {
                string f = files.Get(i);
                if (!P.FileExists(f)) continue;
                byte[] fl = P.ReadAllBytes(f);
                int n = Build.ReadFlashCount(fl);
                if (n >= 0 && fullSrc == null) fullSrc = SourceOf(fl);
                if (n > c.n) { c.n = n; c.src = SourceOf(fl); fullSrc = c.src; }
            }
            string list = P.PathJoin(DumpsDir(folder), "dumps.txt");
            if (P.FileExists(list))
            {
                string[] ln = P.Lines(P.Ascii(P.ReadAllBytes(list)));
                for (int i = 0; i < ln.Length; i++)
                {
                    int k = P.IndexOf(ln[i], "  counter ");
                    if (k < 0 || P.StartsWith(ln[i], "#")) continue;
                    string rest = P.Trim(P.Sub(ln[i], k + 10, P.Len(ln[i]) - k - 10));
                    int sp = P.IndexOf(rest, " ");
                    string v = sp > 0 ? P.Sub(rest, 0, sp) : rest;
                    if (!Digits(v) || P.Len(v) > 6) continue;
                    int n = (int)F.ParseInt(v);
                    if (n > c.n)
                    {
                        c.n = n;
                        if (P.IndexOf(rest, "(Bootloader)") >= 0) c.src = "Bootloader";
                        else if (P.IndexOf(rest, "(Firmware)") >= 0) c.src = "Firmware";
                        else c.src = fullSrc != null ? fullSrc : "Firmware";             // a rev 34 line: as the full dumps show
                    }
                }
            }
            return c;
        }
        public static void SetCounterWarn(string folder, int tabletNow)
        {
            CountReading c = LiveCount();
            bool saysNone = c.src != null && P.Eq(c.src, "N");          // the tablet itself reports no counter: old dumps are stale
            if (c.n < 0 && !saysNone && tabletNow >= 0)
            {
                // calibration telemetry: the count is the tablet's; where it is kept shows in the backup just made
                c.n = tabletNow;
                c.src = tabletFlash != null ? SourceOf(tabletFlash) : DumpCount(folder).src;
                if (c.src == null) c.src = "Firmware";
            }
            if (c.n < 0 && !saysNone) c = DumpCount(folder);
            if (c.n >= 0 && c.src != null && !P.Eq(c.src, "N")) { Screen.SetWarn("Recorded updates", CounterText(c.n, c.src)); return; }
            int pc = Store.FlashCount();
            Screen.SetWarn("Recorded updates", CounterText(pc > 0 ? pc : 0, "local PC"));
        }

        public static bool MayFlash(InputInfo info, int nres, int passed)
        {
            if (nres != 1) { P.Line(""); P.Line("    Flashing is done one build at a time: re-run with one preset to flash."); return false; }
            if (info.truncated) { P.Line(""); P.Line("    Not flashing: the input file was truncated, so the build is incomplete."); return false; }
            if (passed != 1) { P.Line(""); P.Line("    The build FAILED the digital-twin check. Not flashing."); return false; }
            return true;
        }

        public static int BuildAll(byte[] app, InputInfo info, Level[] levels, bool[] st, string outdir, string outfile, bool binterval, bool alsoBin, Options o, bool realtime, StrList outs, int[] passedEach)
        {
            int ok = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                string name = Name(levels[i], st, o);
                string outp = outfile != null ? Build.MarkVolatile(outfile, st, levels[i]) : (P.Eq(outdir, "") || P.Eq(outdir, ".") ? name : P.PathJoin(outdir, name));
                if (i > 0) { P.Line(""); P.Line("  " + Screen.Rule('=', 94)); }
                P.Line("");
                Build.BuildOne(app, info, outp, levels[i], st, binterval, alsoBin, o);
                gateHeading = Heading(levels[i], st);
                int g = Gate(outp, binterval && Build.VolatileOn(st, levels[i]) ? 1 : 3, info.truncated, realtime);
                gateHeading = null;
                passedEach[i] = g;
                if (g == 1) ok++;
                outs.Add(outp);
            }
            return ok;
        }

        static string Ask(string prompt) { return Screen.Ask(prompt); }
        public static string Name(Level lv, bool[] st, Options o)
        {
            string n = Build.AutoName(lv, st, o.Custom());
            if (Build.RemovesCounter(lv, st, o)) n = P.Sub(n, 0, P.Len(n) - 4) + "-noBC.hex";   // R45: removes the Bootloader Counter
            if (o.calPick > 0) n = P.Sub(n, 0, P.Len(n) - 4) + "-ST.hex";
            return o.lab ? P.Sub(n, 0, P.Len(n) - 4) + "-LAB.hex" : n;
        }
        static bool Digits(string s) { if (P.Len(s) == 0) return false; for (int i = 0; i < P.Len(s); i++) { int c = P.CharAt(s, i); if (c < '0' || c > '9') return false; } return true; }
        static int IntegerOption(string text, string option)
        {
            string value = P.Trim(text);
            bool hex = P.StartsWith(P.Lower(value), "0x");
            if ((!hex && (!Digits(value) || P.Len(value) > 10)) || (hex && (P.Len(value) < 3 || P.Len(value) > 10)))
                throw new Stop(option + " needs a whole number from 0 to 2147483647.");
            long number = F.ParseInt(value);
            if (number < 0 || number > int.MaxValue) throw new Stop(option + " is outside 0 to 2147483647.");
            return (int)number;
        }
        static double NumberOption(string text, string option)
        {
            double number;
            try { number = P.ParseDouble(text); }
            catch (Exception) { throw new Stop(option + " needs a finite number."); }
            if (!(number >= 0 && number <= 1000000)) throw new Stop(option + " needs a finite number from 0 to 1000000.");
            if (number == 0 && !P.Eq(option, "--odd-mm")) throw new Stop(option + " must be greater than zero.");
            return number;
        }
        public static bool[] Defaults() { bool[] st = new bool[Build.SETTINGS.Length]; for (int i = 0; i < st.Length; i++) st[i] = Build.SETTINGS[i].dflt; return st; }

        public static void InputWarnings(InputInfo info)
        {
            if (info.undone != null) Screen.Warn(info.undone);
            if (info.truncated) Screen.Warn(Build.TRUNC_WARN + " " + Build.TRUNC_HELP);
            if (!Build.Verified(info.build) && !info.modded) Screen.Warn(Build.BUILD_WARN + " " + Build.BuildWarn2() + " " + Build.LayoutNote(info));
        }

        // stock firmware to patch: the tablet's own dump (ic902-main) first, then deco01v3 files; modded images are skipped
        public static readonly string[] CANDIDATES = new string[] { "ic902-main.bin", "ic902-main.hex", "deco01v3-main.bin", "deco01v3-main.hex", "deco01v3-stock.hex" };

        public static string PickInput(string folder, StrList notes)
        {
            string[] dirs = new string[] { Folders.Of(Folders.IMPORT), ".", folder, P.ExeDir() };     // R44: the import folder first
            for (int d = 0; d < dirs.Length; d++)
            {
                string latest = P.PathJoin(P.PathJoin(dirs[d], "last-dump"), "ic902-main.bin");
                if (!P.FileExists(latest)) continue;
                InputInfo inf = new InputInfo();
                try { Build.ReadInput(latest, inf); } catch (Stop) { continue; }
                if (!inf.modded) return latest;
                notes.Add("The latest backup contains modified firmware. Choose an untouched stock dump to build from.");
            }
            for (int k = 0; k < CANDIDATES.Length; k++)
            {
                for (int d = 0; d < dirs.Length; d++)
                {
                    string f = P.PathJoin(dirs[d], CANDIDATES[k]);
                    if (!P.FileExists(f)) continue;
                    InputInfo inf = new InputInfo();
                    try { Build.ReadInput(f, inf); } catch (Stop) { continue; }
                    if (inf.modded) { notes.Add(CANDIDATES[k] + " is a build of this tool, not stock firmware: skipped."); break; }
                    return f;
                }
            }
            return CANDIDATES[0];
        }

        // R44: step 1 also takes a folder ("Input File Directory"): the first stock candidate in it, as PickInput
        // orders them; null when it holds none
        public static string InputInFolder(string dir, StrList notes)
        {
            string latest = P.PathJoin(P.PathJoin(dir, "last-dump"), "ic902-main.bin");
            string[] files = new string[CANDIDATES.Length + 1];
            files[0] = latest;
            for (int k = 0; k < CANDIDATES.Length; k++) files[k + 1] = P.PathJoin(dir, CANDIDATES[k]);
            for (int k = 0; k < files.Length; k++)
            {
                if (!P.FileExists(files[k])) continue;
                InputInfo inf = new InputInfo();
                try { Build.ReadInput(files[k], inf); } catch (Stop) { continue; }
                if (inf.modded) { notes.Add(files[k] + " is a build of this tool, not stock firmware: skipped."); continue; }
                return files[k];
            }
            return null;
        }

        // the file of this run's tablet backup: the running startup backup's target, else this run's finished backup
        static string RunBackup()
        {
            StartupBackup.Poll();
            if (StartupBackup.Running()) return StartupBackup.Target();
            return sessionBackup;
        }

        // The startup backup keeps running in the background through these steps (never wait for it here). It is
        // waited for only when its own file is chosen as the input and does not exist yet, before calibration and
        // before flashing (BeforeFlash), and at exit.
        public static int Wizard(string inpath, string outdir, bool realtime)
        {
            string here = outdir != null ? outdir : ".";
            SetCounterWarn(here, -1);
            Screen.Warn(Build.WARRANTY);
            Screen.Warn(Build.VOLATILE_WARN);
            StrList notes = new StrList();
            string dflt = PickInput(here, notes);
            for (int i = 0; i < notes.Count(); i++) Screen.WarnOnce(notes.Get(i), notes.Get(i));   // "X is a build of this tool: skipped" - informational, once only
            // nothing to build from yet, but the startup backup is reading the tablet: offer the file it will write
            if (!P.FileExists(dflt) && StartupBackup.Running()) dflt = StartupBackup.Target();
            bool haveDump = P.FileExists(P.PathJoin(P.PathJoin(P.ExeDir(), "last-dump"), "ic902-main.bin")) || P.FileExists(P.PathJoin(Folders.Of(Folders.IMPORT), "ic902-main.bin"))
                || P.FileExists(P.PathJoin(here, "ic902-main.bin")) || P.FileExists("ic902-main.bin") || HasBackup(here) || StartupBackup.Running();
            if (!haveDump)
                Screen.WarnOnce("no_tablet_dump", "No tablet dump was found. Put ic902-main.bin (from xppen-tools) or deco01v3-main.bin in the import folder. This tool backs up the tablet before any flash.");
            string path;
            InputInfo info;
            byte[] app;
            while (true)
            {
                Screen.Step("Step 1/6: Stock firmware file");
                Screen.Para("This tool searches first for ic902-main.bin, made by xppen-tools (your tablet's latest backup, then the import folder). If that is not found, it falls back to deco01v3-main.bin.", 4);
                P.Line("");
                P.Line("    Enter = " + (P.FileExists(dflt) || StartupBackup.Running() ? P.FullPath(dflt) : "nothing found yet: put a firmware file in " + Folders.Of(Folders.IMPORT)));
                P.Line("");
                string own = RunBackup();
                if (own != null)
                {
                    P.Line("    b = this run's tablet backup" + (StartupBackup.Running() ? " (not finished: " + StartupBackup.State() + "; choosing it waits for it)" : "") + ":");
                    P.Line("        " + own);
                    P.Line("");
                }
                // R45-rc5 (4): the startup backup stopped before its dump (modified firmware; atf_modcheck.cs)
                bool later = own == null && StartupBackup.deferred && sessionBackup == null;
                if (later)
                {
                    P.Line("    b = back up the tablet now (about 3 minutes; it runs " + (tabletState != null ? tabletState.Text() : "modified firmware") + ", so the startup backup waited)");
                    P.Line("");
                }
                Screen.Para("You can also type a file, or a folder to search.", 4);
                path = inpath != null ? inpath : P.Trim(Ask("    Input File Directory: > "));
                path = P.Replace(path, "\"", "");
                if (inpath == null && P.Len(path) > 0 && P.DirExists(path))
                {
                    StrList fn = new StrList();
                    string inDir = InputInFolder(path, fn);
                    for (int i = 0; i < fn.Count(); i++) Screen.Warn(fn.Get(i));
                    if (inDir == null) { Screen.SetWarn("File:", "File: " + path + " has no ic902-main.bin or deco01v3-main.bin of stock firmware. Type a file or another folder."); continue; }
                    path = inDir;
                    P.Line("    Using " + path);
                }
                if (inpath == null && (P.Eq(path, "b") || P.Eq(path, "B")))
                {
                    if (own == null && later)
                    {
                        P.Line("");
                        if (Backup(P.ExeDir(), false, true) == null) { Screen.SetWarn("File:", "File: the backup did not complete (see last-dump/backup.log). Type a file name."); continue; }
                    }
                    else if (own == null) { Screen.SetWarn("File:", "File: there is no tablet backup in this run (no startup backup ran, for example because xppen-tools was not found). Type a file name."); continue; }
                    StartupBackup.Wait();                       // returns at once when it has already finished
                    if (sessionBackup == null) { Screen.SetWarn("File:", "File: this run's tablet backup did not complete (see the startup backup warning). Type a file name."); continue; }
                    path = sessionBackup;
                    P.Line("    Using this run's tablet backup: " + path);
                }
                if (P.Len(path) == 0) path = dflt;
                inpath = null;
                // the startup backup has not written this file yet: this is the one place its dump is the input
                if (!P.FileExists(path) && StartupBackup.Running() && P.Eq(P.FullPath(path), P.FullPath(StartupBackup.Target()))) StartupBackup.Wait();
                if (!P.FileExists(path))
                {
                    Screen.SetWarn("File:", "File: " + path + " was not found." + (StartupBackup.deferred && sessionBackup == null
                        ? " The startup backup was not made (the tablet runs modified firmware): b backs the tablet up now." : ""));
                    continue;
                }
                info = new InputInfo();
                try { app = Build.ReadInput(path, info); }
                catch (Stop e) { Screen.SetWarn("File:", "File: " + e.msg); continue; }
                // e.g. the background backup was replacing last-dump/ic902-main.bin at that moment: ask again
                catch (Exception e) { Screen.SetWarn("File:", "File: " + path + " could not be read (" + P.Msg(e) + "). Press Enter to try again."); continue; }
                if (info.modded) { Screen.SetWarn("File:", "File: " + path + " is a build of this tool, not stock firmware. Use a dump of stock firmware or deco01v3-main.bin."); continue; }
                Screen.SetWarn("File:", "");
                break;
            }
            Screen.warnings = Without(Screen.warnings, "");
            Build.CheckInputLogOnly(info);
            InputWarnings(info);
            string d;
            bool modShown = false;
            while (true)
            {
                Screen.Step("Step 2/6: Output folder");
                // R45 (Anargon: the notice says what was modified, and how): once, on the first screen after the input
                if (!modShown && info.undoneHow != null)
                {
                    StringBuilder mb = new StringBuilder();
                    for (int i = 0; i < info.undoneHow.Count(); i++) mb.Append(i > 0 ? "\n" : "").Append(info.undoneHow.Get(i));
                    Screen.Box(info.reverted ? "THIS TOOL'S CHANGES WERE UNDONE" : "ANOTHER MOD'S CHANGES WERE UNDONE", mb.ToString());
                }
                modShown = true;
                string exportDir = Folders.Of(Folders.EXPORT);
                Screen.Para("This tool saves the built firmware files and their logs here.", 4);
                P.Line("");
                P.Line("    Enter = " + exportDir);
                P.Line("");
                d = outdir != null ? outdir : P.Trim(Ask("    Output Folder: > "));
                d = P.Replace(d, "\"", "");
                if (P.Len(d) == 0) d = exportDir;
                P.MakeDirs(d);
                if (P.DirExists(d)) break;
                Screen.SetWarn("Folder:", "Folder: " + d + " cannot be created."); outdir = null;
            }
            Screen.warnings = WithoutPrefix(Screen.warnings, "Folder:");
            SetCounterWarn(d, -1);
            Build.LOG.SetFolder(d);
            int choice;
            int nAll = Build.LEVELS.Length + 1, dfl = Build.LevelIndex("500") + 1;
            while (true)
            {
                Screen.Step("Step 3/6: Preset (how often the tablet reads the pen)");
                Screen.Para("This sets how many times per second the tablet reads the pen. A higher number means less cursor delay, but more noise.", 4);
                P.Line("");
                for (int i = 0; i < Build.LEVELS.Length; i++) { P.Line("    " + F.Str(i + 1) + ".  " + Build.LEVELS[i].label); P.Line(""); }
                P.Line("    " + F.Str(nAll) + ".  All presets (" + F.Str(Build.LEVELS.Length) + " files)");
                P.Line("");
                string c = P.Trim(Ask("    Preset (1-" + F.Str(nAll) + ", Enter = " + F.Str(dfl) + "): > "));
                if (P.Len(c) == 0) c = F.Str(dfl);
                choice = Digits(c) ? (int)F.ParseInt(c) : -1;
                if (choice >= 1 && choice <= nAll) break;
            }
            Level[] levels = choice == nAll ? Build.LEVELS : new Level[] { Build.LEVELS[choice - 1] };
            bool[] st = Defaults();
            Build.CounterDefaults(st);                                   // R45: found -> on; off then removes it
            Level ml = levels[0].period > 0 ? levels[0] : Build.Lv("500");
            Options o = new Options();
            o.blRemove = Build.counterFound;
            o.fallback = true;                              // R45: Anargon's measurements where this run measures none
            o.lab = labWizard;                              // started with --lab: the Filter Arranger build (atf_lab.cs)
            // R45 (Anargon): 4 Calibration, 5 Noise-Related Patches, 6 Extra QOL Patches. Calibration comes first so the
            // measured levels are in hand, and the settings a level unlocks are offered.
            int calLevel = CalibrationStep(app, info, levels, st, d, o, realtime);
            for (int i = 0; i < st.Length; i++) if (LockedBy(Build.SETTINGS[i].key, calLevel) != null) st[i] = false;
            // Anargon: APIP on by default when the user picks the calibration that trains it (the twin: steadier than
            // Position Interpolation at every height, with no measurable delay)
            // R45: the Tracker, the steadiest APIP filter on the real tablet (Anargon's, ULTRA, 2026-11-11: cursor spread
            // with the pen still 0.08x / 0.06x of no filter, Robust 2 trackers 0.18x / 0.20x); the Robust filters are
            // only chosen by hand
            if (calLevel == CAL_FULL && Build.Unavailable(Build.ApipKey(1), levels[0], st) == null) st[Build.SetIndex(Build.ApipKey(1))] = true;
            MeasureCosts(app, info, ml);
            string problem = null;
            int[] map = new int[Build.SETTINGS.Length];
            bool qol = false;
            while (true)
            {
                if (!qol)
                {
                    Screen.Step("Step 5/6: Noise-Related Patches");
                    Screen.Para("Calibration: " + CAL_LEVELS[calLevel] + (calLevel == CAL_FAST ? " (Pen frequency tuning on)" : "") + "; "
                        + (calLevel == CAL_FULL && o.calPick < 0 && o.apip == null && Build.ApipKind(st) > 0 ? "pick-up levels: measured during the ALPI training" : LevelsText(levels, st, o)) + ". The settings that lower noise the most are on. Aggressive Position Interpolation and Movement Smoothing stay off: they add the most cursor delay.", 4);
                }
                else
                {
                    Screen.Step("Step 6/6: Extra QOL Patches");
                    Screen.Para("Hover range and pickup, and what the build may change on the tablet.", 4);
                }
                P.Line("");
                int n = ShowGroup(st, levels, qol ? QOL_CATS : NOISE_CATS, calLevel, map);
                if (qol) Screen.Para(Build.ALWAYS_ON, 4); else DelayNote();
                if (problem != null) { P.Line(""); Screen.Para(problem, 4); }
                P.Line("");
                Screen.Para("Type the numbers of the settings to switch on or off (for example 1 3). Press Enter on its own to continue" + (qol ? ", or type b to go back to the Noise-Related Patches." : "."), 4);
                string sel = P.Trim(Ask("\n    Settings to switch: > "));
                problem = null;
                if (qol && P.Eq(P.Lower(sel), "b")) { qol = false; continue; }
                if (P.Len(sel) == 0)
                {
                    if (!qol) { qol = true; continue; }
                    CompatResult cr = Compat.Check(app, info, ml, st, o);
                    if (cr.any) { DangerIncompat(cr); return 1; }
                    string bad = CheckChoice(app, info, levels, st, o);
                    if (bad == null) { P.Line(""); Ask("    Your settings fit. Press Enter to continue: > "); break; }
                    problem = "Your settings do not fit: " + bad + " (type b to change the Noise-Related Patches)";
                    continue;
                }
                problem = Toggle(sel, st, map, n, calLevel);
            }
            // R44: APIP is trained per preset on this tablet (atf_apip.cs ApipCli)
            if (Build.ApipKind(st) > 0 && o.apip == null)
            {
                bool usable = false;
                string ak = Build.ApipKey(Build.ApipKind(st));
                for (int i = 0; i < levels.Length; i++) if (Build.Unavailable(ak, levels[i], st) == null) usable = true;
                if (usable && levels.Length != 1)
                {
                    Screen.Step("ALPI training");
                    Screen.Para("ALPI is trained for one preset at a time, so these builds are made without it. Choose one preset to use ALPI.", 4);
                    P.Line(""); Ask("    Press Enter > ");
                    Build.ApipOff(st);
                }
                else if (usable && !ApipCli.Train(app, info, levels[0], st, d, o, realtime)) Build.ApipOff(st);
            }
            if (calLevel == CAL_FULL && o.calPick < 0 && o.calDrop < 0 && Build.ApipKind(st) == 0 && levels.Length == 1 && levels[0].period > 0)
            {
                Screen.Step("Signal thresholds");
                int fbk = Build.FallbackFor(levels[0], st, o);
                Screen.Para("ALPI is off, so this build would use " + (fbk >= 0 ? Build.FB_WHO + " for its pick-up levels" + (Build.FB_ODD[fbk] > 0 ? " and Jump Rejection margin" : "") : "the preset's own pick-up levels") + ". Full calibration skipped Standard's measurement because ALPI replaces most of it.", 4);
                P.Line("");
                if (P.Eq(P.Lower(P.Trim(Ask("    Type m to measure them on your tablet now (a measurement firmware and a few minutes), or press Enter to build without: > "))), "m"))
                    Calibrate(app, info, levels[0], st, d, o, realtime);
            }
            Screen.Step("Building");
            StringBuilder ready = new StringBuilder();
            for (int i = 0; i < levels.Length; i++) { if (i > 0) ready.Append(", "); ready.Append(Name(levels[i], st, o)); }
            Screen.Para("This tool will build: " + ready.ToString(), 4);
            Screen.Para("Folder: " + P.FullPath(d), 4);
            P.Line("");
            if (P.Eq(P.Lower(P.Trim(Ask("    Press Enter to build, or type c to cancel: > "))), "c")) return 0;
            Screen.wipe = false;
            StrList outs = new StrList();
            int[] pe = new int[levels.Length];
            int ok;
            try { ok = BuildAll(app, info, levels, st, d, null, true, false, o, realtime, outs, pe); }
            catch (Stop e) { P.Line(""); P.Line("    Build failed: " + e.msg); return 1; }
            if (!MayFlash(info, outs.Count(), pe[0])) return ok == levels.Length ? 0 : 1;
            string fl = P.Lower(P.Trim(Ask("\n    Flash it now? xppen-tools will ask you to type a confirmation first. (y/N): > ")));
            if (P.Eq(fl, "y") || P.Eq(fl, "yes"))
            {
                bool flashed;
                if (!BeforeFlash(d, info)) return 1;
                try { flashed = FlashSteps(outs.Get(0), backupLeftDfu, false); }
                catch (Stop e) { DangerCritical(); P.Line("    " + e.msg); return 1; }
                if (!flashed) { DangerCritical(); return 1; }
                if (flashed && Build.On(st, "blcounter") && Build.Unavailable("blcounter", levels[0], st) == null) UnplugWarning();
                else if (flashed && Build.RemovesCounter(levels[0], st, o)) RemovalWarning();
                if (flashed) { P.Line(""); P.Line("    Update complete. Try drawing, hovering, pressure and the pen buttons before real work."); P.Line(""); }
            }
            return 0;
        }

        // return to stock: a firmware that puts the bootloader area back (hook removed, counter erased) at its first
        // power-on, then the stock application; both are flashed by you
        public static bool StockBootloaderRestored(byte[] current, byte[] stockBoot)
        {
            if (current == null || current.Length != 0x10000 || stockBoot == null || stockBoot.Length < 0x3800) return false;
            // Boot code must match the stock source, and both extension/counter pages must be erased.
            // Device settings in 0x3000..0x37FF are deliberately preserved and can differ after an update.
            for (int i = 0; i < 0x2800; i++) if (current[i] != stockBoot[i]) return false;
            for (int i = 0x2800; i < 0x3000; i++) if ((current[i] & 0xFF) != 0xFF) return false;
            return true;
        }

        public static int ReturnToStock(string infile, string folder, bool rt)
        {
            if (infile == null) { StrList nn = new StrList(); infile = PickInput(".", nn); }
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(infile, info);
            if (!Build.inputHasBoot) throw new Stop("--return-to-stock needs a full 64 KB dump (an ic902-main.bin backup, or deco01v3-main.bin): " + infile + " has no bootloader");
            if (info.modded) throw new Stop(infile + " is a build of this tool, not stock firmware");
            P.MakeDirs(folder);
            Build.LOG.SetFolder(folder);
            bool[] st = Defaults();
            string[] off = new string[] { "boost", "reach", "longrange", "cal", "odd", "multicoil", "interp2", "blcounter", "volatile", "bootloader", "fhold", "chold", "systick", "tiltskip", "misc", "smatch", "wscale", "dac", "clin", "cgain" };
            for (int i = 0; i < off.Length; i++) st[Build.SetIndex(off[i])] = false;
            Options o = new Options(); o.cleanup = true;
            string rts = P.PathJoin(folder, "return-to-stock.hex"), stock = P.PathJoin(folder, "stock-app.hex");
            Build.BuildOne(app, info, rts, Build.Lv("500"), st, false, false, o);
            if (Gate(rts, 3, false, rt) != 1) { P.Line("    return-to-stock.hex FAILED its check: not continuing."); return 1; }
            Build.WriteHex(app, Build.BASE, stock);
            P.Line("");
            Screen.Para("Wrote " + rts + " and " + stock + " (the stock application from " + infile + ").", 4);
            P.Line("");
            if (!BeforeFlash(folder, info)) return 1;
            P.Line("    1. Flash the return-to-stock firmware (xppen-tools asks for its confirmation):");
            if (!FlashSteps(rts, backupLeftDfu, false)) return 1;
            PowerWait(true);
            P.Line("    2. After the uninterrupted-power wait above, unplug the tablet and plug it back in once, then wait 5 seconds. This firmware");
            P.Line("       puts bootloader page 0x08000C00 back to stock (if it carries the bootloader counter) and erases");
            P.Line("       the counter pages 0x08002800 and 0x08002C00.");
            P.Line("");
            Screen.Ask("    Press Enter once you have replugged the tablet and waited 5 seconds > ");
            P.Line("    3. Flash the stock application:");
            if (Gate(stock, 3, false, rt) != 1 || !BeforeFlash(folder, info, true)) return 1;
            if (!bootRestoredByCrc && !StockBootloaderRestored(tabletFlash, info.boot))
            {
                P.Line("    Bootloader restoration was not verified. The stock application will not be flashed.");
                P.Line("    Keep return-to-stock.hex and the latest full dump; its repair data may still be needed.");
                return 1;
            }
            if (!FlashSteps(stock, backupLeftDfu, false)) return 1;
            P.Line("");
            P.Line("    4. To check: " + Tool() + " dump check  and compare check/ic902-main.bin with your first backup.");
            return 0;
        }

        static StrList Without(StrList l, string v) { StrList n = new StrList(); for (int i = 0; i < l.Count(); i++) if (!P.Eq(l.Get(i), v)) n.Add(l.Get(i)); return n; }
        static StrList WithoutPrefix(StrList l, string v) { StrList n = new StrList(); for (int i = 0; i < l.Count(); i++) if (!P.StartsWith(l.Get(i), v)) n.Add(l.Get(i)); return n; }

        public const string FLASHER_TITLE = "Anargon's Tablet Firmware Mod";
        // the first screen (Anargon, 2026-11-17, naming tool 1)
        public const string TOOLS_WARNING = "Warning: Only the Tablet Firmware Mod (tool 1) has been thoroughly tested by a human "
            + "for logical consistency. You may find the other tools to not perform well.";
        // the tablets every tool here is allowed for (Anargon, 2026-11-17: "for now, that list is only the deco640");
        // add a tablet only when Anargon says so
        public static readonly string[] TOOLS_TABLETS = new string[] { "XPPen Deco 640" };
        public const string RELEASE_VERSION = "R45-rc6";
        // The tablet's USB IDs are fixed by its bootloader and firmware and cannot change: 28BD:2904 running, 28BD:FFF1 in
        // update mode (DFU). They are not options.
        public const string PID_RUN = "2904", PID_DFU = "FFF1";
        public const string XPPEN_IDS = "--runtime-pid " + PID_RUN + " --pid " + PID_DFU;
        public const string HELP = "Anargon's Tablet Tools " + RELEASE_VERSION + " - Anargon's Tablet Firmware Mod (firmware builder) (XPPen Deco 640, deco01v3)\n"
            + "usage: AnargonsTabletTools [INFILE] [OUTFILE|FOLDER] [options]       (no preset given: step-by-step wizard)\n"
            + "  (no arguments)         make the import, export, dumps and patcherpkgs folders next to the program, offer any\n"
            + "                         patcher packages in patcherpkgs (C# source only), then choose the tool\n"
            + "  --rate PRESET          220/stock, 300/quick, 500/fast, 800/rapid, 1000/ultra\n"
            + "  --all                  build every preset (auto-named files in the output folder)\n"
            + "  --smoothing on|off     movement smoothing (default off)      --interp on|off  position interpolation (default on)\n"
            + "  --aggressive-interp    extra moving average on top of interpolation: 16/8/4 positions by speed\n"
            + "  --no-boost  --no-reach  --no-longrange  --no-odd   switch Steady Hover, Reach Stage, Long-range Pickup or Jump Rejection off\n"
            + "  --no-frequency-hold  --no-coil-hold                switch Frequency Hold or Coil Hold off\n"
            + "  --no-timer-off         keep the bootloader's 1 ms timer running (stock; brings back ULTRA's noise bursts)\n"
            + "  --no-tilt-skip         measure tilt while hovering close too (stock; its frames shake 0.2-0.45 mm on Anargon's tablet)\n"
            + "  --no-misc-optimizations  Misc. Optimizations off: no Switch Match, Window Scale, Coil Gains, Coil Linearization or Dynamic Anti-Chatter\n"
            + "  (one of the five off, Misc. Optimizations still on:)\n"
            + "  --no-switch-match      no position match at coil-window and read-length switches (the cursor steps there)\n"
            + "  --no-window-scale      no pressure correction of the position inside a coil window (stock arithmetic)\n"
            + "  --no-anti-chatter      no Dynamic Anti-Chatter (it pulls a frame toward the last one only where the noise is over 0.10 mm, at most +0.4 ms)\n"
            + "  --no-coil-linearization  no Coil Linearization (a fixed correction of the position inside each coil, learned on Anargon's ruler lines; no delay)\n"
            + "  --no-coil-gains        no Coil Gains (each coil's reading strength evened out before the position; ULTRA, with Window Scale; no delay)\n"
            + "  --no-accurate-pressure short frequency readings: more pressure updates, can show pressure while hovering\n"
            + "  --startup-calibration   Pen frequency tuning, EXPERIMENTAL (default off): press hard within 3 s of plugging in; keeps the steadiest scan frequency\n"
            + "  --no-cal               keep the stock scan frequency (also overrides --startup-calibration)\n"
            + "  --bootloader-changes   allow writes to the bootloader area (flash counter); voids the warranty, default off\n"
            + "  --no-volatile          leave .data untouched (500 reports/s cap; not for RAPID/ULTRA)   --discreet  = both off\n"
            + "  --bootloader-counter   EXPERIMENTAL: patch the bootloader so it counts every update (needs a full dump as input)\n"
            + "  --no-bootloader-counter  remove an installed Bootloader Counter in this flash (input: a full dump of that tablet)\n"
            + "  --setup                what Linux and macOS need once so this program can read the tablet\n"
            + "  --hid-check            read HID reports for 5 seconds; no flashing or xppen-tools needed\n"
            + "  --pen-resonance        read tracked resonance for 5 seconds from installed measurement firmware\n"
            + "  --flash-count          show the tablet's flash count and where it was read (Bootloader, Firmware or local PC)\n"
            + "  --recovery [MAIN.BIN] [FOLDER] [--name NAME]   Anargon's Basic Tablet Recovery: repair a damaged tablet from its main.bin\n"
            + "  --filter-arranger      Measurement Tools > Anargon's Filter Arranger (testing only): change which position filters run, and their order,\n"
            + "                         on a tablet running a --lab build; kept in RAM, no flashing\n"
            + "  --lab                  build the Filter Arranger firmware (testing only): every position filter built in, the chosen\n"
            + "                         settings are the order it starts with; output name ends in -LAB\n"
            + "  --return-to-stock      make return-to-stock.hex + stock-app.hex and show the steps to put the tablet fully back to stock\n"
            + "  --multi-coil           EXPERIMENTAL: the pen's position from the five coils around it (500 reports/s and faster)\n"
            + "  --detect-scale X       multiply the pen pick-up/drop thresholds (default 1.0; lower = picks up higher, more noise risk)\n"
            + "  --calibrate            Tablet signal thresholds: flash measurement firmware, guided steps, then build + flash measured thresholds\n"
            + "  --calibration FILE     build with a saved calibration (calibration-<preset>.txt)   --calibration-pass  build measurement firmware; add --flash to install\n"
            + "  --apip FILE            ALgorithmic pen Position Interpolation with a saved training (apip-<preset>.txt; train it in the wizard)\n"
            + "  --apip-filter NAME     ALPI's filter with --apip: tracker (default), robust2, robust3 or robust4 (trackers)\n"
            + "  --samples N --burst N --tick N --poll N --boost-at N --cal-span N --odd-mm MM   expert tuning (files get -custom)\n"
            + "  --keep-binterval       leave bInterval at 3 (500 reports/s cap)\n"
            + "  --bin                  also write a .bin\n"
            + "  --twin-speed uncapped|72mhz   checks at full PC speed (default) or paced; measurements always run at 72 MHz\n"
            + "  --flash                back up and flash one checked build; xppen-tools asks for typed confirmation\n"
            + "  --flash-dry-run        build and check one preset, then print commands without running tablet commands\n"
            + "  --check IMAGE          digital-twin check (and measurements) of any image\n"
            + "  --compat IMAGE [--rate PRESET]   report which modifications are compatible with that firmware's code\n"
            + "  twin ...               the digital twin's own tests (AnargonsTabletTools twin --help)";

        public static int Run(string[] args)
        {
            measurementInstallRequested = false;
            sessionBackup = null; backupLeftDfu = false;           // a run starts without a backup of its own
            tabletApp = null; tabletFlash = null; tabletBuild = null; tabletModded = false;
            tabletState = null; dumpSkipped = false; bootRestoredByCrc = false;
            string infile = null, outfile = null, rate = null, smoothing = "off", interp = "on", speed = "uncapped";
            bool all = false, keepB = false, noAccp = false, noBoost = false, noReach = false, noLong = false, multiCoil = false, noCal = false, startupCal = false, noOdd = false, noFhold = false, noChold = false, noSystick = false, noTiltSkip = false, noSmatch = false, smatchOn = false, noWscale = false, noMisc = false, dacOn = true, clOn = true, cgOn = true, aggr = false, discreet = false, noVolatile = false, blChanges = false;
            bool bin = false, flash = false, dry = false, check = false, wizard = false, calibrate = false, calPass = false, compat = false;
            string calFile = null, apipFile = null, apipFilter = null;
            bool toStock = false, blc = false, noBlc = false, repair = false, arranger = false;
            string repName = null;
            Options o = new Options();
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (P.Eq(a, "-h") || P.Eq(a, "--help")) { P.Line(HELP); return 0; }
                    else if (P.Eq(a, "--version")) { P.Line("Anargon's Tablet Tools " + RELEASE_VERSION); return 0; }
                    else if (P.Eq(a, "--all")) all = true;
                    else if (P.Eq(a, "--keep-binterval")) keepB = true;
                    else if (P.Eq(a, "--no-accurate-pressure")) noAccp = true;
                    else if (P.Eq(a, "--no-boost")) noBoost = true;
                    else if (P.Eq(a, "--no-reach")) noReach = true;
                    else if (P.Eq(a, "--no-longrange")) noLong = true;
                    else if (P.Eq(a, "--multi-coil")) multiCoil = true;
                    else if (P.Eq(a, "--dual-coil") || P.Eq(a, "--dual-rx") || P.Eq(a, "--dual-search"))
                        throw new Stop(a + " was removed in R43: Multi-coil Read (--multi-coil) replaces Dual-coil Mode.");
                    else if (P.Eq(a, "--aggressive-interp")) aggr = true;
                    else if (P.Eq(a, "--discreet")) { discreet = true; noVolatile = true; }
                    else if (P.Eq(a, "--no-volatile")) noVolatile = true;
                    else if (P.Eq(a, "--bootloader-changes")) blChanges = true;
                    else if (P.Eq(a, "--return-to-stock")) toStock = true;
                    else if (P.Eq(a, "--repair") || P.Eq(a, "--recovery")) repair = true;
                    else if (P.Eq(a, "--filter-arranger")) arranger = true;
                    else if (P.Eq(a, "--lab")) { o.lab = true; labWizard = true; }
                    else if (P.Eq(a, "--setup")) { P.Line(SetupHelp()); return 0; }
                    else if (P.Eq(a, "--hid-check")) { Screen.Disclosure(); return CheckHid(false); }
                    else if (P.Eq(a, "--pen-resonance")) { Screen.Disclosure(); return CheckHid(true); }
                    else if (P.Eq(a, "--flash-count")) { Screen.Disclosure(); SetCounterWarn(".", -1); for (int w = 0; w < Screen.warnings.Count(); w++) if (P.StartsWith(Screen.warnings.Get(w), "Recorded updates")) P.Line(Screen.warnings.Get(w)); return 0; }
                    else if (P.Eq(a, "--bootloader-counter")) blc = true;
                    else if (P.Eq(a, "--no-bootloader-counter")) noBlc = true;
                    else if (P.Eq(a, "--no-cal")) noCal = true;
                    else if (P.Eq(a, "--startup-calibration")) startupCal = true;
                    else if (P.Eq(a, "--no-odd")) noOdd = true;
                    else if (P.Eq(a, "--no-frequency-hold")) noFhold = true;
                    else if (P.Eq(a, "--no-coil-hold")) noChold = true;
                    else if (P.Eq(a, "--no-timer-off")) noSystick = true;
                    else if (P.Eq(a, "--no-tilt-skip")) noTiltSkip = true;
                    else if (P.Eq(a, "--no-misc-optimizations")) noMisc = true;
                    else if (P.Eq(a, "--no-switch-match")) noSmatch = true;
                    else if (P.Eq(a, "--switch-match")) smatchOn = true;
                    else if (P.Eq(a, "--no-window-scale")) noWscale = true;
                    else if (P.Eq(a, "--anti-chatter")) dacOn = true;
                    else if (P.Eq(a, "--no-anti-chatter")) dacOn = false;
                    else if (P.Eq(a, "--coil-linearization")) clOn = true;
                    else if (P.Eq(a, "--no-coil-linearization")) clOn = false;
                    else if (P.Eq(a, "--coil-gains")) cgOn = true;
                    else if (P.Eq(a, "--no-coil-gains")) cgOn = false;
                    else if (P.Eq(a, "--bin")) bin = true;
                    else if (P.Eq(a, "--flash")) flash = true;
                    else if (P.Eq(a, "--flash-dry-run")) dry = true;
                    else if (P.Eq(a, "--check")) check = true;
                    else if (P.Eq(a, "--compat")) compat = true;
                    else if (P.Eq(a, "--wizard")) wizard = true;
                    else if (P.Eq(a, "--calibrate")) calibrate = true;
                    else if (P.Eq(a, "--calibration-pass")) calPass = true;
                    else if (P.StartsWith(a, "--"))
                    {
                        if (i + 1 >= args.Length) throw new Stop(a + " needs a value");
                        string v = args[++i];
                        if (P.Eq(a, "--rate")) rate = v;
                        else if (P.Eq(a, "--smoothing")) smoothing = v;
                        else if (P.Eq(a, "--interp")) interp = v;
                        else if (P.Eq(a, "--twin-speed")) speed = v;
                        else if (P.Eq(a, "--pid") || P.Eq(a, "--runtime-pid")) throw new Stop(a + ": the tablet's USB IDs are fixed (28BD:" + PID_RUN + " running, 28BD:" + PID_DFU + " in update mode) and cannot be changed");
                        else if (P.Eq(a, "--retries")) throw new Stop("--retries is not supported. A failed firmware update must be inspected before retrying.");
                        else if (P.Eq(a, "--samples")) o.samples = IntegerOption(v, a);
                        else if (P.Eq(a, "--burst")) o.burst = IntegerOption(v, a);
                        else if (P.Eq(a, "--tick")) o.tick = IntegerOption(v, a);
                        else if (P.Eq(a, "--poll")) o.poll = IntegerOption(v, a);
                        else if (P.Eq(a, "--boost-at")) o.boostAt = IntegerOption(v, a);
                        else if (P.Eq(a, "--cal-span")) o.calSpan = IntegerOption(v, a);
                        else if (P.Eq(a, "--rx2-gain")) throw new Stop("--rx2-gain was removed in R43 with Dual-coil Mode.");
                        else if (P.Eq(a, "--odd-mm")) o.oddMm = NumberOption(v, a);
                        else if (P.Eq(a, "--detect-scale")) o.detectScale = NumberOption(v, a);
                        else if (P.Eq(a, "--calibration")) calFile = v;
                        else if (P.Eq(a, "--apip")) apipFile = v;
                        else if (P.Eq(a, "--apip-filter")) apipFilter = v;
                        else if (P.Eq(a, "--name")) repName = v;
                        else throw new Stop("unknown option " + a);
                    }
                    else if (infile == null) infile = a;
                    else if (outfile == null) outfile = a;
                    else throw new Stop("too many file names");
                }
                if (!P.Eq(speed, "uncapped") && !P.Eq(speed, "72mhz")) throw new Stop("--twin-speed must be uncapped or 72mhz");
                if (!P.Eq(smoothing, "on") && !P.Eq(smoothing, "off")) throw new Stop("--smoothing must be on or off");
                if (!P.Eq(interp, "on") && !P.Eq(interp, "off")) throw new Stop("--interp must be on or off");
                if (dry && (toStock || repair || calibrate || wizard || (rate == null && !all)))
                    throw new Stop("--flash-dry-run needs a preset (--rate) and cannot be combined with calibration, recovery or the wizard.");
                bool rt = P.Eq(speed, "72mhz");
                // AI DISCLOSURE (never remove): the menu, wizard and recovery tool show it on every screen; every
                // other run shows it once, first (only --help, --version, --setup and twin commands do not)
                bool screens = !check && !compat && !toStock && (repair || wizard || (rate == null && !all));
                if (!screens) Screen.Disclosure();
                if (check)
                {
                    if (infile == null) throw new Stop("--check needs an image");
                    Build.LOG.SetFolder(".");
                    return Gate(infile, 0, false, rt) == 1 ? 0 : 1;
                }
                if (compat)
                {
                    if (infile == null) throw new Stop("--compat needs a firmware image");
                    InputInfo ci = new InputInfo(); byte[] capp = Build.ReadInput(infile, ci);
                    Level clv = null;
                    if (rate != null) { for (int i = 0; i < Build.LEVELS.Length; i++) if (P.Eq(Build.LEVELS[i].key, rate) || P.Eq(Build.LEVELS[i].name, rate)) clv = Build.LEVELS[i]; }
                    if (clv == null || clv.period == 0) clv = Build.Lv("500");
                    CompatResult cr = Compat.Check(capp, ci, clv, Defaults(), new Options());
                    P.Line("Compatibility of " + infile + " (checked for preset " + P.Upper(clv.name) + "):");
                    P.Line("  modifications checked: " + F.Str(cr.total));
                    P.Line("  not compatible: " + (cr.any ? Compat.List(cr) : "(none)"));
                    P.Line("  verdict: " + (!cr.any ? "COMPATIBLE" : cr.all ? "ALL INCOMPATIBLE" : "SOME INCOMPATIBLE"));
                    return cr.any ? 1 : 0;
                }
                if (toStock) return ReturnToStock(infile, outfile != null ? outfile : ".", rt);
                if (arranger) return FilterArranger.Run();
                if (repair) return Repair.Run(infile, repName, outfile, rt);
                if (!wizard && rate == null && !all && infile == null && outfile == null)
                {
                    // R44 (Anargon): 0. the folders next to the program, before anything else; 0b. the patcher
                    // packages in patcherpkgs (only shown when that folder has something in it)
                    Folders.Make();
                    int pk = Packages.Select(Folders.Of(Folders.PACKAGES));
                    if (pk != -1) return pk;
                    // no arguments: the first screen chooses the tool. The startup backup starts only with tool 1, which
                    // flashes (Anargon, 2026-11-16): the recovery tool is for a tablet that cannot enter update mode (it
                    // is used after the tablet was recovered), and the Filter Arranger keeps its order in RAM, which an
                    // update-mode restart would clear
                    string m;
                    while (true)
                    {
                        Screen.Step("Choose the tool:");
                        P.Line("    1.  " + FLASHER_TITLE);
                        P.Line("        Pick settings, build the firmware, test it on a digital copy of the tablet, then flash it.");
                        P.Line("");
                        P.Line("    2.  " + Repair.TITLE);
                        P.Line("        Check a main.bin dump for damage and build repair images.");
                        P.Line("");
                        // 3 (Anargon, 2026-11-17): the testing tools, the Filter Arranger first ("a test suite type class,
                        // so it should not be part of the primary tablet patcher"; atf_measure.cs)
                        P.Line("    3.  " + MeasurementTools.TITLE);
                        P.Line("        " + MeasurementTools.WHAT);
                        P.Line("");
                        P.Line("    Warning: These tools are only allowed for the tablets on this list:");
                        for (int t = 0; t < TOOLS_TABLETS.Length; t++) P.Line("      - " + TOOLS_TABLETS[t]);
                        P.Line("");
                        Screen.Para(TOOLS_WARNING, 4);
                        P.Line("");
                        m = P.Trim(Ask("    Tool (1-3): > "));
                        if (P.Eq(m, "1") || P.Eq(m, "2") || P.Eq(m, "3")) break;
                        if (P.Len(m) == 0 && !P.StdinIsTty()) { P.Line(""); P.Line("    No choice made (no input). Run it again and type 1, 2 or 3."); return 2; }
                    }
                    if (P.Eq(m, "2")) return Repair.Run(null, null, null, rt);
                    if (P.Eq(m, "3")) { MeasurementTools.realtime = rt; return MeasurementTools.Run(); }
                }
                if (wizard || (rate == null && !all))
                {
                    Folders.Make();
                    if (P.StdinIsTty()) StartupBackup.Begin(P.ExeDir());
                    return Wizard(infile, outfile, rt);
                }
                if (infile == null) { StrList nn = new StrList(); infile = PickInput(".", nn); }
                Level lvSel = null;
                if (rate != null) { for (int i = 0; i < Build.LEVELS.Length; i++) if (P.Eq(Build.LEVELS[i].key, rate) || P.Eq(Build.LEVELS[i].name, rate)) lvSel = Build.LEVELS[i]; if (lvSel == null) throw new Stop("unknown preset " + rate); }
                if (all && outfile != null && P.EndsWith(P.Lower(outfile), ".hex")) throw new Stop("--all writes one file per preset: give a folder, not a file name");
                string outdir, ofile;
                if (all || outfile == null || P.DirExists(outfile) || P.EndsWith(outfile, "/") || P.EndsWith(outfile, "\\")) { outdir = outfile != null ? outfile : "."; ofile = null; }
                else { outdir = P.DirName(outfile); if (P.Len(outdir) == 0) outdir = "."; ofile = outfile; }
                P.MakeDirs(outdir);
                Build.LOG.SetFolder(outdir);
                bool[] st = Defaults();
                st[Build.SetIndex("smoothing")] = P.Eq(smoothing, "on"); st[Build.SetIndex("interp")] = P.Eq(interp, "on"); st[Build.SetIndex("interp2")] = aggr;
                st[Build.SetIndex("accp")] = !noAccp; st[Build.SetIndex("boost")] = !noBoost; st[Build.SetIndex("reach")] = !noReach; st[Build.SetIndex("longrange")] = !noLong;
                st[Build.SetIndex("cal")] = startupCal && !noCal; st[Build.SetIndex("odd")] = !noOdd; st[Build.SetIndex("fhold")] = !noFhold; st[Build.SetIndex("chold")] = !noChold; st[Build.SetIndex("systick")] = !noSystick; st[Build.SetIndex("tiltskip")] = !noTiltSkip; st[Build.SetIndex("misc")] = !noMisc; st[Build.SetIndex("smatch")] = !noMisc && (smatchOn || !noSmatch); st[Build.SetIndex("wscale")] = !noMisc && !noWscale; st[Build.SetIndex("dac")] = !noMisc && dacOn; st[Build.SetIndex("clin")] = !noMisc && clOn; st[Build.SetIndex("cgain")] = !noMisc && cgOn; st[Build.SetIndex("multicoil")] = multiCoil; st[Build.SetIndex("volatile")] = !noVolatile; st[Build.SetIndex("bootloader")] = (blChanges || blc) && !discreet; st[Build.SetIndex("blcounter")] = blc;
                // R45: --no-bootloader-counter removes an installed Bootloader Counter (needs bootloader writes)
                if (noBlc && blc) throw new Stop("--bootloader-counter and --no-bootloader-counter cannot be combined.");
                if (noBlc && discreet) throw new Stop("--no-bootloader-counter writes to the bootloader area, so it cannot be combined with --discreet.");
                if (noBlc) { st[Build.SetIndex("bootloader")] = true; o.blRemove = true; }
                InputInfo info = new InputInfo();
                byte[] app = Build.ReadInput(infile, info);
                if (Build.CheckInput(info) && P.StdinIsTty()) { P.Line("Continuing in 5 seconds - press Ctrl-C to stop."); P.Sleep(5.0); }
                if (noBlc && !Build.counterFound) P.Line("Note: " + infile + " has no Bootloader Counter, so there is nothing to remove.");
                if (Build.counterFound && !blc && !noBlc) P.Line("Note: " + infile + " has the Bootloader Counter installed. This build leaves it as it is; --no-bootloader-counter removes it.");
                Level[] levels = all ? Build.LEVELS : new Level[] { lvSel };
                if (noVolatile)
                {
                    for (int i = 0; i < levels.Length; i++)
                        if (Build.Required("volatile", levels[i]) != null)
                            throw new Stop("--no-volatile/--discreet cannot be combined with " + P.Upper(levels[i].name) + ". Use FAST or a slower preset to keep firmware data unchanged.");
                }
                if ((calibrate || calPass || calFile != null) && (all || lvSel.period == 0)) throw new Stop("calibration works on one preset other than STOCK (use --rate)");
                if (calibrate || calPass || calFile != null)
                {
                    string profileError = Build.CalibrationProfileError(st, o);
                    if (profileError != null) throw new Stop(profileError);
                }
                if (apipFilter != null && apipFile == null) throw new Stop("--apip-filter needs --apip FILE (a saved training)");
                if (apipFile != null)
                {
                    if (all || lvSel.period == 0) throw new Stop("--apip works on one preset other than STOCK (use --rate)");
                    st[Build.SetIndex("apip")] = true;
                    if (apipFilter != null)
                    {
                        int k = P.Eq(apipFilter, "tracker") ? 1 : P.Eq(apipFilter, "robust2") ? 2 : P.Eq(apipFilter, "robust3") ? 3 : P.Eq(apipFilter, "robust4") ? 4 : 0;
                        if (k == 0) throw new Stop("--apip-filter is tracker, robust2, robust3 or robust4");
                        Build.ApipOff(st); st[Build.SetIndex(Build.ApipKey(k))] = true;
                    }
                    string un = Build.Unavailable(Build.ApipKey(Build.ApipKind(st)), lvSel, st);
                    if (un != null) throw new Stop("ALPI cannot be used on " + P.Upper(lvSel.name) + ": " + un);
                    o.apip = Apip.Load(apipFile);
                    string mm = Apip.Mismatch(o.apip, lvSel, Build.On(st, "fullres") && Build.Unavailable("fullres", lvSel, st) == null);
                    if (mm != null) throw new Stop(apipFile + " does not fit this build (" + mm + "): train ALPI for it in the wizard");
                    if (Build.On(st, "cal")) throw new Stop("--startup-calibration cannot be combined with --apip: ALPI is trained at the stock scan frequency, and Pen frequency tuning moves it");
                }
                if (calFile != null)
                {
                    string cp = LoadCal(calFile, o);
                    if (!P.Eq(cp, lvSel.key)) throw new Stop(calFile + " was made for preset " + cp + ", not " + lvSel.key + ": calibrate this preset");
                }
                // R45-rc2: a training carries the pick-up levels it measured in the background (a calibration file wins)
                if (o.apip != null && o.calPick < 0 && o.apip.pick > 0) { o.calPick = o.apip.pick; o.calDrop = o.apip.drop; }
                if (calPass)
                {
                    if (calibrate || calFile != null) throw new Stop("--calibration-pass cannot be combined with --calibrate or --calibration.");
                    string measurement = BuildPass1(app, info, lvSel, st, outdir, rt);
                    if (measurement == null) return 1;
                    if (flash || dry)
                    {
                        if (!MayFlash(info, 1, 1)) return 1;
                        if (dry) PreviewFlash(measurement);
                        else
                        {
                            if (!BeforeFlash(outdir, info) || !FlashSteps(measurement, backupLeftDfu, true)) return 1;
                            measurementInstallRequested = true;
                            P.Line("Measurement firmware installed. Run --pen-resonance to read the tracked frequency, then flash a checked normal preset to restore cursor reports.");
                        }
                    }
                    return 0;
                }
                if (calibrate)
                {
                    bool calibrated = Calibrate(app, info, lvSel, st, outdir, o, rt);
                    if (!calibrated && !calibrationFirmwareActive) return 1;
                    if (!calibrated) P.Line("    Building and restoring the normal preset with its defaults.");
                    flash = true; // --calibrate includes the final normal build, as its help promises
                }
                StrList outs = new StrList();
                int[] pe = new int[levels.Length];
                int ok = BuildAll(app, info, levels, st, outdir, ofile, !keepB, bin, o, rt, outs, pe);
                if (flash || dry)
                {
                    if (!MayFlash(info, outs.Count(), pe[0])) return 1;
                    if (dry) PreviewFlash(outs.Get(0));
                    else
                    {
                        if (!BeforeFlash(outdir, info)) return 1;
                        if (!FlashSteps(outs.Get(0), backupLeftDfu, false)) return 1;
                        if (blc && Build.Unavailable("blcounter", levels[0], st) == null) UnplugWarning();
                        else if (Build.RemovesCounter(levels[0], st, o)) RemovalWarning();
                    }
                }
                return ok == levels.Length ? 0 : 1;
            }
            catch (Stop e) { P.Line(e.msg); return P.InputEnded() ? 2 : 1; }
            catch (Exception e) { P.Line("Could not complete the command: " + P.Msg(e)); return 1; }
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && P.Eq(args[0], "twin"))
            {
                string[] rest = new string[args.Length - 1];
                P.Copy(args, 1, rest, 0, rest.Length);
                return Twin.Run(rest);
            }
            int result;
            try { result = Cli.Run(args); }
            finally { StartupBackup.Wait(); }
            if (Cli.calibrationFirmwareActive)
            {
                P.Line("The tablet may still have calibration firmware installed; normal cursor reports can be disabled until it is replaced.");
                P.Line("Build a normal preset from your original stock dump and flash its checked .hex, or restore stock before using the tablet.");
                if (result == 0 && !Cli.measurementInstallRequested) result = 1;
            }
            return result;
        }
    }
}

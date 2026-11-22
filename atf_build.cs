// atf_build.cs - presets, settings, building, logs, the digital-twin gate, wizard, flashing and command line.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class Level
    {
        public string key, label, name;
        public int period, samples, burst, tilt, chunk, split, sweepLen = 6, swSamples, swBurst;
        public bool sweepOnTilt;
        public Level(string k, string nm, string l, int p, int s, int b, int t, int c, bool sot, int sp, int sws, int swb)
        { key = k; name = nm; label = l; period = p; samples = s; burst = b; tilt = t; chunk = c; sweepOnTilt = sot; split = sp; swSamples = sws; swBurst = swb; }
    }

    public class Setting
    {
        public string key, name, cat, tag, what, brief;
        public bool dflt;
        public Setting(string k, string n, string c, bool d, string t, string w) { key = k; name = n; cat = c; dflt = d; tag = t; what = w; brief = ""; }
        public Setting Brief(string b) { brief = b; return this; }
        public Setting Copy() { Setting c = new Setting(key, name, cat, dflt, tag, what); c.brief = brief; return c; }
    }

    // R45-rc3: a modification a patcher package adds to the builds (Build.AddPatch, DOCS/tutorial-patcherpkg.md). Apply
    // runs in the build's own transaction (Image.Begin / Fail) after this tool's own changes and before the undo record,
    // so the undo record, the build log and the twin gate cover it like a built-in change.
    public interface IPatch
    {
        string Name();                 // shown in the build log and the build options text
        void Apply(Image im);          // check with im.Expect, write with im.Wr / im.Branch, add code with Asm in the cave
    }

    public class Options
    {
        public int samples = -1, burst = -1, tick = -1, poll = -1, boostAt = -1, calSpan = -1;
        public double oddMm = -1, detectScale = -1;
        public int calPick = -1, calDrop = -1;           // per-tablet fast-stage thresholds from Tablet signal thresholds

        public bool telemetry = false;                   // calibration pass: reports carry the firmware's readings
        public bool lineRec = false;                     // R42: line recorder, with telemetry (multi-coil study)
        public bool cleanup = false;                     // return-to-stock firmware: bootloader area back to stock
        public bool blRemove = false;                    // R45: a normal build may remove an installed Bootloader Counter (Build.RemovesCounter)
        public string note = null;                       // measured threshold profile shown on the Settings screen
        public bool apipTrain = false;                   // R44: APIP training build (records samples for USB string 180)
        public ApipModel apip = null;                    // R44: the trained APIP model (null: builds without an output use a neutral one)
        public bool fallback = false;                    // R45: no measured thresholds: Anargon's own (Build.FallbackIndex), where measured
        public bool lab = false;                         // Filter Arranger build (atf_lab.cs): every position filter, order from RAM; testing only
        public bool Custom() { return samples >= 0 || burst >= 0 || tick >= 0 || poll >= 0 || calSpan >= 0 || detectScale >= 0; }
    }

    public class InputInfo
    {
        public string path, build;
        public int size, end, flashes = 0;
        public byte[] boot = null;                      // bootloader area (0x08000000-0x080037FF) when the input is a full dump
        public bool truncated, stock, modded;
        public int fwEnd;                               // where this firmware's image ends (from its scatter-load table)
        public Layout layout;                           // where this firmware keeps everything the tool uses
        public long crc;
        public string undone;                           // R42: another mod's changes undone to get the stock firmware, else null
        public StrList undoneHow;                       // R45: the full notice (Repair.UndoneDetail): whose mod, what and how
        public bool reverted;                           // R45: the input was a build of this tool, set back to stock (atf_revert.cs)
    }

    // ---------------------------------------------------------------------------------------- logs
    public class RunLog
    {
        public string mainName, verboseName, folder = null;
        public StrList pendName = new StrList(), pendText = new StrList();
        public RunLog() { string t = P.Now("stamp"); mainName = "main-" + t + ".log"; verboseName = "verbose-" + t + ".log"; }
        public void SetFolder(string f)
        {
            folder = P.Len(f) > 0 ? f : ".";
            string t = P.Sub(mainName, 5, P.Len(mainName) - 9);
            int n = 2;
            while (P.FileExists(P.PathJoin(folder, mainName))) { mainName = "main-" + t + "-" + F.Str(n) + ".log"; verboseName = "verbose-" + t + "-" + F.Str(n) + ".log"; n++; }
            for (int i = 0; i < pendName.Count(); i++) W(pendName.Get(i), pendText.Get(i));
            pendName = new StrList(); pendText = new StrList();
        }
        public string Shown(bool main)
        {
            string n = main ? mainName : verboseName;
            if (folder == null || P.Eq(folder, "") || P.Eq(folder, ".")) return n;
            return P.PathJoin(folder, n);
        }
        public void W(string name, string text)
        {
            if (folder == null) { pendName.Add(name); pendText.Add(text); return; }
            P.AppendText(P.PathJoin(folder, name), Build.Ascii(text) + "\n");
        }
        public void Main(string s) { if (Build.quiet) return; W(mainName, s); W(verboseName, s); }
        public void Verbose(string s) { if (Build.quiet) return; W(verboseName, s); }
    }

    // A small per-user state file kept OUTSIDE the output folder, so it survives moving the tool and is out of the
    // way of the user: the best-known flash count (the tablet's own counter is authoritative, but this remembers it
    // and keeps counting the flashes this tool performs even with no dump at hand) and which one-time-only warnings
    // have already been shown. Windows: %APPDATA%\AnargonsTabletTools\state.ini ; otherwise ~/.local/state/AnargonsTabletTools or the exe dir.
    // R45-rc3: the product was renamed from AnargonsDeco640Tools; while only the old folder exists, it is used as it is.
    // ATF_STATE_DIR overrides the directory (used by the tests so they do not touch the real one).
    public static class Store
    {
        static StrList keys = new StrList(), vals = new StrList();
        static bool loaded = false;
        static string dirCache = null;

        static string Dir()
        {
            if (dirCache != null) return dirCache;
            string b = Conf.StateDir();
            if (b == null || P.Len(b) == 0) b = P.Env("APPDATA");
            if (b == null || P.Len(b) == 0) { string h = P.Env("HOME"); if (h != null && P.Len(h) > 0) b = P.PathJoin(h, ".local/state"); }
            if (b == null || P.Len(b) == 0) b = P.ExeDir();
            dirCache = P.Len(Conf.StateDir()) > 0 ? b : P.PathJoin(b, "AnargonsTabletTools");
            if (P.Len(Conf.StateDir()) == 0 && !P.FileExists(P.PathJoin(dirCache, "state.ini")) && P.FileExists(P.PathJoin(P.PathJoin(b, "AnargonsDeco640Tools"), "state.ini")))
                dirCache = P.PathJoin(b, "AnargonsDeco640Tools");
            return dirCache;
        }
        static string Path() { return P.PathJoin(Dir(), "state.ini"); }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            string p = Path();
            if (!P.FileExists(p)) p = P.PathJoin(P.PathJoin(P.DirName(Dir()), "AnargonsTabletFirmware"), "state.ini");      // saved by an unreleased rev 35 build
            if (!P.FileExists(p)) p = P.PathJoin(P.PathJoin(P.DirName(Dir()), "AnargonTabletFirmware"), "state.ini");       // saved by rev 34
            if (!P.FileExists(p)) p = P.PathJoin(P.PathJoin(P.DirName(Dir()), "deco640"), "state.ini");       // saved by rev 33 or older (the old project name)
            if (!P.FileExists(p)) return;
            string[] lines;
            try { lines = P.Lines(P.Ascii(P.ReadAllBytes(p))); } catch (Exception) { return; }
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = P.Trim(lines[i]);
                int eq = P.IndexOf(ln, "=");
                if (P.Len(ln) == 0 || P.StartsWith(ln, "#") || eq < 0) continue;
                keys.Add(P.Sub(ln, 0, eq)); vals.Add(P.Sub(ln, eq + 1, P.Len(ln) - eq - 1));
            }
        }
        static void Save()
        {
            P.MakeDirs(Dir());
            StringBuilder sb = new StringBuilder("# Anargon's Tablet Tools saved state - please do not edit\r\n");
            for (int i = 0; i < keys.Count(); i++) sb.Append(keys.Get(i) + "=" + vals.Get(i) + "\r\n");
            try { P.WriteAllBytes(Path(), Build.Encode(sb.ToString())); } catch (Exception) { }
        }
        static string Get(string k) { Load(); for (int i = 0; i < keys.Count(); i++) if (P.Eq(keys.Get(i), k)) return vals.Get(i); return null; }
        static void Set(string k, string v) { Load(); for (int i = 0; i < keys.Count(); i++) if (P.Eq(keys.Get(i), k)) { vals.a[i] = v; Save(); return; } keys.Add(k); vals.Add(v); Save(); }

        public static int FlashCount() { string v = Get("flash_count"); return v == null ? -1 : (int)F.ParseInt(v); }
        public static void NoteFlashCount(int n) { if (n >= 0 && n > FlashCount()) Set("flash_count", F.Str(n)); }   // the tablet is authoritative; keep the max
        public static void BumpFlashCount() { int c = FlashCount(); Set("flash_count", F.Str(c < 0 ? 1 : c + 1)); }  // this tool just performed a flash

        public static bool WarnedOnce(string key) { return Get("warn_" + key) != null; }
        public static void MarkWarned(string key) { Set("warn_" + key, "1"); }

        // R44: patcher packages the user approved by typing the phrase, by the SHA-256 of their exact content
        public static bool PackageTrusted(string sha) { return Get("package_" + sha) != null; }
        public static void TrustPackage(string sha) { Set("package_" + sha, "1"); }
    }

    // result of a per-modification compatibility probe: which modifications this build would apply that the loaded
    // firmware does not have the expected stock code for, out of how many were checked.
    public class CompatResult
    {
        public StrList bad = new StrList();
        public int total = 0;
        public bool any = false, all = false;
    }

    public static class Compat
    {
        // run the whole build in probe mode (no writes, no throws): collect every modification whose code sites do not
        // match the stock 2025-02-14 firmware. This is what guards against patching a different firmware at the wrong
        // offsets (which is how the compressed .data block gets corrupted and a "known-working" firmware bricks).
        public static CompatResult Check(byte[] app, InputInfo info, Level lv, bool[] st, Options o)
        {
            CompatResult r = new CompatResult();
            bool pq = Build.quiet;
            Build.probing = true; Build.quiet = true;
            Image im = null;
            try { im = Build.BuildOne(app, info, null, lv, st, true, false, o); }
            catch (Stop) { im = null; }                 // should not happen (probe guards the throws); treat as fully incompatible
            Build.probing = false; Build.quiet = pq;
            if (im != null) { for (int i = 0; i < im.probeBad.Count(); i++) r.bad.Add(im.probeBad.Get(i)); r.total = im.probeAll.Count(); }
            else { r.bad.Add("every modification"); r.total = 1; }
            r.any = r.bad.Count() > 0;
            r.all = r.any && r.total > 0 && r.bad.Count() >= r.total;
            return r;
        }

        public static string List(CompatResult r)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < r.bad.Count(); i++) { if (i > 0) sb.Append(", "); sb.Append(r.bad.Get(i)); }
            return sb.ToString();
        }
    }

    public static class Build
    {
        public const int BASE = 0x08003800, FLASH_END = 0x08010000;
        public const string VERSION = "AnargonSS727";
        public const int BUILD_STR_AT = 0x0800BC9A;      // fallback only (2025-02-14 position); FindBuildString searches first
        // the firmware builds this tool has been verified on are listed in Layout.KNOWN (atf_layout.cs)
        public const int BOOST_ENTER = 1600, PROC_US = 130, MIN_BURST = 16, MIN_SAMPLES = 4;
        // R43: 12-bit Readings. Stock clears the low 3 bits of every reading (0..7, 3.5 on average), so a 12-bit reading
        // is on average 3.5 higher than the rounded one. Every signal level this tool sets (pick-up, drop, boost) is in
        // rounded readings - the stock tuning and every saved Tablet signal thresholds file are - so a 12-bit build adds
        // FULLRES_BIAS to each: the hover range stays the same (0.5 stricter, rounding 3.5 up).
        public const int FULLRES_BIAS = 4;
        // Per-frame processing cost of the OPTIONAL filters (twin-measured, rounded up). PROC_US already includes the
        // default-on work (Position Interpolation, Jump Rejection, the hover state machine, calibration), so those are
        // not charged again here. Movement Smoothing is the stock dead-band: its code runs whether on or off, so turning
        // it on only costs the extra non-zero compare (about 1 us) - NOT the 35 us it used to be billed, which was the
        // only reason it could not fit the ULTRA frame. Aggressive Position Interpolation adds a 16-tap average with two
        // hardware divides per report, about 1 us.
        public const int SMOOTH_US = 1, INTERP2_US = 1;
        public static int FilterProcUs(bool smoothing, bool interp2) { return (smoothing ? SMOOTH_US : 0) + (interp2 ? INTERP2_US : 0); }
        // R45: apip = APIP's filter (Build.ApipKind), charged per frame of PERIODUS (Apip.ProcUs)
        public static int FilterProcUs(bool smoothing, bool interp2, int apip, int periodUs) { return FilterProcUs(smoothing, interp2) + Apip.ProcUs(apip, periodUs); }
        public static readonly int[] REACH_MODE = new int[] { 16667, 24, 254 };
        // R45-rc3 (Anargon: push the 60 Hz stage's hover limit): the reach stage keeps a pen it already tracks down to
        // this part of its drop level, in its scan and its sweep; its pick-up levels stay. On Anargon's tablet the reach
        // level fell smoothly to the drop level on every slow lift (frame-to-frame about 1 % of it, every line still
        // shaped by the pen), and the no-pen level is about a quarter of the old drop level. Twin (ULTRA, raising at
        // 20 mm/s): held to 25.8 mm, 22.0 before (x0.8 23.6, x0.7 24.9, x0.5 27.1); no pick-up with no pen. More
        // samples per line did not help: 48 held to 20.4 mm, the 16 ms frame gets too full.
        public const double REACH_HOLD = 0.6;
        public static RunLog LOG = new RunLog();
        // R45-rc3: patcher packages' modifications (IPatch), applied to every normal build made in this run; left out of
        // the measurement, ALPI training and return-to-stock builds, which are this tool's instruments
        public static IPatch[] EXTRA = new IPatch[0];
        public static void AddPatch(IPatch p)
        {
            if (p == null || p.Name() == null || P.Len(p.Name()) == 0) throw new Stop("a package's patch needs a name");
            IPatch[] e = new IPatch[EXTRA.Length + 1];
            for (int i = 0; i < EXTRA.Length; i++) e[i] = EXTRA[i];
            e[EXTRA.Length] = p;
            EXTRA = e;
        }
        static bool ExtraApplies(Options o) { return !o.telemetry && !o.apipTrain && !o.cleanup; }

        // R45-rc3 (Anargon: a patcher package handles its tablet's patching steps, and adds, removes or changes options
        // for its tablet, implemented by the package's developer): package options and hidden options.
        //   - AddSetting: a package option, shown on the settings screens like the tool's own (category PKG_CAT,
        //     "Package options", on the Extra QOL screen, unless it names another category); its patch runs only in
        //     builds where it is on, after the AddPatch patches.
        //   - HideSetting: a built-in option this tablet does not have; not shown, and every build treats it as off.
        //   - Option: a built-in option to change (name, text, default, tag).
        // The first of these copies the settings list, and ClearPatches puts the built-in list back, so the built-in
        // tool is unchanged once the package returns.
        public const string PKG_CAT = "pkg";
        static Setting[] builtinSettings = null;            // SETTINGS before the package's first change; null = none
        static string[] optionKeys = new string[0];         // the package options, and the patch each one runs
        static IPatch[] optionPatches = new IPatch[0];
        static StrList hiddenKeys = new StrList(), hiddenWhy = new StrList();
        static bool[] buildingOn = null;                    // the build in progress (IsOn, BuildingPreset)
        static Level buildingLv = null;

        static void OwnSettings()
        {
            if (builtinSettings != null) return;
            Setting[] c = new Setting[SETTINGS.Length];
            for (int i = 0; i < c.Length; i++) c[i] = SETTINGS[i].Copy();
            builtinSettings = SETTINGS;
            SETTINGS = c;
        }
        static bool Plain(string s, int max, bool word)
        {
            if (P.Len(s) == 0 || P.Len(s) > max) return false;
            for (int i = 0; i < P.Len(s); i++)
            {
                int c = P.CharAt(s, i);
                if (word ? !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9') : c < 32 || c > 126) return false;
            }
            return true;
        }
        public static void AddSetting(Setting s, IPatch p)
        {
            if (s == null || p == null || p.Name() == null || P.Len(p.Name()) == 0) throw new Stop("a package option needs a Setting and a patch with a name");
            if (!Plain(s.key, 16, true)) throw new Stop("a package option's key must be 1-16 letters or digits: " + s.key);
            if (SetIndex(s.key) >= 0) throw new Stop("the option " + s.key + " already exists");
            if (!Plain(s.name, 33, false)) throw new Stop("a package option's name must be 1-33 printable ASCII characters: " + s.key);
            if (!Plain(s.tag, 8, true)) throw new Stop("a package option's file-name tag must be 1-8 letters or digits: " + s.key);
            if (!Plain(s.what, 4000, false)) throw new Stop("a package option needs its text (\"what it does|what it costs\", printable ASCII): " + s.key);
            if (P.Len(s.cat) == 0) s.cat = PKG_CAT;
            bool known = false;
            for (int i = 0; i < CATEGORIES.Length; i += 2) if (P.Eq(CATEGORIES[i], s.cat)) known = true;
            if (!known) throw new Stop("unknown category " + s.cat + " for the option " + s.key + " (use Build.PKG_CAT)");
            OwnSettings();
            Setting[] n = new Setting[SETTINGS.Length + 1];
            for (int i = 0; i < SETTINGS.Length; i++) n[i] = SETTINGS[i];
            n[SETTINGS.Length] = s;
            SETTINGS = n;
            string[] k = new string[optionKeys.Length + 1]; IPatch[] q = new IPatch[optionKeys.Length + 1];
            for (int i = 0; i < optionKeys.Length; i++) { k[i] = optionKeys[i]; q[i] = optionPatches[i]; }
            k[optionKeys.Length] = s.key; q[optionKeys.Length] = p;
            optionKeys = k; optionPatches = q;
        }
        public static bool IsPackageOption(string key) { for (int i = 0; i < optionKeys.Length; i++) if (P.Eq(optionKeys[i], key)) return true; return false; }
        public static void HideSetting(string key, string why)
        {
            int i = SetIndex(key);
            if (i < 0) throw new Stop("HideSetting: there is no option " + key);
            for (int k = 0; k < LEVELS.Length; k++)
                if (Required(key, LEVELS[k]) != null) throw new Stop(SETTINGS[i].name + " cannot be hidden: it is " + Required(key, LEVELS[k]));
            OwnSettings();
            if (HiddenWhy(key) != null) return;
            hiddenKeys.Add(key); hiddenWhy.Add(P.Len(why) > 0 ? why : "removed by the patcher package");
        }
        public static string HiddenWhy(string key) { for (int i = 0; i < hiddenKeys.Count(); i++) if (P.Eq(hiddenKeys.Get(i), key)) return hiddenWhy.Get(i); return null; }
        public static Setting Option(string key)
        {
            if (SetIndex(key) < 0) throw new Stop("Option: there is no option " + key);
            OwnSettings();
            return SETTINGS[SetIndex(key)];
        }
        // inside IPatch.Apply: whether an option is on in the build being made (false outside a build), and its preset
        public static bool IsOn(string key) { int i = SetIndex(key); return buildingOn != null && i >= 0 && i < buildingOn.Length && buildingOn[i]; }
        public static Level BuildingPreset() { return buildingLv; }
        // empties everything a package added or changed: patches, options, hidden options, changed options
        public static void ClearPatches()
        {
            EXTRA = new IPatch[0]; optionKeys = new string[0]; optionPatches = new IPatch[0];
            hiddenKeys = new StrList(); hiddenWhy = new StrList();
            if (builtinSettings != null) { SETTINGS = builtinSettings; builtinSettings = null; }
        }

        public static Level[] LEVELS = new Level[] {
            new Level("220", "stock", "STOCK (220) - XP-Pen's own timing", 0, 45, 40, 0, 0, false, 0, 0, 0),
            new Level("300", "quick", "QUICK (300) - quiet, checks the pen's frequency every frame", 3333, 18, 30, 4, 0, true, 2, 0, 0),
            new Level("500", "fast", "FAST (500) - balanced", 2000, 10, 20, 8, 0, false, 2, 0, 0),
            new Level("800", "rapid", "RAPID (800) - low delay", 1250, 5, 18, 8, 3, false, 2, 0, 0),
            new Level("1000", "ultra", "ULTRA (1000) - lowest delay, most noise, shorter pressure readings", 1000, 4, 16, 16, 1, false, 4, 8, 16) };

        public static int[] BoostOf(string key)
        {
            if (P.Eq(key, "500")) return new int[] { 4000, 32, 40 };
            if (P.Eq(key, "800")) return new int[] { 2500, 24, 40 };
            if (P.Eq(key, "1000")) return new int[] { 2000, 20, 40 };
            return null;
        }

        // setting categories, in the order they are shown (key, heading)
        public static readonly string[] CATEGORIES = new string[] { "scan", "Reading the coils (no cursor delay)", "corr", "Position corrections (no cursor delay)",
            "latency", "Position filters (noise against delay)", "noise", "Noise-related tuning",
            "apip", "ALPI: ALgorithmic pen Position Interpolation (pick one)",
            "hover", "Hover and pickup range", "exp", "Experimental hardware", "storage", "Warranty and Firmware Discreetness",
            PKG_CAT, "Package options" };

        // R45 (Anargon): "Fallback is default values that are produced by all calibration steps that I have done". Where
        // this run measured no signal thresholds (wizard: Options.fallback), each preset that Anargon measured uses
        // Anargon's values (rounded readings, like calibration files; never above the preset's own, so the pickup range
        // never shrinks):
        //   RAPID: Tablet signal thresholds on 2026-11-09 (calibration-rapid.txt: pick 135, drop 90, Jump Rejection
        //          margin 1.293 mm = 4 x the 5 mm shake).
        //   ULTRA: the R44 run on 2026-11-10 (5 mm 112, highest hover 96: pick 93, drop 72, capped to ULTRA's 64). That
        //          run's 5 mm hold moved 2.9 mm, so the tool kept no file and its Jump Rejection margin is not used.
        // Not measured by Anargon: QUICK and FAST (their own levels). Measured levels need the stock scan frequency, so
        // Pen frequency tuning builds keep the preset's own levels.
        public static readonly string[] FB_KEY = new string[] { "800", "1000" };
        public static readonly int[] FB_PICK = new int[] { 135, 93 }, FB_DROP = new int[] { 90, 64 };
        public static readonly double[] FB_ODD = new double[] { 1.293, -1 };
        public const string FB_WHO = "Anargon's own measurements";
        public static int FallbackIndex(string key) { for (int i = 0; i < FB_KEY.Length; i++) if (P.Eq(FB_KEY[i], key)) return i; return -1; }
        // the fallback thresholds this build would use, or -1
        public static int FallbackFor(Level lv, bool[] st, Options o)
        {
            if (!o.fallback || o.telemetry || o.calPick >= 0 || o.calDrop >= 0 || lv.period <= 0) return -1;
            if (CalibrationProfileError(st, o) != null) return -1;
            return FallbackIndex(lv.key);
        }

        // key, name, category, default, file tag, what it does | what it costs (shown on separate lines). The list order
        // is the order of the file-name tags; the screens show the categories in Cli.NOISE_CATS / QOL_CATS order and, inside
        // a category, the settings in Cli.SCREEN_ORDER order (R45, 2026-11-17: grouped by where they act), then the rest.
        public static string LabOrderText(int w)
        {
            int[] ids = Lab.Decode(w); string t = "";
            for (int i = 0; i < ids.Length; i++) t += (i > 0 ? " > " : "") + (ids[i] <= Lab.ID_MAX ? Lab.TAG[ids[i]] : "?");
            return ids.Length == 0 ? "none" : t;
        }

        public static Setting[] SETTINGS = new Setting[] {
            new Setting("odd", "Jump Rejection", "latency", true, "JR", "Ignores a single reading that jumps far off the pen's path and continues the path for that frame instead.|A real, sudden change of direction can lose its first reading."),
            new Setting("smoothing", "Movement Smoothing", "latency", false, "SM", "Ignores tiny movements, so a still pen does not shake (the stock dead zone).|The cursor lags while moving and can sit a little off the pen tip."),
            new Setting("cal", "Pen frequency tuning (experimental)", "noise", false, "FT", "Press hard once within 3 s of plugging in: the tablet tries a few scan frequencies and keeps the steadiest. The LED blinks for about 4 s.|Can lower hover height. Not with Tablet Calibration's measured levels or ALPI (both use the stock frequency)."),
            new Setting("accp", "Accurate Pressure Readings", "scan", true, "AP", "Reads pressure and the pen buttons with full-length readings, like stock. Without it, pressure can show while hovering and light presses can be missed.|Fewer pressure updates per second (below). Does not fit ULTRA."),
            new Setting("fullres", "12-bit Readings", "scan", true, "12B", "Keeps every coil reading at the full 12 bits the tablet measures. Stock drops the lowest 3 bits, so readings move in steps of 8 and the cursor shakes, most of all while hovering. On Anargon's tablet at ULTRA, hovering low: 0.38x the sideways shake and 0.26x up and down. Pick-up and drop levels are raised to match, so the hover range stays the same.|None: no extra frame time."),
            // R45: two causes of a still pen's shake at ULTRA, found on Anargon's tablet (Sched.HOLD_ADDR, Sched.COIL_MARGIN)
            new Setting("fhold", "Frequency Hold", "scan", true, "FH", "Keeps the scan on one pen frequency step until 8 frequency checks in a row agree that another is better. Stock picks again after every check, and at ULTRA it hops between two steps at random; each hop moves the cursor about 0.5 mm. On Anargon's tablet at ULTRA, pen held still: 0.71x the sideways shake and 0.78x up and down.|No cursor delay. After a real frequency change (another pen) the scan can be one step off for up to 8 checks (about 65 ms at ULTRA)."),
            new Setting("chold", "Coil Hold", "scan", true, "CH", "Keeps reading around the same coil while the pen sits between two coils, and moves on only when the next coil reads clearly stronger. The middle coil always reads 2-3 % low, so stock switched coils in most frames there and the cursor shook.|No cursor delay. A moving pen switches up to about 1 mm past the middle between two coils."),
            // R45-rc3 (Anargon: "its nice to disclose all firmware"): the rc2 SysTick fix as its own setting
            new Setting("systick", "Bootloader Timer Off", "scan", true, "TO", "Switches off a 1 ms timer that the tablet's bootloader leaves running; the firmware never uses it. It paused the scan every 1 ms and gave bursts of shake at ULTRA, worst between two coils (up to 0.2 mm on Anargon's tablet).|None found. Off = stock behaviour."),
            // R45 (Anargon, 2026-11-17: the filter order tested live, then "these all get bundled as part of a single option
            // called 'Misc. Optimizations'"): one switch for the position fixes below (MISC_KEYS; Coil Gains joined 2026-11-22); the screens show only
            // this one. Order in the firmware: Switch Match > Coil Linearization (table B) > ALPI > Dynamic Anti-Chatter,
            // Window Scale inside the stock position routine (experiments/precision/RESULTS.md)
            new Setting("misc", "Misc. Optimizations", "corr", true, "MO", "Five position fixes, all on together. Switch Match: no cursor step where the tablet changes which coils it reads. Window Scale (ULTRA): no step at each coil switch. Coil Gains (ULTRA): evens out how strongly each coil reads, so slanted lines bend less. Coil Linearization: an even cursor across every coil (a table learned on Anargon's ruler lines). Dynamic Anti-Chatter: pulls back a reading that jumps off the pen's line. Only past positions are used, nothing ahead of the pen. On Anargon's tablet (ULTRA, ALPI, along a ruler): 0.02-0.04 mm of error left along an axis, 0.06-0.09 mm on a slanted line.|Up to 0.4 ms of delay, only on the frames Dynamic Anti-Chatter pulls back (about 0.1 ms on average). Measured on one tablet and pen (Anargon's); another tablet may gain less."),
            // R45-rc3 (Anargon's low hover shake): see Sched.Patch, "Tilt Frame Skip"
            new Setting("smatch", "Switch Match", "corr", true, "SWM", "When the tablet changes which coils it reads (slanted lines: 0.24 mm steps on Anargon's tablet) or how long it reads (hover height: up to 0.67 mm), the new reading is matched to the line the pen was following, and the match fades out over half a coil. Uses only past positions, nothing ahead of the pen.|Only while the pen moves (15 mm/s or more) and not right after another coil switch, so a still pen is left alone. A fast flick or a sharp turn right at a switch is left as it is."),
            // R45-rc5 (Anargon: "Prevents chatter by only applying smoothing on detection of chatter"): see atf_antichatter.cs
            new Setting("dac", "Dynamic Anti-Chatter", "latency", true, "DAC", "Prevents chatter by only applying smoothing on detection of chatter: a position more than 0.10 mm off the straight line through the last two is pulled toward the previous one (fully from 0.20 mm); every other frame passes as it is.|Up to 0.4 ms of delay, only on the frames it smooths (about 0.1 ms on average)."),
            // R45-rc5 (Anargon: the coil-scale error "is still bad"; "we are only keeping the best one and it is opt in by default"): see atf_coillin.cs
            new Setting("clin", "Coil Linearization", "corr", true, "CL", "Corrects the position inside each coil with a fixed table, so the cursor moves evenly across every coil. On Anargon's tablet along a ruler, the error that repeats at every coil fell from 0.20-0.26 to 0.08-0.15 mm (X) and from 0.08-0.14 to 0.05-0.07 mm (Y). No delay.|Learned on one tablet and pen (Anargon's, at ULTRA); another tablet may gain less."),
            // R45-rc4 (2) (Anargon: "X will not report position consistently"): see atf_winscale.cs
            new Setting("wscale", "Window Scale", "corr", true, "WS", "Corrects the position inside each group of coils the tablet reads, by pressure: the readings stretch the position there, so the cursor stepped at every coil switch (X 0.24 mm on Anargon's tablet; 0.08 mm with it, 0.04 mm with Switch Match too, offline). No delay.|Measured on one tablet and pen (Anargon's). ULTRA only, at its fastest hover step. Not with Multi-coil Read."),
            // PR1 (Anargon, 2026-11-22: the slanted-line error "That goes into misc options."): see atf_coilgain.cs
            new Setting("cgain", "Coil Gains", "corr", true, "CG", "Evens out how strongly each coil reads before the position is worked out. Some coils read 1-2 % stronger or weaker than the next one, which bends a slanted line. Set to 58.9 % of the measured difference: on every recorded ruler session of Anargon's tablet that took 9-22 % off the slanted-line error (offline); on the tablet, the error that repeats on every pass fell from 0.021 to 0.009 mm (at 60 %). No delay.|Measured on one tablet and pen (Anargon's); how much of the difference showed changed between sessions there (0.5-1.5 x). ULTRA only, with Window Scale. Not with Multi-coil Read."),
            new Setting("tiltskip", "Tilt Frame Skip", "scan", true, "TS", "4 frames in every 16 also measure tilt. While the pen hovers close, their positions came out 0.2-0.45 mm off on Anargon's tablet (a shake about 60 times a second). Hovering close, those frames now read the position like the others.|Tilt keeps its last value while the pen hovers close; touching, Steady Hover and Reach Stage measure it as before. Checked in the digital twin; not measured on a tablet yet."),
            new Setting("interp", "Position Interpolation", "latency", true, "PI", "Averages the last 3-5 positions (stock), so the cursor is steadier.|Adds cursor delay (measured, below)."),
            new Setting("interp2", "Aggressive Position Interpolation", "latency", false, "AI", "Averages more positions: 16 while still, 8 while slow, 4 while fast.|More cursor delay, most when moving slowly. Needs Position Interpolation."),
            // R45 (Anargon: "put the filters each as options under a dedicated APIP section"): one at a time (Build.ApipKind)
            new Setting("apip", "ALPI: Tracker", "apip", false, "APIP", "A trained tracking filter: it follows the pen's position and speed and moves toward each reading, so it does not trail behind the way an average does, and holds much steadier while the pen is still. On Anargon's tablet (ULTRA, pen still at the centre, own training): 13x less shake sideways and 16x less up and down than no filter. The steadiest ALPI filter on a real tablet.|About 10 us per frame: fits every preset."),
            new Setting("apipr2", "ALPI: Robust, 2 trackers", "apip", false, "APIPR2", "Experimental. Two trackers side by side, one for a still pen and one for quick moves, following whichever explains the readings best. On Anargon's tablet (ULTRA, pen still) it shook about 2.3x more sideways and 3.2x more up and down than the Tracker.|About 20 us per frame: the most that fits ULTRA."),
            new Setting("apipr3", "ALPI: Robust, 3 trackers", "apip", false, "APIPR3", "Experimental. Three trackers. Replayed on Anargon's recorded centre hold (not flashed): 1.3x the Tracker's shake sideways, about the same up and down.|About 27 us per frame: too much for ULTRA."),
            new Setting("apipr4", "ALPI: Robust, 4 trackers", "apip", false, "APIPR4", "Experimental. Four trackers. Replayed on Anargon's recorded centre hold (not flashed): about the Tracker's shake sideways, 1.2x up and down.|About 34 us per frame: too much for ULTRA."),
            new Setting("boost", "Steady Hover", "hover", true, "SH", "When the pen is high and its signal gets weak, the tablet reads longer and reports less often, only as far as it must to keep the cursor as steady as at low hover. It slows down in small steps (ULTRA: 1000 down to 426 reports per second; FAST: 500 down to 370) and speeds up again as the pen comes closer. A still pen keeps its step.|The report rate drops step by step while active. Full rate comes back when the pen gets closer or touches."),
            new Setting("reach", "Reach Stage", "hover", true, "RS", "If the signal is still too weak, drops to 60 reports per second to keep the pen tracked. A pen it already tracks is kept down to 60 % of the level needed to pick it up (digital twin: held up to about 26 mm).|60 reports per second while active. Needs Steady Hover. A pen lifted away is let go a little higher up."),
            new Setting("longrange", "Long-range Pickup", "hover", true, "LP", "Uses stronger pulses while looking for the pen, so it is found from higher up.|Each long search takes about 0.1 s."),
            new Setting("multicoil", "Multi-coil Read", "exp", false, "MC", "EXPERIMENTAL. Works out the position from the five coils around the pen, using the coil response measured on a real tablet. On Anargon's tablet at ULTRA (hovering low) it helps only without 12-bit Readings (0.68x the sideways shake); with 12-bit Readings it shook 1.6x more sideways and 1.4x more up and down. No gain with ALPI. Leave it off.|500 reports per second and faster only."),
            new Setting("volatile", "Volatile Changes", "storage", true, "volatile", "Lets the tablet send a report every 1 ms instead of every 3 ms, by editing the firmware's start-up data (.data). A stock firmware update puts it back.|Needed for RAPID and ULTRA."),
            new Setting("bootloader", "Bootloader Changes", "storage", false, "BL", "Lets the firmware count updates in the bootloader area.|A stock firmware update does not remove this. May affect warranty."),
            new Setting("blcounter", "Bootloader Counter", "storage", false, "BC", "The bootloader counts every update itself, stock and failed ones too. Needs Bootloader Changes and a full dump. Worked on Anargon's tablet.|Its first start rewrites bootloader pages: keep the tablet plugged in for 10 s after flashing. A power cut then can need a hardware repair.") };
        public const string WARRANTY = "Bootloader Changes and the Bootloader Counter stay after a stock firmware update, which may affect warranty.";
        // R44 (Anargon's wording, kept to what is true: an incomplete flash stops the tablet with or without them, and
        // the bootloader survives it, so it can be reflashed)
        public const string VOLATILE_WARN = "Volatile Changes change how the tablet connects to your system (a USB report every 1 ms). They are in the part of the firmware that an incomplete flash loses, so only flash complete files made by this tool. A stock firmware update undoes them.";
        public const string INTERP_RAW = "Smoothing and both interpolations off = raw positions. Jump Rejection is separate.";
        public const string CALC_TIME = "Processing time: no measurable increase in the twin.";
        public const string ALWAYS_ON = "Always on: hardware divide, the full-pressure fix, pick-up and drop levels matched to each speed, and longer pressure readings where a speed needs them. The LED works as in stock (it blinks while Pen frequency tuning runs).";
        public const string TRUNC_WARN = "This file is missing the firmware's start-up data (.data).";
        public const string TRUNC_HELP = "Without it, the tablet may not start. Use your tablet's complete main.bin dump instead.";
        public const string BUILD_WARN = "This tool has not been tested with this firmware build. Check the compatibility results and use your tablet's own complete dump.";
        public static string BuildWarn2() { return "Warning: This build also does not match any other known public builds. You may want to join " + Conf.SupportUrl() + " and go to " + Conf.SupportWhere(); }

        public static string Ascii(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < P.Len(s); i++) { int c = P.CharAt(s, i); sb.Append(c < 128 ? (char)c : '?'); }
            return sb.ToString();
        }
        public static bool quiet = false;
        public static bool probing = false;                // compatibility probe: collect site mismatches, do not write or throw
        public static bool forcing = false;                // retained internal force mode; the CLI does not expose it
        public static bool inputHasBoot = false;          // the input carries the bootloader (full 64 KB dump)
        public static byte[] stockBoot = null;                // in-memory builds for measurements: no output, no logs
        public static bool counterFound = false;          // R45: the input backup carries this tool's Bootloader Counter (CounterIn)
        public static byte[] stockApp = null;                 // the unmodified input application, for the latency baseline
        static Latency stockDelay = null;                     // cached stock cursor delay (null = not measured yet)
        // the cursor delay of the unmodified input firmware (the stock baseline; not necessarily what the tablet runs
        // now), measured once per input
        public static Latency StockDelay()
        {
            if (stockDelay != null) return stockDelay;
            stockDelay = stockApp != null ? Twin.MeasureDelay(Twin.FromApp(stockApp), true, true) : new Latency();
            return stockDelay;
        }
        public static bool StockDelayKnown() { return stockDelay != null; }
        public static void CacheStockDelay(Latency l) { if (l != null && l.ok) stockDelay = l; }
        public static void Say(string s) { if (quiet) return; P.Line(s); LOG.Verbose(s); }
        public static void Tell(string s) { if (quiet) return; P.Line(s); LOG.Main(s); }

        public static string CategoryName(string key) { for (int i = 0; i < CATEGORIES.Length; i += 2) if (P.Eq(CATEGORIES[i], key)) return CATEGORIES[i + 1]; return key; }

        public static Level Lv(string key) { for (int i = 0; i < LEVELS.Length; i++) if (P.Eq(LEVELS[i].key, key)) return LEVELS[i]; return null; }
        public static int LevelIndex(string key) { for (int i = 0; i < LEVELS.Length; i++) if (P.Eq(LEVELS[i].key, key)) return i; return -1; }
        public static int SetIndex(string key) { for (int i = 0; i < SETTINGS.Length; i++) if (P.Eq(SETTINGS[i].key, key)) return i; return -1; }
        public static bool On(bool[] st, string key) { return st[SetIndex(key)]; }
        // R45: APIP's filters, at most one on: kind 1 = the tracker (atf_apip.cs), 2-4 = the robust filter with that many
        // trackers (atf_apipr.cs); 0 = none
        public static readonly string[] APIP_KEYS = new string[] { "apip", "apipr2", "apipr3", "apipr4" };
        // R45: the settings Misc. Optimizations switches; not shown on their own (Cli.ShowGroup), still on the command line
        public static readonly string[] MISC_KEYS = new string[] { "smatch", "wscale", "cgain", "clin", "dac" };
        public static bool InMisc(string key) { for (int i = 0; i < MISC_KEYS.Length; i++) if (P.Eq(MISC_KEYS[i], key)) return true; return false; }
        // Misc. Optimizations on or off: its settings follow
        public static void SetMisc(bool[] st, bool on) { st[SetIndex("misc")] = on; for (int i = 0; i < MISC_KEYS.Length; i++) st[SetIndex(MISC_KEYS[i])] = on; }
        public const string APIP_NOTE = "Pick one. Each learns your tablet's noise in Full calibration (step 4), or uses Anargon's training, and replaces Position Interpolation, Movement Smoothing and Jump Rejection. None adds cursor delay of its own (simulation). On a real tablet the Tracker is the steadiest.";
        public static int ApipKindOf(string key) { for (int i = 0; i < APIP_KEYS.Length; i++) if (P.Eq(APIP_KEYS[i], key)) return i == 0 ? 1 : i + 1; return 0; }
        public static bool IsApip(string key) { return ApipKindOf(key) > 0; }
        public static string ApipKey(int kind) { return kind <= 0 ? null : kind == 1 ? "apip" : "apipr" + F.Str(kind); }
        public static int ApipKind(bool[] st)
        {
            if (st == null) return 0;
            for (int i = 0; i < APIP_KEYS.Length; i++) { int k = SetIndex(APIP_KEYS[i]); if (k < st.Length && st[k]) return ApipKindOf(APIP_KEYS[i]); }
            return 0;
        }
        public static void ApipOff(bool[] st) { for (int i = 0; i < APIP_KEYS.Length; i++) st[SetIndex(APIP_KEYS[i])] = false; }

        // ---- step-time model (twin-measured)
        public static double StepWorkUs(int burst, int samples) { return 24.7 + 1.94 * burst + 1.9 * samples; }
        public static int TickFor(int burst, int samples) { int t = (int)(StepWorkUs(burst, samples) + 1.5 + 0.999); return t < 255 ? t : 255; }
        public static double StepPeriod(int burst, int samples, int poll)
        {
            double w = StepWorkUs(burst, samples);
            int t = TickFor(burst, samples);
            return t + 2.5 < w + 3 ? w + 3 + poll + 8 : t + 11;
        }
        // the scan time of the busiest frame (normal vs tilt), used by FrameNeedUs and the build log
        static double FrameScanUs(Level lv, int samples, int burst, int swS, int swB, int poll)
        {
            int sweepSteps = lv.chunk == 0 ? lv.sweepLen : lv.chunk;
            double scan = StepPeriod(burst, samples, poll), sw = StepPeriod(swB, swS, poll);
            double normal = sweepSteps * sw + 10 * scan;
            double tilt = (lv.sweepOnTilt ? sweepSteps * sw : 0) + (10 + (lv.split == 2 ? 2 : 1)) * scan;
            return normal > tilt ? normal : tilt;
        }
        // R43: multi = Multi-coil Read on (MultiCoil.MC_US of processing per frame)
        public static double FrameNeedUs(Level lv, int samples, int burst, int swS, int swB, int poll, bool smoothing, bool interp2, bool multi)
        {
            return FrameNeedUs(lv, samples, burst, swS, swB, poll, smoothing, interp2, multi, 0);
        }
        // R44: apip = Algorithmic Position Interpolation (R45: its filter, Build.ApipKind; Apip.ProcUs per frame)
        public static double FrameNeedUs(Level lv, int samples, int burst, int swS, int swB, int poll, bool smoothing, bool interp2, bool multi, int apip)
        {
            return FrameScanUs(lv, samples, burst, swS, swB, poll) + PROC_US + FilterProcUs(smoothing, interp2, apip, lv.period) + (multi ? MultiCoil.MC_US : 0);
        }

        public static StrList Validate(int samples, int burst, int poll, int periodUs, Level lv, int swS, int swB, bool smoothing, bool interp2, bool multi)
        {
            return Validate(samples, burst, poll, periodUs, lv, swS, swB, smoothing, interp2, multi, 0);
        }
        public static StrList Validate(int samples, int burst, int poll, int periodUs, Level lv, int swS, int swB, bool smoothing, bool interp2, bool multi, int apip)
        {
            StrList err = new StrList();
            if (!(burst >= 1 && burst <= 254)) err.Add("pulses " + F.Str(burst) + ": must be 1..254 (255 never stops the excitation timer)");
            if (burst < MIN_BURST) err.Add("pulses " + F.Str(burst) + ": below " + F.Str(MIN_BURST) + " the pen signal falls under the firmware's fixed detection thresholds");
            if (!(samples >= 2 && samples <= 99)) err.Add("samples " + F.Str(samples) + ": must be 2..99 (the ADC buffer holds 100 entries)");
            if (samples < MIN_SAMPLES) err.Add("samples " + F.Str(samples) + ": below " + F.Str(MIN_SAMPLES) + " the pen is never picked up (ULTRA with 3 samples saw no pen on the tablet)");
            if (!(poll >= 5 && poll <= 50)) err.Add("poll " + F.Str(poll) + ": must be 5..50 us");
            if (periodUs > 0 && lv != null)
            {
                double need = FrameNeedUs(lv, samples, burst, swS, swB, poll, smoothing, interp2, multi, apip);
                if (need > periodUs - 25)
                    err.Add("frame budget: " + F.Fx(need, 0) + " us of work does not fit one frame of " + F.Str(periodUs) + " us (frames would overrun and jitter)");
            }
            return err;
        }

        // do these filters fit this preset's frame (the build trims readings down to the detection floor to try)?
        public static bool FiltersFit(Level lv, bool smoothing, bool interp2)
        {
            int s = lv.samples, b = lv.burst;
            while (true)
            {
                int sws = lv.swSamples > 0 ? (lv.swSamples > s ? lv.swSamples : s) : s, swb = lv.swBurst > 0 ? (lv.swBurst > b ? lv.swBurst : b) : b;
                if (FrameNeedUs(lv, s, b, sws, swb, 10, smoothing, interp2, false) <= lv.period - 25) return true;
                if (s > MIN_SAMPLES) s--; else if (b > MIN_BURST) b--; else return false;
            }
        }
        public static bool SmoothingFits(Level lv) { return FiltersFit(lv, true, false); }
        // R43: does Multi-coil Read fit this preset's frame at its own readings (with the sweep Accurate Pressure
        // Readings gives it, and the filters chosen)? It is never bought by shorter readings: they would cost more
        // signal than the fit gains.
        public static bool MultiFits(Level lv0, bool[] st)
        {
            if (lv0.period <= 0) return false;
            bool[] s2 = new bool[SETTINGS.Length];
            for (int i = 0; i < SETTINGS.Length; i++) s2[i] = st != null && i < st.Length && st[i];
            s2[SetIndex("multicoil")] = true;
            Level lv = Effective(lv0, s2, new Options());
            int swS = lv.swSamples > 0 ? lv.swSamples : lv.samples, swB = lv.swSamples > 0 ? lv.swBurst : lv.burst;
            return FrameNeedUs(lv, lv.samples, lv.burst, swS, swB, 10, On(s2, "smoothing"), On(s2, "interp") && On(s2, "interp2"), true, ApipKind(s2)) <= lv.period - 25;
        }
        // R44: does APIP fit this preset's frame at its own readings (with Multi-coil Read if chosen)? R45: KIND = its
        // filter (Build.ApipKind)
        public static bool ApipFits(Level lv0, bool[] st, int kind)
        {
            if (lv0.period <= 0 || kind <= 0) return false;
            bool[] s2 = new bool[SETTINGS.Length];
            for (int i = 0; i < SETTINGS.Length; i++) s2[i] = st != null && i < st.Length && st[i];
            ApipOff(s2); s2[SetIndex(ApipKey(kind))] = true; s2[SetIndex("smoothing")] = false; s2[SetIndex("interp2")] = false;
            Level lv = Effective(lv0, s2, new Options());
            int swS = lv.swSamples > 0 ? lv.swSamples : lv.samples, swB = lv.swSamples > 0 ? lv.swBurst : lv.burst;
            return FrameNeedUs(lv, lv.samples, lv.burst, swS, swB, 10, false, false, On(s2, "multicoil"), kind) <= lv.period - 25;
        }

        // ---- flash counter: page 0x08002C00 in the bootloader area (free in stock; the bootloader never reads it).
        //      +0 "AnSC", +4 base, +8 ~base; from +16 records (build id, ~build id); count = base + valid records.
        public const int COUNTER_PAGE = 0x08002C00, COUNTER_MAGIC = 0x43536E41;
        public const int COUNT_MAX = 999999;       // a base above it is a corrupt header (no counter); counts above it show as it
        public static int ReadFlashCount(byte[] flash64k)
        {
            if (flash64k.Length < 0x10000) return -1;
            int o = COUNTER_PAGE - 0x08000000;
            if (Machine.LE(flash64k, o, 4) != COUNTER_MAGIC) return -1;
            int bse = Machine.LE(flash64k, o + 4, 4);
            if (Machine.LE(flash64k, o + 8, 4) != ~bse) return -1;
            if (bse < 0 || bse > COUNT_MAX) return -1;
            int n = bse;
            for (int r = o + 16; r + 8 <= o + 0x400; r += 8)
            {
                int id = Machine.LE(flash64k, r, 4), chk = Machine.LE(flash64k, r + 4, 4);
                if (id == -1 && chk == -1) break;
                if (chk == ~id) n++;
            }
            return n > COUNT_MAX ? COUNT_MAX : n;
        }

        public static bool Has(StrList l, string part) { for (int i = 0; i < l.Count(); i++) if (P.IndexOf(l.Get(i), part) >= 0) return true; return false; }

        // Volatile Changes cannot be turned off where the preset needs the 1 ms report interval (above 500 per second)
        public static string Required(string key, Level lv) { return P.Eq(key, "volatile") && lv.period > 0 && lv.period < 2000 ? "required by " + P.Sub(lv.label, 0, P.IndexOf(lv.label, " (")) : null; }
        public static bool VolatileOn(bool[] st, Level lv) { return On(st, "volatile") || Required("volatile", lv) != null; }

        // R41: Accurate Pressure Readings. The 6-step frequency sweep uses stock's step length (45 samples, 40 pulses),
        // with as many steps per frame as fit next to the scan and every optional filter: QUICK the whole sweep, FAST 3
        // steps, RAPID 1. ULTRA's 1 ms frame has no room for one. Returns null where it does not apply. Shorter steps let
        // the pen's ringing carry from one step into the next; in the twin that biased the frequency up by up to 9 kHz
        // (Q 300), which shows pressure while hovering, and missed light presses. Stock-length steps did neither.
        public const int ACC_SAMPLES = 45, ACC_BURST = 40;
        public static Level Accurate(Level lv, bool[] st)
        {
            if (lv.period == 0) return null;
            bool multi = st != null && On(st, "multicoil");
            int[] chunks = new int[] { 0, 3, 2, 1 };
            for (int i = 0; i < chunks.Length; i++)
            {
                Level a = new Level(lv.key, lv.name, lv.label, lv.period, lv.samples, lv.burst, lv.tilt, chunks[i], lv.sweepOnTilt, lv.split, ACC_SAMPLES, ACC_BURST);
                if (FrameNeedUs(a, lv.samples, lv.burst, ACC_SAMPLES, ACC_BURST, 10, true, true, multi) <= lv.period - 25) return a;
            }
            return null;
        }
        // the level a build really uses: the preset, or its Accurate Pressure Readings variant
        public static Level Effective(Level lv, bool[] st, Options o)
        {
            if (lv.period == 0 || st == null || !On(st, "accp") || (o != null && (o.samples >= 0 || o.burst >= 0))) return lv;
            Level a = Accurate(lv, st);
            return a != null ? a : lv;
        }
        // frequency sweeps (pressure updates) per second: tilt frames carry no sweep unless sweepOnTilt, and a sweep split
        // into chunks takes one frame per chunk
        public static double SweepRate(Level lv)
        {
            if (lv.period == 0) return 1e6 / 4500.0;
            double frames = 1e6 / lv.period;
            int phases = lv.split == 2 ? 2 : 4;
            if (!lv.sweepOnTilt) frames = frames * (lv.tilt - phases) / lv.tilt;
            int per = lv.chunk <= 0 || lv.chunk >= lv.sweepLen ? 1 : (lv.sweepLen + lv.chunk - 1) / lv.chunk;
            return frames / per;
        }

        public static string Unavailable(string key, Level lv, bool[] st)
        {
            string hid = HiddenWhy(key);                     // R45-rc3: hidden by a patcher package
            if (hid != null) return hid;
            string name = P.Sub(lv.label, 0, P.IndexOf(lv.label, " ("));
            bool paced = lv.period > 0;
            if (P.Eq(key, "accp") && !paced) return "STOCK already reads this way";
            if (P.Eq(key, "accp") && Accurate(lv, st) == null) return "does not fit " + name + "'s " + F.Fx(lv.period / 1000.0, 1) + " ms frame";
            if ((P.Eq(key, "boost") || P.Eq(key, "reach")) && BoostOf(lv.key) == null) return "not used by " + name;
            if (P.Eq(key, "reach") && st != null && !On(st, "boost")) return "needs Steady Hover";
            if (P.Eq(key, "interp2") && !paced) return "not used by " + name;
            // R44: APIP replaces Position Interpolation and Movement Smoothing (it filters the frame's final position);
            // R45: and Jump Rejection (its coasting turned a hover spike into a ramp APIP then followed)
            if (IsApip(key) && !paced) return "not used by " + name;
            // R45-rc3: Pen frequency tuning moves the scan frequency every ALPI training is made at (Full calibration
            // keeps the stock frequency), so ALPI is not offered with it
            if (IsApip(key) && st != null && On(st, "cal")) return "not with Pen frequency tuning (ALPI is trained at the stock frequency)";
            if (IsApip(key) && !ApipFits(lv, st, ApipKindOf(key))) return "does not fit " + name + "'s " + F.Fx(lv.period / 1000.0, 1) + " ms frame";
            if ((P.Eq(key, "smoothing") || P.Eq(key, "interp") || P.Eq(key, "interp2") || P.Eq(key, "odd")) && paced && st != null && ApipKind(st) > 0 && ApipFits(lv, st, ApipKind(st)))
                return "replaced by ALPI";
            if (P.Eq(key, "blcounter") && !paced) return "not used by " + name;
            if (P.Eq(key, "blcounter") && st != null && !On(st, "bootloader")) return "needs Bootloader Changes";
            if (P.Eq(key, "blcounter") && !inputHasBoot) return "needs a full 64 KB dump as input (ic902-main.bin or deco01v3-main.bin)";
            if (P.Eq(key, "smoothing") && paced && !FiltersFit(lv, true, false)) return "does not fit " + name + "'s " + F.Fx(lv.period / 1000.0, 1) + " ms frame";
            if (P.Eq(key, "interp2") && st != null && !On(st, "interp")) return "needs Position Interpolation";
            // smoothing and aggressive interpolation both fit alone, but at the tightest frame (1000 Hz) not together;
            // smoothing (the stock dead-band) keeps priority, aggressive yields
            if (P.Eq(key, "interp2") && paced && st != null && On(st, "smoothing") && !FiltersFit(lv, true, true))
                return "does not fit " + name + "'s " + F.Fx(lv.period / 1000.0, 1) + " ms frame with Movement Smoothing on";
            if ((P.Eq(key, "cal") || P.Eq(key, "odd") || P.Eq(key, "longrange") || P.Eq(key, "fullres") || P.Eq(key, "fhold") || P.Eq(key, "chold") || P.Eq(key, "systick")) && !paced) return "not used by " + name;
            // R43: Anargon - multi-coil is not worth it below 500 reports per second
            if (P.Eq(key, "tiltskip") && !paced) return "not used by " + name;
            if (P.Eq(key, "misc") && !paced) return "not used by " + name;
            if (P.Eq(key, "smatch") && !paced) return "not used by " + name;
            if (P.Eq(key, "dac") && !paced) return "not used by " + name;
            if (P.Eq(key, "clin") && !paced) return "not used by " + name;
            if (P.Eq(key, "wscale") && !paced) return "not used by " + name;
            if (P.Eq(key, "wscale") && lv.period != 1000) return "measured at ULTRA only";
            if (P.Eq(key, "wscale") && st != null && On(st, "multicoil")) return "replaced by Multi-coil Read";
            // Coil Gains runs inside Window Scale's wrapper of the stock position routine (atf_coilgain.cs)
            if (P.Eq(key, "cgain") && !paced) return "not used by " + name;
            if (P.Eq(key, "cgain") && lv.period != 1000) return "measured at ULTRA only";
            if (P.Eq(key, "cgain") && st != null && On(st, "multicoil")) return "replaced by Multi-coil Read";
            if (P.Eq(key, "cgain") && st != null && !On(st, "wscale")) return "needs Window Scale";
            if (P.Eq(key, "multicoil") && (lv.period <= 0 || lv.period > 2000)) return "only 500 reports per second and faster";
            if (P.Eq(key, "multicoil") && !MultiFits(lv, st)) return "does not fit " + name + "'s " + F.Fx(lv.period / 1000.0, 1) + " ms frame";
            return null;
        }

        public static string AutoName(Level lv, bool[] st, bool custom)
        {
            StringBuilder sb = new StringBuilder((VolatileOn(st, lv) ? VOLATILE_PREFIX : "") + lv.name);
            bool raw = RawHover(lv, st);
            for (int i = 0; i < SETTINGS.Length; i++)
            {
                Setting s = SETTINGS[i];
                if (P.Eq(s.key, "volatile") || Unavailable(s.key, lv, st) != null || st[i] == s.dflt) continue;
                if (raw && P.Eq(s.cat, "hover")) continue;
                if (InMisc(s.key) && !On(st, "misc")) continue;          // R45: "-noMO" says it
                sb.Append(st[i] ? "-" + s.tag : "-no" + s.tag);
            }
            if (raw) sb.Append("-rawhover");
            if (custom) sb.Append("-custom");
            return sb.ToString() + ".hex";
        }

        // R42 (Anargon): the prefix is separated from the rest of the name, "[VOLATILE]_rapid.hex"
        public const string VOLATILE_PREFIX = "[VOLATILE]_";

        // R42 (Anargon): "-rawhover" for players who hover as a skill (osu!-style): every hover helper this preset
        // has (Steady Hover, Reach Stage, Long-range Pickup) is off, so the tablet keeps its full rate in the air
        public static bool RawHover(Level lv, bool[] st)
        {
            int avail = 0;
            for (int i = 0; i < SETTINGS.Length; i++)
            {
                if (!P.Eq(SETTINGS[i].cat, "hover") || Unavailable(SETTINGS[i].key, lv, st) != null) continue;
                if (st[i]) return false;
                avail++;
            }
            return avail > 0;
        }

        // Explicit names still disclose .data changes. Keep the directory unchanged and prefix only the filename.
        public static string MarkVolatile(string path, bool[] st, Level lv)
        {
            string name = P.FileName(path);
            if (!VolatileOn(st, lv) || P.StartsWith(name, "[VOLATILE]")) return path;
            string dir = P.DirName(path);
            return P.Len(dir) == 0 ? VOLATILE_PREFIX + name : P.PathJoin(dir, VOLATILE_PREFIX + name);
        }

        // R45 (Anargon: "an unused variable at the end of the file containing raw text similar to the version string,
        // saying the picked options on the build (e.g. ALPI, 12BIT, etc.)", stored at the hex end, read in a .bin):
        // "AnargonSS727_<date> <PRESET> <option> ...". Only options that are on and available; ASCII, one line.
        public static string OptionsText(Level preset, bool[] on, Options o, int pick, int drop, bool fallback, ApipModel apm)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(VERSION + "_" + P.Now("date") + " " + P.Upper(preset.name));
            if (o.telemetry) sb.Append(" MEASUREMENT-BUILD");
            if (o.apipTrain) sb.Append(" ALPI-TRAINING-BUILD");
            if (o.cleanup) sb.Append(" RETURN-TO-STOCK");
            for (int i = 0; i < SETTINGS.Length; i++) if (on[i]) sb.Append(" " + OptionWord(SETTINGS[i].key));
            if (RemovesCounter(preset, on, o)) sb.Append(" REMOVES-BOOTLOADER-COUNTER");
            if (apm != null && apm.trained != null) sb.Append(" ALPI-TRAINED=" + Printable(apm.trained));
            // R45-rc2: the measurement and training builds' own levels are not measured ones
            if (pick >= 0 && drop >= 0) sb.Append(" PICKUP=" + F.Str(pick) + " DROP=" + F.Str(drop) + (fallback ? " (ANARGON-DEFAULTS)" : o.telemetry || o.apipTrain ? " (MEASUREMENT-LEVELS)" : o.calPick >= 0 ? " (MEASURED)" : ""));
            if (o.Custom()) sb.Append(" CUSTOM-TUNING");
            return sb.ToString();
        }
        static string Printable(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < P.Len(s); i++) { int c = P.CharAt(s, i); sb.Append(c > 32 && c < 127 ? P.Sub(s, i, 1) : "-"); }
            return sb.ToString();
        }
        static string OptionWord(string key)
        {
            string[] k = new string[] { "odd", "smoothing", "cal", "accp", "fullres", "fhold", "chold", "systick", "tiltskip", "misc", "smatch", "wscale", "dac", "clin", "cgain", "interp", "interp2", "apip", "apipr2", "apipr3", "apipr4", "boost", "reach", "longrange", "multicoil", "volatile", "bootloader", "blcounter" };
            string[] w = new string[] { "JUMP-REJECTION", "SMOOTHING", "FREQ-TUNING", "ACCURATE-PRESSURE", "12BIT", "FREQ-HOLD", "COIL-HOLD", "TIMER-OFF", "TILT-SKIP", "MISC-OPTIMIZATIONS", "SWITCH-MATCH", "WINDOW-SCALE", "DYNAMIC-ANTI-CHATTER", "COIL-LINEARIZATION", "COIL-GAINS","INTERP", "AGGRESSIVE-INTERP", "ALPI-TRACKER", "ALPI-ROBUST2", "ALPI-ROBUST3", "ALPI-ROBUST4", "STEADY-HOVER", "REACH", "LONG-RANGE", "MULTICOIL", "VOLATILE", "BOOTLOADER-CHANGES", "BOOTLOADER-COUNTER" };
            for (int i = 0; i < k.Length; i++) if (P.Eq(k[i], key)) return w[i];
            return IsPackageOption(key) ? "PACKAGE-OPTION=" + P.Upper(key) : P.Upper(key);
        }

        public static string SettingsLine(bool[] st, Level lv)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < SETTINGS.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SETTINGS[i].name + " " + (Unavailable(SETTINGS[i].key, lv, st) != null ? "n/a" : st[i] ? "on" : "off"));
            }
            return sb.ToString();
        }

        // ---- hex / input
        public static void WriteHex(byte[] data, int bs, string path)
        {
            StringBuilder sb = new StringBuilder();
            int curHi = -1;
            for (int off = 0; off < data.Length; off += 16)
            {
                int addr = bs + off;
                if ((addr >> 16) != curHi)
                {
                    curHi = addr >> 16;
                    int[] r1 = new int[] { 2, 0, 0, 4, (curHi >> 8) & 0xFF, curHi & 0xFF };
                    sb.Append(Rec(r1, r1.Length) + "\r\n");
                }
                int n = data.Length - off < 16 ? data.Length - off : 16;
                int a = addr & 0xFFFF;
                int[] rec = new int[4 + n];
                rec[0] = n; rec[1] = a >> 8; rec[2] = a & 0xFF; rec[3] = 0;
                for (int k = 0; k < n; k++) rec[4 + k] = data[off + k] & 0xFF;
                sb.Append(Rec(rec, rec.Length) + "\r\n");
            }
            sb.Append(":00000001FF\r\n");
            P.WriteAllBytes(path, Encode(sb.ToString()));
        }
        static string Rec(int[] r, int n)
        {
            int sum = 0;
            StringBuilder sb = new StringBuilder(":");
            for (int i = 0; i < n; i++) { sum += r[i]; sb.Append(F.HexDigits(r[i], 2, true)); }
            sb.Append(F.HexDigits((-sum) & 0xFF, 2, true));
            return sb.ToString();
        }
        public static byte[] Encode(string s) { byte[] b = new byte[P.Len(s)]; for (int i = 0; i < b.Length; i++) b[i] = (byte)P.CharAt(s, i); return b; }

        public static int[] FindBuildString(byte[] d, int n)
        {
            for (int k = 1; k + 7 < n; k++)
            {
                if ((d[k] & 0xFF) == 0x03 && d[k + 1] == (byte)'2' && d[k + 2] == 0 && d[k + 3] == (byte)'0' && d[k + 4] == 0 && d[k + 5] == (byte)'2' && d[k + 6] == 0)
                {
                    int blen = d[k - 1] & 0xFF;
                    if (!(blen >= 10 && blen <= 64 && blen % 2 == 0)) return null;
                    return new int[] { k + 1, (blen - 2) / 2 };
                }
            }
            return null;
        }
        public static string Utf16At(byte[] d, int off, int nch)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < nch && off + 2 * i + 1 < d.Length; i++) sb.Append((char)((d[off + 2 * i] & 0xFF) | ((d[off + 2 * i + 1] & 0xFF) << 8)));
            string s = sb.ToString();
            while (P.Len(s) > 0 && (P.CharAt(s, P.Len(s) - 1) == 0 || P.CharAt(s, P.Len(s) - 1) == ' ')) s = P.Sub(s, 0, P.Len(s) - 1);
            return s;
        }
        public static string ReadBuildString(byte[] d, int n)
        {
            int[] bs = FindBuildString(d, n);
            if (bs != null) return Utf16At(d, bs[0], bs[1]);
            int k = BUILD_STR_AT - BASE;
            if (n > k && (d[k - 1] & 0xFF) == 0x03 && (d[k - 2] & 0xFF) >= 10 && (d[k - 2] & 0xFF) <= 64) return Utf16At(d, k, ((d[k - 2] & 0xFF) - 2) / 2);
            return null;
        }
        // the firmware version string (USB string descriptor) is always VERSION_<this computer's date>, on every build;
        // it is not a setting and is not shown. The descriptor is resized to exactly that string.
        public static void ApplyBrand(Image im)
        {
            int[] bs = FindBuildString(im.data, im.len);
            string nw = VERSION + "_" + P.Now("date");
            int n = P.Len(nw);
            if (bs == null || bs[1] < n) { if (im.Skip("version string not found")) return; throw new Stop("refusing to build: the firmware version string was not found in the input (use the stock 2025-02-14 firmware)"); }
            im.ProbeSeen(im.tag);
            for (int i = 0; i < bs[1]; i++)
            {
                im.data[bs[0] + 2 * i] = i < n ? (byte)P.CharAt(nw, i) : (byte)0;
                im.data[bs[0] + 2 * i + 1] = 0;
            }
            im.data[bs[0] - 2] = (byte)(2 + 2 * n);         // bLength
        }

        public static byte[] ParseHex(string path, int[] loOut)
        {
            return IntelHex.Read(path, loOut);
        }

        public static byte[] ReadInput(string path, InputInfo info)
        {
            byte[] raw, orig = null;
            if (P.EndsWith(P.Lower(path), ".bin"))
            {
                raw = P.ReadAllBytes(path);
                orig = raw;
                if (raw.Length >= 0x10000 && (Machine.LE(raw, 0, 4) & 0x2FFE0000) == 0x20000000 && (Machine.LE(raw, 0x3800, 4) & 0x2FFE0000) == 0x20000000)
                {
                    byte[] t = new byte[raw.Length - 0x3800]; P.Copy(raw, 0x3800, t, 0, t.Length); raw = t;
                }
            }
            else
            {
                int[] lo = new int[1];
                raw = ParseHex(path, lo);
                if (lo[0] != BASE) throw new Stop(path + ": starts at " + F.Hx(lo[0], 8) + ", expected the application at " + F.Hx(BASE, 8));
            }
            // The firmware's real end comes from its own scatter-load table (the firmware layout); a build verified by
            // this tool also has it stored. Anything after the end (0xFF, or code added by an earlier build) is dropped.
            string bstr = ReadBuildString(raw, raw.Length);
            // R45 (Anargon: "It should also revert mods made BY this patcher"): a build of this tool is set back to the
            // stock firmware it was made from, with the undo record it carries or from the user's own stock copy
            OwnRevertResult own = null;
            if (OwnRevert.IsOwnBuild(raw, raw.Length))
            {
                own = OwnRevert.Revert(raw, raw.Length, path);
                if (own.stock == null) throw new Stop(path + ": " + own.why);
                raw = own.stock; bstr = ReadBuildString(raw, raw.Length);
            }
            // R42: erased end = damaged; changes another mod made at known places are undone (the result must be
            // exactly the verified stock build); a verified build changed elsewhere in a known part is refused
            FwCheck ck = Repair.Examine(raw, raw.Length);
            if (ck.Damaged()) throw new Stop(path + ": " + Repair.DAMAGED);
            info.undone = null; info.undoneHow = null;
            if (ck.Edited() && ck.pagesOther == 0 && ck.tailOther == 0 && (ck.stock != null || ck.truncated))
            {
                byte[] u = ck.stock != null ? ck.stock : new byte[raw.Length];
                if (ck.stock == null) P.Copy(ck.work, u, raw.Length);   // truncated: the undone bytes, still truncated
                raw = u; info.undone = Repair.UndoneText(ck); info.undoneHow = Repair.UndoneDetail(ck);
            }
            // R43 review: a code page changed in an unknown way is refused too, with or without known edits (R42 let it
            // through silently when there were none; R41 refused it, as "damaged")
            else if (ck.tailOther > 0 || ck.pagesOther > 0 || ck.Edited()) throw new Stop(path + ": " + Repair.ChangedText(ck));
            info.reverted = own != null;
            if (own != null) { info.undone = OwnRevert.Line(own, bstr); info.undoneHow = OwnRevert.Detail(own, bstr); }
            int fend = Layout.FindEnd(raw, 0, raw.Length);
            KnownBuild kb = null;
            for (int i = 0; i < Layout.KNOWN.Length; i++) if (P.Eq(bstr, Layout.KNOWN[i].build)) kb = Layout.KNOWN[i];
            bool endKnown = fend > 0;
            if (fend < 0 && kb != null) fend = kb.end;
            if (fend < 0) { fend = BASE + raw.Length; while (fend > BASE && (raw[fend - BASE - 1] & 0xFF) == 0xFF) fend--; fend = (fend + 3) & ~3; }
            int n = raw.Length < fend - BASE ? raw.Length : fend - BASE;
            byte[] app = new byte[n];
            P.Copy(raw, app, n);
            info.path = path; info.size = n; info.end = BASE + n; info.fwEnd = fend;
            // truncated: shorter than its own end, or no scatter-load table in it at all (it stops before the table)
            info.truncated = BASE + n < fend || !endKnown;
            info.build = ReadBuildString(app, n); info.crc = Txt.Crc32(app, 0, n);
            info.stock = kb != null && n == kb.end - BASE && info.crc == kb.crc;
            info.layout = Fw.Use(app, info.build);
            byte[] full = orig != null ? orig : raw;
            inputHasBoot = false; stockBoot = null; counterFound = false;
            if (full.Length >= 0x10000 && (Machine.LE(full, 0, 4) & 0x2FFE0000) == 0x20000000 && (Machine.LE(full, 0x3800, 4) & 0x2FFE0000) == 0x20000000)
            {
                info.boot = new byte[0x3800]; P.Copy(full, info.boot, 0x3800);
                inputHasBoot = true; stockBoot = info.boot;
                // R45 (Anargon): a backup of a tablet that runs the Bootloader Counter. Its start-up pages are stock but
                // for two words (the hook in set_boot_state and the reset boot-jump), so the stock pages are rebuilt
                // here: a counter build, its removal and return-to-stock then need no older stock backup.
                counterFound = CounterIn(info.boot);
                if (counterFound) StockWords(info.boot);
            }
            info.flashes = full.Length >= 0x10000 ? ReadFlashCount(full) : -1;
            info.modded = (info.build != null && P.StartsWith(info.build, VERSION)) || (n > 0x190 && (app[0x18C] & 0xFF) != 0x30);
            stockApp = app; stockDelay = null;                // cursor-delay baseline = this input firmware
            return app;
        }

        // R45: this tool's Bootloader Counter in a bootloader area (0x08000000-0x080037FF): the set_boot_state hook
        // points at the routine page 0x08002800, which is not erased. The reset boot-jump is then stock (rev 31) or
        // redirected into that page (rev 32 on).
        public static bool CounterIn(byte[] boot)
        {
            if (boot == null || boot.Length < 0x2C00) return false;
            if (Machine.LE(boot, Sched.BL_ERASE - 0x08000000, 4) != Sched.BlPatchWord()) return false;
            return Machine.LE(boot, Sched.BL_WRAP - 0x08000000, 4) != -1;
        }
        // the two counter words of a bootloader area back to their stock values (the rest of both pages is stock)
        static void StockWords(byte[] boot)
        {
            Put32(boot, Sched.BL_ERASE - 0x08000000, Sched.BL_STOCK_WORD);
            int lit = Machine.LE(boot, Sched.BL_BOOT_LIT - 0x08000000, 4);
            if ((lit & 1) == 1 && lit - 1 >= Sched.BL_WRAP && lit - 1 < Sched.BL_WRAP + 0x400) Put32(boot, Sched.BL_BOOT_LIT - 0x08000000, Sched.BL_BOOT_WORD);
        }
        static void Put32(byte[] b, int at, int v)
        {
            b[at] = (byte)(v & 0xFF); b[at + 1] = (byte)((v >> 8) & 0xFF); b[at + 2] = (byte)((v >> 16) & 0xFF); b[at + 3] = (byte)((v >> 24) & 0xFF);
        }
        // R45 (Anargon): with the counter found, Step 6 starts with Bootloader Changes and Bootloader Counter on
        public static void CounterDefaults(bool[] st)
        {
            if (!counterFound) return;
            st[SetIndex("bootloader")] = true; st[SetIndex("blcounter")] = true;
        }
        // R45: this build removes the installed Bootloader Counter: asked for (the wizard's toggle off, or
        // --no-bootloader-counter), found in the input, Bootloader Changes on, and a normal build of a paced preset.
        // The firmware's own counter goes on counting in page 0x08002C00.
        public static bool RemovesCounter(Level lv, bool[] st, Options o)
        {
            if (o == null || !o.blRemove || !counterFound || o.telemetry || o.apipTrain || o.cleanup || o.lab) return false;
            return lv.period > 0 && On(st, "bootloader") && !On(st, "blcounter");
        }

        public static bool Verified(string build) { for (int i = 0; i < Layout.KNOWN.Length; i++) if (P.Eq(build, Layout.KNOWN[i].build)) return true; return false; }
        public static string LayoutNote(InputInfo info)
        {
            return "This tool found " + F.Str(info.layout.Resolved()) + " of the " + F.Str(Layout.Count()) + " firmware locations it uses in this build by their code, not by fixed addresses. "
                + "Anything it could not find is reported by the compatibility check and left out. If too much is missing, flash a verified stock build (" + VerifiedList() + ") and build from that.";
        }
        public static string VerifiedList() { string s = ""; for (int i = 0; i < Layout.KNOWN.Length; i++) s += (i > 0 ? " or " : "") + P.Sub(Layout.KNOWN[i].build, 0, 10); return s; }
        public static void LogLayout(InputInfo info)
        {
            Layout l = info.layout;
            LOG.Main("   firmware layout: " + l.Summary());
            if (l.Resolved() < Layout.Count()) LOG.Main("   not found: " + l.Unresolved());
            if (l.mismatch != null) LOG.Main("   WARNING: " + l.mismatch + " (this is not the untouched build its version string names; those locations are not used)");
        }

        // the wizard shows input warnings as persistent boxes instead
        public static void CheckInputLogOnly(InputInfo info)
        {
            LOG.Main("Input file: " + info.path);
            LOG.Main("   application " + F.Hx(BASE, 8) + ".." + F.Hx(info.end, 8) + " (" + F.Str(info.size) + " bytes)");
            LOG.Main(info.build != null ? "   firmware build string: '" + info.build + "'" : "   firmware build string: not found");
            LOG.Main(info.stock ? "   identical to the verified stock image of this build" : "   not identical to a verified stock image (CRC-32 " + F.HexDigits(info.crc, 8, true) + ")");
            LogUndone(info);
            LogLayout(info);
        }

        // R45: the other mod's changes, one log line each
        static void LogUndone(InputInfo info)
        {
            if (info.undoneHow != null) { for (int i = 0; i < info.undoneHow.Count(); i++) LOG.Main("   " + info.undoneHow.Get(i)); }
            else if (info.undone != null) LOG.Main("   " + info.undone);
        }

        public static bool CheckInput(InputInfo info)
        {
            LOG.Main("Input file: " + info.path);
            LOG.Main("   application " + F.Hx(BASE, 8) + ".." + F.Hx(info.end, 8) + " (" + F.Str(info.size) + " bytes; this firmware's image ends at " + F.Hx(info.fwEnd, 8) + ")");
            LOG.Main(info.build != null ? "   firmware build string: '" + info.build + "'" : "   firmware build string: not found");
            LOG.Main(info.stock ? "   identical to the verified " + info.build + " stock image (CRC-32 " + F.HexDigits(info.crc, 8, true) + ")"
                : "   not identical to a verified stock image (CRC-32 " + F.HexDigits(info.crc, 8, true) + ")");
            LogUndone(info);
            LogLayout(info);
            StrList lines = new StrList();
            if (info.undoneHow != null)
            {
                // R45: what the other mod changed, and how (Anargon)
                for (int k = 0; k < info.undoneHow.Count(); k++)
                {
                    string t = info.undoneHow.Get(k);
                    bool item = P.StartsWith(t, "  - ");
                    StrList w = Txt.Wrap(item ? P.Sub(t, 4, P.Len(t) - 4) : t, item ? 96 : 100);
                    for (int i = 0; i < w.Count(); i++) lines.Add((item ? (i == 0 ? "  - " : "    ") : "") + w.Get(i));
                }
                lines.Add("");
            }
            else if (info.undone != null) { StrList w = Txt.Wrap(info.undone, 100); for (int i = 0; i < w.Count(); i++) lines.Add(w.Get(i)); lines.Add(""); }
            if (info.truncated) { lines.Add(TRUNC_WARN); lines.Add(""); StrList w = Txt.Wrap(TRUNC_HELP, 100); for (int i = 0; i < w.Count(); i++) lines.Add(w.Get(i)); }
            if (!Verified(info.build))
            {
                if (lines.Count() > 0) lines.Add("");
                lines.Add(BUILD_WARN);
                lines.Add(BuildWarn2());
                StrList w = Txt.Wrap(LayoutNote(info), 100); for (int i = 0; i < w.Count(); i++) lines.Add(w.Get(i));
            }
            if (lines.Count() > 0) { P.Line(""); for (int i = 0; i < lines.Count(); i++) Tell(lines.Get(i)); P.Line(""); }
            return info.truncated;
        }

        // reading length relative to FAST's (10 samples x 20 pulses): the receiver integrates, so a shorter reading
        // reads proportionally smaller and needs proportionally lower thresholds (FAST and longer keep the stock ones)
        public static double Weight(int samples, int burst) { double k = samples * burst / 200.0; return k < 1 ? k : 1; }
        // R45-rc4 dynamic Steady Hover (Anargon, 2026-11-13: "a dynamic speed limiter with noise targets matching the
        // absolute max of low hover before decreasing the RPS dynamically while maintaining the same noise (BEFORE
        // algorithmic correction, which zeroes it out nearly)", at 1000, 800 and 500 alike; then, after the first rc4
        // build: "1000hz is WAY too low; it should test the upper extremes. The report rate should also be ridiculously
        // dynamic, as in it holds 1000hz. If the tablet thinks the noise level now exceeds low hover as per past scans
        // (basically 7mm), then it slowly starts decreasing the hz until it matches noise wise with 7mm 1000hz, so it
        // doesnt just suddenly drop to 500, or 333").
        // The fast stage keeps the preset's rate down to LAD_MARGIN_FAST x its own drop level: the top of its range
        // (on the tablet ULTRA's fast stage kept the pen down to level 76 = 1.12 x its drop). The noise target is
        // ULTRA's fast stage there (NoiseRef), raw positions before ALPI. Past it the rate falls in small steps: at
        // every LAD_PERIOD_STEP us of frame time (to LAD_PERIOD_MAX) the strongest read that fits the frame (at most
        // LAD_MAX_SAMPLES samples, the stock read's, and Steady Hover's pulses) becomes a step when it reads at least
        // LAD_STEP x the last step's level (Gain), and each step is kept down to the level where its noise reaches the
        // target (NoiseLevel). So the noise stays at the target while the rate falls only as far as the pen's height
        // needs. Noise model (tablet, rc4 hover and lift recordings, frame to frame second difference on the worse
        // axis): k / (level - floor) mm, k about 2.05 x sqrt(samples) (fast 4.1, Steady Hover 8.9), floor = the level
        // of a read with no pen (LevelFloor: fast 54, Steady Hover 68). The longer reads hold the pen well past the
        // target (Steady Hover: 0.05-0.013 mm at levels 250-1050), so a step's drop levels (its read's and its
        // frequency sweep's) are lowered to what they read at its step-down level / LAD_MARGIN_FAST, never under
        // their floor + LAD_DROP_FLOOR; the last step, with no slower one to go to, keeps the pen down to the reach
        // stage's share of its drop level (REACH_HOLD). The reach stage (24 x 254 at 60/s) is the last step only when
        // it reads at least LAD_STEP x the slowest Steady Hover step (by Gain it reads less than any step past 30
        // samples x 40 pulses; the first rc4 build went from 333/s straight to it). Each frame the firmware takes the
        // fastest step whose level, predicted from this frame's, meets that step's level x LAD_MARGIN for a few frames
        // in a row, and one step slower when the running one has been under its own for a few frames in a row (Sched,
        // "m_air"); between Steady Hover steps LAD_DOWN frames (2: a step down costs a few percent of the rate, and a
        // lifted pen has many steps to go through). The first rc4 build (fast, 500/s 20x40, 333/s 32x40, reach) left
        // 1000/s below level 82 and halved the rate in one step. Before rc4 the fast stage left at level 516 (24
        // frames), Steady Hover came back from a level the fast stage could not keep (24 fast + 3 Steady Hover frames,
        // about 37 times a second at low hover), and probe frames tried the faster stage every 16 frames (with a still
        // pen 29 times a second on Anargon's tablet, each 0.36 mm off).
        public const int LAD_MAX_SAMPLES = 45, LAD_DOWN_FAST = 8, LAD_DOWN = 2, LAD_UP = 4, LAD_UP_REACH = 2;
        public const int LAD_PERIOD_STEP = 25, LAD_PERIOD_MAX = 4000, LAD_DROP_FLOOR = 10;
        public const double LAD_MARGIN = 1.25, LAD_MARGIN_FAST = 1.12, LAD_STEP = 1.2;
        // the frame periods a ladder step may take (us), fastest first: every LAD_PERIOD_STEP us after the preset's own
        // period P0, to LAD_PERIOD_MAX (the presets with Steady Hover only)
        public static int[] RungPeriods(string key, int p0)
        {
            if (!P.Eq(key, "1000") && !P.Eq(key, "800") && !P.Eq(key, "500")) return null;
            int n = (LAD_PERIOD_MAX - p0) / LAD_PERIOD_STEP;
            if (n < 0) n = 0;
            int[] r = new int[n];
            for (int i = 0; i < n; i++) r[i] = p0 + LAD_PERIOD_STEP * (i + 1);
            return r;
        }
        // the level of a read of SAMPLES samples after BURST pulses, relative, fitted to Anargon's tablet: Steady Hover
        // (20 x 40) read 10.7-11.6 x the fast stage (4 x 16) where the stages switched (median of 637, 5004 and 3134
        // switches in three recordings), the reach stage (24 x 254) 1.43-1.47 x Steady Hover (15 and 3 switches); this
        // gives 10.9 and 1.52. The pen rings up as 1 - exp(-n pi / Q) of full after n pulses with Q about 75 there, so
        // past about 40 pulses a read gains little (the twin's pen, Q 300, keeps ringing up: reach 2.7 x Steady Hover).
        // Only three reads were fitted: the steps in between are model values until measured; leaving a step uses the
        // ratio measured as the pen came in (Sched).
        public static double Gain(int samples, int burst) { return Math.Pow(samples, 1.17) * (1 - Math.Exp(-burst * Math.PI / 75.0)); }
        static int CeilI(double v) { int i = (int)v; return i < v ? i + 1 : i; }
        // the level of a read of SAMPLES samples with no pen (fitted on the tablet: 77.3 per conversion in 12-bit
        // readings, 72 in rounded ones; the first conversion adds nothing)
        public static double LevelFloor(int samples, int bias) { return (bias > 0 ? 77.3 : 72.0) * (samples - 1) / samples - 4; }
        // the level at which a read of SAMPLES samples is as noisy as one of S0 samples at level T0 (noise model above)
        public static int NoiseLevel(int samples, int s0, int t0, int bias)
        {
            return CeilI(LevelFloor(samples, bias) + (t0 - LevelFloor(s0, bias)) * Math.Sqrt((double)samples / s0));
        }
        // the level a read of SQ samples (Gain GQ) reads where one of SP samples (Gain GP) reads L: the floors stay,
        // the pen's part scales with Gain
        static double Pred(double l, int sp, double gp, int sq, double gq, int bias)
        {
            return LevelFloor(sq, bias) + (l - LevelFloor(sp, bias)) * gq / gp;
        }
        // the noise target: ULTRA's fast stage at LAD_MARGIN_FAST x its drop level
        public static int NoiseRef(double dscale, int bias)
        {
            Level u = Lv("1000");
            return CeilI(LAD_MARGIN_FAST * (Drop(u.samples, u.burst, dscale) + bias));
        }
        // a Steady Hover step's drop level D (its read of SAMPLES): at most what it reads at its step-down level T /
        // LAD_MARGIN_FAST, at least its floor + LAD_DROP_FLOOR (and never raised)
        static int LowerDrop(int d, double t, int samples, int bias)
        {
            int dmax = (int)(t / LAD_MARGIN_FAST), lo = CeilI(LevelFloor(samples, bias)) + LAD_DROP_FLOOR;
            if (dmax < lo) dmax = lo;
            return d > dmax ? dmax : d;
        }
        // a Steady Hover step's frequency sweep: Steady Hover's pulses and the step's samples, at least the fast stage's
        // sweep's. Twin, RAPID with short frequency readings, a Q 150 pen 3 kHz below where pressure starts, holds on:
        // steps sweeping with their own reads (18-23 pulses, 20-45 samples) now and then picked a drive step 2 above
        // the 40-pulse sweep's, which Frequency Hold takes at once, and every report after showed pressure.
        static int SweepSamples(int s, Stage f0) { return s > f0.swSamples ? s : f0.swSamples; }
        static int SweepBurst(int[] bst, Stage f0) { return bst[2] > f0.swBurst ? bst[2] : f0.swBurst; }
        // one Steady Hover step: the frame period PU, its read (BS x BB) and frequency sweep (WS x WB); t[0] = its
        // step-down level (the noise target for its read, at least LAD_MARGIN_FAST x its drop levels)
        static Stage LadStep(int pu, int bs, int bb, int ws, int wb, bool sof, double dscale, int bias, int us, int tRef, int[] t)
        {
            double g = Gain(bs, bb), gw = Gain(ws, wb);
            Stage st = MakeStage(pu, bs, bb, ws, wb, sof && pu % 1000 == 0, dscale);
            st.pick += bias; st.drop += bias; st.swPick += bias; st.swDrop += bias;
            int v = NoiseLevel(bs, us, tRef, bias);
            st.drop = LowerDrop(st.drop, v, bs, bias);
            st.swDrop = LowerDrop(st.swDrop, Pred(v, bs, g, ws, gw, bias), ws, bias);
            int td = CeilI(LAD_MARGIN_FAST * st.drop), tsw = CeilI(Pred(LAD_MARGIN_FAST * st.swDrop, ws, gw, bs, g, bias));
            if (td > v) v = td;
            if (tsw > v) v = tsw;
            t[0] = v;
            return st;
        }
        public static void BuildLadder(Cfg cfg, Level lv, int poll, bool sm, bool ag, bool mu, int apk, bool sof, double dscale, int bias)
        {
            Stage f0 = cfg.modes[0];
            int[] per = RungPeriods(lv.key, f0.periodUs), bst = BoostOf(lv.key);
            bool rch = cfg.nModes > 2;
            int cap = Sched.LAD_MAX - 1, us = Lv("1000").samples, tRef = NoiseRef(dscale, bias);
            Stage[] lad = new Stage[Sched.LAD_MAX];
            int[] tl = new int[Sched.LAD_MAX];
            int n = 0;
            lad[n] = f0; tl[n] = CeilI(LAD_MARGIN_FAST * f0.drop); n++;
            double gPrev = Gain(f0.samples, f0.burst);
            for (int k = 0; k < per.Length && n < cap; k++)
            {
                int pu = per[k], bs = -1, bb = -1, ws = -1, wb = -1;
                for (int b = bst[2]; b >= f0.burst; b--)          // the strongest read that fits, at most Steady Hover's pulses
                    for (int s = LAD_MAX_SAMPLES; s >= f0.samples; s--)
                    {
                        int s2 = SweepSamples(s, f0), b2 = SweepBurst(bst, f0);
                        if (Validate(s, b, poll, pu, lv, s2, b2, sm, ag, mu, apk).Count() != 0) continue;
                        if (bs < 0 || Gain(s, b) > Gain(bs, bb)) { bs = s; bb = b; ws = s2; wb = b2; }
                        break;
                    }
                if (bs < 0) continue;
                double g = Gain(bs, bb);
                if (g < gPrev * LAD_STEP) continue;
                int[] t1 = new int[1];
                lad[n] = LadStep(pu, bs, bb, ws, wb, sof, dscale, bias, us, tRef, t1); tl[n] = t1[0]; n++; gPrev = g;
            }
            // the slowest step reads with Steady Hover's pulses: a found pen starts there (Cfg.ladAcq), and a first
            // frequency sweep with fewer reads the pen's frequency off. Twin, RAPID, a Q 150 pen 3 kHz below where
            // pressure starts, Frequency Hold and Coil Hold on, short frequency readings: started at 625/s 12 x 30 (the
            // first rc4 build) or at 434/s 45 x 33 (the slowest step read for strength only) the first sweep picked a
            // drive step 1-2 higher and every report showed pressure; from 312/s 32 x 40 none, as rc3. So it moves to the
            // first period where a read of those pulses is at least as strong.
            if (n > 1 && lad[n - 1].burst < bst[2])
            {
                Stage sl = lad[n - 1];
                double gl = Gain(sl.samples, sl.burst);
                for (int k = 0; k < per.Length; k++)
                {
                    if (per[k] < sl.periodUs) continue;
                    int s = LAD_MAX_SAMPLES;
                    while (s >= f0.samples && Validate(s, bst[2], poll, per[k], lv, SweepSamples(s, f0), SweepBurst(bst, f0), sm, ag, mu, apk).Count() != 0) s--;
                    if (s < f0.samples || Gain(s, bst[2]) < gl) continue;
                    int[] t1 = new int[1];
                    lad[n - 1] = LadStep(per[k], s, bst[2], SweepSamples(s, f0), SweepBurst(bst, f0), sof, dscale, bias, us, tRef, t1); tl[n - 1] = t1[0];
                    gPrev = Gain(s, bst[2]);
                    break;
                }
            }
            Stage rs = rch ? cfg.modes[2] : null;
            bool inLad = rs != null && Gain(rs.samples, rs.burst) >= LAD_STEP * gPrev;
            if (inLad) { lad[n] = rs; tl[n] = 0; n++; }
            else if (n > 1)
            {
                // the slowest step keeps the pen as far as the reach stage would (its drop levels x REACH_HOLD)
                Stage sl = lad[n - 1];
                int d1 = Drop(sl.samples, sl.burst, dscale * REACH_HOLD) + bias, d2 = Drop(sl.swSamples, sl.swBurst, dscale * REACH_HOLD) + bias;
                int f1 = CeilI(LevelFloor(sl.samples, bias)) + LAD_DROP_FLOOR, f2 = CeilI(LevelFloor(sl.swSamples, bias)) + LAD_DROP_FLOOR;
                if (d1 < sl.drop) sl.drop = d1 > f1 ? d1 : f1;
                if (d2 < sl.swDrop) sl.swDrop = d2 > f2 ? d2 : f2;
            }
            // a step leaves for the next only while the next one still reads LAD_MARGIN_FAST x its drop levels
            for (int p = n - 2; p >= 0; p--)
            {
                Stage nx = lad[p + 1];
                double gp = Gain(lad[p].samples, lad[p].burst), gn = Gain(nx.samples, nx.burst), gw = Gain(nx.swSamples, nx.swBurst);
                int v1 = CeilI(Pred(LAD_MARGIN_FAST * nx.drop, nx.samples, gn, lad[p].samples, gp, bias));
                int v2 = CeilI(Pred(LAD_MARGIN_FAST * nx.swDrop, nx.swSamples, gw, lad[p].samples, gp, bias));
                if (v1 > tl[p]) tl[p] = v1;
                if (v2 > tl[p]) tl[p] = v2;
            }
            rch = inLad;
            cfg.lad = new Stage[n]; cfg.nLad = n;
            for (int p = 0; p < n; p++) cfg.lad[p] = lad[p];
            // a found pen (800 / 1000) starts at the first step of Steady Hover's period and pulses (see above)
            int last = n - 1 - (rch ? 1 : 0);
            cfg.ladAcq = last >= 1 ? last : 1;
            for (int p = last; p >= 1; p--) if (lad[p].periodUs >= bst[0] && lad[p].burst >= bst[2]) cfg.ladAcq = p;
            cfg.ladStage = new int[n]; cfg.ladT = new int[n]; cfg.ladDown = new int[n]; cfg.ladUp = new int[n]; cfg.ladU = new int[n, n];
            cfg.ladTimer = new bool[n];
            for (int p = 0; p < n; p++)
            {
                cfg.ladStage[p] = p == 0 ? 0 : (rch && p == n - 1 ? 2 : 1);
                cfg.ladT[p] = tl[p] > 65535 ? 65535 : tl[p];
                cfg.ladDown[p] = p == 0 ? LAD_DOWN_FAST : LAD_DOWN;
                cfg.ladUp[p] = cfg.ladStage[p] == 2 ? LAD_UP_REACH : LAD_UP;
                cfg.ladTimer[p] = !(sof && lad[p].periodUs % 1000 == 0);
                for (int j = 0; j < p; j++)
                {
                    double v = Pred(LAD_MARGIN * cfg.ladT[j], lad[j].samples, Gain(lad[j].samples, lad[j].burst), lad[p].samples, Gain(lad[p].samples, lad[p].burst), bias);
                    cfg.ladU[p, j] = v > 65535 ? 65535 : CeilI(v);
                }
            }
        }
        public static string LadderText(Cfg cfg)
        {
            StringBuilder b = new StringBuilder();
            for (int p = 0; p < cfg.nLad; p++)
            {
                Stage m = cfg.lad[p];
                if (p > 0) b.Append("; ");
                b.Append((cfg.ladStage[p] == 0 ? "fast " : cfg.ladStage[p] == 2 ? "reach " : "") + F.Str((int)P.Rint(1e6 / m.periodUs)) + "/s " + F.Str(m.samples) + "x" + F.Str(m.burst)
                    + (m.swSamples != m.samples || m.swBurst != m.burst ? " (sweep " + F.Str(m.swSamples) + "x" + F.Str(m.swBurst) + ")" : "") + " drop " + F.Str(m.drop));
                if (p < cfg.nLad - 1) b.Append(", down below " + F.Str(cfg.ladT[p]));
                if (p > 0) b.Append(", up from " + F.Str(cfg.ladU[p, p - 1]));
            }
            return b.ToString();
        }
        // R45-rc3: the level a slower hover stage (STAGE pulses) reads where the fast stage (FAST pulses) reads ENTER,
        // plus 25%. Its level is per sample, but more pulses ring the pen up further: 1 - exp(-n pi / Q) of full, Q 300
        // (the twin's pen; on Anargon's tablet Steady Hover's 40 pulses read 2.3 x the fast stage's 16, the model 2.2).
        // Before rc3 a slower stage was compared with ENTER itself, so a pen too weak for the fast stage read "strong"
        // there and went straight back: at low hover 24 fast frames and 3 Steady Hover frames, about 37 times a second
        // (each switch moved the cursor 0.1-0.2 mm), and from reach at about 16 mm, where the fast stage lost the pen.
        public static int StepUpLevel(int enter, int fast, int stage)
        {
            double q = 300.0, rf = 1 - Math.Exp(-fast * Math.PI / q), rs = 1 - Math.Exp(-stage * Math.PI / q);
            double v = enter * rs / rf * 1.25;
            return v > 65535 ? 65535 : (int)v;
        }
        public static int Pick(int s, int b, double scale) { int v = (int)P.Rint(300 * Weight(s, b) * scale); return v > 64 ? v : 64; }
        public static int Drop(int s, int b, double scale) { int v = (int)P.Rint(200 * Weight(s, b) * scale); return v > 40 ? v : 40; }

        static Stage MakeStage(int periodUs, int samples, int burst, int swS, int swB, bool sof, double scale)
        {
            Stage s = new Stage();
            s.periodUs = periodUs; s.period = sof ? periodUs / 1000 : periodUs; s.samples = samples; s.burst = burst; s.tick = TickFor(burst, samples);
            s.swSamples = swS; s.swBurst = swB; s.swTick = TickFor(swB, swS);
            s.pick = Pick(samples, burst, scale); s.drop = Drop(samples, burst, scale); s.swPick = Pick(swS, swB, scale); s.swDrop = Drop(swS, swB, scale);
            return s;
        }

        // Signal-threshold calibration measures the preset's default fast stage at the stock frequency.
        public static string CalibrationProfileError(bool[] st, Options o)
        {
            if (o.samples >= 0 || o.burst >= 0 || o.tick >= 0 || o.poll >= 0 || o.calSpan >= 0 || o.detectScale >= 0)
                return "Signal-threshold calibration needs the preset's default scan timing and gain; remove expert timing, gain and detection overrides.";
            if (st[SetIndex("cal")]) return "Signal-threshold calibration uses the stock frequency; turn experimental Pen frequency tuning off.";
            return null;
        }

        // ---- one preset -> one complete image
        public static Image BuildOne(byte[] app, InputInfo info, string outpath, Level lv, bool[] stIn, bool binterval, bool alsoBin, Options o)
        {
            bool[] on = new bool[SETTINGS.Length];
            for (int i = 0; i < SETTINGS.Length; i++) on[i] = stIn[i] && Unavailable(SETTINGS[i].key, lv, stIn) == null;
            if (!o.telemetry && (o.calPick >= 0 || o.calDrop >= 0))
            {
                string profileError = CalibrationProfileError(stIn, o);
                if (profileError != null) throw new Stop(profileError);
                if (lv.period == 0 || o.calPick <= o.calDrop || o.calDrop <= 0 || o.calPick > 65535)
                    throw new Stop("Invalid tablet signal thresholds: use a measured pick-up threshold above the drop threshold on one non-STOCK preset.");
            }
            // R45: no measured thresholds: Anargon's own where Anargon measured this preset (FB_KEY), else the preset's
            int fb = FallbackFor(lv, stIn, o), cPick = o.calPick, cDrop = o.calDrop;
            if (fb >= 0) { cPick = FB_PICK[fb]; cDrop = FB_DROP[fb]; }
            Level preset = lv;
            lv = Effective(lv, on, o);                       // R41: Accurate Pressure Readings changes the sweep's steps
            int period = lv.period;
            int samples = o.samples >= 0 ? o.samples : lv.samples, burst = o.burst >= 0 ? o.burst : lv.burst;
            int poll = o.poll >= 0 ? o.poll : (period > 0 ? 10 : 50);
            bool sweepOwn = lv.swSamples > 0 && o.samples < 0 && o.burst < 0;
            int swS = sweepOwn ? lv.swSamples : samples, swB = sweepOwn ? lv.swBurst : burst;
            bool sm = On(on, "smoothing"), ag = On(on, "interp") && On(on, "interp2"), mu = On(on, "multicoil"), fr = On(on, "fullres");
            int apk = ApipKind(on);
            bool ap = apk > 0 && period > 0 && !o.apipTrain;
            if (!ap) apk = 0;
            ApipModel apm = null;
            if (ap)
            {
                // R44: a real build needs this tablet's training for this preset; builds without an output (the Settings
                // check, measurements) use a neutral model, which costs the same frame time
                if (o.apip == null && outpath != null) throw new Stop("ALgorithmic pen Position Interpolation (ALPI) is on but not trained for " + lv.label + ". Train it (the wizard's ALPI training step, or --apip FILE) or turn it off.");
                apm = o.apip != null ? o.apip : Apip.Neutral(lv, fr);
                string mm = o.apip != null ? Apip.Mismatch(o.apip, lv, fr) : null;
                if (mm != null) throw new Stop("This ALPI training does not match the build (" + mm + "). Train again for this preset.");
            }
            int bias = fr ? FULLRES_BIAS : 0;
            StrList err = period > 0 ? Validate(samples, burst, poll, period, lv, swS, swB, sm, ag, mu, apk) : Validate(samples, burst, poll, 0, null, 0, 0, false, false, false);
            int trimS = -1, trimB = -1;
            while (period > 0 && Has(err, "frame budget") && o.samples < 0 && o.burst < 0)
            {
                if (samples > MIN_SAMPLES) samples--; else if (burst > MIN_BURST) burst--; else break;
                if (lv.swSamples > 0) { swS = swS > samples ? swS : samples; swB = swB > burst ? swB : burst; } else { swS = samples; swB = burst; }
                err = Validate(samples, burst, poll, period, lv, swS, swB, sm, ag, mu, apk);
                trimS = samples; trimB = burst;
            }
            int tick = o.tick >= 0 ? o.tick : (period > 0 ? TickFor(burst, samples) : 200);
            bool sof = period > 0 && period % 1000 == 0;
            Cfg cfg = new Cfg();
            if (o.lab && period <= 0) throw new Stop("the Filter Arranger build (--lab) needs a preset other than STOCK");
            int[] bst = BoostOf(lv.key);
            double dscale = o.detectScale > 0 ? o.detectScale : 1.0;
            if (period > 0)
            {
                cfg.modes[cfg.nModes++] = MakeStage(period, samples, burst, swS, swB, sof, dscale);
                if (On(on, "boost"))
                {
                    StrList e2 = Validate(bst[1], bst[2], poll, bst[0], lv, bst[1], bst[2], sm, ag, mu, apk);    // R43: Multi-coil Read runs in every stage (R44: APIP too)
                    for (int i = 0; i < e2.Count(); i++) err.Add("Steady Hover: " + e2.Get(i));
                    cfg.modes[cfg.nModes++] = MakeStage(bst[0], bst[1], bst[2], bst[1], bst[2], sof, dscale);
                    if (On(on, "reach"))
                    {
                        Stage rs = MakeStage(sof ? 16000 : REACH_MODE[0], REACH_MODE[1], REACH_MODE[2], REACH_MODE[1], REACH_MODE[2], sof, dscale);
                        rs.drop = Drop(REACH_MODE[1], REACH_MODE[2], dscale * REACH_HOLD); rs.swDrop = rs.drop;   // R45-rc3
                        cfg.modes[cfg.nModes++] = rs;
                    }
                }
                cfg.modes[0].tick = tick;
                // R45: Anargon's fallback only where it fits this build's scan (else the preset's own, silently)
                if (fb >= 0 && (samples != lv.samples || burst != lv.burst || cPick > cfg.modes[0].pick || cDrop > cfg.modes[0].drop)) { fb = -1; cPick = o.calPick; cDrop = o.calDrop; }
                if (cPick > 0)
                {
                    if (!o.telemetry && (samples != lv.samples || burst != lv.burst))
                        throw new Stop("These settings changed the scan timing after Tablet signal thresholds. Use the preset's default timing or build without the saved calibration.");
                    // calibrated on this tablet: the sweep keeps the same ratio to the scan as the defaults have
                    Stage f = cfg.modes[0];
                    if (!o.telemetry && (cPick > f.pick || cDrop > f.drop))
                        throw new Stop("Tablet signal thresholds would reduce the preset's pickup range. Recalibrate with this version of the tool.");
                    double r = (double)Weight(f.swSamples, f.swBurst) / Weight(f.samples, f.burst);
                    f.pick = cPick; f.drop = cDrop;
                    f.swPick = (int)P.Rint(cPick * r); f.swDrop = (int)P.Rint(cDrop * r);
                }
                for (int i = 0; i < cfg.nModes; i++)                // 12-bit Readings: levels set in rounded readings
                {
                    Stage m = cfg.modes[i];
                    m.pick += bias; m.drop += bias; m.swPick += bias; m.swDrop += bias;
                }
            }
            int calSpan = o.calSpan >= 0 ? o.calSpan : 3;
            if (On(on, "cal") && !(calSpan >= 1 && calSpan <= 3)) err.Add("calibration span " + F.Str(calSpan) + ": must be 1..3 (its sums would overwrite the cold-boot marker)");
            if (err.Count() > 0 && !probing)  // compatibility overrides never bypass the hardware timing limits
            {
                StringBuilder sb = new StringBuilder("refusing to build:");
                for (int i = 0; i < err.Count(); i++) sb.Append("\n  - " + err.Get(i));
                throw new Stop(sb.ToString());
            }
            int n = LevelIndex(lv.key) + 1;
            Say("Building preset " + F.Str(n) + "/" + F.Str(LEVELS.Length) + ": " + lv.label);
            Say("");
            Say("    " + (period > 0 ? "One frame every " + F.Str(period) + " microseconds" : "Vendor timing, not paced: about one frame every 4500 microseconds"));
            Say("    Build details:      " + LOG.Shown(true));
            Say("    For bug reports:    " + LOG.Shown(false));
            LOG.Main(""); LOG.Main("== Preset " + F.Str(n) + "/" + F.Str(LEVELS.Length) + ": " + lv.label); LOG.Main("   output: " + outpath);
            LOG.Main("   settings: " + SettingsLine(stIn, lv));
            if (period > 0)
            {
                LOG.Main("   scan: one frame every " + F.Str(period) + " us (" + F.Fx(1e6 / period, 1) + "/s), " + F.Str(samples) + " ADC samples and " + F.Str(burst)
                    + " excitation pulses per step, step tick " + F.Str(tick) + " us, poll " + F.Str(poll) + " us");
                if (swS != samples || swB != burst)
                    LOG.Main("   frequency-sweep steps: " + F.Str(swS) + " samples, " + F.Str(swB) + " pulses (longer readings keep the pen frequency, and so the pressure, accurate)");
                if (lv != preset)
                    LOG.Main("   Accurate Pressure Readings: " + (lv.chunk <= 0 ? "the whole 6-step sweep in every frame" : F.Str(lv.chunk) + " sweep step" + (lv.chunk > 1 ? "s" : "") + " per frame")
                        + ", about " + F.Fx(SweepRate(lv), 0) + " pressure updates per second (" + F.Fx(SweepRate(preset), 0) + " with short readings)");
                // show that the whole frame fits, with the enabled filters charged (see FilterProcUs)
                double scanUs = FrameScanUs(lv, samples, burst, swS, swB, poll);
                int fproc = FilterProcUs(sm, ag, apk, period) + (mu ? MultiCoil.MC_US : 0);
                double needUs = scanUs + PROC_US + fproc;
                StrList fl = new StrList();
                if (sm) fl.Add("smoothing");
                if (ag) fl.Add("aggressive interp");
                if (mu) fl.Add("multi-coil read");
                if (ap) fl.Add(apk >= 2 ? "ALPI robust " + F.Str(apk) : "ALPI");
                StringBuilder fs = new StringBuilder();
                for (int i = 0; i < fl.Count(); i++) { if (i > 0) fs.Append(", "); fs.Append(fl.Get(i)); }
                LOG.Main("   frame budget: " + F.Fx(scanUs, 0) + " us scan + " + F.Str(PROC_US) + " us processing"
                    + (fproc > 0 ? " + " + F.Str(fproc) + " us filters (" + fs.ToString() + ")" : "")
                    + " = " + F.Fx(needUs, 0) + " us of " + F.Str(period) + " us (" + F.Fx(period - needUs, 0) + " us to spare)");
            }
            if (trimS >= 0) LOG.Main("   smoothing on: scan trimmed to " + F.Str(trimS) + " samples / " + F.Str(trimB) + " pulses to fit the frame");
            if (o.telemetry) LOG.Main("   CALIBRATION PASS: every pen report carries the firmware's own readings (signal, position, frequency) for the calibration app");
            if (o.telemetry && o.lineRec) LOG.Main("   LINE RECORDER: each frame end copies all ten line sums and both positions; the next five reports carry them (pages 0xF0-0xF4)");
            if (o.calPick > 0) LOG.Main("   thresholds calibrated on this tablet (Tablet signal thresholds)");
            if (fb >= 0) LOG.Main("   thresholds: " + FB_WHO + " for " + P.Upper(preset.name) + " (fallback: pick up above " + F.Str(cPick) + ", drop below " + F.Str(cDrop) + ", in rounded readings)");
            if (ap) LOG.Main("   ALgorithmic pen Position Interpolation: " + (o.apip != null ? (apm.fallback ? ApipCli.FB_NAME + " (fallback, " + apm.trained + ", ULTRA" + (P.Eq(preset.key, Apip.FB_PRESET) ? "" : ", moved to this rate") + ")" : "trained " + apm.trained) + (apm.file != null ? " (" + apm.file + ")" : "") : "NEUTRAL model (no training: measurement build)")
                + "; replaces Position Interpolation, Movement Smoothing and Jump Rejection; per resonance band: coil-distance correction, then "
                + (apk >= 2 ? "the robust filter: " + F.Str(apk) + " trackers (each " + F.Fx(ApipRobust.RATIO, 0) + " x livelier), weighed by how well each predicts the readings; wild readings (" + F.Fx(ApipRobust.EPS * 100, 0) + " %, " + F.Fx(P.Sqrt(ApipRobust.WILD), 0) + " x the noise) barely move it"
                    : "an alpha-beta tracker with gates at " + F.Fx(Apip.GATE_LO, 1) + "-" + F.Fx(Apip.GATE_HI, 1) + " x the measured noise"));
            if (o.apipTrain && period > 0) LOG.Main("   ALPI TRAINING: every frame's position, signal level, pressure and hover stage are kept in RAM for USB string " + F.Str(Apip.STRING) + "; LED zone as the calibration pass");
            for (int i = 0; i < cfg.nModes; i++)
                LOG.Main("   " + (i == 0 ? "fast stage" : i == 1 ? "Steady Hover" : "reach stage") + " pen thresholds: pick up above " + F.Str(cfg.modes[i].pick) + ", drop below " + F.Str(cfg.modes[i].drop)
                    + " (sweep " + F.Str(cfg.modes[i].swPick) + "/" + F.Str(cfg.modes[i].swDrop) + "; stock 300/200 for 45-sample readings)");
            string[] stn = new string[] { "", "Steady Hover", "reach stage" };
            for (int i = 1; i < cfg.nModes; i++)
                LOG.Main("   " + stn[i] + ": one frame every " + F.Str(cfg.modes[i].periodUs) + " us with " + F.Str(cfg.modes[i].samples) + " samples and " + F.Str(cfg.modes[i].burst) + " pulses");
            int enter = (o.boostAt >= 0 ? o.boostAt : (int)P.Rint(BOOST_ENTER * Weight(samples, burst))) + bias;
            if (On(on, "boost"))
            {
                BuildLadder(cfg, lv, poll, sm, ag, mu, apk, sof, dscale, bias);
                LOG.Main("   Steady Hover (dynamic): the fast stage keeps the preset's rate down to " + F.Fx(LAD_MARGIN_FAST, 2) + " x its drop level; past it the rate falls in steps of "
                    + F.Str(LAD_PERIOD_STEP) + " us of frame time (to " + F.Str(LAD_PERIOD_MAX) + " us), each reading at least " + F.Fx(LAD_STEP, 1) + " x the last and kept until its noise reaches ULTRA's fast stage at level "
                    + F.Str(NoiseRef(dscale, bias)) + " (raw, before ALPI); steps: " + LadderText(cfg) + ". One step faster when its level, at the ratio of the two reads measured at the last step down (until then Gain's), meets its level x "
                    + F.Fx(LAD_MARGIN, 2) + " for " + F.Str(LAD_UP) + " frames (from reach " + F.Str(LAD_UP_REACH) + "); one step slower after " + F.Str(LAD_DOWN_FAST) + " fast / "
                    + F.Str(LAD_DOWN) + " Steady Hover frames below it; touching or the AGC delaying keeps the fast stage");
            }
            if (On(on, "longrange"))
                LOG.Main("   long-range pickup: after " + F.Str(Sched.LONG_AFTER) + " failed quick searches, searches use " + F.Str(Sched.LONG_SEARCH[1]) + " pulses and " + F.Str(Sched.LONG_SEARCH[0]) + " samples at the last tracked frequency -3..+3 (every 4th stays quick)");
            if (fr)
                LOG.Main("   12-bit readings: every coil reading keeps all 12 bits (stock clears the low 3); pick-up, drop and boost levels +"
                    + F.Str(FULLRES_BIAS) + " to match (the cleared bits are 3.5 on average)");
            if (mu)
                LOG.Main("   Multi-coil Read (experimental): each position is a fit of the five lines around the pen to the coils' measured response (R43), floor "
                    + F.Str(MultiCoil.FloorA(fr)) + " x reads - " + F.Str(-MultiCoil.FloorB(fr)) + ", effective range " + F.Str(MultiCoil.ROW) + "/16 of the measured one; it replaces the stock arithmetic, which still runs where the fit does not apply");
            if (On(on, "cal"))
                LOG.Main("   startup calibration: within 3 s of plugging in, one hard press (pressure >= 15500) starts it; it measures while the pen hovers or rests lightly (pressure < 500), ~4 s, LED blinking at 2.5 Hz; while paused by a press it scans at the best offset so far");
            // R45 review: the margin does not depend on the scan frequency, so Pen frequency tuning (which drops the
            // fallback pick-up levels) keeps the fallback margin
            int fbo = o.fallback && !o.telemetry && lv.period > 0 ? FallbackIndex(lv.key) : -1;
            double oddMm = o.oddMm >= 0 ? o.oddMm : fbo >= 0 && FB_ODD[fbo] > 0 ? FB_ODD[fbo] : 1.0;
            if (On(on, "odd"))
                LOG.Main("   jump rejection: momentum model - readings outside the pen's stop-to-coast corridor by more than the running noise + " + F.Gfmt(oddMm) + " mm are replaced by the predicted coast (at most 3 in a row, then let through)");
            Fw.Use(app, info.build);                    // this firmware's layout (every location the patches use)
            Image im = new Image(app);
            im.probe = probing;                         // when set, Expect records per-modification mismatches instead of throwing
            im.Begin("Hardware divide"); try { Patches.Udiv(im); } catch (Stop e) { im.Fail(e); }
            im.Begin("Movement Smoothing"); try { if (!sm) Patches.SmoothingOff(im); } catch (Stop e) { im.Fail(e); }
            im.Begin("Position Interpolation"); try { if (!On(on, "interp")) Patches.InterpOff(im); } catch (Stop e) { im.Fail(e); }
            cfg.aggressive = On(on, "interp") && On(on, "interp2") && period > 0;
            im.Begin("Report rate (scan timing)"); try { if (period > 0) Patches.Scan(im, samples, burst, tick, poll); } catch (Stop e) { im.Fail(e); }
            im.Begin("Gain-control (AGC) fix"); try { Patches.AgcCap(im, samples); } catch (Stop e) { im.Fail(e); }
            im.Begin("12-bit Readings"); try { if (fr) Patches.FullRes(im); } catch (Stop e) { im.Fail(e); }
            bool volat = VolatileOn(on, lv);
            im.Begin("1 ms USB report interval"); try { if (binterval && volat) Patches.Binterval(im); } catch (Stop e) { im.Fail(e); }
            im.Begin("Max-pressure fix"); try { Patches.PressureClamp(im); } catch (Stop e) { im.Fail(e); }
            im.Begin("Resonance band-edge hold"); try { Patches.BandEdge(im); } catch (Stop e) { im.Fail(e); }
            if (period > 0)
            {
                int per = 4000000 / 7 / period; if (per < 12) per = 12;
                cfg.boost = On(on, "boost"); cfg.reach = On(on, "boost") && On(on, "reach"); cfg.enter = enter;
                cfg.enterUp1 = StepUpLevel(enter, burst, cfg.nModes > 1 ? cfg.modes[1].burst : burst);
                cfg.enterUp2 = StepUpLevel(enter, burst, cfg.nModes > 2 ? cfg.modes[2].burst : cfg.nModes > 1 ? cfg.modes[1].burst : burst);
                cfg.holdHover = cfg.boost && period <= 1250;      // 800/1000 Hz: acquire into boost so a weak hover holds
                cfg.longSearch = On(on, "longrange"); cfg.multiCoil = mu; cfg.fullRes = fr; cfg.freqHold = On(on, "fhold"); cfg.systickOff = On(on, "systick"); cfg.tiltSkip = On(on, "tiltskip"); cfg.switchMatch = On(on, "smatch"); cfg.winScale = On(on, "wscale"); cfg.antiChatter = On(on, "dac"); cfg.coilLin = On(on, "clin"); cfg.coilGains = On(on, "cgain"); cfg.coilHold = On(on, "chold"); cfg.telemetry = o.telemetry; cfg.lineRec = o.telemetry && o.lineRec && lv.period > 0;
                cfg.apip = apm; cfg.apipTrain = o.apipTrain;
                cfg.apipK = apm != null && apk >= 2 ? ApipRobust.Constants(apk, lv) : null;
                cfg.counter = On(on, "bootloader") && !o.cleanup;
                cfg.blCounter = On(on, "blcounter") && !o.cleanup && !probing;   // counter code is ours (always compatible); its real need is a full dump
                bool removes = RemovesCounter(lv, on, o) && !probing;           // R45: removal in a normal build keeps the count page
                cfg.cleanup = (o.cleanup || removes) && !probing;
                cfg.keepCount = removes && !o.cleanup;
                if (cfg.blCounter || cfg.cleanup)
                {
                    string blErr = null;
                    if (stockBoot == null) blErr = "this needs a full 64 KB dump as input (ic902-main.bin or deco01v3-main.bin): the stock bootloader pages come from it";
                    else
                    {
                        cfg.stockPage = new byte[1024]; P.Copy(stockBoot, Sched.BL_PAGE - 0x08000000, cfg.stockPage, 0, 1024);
                        cfg.stock0Page = new byte[1024]; P.Copy(stockBoot, Sched.BL_PAGE0 - 0x08000000, cfg.stock0Page, 0, 1024);
                        if (Machine.LE(cfg.stockPage, Sched.BL_ERASE - Sched.BL_PAGE, 4) != Sched.BL_STOCK_WORD) blErr = "this bootloader is not the expected version: its set_boot_state routine is not at 0x08000F40";
                        else if (Machine.LE(cfg.stock0Page, Sched.BL_BOOT_LIT - Sched.BL_PAGE0, 4) != Sched.BL_BOOT_WORD) blErr = "this bootloader is not the expected version: its reset boot-jump is not at 0x0800015C (needed for the reset-vector self-heal)";
                    }
                    // NEVER force a bootloader rewrite onto an unknown bootloader - that is the most dangerous possible
                    // write. If it is not exactly the expected bootloader, drop the counter (even under --force).
                    if (blErr != null)
                    {
                        if (!forcing) throw new Stop(blErr);
                        im.skipped.Add("FORCED: Bootloader Counter / self-heal NOT installed (" + blErr + ")");
                        cfg.blCounter = false; cfg.cleanup = false; cfg.stockPage = null; cfg.stock0Page = null;
                    }
                }
                string seed = P.Now("stamp") + "|" + F.Fx(P.NowSec(), 6) + "|" + lv.key + "|" + SettingsLine(stIn, lv) + "|" + F.Str(o.calPick) + (o.telemetry ? "|t" : "") + (o.apipTrain ? "|a" : "") + (apm != null ? "|" + apm.trained : "");
                byte[] sb = Encode(seed);
                int bid = (int)Txt.Crc32(sb, 0, sb.Length);
                if (bid == 0 || bid == -1) bid = 0x5A5A5A5A;
                cfg.buildId = bid;
                int mx = 0; for (int i = 0; i < cfg.nModes; i++) if (cfg.modes[i].periodUs > mx) mx = cfg.modes[i].periodUs;
                cfg.freeRunPolls = mx / 10 > 400 ? mx / 10 : 400;
                if (On(on, "cal"))
                {
                    CalCfg c = new CalCfg();
                    int sk = per / 10 > 2 ? per / 10 : 2;
                    c.span = calSpan; c.frames = per - sk; c.skip = sk; c.hold = 1; c.enterP = 15500; c.nearP = 500;
                    c.blink = 200; c.windowMs = 3000;     // blink: ms per LED half-period (time-based since rev 33)
                    cfg.cal = c;
                }
                if (On(on, "odd")) { OddCfg oc = new OddCfg(); oc.margin = (int)P.Rint(oddMm * 100); oc.maxRun = 3; cfg.odd = oc; }
                if (o.lab)
                {
                    // Filter Arranger: the chosen settings are the order it starts with; then every filter is built in
                    if (o.telemetry || o.apipTrain || o.cleanup) throw new Stop("the Filter Arranger build (--lab) is a normal build: not with calibration, ALPI training or return to stock");
                    cfg.lab = true; cfg.labDefault = Lab.DefaultOrder(cfg); cfg.labDefaultFlags = Lab.DefaultFlags(cfg);
                    Lab.Force(cfg, oddMm, Unavailable("wscale", lv, on) == null);
                    LOG.Main("   Filter Arranger (lab build, testing only): every position filter built in; order " + Lab.HexText(cfg.labDefault, 8) + " (" + LabOrderText(cfg.labDefault) + ") until the PC sets another over USB strings " + F.Str(Lab.STRING) + "-" + F.Str(Lab.STRING_LAST));
                }
                im.Begin("Frame scheduler and pen tracking"); try { Sched.Patch(im, lv, cfg); } catch (Stop e) { im.Fail(e); }
            }
            bool zled = cfg.telemetry || cfg.apipTrain;
            if (zled) { im.Begin("LED: calibration pass"); try { Patches.LedOn(im, cfg.ledTarget); } catch (Stop e) { im.Fail(e); } }
            im.Begin("Firmware version string"); try { ApplyBrand(im); } catch (Stop e) { im.Fail(e); }
            im.Begin("Flash-count readout (USB string 173)"); try { Patches.CounterString(im, zled && cfg.ledTarget >= 0, cfg.apipTrain, cfg.lab ? cfg : null); } catch (Stop e) { im.Fail(e); }
            // R45-rc3: patcher packages' modifications (Build.AddPatch), each in its own transaction
            if ((EXTRA.Length > 0 || optionKeys.Length > 0) && !ExtraApplies(o)) LOG.Main("   package modifications: left out of this build (measurement, training or return-to-stock)");
            buildingOn = on; buildingLv = lv;
            try
            {
                for (int i = 0; i < EXTRA.Length && ExtraApplies(o); i++)
                {
                    im.Begin("Package: " + EXTRA[i].Name()); try { EXTRA[i].Apply(im); } catch (Stop e) { im.Fail(e); }
                }
                // package options (Build.AddSetting): each one's patch, only when the option is on in this build
                for (int i = 0; i < optionKeys.Length && ExtraApplies(o); i++)
                {
                    if (!On(on, optionKeys[i])) continue;
                    im.Begin("Package option: " + optionPatches[i].Name()); try { optionPatches[i].Apply(im); } catch (Stop e) { im.Fail(e); }
                }
            }
            finally { buildingOn = null; buildingLv = null; }
            // R45: the undo record (atf_revert.cs): the stock value of every byte of the input this build changed, so a
            // later run can set this build back to stock; allocated after every change, before the options text
            im.Begin("Undo record"); try { byte[] rec = OwnRevert.Record(im); im.AllocData(rec, "undo record (" + F.Str(rec.Length) + " bytes): the stock value of every changed byte, to set this build back to stock"); } catch (Stop e) { im.Fail(e); }
            // R45 (Anargon): the build's options as plain text, the very last bytes of the image (allocated last)
            string optText = OptionsText(preset, on, o, cPick, cDrop, fb >= 0, apm);
            for (int i = 0; i < EXTRA.Length && ExtraApplies(o); i++) optText += " PACKAGE=" + Printable(EXTRA[i].Name());
            im.Begin("Build options text"); try { im.AllocData(Encode(optText + "\0"), "build options as text (nothing reads it; readable in a .bin or a dump): " + optText); } catch (Stop e) { im.Fail(e); }
            LOG.Main("   version string (locked): " + VERSION + "_" + P.Now("date"));
            LOG.Main("   changes:");
            for (int i = 0; i < im.log.Count(); i++) if (P.IndexOf(im.log.Get(i), "reflash counter") < 0) LOG.Main(im.log.Get(i));
            for (int i = 0; i < im.skipped.Count(); i++) LOG.Main("   SKIPPED " + im.skipped.Get(i));

            byte[] data = im.Bytes();
            string kind = info.truncated ? "INCOMPLETE image (the input file was truncated)" : "complete image";
            int end = BASE + data.Length;
            if (outpath != null)
            {
                WriteHex(data, BASE, outpath);
                if (alsoBin) P.WriteAllBytes(P.Sub(outpath, 0, P.Len(outpath) - 4) + ".bin", data);
            }
            Say("");
            Tell("    Wrote " + outpath + ":");
            Tell("        " + kind + " " + F.Hx(BASE, 7) + ".." + F.Hx(end, 7) + ", " + F.Str(data.Length) + " bytes, " + F.Str(FLASH_END - end) + " bytes of flash left");
            return im;
        }
    }
}

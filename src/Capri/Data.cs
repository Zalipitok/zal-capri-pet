using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Capri
{
    // tuning.json: numbers that set her feel. Field names match the file's keys.
    class Tuning
    {
        public double throwSpeed = 800, gravity = 2600, floorBounce = 0.38, wallBounce = 0.55;
        public double perchChance = 0.6, perchSink = 0, roamChance = 0.2, crossfadeSeconds = 0.12;
        public double mouthHalf = 0.25, mouthWide = 0.6, mouthDelay = 0.06, musicBob = 1.0, musicBpm = 108;
        public double songRemarkMinutes = 3, appRemarkMinutes = 2;
        public string hotkey = "Ctrl+Alt+C";
        public double perMonitorDpi = 0;
        public double ballBounce = 0.62, ballChance = 0.3, friendHue = 150, weatherMinutes = 60;
        public string chatModel = "claude-haiku-5-5";
        public double chatMaxTokens = 300;
        public string chatVoiceName = "", chatVoicePitch = "high", chatVoiceRate = "medium";
        public double powersChance = 0.04;
        public string updateRepo = "Zalipitok/zal-capri-pet";

        public string Load(string path)
        {
            if (!File.Exists(path)) return "";
            try
            {
                var saved = Json.LoadDict(path);
                foreach (var field in GetType().GetFields())
                {
                    var value = Json.Get(saved, field.Name);
                    if (value == null) continue;
                    if (field.FieldType == typeof(string)) field.SetValue(this, Json.Str(value));
                    else field.SetValue(this, Json.Num(value, (double)field.GetValue(this)));
                }
                return "";
            }
            catch (Exception e) { return e.Message; }
        }
    }

    // One state from behaviours.json.
    class Spec
    {
        public string id, title, menu, category = "antics", effect, target = "random", path, next, nextNoChase, setMood, motion, remark = "idle";
        public bool auto, rare, loop, clampTarget;
        public int? pose;
        public List<KeyValuePair<int, double>> timeline;
        public double cycle;
        public double secondsFixed = double.NaN; public int secondsMin = 5, secondsMax = 10;
        public double? speed;
        public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool Has(string tag) => tags.Contains(tag);

        public static Spec From(object entry)
        {
            var s = new Spec();
            s.id = Json.Str(Json.Get(entry, "id"));
            s.title = Json.Str(Json.Get(entry, "title")); s.menu = Json.Str(Json.Get(entry, "menu"));
            s.auto = Json.Bool(Json.Get(entry, "auto")) == true; s.rare = Json.Bool(Json.Get(entry, "rare")) == true;
            var cat = Json.Str(Json.Get(entry, "category")); if (cat != "") s.category = cat;
            if (Json.Get(entry, "pose") != null) s.pose = (int)Json.Num(Json.Get(entry, "pose"));
            var timeline = Json.Get(entry, "poses");
            if (timeline != null)
            {
                s.timeline = new List<KeyValuePair<int, double>>();
                foreach (var step in Json.List(timeline))
                {
                    var parts = Json.List(step);
                    double length = parts.Count > 1 ? Json.Num(parts[1]) : double.PositiveInfinity;
                    s.cycle += length;
                    s.timeline.Add(new KeyValuePair<int, double>((int)Json.Num(parts[0]), length));
                }
            }
            s.loop = Json.Bool(Json.Get(entry, "loop")) == true;
            var seconds = Json.Get(entry, "seconds");
            if (seconds == null) { s.secondsMin = 5; s.secondsMax = 10; }
            else if (Json.IsNumber(seconds)) s.secondsFixed = Json.Num(seconds);
            else { var range = Json.List(seconds); s.secondsMin = (int)Json.Num(range[0]); s.secondsMax = (int)Json.Num(range[1]); }
            s.effect = Json.Str(Json.Get(entry, "effect"));
            var target = Json.Str(Json.Get(entry, "target")); if (target != "") s.target = target;
            s.path = Json.Str(Json.Get(entry, "path"));
            if (Json.Get(entry, "speed") != null) s.speed = Json.Num(Json.Get(entry, "speed"));
            s.clampTarget = Json.Bool(Json.Get(entry, "clampTarget")) == true;
            s.next = Json.Str(Json.Get(entry, "next")); s.nextNoChase = Json.Str(Json.Get(entry, "nextNoChase")); s.setMood = Json.Str(Json.Get(entry, "setMood"));
            var motion = Json.Str(Json.Get(entry, "motion")); s.motion = motion != "" ? motion : s.id;
            var remark = Json.Str(Json.Get(entry, "remark")); if (remark != "") s.remark = remark;
            foreach (var tag in Json.List(Json.Get(entry, "tags"))) { var t = Json.Str(tag); if (t != "") s.tags.Add(t); }
            return s;
        }

        public int? PoseAt(double phase)
        {
            if (timeline == null) return pose;
            double p = phase;
            if (loop && cycle > 0 && !double.IsInfinity(cycle)) p = phase % cycle;
            foreach (var step in timeline) { if (p < step.Value) return step.Key; p -= step.Value; }
            return timeline[timeline.Count - 1].Key;
        }
    }

    partial class Pet
    {
        public Tuning tuning = new Tuning();
        public string tuningError = "";
        double friendHue = 150;
        public bool perMonitorDpi;

        // State
        public string state = "float"; public Spec spec;
        public double until, messageUntil, stateStart, anchorX, anchorY;
        public bool paused, dragging, chase = true;
        public double targetX, targetY, direction = 1.0, last;
        double nextBlink = 3, blinkUntil;
        double? cursorLastX, cursorLastY;
        public double caughtCooldown, lastInteraction, idleSleepAfter = 300, petSize = 100, swatNext;
        public bool facialMotion = true, cursorKnockback, blackwallEnabled = true;
        double nextCursorHit;
        public string idleRest = "awake", sleepReason = "";
        public bool busyMode; public double nextRetry = double.PositiveInfinity;
        public Dictionary<string, int> activityRates = new Dictionary<string, int> { { "attacks", 100 }, { "wandering", 100 }, { "resting", 100 }, { "antics", 100 }, { "talking", 100 }, { "rare", 100 } };
        public int rivalEscapes, rivalWins, escapeStreak, rivalRetries;
        double nextRare = 180; bool teleported;
        public string mood = "curious"; double nextMood = 120;
        public bool allowClose, exiting;
        public string fullscreenMode = "hide";
        public bool windowPerch = true, throwPhysics = true, timeOfDay = true;
        public int breakMinutes;
        public bool hiddenManual, hiddenFullscreen, isHidden, fullscreenOverride, busyByFullscreen;
        double nextFullscreenCheck;
        public long lastAppWindow, perchHandle; double perchOffsetX; bool perchArrived;
        double throwVX, throwVY, throwSquash; string throwReason = "";
        List<double[]> dragSamples = new List<double[]>();
        public double activeSince; DateTime drinkBreakDay = DateTime.MinValue;
        int tickMs = 33; double nextNoticeAt;
        // Settings defined by later parts (they must exist before voice-settings.json is read)
        public bool musicEnabled = true, appAware, hotkeyEnabled = true, beatSync = true, batteryReactions = true, dndAware = true;
        public bool weatherEnabled, chatEnabled, chatVoice = true, possessWindows, cursorRepairs = true, autoUpdate = true;
        public string weatherCity = "", weatherUnit = "C";
        public string scanName = ""; public int scanScore;

        public Dictionary<string, Spec> specs = new Dictionary<string, Spec>();
        public List<string> specOrder = new List<string>();
        public Spec defaultSpec;
        public Dictionary<string, List<string>> routines = new Dictionary<string, List<string>>();
        public List<string> routineOrder = new List<string>();

        void LoadEarlyTuning()
        {
            tuningError = tuning.Load(App.PathOf("tuning.json"));
            friendHue = tuning.friendHue;
            if (tuning.perMonitorDpi != 0)
            {
                try { AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false); perMonitorDpi = CapriDesktop.EnablePerMonitorDpi(); } catch { }
            }
        }

        public void ImportTuning() { tuningError = tuning.Load(App.PathOf("tuning.json")); }

        void LoadData()
        {
            var doc = Json.LoadDict(App.PathOf("behaviours.json"));
            if (Json.Get(doc, "states") == null) throw new Exception("behaviours.json is from an older Capri version.");
            foreach (var entry in Json.List(Json.Get(doc, "states")))
            {
                var s = Spec.From(entry); specs[s.id] = s; specOrder.Add(s.id);
            }
            defaultSpec = new Spec { id = "?", pose = 0, motion = "?" };
            spec = defaultSpec;
            var rs = Json.Dict(Json.Get(doc, "routines"));
            if (rs != null) foreach (var kv in rs) { routines[kv.Key] = Json.List(kv.Value).Select(x => Json.Str(x)).ToList(); routineOrder.Add(kv.Key); }
        }

        public Spec GetSpec(string name) { Spec s; return specs.TryGetValue(name, out s) ? s : defaultSpec; }
    }
}

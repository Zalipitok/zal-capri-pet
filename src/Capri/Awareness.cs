using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using WF = System.Windows.Forms;

namespace Capri
{
    // Awareness: music, beat, battery, Do Not Disturb, the app in front (opt-in), file drops, hotkey.
    partial class Pet
    {
        // --- Music: Windows media sessions (Spotify, browsers, players) through WinRT, by reflection
        // so nothing extra is needed to build or run.
        public bool musicAvailable; public string musicError = "";
        object musicManager; public bool musicPlaying; public string musicTitle = "", musicArtist = "", musicApp = "";
        double musicPlayingSince, lastSongRemark = -1000, nextMusicPoll = 3; int musicFailures;
        Type managerType, propsType; MethodInfo asTask; bool musicBusy;

        void InitMusic()
        {
            try
            {
                managerType = Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime", true);
                propsType = Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType=WindowsRuntime", true);
                var ext = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
                asTask = ext.GetMethods().First(m => m.Name == "AsTask" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                musicAvailable = true;
                StartMusicManager();
            }
            catch (Exception e) { musicAvailable = false; musicError = e.Message; }
        }

        async void StartMusicManager()
        {
            try
            {
                var op = managerType.GetMethod("RequestAsync").Invoke(null, null);
                var task = (Task)asTask.MakeGenericMethod(managerType).Invoke(null, new[] { op });
                await task;
                musicManager = task.GetType().GetProperty("Result").GetValue(task);
            }
            catch (Exception e) { musicAvailable = false; musicError = "media sessions unavailable: " + e.Message; }
        }

        static object Prop(object o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);

        async void UpdateMusic(double now)
        {
            if (!musicAvailable || !musicEnabled || now < nextMusicPoll || musicManager == null || musicBusy) return;
            nextMusicPoll = now + 2; musicBusy = true;
            try
            {
                var session = managerType.GetMethod("GetCurrentSession").Invoke(musicManager, null);
                bool was = musicPlaying;
                if (session == null) musicPlaying = false;
                else
                {
                    var info = session.GetType().GetMethod("GetPlaybackInfo").Invoke(session, null);
                    bool playing = info != null && Convert.ToString(Prop(info, "PlaybackStatus")) == "Playing";
                    musicApp = Convert.ToString(Prop(session, "SourceAppUserModelId"));
                    var op = session.GetType().GetMethod("TryGetMediaPropertiesAsync").Invoke(session, null);
                    var task = (Task)asTask.MakeGenericMethod(propsType).Invoke(null, new[] { op });
                    await task;
                    var props = task.GetType().GetProperty("Result").GetValue(task);
                    string title = Convert.ToString(Prop(props, "Title")), artist = Convert.ToString(Prop(props, "Artist"));
                    // Videos (YouTube and the like) aren't music: no headphones, no head-bobbing.
                    videoPlaying = playing && IsVideo(props, info);
                    musicPlaying = playing && !videoPlaying;
                    if (!string.IsNullOrEmpty(title) && title != musicTitle)
                    {
                        musicTitle = title; musicArtist = artist;
                        // Her opinion of the new song (most songs, not more often than songRemarkMinutes).
                        if (musicPlaying && Now - lastSongRemark >= tuning.songRemarkMinutes * 60 && rng.NextDouble() < 0.85)
                            ReactToSong(title, artist);
                    }
                }
                if (session == null) videoPlaying = false;
                if (musicPlaying && !was)
                {
                    musicPlayingSince = Now;
                    // put the headphones on: wrap up what she's doing if it has no headphone drawing
                    if (musicEnabled && newSprites && CanReact && spec.Has("calm") && routine.Count == 0 && !HasHeadphones(state)) until = Math.Min(until, Now + 1);
                }
                if (was && !musicPlaying && Now - musicPlayingSince > 60 && rng.NextDouble() < 0.5) ShowRemark("musicstop");
                musicFailures = 0;
            }
            catch (Exception e)
            {
                if (++musicFailures >= 5) { musicAvailable = false; musicError = e.Message; }
            }
            finally { musicBusy = false; }
        }

        public bool videoPlaying;
        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "308046b0af4a39cb", "opera", "brave", "vivaldi", "thebrowsercompany", "yandex" };
        static readonly string[] MusicSites = { "youtube music", "music.youtube", "spotify", "soundcloud", "deezer", "tidal", "bandcamp", "apple music", "music.apple", "pandora" };

        // A video: Windows says so, or a browser is playing something without an album (YouTube
        // videos have none; music sites like YouTube Music and Spotify set one). Foreground window
        // titles help too: a browser showing YouTube (but not YouTube Music) counts as video.
        bool IsVideo(object props, object info)
        {
            if (Convert.ToString(Prop(props, "PlaybackType")) == "Video" || Convert.ToString(Prop(info, "PlaybackType")) == "Video") return true;
            string app = (musicApp ?? "").ToLowerInvariant();
            if (!Browsers.Any(b => app.Contains(b))) return false;
            string album = Convert.ToString(Prop(props, "AlbumTitle"));
            string title = "";
            try { var fg = CapriDesktop.ForegroundApp(); if (fg != null) title = (fg[1] ?? "").ToLowerInvariant(); } catch { }
            if (MusicSites.Any(site => title.Contains(site))) return false;
            if (title.Contains("youtube")) return true;
            return string.IsNullOrEmpty(album);
        }

        bool MusicGroove => musicEnabled && musicPlaying && !voiceBusy && spec.Has("calm");

        double BeatPhase() { if (!beatSync) return -1; try { return CapriBeat.Phase(); } catch { return -1; } }

        // 0 = tilt one way, 1 = up, 2 = tilt the other way, 3 = up; on the real beat when there is one.
        public int GetMusicStep(double now)
        {
            double p = BeatPhase();
            if (p >= 0) return (int)(Math.Abs(CapriBeat.BeatNumber()) % 2) * 2 + (p < 0.5 ? 0 : 1);
            return (int)Math.Floor(now * Math.Max(30.0, tuning.musicBpm) / 30.0) % 4;
        }
        int MusicPose(double now) => musicPoseStart + GetMusicStep(now);

        double nextBeatCheck;
        void UpdateBeatSync(double now)
        {
            if (now < nextBeatCheck) return;
            nextBeatCheck = now + 2;
            bool want = beatSync && musicEnabled && musicPlaying && !isHidden;
            try
            {
                if (want && !CapriBeat.Running) CapriBeat.Start();
                else if (!want && CapriBeat.Running) CapriBeat.Stop();
            }
            catch { }
        }

        bool musicFrameActive;
        void ApplyMusicGroove(double now)
        {
            if (musicFrameActive || !MusicGroove) return;
            double k = tuning.musicBob, p = BeatPhase();
            if (p >= 0)
            {
                double side = CapriBeat.BeatNumber() % 2 == 0 ? 1.0 : -1.0;
                shift.Y += Math.Cos(2 * Math.PI * p) * 3.0 * k;
                rotation.Angle += side * Math.Sin(Math.PI * p) * 2.5 * k;
                return;
            }
            shift.Y += Math.Sin(now * 11.3) * 3.0 * k;
            rotation.Angle += Math.Sin(now * 5.65) * 2.5 * k;
        }

        // --- Battery (laptops)
        bool batKnown, batOnline, batLow, batCritical, batFull; int batPercent = -1; double nextBatteryPoll = 5;
        public bool CanReact => idleRest == "awake" && !busyMode && !dragging && !paused && !isHidden && !spec.Has("noCaught");

        void UpdateBattery(double now)
        {
            if (!batteryReactions || now < nextBatteryPoll) return;
            nextBatteryPoll = now + 15;
            WF.PowerStatus power;
            try { power = WF.SystemInformation.PowerStatus; } catch { return; }
            var status = power.BatteryChargeStatus;
            if (status.HasFlag(WF.BatteryChargeStatus.NoSystemBattery) || status == WF.BatteryChargeStatus.Unknown) return;
            int percent = (int)Math.Round(power.BatteryLifePercent * 100);
            bool online = power.PowerLineStatus == WF.PowerLineStatus.Online;
            if (!batKnown) { batKnown = true; batOnline = online; batPercent = percent; batLow = percent <= 20; batCritical = percent <= 10; batFull = percent >= 100; return; }
            if (online && !batOnline) { batLow = false; batCritical = false; if (CanReact) BeginState("celebrate"); ShowRemark("pluggedin", false, percent); }
            else if (!online && batOnline) { batFull = false; ShowRemark("unplugged", false, percent); }
            if (!online)
            {
                if (percent <= 10 && !batCritical)
                {
                    batCritical = true; batLow = true;
                    if (CanReact) BeginState("dizzy");
                    ShowRemark("batterycritical", true, percent);
                    try { tray?.ShowBalloonTip(6000, "Capri", "Battery at " + percent + "%. Plug me in!", WF.ToolTipIcon.Warning); } catch { }
                }
                else if (percent <= 20 && !batLow) { batLow = true; if (CanReact) BeginState("shiver"); ShowRemark("batterylow", false, percent); }
            }
            else if (percent >= 100 && !batFull) { batFull = true; ShowRemark("batteryfull"); }
            batOnline = online; batPercent = percent;
        }

        // --- Do Not Disturb / Focus: she goes to her quiet corner while it's on.
        public bool busyByDnd; bool dndOn; double nextDndCheck = 2;
        void UpdateQuietMode(double now)
        {
            if (now < nextDndCheck) return;
            nextDndCheck = now + 3;
            bool on = false;
            if (dndAware) { try { on = CapriQuiet.State() > 0; } catch { } }
            if (on == dndOn) return;
            dndOn = on;
            if (on)
            {
                if (!busyMode) { busyByDnd = true; SetBusyMode(true, true); ShowStatus(DndMessage(), 7); }
            }
            else if (busyByDnd)
            {
                busyByDnd = false;
                if (busyMode) { SetBusyMode(false, true); ShowRemark("dndover"); }
            }
        }

        // --- Apps (opt-in): only the name and title of the window in front; nothing stored or sent.
        string DndMessage()
        {
            string what = CapriQuiet.Source == "presentation" ? "Presentation mode" : CapriQuiet.Source == "dnd" ? "Windows notifications are off" : "Do Not Disturb / Focus";
            return what + ". Got it, I'll be quiet over here. (Drag me out or untick Busy to bring me back.)";
        }

        object appRules; public string appCategory = ""; double appCategorySince; string appAnnounced = ""; double lastAppRemark = -1000, nextAppPoll;
        void ImportAppRules()
        {
            appRules = null;
            try { if (File.Exists(App.PathOf("app-reactions.json"))) appRules = Json.Load(App.PathOf("app-reactions.json")); } catch { }
        }
        public string AppCategory(string process, string title)
        {
            if (appRules == null) return "other";
            foreach (var cat in new[] { "games", "code", "video" })
                foreach (var n in Json.List(Json.Get(appRules, cat))) if (string.Equals(process, Json.Str(n), StringComparison.OrdinalIgnoreCase)) return cat;
            if (Json.List(Json.Get(appRules, "browsers")).Any(n => string.Equals(process, Json.Str(n), StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var site in Json.List(Json.Get(appRules, "videoSites"))) { var s = Json.Str(site); if (s != "" && title.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return "video"; }
                return "browser";
            }
            return "other";
        }
        void UpdateAppAwareness(double now)
        {
            if (!appAware || now < nextAppPoll) return;
            nextAppPoll = now + 2;
            string[] app = null; try { app = CapriDesktop.ForegroundApp(); } catch { }
            if (app == null) return;
            var category = AppCategory(app[0], app[1]);
            if (category != appCategory) { appCategory = category; appCategorySince = now; return; }
            if (category == appAnnounced || now - appCategorySince < 15) return;
            appAnnounced = category;
            if (!new[] { "games", "code", "video" }.Contains(category)) return;
            if (now - lastAppRemark < tuning.appRemarkMinutes * 60 || rng.NextDouble() > 0.7) return;
            string kind = "app" + category;
            if (timeOfDay && DateTime.Now.Hour < 5 && (category == "games" || category == "video")) kind = "applate";
            if (ShowRemark(kind)) lastAppRemark = now;
        }
        bool QuietApp => appAware && appCategory == "code";

        // --- Files dropped on her: a mock scan of the name; the file is never opened.
        void InitFileDrop()
        {
            w.AllowDrop = true;
            w.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            w.Drop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                StartFileScan(files[0], files.Length); e.Handled = true;
            };
        }
        public void StartFileScan(string path, int count = 1)
        {
            string name = Path.GetFileName(path.TrimEnd('\\'));
            if (name == "") name = path;
            if (name.Length > 34) name = name.Substring(0, 31) + "...";
            int score = 0; foreach (char ch in name.ToLowerInvariant()) score = (score * 31 + ch) % 100003;
            score = 1 + score % 10;
            if (new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".msi", ".scr" }.Contains(Path.GetExtension(path).ToLowerInvariant())) score = Math.Min(10, score + 3);
            scanName = count > 1 ? name + " (+" + (count - 1) + " more)" : name;
            scanScore = score;
            AddMemory("scans");
            PrepareManualAction(); routine.Clear(); BeginState("filescan");
        }

        // --- Summon hotkey
        public bool hotkeyOk; public string hotkeyError = "";
        static uint[] ParseHotkey(string text)
        {
            uint mods = 0, key = 0;
            foreach (var part in text.Split('+'))
            {
                var p = part.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 2; else if (p == "alt") mods |= 1; else if (p == "shift") mods |= 4; else if (p == "win") mods |= 8;
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) key = char.ToUpperInvariant(p[0]);
                else if (p.Length >= 2 && p[0] == 'f') { int n; if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 12) key = (uint)(0x6F + n); }
            }
            return key == 0 ? null : new[] { mods, key };
        }
        public void RegisterSummonHotkey()
        {
            hotkeyOk = false; hotkeyError = "";
            if (petHandle == IntPtr.Zero) { hotkeyError = "unavailable"; return; }
            try { CapriHotkey.Unregister(petHandle); } catch { }
            if (!hotkeyEnabled || App.Friend) return;
            var parsed = ParseHotkey(tuning.hotkey);
            if (parsed == null) { hotkeyError = "could not read \"" + tuning.hotkey + "\" in tuning.json"; return; }
            try { hotkeyOk = CapriHotkey.Register(petHandle, parsed[0], parsed[1]); } catch (Exception e) { hotkeyError = e.Message; }
            if (!hotkeyOk && hotkeyError == "") hotkeyError = tuning.hotkey + " is already used by another app";
        }
        // Settings > Summon hotkey: checks the new keys, tries them, and keeps them in tuning.json.
        public string ChangeHotkey(string text)
        {
            text = (text ?? "").Trim();
            var parsed = ParseHotkey(text);
            if (parsed == null) return "Use something like Ctrl+Alt+C or Ctrl+Shift+F9: Ctrl, Alt, Shift or Win, plus a letter, digit or F1-F12.";
            if (parsed[0] == 0) return "Add Ctrl, Alt, Shift or Win, or the key would stop typing anywhere else.";
            var parts = text.Split('+').Select(p => p.Trim()).Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant());
            string tidy = string.Join("+", parts).Replace("Control", "Ctrl");
            string old = tuning.hotkey;
            tuning.hotkey = tidy;
            bool wasEnabled = hotkeyEnabled; hotkeyEnabled = true;
            RegisterSummonHotkey();
            if (!hotkeyOk) { var why = hotkeyError; tuning.hotkey = old; hotkeyEnabled = wasEnabled; RegisterSummonHotkey(); return tidy + " didn't work: " + why + "."; }
            try
            {
                var path = App.PathOf("tuning.json");
                var saved = System.IO.File.Exists(path) ? (Json.LoadDict(path) ?? new Dictionary<string, object>()) : new Dictionary<string, object>();
                saved["hotkey"] = tidy;
                Json.Save(path, saved);
            }
            catch (Exception e) { return "It works now, but tuning.json could not be saved: " + e.Message; }
            if (hotkeyItem != null) hotkeyItem.Header = "Summon hotkey (" + tidy + ")";
            return "";
        }

        // Hidden or far away: come here. Already next to the pointer: hide.
        void InvokeSummon()
        {
            if (clickThrough) { SetClickThrough(false); return; }
            if (isHidden) { InvokeCallOver(); return; }
            bool near = false;
            try { var c = LocalCursor(); near = Math.Abs(c.X - w.Width / 2) < 220 && Math.Abs(c.Y - w.Height / 2) < 260; } catch { }
            if (near && tray != null) SetHidden(true); else InvokeCallOver();
        }

        void InitAwareness()
        {
            ImportAppRules();
            InitMusic();
            InitFileDrop();
        }
    }
}

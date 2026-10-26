using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Capri
{
    // Self-test: Capri.exe --selftest (or Debug and help > Run self-test).
    // Runs every state for a few frames and checks data, audio, menus and Windows features, then
    // writes selftest-report.txt. It never saves settings or memory, and plays no sound.
    partial class Pet
    {
        List<string> report = new List<string>();
        Dictionary<string, int> counts = new Dictionary<string, int> { { "PASS", 0 }, { "WARN", 0 }, { "FAIL", 0 } };

        void AddResult(string level, string name, string detail = "")
        {
            counts[level]++;
            report.Add(string.Format("{0,-4}  {1}", level, name) + (detail != "" ? " - " + detail : ""));
        }

        // A check returns null/"" to pass, "WARN: ..." to warn, anything else to fail.
        void Step(string name, Func<string> check)
        {
            try
            {
                var result = check();
                if (string.IsNullOrEmpty(result)) AddResult("PASS", name);
                else if (result.StartsWith("WARN: ")) AddResult("WARN", name, result.Substring(6));
                else AddResult("FAIL", name, result);
            }
            catch (Exception e) { AddResult("FAIL", name, e.Message); }
        }

        string Errors() => selfTestErrors.Count > 0 ? string.Join("; ", selfTestErrors.Distinct().Take(6)) : null;
        void Frames(int n) { for (int i = 0; i < n; i++) { Tick(); Thread.Sleep(15); } }

        public void RunSelfTest()
        {
            muted = true; cursorKnockback = false; fullscreenMode = "ignore"; breakMinutes = 0;
            hiddenFullscreen = false; hiddenManual = false; ApplyVisibility();

            Step("Required files", () =>
            {
                var missing = new[] { "behaviours.json", "remarks.json", "voice-pack.json", "menu-theme.xaml", "tuning.json", "app-reactions.json", "unlocks.json", "games.json", "VERSION",
                    @"sounds\swat.wav", @"sounds\headpat.wav", @"sounds\ballhit.wav", @"sounds\reminder.wav", @"sprites\frames.json" }.Where(f => !File.Exists(App.PathOf(f))).ToList();
                return missing.Count > 0 ? "missing: " + string.Join(", ", missing) : null;
            });
            Step("Sprite frames", () => poses.Count < 33 ? "expected at least 33 frames, found " + poses.Count : null);
            Step("Talking-mouth frames", () => mouthPoses.Count == 0 ? "WARN: no mouth frames; her mouth stays still while she talks" : null);
            Step("Eye frames", () => eyePoses.Count == 0 ? "WARN: no eye frames; her eyes stay still" : null);
            Step("Headphone poses", () => hpPoses.Count == 0 ? "WARN: no headphone poses" : hpBobPoses.Count == 0 ? "WARN: no head-bob tilts" : null);
            Step("Music frames", () => musicPoseStart < 0 ? "WARN: no headphone frames; she sways to music instead" : null);
            Step("Normalised sprites", () =>
            {
                var sizes = Enumerable.Range(0, poses.Count).Select(i => Frame(i)).Select(p => p.PixelWidth + "x" + p.PixelHeight).Distinct().ToList();
                if (sizes.Count != 1) return "frames have different sizes: " + string.Join(", ", sizes);
                return spriteFeet.Count != poses.Count ? "frames.json lists " + spriteFeet.Count + " feet positions for " + poses.Count + " frames" : null;
            });
            Step("Per-animation sprites", () =>
            {
                var missing = specOrder.Where(id => !animPoses.ContainsKey(id)).ToList();
                if (animPoses.Count == 0) return "WARN: no anim-*.png sprites; she uses the original poses";
                return missing.Count > 0 ? "WARN: no drawing for " + string.Join(", ", missing) : null;
            });
            Step("tuning.json", () => tuningError != "" ? tuningError : null);
            Step("Settings file", () =>
            {
                if (File.Exists(settingsPath)) Json.Load(settingsPath);
                return volume <= 0 ? "WARN: saved volume is 0, so she is silent. Pick a level in Settings > Voice." : null;
            });
            Step("State definitions", () =>
            {
                var problems = new List<string>();
                foreach (var s in specs.Values)
                {
                    var indices = new List<int>(); if (s.pose != null) indices.Add(s.pose.Value); if (s.timeline != null) indices.AddRange(s.timeline.Select(x => x.Key));
                    foreach (var i in indices) if (i < 0 || i >= poses.Count) problems.Add(s.id + ": pose " + i + " does not exist");
                    if (s.path != "" && !paths.ContainsKey(s.path)) problems.Add(s.id + ": unknown path '" + s.path + "'");
                    foreach (var next in new[] { s.next, s.nextNoChase }) if (next != "" && next != "choose" && next != "@attack" && !specs.ContainsKey(next)) problems.Add(s.id + ": next state '" + next + "' does not exist");
                    if (s.remark != "" && !remarkPack.ContainsKey(s.remark)) problems.Add(s.id + ": remark pool '" + s.remark + "' missing");
                    if (s.menu != "" && s.title == "") problems.Add(s.id + ": in a menu but has no title");
                }
                foreach (var kv in routines) foreach (var step in kv.Value) if (!specs.ContainsKey(step)) problems.Add("routine '" + kv.Key + "': unknown state '" + step + "'");
                return problems.Count > 0 ? string.Join("; ", problems) : null;
            });
            Step("Remark pools", () =>
            {
                var used = new[] { "hold", "release", "idle", "pet", "shaken", "thrown", "fell", "returned", "morning", "latenight", "break", "firstmeet", "longtime", "anniversary", "memorypets", "memorythrows", "memoryswats",
                    "song", "musicstop", "appgames", "appcode", "appvideo", "applate", "scanverdict", "pluggedin", "unplugged", "batterylow", "batterycritical", "batteryfull", "gamestart", "gameover", "gamenewbest", "gamezero",
                    "unlocked", "ballout", "ballfetch", "ballbat", "ballbored", "ballsteal", "ballhide", "balltaken", "reminder", "reminderlate", "reminderset", "dndover", "weatherreport",
                    "friendhello", "friendarrive", "friendgone", "friendbye", "friendwave", "frienddance", "friendchase", "friendtag", "friendtagged", "friendtalk",
                    "possess", "cursorrepair", "cursorrepairend", "focusstart", "focusdone", "focusquit", "notedone", "gamewin", "gamelose", "triviaright", "triviawrong", "updateavailable", "updating" }
                    .Concat(WeatherReaction.Keys.Select(k => "weather" + k));
                var missing = used.Where(k => RemarkLines(k).Count == 0).ToList();
                return missing.Count > 0 ? "empty or missing in remarks.json: " + string.Join(", ", missing) : null;
            });

            // Every state, a few frames each.
            var failures = new List<string>();
            foreach (var id in specOrder.ToList())
            {
                if (id == "depart") continue;
                selfTestErrors.Clear();
                paused = false; busyMode = false; idleRest = "awake"; routine.Clear(); deferred.Clear();
                try
                {
                    BeginState(id);
                    if (id == "thrown") { throwVX = 600; throwVY = -400; }
                    Frames(4);
                }
                catch (Exception e) { selfTestErrors.Add(id + ": " + e.Message); }
                if (poseIndex < 0 || poseIndex >= poses.Count) selfTestErrors.Add(id + ": ended on missing pose " + poseIndex);
                failures.AddRange(selfTestErrors);
            }
            if (failures.Count > 0) foreach (var f in failures.Distinct()) AddResult("FAIL", "State runs", f);
            else AddResult("PASS", "All " + (specs.Count - 1) + " states run");
            StopPossession(); RestoreInFront();

            Step("Busy, paused and hidden frames", () =>
            {
                selfTestErrors.Clear();
                SetBusyMode(true); Tick(); Tick(); SetBusyMode(false);
                paused = true; Tick(); paused = false;
                hiddenManual = true; ApplyVisibility(); Tick(); hiddenManual = false; ApplyVisibility();
                return Errors();
            });
            Step("Unlocks", () =>
            {
                if (unlocksError != "") return "unlocks.json: " + unlocksError;
                if (unlocks.Count == 0) return "WARN: unlocks.json missing or empty";
                var bad = new List<string>();
                foreach (var u in unlocks)
                {
                    foreach (var s in Json.List(Json.Get(u, "routine"))) if (!specs.ContainsKey(Json.Str(s))) bad.Add(Json.Str(Json.Get(u, "id")) + ": unknown state " + Json.Str(s));
                    var pool = Json.Str(Json.Get(u, "remarks")); if (pool != "" && !remarkPack.ContainsKey(pool)) bad.Add(Json.Str(Json.Get(u, "id")) + ": remark pool " + pool + " missing");
                }
                UnlockSummary();
                return bad.Count > 0 ? string.Join("; ", bad) : null;
            });
            Step("Catch-me game", () =>
            {
                selfTestErrors.Clear();
                StartCatchGame(); Tick(); RegisterGameCatch(); Tick();
                if (gameScore != 1) return "a click did not count";
                StopCatchGame(); Tick();
                if (gameActive) return "the game did not end";
                return Errors();
            });
            Step("Settings window", () => { ShowSettingsWindow(); if (settingsWindow == null) return "window did not open"; settingsWindow.Close(); return null; });
            Step("Eyes follow the cursor", () => { var c = LocalCursor(); EyeDirection(w.Left + c.X, w.Top + c.Y); return null; });
            Step("Battery check", () => { nextBatteryPoll = 0; UpdateBattery(1.0); return null; });
            Step("Beat sync", () =>
            {
                if (!beatSync) return "WARN: turned off in settings";
                CapriBeat.Start();
                var deadline = DateTime.Now.AddSeconds(2.5);
                while (DateTime.Now < deadline && string.IsNullOrEmpty(CapriBeat.LastError)) Thread.Sleep(100);
                var problem = CapriBeat.LastError; CapriBeat.Stop();
                return string.IsNullOrEmpty(problem) ? null : "WARN: cannot listen to the PC's sound (" + problem + "); she bobs at a fixed tempo instead";
            });
            Step("Routines start", () => { foreach (var name in routineOrder.ToList()) StartRoutine(name); routine.Clear(); return null; });
            Step("Toy ball", () =>
            {
                selfTestErrors.Clear();
                SetBallShown(true); Frames(4);
                StartBallChase(); Frames(4);
                ballX = w.Left + w.Width / 2 - 22; HitBall(); Tick();
                SetBallShown(false); Tick();
                return Errors();
            });
            Step("Reminder times", () =>
            {
                var b = new DateTime(2026, 5, 4, 10, 0, 0);
                var cases = new Dictionary<string, DateTime> { { "20", b.AddMinutes(20) }, { "in 1h30", b.AddMinutes(90) }, { "45 min", b.AddMinutes(45) }, { "1 hour and 15 minutes", b.AddMinutes(75) },
                    { "half an hour", b.AddMinutes(30) }, { "17:45", b.Date.AddHours(17.75) }, { "5pm", b.Date.AddHours(17) }, { "9:00", b.Date.AddDays(1).AddHours(9) }, { "tomorrow 8am", b.Date.AddDays(1).AddHours(8) } };
                var bad = cases.Where(c => ParseWhen(c.Key, b) != c.Value).Select(c => "\"" + c.Key + "\" gave " + ParseWhen(c.Key, b)).ToList();
                foreach (var nonsense in new[] { "banana", "", "25:99" }) if (ParseWhen(nonsense, b) != null) bad.Add("\"" + nonsense + "\" should not be understood");
                if (remindersError != "") bad.Add("reminders.json: " + remindersError);
                return bad.Count > 0 ? string.Join("; ", bad) : null;
            });
            Step("Repeating reminders", () =>
            {
                var b = new DateTime(2026, 10, 24, 10, 0, 0); // a Saturday
                var bad = new List<string>();
                var r = ParseRepeating("every day at 9", b); if (r == null || r.Item1 != b.Date.AddDays(1).AddHours(9) || r.Item2 != "daily") bad.Add("every day at 9");
                r = ParseRepeating("weekdays at 8:30", b); if (r == null || r.Item1 != new DateTime(2026, 10, 26, 8, 30, 0) || r.Item2 != "weekdays") bad.Add("weekdays at 8:30");
                r = ParseRepeating("every monday at 18:00", b); if (r == null || r.Item1 != new DateTime(2026, 10, 26, 18, 0, 0) || r.Item2 != "weekly") bad.Add("every monday at 18:00");
                if (ParseRepeating("monday at 9", b) != null) bad.Add("\"monday at 9\" should not repeat");
                if (NextOccurrence(new DateTime(2026, 10, 23, 9, 0, 0), "weekdays", new DateTime(2026, 10, 23, 9, 0, 1)).DayOfWeek != DayOfWeek.Monday) bad.Add("weekdays skip the weekend");
                return bad.Count > 0 ? "not understood: " + string.Join(", ", bad) : null;
            });
            Step("Quiet hours", () =>
            {
                if (!InQuietHours("22-7", 23) || !InQuietHours("22-7", 3) || InQuietHours("22-7", 7) || InQuietHours("22-7", 15)) return "22-7 is wrong";
                if (!InQuietHours("0-8", 0) || InQuietHours("0-8", 8) || InQuietHours("off", 2)) return "0-8 or off is wrong";
                return null;
            });
            Step("Hotkey parsing", () =>
            {
                var a = ParseHotkey("Ctrl+Alt+C"); var f = ParseHotkey("ctrl+shift+f9");
                if (a == null || a[0] != 3 || a[1] != 'C') return "Ctrl+Alt+C";
                if (f == null || f[0] != 6 || f[1] != 0x78) return "Ctrl+Shift+F9";
                return ParseHotkey("Ctrl+Alt") != null ? "a hotkey without a key was accepted" : null;
            });
            Step("Reminder delivery", () =>
            {
                selfTestErrors.Clear();
                SendReminder(new Reminder { at = DateTime.Now, text = "self-test" }, 0); Tick();
                if (state != "remind") return "she did not come over (state " + state + ")";
                return Errors();
            });
            Step("Chat", () =>
            {
                if (ChatSystemPrompt().Length < 200) return "system prompt is missing";
                var m = MatchReminder("remind me in 20 minutes to stretch");
                if (!m.Success || m.Groups["what"].Value != "stretch") return "chat reminders are not understood";
                return chatEnabled && GetChatKey() == "" ? "WARN: replies are on but no API key is saved (chat window > Save key)" : null;
            });
            Step("Chat games", () =>
            {
                if (triviaBank.Count == 0) return "games.json missing or empty" + (gamesError != "" ? ": " + gamesError : "");
                var bad = triviaBank.Where(q => Json.List(Json.Get(q, "choices")).Count != 3 || Json.Num(Json.Get(q, "answer")) < 0 || Json.Num(Json.Get(q, "answer")) > 2).Select(q => Json.Str(Json.Get(q, "q"))).ToList();
                return bad.Count > 0 ? "bad trivia: " + string.Join("; ", bad) : null;
            });
            Step("Chat voice", () =>
            {
                if (!ttsAvailable) return "WARN: Windows speech is unavailable (" + ttsError + "); chat replies stay silent";
                if (CapriTts.Voices().Length == 0) return "WARN: no Windows voices installed";
                var task = CapriTts.Render("Okie doke.", tuning.chatVoiceName, tuning.chatVoicePitch, tuning.chatVoiceRate);
                if (!task.Wait(15000)) return "WARN: speech took too long";
                if (task.Result.Length < 1000) return "speech came out empty";
                return CapriPcmAudio.Envelope(task.Result, 30.0).Length == 0 ? "no mouth data from the speech" : null;
            });
            Step("Weather", () =>
            {
                var bad = new[] { new object[] { 0, 20.0, "clear" }, new object[] { 3, 15.0, "cloudy" }, new object[] { 63, 12.0, "rain" }, new object[] { 73, -2.0, "snow" }, new object[] { 95, 25.0, "storm" }, new object[] { 1, 34.0, "hot" }, new object[] { 2, -4.0, "cold" } }
                    .Where(c => WeatherKind((int)c[0], (double)c[1]) != (string)c[2]).Select(c => "code " + c[0] + " at " + c[1] + " gave " + WeatherKind((int)c[0], (double)c[1])).ToList();
                if (bad.Count > 0) return string.Join("; ", bad);
                if (weatherEnabled && weatherCity == "") return "WARN: weather is on but no city is set (Settings > Fun)";
                return weatherError != "" ? "WARN: last check failed: " + weatherError : null;
            });
            Step("Possession", () =>
            {
                CapriWindows.Rect(petHandle.ToInt64()); CapriWindows.Maximized(petHandle.ToInt64());
                selfTestErrors.Clear(); BeginState("possess"); Tick(); StopPossession();
                return Errors();
            });
            Step("Cursor trail", () =>
            {
                selfTestErrors.Clear(); StartCursorRepair(false); Tick(); Tick();
                trailUntil = 0; Tick();
                return Errors();
            });
            Step("Peeking from behind a window", () =>
            {
                selfTestErrors.Clear(); BeginState("peekbehind"); Frames(3);
                BeginState("float"); Tick();
                if (behindWindow) return "she stayed behind the window";
                return Errors();
            });
            Step("Focus timer", () =>
            {
                selfTestErrors.Clear(); StartFocus(1, true); Tick();
                if (!busyMode) return "focus did not start";
                focusEnds = DateTime.Now.AddSeconds(-1); nextFocusTick = 0; Tick();
                if (focusActive) return "focus did not finish";
                return Errors();
            });
            Step("Sticky notes", () =>
            {
                if (notesError != "") return "notes.json: " + notesError;
                if (!NewStickyNote("self-test", 0, 0, true)) return "could not make a note";
                RemoveStickyNote(notes[notes.Count - 1]); return null;
            });
            Step("Version", () => appVersion == new Version(0, 0, 0) ? "WARN: VERSION file missing, so updates cannot compare versions" : null);
            Step("Do Not Disturb check", () => { CapriQuiet.State(); return null; });
            Step("Two Capris link", () => linkOk ? null : "WARN: shared memory unavailable (" + linkError + "); a friend could not see her");
            Step("File drop scan", () => { StartFileScan(@"C:\example\test file.txt"); Tick(); return state != "filescan" ? "scan did not start" : null; });

            // Sound
            Step("Volume keeps its value", () =>
            {
                double original = volume; var bad = new List<string>();
                foreach (var level in new[] { 0.25, 0.5, 0.75, 1.0 }) { SetVolume(level); if (Math.Abs(volume - level) > 0.0001) bad.Add(level + " became " + volume); }
                SetVolume(original);
                return bad.Count > 0 ? string.Join("; ", bad) : null;
            });
            Step("Live volume control", () => liveVolume ? null : "WARN: Windows refused per-app volume; volume changes apply from the next line instead");
            Step("Voice clips are readable 16-bit WAV", () =>
            {
                if (voicePack.Count == 0) return "WARN: no voice clips installed (the voices folder isn't included in downloads); she uses text bubbles";
                var bad = new List<string>();
                foreach (var clip in voicePack)
                {
                    try { var bytes = File.ReadAllBytes(App.PathOf(clip.file)); CapriPcmAudio.WithVolume(bytes, 1.0); if (CapriPcmAudio.Envelope(bytes, 30.0).Length == 0) bad.Add(clip.id + ": no loudness data for the mouth"); }
                    catch (Exception e) { bad.Add(clip.id + ": " + e.Message); }
                }
                return bad.Count > 0 ? string.Join("; ", bad.Take(8)) + (bad.Count > 8 ? " (+" + (bad.Count - 8) + " more)" : "") : null;
            });
            Step("Voice clips switched on", () => voicePack.Count > 0 && !voicePack.Any(c => c.language == language && approvedVoices.Contains(c.Key)) ? "WARN: no " + language + " clips are on for random chatter (Voice library > Enable established only)" : null);

            // Menus and Windows features
            Step("Menu checkmarks", () => { foreach (var s in menuSync) s.Value(); return null; });
            Step("Menu items", () =>
            {
                int count = 0; var stack = new Stack<System.Windows.Controls.ItemsControl>(); stack.Push(menu);
                while (stack.Count > 0) { var node = stack.Pop(); foreach (var child in node.Items) { var mi = child as System.Windows.Controls.MenuItem; if (mi != null) { count++; stack.Push(mi); } } }
                return count < 60 ? "only " + count + " menu items were built" : null;
            });
            Step("Tray icon", () => tray == null ? "WARN: no tray icon; she keeps a taskbar button instead" : null);
            Step("Monitors", () => AllWorkAreas().Count == 0 ? "no monitors reported" : null);
            Step("Work area", () => { var a = WorkArea(); return a.Width < 300 || a.Height < 300 ? "work area looks wrong: " + a : null; });
            Step("Fullscreen detection", () => { CapriDesktop.FullscreenBlocks(petHandle); return null; });
            Step("Idle time", () => CapriDesktop.IdleSeconds() < 0 ? "negative idle time" : null);
            Step("Summon hotkey", () =>
            {
                if (!hotkeyEnabled || hotkeyOk) return null;
                Mutex running;
                if (Mutex.TryOpenExisting("Local\\CapriDesktopCompanion", out running)) { running.Dispose(); return null; } // the normal Capri holds it
                return "WARN: " + hotkeyError;
            });
            Step("Music reactions", () =>
            {
                if (!musicAvailable) return "WARN: not available (" + musicError + ")";
                var deadline = DateTime.Now.AddSeconds(3);
                while (musicManager == null && musicAvailable && DateTime.Now < deadline) { UiDispatcher().Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); Thread.Sleep(50); }
                return musicManager == null ? "WARN: Windows media sessions did not respond" : null;
            });
            Step("App awareness", () =>
            {
                var app = CapriDesktop.ForegroundApp(); if (app != null) AppCategory(app[0], app[1]);
                return appRules == null ? "WARN: app-reactions.json missing or invalid" : null;
            });

            var header = new[] { "Capri self-test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), counts["PASS"] + " passed, " + counts["WARN"] + " warnings, " + counts["FAIL"] + " failed",
                "Windows " + Environment.OSVersion.Version + "  .NET " + Environment.Version + "  Capri " + appVersion + " (native)", "" };
            try { var reportPath = App.PathOf("selftest-report.txt"); Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); File.WriteAllLines(reportPath, header.Concat(report)); } catch { }
            var notPassed = report.Where(r => !r.StartsWith("PASS")).Take(10).ToList();
            MessageBox.Show(header[1] + (notPassed.Count > 0 ? "\n\n" + string.Join("\n", notPassed) : "\n\nEverything checks out.") + "\n\nFull report: logs\\selftest-report.txt", "Capri self-test");
            allowClose = true; w.Close();
        }

        static System.Windows.Threading.Dispatcher UiDispatcher() => Application.Current.Dispatcher;
    }
}

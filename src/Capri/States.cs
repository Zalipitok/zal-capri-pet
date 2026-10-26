using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Capri
{
    // State machine, throws, visibility and modes.
    partial class Pet
    {
        public Queue<string> routine = new Queue<string>();
        Queue<string> autoBag = new Queue<string>(), routineBag = new Queue<string>(), busyBag = new Queue<string>();
        bool hoverLatched, attackContact;
        string busyActivity = "gaming"; double busyUntil, busyStart;
        double lastFxError = -60, travelFxUntil, travelFxDirection = 1.0;
        double nextPet;
        string startupLink;

        // Per-frame locals shared with the paths and motions (set by the tick).
        double t, dt, phase; Rect area; Point mouse; double cursorX, cursorY;

        void InitStates()
        {
            startupLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Capri.lnk");
            UpgradeStartupLink();
            InitMotions();
        }

        public void StartRoutine(string name)
        {
            routine.Clear();
            List<string> steps;
            if (routines.TryGetValue(name, out steps)) foreach (var s in steps) routine.Enqueue(s);
            paused = false;
            ChooseState();
        }

        public void BeginState(string name)
        {
            if (exiting && name != "depart") return;
            KeyValuePair<long, Rect>? perch = null;
            if (name == "perch" && windowPerch && !stayPut && rng.NextDouble() < tuning.perchChance) { perch = FindPerchWindow(); if (perch != null) name = "windowperch"; }
            else if (name == "windowperch") { perch = FindPerchWindow(); if (perch == null) name = "perch"; }
            var a = WorkArea();
            var others = new List<Rect>();
            if (name == "roam") { others = AllWorkAreas().Where(r => !r.Equals(a)).ToList(); if (others.Count == 0) name = "wander"; }
            var s = GetSpec(name);
            state = name; spec = s; stateStart = Now;
            until = stateStart + (double.IsNaN(s.secondsFixed) ? rng.Next(s.secondsMin, s.secondsMax + 1) : s.secondsFixed);
            anchorX = w.Left; anchorY = w.Top;
            targetX = a.Left + rng.NextDouble() * Math.Max(1.0, a.Width - w.Width);
            targetY = a.Top + rng.NextDouble() * Math.Max(1.0, a.Height - w.Height);
            switch (s.target)
            {
                case "bottom": targetY = FloorTop(a, PoseFor(name)); break;
                case "taskbar": targetX = w.Left; targetY = FloorTop(a, PoseFor(name)); break;
                case "edge": targetX = rng.Next(2) == 0 ? a.Left : a.Right - w.Width; break;
                case "cursor": { var c = LocalCursor(); targetX = w.Left + c.X - w.Width / 2; targetY = w.Top + c.Y - (w.Height + 100) / 2; break; }
                case "cursorAnchor": { var c = LocalCursor(); anchorX = w.Left + c.X - w.Width / 2; anchorY = w.Top + c.Y - w.Height / 2; break; }
                case "otherMonitor":
                    if (others.Count > 0)
                    {
                        var b = others[rng.Next(others.Count)];
                        targetX = b.Left + rng.NextDouble() * Math.Max(1.0, b.Width - w.Width);
                        targetY = b.Top + rng.NextDouble() * Math.Max(1.0, b.Height - w.Height);
                        double distance = Math.Sqrt(Math.Pow(targetX - w.Left, 2) + Math.Pow(targetY - w.Top, 2));
                        until = stateStart + distance / Math.Max(1.0, s.speed ?? 1) + 1.5;
                    }
                    break;
                case "window":
                    if (perch != null)
                    {
                        perchHandle = perch.Value.Key; var r = perch.Value.Value; perchArrived = false;
                        perchOffsetX = !double.IsNaN(perchDropX) ? Math.Max(-w.Width * 0.3, Math.Min(r.Width - w.Width * 0.7, perchDropX)) : (r.Width - w.Width) * (0.1 + 0.8 * rng.NextDouble());
                        perchDropX = double.NaN;
                        targetX = r.Left + perchOffsetX; targetY = r.Top - w.Height + SpriteFoot(PerchPose) + tuning.perchSink;
                    }
                    break;
            }
            if (s.speed != null && (s.target == "random" || s.target == "bottom" || s.target == "edge") && name != "roam") AvoidWorkTarget(a, s.target != "random");
            switch (name)
            {
                case "teleport": teleported = false; break;
                case "pounce": attackContact = false; break;
                case "caught": caughtCooldown = stateStart + 5; break;
                case "scanverdict": ShowRemark("scanverdict", false, scanScore); break;
            }
            effect.Text = s.effect;
        }

        string ActivityCategory(string name) => GetSpec(name).category;

        void AddShuffled(Queue<string> queue, List<string> items)
        {
            var list = Shuffle(items);
            if (list.Count > 1 && list[0] == state) { var x = list[0]; list[0] = list[1]; list[1] = x; }
            foreach (var i in list) queue.Enqueue(i);
        }

        public void ChooseState()
        {
            if (busyMode) { BeginState("taskbar"); return; }
            if (routine.Count > 0) { BeginState(routine.Dequeue()); return; }
            double now = Now;
            if (activityRates["rare"] > 0 && now > nextRare && !voiceBusy)
            {
                nextRare = now + rng.Next(180, 361) * 100.0 / activityRates["rare"];
                var rare = specOrder.Where(id => specs[id].rare).ToList();
                if (rare.Count > 0) { BeginState(rare[rng.Next(rare.Count)]); return; }
            }
            if (TryTrick(now)) return;
            if (BallAvailable && !stayPut && activityRates["antics"] > 0 && rng.NextDouble() < tuning.ballChance) { StartBallChase(); return; }
            double powers = QuietNow ? 0 : tuning.powersChance;
            if (activityRates["antics"] > 0 && possessWindows && rng.NextDouble() < powers) { if (InvokePossession(false)) return; }
            if (activityRates["antics"] > 0 && herCursorEnabled && !herCursorActive && rng.NextDouble() < powers / 2) { StartHerCursor(false); if (herCursorActive) return; }
            if (activityRates["antics"] > 0 && cursorRepairs && Now > trailUntil + 600 && rng.NextDouble() < powers / 2) { StartCursorRepair(false); return; }
            if (musicEnabled && musicPlaying && activityRates["antics"] > 0 && rng.NextDouble() < 0.2) { BeginState("dance"); return; }
            if (timeOfDay)
            {
                var date = DateTime.Now;
                if (!stayPut && date.Hour >= 14 && date.Hour < 17 && drinkBreakDay != date.Date && now > 600 && activityRates["antics"] > 0 && routines.ContainsKey("Drink break"))
                { drinkBreakDay = date.Date; StartRoutine("Drink break"); return; }
                if (date.Hour < 5 && activityRates["resting"] > 0 && rng.NextDouble() < 0.25)
                { var sleepy = new[] { "yawn", "sleepy", "meditate" }; BeginState(sleepy[rng.Next(sleepy.Length)]); return; }
            }
            for (int guard = 0; guard < 1000; guard++)
            {
                if (autoBag.Count == 0)
                {
                    var choices = specOrder.Where(id => specs[id].auto && (chase || !specs[id].Has("needsChase")) && (!stayPut || specs[id].speed == null)).ToList();
                    if (!stayPut) choices.Add("__routine");
                    var weighted = new List<string>();
                    foreach (var choice in choices)
                    {
                        string category = choice == "__routine" ? "antics" : ActivityCategory(choice);
                        int rate; if (!activityRates.TryGetValue(category, out rate)) rate = 100;
                        if (rate >= 100) weighted.Add(choice);
                        if (rng.Next(100) < (rate % 100)) weighted.Add(choice);
                        if (rate == 200) weighted.Add(choice);
                    }
                    if (weighted.Count == 0) weighted.Add("float");
                    AddShuffled(autoBag, weighted);
                }
                var next = autoBag.Dequeue();
                if (stayPut && (next == "__routine" || GetSpec(next).speed != null)) { autoBag.Clear(); continue; }
                if (next == "__routine")
                {
                    if (activityRates["antics"] <= 0) continue;
                    if (routineBag.Count == 0) AddShuffled(routineBag, routineOrder);
                    StartRoutine(routineBag.Dequeue()); return;
                }
                int r2; if (!activityRates.TryGetValue(ActivityCategory(next), out r2)) r2 = 100;
                if (next != "float" && r2 <= 0) continue;
                if (!chase && GetSpec(next).Has("needsChase")) continue;
                if (next == "wander" && rng.NextDouble() < tuning.roamChance && AllWorkAreas().Count > 1) next = "roam";
                BeginState(next); return;
            }
            BeginState("float");
        }

        // ---------------------------------------------------------------------------------
        // Throws and falls
        public void StartThrow(double vx, double vy, string reason)
        {
            throwVX = vx; throwVY = vy; throwSquash = 0; throwReason = reason;
            routine.Clear(); BeginState("thrown");
        }

        void FinishThrow()
        {
            rotation.Angle = 0;
            if (shakeAngry) { shakeAngry = false; BeginState("shaken"); caughtCooldown = until + 1; }
            else
            {
                BeginState("landing");
                if (throwReason == "fell") ShowRemark("fell");
                else
                {
                    int count = AddMemory("throws");
                    if (Milestone(count, 3, 10, 25)) ShowRemark("memorythrows", false, count); else ShowRemark("thrown");
                }
            }
        }

        static double[] SampleVelocity(List<double[]> samples, double now)
        {
            if (samples.Count < 2) return null;
            var last = samples[samples.Count - 1];
            if (now - last[0] > 0.09) return null; // held still before release
            var first = samples[0];
            for (int i = samples.Count - 2; i >= 0; i--) { first = samples[i]; if (last[0] - samples[i][0] >= 0.06) break; }
            double span = last[0] - first[0]; if (span <= 0.005) return null;
            return new[] { (last[1] - first[1]) / span, (last[2] - first[2]) / span };
        }

        // ---------------------------------------------------------------------------------
        // Visibility: hidden from the tray, or automatically for fullscreen apps.
        public void ApplyVisibility()
        {
            bool hidden = hiddenManual || hiddenFullscreen;
            if (hidden == isHidden) return;
            isHidden = hidden;
            if (hidden)
            {
                StopVoice(); sfxPlayer.Close(); menu.IsOpen = false;
                if (cursorFx.IsVisible) cursorFx.Hide();
                wall.Visibility = Visibility.Collapsed; bubble.Visibility = Visibility.Collapsed;
                w.Topmost = false; w.Opacity = 0; w.IsHitTestVisible = false;
            }
            else
            {
                w.Opacity = 1; w.IsHitTestVisible = true; w.Topmost = true;
                ClampPet();
                if (!busyMode) BeginState("arrive");
            }
        }

        public void SetHidden(bool hidden)
        {
            hiddenManual = hidden;
            if (!hidden && hiddenFullscreen) { hiddenFullscreen = false; fullscreenOverride = true; }
            ApplyVisibility();
        }

        void UpdateFullscreenState()
        {
            if (fullscreenMode == "ignore" || exiting)
            {
                if (hiddenFullscreen) { hiddenFullscreen = false; ApplyVisibility(); }
                if (busyByFullscreen) { busyByFullscreen = false; if (busyMode) SetBusyMode(false, true); }
                return;
            }
            bool blocked = false;
            try { blocked = CapriDesktop.FullscreenBlocks(petHandle); } catch { }
            if (!blocked) fullscreenOverride = false;
            if (fullscreenMode == "hide")
            {
                bool want = blocked && !fullscreenOverride;
                if (want != hiddenFullscreen) { hiddenFullscreen = want; ApplyVisibility(); }
            }
            else
            {
                if (blocked && !busyMode && !fullscreenOverride) { busyByFullscreen = true; SetBusyMode(true, true); }
                else if (!blocked && busyByFullscreen) { busyByFullscreen = false; if (busyMode) SetBusyMode(false, true); }
            }
        }

        public void SetFullscreenMode(string mode)
        {
            fullscreenMode = mode; fullscreenOverride = false;
            if (mode != "hide" && hiddenFullscreen) { hiddenFullscreen = false; ApplyVisibility(); }
            if (mode != "busy" && busyByFullscreen) { busyByFullscreen = false; if (busyMode) SetBusyMode(false, true); }
            nextFullscreenCheck = 0;
            SaveSettings();
        }

        // Away from the computer: sleep while you're gone, greet you when you're back.
        void WakeFromAway()
        {
            double now = Now;
            idleRest = "awake"; sleepReason = ""; lastInteraction = now;
            mood = "curious"; nextMood = now + 60; caughtCooldown = now + 4;
            activeSince = now;
            AddMemory("returns");
            BeginState("welcomeback");
            string kind = "returned";
            if (timeOfDay) { int hour = DateTime.Now.Hour; if (hour >= 5 && hour < 11) kind = "morning"; else if (hour < 5) kind = "latenight"; }
            ShowRemark(kind);
            nextRemark = now + 40;
            ScheduleYap();
        }

        public void SetBusyMode(bool on, bool auto = false)
        {
            // Turning busy off by hand during a fullscreen app keeps it off until that app closes.
            if (!auto && !on && busyByFullscreen) fullscreenOverride = true;
            busyMode = on; RegisterInteraction();
            if (on)
            {
                StopVoice(); sfxPlayer.Close(); nextRetry = double.PositiveInfinity;
                var a = WorkArea(); w.Left = a.Right - w.Width - 12; w.Top = FloorTop(a, PoseFor("taskbar"));
                busyUntil = 0; busyBag.Clear();
                activeSince = Now;
                BeginState("taskbar");
            }
            else { busyByFullscreen = false; busyByDnd = false; ScheduleYap(); BeginState("stretch"); }
        }

        public void SetPaused(bool on)
        {
            paused = on;
            if (on) { StopVoice(); sfxPlayer.Close(); ShowStatus("chilling."); }
        }

        public void SetMuted(bool on) { muted = on; StopVoice(); if (on) sfxPlayer.Close(); SaveSettings(); }

        public void PrepareManualAction()
        {
            RegisterInteraction();
            if (busyByFullscreen) fullscreenOverride = true;
            if (focusActive) { busyMode = false; StopFocus(true); }
            busyMode = false; busyByFullscreen = false; busyByDnd = false;
            paused = false; mouseDown = false; hoverLatched = true;
            caughtCooldown = Now + 3;
            nextRetry = double.PositiveInfinity;
            nextYap = Now + 15; nextRemark = Now + 15;
            if (stayPut) stayAway = true;
        }

        public void InvokeHeadPat()
        {
            if (Now < nextPet) return;
            PrepareManualAction(); routine.Clear(); BeginState("headpat");
            caughtCooldown = until + 1; nextPet = Now + 2;
            PlaySfx("headpat.wav", 0.8);
            int count = AddMemory("pets");
            if (Milestone(count, 3, 10, 25, 50, 100)) ShowRemark("memorypets", false, count); else ShowRemark("pet");
        }

        // Windows refuses the cursor position while a secure screen is up (a UAC prompt, the lock
        // screen, Ctrl+Alt+Del): use the last position we saw instead of failing.
        Point lastCursorScreen;
        public Point LocalCursor()
        {
            try { var p = CapriCursor.ReadPosition(); lastCursorScreen = new Point(p.X, p.Y); } catch { }
            return w.PointFromScreen(lastCursorScreen);
        }

        bool CursorContact()
        {
            var point = LocalCursor();
            var bounds = sprite.TransformToAncestor(w).TransformBounds(new Rect(0, 0, sprite.ActualWidth, sprite.ActualHeight));
            double rx = Math.Max(bounds.Left - point.X, Math.Max(0.0, point.X - bounds.Right));
            double ry = Math.Max(bounds.Top - point.Y, Math.Max(0.0, point.Y - bounds.Bottom));
            return Math.Sqrt(rx * rx + ry * ry) < 32;
        }

        bool MouseButtonsUp => Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released && Mouse.MiddleButton == MouseButtonState.Released;

        void FinishCursorAttack()
        {
            // Only a nearby hit can move the pointer, never while a mouse button is held.
            var point = LocalCursor();
            double dx = point.X - w.Width / 2;
            bool near = attackContact || CursorContact();
            if (near && chase && !busyMode && cursorKnockback && Now >= nextCursorHit && MouseButtonsUp)
            {
                bool moved = CapriCursor.Nudge(dx < 0 ? -75 : 75, -20);
                if (moved) nextCursorHit = Now + 20;
                App.Log("cursor-error.log", "attack: " + CapriCursor.LastResult);
                cursorLastX = null; cursorLastY = null;
            }
            if (near)
            {
                rivalWins++; escapeStreak = 0; SaveSettings();
                mood = "smug"; rivalRetries = 0; nextRetry = double.PositiveInfinity;
                BeginState("impact");
            }
            else
            {
                rivalEscapes++; escapeStreak = Math.Min(5, escapeStreak + 1); SaveSettings();
                mood = "irritated";
                if (rivalRetries < 1) nextRetry = Now + Math.Max(15.0, 30 - escapeStreak * 3); else { nextRetry = double.PositiveInfinity; rivalRetries = 0; }
                BeginState("rivalglare");
            }
            nextMood = Now + 60;
        }

        void UpdateBlackwall()
        {
            bool travelling = Now < travelFxUntil;
            var tags = spec.tags;
            if (!blackwallEnabled || isHidden || paused || dragging || busyMode || menu.IsOpen || (!travelling && !tags.Contains("blackwall")))
            {
                if (wall.Visibility != Visibility.Collapsed) wall.Visibility = Visibility.Collapsed;
                return;
            }
            wall.Visibility = Visibility.Visible;
            double elapsed = Now - stateStart;
            bool strong = tags.Contains("strong");
            wall.Opacity = strong ? 0.95 : 0.3;
            CapriFx.UpdateBars(wallBars, Math.Max(1.0, wall.ActualWidth), Math.Max(1.0, wall.ActualHeight), elapsed, travelling && !strong, travelFxDirection);
        }

        void UpdateAttackArcs()
        {
            bool active = blackwallEnabled && !isHidden && !busyMode && !paused && !dragging && !menu.IsOpen && spec.Has("arcs");
            if (!active)
            {
                if (cursorFx.IsVisible) cursorFx.Hide();
                if (petArcs[0].Visibility != Visibility.Collapsed) foreach (var arc in petArcs) arc.Visibility = Visibility.Collapsed;
                return;
            }
            if (!cursorFx.IsVisible)
            {
                cursorFx.Owner = w; cursorFx.Show();
                cursorFxHandle = new System.Windows.Interop.WindowInteropHelper(cursorFx).Handle;
                CapriCursor.MakeOverlay(cursorFxHandle);
            }
            CapriCursor.FollowCursor(cursorFxHandle);
            double time = Now, strength = spec.Has("strong") ? 0.95 : 0.4;
            cursorCanvas.Opacity = strength;
            CapriFx.UpdateArcs(cursorArcs, 160, 160, time, strength);
            CapriFx.UpdateArcs(petArcs, Math.Max(1.0, wall.ActualWidth), Math.Max(1.0, wall.ActualHeight), time, strength);
        }

        public void SetPetSize(double percent)
        {
            petSize = Math.Max(65.0, Math.Min(160.0, percent));
            double factor = petSize / 100;
            double oldBottom = w.Top + w.Height, oldCenter = w.Left + w.Width / 2;
            w.Width = 260 * factor * spriteScale; w.Height = 130 + 300 * factor * spriteScale;
            if (!double.IsNaN(oldBottom)) w.Top = oldBottom - w.Height;
            if (!double.IsNaN(oldCenter)) w.Left = oldCenter - w.Width / 2;
            ClampPet();
        }

        // Sound effects share one player. important (reminders) plays in Busy mode, hidden, and with
        // sound effects off; only Mute stops it.
        public void PlaySfx(string file, double gain = 1.0, bool important = false)
        {
            if (muted || (!important && (!sfxEnabled || busyMode || isHidden))) return;
            var path = App.PathOf(Path.Combine("sounds", file));
            if (!File.Exists(path)) return;
            try
            {
                sfxPlayer.Close(); UpdateSfxVolume(); sfxPlayer.Volume = Math.Min(1.0, sfxPlayer.Volume * gain);
                sfxPlayer.Open(new Uri(path, UriKind.Absolute)); sfxPlayer.Play();
            }
            catch { }
        }

        public void InvokeSwat()
        {
            PrepareManualAction();
            double now = Now;
            if (now < swatNext) return;
            swatNext = now + 3; caughtCooldown = now + 3;
            paused = false; BeginState("swat");
            int count = AddMemory("swats");
            if (Milestone(count, 10, 30, 60)) ShowRemark("memoryswats", false, count);
            PlaySfx("cartoon-swat.wav");
        }

        // Brings Capri next to the pointer, on whichever monitor it's on.
        public void InvokeCallOver()
        {
            SetHidden(false);
            PrepareManualAction(); routine.Clear();
            try { var c = LocalCursor(); w.Left = w.Left + c.X - w.Width / 2; w.Top = w.Top + c.Y - w.Height + 40; } catch { }
            ClampPet(); MarkStayHere(); BeginState("arrive");
        }

        // Start with Windows: a Startup-folder shortcut.
        public bool StartsWithWindows => File.Exists(startupLink);

        // A "Start with Windows" shortcut from the PowerShell days starts PowerShell first; point it
        // straight at Capri.exe (faster, no console flash).
        void UpgradeStartupLink()
        {
            if (App.SelfTest || App.Friend || !File.Exists(startupLink)) return;
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic link = shell.CreateShortcut(startupLink);
                string target = (string)link.TargetPath ?? "";
                if (string.Equals(target, App.ExePath, StringComparison.OrdinalIgnoreCase)) return;
                string name = Path.GetFileName(target).ToLowerInvariant();
                if (name == "powershell.exe" || name == "wscript.exe" || name == "pwsh.exe" || name == "capri.exe")
                {
                    link.TargetPath = App.ExePath; link.Arguments = ""; link.WorkingDirectory = App.Root;
                    link.Description = "Capri desktop companion"; link.IconLocation = App.ExePath + ",0";
                    link.Save();
                }
            }
            catch { }
        }
        public void SetStartWithWindows(bool on)
        {
            try
            {
                if (on)
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic link = shell.CreateShortcut(startupLink);
                    link.TargetPath = App.ExePath; link.Arguments = ""; link.WorkingDirectory = App.Root;
                    link.Description = "Capri desktop companion"; link.IconLocation = App.ExePath + ",0";
                    link.Save();
                    ShowStatus("I will be here when you log in.");
                }
                else if (File.Exists(startupLink)) File.Delete(startupLink);
            }
            catch (Exception e) { ShowProblem("Could not change the startup shortcut: " + e.Message); }
        }
    }
}

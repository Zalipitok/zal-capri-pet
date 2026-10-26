using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Capri
{
    // The per-frame loop and shutdown.
    partial class Pet
    {
        public DispatcherTimer timer;
        double lastRuntimeError = -60;
        List<double> errorTimes = new List<double>();
        Queue<Action> deferred = new Queue<Action>();
        public List<string> selfTestErrors = new List<string>();

        // Runs an action on the next frame once the menu has closed (menu clicks happen mid-menu).
        public void Defer(Action action) => deferred.Enqueue(action);

        void InitLoop()
        {
            timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (s, e) => Tick();
            w.Closing += (s, e) =>
            {
                if (allowClose) return;
                e.Cancel = true;
                if (!exiting)
                {
                    StopVoice(); paused = false; busyMode = false; exiting = true;
                    hiddenManual = false; hiddenFullscreen = false; ApplyVisibility();
                    menu.IsOpen = false; BeginState("depart");
                    if (!timer.IsEnabled) { allowClose = true; w.Dispatcher.BeginInvoke(new Action(() => w.Close())); }
                }
            };
            w.Closed += (s, e) =>
            {
                timer.Stop(); SaveSettings(); SaveMemory(); StopVoice(); sfxPlayer.Close();
                try { if (!App.Friend) StopFriendCapri(); } catch { }
                try { StopPossession(); RestoreInFront(); } catch { }
                try { CapriHotkey.Unregister(petHandle); } catch { }
                try { CapriBeat.Stop(); } catch { }
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                Application.Current.Shutdown();
            };
        }

        void UpdateTickRate()
        {
            int ms = 33;
            if (isHidden) ms = 250;
            else if (busyMode || paused || idleRest == "sleeping") ms = 50;
            if (ms != tickMs) { tickMs = ms; timer.Interval = TimeSpan.FromMilliseconds(ms); }
        }

        public void Tick()
        {
            try { TickBody(); }
            catch (Exception ex)
            {
                if (Now - lastRuntimeError > 5) { lastRuntimeError = Now; App.Log("runtime-error.log", ex.ToString()); }
                if (App.SelfTest) { selfTestErrors.Add(state + ": " + ex.Message + " " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim()); return; }
                // A one-off hiccup: skip this frame and start something fresh. Only a run of errors pauses her.
                errorTimes.Add(Now); errorTimes.RemoveAll(x => Now - x > 10);
                if (errorTimes.Count < 5) { try { routine.Clear(); ChooseState(); } catch { } return; }
                errorTimes.Clear();
                paused = true; wall.Visibility = Visibility.Collapsed;
                if (cursorFx.IsVisible) cursorFx.Hide();
                ShowProblem("I paused after an animation error (see runtime-error.log). Right-click me > Interact > Do something! to retry.");
            }
        }

        void TickBody()
        {
            while (deferred.Count > 0 && !menu.IsOpen) deferred.Dequeue()();
            UpdateTickRate();
            UpdatePoseFade();
            try { UpdateBlackwall(); UpdateAttackArcs(); }
            catch (Exception ex) { if (Now - lastFxError > 5) { lastFxError = Now; App.Log("effects-error.log", ex.ToString()); } }
            sabotagePanel.Visibility = Visibility.Collapsed;
            if (exiting)
            {
                double progress = Math.Min(1.0, (Now - stateStart) / 1.2);
                sprite.Opacity = 1 - progress; shift.X = Math.Sin(progress * 24) * 6;
                if (progress >= 1) { allowClose = true; w.Close(); }
                return;
            }
            t = Now; double elapsedTick = Math.Max(0.0, t - last); dt = Math.Min(0.08, elapsedTick); last = t;
            if (voiceBusy && voiceJob != null && voiceJob.IsCompleted)
            {
                var failure = voiceJob.Error; voiceJob = null;
                voiceBusy = false; EndSpeechPose();
                subtitleUntil = t + 1.5; messageUntil = subtitleUntil;
                if (!string.IsNullOrEmpty(failure)) ReportAudioError(failure);
            }
            if (t > messageUntil) bubble.Visibility = Visibility.Collapsed;
            // Once a second: fullscreen apps, and remember the last app window.
            if (t >= nextFullscreenCheck)
            {
                nextFullscreenCheck = t + 1;
                UpdateFullscreenState();
                try { var fg = CapriDesktop.ForegroundWindow(); if (fg != null) lastAppWindow = fg[0]; } catch { }
            }
            if (hotkeyOk && CapriHotkey.TakePressed()) InvokeSummon();
            if (memoryDirty && t >= nextMemorySave) { nextMemorySave = t + 30; SaveMemory(); }
            UpdateMusic(t); UpdateBeatSync(t); UpdateBattery(t); UpdateAppAwareness(t);
            UpdateQuietMode(t); UpdateReminders(t); UpdateWeather(t); UpdateFriend(t);
            UpdateBall(t, dt); UpdatePowers(t); UpdateHerCursor(t); UpdateFocus(t); UpdateNotes(t); UpdateUpdates(t);
            if (isHidden)
            {
                lastInteraction = t; stateStart += elapsedTick; until += elapsedTick; nextRetry += elapsedTick; nextMood += elapsedTick;
                gameEnds += elapsedTick; gameNextJump += elapsedTick;
                return;
            }
            // Recover a release delivered outside our window; a stale hold freezes the timer.
            if (mouseDown && !dragging && Mouse.LeftButton == MouseButtonState.Released) { mouseDown = false; downPoint = null; RegisterInteraction(); }
            if ((mouseDown || dragging) && t >= nextHoldRemark) { heldLongEnough = true; ShowRemark("hold"); nextHoldRemark = t + 5; }
            if (dragging)
            {
                UpdateShadow(false, false);
                SetPose(shakeAngry ? AnimPose("shaken", 20) : AnimPose("thrown", 30));
                rotation.Angle = Math.Sin(t * 16) * 9;
                scale.ScaleX = direction * (1 + Math.Sin(t * 14) * 0.025); scale.ScaleY = 1 + Math.Sin(t * 18) * 0.04;
                shift.X = Math.Sin(t * 22) * 3; shift.Y = Math.Sin(t * 16) * 3; sprite.Opacity = 1;
                return;
            }
            if (paused || menu.IsOpen || voiceChooserOpen || mouseDown)
            {
                lastInteraction = t; stateStart += elapsedTick; until += elapsedTick; nextRetry += elapsedTick; nextMood += elapsedTick;
                gameEnds += elapsedTick; gameNextJump += elapsedTick;
                ApplySpeechPose(); return;
            }
            // You: system-wide idle time (any keyboard or mouse input).
            double sysIdle = 0; try { sysIdle = CapriDesktop.IdleSeconds(); } catch { }
            bool away = sysIdle >= 60;
            if (sysIdle >= 300) activeSince = t;
            if (breakMinutes > 0 && t - activeSince >= breakMinutes * 60 && idleRest == "awake" && !voiceBusy && !spec.Has("noCaught"))
            {
                activeSince = t;
                if (!busyMode) { routine.Clear(); BeginState("breaktime"); }
                ShowRemark("break", true, breakMinutes);
            }
            if (busyMode) { BusyFrame(t); return; }
            if (t >= nextUnlockCheck) { nextUnlockCheck = t + 30; TestUnlocks(); }
            if (t >= nextMood) { var moods = new[] { "bored", "curious", "smug", "irritated" }.Where(m => m != mood).ToArray(); mood = moods[rng.Next(moods.Length)]; nextMood = t + rng.Next(90, 181); }
            if (activityRates["attacks"] > 0 && t >= nextRetry && spec.Has("calm") && chase && idleRest == "awake" && !voiceBusy && !away && !QuietNow)
            { nextRetry = double.PositiveInfinity; rivalRetries++; BeginState("rivalsneak"); }
            mouse = LocalCursor();
            cursorX = w.Left + mouse.X; cursorY = w.Top + mouse.Y;
            bool cursorMoved = cursorLastX != null && Math.Abs(cursorX - cursorLastX.Value) + Math.Abs(cursorY - cursorLastY.Value) > 2;
            cursorLastX = cursorX; cursorLastY = cursorY;
            if (idleRest == "sleeping" && sleepReason == "away" && sysIdle < 2) WakeFromAway();
            if (sprite.IsMouseOver && cursorMoved) RegisterInteraction();
            if (idleSleepAfter > 0 && idleRest == "awake" && t - lastInteraction >= idleSleepAfter && !spec.Has("noSleep") && !voiceBusy)
            {
                idleRest = "yawning"; sleepReason = away ? "away" : "ignored";
                BeginState("yawn"); bubble.Visibility = Visibility.Collapsed;
            }
            if (idleRest != "awake" && sleepReason == "ignored" && sysIdle >= 120) sleepReason = "away";
            if (idleRest == "yawning" && t > until) { idleRest = "sleeping"; BeginState("nap"); until = double.PositiveInfinity; }
            if (!sprite.IsMouseOver) hoverLatched = false;
            if (sprite.IsMouseOver && !hoverLatched && cursorMoved && t > caughtCooldown && !spec.Has("noCaught")) { hoverLatched = true; BeginState("caught"); }
            if (idleRest == "awake" && t > until && !voiceBusy)
            {
                if (spec.setMood != "") mood = spec.setMood;
                string next = spec.next;
                if (!chase && spec.nextNoChase != "") next = spec.nextNoChase;
                if (next == "@attack") FinishCursorAttack();
                else if (next != "" && next != "choose") BeginState(next);
                else ChooseState();
            }
            // This session's greeting (first meeting, a long absence, an anniversary).
            if (greeting != null && t > 3 && state != "arrive" && !voiceBusy) { ShowRemark((string)greeting[0], false, greeting[1]); greeting = null; }
            if (idleRest == "awake" && !away && !QuietApp && !QuietNow && chatter != "off" && t > nextYap && !spec.Has("noYap"))
            {
                PlayVoice("random");
                if (t > nextYap) ScheduleYap();
            }
            if (idleRest == "awake" && !away && !QuietNow && t >= nextRemark && t > messageUntil && !spec.Has("noRemark"))
            {
                if (!voiceBusy && (chatter == "off" || muted || nextYap - t > 6 || voicePack.Count == 0))
                {
                    string kind = spec.remark;
                    if (kind == "idle" && unlockPools.Count > 0 && rng.NextDouble() < 0.25) kind = unlockPools[rng.Next(unlockPools.Count)];
                    if (kind == "idle" && timeOfDay && rng.NextDouble() < 0.2) { int hour = DateTime.Now.Hour; if (hour < 5) kind = "latenight"; else if (hour < 10) kind = "morning"; }
                    if (!ShowRemark(kind)) nextRemark = t + 20;
                }
            }

            // Pose and animation for the current state, from its behaviours.json entry.
            var s = spec;
            phase = t - stateStart;
            int? pose = s.PoseAt(phase);
            int animPose = -1; curAnimPose = -1;
            if (newSprites && animPoses.TryGetValue(state, out animPose)) { pose = animPose; curAnimPose = animPose; }
            bool moodPose = s.Has("mood") && !voiceBusy;
            if (moodPose && curAnimPose >= 0) { if (mood == "smug") pose = curAnimPose = AnimPose("smug", animPose); else if (mood == "irritated") pose = curAnimPose = AnimPose("rivalglare", animPose); }
            else if (moodPose && s.timeline == null) { if (mood == "smug") pose = 5; else if (mood == "irritated") pose = 2; }
            if (facialMotion && s.Has("blink"))
            {
                if (t >= nextBlink) { blinkUntil = t + 0.14; nextBlink = t + 2.5 + rng.NextDouble() * 3.5; }
                if (t < blinkUntil && (state != "caught" || phase > 0.7))
                {
                    int closed;
                    if (curAnimPose < 0) pose = 13;
                    else if (blinkPoses.TryGetValue(curAnimPose, out closed)) pose = closed;
                }
            }
            musicFrameActive = false;
            if (musicPoseStart >= 0 && (pose == 12 || pose == 13) && MusicGroove) { pose = MusicPose(t); musicFrameActive = true; }
            int[] eyes;
            if (eyesFollow && pose != null && eyePoses.TryGetValue(pose.Value, out eyes) && !voiceBusy)
            {
                int look = EyeDirection(cursorX, cursorY);
                if (look >= 0) pose = eyes[look];
            }
            if (pose != null) SetPose(pose.Value);
            scale.ScaleX = direction; scale.ScaleY = 1; rotation.Angle = 0;
            shift.X = 0; shift.Y = Math.Sin(t * 2) * 5; sprite.Opacity = 1;
            area = WorkArea();
            string stateBefore = state;
            Action path;
            if (s.path != "" && paths.TryGetValue(s.path, out path)) path();
            if (state != stateBefore) return; // the path changed state
            if (s.clampTarget)
            {
                targetX = Math.Max(area.Left, Math.Min(targetX, area.Right - w.Width));
                targetY = Math.Max(area.Top, Math.Min(targetY, area.Bottom - w.Height));
            }
            if (s.speed != null)
            {
                double dx = targetX - w.Left, dy = targetY - w.Top, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 3)
                {
                    double step = Math.Min(distance, s.speed.Value * dt);
                    w.Left += dx / distance * step; w.Top += dy / distance * step;
                    if (step > 0.05) { travelFxUntil = t + 0.12; travelFxDirection = dx >= 0 ? 1.0 : -1.0; }
                    if (Math.Abs(dx) > 4) direction = dx < 0 ? 1.0 : -1.0;
                }
            }
            effect.Opacity = 1;
            Action motion;
            if (motions.TryGetValue(s.motion, out motion)) motion();
            ApplyMusicGroove(t);
            ApplyJuice(t, dt);
            if (voiceBusy && !s.Has("noWobble")) scale.ScaleY += Math.Sin(t * 16) * 0.015;
            if (moodPose && mood == "bored") rotation.Angle = -3;
            if (state == "pounce" && phase > 0.12 && CursorContact()) { attackContact = true; until = t; }
            ApplySpeechPose();
            ClampPet();
            UpdateShadow(spec.Has("grounded") && (state != "windowperch" || perchArrived), state == "windowperch");
            if (!spec.Has("noFace"))
            {
                double faceDelta = cursorX - (w.Left + w.Width / 2);
                if (faceDelta < -6) direction = 1.0; else if (faceDelta > 6) direction = -1.0;
                scale.ScaleX = Math.Abs(scale.ScaleX) * direction;
            }
        }

        // Busy mode: quietly in the corner, cycling through a few calm activities.
        void BusyFrame(double now)
        {
            lastInteraction = now;
            var a = WorkArea(); w.Left = a.Right - w.Width - 12; w.Top = a.Bottom - w.Height;
            if (now >= busyUntil)
            {
                if (busyBag.Count == 0)
                {
                    AddShuffled(busyBag, new List<string> { "gaming", "dance", "sip", "stretch", "peek", "rest" });
                    if (busyBag.Peek() == busyActivity) busyBag.Enqueue(busyBag.Dequeue());
                }
                busyActivity = busyBag.Dequeue(); busyStart = now; busyUntil = now + rng.Next(9, 19);
            }
            double bp = now - busyStart;
            rotation.Angle = 0; scale.ScaleX = direction; scale.ScaleY = 1;
            shift.X = 0; shift.Y = Math.Sin(bp) * 1.5; sprite.Opacity = 1;
            wall.Visibility = Visibility.Collapsed; effect.Text = "";
            switch (busyActivity)
            {
                case "gaming": SetPose(AnimPose("gaming", 25)); rotation.Angle = Math.Sin(bp * 3) * 2; shift.Y = Math.Sin(bp * 5) * 1.5; break;
                case "dance": SetPose(newSprites && animPoses.ContainsKey("dance") ? animPoses["dance"] : (int)Math.Floor(bp / 0.45) % 2 == 0 ? 11 : 6); rotation.Angle = Math.Sin(bp * 3) * 7; shift.X = Math.Sin(bp * 3) * 5; shift.Y = -Math.Abs(Math.Sin(bp * 3)) * 5; break;
                case "sip": SetPose(AnimPose("sip", 27)); rotation.Angle = -3 + 2 * Math.Sin(bp); break;
                case "stretch": SetPose(AnimPose("stretch", (bp % 2.2) < 1.2 ? 11 : 21)); scale.ScaleY = 1 + 0.025 * Math.Sin(bp * 1.5); break;
                case "peek": SetPose(AnimPose("peek", 7)); rotation.Angle = Math.Sin(bp) * 3; break;
                case "rest": SetPose(AnimPose("taskbar", 5)); break;
            }
            UpdateShadow(true, false);
        }
    }
}

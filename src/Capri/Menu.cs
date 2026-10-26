using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace Capri
{
    // Voice library, context menu and tray icon.
    partial class Pet
    {
        public ContextMenu menu;
        List<KeyValuePair<MenuItem, Func<bool>>> menuSync = new List<KeyValuePair<MenuItem, Func<bool>>>();
        public WF.NotifyIcon tray; IntPtr trayIconHandle = IntPtr.Zero;
        public bool voiceChooserOpen;
        MenuItem hideItem, hotkeyItem, moodItem, rivalItem, snoozeItem, friendItem, focusStopItem, updateItem;

        public MenuItem NewMenuEntry(ItemsControl parent, string header, Action onClick = null, Func<bool> isChecked = null)
        {
            var item = new MenuItem { Header = header };
            if (isChecked != null) { item.IsCheckable = true; menuSync.Add(new KeyValuePair<MenuItem, Func<bool>>(item, isChecked)); }
            if (onClick != null) item.Click += (s, e) => onClick();
            parent.Items.Add(item);
            return item;
        }
        MenuItem NewSubMenu(ItemsControl parent, string header) { var item = new MenuItem { Header = header }; parent.Items.Add(item); return item; }
        static void Separator(ItemsControl parent) => parent.Items.Add(new Separator());

        TextBlock NewMenuSlider(ItemsControl parent, string label, double min, double max, double tick, double value, Action<double, TextBlock> onChange)
        {
            var panel = new StackPanel { Width = 210 };
            var text = new TextBlock { Text = label }; panel.Children.Add(text);
            var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = tick, IsSnapToTickEnabled = true, Value = value, Margin = new Thickness(4, 10, 4, 10) };
            panel.Children.Add(slider);
            slider.ValueChanged += (s, e) => onChange(slider.Value, text);
            parent.Items.Add(new MenuItem { StaysOpenOnClick = true, Header = panel });
            return text;
        }

        // Toggles a saved setting and stores it.
        Action Toggle(string field, Action after = null) => () => { SetBoolSetting(field, !GetBoolSetting(field)); after?.Invoke(); SaveSettings(); };
        Func<bool> Is(string field) => () => GetBoolSetting(field);
        Action Act(Action a) => () => Defer(a);
        Action State(string id) => () => Defer(() => { PrepareManualAction(); routine.Clear(); BeginState(id); });

        void MenuSlabResize(object sender, SizeChangedEventArgs e)
        {
            var host = (Grid)sender;
            foreach (var c in host.Children)
                if (c is Canvas cv && cv.Children.Count > 0 && cv.Children[0] is System.Windows.Shapes.Path p) { p.Data = CutCorners(host.ActualWidth, host.ActualHeight, 14); break; }
        }

        void BuildMenu()
        {
            menu = new ContextMenu();
            var title = new MenuItem { Header = "CAPRI  //  NEURAL LINK", IsEnabled = false, Foreground = Brushes.Yellow, FontWeight = FontWeights.Bold };
            menu.Items.Add(title);
            NewMenuEntry(menu, "Pause", () => SetPaused(!paused), () => paused);
            NewMenuEntry(menu, "Busy, Capri (quiet corner)", () => SetBusyMode(!busyMode), () => busyMode);
            NewMenuEntry(menu, "Mute", () => SetMuted(!muted), () => muted);
            hideItem = NewMenuEntry(menu, "Hide (tray icon brings her back)", Act(() => { SetHidden(true); try { tray?.ShowBalloonTip(4000, "Capri", "Hiding. Double-click the tray icon to bring me back.", WF.ToolTipIcon.Info); } catch { } }));
            Separator(menu);

            var interact = NewSubMenu(menu, "Interact");
            NewMenuEntry(interact, "Do something!", Act(() => { PrepareManualAction(); routine.Clear(); ChooseState(); }));
            NewMenuEntry(interact, "Pet her (Ctrl-click)", Act(InvokeHeadPat));
            NewMenuEntry(interact, "Play catch me (30 s)", Act(StartCatchGame));
            NewMenuEntry(interact, "Toy ball (throw it, she fetches)", Act(() => SetBallShown(!ballShown)), () => ballShown);
            NewMenuEntry(interact, "Chat with her...", Act(ShowChatWindow));
            NewMenuEntry(interact, "Reminders...", Act(ShowReminderWindow));
            NewMenuEntry(interact, "Leave a sticky note", Act(LeaveNote));
            var focusMenu = NewSubMenu(interact, "Focus timer");
            foreach (var minutes in new[] { 25, 45, 60 }) { int m = minutes; NewMenuEntry(focusMenu, m + " minutes", Act(() => StartFocus(m))); }
            focusStopItem = NewMenuEntry(focusMenu, "Stop focusing", Act(() => { if (focusActive) SetBusyMode(false); else EndFocusBreak(false); }));
            Separator(focusMenu);
            NewMenuEntry(focusMenu, "Short break after focus", Toggle("focusBreaks", () => { if (!focusBreaks) EndFocusBreak(false); }), Is("focusBreaks"));
            NewMenuEntry(interact, "Possess a window", Act(() => InvokePossession(true)));
            NewMenuEntry(interact, "Upgrade my cursor", Act(() => StartCursorRepair(true)));
            NewMenuEntry(interact, "Use her own cursor", Act(() => StartHerCursor(true)));
            NewMenuEntry(interact, "Peek from behind a window", State("peekbehind"));
            snoozeItem = NewMenuEntry(interact, "Snooze last reminder (10 min)", Act(() => SnoozeReminder(10)));
            friendItem = NewMenuEntry(interact, "Invite a friend (second Capri)", Act(() => { if (friendPresent) StopFriendCapri(); else StartFriendCapri(); }));
            NewMenuEntry(interact, "Cartoon swat (Shift-click)", Act(InvokeSwat));
            NewMenuEntry(interact, "Say a line now", Act(() => { PrepareManualAction(); voiceNext = 0; PlayVoice("random", true); }));
            NewMenuEntry(interact, "Attack cursor (Blackwall)", Act(() => { PrepareManualAction(); routine.Clear(); chase = true; blackwallEnabled = true; caughtCooldown = Now + 12; BeginState("watch"); }));
            NewMenuEntry(interact, "Sit on the active window", Act(() =>
            {
                PrepareManualAction(); routine.Clear();
                if (FindPerchWindow() == null) ShowStatus("No window to sit on. Click one first, and leave room above it."); else BeginState("windowperch");
            }));
            NewMenuEntry(interact, "Perch at taskbar", State("taskbar"));
            NewMenuEntry(interact, "Fake sabotage / repair", State("sabotage"));

            var animations = NewSubMenu(menu, "Animations");
            var groups = new Dictionary<string, MenuItem>();
            foreach (var id in specOrder)
            {
                var s = specs[id]; if (s.menu == "") continue;
                MenuItem group;
                if (!groups.TryGetValue(s.menu, out group)) { group = NewSubMenu(animations, s.menu); groups[s.menu] = group; }
                NewMenuEntry(group, s.title, State(s.id));
            }
            Separator(animations);
            routineMenu = NewSubMenu(animations, "Little routines");
            foreach (var name in routineOrder.ToList()) { var n = name; NewMenuEntry(routineMenu, n, () => StartRoutineManual(n)); }
            NewMenuEntry(animations, "Stop routine", State("float"));

            NewMenuEntry(menu, "Settings window...", Act(ShowSettingsWindow));
            var settings = NewSubMenu(menu, "Settings");
            var voiceMenu = NewSubMenu(settings, "Voice and subtitles");
            foreach (var entry in new[] { new[] { "English", "en" }, new[] { "Japanese", "ja" } })
            {
                var code = entry[1];
                NewMenuEntry(voiceMenu, entry[0], () => { StopVoice(); language = code; lastClip = ""; voiceNext = 0; ScheduleYap(); SaveSettings(); }, () => language == code);
            }
            Separator(voiceMenu);
            var volumeMenu = NewSubMenu(voiceMenu, "Volume");
            foreach (var level in new[] { 25, 50, 75, 100 }) { int l = level; NewMenuEntry(volumeMenu, l + "%", () => { SetVolume(l / 100.0); SaveSettings(); }, () => (int)Math.Round(volume * 100) == l); }
            var chatterMenu = NewSubMenu(voiceMenu, "Random yapping");
            foreach (var entry in new[] { new[] { "Off (reactions only)", "off" }, new[] { "Quiet: 100-180 seconds", "quiet" }, new[] { "Normal: 45-90 seconds", "normal" }, new[] { "Chatty: 18-35 seconds", "chatty" } })
            {
                var code = entry[1];
                NewMenuEntry(chatterMenu, entry[0], () => { chatter = code; ScheduleYap(); SaveSettings(); }, () => chatter == code);
            }
            NewMenuEntry(voiceMenu, "Speech subtitles", () =>
            {
                subtitles = !subtitles;
                if (subtitles && voiceBusy) { words.Text = activeCaption; bubble.Visibility = Visibility.Visible; subtitleUntil = double.PositiveInfinity; messageUntil = double.PositiveInfinity; }
                else { subtitleUntil = 0; messageUntil = 0; bubble.Visibility = Visibility.Collapsed; }
                SaveSettings();
            }, () => subtitles);
            NewMenuEntry(voiceMenu, "Character remarks", () =>
            {
                remarksEnabled = !remarksEnabled;
                if (!voiceBusy && Now >= subtitleUntil) { bubble.Visibility = Visibility.Collapsed; messageUntil = 0; }
                nextRemark = Now + 5; SaveSettings();
            }, () => remarksEnabled);
            NewMenuEntry(voiceMenu, "Read chat replies aloud", Toggle("chatVoice"), Is("chatVoice"));
            Separator(voiceMenu);
            NewMenuEntry(voiceMenu, "Voice library...", Act(ShowVoiceLibrary));

            var appearance = NewSubMenu(settings, "Appearance");
            var sizeMenu = NewSubMenu(appearance, "Size");
            NewMenuSlider(sizeMenu, "Size: " + (int)petSize + "%", 65, 160, 5, petSize, (v, label) => { RegisterInteraction(); SetPetSize(v); label.Text = "Size: " + (int)petSize + "%"; });
            NewMenuEntry(appearance, "Shadow when perched", Toggle("shadowEnabled"), Is("shadowEnabled"));
            NewMenuEntry(appearance, "Eyes follow the cursor", Toggle("eyesFollow"), Is("eyesFollow"));
            NewMenuEntry(appearance, "Talking mouth", Toggle("talkingMouth"), Is("talkingMouth"));
            NewMenuEntry(appearance, "New per-animation sprites", Toggle("newSprites"), Is("newSprites"));
            NewMenuEntry(appearance, "Smooth pose changes", Toggle("smoothPoses"), Is("smoothPoses"));
            NewMenuEntry(appearance, "Animation effects (sparkles, z's, confetti)", Toggle("effectsEnabled"), Is("effectsEnabled"));
            NewMenuEntry(appearance, "Idle blinking", Toggle("facialMotion"), Is("facialMotion"));
            NewMenuEntry(appearance, "Blackwall effects", Toggle("blackwallEnabled"), Is("blackwallEnabled"));
            NewMenuEntry(appearance, "Sound effects (swat, head pat)", Toggle("sfxEnabled", () => { if (!sfxEnabled) sfxPlayer.Close(); }), Is("sfxEnabled"));

            var behaviour = NewSubMenu(settings, "Behaviour");
            NewMenuEntry(behaviour, "Cursor chasing", () =>
            {
                chase = !chase;
                if (!chase) { nextRetry = double.PositiveInfinity; if (spec.Has("needsChase")) BeginState("float"); }
                autoBag.Clear(); until = 0;
                ShowStatus("Cursor chasing " + (chase ? "on." : "off.")); SaveSettings();
            }, () => chase);
            NewMenuEntry(behaviour, "Cursor knockback (moves your pointer)", () => cursorKnockback = !cursorKnockback, () => cursorKnockback);
            NewMenuEntry(behaviour, "Sit on windows", Toggle("windowPerch"), Is("windowPerch"));
            NewMenuEntry(behaviour, "Throwing physics", Toggle("throwPhysics"), Is("throwPhysics"));
            NewMenuEntry(behaviour, "Time-of-day moods", Toggle("timeOfDay"), Is("timeOfDay"));
            NewMenuEntry(behaviour, "Quiet corner during Do Not Disturb / Focus", Toggle("dndAware", () => nextDndCheck = 0), Is("dndAware"));
            NewMenuEntry(behaviour, "Possess windows sometimes (shakes them a little)", Toggle("possessWindows"), Is("possessWindows"));
            NewMenuEntry(behaviour, "Glitch trail on the cursor sometimes", Toggle("cursorRepairs"), Is("cursorRepairs"));
            NewMenuEntry(behaviour, "Battery reactions (laptops)", Toggle("batteryReactions"), Is("batteryReactions"));
            NewMenuEntry(behaviour, "Bob to the real beat (listens to your PC's sound)", Toggle("beatSync"), Is("beatSync"));
            NewMenuEntry(behaviour, "React to music", Toggle("musicEnabled", () => { if (musicEnabled && !musicAvailable) ShowStatus("Music reactions are not available here: " + musicError); }), Is("musicEnabled"));
            NewMenuEntry(behaviour, "Notice which app I'm using (stays on this PC)", Toggle("appAware", () => { appCategory = ""; appAnnounced = ""; if (appAware) ShowStatus("I'll only look at which app is in front. Nothing leaves this PC.", 5); }), Is("appAware"));
            hotkeyItem = NewMenuEntry(behaviour, "Summon hotkey", () =>
            {
                hotkeyEnabled = !hotkeyEnabled; RegisterSummonHotkey(); SaveSettings();
                if (hotkeyEnabled) { if (hotkeyOk) ShowStatus(tuning.hotkey + " calls me over. Press it again next to me to hide me.", 5); else ShowProblem("Hotkey not set: " + hotkeyError); }
            }, () => hotkeyEnabled);
            Separator(behaviour);
            var sleepMenu = NewSubMenu(behaviour, "Sleep after inactivity");
            foreach (var minutes in new[] { 2, 5, 10, 0 }) { double s = minutes * 60; NewMenuEntry(sleepMenu, minutes == 0 ? "Never" : minutes + " minutes", () => { RegisterInteraction(); idleSleepAfter = s; SaveSettings(); }, () => idleSleepAfter == s); }
            var breakMenu = NewSubMenu(behaviour, "Break reminders");
            foreach (var minutes in new[] { 0, 25, 50, 90 }) { int m = minutes; NewMenuEntry(breakMenu, m == 0 ? "Off" : "Every " + m + " minutes of screen time", () => { breakMinutes = m; activeSince = Now; SaveSettings(); }, () => breakMinutes == m); }
            var quietMenu = NewSubMenu(behaviour, "Quiet hours (no yapping, attacks or tricks)");
            foreach (var q in QuietHourChoices) { var code = q; NewMenuEntry(quietMenu, QuietHoursLabel(code), () => { quietHours = code; SaveSettings(); }, () => quietHours == code); }
            var fullscreenMenu = NewSubMenu(behaviour, "When a fullscreen app is open");
            foreach (var entry in new[] { new[] { "Hide until it closes", "hide" }, new[] { "Sit quietly in the corner", "busy" }, new[] { "Stay on top", "ignore" } })
            { var mode = entry[1]; NewMenuEntry(fullscreenMenu, entry[0], () => SetFullscreenMode(mode), () => fullscreenMode == mode); }
            var ratesMenu = NewSubMenu(behaviour, "Activity frequency");
            foreach (var entry in new[] { new[] { "Attacks", "attacks" }, new[] { "Wandering", "wandering" }, new[] { "Resting", "resting" }, new[] { "Antics", "antics" }, new[] { "Talking", "talking" }, new[] { "Rare antics", "rare" } })
            {
                var key = entry[1];
                var rateMenu = NewSubMenu(ratesMenu, entry[0]);
                NewMenuSlider(rateMenu, activityRates[key] + "%  (0 = off, 100 = normal)", 0, 200, 25, activityRates[key], (v, label) =>
                {
                    activityRates[key] = (int)v; label.Text = (int)v + "%  (0 = off, 100 = normal)";
                    autoBag.Clear(); if (key == "talking") ScheduleYap(); if (key == "attacks" && v == 0) nextRetry = double.PositiveInfinity;
                });
            }
            Separator(settings);
            NewMenuEntry(settings, "Start with Windows", () => SetStartWithWindows(!StartsWithWindows), () => StartsWithWindows);

            var debug = NewSubMenu(menu, "Debug and help");
            NewMenuEntry(debug, "Movement status", () =>
            {
                double idle = 0; try { idle = CapriDesktop.IdleSeconds(); } catch { }
                string beat; try { beat = CapriBeat.Running ? string.Format("{0:0} BPM, confidence {1:0.00}", CapriBeat.Bpm, CapriBeat.Confidence) : "not listening"; } catch { beat = "unavailable"; }
                MessageBox.Show("State: " + state + "\nPaused: " + paused + "\nBusy mode: " + busyMode + "\nHidden: " + isHidden + " (fullscreen: " + hiddenFullscreen + ")\nSleep: " + idleRest + " " + sleepReason +
                    "\nDragging: " + dragging + "\nVoice playing: " + voiceBusy + "\nLive volume: " + liveVolume + "\nMonitors: " + AllWorkAreas().Count + "\nYou idle for: " + (int)idle + " s\nBeat: " + beat +
                    "\nFrame time: " + tickMs + " ms\nVersion: " + appVersion + " (native)", "Capri movement status");
            });
            NewMenuEntry(debug, "Our history", Act(ShowHistoryWindow));
            NewMenuEntry(debug, "Run self-test", Act(StartSelfTestRun));
            NewMenuEntry(debug, "Reload tuning.json", () =>
            {
                ImportTuning(); RegisterSummonHotkey();
                if (tuningError != "") { ShowProblem("tuning.json has a problem: " + tuningError); tuningError = ""; } else ShowStatus("Tuning reloaded.");
            });
            NewMenuEntry(debug, "Check for updates", Act(() => { CheckForUpdates(true); ShowStatus("Checking for updates..."); }));
            updateItem = NewMenuEntry(debug, "Install update", Act(InstallUpdate));
            NewMenuEntry(debug, "Weather now", Act(() => { RequestWeatherReport(); MessageBox.Show(WeatherSummary(), "Capri - weather"); }));
            NewMenuEntry(debug, "Wander now", Act(() =>
            {
                PrepareManualAction(); routine.Clear(); BeginState("wander");
                var a = WorkArea(); double span = Math.Max(0.0, a.Width - w.Width);
                targetX = w.Left < a.Left + span / 2 ? a.Left + span * 0.85 : a.Left + span * 0.15; targetY = w.Top;
                until = Now + Math.Abs(targetX - w.Left) / 65 + 2; caughtCooldown = until;
            }));
            NewMenuEntry(debug, "Test cursor movement (75 px)", Act(() =>
            {
                PrepareManualAction();
                if (MouseButtonsUp) { CapriCursor.Nudge(75, -20); App.Log("cursor-error.log", "test: " + CapriCursor.LastResult); MessageBox.Show(CapriCursor.LastResult, "Capri - cursor test"); }
                else MessageBox.Show("Release the mouse buttons and try the test again.", "Capri - cursor test");
            }));
            Separator(debug);
            NewMenuEntry(debug, "Controls and troubleshooting", () => OpenPath(App.PathOf("README.txt")));
            NewMenuEntry(debug, "Open Capri folder", () => OpenPath(App.Root));
            NewMenuEntry(debug, "Open voice folder", () => OpenPath(App.PathOf(Path.Combine("voices", language))));

            Separator(menu);
            moodItem = new MenuItem { IsEnabled = false }; menu.Items.Add(moodItem);
            rivalItem = new MenuItem { IsEnabled = false }; menu.Items.Add(rivalItem);
            NewMenuEntry(menu, "Quit Capri", () => w.Close());

            menu.Opened += (s, e) =>
            {
                RegisterInteraction();
                foreach (var sync in menuSync) sync.Key.IsChecked = sync.Value();
                moodItem.Header = "Mood: " + mood;
                rivalItem.Header = "Rivalry: " + rivalWins + " catches / " + rivalEscapes + " escapes";
                hideItem.IsEnabled = tray != null;
                hotkeyItem.Header = "Summon hotkey (" + tuning.hotkey + ")";
                snoozeItem.IsEnabled = CanSnooze;
                focusStopItem.IsEnabled = focusActive || onFocusBreak;
                focusStopItem.Header = onFocusBreak && !focusActive ? "Skip the break" : "Stop focusing";
                updateItem.Header = updateVersion != null ? "Install update (" + updateVersion + ")" : "Install update (none found yet; version " + appVersion + ")";
                updateItem.IsEnabled = updateVersion != null;
                friendItem.Header = App.Friend ? "(I'm the friend: Quit sends me home)" : friendPresent ? "Send her friend home" : "Invite a friend (second Capri)";
                friendItem.IsEnabled = !App.Friend;
            };
            menu.Closed += (s, e) => { RegisterInteraction(); SaveSettings(); };
            try
            {
                using (var reader = System.Xml.XmlReader.Create(App.PathOf("menu-theme.xaml")))
                {
                    var theme = (ResourceDictionary)XamlReader.Load(reader);
                    menu.Resources.MergedDictionaries.Add(theme);
                    menu.Style = theme[typeof(ContextMenu)] as Style;
                    // size the cut-corner slab to the menu once it's laid out
                    menu.Opened += (s, e) =>
                    {
                        var slab = menu.Template?.FindName("Slab", menu) as System.Windows.Shapes.Path;
                        var host = (slab?.Parent as FrameworkElement)?.Parent as FrameworkElement;
                        if (slab == null || host == null) return;
                        slab.Data = CutCorners(host.ActualWidth, host.ActualHeight, 14);
                        host.SizeChanged -= MenuSlabResize; host.SizeChanged += MenuSlabResize;
                    };
                }
            }
            catch (Exception e) { App.Log("runtime-error.log", "menu theme: " + e.Message); }
            w.ContextMenu = menu;
            EnableEarlierUnlocks();
        }

        public static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        // ---------------------------------------------------------------------------------
        // Voice library (also where clips are switched on or off for random chatter)
        void ShowVoiceLibrary()
        {
            if (voiceChooserOpen) return;
            StopVoice(); RegisterInteraction(); voiceChooserOpen = true;
            var dialog = new Window { Title = "Capri // Voice library", Width = 650, Height = 500, Owner = w, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = HexBrush("#111722") };
            var stack = new StackPanel { Margin = new Thickness(16) }; dialog.Content = stack;
            stack.Children.Add(new TextBlock { Foreground = Brushes.Yellow, FontSize = 20, Text = language == "ja" ? "JAPANESE // SELECT A LINE" : "ENGLISH // SELECT A LINE" });
            stack.Children.Add(new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Text = "Select a line, then Play selected. [ON] lines can come up in random chatter; [OFF] lines can still be previewed." });
            var list = new ListBox { Height = 245 };
            foreach (var clip in voicePack.Where(c => c.language == language))
                list.Items.Add(new ListBoxItem { Tag = clip, Content = (approvedVoices.Contains(clip.Key) ? "[ON] " : "[OFF] ") + clip.subtitle });
            stack.Children.Add(list);
            var status = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8), Text = list.Items.Count + " clips available." + (voicesMissing > 0 ? " (" + voicesMissing + " clips are missing from the voices folder.)" : "") };
            stack.Children.Add(status);
            var buttons = new WrapPanel(); stack.Children.Add(buttons);
            foreach (var action in new[] { "Play selected", "Stop", "Enable selected", "Disable selected", "Enable all listed", "Enable established only", "Open voice folder" })
            {
                var button = new Button { Content = action, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 5, 8, 5) };
                var a = action;
                button.Click += (s, e) =>
                {
                    if (a == "Stop") { StopVoice(); status.Text = "Stopped."; return; }
                    if (a == "Open voice folder") { OpenPath(App.PathOf(Path.Combine("voices", language))); return; }
                    if (a == "Enable all listed" || a == "Enable established only")
                    {
                        bool established = a == "Enable established only";
                        foreach (ListBoxItem item in list.Items)
                        {
                            var clip = (VoiceClip)item.Tag; bool on = !established || !clip.needsReview;
                            approvedVoices.Remove(clip.Key); if (on) approvedVoices.Add(clip.Key);
                            item.Content = (on ? "[ON] " : "[OFF] ") + clip.subtitle;
                        }
                        SaveSettings(); ScheduleYap();
                        status.Text = established ? "Only established clips are on for random chatter." : "All listed clips enabled for random chatter, including review clips.";
                        return;
                    }
                    var selected = list.SelectedItem as ListBoxItem;
                    if (selected == null) { status.Text = "Select a line first."; return; }
                    var c2 = (VoiceClip)selected.Tag;
                    if (a == "Play selected")
                    {
                        if (muted) { status.Text = "Voice is muted. Turn off Mute in the Capri menu."; return; }
                        if (volume <= 0) { status.Text = "Volume is zero. Raise it in Settings > Voice > Volume."; return; }
                        StopVoice(); PlayVoice("random", true, c2.id);
                        status.Text = voiceBusy ? "Playing: " + c2.subtitle + " (" + c2.id + ")" : "Could not play: " + lastAudioError;
                        return;
                    }
                    approvedVoices.Remove(c2.Key);
                    if (a == "Enable selected") { approvedVoices.Add(c2.Key); selected.Content = "[ON] " + c2.subtitle; } else selected.Content = "[OFF] " + c2.subtitle;
                    SaveSettings(); ScheduleYap(); status.Text = (string)selected.Content;
                };
                buttons.Children.Add(button);
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            CyberChrome(dialog, "Capri // Voice bank", "clips");
            try { dialog.ShowDialog(); } finally { voiceChooserOpen = false; RegisterInteraction(); SaveSettings(); ScheduleYap(); }
        }

        // ---------------------------------------------------------------------------------
        // Tray icon: always reachable, even when she's asleep in a corner, busy, or hidden.
        System.Drawing.Icon TrayIconImage()
        {
            try { return System.Drawing.Icon.ExtractAssociatedIcon(App.ExePath); } catch { }
            return System.Drawing.SystemIcons.Application;
        }

        void BuildTray()
        {
            try
            {
                tray = new WF.NotifyIcon { Icon = TrayIconImage(), Text = App.Friend ? "Capri (friend)" : "Capri" };
                var trayMenu = new WF.ContextMenuStrip();
                var show = trayMenu.Items.Add("Hide Capri"); show.Click += (s, e) => SetHidden(!isHidden);
                trayMenu.Items.Add("Call her over here").Click += (s, e) => InvokeCallOver();
                trayMenu.Items.Add(new WF.ToolStripSeparator());
                var pause = (WF.ToolStripMenuItem)trayMenu.Items.Add("Pause"); pause.Click += (s, e) => SetPaused(!paused);
                var busy = (WF.ToolStripMenuItem)trayMenu.Items.Add("Busy (quiet corner)"); busy.Click += (s, e) => SetBusyMode(!busyMode);
                var mute = (WF.ToolStripMenuItem)trayMenu.Items.Add("Mute"); mute.Click += (s, e) => SetMuted(!muted);
                trayMenu.Items.Add(new WF.ToolStripSeparator());
                trayMenu.Items.Add("Remind me...").Click += (s, e) => ShowReminderWindow();
                trayMenu.Items.Add("Chat with her...").Click += (s, e) => ShowChatWindow();
                var focus = trayMenu.Items.Add("Focus 25 minutes"); focus.Click += (s, e) => Defer(() => { if (focusActive) SetBusyMode(false); else StartFocus(25); });
                trayMenu.Items.Add("Leave a sticky note").Click += (s, e) => Defer(LeaveNote);
                trayMenu.Items.Add("Settings...").Click += (s, e) => ShowSettingsWindow();
                trayMenu.Items.Add("Capri menu...").Click += (s, e) =>
                {
                    SetHidden(false); try { w.Activate(); } catch { }
                    menu.PlacementTarget = w; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
                };
                trayMenu.Items.Add("Quit").Click += (s, e) => w.Close();
                trayMenu.Opening += (s, e) =>
                {
                    show.Text = isHidden ? "Show Capri" : "Hide Capri";
                    pause.Checked = paused; busy.Checked = busyMode; mute.Checked = muted;
                    focus.Text = focusActive ? "Stop focusing" : "Focus 25 minutes";
                };
                tray.ContextMenuStrip = trayMenu;
                tray.DoubleClick += (s, e) => SetHidden(!isHidden);
                tray.Visible = true;
            }
            catch (Exception e)
            {
                tray = null; w.ShowInTaskbar = true;
                App.Log("runtime-error.log", "Tray icon unavailable: " + e.Message);
            }
        }
    }
}

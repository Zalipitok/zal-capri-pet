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
            // The everyday things first; everything else is in the Settings window.
            NewMenuEntry(menu, "Chat with her...", Act(ShowChatWindow));
            NewMenuEntry(menu, "Reminders...", Act(ShowReminderWindow));
            snoozeItem = NewMenuEntry(menu, "Snooze last reminder (10 min)", Act(() => SnoozeReminder(10)));
            NewMenuEntry(menu, "Leave a sticky note", Act(LeaveNote));
            var focusMenu = NewSubMenu(menu, "Focus timer");
            foreach (var minutes in new[] { 25, 45, 60 }) { int m = minutes; NewMenuEntry(focusMenu, m + " minutes", Act(() => StartFocus(m))); }
            focusStopItem = NewMenuEntry(focusMenu, "Stop focusing", Act(() => { if (focusActive) SetBusyMode(false); else EndFocusBreak(false); }));
            Separator(focusMenu);
            NewMenuEntry(focusMenu, "Short break after focus", Toggle("focusBreaks", () => { if (!focusBreaks) EndFocusBreak(false); }), Is("focusBreaks"));
            Separator(menu);

            NewMenuEntry(menu, "Stay here", Act(() => SetStayPut(!stayPut)), () => stayPut);
            NewMenuEntry(menu, "Let clicks through her", Act(() => SetClickThrough(true)));
            NewMenuEntry(menu, "Busy (quiet corner)", () => SetBusyMode(!busyMode), () => busyMode);
            NewMenuEntry(menu, "Pause", () => SetPaused(!paused), () => paused);
            NewMenuEntry(menu, "Mute", () => SetMuted(!muted), () => muted);
            hideItem = NewMenuEntry(menu, "Hide (tray icon brings her back)", Act(() => { SetHidden(true); try { tray?.ShowBalloonTip(4000, "Capri", "Hiding. Double-click the tray icon to bring me back.", WF.ToolTipIcon.Info); } catch { } }));
            Separator(menu);

            var play = NewSubMenu(menu, "Play");
            NewMenuEntry(play, "Catch me (30 s)", Act(StartCatchGame));
            NewMenuEntry(play, "Toy ball (throw it, she fetches)", Act(() => SetBallShown(!ballShown)), () => ballShown);
            friendItem = NewMenuEntry(play, "Invite a friend (second Capri)", Act(() => { if (friendPresent) StopFriendCapri(); else StartFriendCapri(); }));
            NewMenuEntry(play, "Pet her (Ctrl-click)", Act(InvokeHeadPat));
            NewMenuEntry(play, "Cartoon swat (Shift-click)", Act(InvokeSwat));

            var interact = NewSubMenu(menu, "Tricks");
            NewMenuEntry(interact, "Do something!", Act(() => { PrepareManualAction(); routine.Clear(); ChooseState(); }));
            NewMenuEntry(interact, "Say a line now", Act(() => { PrepareManualAction(); voiceNext = 0; PlayVoice("random", true); }));
            NewMenuEntry(interact, "Sit on a window (or drop her on a title bar)", Act(() =>
            {
                PrepareManualAction(); routine.Clear();
                if (FindPerchWindow() == null) ShowStatus("No window to sit on. Click one first, and leave room above it."); else BeginState("windowperch");
            }));
            NewMenuEntry(interact, "Perch at taskbar", State("taskbar"));
            NewMenuEntry(interact, "Peek from behind a window", State("peekbehind"));
            NewMenuEntry(interact, "Use her own cursor", Act(() => StartHerCursor(true)));
            NewMenuEntry(interact, "Upgrade my cursor", Act(() => StartCursorRepair(true)));
            NewMenuEntry(interact, "Possess a window", Act(() => InvokePossession(true)));
            NewMenuEntry(interact, "Attack cursor (Blackwall)", Act(() => { PrepareManualAction(); routine.Clear(); chase = true; blackwallEnabled = true; caughtCooldown = Now + 12; BeginState("watch"); }));
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
            Separator(menu);

            var settingsItem = NewMenuEntry(menu, "Settings...", Act(ShowSettingsWindow)); settingsItem.FontWeight = FontWeights.Bold;
            var sizeMenu = NewSubMenu(menu, "Size");
            NewMenuSlider(sizeMenu, "Size: " + (int)petSize + "%", 65, 160, 5, petSize, (v, label) => { RegisterInteraction(); SetPetSize(v); MarkStayHere(); label.Text = "Size: " + (int)petSize + "%"; });
            NewMenuEntry(menu, "Voice library...", Act(ShowVoiceLibrary));

            var debug = NewSubMenu(menu, "Debug and help");
            NewMenuEntry(debug, "Movement status", () =>
            {
                double idle = 0; try { idle = CapriDesktop.IdleSeconds(); } catch { }
                string beat; try { beat = CapriBeat.Running ? string.Format("{0:0} BPM, confidence {1:0.00}", CapriBeat.Bpm, CapriBeat.Confidence) : "not listening"; } catch { beat = "unavailable"; }
                MessageBox.Show("State: " + state + "\nPaused: " + paused + "\nBusy mode: " + busyMode + (busyMode ? " (" + (focusActive ? "focus timer" : busyByDnd ? "Windows " + CapriQuiet.Source : busyByFullscreen ? "fullscreen app" : "by hand") + ")" : "") + "\nHidden: " + isHidden + " (fullscreen: " + hiddenFullscreen + ")\nSleep: " + idleRest + " " + sleepReason +
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
                snoozeItem.Visibility = CanSnooze ? Visibility.Visible : Visibility.Collapsed;
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
                var stay = (WF.ToolStripMenuItem)trayMenu.Items.Add("Stay here"); stay.Click += (s, e) => SetStayPut(!stayPut);
                var through = (WF.ToolStripMenuItem)trayMenu.Items.Add("Let clicks through her"); through.Click += (s, e) => SetClickThrough(!clickThrough);
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
                    pause.Checked = paused; busy.Checked = busyMode; mute.Checked = muted; stay.Checked = stayPut; through.Checked = clickThrough;
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

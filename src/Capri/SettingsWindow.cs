using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capri
{
    // Settings window: every setting in one tabbed window, built from this list. Not modal, so
    // changes show on Capri right away.
    class SettingDef
    {
        public string tab, type, label, hint = "", unit = "%";
        public Func<object> get; public Action<object> set; public Action click;
        public object[][] options; public double min, max, step; public Func<string> error;
    }

    partial class Pet
    {
        List<SettingDef> settingDefs; string hotkeyChangeError = "";
        Window settingsWindow;

        SettingDef Bool(string tab, string label, Func<bool> get, Action<bool> set, string hint = "") => new SettingDef { tab = tab, type = "bool", label = label, get = () => get(), set = v => set((bool)v), hint = hint };
        SettingDef BoolField(string tab, string label, string field, string hint = "", Action after = null) => Bool(tab, label, () => GetBoolSetting(field), v => { SetBoolSetting(field, v); after?.Invoke(); }, hint);
        SettingDef Choice(string tab, string label, object[][] options, Func<object> get, Action<object> set) => new SettingDef { tab = tab, type = "choice", label = label, options = options, get = get, set = set };
        SettingDef SliderDef(string tab, string label, double min, double max, double step, Func<double> get, Action<double> set) => new SettingDef { tab = tab, type = "slider", label = label, min = min, max = max, step = step, get = () => get(), set = v => set((double)v) };
        SettingDef Text(string tab, string label, Func<string> get, Action<string> set, string hint = "") => new SettingDef { tab = tab, type = "text", label = label, get = () => get(), set = v => set((string)v), hint = hint };
        SettingDef ButtonDef(string tab, string label, Action click) => new SettingDef { tab = tab, type = "button", label = label, click = click };
        static object[] Opt(string label, object value) => new[] { label, value };

        void InitSettingsWindow()
        {
            settingDefs = new List<SettingDef>
            {
                SliderDef("General", "Size", 65, 160, 5, () => petSize, SetPetSize),
                Bool("General", "Start with Windows", () => StartsWithWindows, SetStartWithWindows),
                Choice("General", "When a fullscreen app is open", new[] { Opt("Hide until it closes", "hide"), Opt("Sit quietly in the corner", "busy"), Opt("Stay on top", "ignore") }, () => fullscreenMode, v => SetFullscreenMode((string)v)),
                BoolField("General", "Quiet corner during Do Not Disturb / Focus", "dndAware", "When Windows Do Not Disturb, Focus or presentation mode is on, she sits quietly in the corner.", () => nextDndCheck = 0),
                BoolField("General", "Check for updates once a day", "autoUpdate", "Asks GitHub whether a newer Capri is out. Debug and help > Install update installs it."),
                Bool("General", "Summon hotkey", () => hotkeyEnabled, v => { hotkeyEnabled = v; RegisterSummonHotkey(); }, "Calls her to the pointer; press again next to her to hide her."),
                new SettingDef { tab = "General", type = "text", label = "Hotkey keys", get = () => tuning.hotkey, set = v => hotkeyChangeError = ChangeHotkey((string)v), error = () => hotkeyChangeError, hint = "For example Ctrl+Alt+C, Ctrl+Shift+K or Alt+F9. Saved in tuning.json." },
                Choice("General", "Quiet hours (no yapping, attacks or tricks)", QuietHourChoices.Select(q => Opt(QuietHoursLabel(q), q)).ToArray(), () => quietHours, v => quietHours = (string)v),
                Choice("General", "Sleep after inactivity", new[] { Opt("2 minutes", 120.0), Opt("5 minutes", 300.0), Opt("10 minutes", 600.0), Opt("Never", 0.0) }, () => idleSleepAfter, v => idleSleepAfter = (double)v),
                Choice("General", "Break reminders", new[] { Opt("Off", 0), Opt("Every 25 minutes", 25), Opt("Every 50 minutes", 50), Opt("Every 90 minutes", 90) }, () => breakMinutes, v => { breakMinutes = (int)v; activeSince = Now; }),
                Choice("Voice", "Language", new[] { Opt("English", "en"), Opt("Japanese", "ja") }, () => language, v => { StopVoice(); language = (string)v; lastClip = ""; voiceNext = 0; ScheduleYap(); }),
                SliderDef("Voice", "Volume", 0, 100, 5, () => Math.Round(volume * 100), v => SetVolume(v / 100.0)),
                Bool("Voice", "Mute", () => muted, SetMuted),
                Choice("Voice", "Random yapping", new[] { Opt("Off (reactions only)", "off"), Opt("Quiet: 100-180 seconds", "quiet"), Opt("Normal: 45-90 seconds", "normal"), Opt("Chatty: 18-35 seconds", "chatty") }, () => chatter, v => { chatter = (string)v; ScheduleYap(); }),
                BoolField("Voice", "Speech subtitles", "subtitles"),
                BoolField("Voice", "Character remarks", "remarksEnabled"),
                BoolField("Voice", "Talking mouth", "talkingMouth"),
                ButtonDef("Voice", "Open voice library...", () => Defer(ShowVoiceLibrary)),
                BoolField("Look", "Eyes follow the cursor (original poses only)", "eyesFollow"),
                BoolField("Look", "New per-animation sprites", "newSprites", "Every animation has its own drawing. Turn off for the original poses."),
                BoolField("Look", "Smooth pose changes", "smoothPoses"),
                BoolField("Look", "Animation effects", "effectsEnabled", "Sparkles, z's, confetti, dust, afterimages when she zooms, and a little bounce when she lands."),
                BoolField("Look", "Shadow when perched", "shadowEnabled"),
                BoolField("Look", "Idle blinking (original poses only)", "facialMotion"),
                BoolField("Look", "Blackwall effects", "blackwallEnabled"),
                BoolField("Look", "Sound effects", "sfxEnabled", "Little sounds for head pats, swats, clicks, throws and landings, sitting, peeking, the ball, her cursor and unlocks. Reminders always chime.", () => { if (!sfxEnabled) sfxPlayer.Close(); }),
                Bool("Behaviour", "Cursor chasing", () => chase, v => { chase = v; if (!v) { nextRetry = double.PositiveInfinity; if (spec.Has("needsChase")) BeginState("float"); } autoBag.Clear(); }),
                Bool("Behaviour", "Cursor knockback (moves your pointer)", () => cursorKnockback, v => cursorKnockback = v, "Not saved: always starts off."),
                Bool("Behaviour", "Stay where I put her", () => stayPut, SetStayPut, "She still moves, talks and reacts, but stays in one spot. Drag her to move the spot."),
                BoolField("Behaviour", "Keep off the window I'm working in", "avoidWork", "She moves aside when she's on top of the window you're using (its title bar is still fair game)."),
                BoolField("Behaviour", "Sit on windows", "windowPerch"),
                BoolField("Behaviour", "Throwing physics", "throwPhysics"),
                BoolField("Behaviour", "Time-of-day moods", "timeOfDay"),
                BoolField("Behaviour", "Possess windows sometimes", "possessWindows", "Shakes the window you are using a few pixels for two seconds with a red flicker, then puts it back exactly where it was."),
                BoolField("Behaviour", "Her own cursor sometimes", "herCursorEnabled", "She flies a neon pointer of her own around your screen and 'clicks' on things. Only a picture: your cursor and apps are never touched."),
                BoolField("Behaviour", "Glitch trail on the cursor sometimes", "cursorRepairs", "A picture follows the pointer for a minute. The real cursor is never changed."),
                BoolField("Behaviour", "Battery reactions (laptops)", "batteryReactions"),
                SliderDef("Behaviour", "Attacks", 0, 200, 25, () => activityRates["attacks"], v => { activityRates["attacks"] = (int)v; autoBag.Clear(); if (v == 0) nextRetry = double.PositiveInfinity; }),
                SliderDef("Behaviour", "Wandering", 0, 200, 25, () => activityRates["wandering"], v => { activityRates["wandering"] = (int)v; autoBag.Clear(); }),
                SliderDef("Behaviour", "Resting", 0, 200, 25, () => activityRates["resting"], v => { activityRates["resting"] = (int)v; autoBag.Clear(); }),
                SliderDef("Behaviour", "Antics", 0, 200, 25, () => activityRates["antics"], v => { activityRates["antics"] = (int)v; autoBag.Clear(); }),
                SliderDef("Behaviour", "Talking", 0, 200, 25, () => activityRates["talking"], v => { activityRates["talking"] = (int)v; ScheduleYap(); }),
                SliderDef("Behaviour", "Rare antics", 0, 200, 25, () => activityRates["rare"], v => activityRates["rare"] = (int)v),
                BoolField("Music and apps", "React to music", "musicEnabled"),
                BoolField("Music and apps", "Bob to the real beat", "beatSync", "Listens to what your PC is playing to find the beat. Nothing is recorded or saved."),
                BoolField("Music and apps", "Notice which app I'm using", "appAware", "Reads only the name and title of the window in front. Nothing leaves this PC.", () => { appCategory = ""; appAnnounced = ""; }),
                ButtonDef("Fun", "Play catch me (30 s)", () => Defer(StartCatchGame)),
                ButtonDef("Fun", "Toy ball (show / put away)", () => Defer(() => SetBallShown(!ballShown))),
                ButtonDef("Fun", "Invite a friend (second Capri)", () => Defer(StartFriendCapri)),
                ButtonDef("Fun", "Reminders...", () => Defer(ShowReminderWindow)),
                ButtonDef("Fun", "Chat with her...", () => Defer(ShowChatWindow)),
                BoolField("Fun", "Let her reply in chat (Claude API)", "chatEnabled", "Uses your own Claude API key (set it in the chat window). What you type, plus the time, the song playing, the weather and your history with her, goes to Anthropic to write her reply."),
                BoolField("Fun", "Read her chat replies aloud", "chatVoice", "Uses a voice installed in Windows (Settings > Time and language > Speech adds more). chatVoiceName in tuning.json picks one."),
                ButtonDef("Fun", "Leave a sticky note", () => Defer(LeaveNote)),
                ButtonDef("Fun", "Focus timer (25 minutes)", () => Defer(() => StartFocus(25))),
                BoolField("Fun", "Short break after focus", "focusBreaks", "After a finished focus session she counts down a " + tuning.focusBreakMinutes + "-minute break (focusBreakMinutes in tuning.json) and chimes when it's over."),
                BoolField("Fun", "Weather reactions", "weatherEnabled", "Checks open-meteo.com about once an hour. Only the city name and its coordinates are sent.", ResetWeather),
                Text("Fun", "City for the weather", () => weatherCity, v => { weatherCity = v.Trim(); ResetWeather(); }, "For example: Istanbul, or Paris, France"),
                Choice("Fun", "Temperature", new[] { Opt("Celsius", "C"), Opt("Fahrenheit", "F") }, () => weatherUnit, v => { weatherUnit = (string)v; ResetWeather(); }),
                ButtonDef("Fun", "Weather now", () => { RequestWeatherReport(); MessageBox.Show(WeatherSummary(), "Capri - weather"); }),
                ButtonDef("Fun", "Our history and unlocks...", () => Defer(ShowHistoryWindow)),
                ButtonDef("Fun", "Run self-test", () => Defer(StartSelfTestRun)),
            };
        }

        public void ShowSettingsWindow()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            var dialog = new Window { Title = "Capri // Settings", Width = 520, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = HexBrush("#111722"), Foreground = Brushes.White };
            var tabs = new TabControl { Margin = new Thickness(0) };
            dialog.Content = tabs;
            var pages = new Dictionary<string, StackPanel>();
            foreach (var def in settingDefs)
            {
                StackPanel stack;
                if (!pages.TryGetValue(def.tab, out stack))
                {
                    stack = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
                    stack.Children.Add(CyberHeading(def.tab));
                    tabs.Items.Add(new TabItem { Header = def.tab, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack } });
                    pages[def.tab] = stack;
                }
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                var d = def;
                switch (def.type)
                {
                    case "bool":
                        {
                            var box = new CheckBox { Content = d.label, Foreground = Brushes.White, IsChecked = (bool)d.get() };
                            box.Click += (s, e) => { d.set(box.IsChecked == true); SaveSettings(); box.IsChecked = (bool)d.get(); };
                            row.Children.Add(box); break;
                        }
                    case "choice":
                        {
                            row.Children.Add(new TextBlock { Text = d.label });
                            var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
                            var current = Convert.ToString(d.get(), System.Globalization.CultureInfo.InvariantCulture);
                            foreach (var option in d.options)
                            {
                                var item = new ComboBoxItem { Content = option[0], Tag = option[1] }; combo.Items.Add(item);
                                if (Convert.ToString(option[1], System.Globalization.CultureInfo.InvariantCulture) == current) combo.SelectedItem = item;
                            }
                            combo.SelectionChanged += (s, e) => { var item = combo.SelectedItem as ComboBoxItem; if (item == null) return; d.set(item.Tag); SaveSettings(); };
                            row.Children.Add(combo); break;
                        }
                    case "slider":
                        {
                            var label = new TextBlock { Text = d.label + ": " + (int)(double)d.get() + d.unit }; row.Children.Add(label);
                            var slider = new Slider { Minimum = d.min, Maximum = d.max, TickFrequency = d.step, IsSnapToTickEnabled = true, Value = (double)d.get(), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Width = 320 };
                            slider.ValueChanged += (s, e) => { d.set(slider.Value); label.Text = d.label + ": " + (int)slider.Value + d.unit; };
                            slider.LostMouseCapture += (s, e) => SaveSettings();
                            row.Children.Add(slider); break;
                        }
                    case "text":
                        {
                            row.Children.Add(new TextBlock { Text = d.label });
                            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Width = 320 };
                            var apply = new Button { Content = "Apply", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
                            DockPanel.SetDock(apply, Dock.Right); line.Children.Add(apply);
                            var tb = new TextBox { Text = (string)d.get(), Padding = new Thickness(3) }; line.Children.Add(tb);
                            apply.Click += (s, e) =>
                            {
                                d.set(tb.Text); SaveSettings();
                                var problem = d.error?.Invoke() ?? "";
                                if (problem != "") MessageBox.Show(dialog, problem, "Capri", MessageBoxButton.OK, MessageBoxImage.Warning);
                                else ShowStatus("Saved.", 2);
                                tb.Text = (string)d.get();
                            };
                            row.Children.Add(line); break;
                        }
                    case "button":
                        {
                            var button = new Button { Content = d.label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5) };
                            button.Click += (s, e) => d.click();
                            row.Children.Add(button); break;
                        }
                }
                if (def.hint != "") row.Children.Add(new TextBlock { Text = def.hint, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 0) });
                stack.Children.Add(row);
            }
            dialog.Closed += (s, e) => { settingsWindow = null; SaveSettings(); RegisterInteraction(); };
            settingsWindow = dialog;
            CyberChrome(dialog, "Capri // Mainframe");
            dialog.Show();
        }

        // Starts a second, silent Capri that runs the self-test.
        public void StartSelfTestRun()
        {
            try
            {
                Process.Start(new ProcessStartInfo(App.ExePath, "--selftest") { UseShellExecute = false });
                ShowStatus("Self-test started. A second me will appear briefly, then show the results.", 5);
            }
            catch (Exception e) { ShowProblem("Could not start the self-test: " + e.Message); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capri
{
    class Reminder { public DateTime at; public string text; public string repeat = ""; } // repeat: "", daily, weekdays, weekly

    // Reminders: she flies over and tells you, with a chime and a Windows notification (reminders.json).
    partial class Pet
    {
        public List<Reminder> reminders = new List<Reminder>();
        string remindersPath; public string remindersError = "";
        Reminder lastReminder; DateTime lastReminderAt; double nextReminderCheck;
        Window reminderWindow; ListBox remList; TextBox remWhat, remWhen; TextBlock remStatus;

        void InitReminders()
        {
            remindersPath = App.PathOf("reminders.json");
            try
            {
                if (File.Exists(remindersPath))
                    foreach (var e in Json.List(Json.Load(remindersPath)))
                    {
                        var text = Json.Str(Json.Get(e, "text")); var at = Json.Str(Json.Get(e, "at"));
                        DateTime when;
                        if (text != "" && DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when)) reminders.Add(new Reminder { at = when.Kind == DateTimeKind.Utc ? when.ToLocalTime() : when, text = text, repeat = Json.Str(Json.Get(e, "repeat")) });
                    }
            }
            catch (Exception e) { remindersError = e.Message; }
        }

        void SaveReminders()
        {
            if (App.SelfTest || App.Friend) return;
            try
            {
                if (reminders.Count == 0) { if (File.Exists(remindersPath)) File.Delete(remindersPath); return; }
                Json.Save(remindersPath, reminders.OrderBy(r => r.at).Select(r => new Dictionary<string, object> { { "at", r.at.ToString("o") }, { "text", r.text }, { "repeat", r.repeat } }).ToList());
            }
            catch (Exception e) { ShowProblem("Reminders could not be saved: " + e.Message); }
        }

        // "20" (minutes), "20m", "1h30", "1 hour 15 min", "half an hour", "17:45", "5pm", "tomorrow 9:00".
        public static DateTime? ParseWhen(string text, DateTime now)
        {
            string s = Regex.Replace(Regex.Replace(text.Trim().ToLowerInvariant(), @"^(in|at)\s+", ""), @"\s+from now$", "");
            if (s == "") return null;
            int dayOffset = 0;
            var m = Regex.Match(s, @"^tomorrow\s*(at\s+)?(.*)$");
            if (m.Success) { dayOffset = 1; s = m.Groups[2].Value.Trim(); if (s == "") s = "9:00"; }
            var m1 = Regex.Match(s, @"^(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)$");
            var m2 = Regex.Match(s, @"^(\d{1,2})[:.](\d{2})$");
            if (m1.Success || m2.Success)
            {
                var mm = m1.Success ? m1 : m2;
                int hour = int.Parse(mm.Groups[1].Value), minute = mm.Groups[2].Success && mm.Groups[2].Value != "" ? int.Parse(mm.Groups[2].Value) : 0;
                string ampm = m1.Success ? m1.Groups[3].Value : "";
                if (ampm == "pm" && hour < 12) hour += 12; else if (ampm == "am" && hour == 12) hour = 0;
                if (hour > 23 || minute > 59) return null;
                var at = now.Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
                if (dayOffset == 0 && at <= now) at = at.AddDays(1);
                return at;
            }
            if (dayOffset > 0) return null;
            if (Regex.IsMatch(s, @"^\d{1,4}$")) return now.AddMinutes(int.Parse(s));
            var hm = Regex.Match(s, @"^(\d{1,3})\s*h\s*(\d{1,2})$");
            if (hm.Success) return now.AddHours(int.Parse(hm.Groups[1].Value)).AddMinutes(int.Parse(hm.Groups[2].Value));
            s = Regex.Replace(s, @"\bhalf an? hour\b", "30 min"); s = Regex.Replace(s, @"\ban? hour\b", "1 hour"); s = Regex.Replace(s, @"\ban? minute\b", "1 min");
            s = Regex.Replace(s, @"\ba couple of\b", "2"); s = Regex.Replace(s, @"\ba few\b", "3");
            const string pattern = @"(\d+(?:[.,]\d+)?)\s*(hours|hour|hrs|hr|h|minutes|minute|mins|min|m|seconds|second|secs|sec|s)\b";
            var found = Regex.Matches(s, pattern);
            if (found.Count == 0) return null;
            if (Regex.Replace(Regex.Replace(Regex.Replace(s, pattern, ""), @"\band\b", ""), @"[,\s]", "") != "") return null;
            double seconds = 0;
            foreach (Match f in found)
            {
                double n = double.Parse(f.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                var unit = f.Groups[2].Value;
                seconds += unit.StartsWith("h") ? n * 3600 : unit.StartsWith("m") ? n * 60 : n;
            }
            if (seconds <= 0) return null;
            return now.AddSeconds(seconds);
        }

        static readonly string[] DayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

        // "every day at 9", "daily 8am", "every weekday at 8:30", "weekdays at 9", "every monday at 18:00".
        // Returns the first time and the repeat kind (daily, weekdays, weekly), or null.
        public static Tuple<DateTime, string> ParseRepeating(string text, DateTime now)
        {
            var m = Regex.Match(text.Trim().ToLowerInvariant(), @"^(?:every\s+)?(?<kind>daily|day|weekdays|weekday|monday|tuesday|wednesday|thursday|friday|saturday|sunday)s?\s*(?:at\s+)?(?<time>.*)$");
            if (!m.Success) return null;
            string kind = m.Groups["kind"].Value, timeText = m.Groups["time"].Value.Trim();
            if (timeText == "") timeText = "9:00";
            if (!text.Trim().ToLowerInvariant().StartsWith("every") && kind != "daily" && kind != "weekdays" && kind != "weekday") return null;
            var at = ParseWhen(timeText, now);
            if (at == null || !Regex.IsMatch(timeText, @"\d[:.]\d|am|pm|^\d{1,2}$")) return null;
            // A bare number here means an hour of the day, not minutes from now.
            if (Regex.IsMatch(timeText, @"^\d{1,2}$")) { int h = int.Parse(timeText); if (h > 23) return null; at = now.Date.AddHours(h); }
            var first = now.Date.Add(at.Value.TimeOfDay);
            string repeat = kind == "day" || kind == "daily" ? "daily" : kind.StartsWith("weekday") ? "weekdays" : "weekly";
            if (repeat == "weekly")
            {
                int target = Array.IndexOf(DayNames, kind);
                while ((int)first.DayOfWeek != target || first <= now) first = first.AddDays(1);
            }
            else
            {
                while (first <= now || (repeat == "weekdays" && (first.DayOfWeek == DayOfWeek.Saturday || first.DayOfWeek == DayOfWeek.Sunday))) first = first.AddDays(1);
            }
            return Tuple.Create(first, repeat);
        }

        public static DateTime NextOccurrence(DateTime at, string repeat, DateTime now)
        {
            var next = at;
            do
            {
                next = next.AddDays(repeat == "weekly" ? 7 : 1);
                while (repeat == "weekdays" && (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)) next = next.AddDays(1);
            } while (next <= now);
            return next;
        }

        public static string RepeatLabel(Reminder r) =>
            r.repeat == "daily" ? "  (every day)" : r.repeat == "weekdays" ? "  (weekdays)" : r.repeat == "weekly" ? "  (every " + r.at.ToString("dddd", CultureInfo.InvariantCulture) + ")" : "";

        public static string FormatReminderTime(DateTime at)
        {
            var now = DateTime.Now; string clockText = at.ToString("HH:mm");
            if (at.Date == now.Date) return clockText;
            if (at.Date == now.Date.AddDays(1)) return "tomorrow " + clockText;
            return at.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
        }

        public void AddReminder(DateTime at, string text, string repeat = "")
        {
            text = (text ?? "").Trim(); if (text == "") text = "Time's up.";
            if (text.Length > 140) text = text.Substring(0, 140);
            var r = new Reminder { at = at, text = text, repeat = repeat ?? "" };
            reminders.Add(r);
            SaveReminders(); RefreshReminderList();
            ShowStatus("Got it. " + FormatReminderTime(at) + RepeatLabel(r) + ": " + text, 4);
        }

        void UpdateReminders(double now)
        {
            if (App.Friend || now < nextReminderCheck || reminders.Count == 0) return;
            nextReminderCheck = now + 1;
            var due = reminders.Where(r => r.at <= DateTime.Now).OrderBy(r => r.at).FirstOrDefault();
            if (due == null) return;
            double late = (DateTime.Now - due.at).TotalMinutes;
            reminders.Remove(due);
            // A repeating one comes back for its next time.
            if (due.repeat != "") reminders.Add(new Reminder { at = NextOccurrence(due.at, due.repeat, DateTime.Now), text = due.text, repeat = due.repeat });
            SaveReminders(); RefreshReminderList();
            SendReminder(due, late);
        }

        public void SendReminder(Reminder r, double minutesLate)
        {
            lastReminder = r; lastReminderAt = DateTime.Now;
            if (tray != null && !App.SelfTest) { try { tray.ShowBalloonTip(10000, "Capri reminder", r.text, System.Windows.Forms.ToolTipIcon.Info); } catch { } }
            PlaySfx("reminder.wav", 0.9, true);
            if (isHidden) return;
            StopVoice();
            if (idleRest != "awake") { idleRest = "awake"; sleepReason = ""; lastInteraction = Now; }
            if (!busyMode && !dragging && state != "catchgame") { routine.Clear(); BeginState("remind"); }
            bool shown = minutesLate >= 5 ? ShowRemark("reminderlate", true, new object[] { r.text, r.at.ToString("HH:mm") }) : ShowRemark("reminder", true, r.text);
            if (shown) { messageUntil = Now + 15; nextRemark = Now + 40; }
        }

        public bool CanSnooze => lastReminder != null && (DateTime.Now - lastReminderAt).TotalMinutes < 30;
        public void SnoozeReminder(int minutes = 10)
        {
            if (lastReminder == null) return;
            AddReminder(DateTime.Now.AddMinutes(minutes), lastReminder.text);
            lastReminder = null;
        }

        void RefreshReminderList()
        {
            if (remList == null) return;
            remList.Items.Clear();
            foreach (var r in reminders.OrderBy(x => x.at)) remList.Items.Add(new ListBoxItem { Content = FormatReminderTime(r.at) + RepeatLabel(r) + "   " + r.text, Tag = r });
            if (remList.Items.Count == 0) remList.Items.Add("(nothing yet)");
        }

        public static Brush HexBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);

        public void ShowReminderWindow()
        {
            if (reminderWindow != null) { reminderWindow.Activate(); return; }
            var dialog = new Window { Title = "Capri // Reminders", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = HexBrush("#111722"), Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize };
            var stack = new StackPanel { Margin = new Thickness(16) }; dialog.Content = stack;
            Func<string, TextBlock> label = text => new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 3) };
            stack.Children.Add(label("Remind me to:"));
            remWhat = new TextBox { Padding = new Thickness(4) }; stack.Children.Add(remWhat);
            stack.Children.Add(label("When:"));
            remWhen = new TextBox { Padding = new Thickness(4), Text = "20" }; stack.Children.Add(remWhen);
            stack.Children.Add(new TextBlock { Text = "20 = in 20 minutes.  Also: 1h30, 45 min, 17:45, 5pm, tomorrow 9:00, every day at 9, weekdays at 8:30, every monday at 18:00", FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
            var quick = new WrapPanel(); stack.Children.Add(quick);
            foreach (var q in new[] { "5", "10", "25", "1h", "2h" })
            {
                var b = new Button { Content = q, Tag = q, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
                b.Click += (s, e) => remWhen.Text = (string)((Button)s).Tag; quick.Children.Add(b);
            }
            remStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), Foreground = HexBrush("#FCEE0A") };
            var add = new Button { Content = "Add reminder", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 6, 0, 0) };
            stack.Children.Add(add); stack.Children.Add(remStatus);
            stack.Children.Add(label("Coming up:"));
            remList = new ListBox { Height = 130 }; stack.Children.Add(remList);
            var remove = new Button { Content = "Remove selected", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 6, 0, 0) };
            stack.Children.Add(remove);
            RefreshReminderList();
            add.Click += (s, e) =>
            {
                var repeating = ParseRepeating(remWhen.Text, DateTime.Now);
                var at = repeating != null ? repeating.Item1 : ParseWhen(remWhen.Text, DateTime.Now);
                if (at == null) { remStatus.Text = "I don't understand \"" + remWhen.Text + "\". Try 20, 1h30, 17:45 or every day at 9."; return; }
                AddReminder(at.Value, remWhat.Text, repeating?.Item2 ?? "");
                remStatus.Text = "Added for " + FormatReminderTime(at.Value) + (repeating != null ? RepeatLabel(new Reminder { at = at.Value, repeat = repeating.Item2 }) : "") + "."; remWhat.Text = ""; remWhat.Focus();
            };
            remove.Click += (s, e) =>
            {
                var item = remList.SelectedItem as ListBoxItem;
                if (item != null) { reminders.Remove((Reminder)item.Tag); SaveReminders(); RefreshReminderList(); }
            };
            dialog.Closed += (s, e) => { reminderWindow = null; remList = null; RegisterInteraction(); };
            reminderWindow = dialog;
            CyberChrome(dialog, "Capri // Reminders", "timers");
            dialog.Show(); remWhat.Focus();
        }
    }
}

using System;

namespace Capri
{
    // Night / quiet hours: no random yapping, idle remarks, cursor attacks or window tricks while
    // they're on. Reminders, focus, games and anything you start yourself still work.
    partial class Pet
    {
        public static readonly string[] QuietHourChoices = { "off", "22-7", "23-7", "0-8", "1-9" };
        public string quietHours = "off";

        public static bool InQuietHours(string range, int hour)
        {
            if (string.IsNullOrEmpty(range) || range == "off") return false;
            var parts = range.Split('-'); int from, to;
            if (parts.Length != 2 || !int.TryParse(parts[0], out from) || !int.TryParse(parts[1], out to)) return false;
            return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        public bool QuietNow => !App.SelfTest && InQuietHours(quietHours, DateTime.Now.Hour);

        public static string QuietHoursLabel(string range)
        {
            if (Array.IndexOf(QuietHourChoices, range) <= 0) return "Off";
            var p = range.Split('-');
            return Hour12(int.Parse(p[0])) + " to " + Hour12(int.Parse(p[1]));
        }
        static string Hour12(int h) => h == 0 ? "midnight" : h == 12 ? "noon" : h < 12 ? h + " am" : (h - 12) + " pm";
    }
}

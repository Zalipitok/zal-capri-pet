using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Capri
{
    // What Capri remembers between sessions (memory.json).
    partial class Pet
    {
        static readonly string[] MemoryStrings = { "firstMet", "lastSeen", "lastDay", "unlocked", "todayDate", "lastFocusDay", "lastUpdateCheck" };
        static readonly string[] MemoryNumbers = { "days", "sessions", "pets", "throws", "shakes", "swats", "returns", "scans", "games", "bestCatch", "todayPets", "todayThrows", "todaySwats", "focusSessions", "focusMinutes", "focusStreak" };
        public Dictionary<string, string> memText = new Dictionary<string, string>();
        public Dictionary<string, int> memNum = new Dictionary<string, int>();
        string memoryPath; public bool memoryDirty; double nextMemorySave; object[] greeting;

        void InitMemory()
        {
            memoryPath = App.PathOf("memory.json");
            foreach (var k in MemoryStrings) memText[k] = "";
            foreach (var k in MemoryNumbers) memNum[k] = 0;
            try
            {
                if (File.Exists(memoryPath))
                {
                    var saved = Json.LoadDict(memoryPath);
                    foreach (var k in MemoryStrings) if (Json.Get(saved, k) != null) memText[k] = Json.Str(Json.Get(saved, k));
                    foreach (var k in MemoryNumbers) if (Json.Get(saved, k) != null) memNum[k] = Math.Max(0, (int)Json.Num(Json.Get(saved, k)));
                }
            }
            catch { } // A damaged memory file starts fresh.
        }

        public int Mem(string key) { int v; return memNum.TryGetValue(key, out v) ? v : 0; }

        public void SaveMemory()
        {
            if (App.SelfTest || App.Friend || !memoryDirty) return;
            try
            {
                memText["lastSeen"] = DateTime.Now.ToString("o");
                var d = new Dictionary<string, object>();
                foreach (var k in MemoryStrings) d[k] = memText[k];
                foreach (var k in MemoryNumbers) d[k] = memNum[k];
                Json.Save(memoryPath, d);
                memoryDirty = false;
            }
            catch { }
        }

        bool UpdateMemoryDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (memText["todayDate"] != today) { memText["todayDate"] = today; memNum["todayPets"] = 0; memNum["todayThrows"] = 0; memNum["todaySwats"] = 0; memoryDirty = true; }
            if (memText["lastDay"] != today) { memText["lastDay"] = today; memNum["days"]++; memoryDirty = true; return true; }
            return false;
        }

        // Counts an event; returns today's count for it (or the all-time count when there's no daily one).
        public int AddMemory(string what)
        {
            UpdateMemoryDay();
            if (!memNum.ContainsKey(what)) memNum[what] = 0;
            memNum[what]++; memoryDirty = true;
            string daily = "today" + char.ToUpper(what[0]) + what.Substring(1);
            if (memNum.ContainsKey(daily)) { memNum[daily]++; return memNum[daily]; }
            return memNum[what];
        }

        void StartMemorySession()
        {
            var now = DateTime.Now; int gapDays = 0;
            DateTime seen;
            if (memText["lastSeen"] != "" && DateTime.TryParse(memText["lastSeen"], null, System.Globalization.DateTimeStyles.RoundtripKind, out seen)) gapDays = (int)Math.Floor((now - seen).TotalDays);
            bool newDay = UpdateMemoryDay();
            memNum["sessions"]++; memoryDirty = true;
            if (memText["firstMet"] == "") { memText["firstMet"] = now.ToString("yyyy-MM-dd"); greeting = new object[] { "firstmeet", null }; }
            else if (newDay && new[] { 7, 30, 50, 100, 200, 365, 500, 1000 }.Contains(memNum["days"])) greeting = new object[] { "anniversary", memNum["days"] };
            else if (gapDays >= 2) greeting = new object[] { "longtime", gapDays };
            SaveMemory();
        }

        public string MemorySummary()
        {
            return "First met: " + memText["firstMet"] + "\nDays together: " + Mem("days") + "\nSessions: " + Mem("sessions") +
                "\n\nHead pats: " + Mem("pets") + " (" + Mem("todayPets") + " today)\nThrows: " + Mem("throws") + " (" + Mem("todayThrows") + " today)\nShakes: " + Mem("shakes") +
                "\nSwats: " + Mem("swats") + "\nCursor catches: " + rivalWins + " / escapes: " + rivalEscapes + "\nWelcome-backs: " + Mem("returns") + "\nFiles scanned: " + Mem("scans") +
                "\nFocus sessions: " + Mem("focusSessions") + " (" + Mem("focusMinutes") + " minutes, streak " + Mem("focusStreak") + " days)";
        }

        static bool Milestone(int count, params int[] marks) => marks.Contains(count);
    }
}

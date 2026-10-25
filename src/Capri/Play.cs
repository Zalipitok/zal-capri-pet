using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace Capri
{
    // The catch-me game and unlocks.
    partial class Pet
    {
        public bool gameActive; public int gameScore; public double gameEnds, gameNextJump, gameFlash = -10, gameLength = 30;

        public void StartCatchGame()
        {
            PrepareManualAction(); routine.Clear();
            double now = Now;
            gameActive = true; gameScore = 0; gameEnds = now + gameLength; gameNextJump = now + 0.9;
            BeginState("catchgame");
            ShowRemark("gamestart", true);
        }

        void MoveGameTarget()
        {
            var a = WorkArea(); double x = w.Left, y = w.Top;
            for (int i = 0; i < 8; i++)
            {
                x = a.Left + rng.NextDouble() * Math.Max(1.0, a.Width - w.Width);
                y = a.Top + rng.NextDouble() * Math.Max(1.0, a.Height - w.Height);
                if (Math.Abs(x - w.Left) + Math.Abs(y - w.Top) > (a.Width + a.Height) / 6.0) break;
            }
            targetX = x; targetY = y;
            gameNextJump = Now + Math.Max(0.55, 1.7 - gameScore * 0.09);
        }

        // From the mouse handler: true when the click counted as a catch.
        public bool RegisterGameCatch()
        {
            if (!gameActive || state != "catchgame") return false;
            gameScore++; gameFlash = Now;
            PlaySfx("cartoon-swat.wav", 0.7);
            MoveGameTarget();
            return true;
        }

        public void StopCatchGame()
        {
            gameActive = false;
            AddMemory("games");
            int best = Mem("bestCatch");
            if (gameScore > best) { memNum["bestCatch"] = gameScore; memoryDirty = true; BeginState("grumpy"); ShowRemark("gamenewbest", true, gameScore); }
            else if (gameScore == 0) { BeginState("smug"); ShowRemark("gamezero", true); }
            else { BeginState("smug"); ShowRemark("gameover", true, new object[] { gameScore, best }); }
            TestUnlocks();
        }

        // --- Unlocks (unlocks.json): stats that all must be met grant a routine or a remark pool.
        public List<object> unlocks = new List<object>();
        public List<string> unlockPools = new List<string>();
        public string unlocksError = ""; double nextUnlockCheck = 20;
        MenuItem routineMenu;
        static readonly Dictionary<string, string> StatNames = new Dictionary<string, string> { { "days", "days together" }, { "pets", "head pats" }, { "throws", "throws" }, { "swats", "swats" }, { "shakes", "shakes" }, { "returns", "welcome-backs" }, { "scans", "file scans" }, { "sessions", "sessions" }, { "bestCatch", "best catch-me score" }, { "games", "catch-me games" }, { "catches", "cursor catches" } };

        void InitPlay()
        {
            paths["catchgame"] = () =>
            {
                double now = Now;
                if (!gameActive) { ChooseState(); return; }
                if (now >= gameEnds) { StopCatchGame(); return; }
                if (now >= gameNextJump || Math.Abs(targetX - w.Left) + Math.Abs(targetY - w.Top) < 6) MoveGameTarget();
                until = now + 5;
                effect.Text = "CATCH ME   " + gameScore + "   " + (int)Math.Ceiling(gameEnds - now) + "s";
            };
            motions["catchgame"] = () =>
            {
                rotation.Angle = -direction * 12; scale.ScaleY = 0.94;
                double hit = Now - gameFlash;
                if (hit >= 0 && hit < 0.25) { sprite.Opacity = 0.5 + 2 * hit; shift.X = Math.Sin(hit * 60) * 6; }
            };
            try
            {
                var path = App.PathOf("unlocks.json");
                if (File.Exists(path)) unlocks = Json.List(Json.Load(path));
            }
            catch (Exception e) { unlocksError = e.Message; }
        }

        int Stat(string name) => name == "catches" ? rivalWins : Mem(name);
        bool UnlockMet(object u)
        {
            var need = Json.Dict(Json.Get(u, "need"));
            if (need == null) return true;
            foreach (var kv in need) if (Stat(kv.Key) < (int)Json.Num(kv.Value)) return false;
            return true;
        }
        public List<string> UnlockedIds() => memText["unlocked"].Split(',').Where(x => x != "").ToList();

        void EnableUnlock(object u)
        {
            var title = Json.Str(Json.Get(u, "title"));
            var r = Json.Get(u, "routine");
            if (r != null && !routines.ContainsKey(title))
            {
                routines[title] = Json.List(r).Select(x => Json.Str(x)).Where(x => specs.ContainsKey(x)).ToList();
                routineOrder.Add(title); routineBag.Clear();
                if (routineMenu != null) NewMenuEntry(routineMenu, title, () => StartRoutineManual(title));
            }
            var pool = Json.Str(Json.Get(u, "remarks"));
            if (pool != "" && !unlockPools.Contains(pool)) unlockPools.Add(pool);
        }

        public void StartRoutineManual(string name) => Defer(() => { PrepareManualAction(); StartRoutine(name); });

        public void TestUnlocks()
        {
            if (App.Friend) return;
            var have = UnlockedIds();
            foreach (var u in unlocks)
            {
                var id = Json.Str(Json.Get(u, "id"));
                if (have.Contains(id) || !UnlockMet(u)) continue;
                have.Add(id); memText["unlocked"] = string.Join(",", have); memoryDirty = true;
                EnableUnlock(u);
                ShowRemark("unlocked", true, Json.Str(Json.Get(u, "title")));
                try { tray?.ShowBalloonTip(5000, "Capri unlocked something", "New: " + Json.Str(Json.Get(u, "title")), System.Windows.Forms.ToolTipIcon.Info); } catch { }
                return; // one announcement at a time
            }
        }

        public string UnlockSummary()
        {
            var have = UnlockedIds();
            var lines = new List<string>();
            foreach (var u in unlocks)
            {
                if (have.Contains(Json.Str(Json.Get(u, "id")))) { lines.Add("  [x] " + Json.Str(Json.Get(u, "title"))); continue; }
                var need = Json.Dict(Json.Get(u, "need")) ?? new Dictionary<string, object>();
                var hint = string.Join(", ", need.Select(kv => (int)Json.Num(kv.Value) + " " + (StatNames.ContainsKey(kv.Key) ? StatNames[kv.Key] : kv.Key) + " (now " + Stat(kv.Key) + ")"));
                lines.Add("  [ ] ??? - " + hint);
            }
            return "Unlocked " + have.Count + " of " + unlocks.Count + ":\n" + string.Join("\n", lines);
        }

        void EnableEarlierUnlocks()
        {
            var have = UnlockedIds();
            foreach (var u in unlocks) if (have.Contains(Json.Str(Json.Get(u, "id")))) EnableUnlock(u);
        }
    }
}

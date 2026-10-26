using System;
using System.Windows;

namespace Capri
{
    // Focus timer: she waits in her corner with a countdown, cheers when you finish, keeps a streak.
    partial class Pet
    {
        public bool focusActive; int focusMinutes; DateTime focusEnds, focusStarted; double nextFocusTick;
        public bool focusBreaks = true, onFocusBreak; DateTime focusBreakEnds;

        void InitFocus() { }

        public void StartFocus(int minutes, bool quiet = false)
        {
            if (App.Friend) return;
            onFocusBreak = false;
            minutes = Math.Max(1, Math.Min(180, minutes));
            focusActive = true; focusMinutes = minutes; focusStarted = DateTime.Now; focusEnds = DateTime.Now.AddMinutes(minutes);
            busyByDnd = false; busyByFullscreen = false; // the session owns Busy mode now
            if (!busyMode) SetBusyMode(true, true);
            nextFocusTick = 0;
            if (!quiet) ShowRemark("focusstart", true, minutes);
        }

        public void StopFocus(bool gaveUp)
        {
            if (!focusActive) return;
            focusActive = false;
            focusBadge.Visibility = Visibility.Collapsed;
            if (tray != null) try { tray.Text = App.Friend ? "Capri (friend)" : "Capri"; } catch { }
            if (gaveUp) { ShowRemark("focusquit", true); return; }
            int done = (int)Math.Round((DateTime.Now - focusStarted).TotalMinutes);
            AddMemory("focusSessions"); memNum["focusMinutes"] += done;
            string today = DateTime.Now.ToString("yyyy-MM-dd"), yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            if (memText["lastFocusDay"] != today)
            {
                memNum["focusStreak"] = memText["lastFocusDay"] == yesterday ? memNum["focusStreak"] + 1 : 1;
                memText["lastFocusDay"] = today;
            }
            memoryDirty = true;
            if (busyMode) SetBusyMode(false, true);
            PlaySfx("reminder.wav", 0.8, true);
            if (tray != null && !App.SelfTest) try { tray.ShowBalloonTip(6000, "Focus done", done + " minutes. Streak: " + memNum["focusStreak"] + " day(s).", System.Windows.Forms.ToolTipIcon.Info); } catch { }
            if (CanReact) { routine.Clear(); BeginState("celebrate"); }
            int rest = (int)Math.Max(0, Math.Min(60, tuning.focusBreakMinutes));
            if (focusBreaks && rest > 0 && !App.SelfTest)
            {
                // A short break: a countdown on her badge, a stretch, and a chime when it's over.
                onFocusBreak = true; focusBreakEnds = DateTime.Now.AddMinutes(rest);
                if (specs.ContainsKey("breaktime")) routine.Enqueue("breaktime");
                ShowRemark("focusbreak", true, new object[] { done, memNum["focusStreak"], rest });
            }
            else ShowRemark("focusdone", true, new object[] { done, memNum["focusStreak"] });
            messageUntil = Now + 8;
        }

        public void EndFocusBreak(bool finished)
        {
            if (!onFocusBreak) return;
            onFocusBreak = false;
            focusBadge.Visibility = Visibility.Collapsed;
            if (tray != null) try { tray.Text = App.Friend ? "Capri (friend)" : "Capri"; } catch { }
            if (!finished) return;
            PlaySfx("reminder.wav", 0.8, true);
            if (tray != null) try { tray.ShowBalloonTip(5000, "Break's over", "Ready for another round? Right-click her > Focus timer.", System.Windows.Forms.ToolTipIcon.Info); } catch { }
            if (CanReact) { routine.Clear(); BeginState("wave"); }
            ShowRemark("breakover", true);
        }

        void UpdateFocus(double now)
        {
            if (onFocusBreak && !focusActive && now >= nextFocusTick)
            {
                nextFocusTick = now + 0.5;
                var rest = focusBreakEnds - DateTime.Now;
                if (rest.TotalSeconds <= 0) { EndFocusBreak(true); return; }
                string text = "BREAK " + (int)Math.Floor(rest.TotalMinutes) + ":" + rest.Seconds.ToString("00");
                focusText.Text = text;
                if (focusBadge.Visibility != Visibility.Visible) focusBadge.Visibility = Visibility.Visible;
                if (tray != null) try { tray.Text = "Capri - " + text.ToLowerInvariant() + " left"; } catch { }
                return;
            }
            if (!focusActive || now < nextFocusTick) return;
            nextFocusTick = now + 0.5;
            if (!busyMode) { StopFocus(true); return; } // you turned Busy off
            var left = focusEnds - DateTime.Now;
            if (left.TotalSeconds <= 0) { StopFocus(false); return; }
            string text = "FOCUS " + (int)Math.Floor(left.TotalMinutes) + ":" + left.Seconds.ToString("00");
            focusText.Text = text;
            if (focusBadge.Visibility != Visibility.Visible) focusBadge.Visibility = Visibility.Visible;
            if (tray != null) try { tray.Text = "Capri - " + text.ToLowerInvariant() + " left"; } catch { }
        }
    }
}

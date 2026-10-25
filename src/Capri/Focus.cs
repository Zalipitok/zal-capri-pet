using System;
using System.Windows;

namespace Capri
{
    // Focus timer: she waits in her corner with a countdown, cheers when you finish, keeps a streak.
    partial class Pet
    {
        public bool focusActive; int focusMinutes; DateTime focusEnds, focusStarted; double nextFocusTick;

        void InitFocus() { }

        public void StartFocus(int minutes, bool quiet = false)
        {
            if (App.Friend) return;
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
            ShowRemark("focusdone", true, new object[] { done, memNum["focusStreak"] });
            messageUntil = Now + 8;
        }

        void UpdateFocus(double now)
        {
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

using System;
using System.Windows;

namespace Capri
{
    // Quality of life: keep her in one spot, let clicks pass through her, and keep her off the
    // window you're working in.
    partial class Pet
    {
        public bool stayPut, avoidWork = true, clickThrough;
        public double stayX = double.NaN, stayY = double.NaN;
        double nextWorkCheck = 5;

        // --- Stay here: she still animates, talks and reacts, but doesn't travel.
        public void SetStayPut(bool on)
        {
            stayPut = on; autoBag.Clear(); routine.Clear();
            if (on) { stayX = w.Left; stayY = w.Top; nextRetry = double.PositiveInfinity; ShowStatus("Staying right here. Drag me if you want me somewhere else.", 4); }
            else ShowStatus("Free to roam again.", 3);
            SaveSettings();
        }

        // Wherever you put her by hand becomes her new spot.
        public void MarkStayHere() { if (stayPut) { stayX = w.Left; stayY = w.Top; SaveSettings(); } }

        void ApplyStayPut()
        {
            if (!stayPut || double.IsNaN(stayX) || busyMode || dragging) return;
            if (state == "windowperch" || state == "thrown" || state == "catchgame") return;   // things you started yourself
            w.Left = stayX; w.Top = stayY;
        }

        void RestoreStayPut()
        {
            if (!stayPut || double.IsNaN(stayX)) return;
            w.Left = stayX; w.Top = stayY; ClampPet();
        }

        // --- Click-through: clicks land on whatever is under her. The tray icon or the summon hotkey undo it.
        public void SetClickThrough(bool on)
        {
            clickThrough = on;
            try
            {
                int ex = CapriCursor.GetWindowLong(petHandle, -20);
                CapriCursor.SetWindowLong(petHandle, -20, on ? ex | 0x20 : ex & ~0x20);
            }
            catch { }
            scene.Opacity = on ? 0.6 : 1.0;
            if (on) ShowStatus("Clicks go through me now. Tray icon > \"Let clicks through her\" or the summon hotkey turns it off.", 6);
            else ShowStatus("You can grab me again.", 3);
        }

        // --- Keep away from your work: the middle of the window you're using is off limits.
        Rect? WorkZone()
        {
            if (!avoidWork) return null;
            long[] fg = null; try { fg = CapriDesktop.ForegroundWindow(); } catch { }
            if (fg == null) return null;
            var r = WindowRectDip(fg[0]); if (r == null) return null;
            var z = r.Value;
            double inset = z.Width * (CapriWindows.Maximized(fg[0]) ? 0.2 : 0.05);
            if (z.Width - 2 * inset < 60 || z.Height < 80) return null;
            return new Rect(z.Left + inset, z.Top + 34, z.Width - 2 * inset, z.Height - 34);   // the title bar is fine: that's where she sits
        }

        Rect BodyAt(double left, double top)
        {
            double above = EmptyAbove(poseIndex), foot = SpriteFoot(poseIndex);
            return new Rect(left + w.Width * 0.25, top + above, w.Width * 0.5, Math.Max(10.0, w.Height - foot - above));
        }

        bool ClearOf(Rect zone, double left, double top) => !BodyAt(left, top).IntersectsWith(zone);

        // A spot in the work area that keeps her body off the zone (keepY: only move sideways).
        Point? ClearSpot(Rect zone, Rect a, double y, bool keepY)
        {
            for (int i = 0; i < 24; i++)
            {
                double x = i < 16 ? a.Left + rng.NextDouble() * Math.Max(1.0, a.Width - w.Width) : (i % 2 == 0 ? a.Left : a.Right - w.Width);
                double ty = keepY || i >= 16 ? y : a.Top + rng.NextDouble() * Math.Max(1.0, a.Height - w.Height);
                if (ClearOf(zone, x, ty)) return new Point(x, ty);
            }
            return null;
        }

        // Called when a state picks a random destination.
        void AvoidWorkTarget(Rect a, bool keepY)
        {
            var z = WorkZone(); if (z == null || ClearOf(z.Value, targetX, targetY)) return;
            var p = ClearSpot(z.Value, a, targetY, keepY);
            if (p != null) { targetX = p.Value.X; targetY = p.Value.Y; }
        }

        // Every couple of seconds: if she's idling on top of what you're working on, she moves aside.
        void ScootFromWork(double now, double sysIdle)
        {
            if (!avoidWork || stayPut || now < nextWorkCheck) return;
            nextWorkCheck = now + 2;
            if (!CanReact || !spec.Has("calm") || state == "windowperch" || voiceBusy || sysIdle > 20 || routine.Count > 0) return;
            var z = WorkZone(); if (z == null || ClearOf(z.Value, w.Left, w.Top)) return;
            var a = WorkArea();
            var p = ClearSpot(z.Value, a, w.Top, false); if (p == null) return;
            BeginState("wander"); targetX = p.Value.X; targetY = p.Value.Y;
            until = Now + 4 + Math.Sqrt(Math.Pow(targetX - w.Left, 2) + Math.Pow(targetY - w.Top, 2)) / 120;
        }
    }
}

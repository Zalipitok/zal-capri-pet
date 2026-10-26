using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Capri
{
    // Her powers, played for fun: possessing a window (opt-in), a glitch trail on the cursor, and
    // peeking from behind the window you're using.
    partial class Pet
    {
        Window possessWin; TextBlock possessLabel;
        long possessHandle; int possessX, possessY; bool possessing;
        List<Window> trailWindows = new List<Window>(); public double trailUntil = -10000; List<Point> trailHistory = new List<Point>();
        public bool behindWindow; long peekHandle;

        void InitPowers()
        {
            possessWin = LoadXaml<Window>("Possess.xml");
            possessLabel = (TextBlock)possessWin.FindName("Label");
            possessWin.SourceInitialized += (s, e) => { try { CapriCursor.MakeOverlay(new WindowInteropHelper(possessWin).Handle); } catch { } };

            motions["possess"] = () =>
            {
                rotation.Angle = direction * 6 + Math.Sin(phase * 30) * 2; shift.X = Math.Sin(phase * 40) * 3;
                if (!possessing) return;
                if (phase >= 2.2 || Mouse.LeftButton != MouseButtonState.Released) { StopPossession(); return; }
                double k = Math.Max(0.0, 1 - phase / 2.2);
                int dx = (int)(Math.Sin(phase * 47) * 7 * k), dy = (int)(Math.Cos(phase * 39) * 4 * k);
                try { CapriWindows.MoveTo(possessHandle, possessX + dx, possessY + dy); } catch { }
                possessWin.Opacity = 0.55 + 0.45 * Math.Abs(Math.Sin(phase * 23));
                possessLabel.Margin = new Thickness(Math.Sin(phase * 31) * 12, 0, 0, 0);
            };
            motions["repaircursor"] = () =>
            {
                rotation.Angle = direction * 3 * Math.Sin(phase * 2);
                shift.X = phase < 1.2 ? -direction * 6 : direction * 8 * Math.Exp(-(phase - 1.2) * 4);
            };
            paths["peekbehind"] = () =>
            {
                if (peekHandle == 0)
                {
                    var spot = FindPeekWindow();
                    if (spot == null) { ChooseState(); return; }
                    SetPeekTarget(spot.Value);
                }
                var r = WindowRectDip(peekHandle);
                if (r == null || CapriWindows.Maximized(peekHandle)) { RestoreInFront(); peekHandle = 0; ChooseState(); return; }
                // Lean out past the edge, duck back, lean out again.
                double edge = peekSide > 0 ? r.Value.Right : r.Value.Left;
                double outBy = phase < 2.0 ? 20.0 : 4.0 + 20.0 * (0.5 + 0.5 * Math.Cos((phase - 2.0) * 1.6));
                var head = PeekHeadOffset();
                targetX = edge + peekSide * outBy - head.X;
                targetY = r.Value.Top + r.Value.Height * peekHeight - head.Y;
                direction = peekSide > 0 ? -1.0 : 1.0;   // drawing mirrored: hair on the hidden side
                bool close = Math.Abs(targetX - w.Left) + Math.Abs(targetY - w.Top) < 12;
                if (!behindWindow && (close || phase > 2.5))
                {
                    behindWindow = true; w.Topmost = false;
                    try { CapriWindows.PlaceBehind(petHandle, peekHandle); } catch { }
                }
                if (behindWindow) { w.Left = targetX; w.Top = targetY; }
            };
            motions["peekbehind"] = () => { rotation.Angle = peekSide * PeekLean + Math.Sin(phase * 1.3) * 2; shift.X = 0; shift.Y = 0; };
        }

        const double PeekLean = 22;
        int peekSide = 1; double peekHeight = 0.35;

        // Where her head is inside her window when she leans for peeking.
        Point PeekHeadOffset()
        {
            try
            {
                var origin = scene.TransformToAncestor(w).Transform(new Point(0, 0));
                var off = VisualTreeHelper.GetOffset(sprite);
                double W = sprite.ActualWidth, H = sprite.ActualHeight;
                double cx = origin.X + off.X + W / 2, cy = origin.Y + off.Y + H / 2;
                double th = peekSide * PeekLean * Math.PI / 180, dy = (visorFraction - 0.5) * H;
                return new Point(cx - dy * Math.Sin(th), cy + dy * Math.Cos(th));
            }
            catch { return new Point(w.Width / 2, w.Height * 0.45); }
        }

        void SetPeekTarget(PeekSpot spot)
        {
            peekHandle = spot.handle; peekSide = spot.side; peekHeight = 0.2 + 0.35 * rng.NextDouble();
        }

        // Peek out from behind a window: false when there's no window to hide behind.
        public bool StartPeek(bool manual)
        {
            var spot = FindPeekWindow();
            if (spot == null)
            {
                if (manual) ShowStatus("No window to hide behind. I need one that isn't maximized, with some room beside it.", 5);
                return false;
            }
            if (manual) { PrepareManualAction(); routine.Clear(); }
            BeginState("peekbehind"); SetPeekTarget(spot.Value);
            return true;
        }

        // --- Possession
        bool StartPossession()
        {
            long target = 0;
            try { var fg = CapriDesktop.ForegroundWindow(); if (fg != null) target = fg[0]; } catch { }
            if (target == 0) target = lastAppWindow;
            if (target == 0 || Mouse.LeftButton != MouseButtonState.Released || Mouse.RightButton != MouseButtonState.Released || CapriWindows.Maximized(target)) return false;
            var r = CapriWindows.Rect(target); if (r == null) return false;
            try { if (CapriDesktop.FullscreenBlocks(IntPtr.Zero)) return false; } catch { }
            possessHandle = target; possessX = r[0]; possessY = r[1]; possessing = true;
            var m = DeviceScale();
            if (m != null)
            {
                possessWin.Left = r[0] / m.Value.M11; possessWin.Top = r[1] / m.Value.M22;
                possessWin.Width = Math.Max(50.0, (r[2] - r[0]) / m.Value.M11); possessWin.Height = Math.Max(50.0, (r[3] - r[1]) / m.Value.M22);
                possessWin.Show();
            }
            return true;
        }

        public void StopPossession()
        {
            if (!possessing) return;
            possessing = false;
            try { CapriWindows.MoveTo(possessHandle, possessX, possessY); } catch { }
            if (possessWin.IsVisible) possessWin.Hide();
        }

        public bool InvokePossession(bool manual)
        {
            if (!manual && !possessWindows) return false;
            if (manual) { PrepareManualAction(); routine.Clear(); }
            if (!StartPossession()) { if (manual) ShowStatus("No window to possess. Click one first (not maximized)."); return false; }
            BeginState("possess");
            ShowRemark("possess", true);
            return true;
        }

        // --- Cursor repairs: a trail of glitch squares replays where the pointer just was.
        Window NewTrailWindow(int i)
        {
            int size = 14 - i;
            var win = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Width = size + 6, Height = size + 6, IsHitTestVisible = false };
            win.Content = new Rectangle { Width = size, Height = Math.Max(3, size * 0.55), Fill = HexBrush(i % 3 == 1 ? "#E000E5FF" : "#E0FF2246") };
            win.Opacity = 1.0 - i * 0.1;
            win.SourceInitialized += (s, e) => { try { CapriCursor.MakeOverlay(new WindowInteropHelper((Window)s).Handle); } catch { } };
            return win;
        }

        public void StartCursorRepair(bool manual)
        {
            if (manual) { PrepareManualAction(); routine.Clear(); }
            if (trailWindows.Count == 0) for (int i = 0; i < 8; i++) trailWindows.Add(NewTrailWindow(i));
            trailUntil = Now + 60; trailHistory.Clear();
            BeginState("repaircursor");
            ShowRemark("cursorrepair", true);
        }

        void UpdateCursorTrail(double now)
        {
            if (trailWindows.Count == 0) return;
            bool active = now < trailUntil && !isHidden && !exiting;
            if (!active)
            {
                if (trailWindows[0].IsVisible) { foreach (var win in trailWindows) win.Hide(); if (!isHidden && !exiting) ShowRemark("cursorrepairend"); }
                return;
            }
            var m = DeviceScale(); if (m == null) return;
            CapriCursor.Point p;
            try { p = CapriCursor.ReadPosition(); } catch { return; }
            trailHistory.Insert(0, new Point(p.X / m.Value.M11, p.Y / m.Value.M22));
            while (trailHistory.Count > 26) trailHistory.RemoveAt(trailHistory.Count - 1);
            for (int i = 0; i < trailWindows.Count; i++)
            {
                var win = trailWindows[i]; var spot = trailHistory[Math.Min(trailHistory.Count - 1, 2 + i * 3)];
                int jitter = rng.NextDouble() < 0.15 ? rng.Next(-6, 7) : 0;
                win.Left = spot.X + 8 + jitter; win.Top = spot.Y + 10 + i;
                if (!win.IsVisible) win.Show();
            }
        }

        // --- Peeking
        void RestoreInFront()
        {
            if (!behindWindow) return;
            behindWindow = false; w.Topmost = false; w.Topmost = true;
        }

        void UpdatePowers(double now)
        {
            if (possessing && (paused || menu.IsOpen || busyMode || isHidden || dragging || mouseDown)) StopPossession();
            if (state != "peekbehind") { peekHandle = 0; RestoreInFront(); }
            if (possessing && state != "possess") StopPossession();
            UpdateCursorTrail(now);
        }
    }
}

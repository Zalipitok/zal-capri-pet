using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Capri
{
    // Monitors, work areas and windows. WPF units = physical pixels / system DPI scale.
    partial class Pet
    {
        public bool shadowEnabled = true, eyesFollow = true;
        int eyeDir = -1;

        void InitScreens() { }

        public Matrix? DeviceScale()
        {
            PresentationSource source = null;
            try { source = PresentationSource.FromVisual(w); } catch { }
            if (source?.CompositionTarget == null) return null;
            return source.CompositionTarget.TransformToDevice;
        }

        public static Rect? FromDeviceRect(int[] r, Matrix m)
        {
            if (r == null) return null;
            return new Rect(r[0] / m.M11, r[1] / m.M22, Math.Max(1.0, (r[2] - r[0]) / m.M11), Math.Max(1.0, (r[3] - r[1]) / m.M22));
        }

        // Work area of the monitor Capri's centre is on (primary monitor before the window exists).
        public Rect WorkArea()
        {
            var m = DeviceScale();
            if (m == null || double.IsNaN(w.Left)) return SystemParameters.WorkArea;
            int[] r = null;
            try { r = CapriDesktop.WorkAreaAt((int)((w.Left + w.Width / 2) * m.Value.M11), (int)((w.Top + w.Height / 2) * m.Value.M22)); } catch { }
            return FromDeviceRect(r, m.Value) ?? SystemParameters.WorkArea;
        }

        public Rect? WorkAreaAtDip(double x, double y)
        {
            var m = DeviceScale(); if (m == null) return null;
            int[] r = null;
            try { r = CapriDesktop.WorkAreaAt((int)(x * m.Value.M11), (int)(y * m.Value.M22)); } catch { }
            return FromDeviceRect(r, m.Value);
        }

        public List<Rect> AllWorkAreas()
        {
            var list = new List<Rect>();
            var m = DeviceScale(); if (m == null) return list;
            int[] flat = null; try { flat = CapriDesktop.AllWorkAreas(); } catch { }
            if (flat == null) return list;
            for (int i = 0; i + 3 < flat.Length; i += 4) list.Add(FromDeviceRect(new[] { flat[i], flat[i + 1], flat[i + 2], flat[i + 3] }, m.Value).Value);
            return list;
        }

        public void ClampPet()
        {
            if (spec.Has("noClamp")) return;
            var a = WorkArea();
            w.Left = Math.Max(a.Left, Math.Min(w.Left, Math.Max(a.Left, a.Right - w.Width)));
            w.Top = Math.Max(a.Top, Math.Min(w.Top, Math.Max(a.Top, a.Bottom - w.Height)));
        }

        // Distance from the window's bottom edge to her feet in the given pose (default hover, 12).
        public double SpriteFoot(int pose = 12)
        {
            try
            {
                var origin = scene.TransformToAncestor(w).Transform(new Point(0, 0));
                var offset = VisualTreeHelper.GetOffset(sprite);
                double feet = spriteFeet != null && pose < spriteFeet.Count ? spriteFeet[pose] : 1.0;
                return Math.Max(0.0, w.Height - (origin.Y + offset.Y + sprite.ActualHeight * feet));
            }
            catch { return 18.0; }
        }

        public double VisorOffset()
        {
            try
            {
                var origin = scene.TransformToAncestor(w).Transform(new Point(0, 0));
                var offset = VisualTreeHelper.GetOffset(sprite);
                return origin.Y + offset.Y + sprite.ActualHeight * visorFraction;
            }
            catch { return w.Height * 0.45; }
        }

        // A soft shadow on whatever she stands on; it fades the higher her feet are above it.
        void UpdateShadow(bool grounded, bool onWindow)
        {
            if (!shadowEnabled || !grounded || isHidden) { if (shadow.Visibility != Visibility.Collapsed) shadow.Visibility = Visibility.Collapsed; return; }
            double foot = SpriteFoot(poseIndex);
            double surface = onWindow ? SpriteFoot(12) : 8.0;
            double height = Math.Max(0.0, foot - surface);
            double width = Math.Max(20.0, sprite.ActualWidth * 0.42 * (1.0 - Math.Min(0.5, height / 400.0)));
            shadow.Width = width; shadow.Height = width * 0.16;
            shadow.Margin = new Thickness(0, 0, 0, surface - 18.0 - shadow.Height / 2.0);
            shadow.Opacity = 0.45 * Math.Max(0.0, 1.0 - height / 160.0);
            if (shadow.Visibility != Visibility.Visible) shadow.Visibility = Visibility.Visible;
        }

        // Which of the 8 eye frames looks at the cursor (-1 = ahead), with hysteresis at sector edges.
        int EyeDirection(double cursorX, double cursorY)
        {
            double headX, headY;
            try
            {
                var origin = scene.TransformToAncestor(w).Transform(new Point(0, 0));
                var offset = VisualTreeHelper.GetOffset(sprite);
                headX = w.Left + origin.X + offset.X + sprite.ActualWidth * 0.5;
                headY = w.Top + origin.Y + offset.Y + sprite.ActualHeight * visorFraction;
            }
            catch { return -1; }
            double dx = (cursorX - headX) * Math.Sign(direction), dy = cursorY - headY;
            if (Math.Sqrt(dx * dx + dy * dy) < 45) { eyeDir = -1; return -1; }
            double angle = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            if (eyeDir >= 0)
            {
                double diff = Math.Abs(((angle - eyeDir * 45.0) + 540.0) % 360.0 - 180.0);
                if (diff <= 30) return eyeDir;
            }
            eyeDir = (int)Math.Round(angle / 45.0) % 8;
            return eyeDir;
        }

        public Rect? WindowRectDip(long handle)
        {
            var m = DeviceScale(); if (m == null || handle == 0) return null;
            int[] r = null; try { r = CapriDesktop.WindowBounds(handle); } catch { }
            return FromDeviceRect(r, m.Value);
        }

        // A window with room above its title bar for Capri to sit on.
        public KeyValuePair<long, Rect>? FindPerchWindow()
        {
            var candidates = new List<long>();
            try { var fg = CapriDesktop.ForegroundWindow(); if (fg != null) candidates.Add(fg[0]); } catch { }
            if (lastAppWindow != 0) candidates.Add(lastAppWindow);
            var m = DeviceScale(); if (m == null) return null;
            foreach (var handle in candidates)
            {
                var r = WindowRectDip(handle);
                if (r == null || r.Value.Width < 300 || r.Value.Height < 150) continue;
                var area = WorkAreaAtDip(r.Value.Left + r.Value.Width / 2, r.Value.Top + 5);
                if (area == null) continue;
                if (r.Value.Top - (w.Height - SpriteFoot()) < area.Value.Top) continue;
                return new KeyValuePair<long, Rect>(handle, r.Value);
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
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
            // the transparent space above her head may go off the top of the screen
            double above = Math.Max(0.0, EmptyAbove(poseIndex) - 4);
            w.Top = Math.Max(a.Top - above, Math.Min(w.Top, Math.Max(a.Top - above, FloorTop(a))));
        }

        // Where the drawing starts in each frame (fraction of the frame height from the top).
        Dictionary<int, double> artTops = new Dictionary<int, double>();
        public double ArtTop(int pose)
        {
            double v;
            if (artTops.TryGetValue(pose, out v)) return v;
            v = 0.0;
            try
            {
                var bmp = new FormatConvertedBitmap(Frame(pose), PixelFormats.Bgra32, null, 0);
                int wd = bmp.PixelWidth, ht = bmp.PixelHeight, stride = wd * 4;
                var px = new byte[stride * ht]; bmp.CopyPixels(px, stride, 0);
                for (int y = 0; y < ht && v == 0.0; y++)
                {
                    int solid = 0;
                    for (int x = 0; x < wd; x++) if (px[y * stride + x * 4 + 3] > 60 && ++solid >= 3) { v = (double)y / ht; break; }
                }
            }
            catch { v = 0.25; }
            artTops[pose] = v; return v;
        }
        // Middle of the drawing's bounding box (fractions of the frame), for spinning around her.
        Dictionary<int, Point> artCenters = new Dictionary<int, Point>();
        public Point ArtCenter(int pose)
        {
            Point c;
            if (artCenters.TryGetValue(pose, out c)) return c;
            c = new Point(0.5, 0.58);
            try
            {
                var bmp = new FormatConvertedBitmap(Frame(pose), PixelFormats.Bgra32, null, 0);
                int wd = bmp.PixelWidth, ht = bmp.PixelHeight, stride = wd * 4;
                var px = new byte[stride * ht]; bmp.CopyPixels(px, stride, 0);
                int x0 = wd, x1 = -1, y0 = ht, y1 = -1;
                for (int y = 0; y < ht; y += 2)
                    for (int x = 0; x < wd; x += 2)
                        if (px[y * stride + x * 4 + 3] > 60) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 >= 0) c = new Point((x0 + x1) / 2.0 / wd, (y0 + y1) / 2.0 / ht);
            }
            catch { }
            artCenters[pose] = c; return c;
        }

        // Rotate her around the middle of the drawing instead of the middle of the frame, so a full
        // spin stays inside her window at (almost) full size.
        public void SpinAround(double angle, double size)
        {
            var c = ArtCenter(poseIndex);
            double W = sprite.ActualWidth, H = sprite.ActualHeight;
            double sx = size * Math.Sign(direction == 0 ? 1 : direction) * facingFlip.ScaleX;
            double px = (c.X - 0.5) * W * sx, py = (c.Y - 0.5) * H * size;
            double a = angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            rotation.Angle = angle; scale.ScaleX = Math.Sign(direction == 0 ? 1 : direction) * size; scale.ScaleY = size;
            shift.X = px - (px * cos - py * sin); shift.Y = py - (px * sin + py * cos);
        }

        // Transparent space between the top of her window and the top of the drawing.
        public double EmptyAbove(int pose)
        {
            try
            {
                if (sprite.ActualHeight <= 0 || pose < 0 || pose >= poses.Count) return 0.0;
                var origin = scene.TransformToAncestor(w).Transform(new Point(0, 0));
                var offset = VisualTreeHelper.GetOffset(sprite);
                return origin.Y + offset.Y + ArtTop(pose) * sprite.ActualHeight;
            }
            catch { return 0.0; }
        }
        // The window top that stands her feet (in the given pose) on the bottom of the work area.
        // The drawings have empty space under the feet, different in every pose.
        public double FloorTop(Rect a, int pose = -1) { return a.Bottom - w.Height + Math.Max(0.0, SpriteFoot(pose < 0 ? poseIndex : pose) - 3); }
        public int PoseFor(string st) { int p; return newSprites && animPoses.TryGetValue(st, out p) ? p : poseIndex; }
        public int PerchPose { get { return AnimPose("windowperch", 12); } }
        // How much room she needs above a window's top edge to sit on it (just her drawn height).
        public double PerchRoom() { return w.Height - SpriteFoot(PerchPose) - EmptyAbove(PerchPose) + 4; }

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
            double surface = onWindow ? SpriteFoot(PerchPose) : Math.Max(0.0, w.Top + w.Height - WorkArea().Bottom);
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

        // A window with room above its title bar for Capri to sit on: the one she was dropped on,
        // the active one, the last one used, then any other visible window whose top edge isn't covered.
        public long preferPerch;
        public KeyValuePair<long, Rect>? FindPerchWindow() => FindWindowWithRoom(PerchRoom());
        // Peeking needs only room for her head over the edge (her body hides behind the window).
        // Peeking: she hides behind the left or right edge of a window and leans her head out.
        public struct PeekSpot { public long handle; public Rect rect; public int side; }
        public PeekSpot? FindPeekWindow()
        {
            if (DeviceScale() == null) return null;
            var first = new List<long>();
            try { var fg = CapriDesktop.ForegroundWindow(); if (fg != null) first.Add(fg[0]); } catch { }
            if (lastAppWindow != 0 && !first.Contains(lastAppWindow)) first.Add(lastAppWindow);
            long[] all = new long[0]; try { all = CapriDesktop.AppWindows(); } catch { }
            var stack = new List<KeyValuePair<long, Rect>>();
            foreach (var h in all) { var rr = WindowRectDip(h); if (rr != null) stack.Add(new KeyValuePair<long, Rect>(h, rr.Value)); }
            var order = first.Concat(all.Where(h => !first.Contains(h))).ToList();
            var ok = new List<PeekSpot>();
            foreach (var h in order)
            {
                if (h == 0 || CapriWindows.Maximized(h)) continue;
                var r = WindowRectDip(h); if (r == null || r.Value.Width < 200 || r.Value.Height < 180) continue;
                var area = WorkAreaAtDip(r.Value.Left + r.Value.Width / 2, r.Value.Top + r.Value.Height / 2); if (area == null) continue;
                double y = r.Value.Top + r.Value.Height * 0.35;
                if (y < area.Value.Top + 50 || y > area.Value.Bottom - 50) continue;
                int at = stack.FindIndex(z => z.Key == h);
                foreach (int side in new[] { 1, -1 })
                {
                    double edge = side > 0 ? r.Value.Right : r.Value.Left;
                    if (side > 0 ? edge + 70 > area.Value.Right : edge - 70 < area.Value.Left) continue;
                    var outside = new Point(edge + side * 25, y); var inside = new Point(edge - side * 6, y);
                    bool covered = at > 0 && stack.Take(at).Any(z => z.Value.Contains(outside) || z.Value.Contains(inside));
                    if (!covered) ok.Add(new PeekSpot { handle = h, rect = r.Value, side = side });
                }
                if (ok.Count >= 6) break;
            }
            if (ok.Count == 0) return null;
            var mine = ok.Where(p => first.Count > 0 && p.handle == first[0]).ToList();
            return mine.Count > 0 && rng.NextDouble() < 0.6 ? mine[rng.Next(mine.Count)] : ok[rng.Next(ok.Count)];
        }

        KeyValuePair<long, Rect>? FindWindowWithRoom(double room)
        {
            var m = DeviceScale(); if (m == null) return null;
            Func<long, Rect?> fits = h => FitsWithRoom(h, room);
            if (preferPerch != 0) { long p = preferPerch; preferPerch = 0; var r = fits(p); if (r != null) return new KeyValuePair<long, Rect>(p, r.Value); }
            var first = new List<long>();
            try { var fg = CapriDesktop.ForegroundWindow(); if (fg != null) first.Add(fg[0]); } catch { }
            if (lastAppWindow != 0) first.Add(lastAppWindow);
            foreach (var h in first) { var r = fits(h); if (r != null) return new KeyValuePair<long, Rect>(h, r.Value); }
            long[] all = new long[0]; try { all = CapriDesktop.AppWindows(); } catch { }
            var above = new List<Rect>(); var ok = new List<KeyValuePair<long, Rect>>();
            foreach (var h in all)
            {
                var rr = WindowRectDip(h); if (rr == null) continue;
                var r = fits(h);
                if (r != null)
                {
                    // skip it if a window in front covers the middle of its top edge
                    var mid = new Point(r.Value.Left + r.Value.Width / 2, r.Value.Top + 2);
                    if (!above.Any(o => o.Contains(mid))) ok.Add(new KeyValuePair<long, Rect>(h, r.Value));
                }
                above.Add(rr.Value);
                if (ok.Count >= 4) break;
            }
            if (ok.Count == 0) return null;
            return ok[rng.Next(ok.Count)];
        }

        public Rect? PerchFits(long handle) => FitsWithRoom(handle, PerchRoom());
        Rect? FitsWithRoom(long handle, double room)
        {
            if (handle == 0 || CapriWindows.Maximized(handle)) return null;
            var r = WindowRectDip(handle);
            if (r == null || r.Value.Width < 260 || r.Value.Height < 120) return null;
            var area = WorkAreaAtDip(r.Value.Left + r.Value.Width / 2, r.Value.Top + 5);
            if (area == null || r.Value.Top - room < area.Value.Top || r.Value.Top > area.Value.Bottom - 40) return null;
            return r;
        }

        // Dropped near the top edge of a window: sit on it there.
        public bool TryDropPerch()
        {
            try
            {
                double feet = w.Top + w.Height - SpriteFoot(poseIndex), cx = w.Left + w.Width / 2;
                foreach (var h in CapriDesktop.AppWindows())
                {
                    var r = WindowRectDip(h); if (r == null) continue;
                    bool overX = cx > r.Value.Left + 10 && cx < r.Value.Right - 10;
                    if (!overX) continue;
                    if (feet > r.Value.Top - 70 && feet < r.Value.Top + 45)
                    {
                        if (PerchFits(h) == null) { ShowStatus("No room above that window for her to sit.", 3); return false; }
                        preferPerch = h; perchDropX = cx - w.Width / 2 - r.Value.Left;
                        BeginState("windowperch"); return true;
                    }
                    if (r.Value.Contains(new Point(cx, feet))) return false;   // dropped onto the middle of a window
                }
            }
            catch { }
            return false;
        }
        public double perchDropX = double.NaN;
    }
}

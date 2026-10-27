using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static System.Math;

namespace Capri
{
    // Animation polish on top of each animation's own motion: a springy pop when an animation starts,
    // a squash when she lands, a lean into fast moves with afterimages, and small particles
    // (z's, sparkles, confetti, dust, steam...) for the animations that suit them.
    class Particle
    {
        public FrameworkElement el; public double x, y, vx, vy, gravity, age, life, spin, angle, size = 1, grow, wobble;
        public bool tied; public string owner;
    }

    partial class Pet
    {
        public bool effectsEnabled = true;
        Canvas fxCanvas; readonly List<Particle> particles = new List<Particle>();
        Image[] trail = new Image[3]; ScaleTransform[] trailScale = new ScaleTransform[3]; RotateTransform[] trailRot = new RotateTransform[3]; TranslateTransform[] trailShift = new TranslateTransform[3];
        readonly List<double[]> posHistory = new List<double[]>();
        double velX, velY, landAt = -10, lastLeft = double.NaN, lastTop = double.NaN;
        readonly Dictionary<string, double> fxNext = new Dictionary<string, double>();
        readonly HashSet<string> fxOnce = new HashSet<string>();
        string fxState = "";
        static readonly Brush Pink = Frozen("#FF2A6D"), Green = Frozen("#1EF07A"), Cyan = Frozen("#5EF6FF"), Yellow = Frozen("#FCEE0A"), Red = Frozen("#FF1F3D"), White = Frozen("#F4FBFF"), Purple = Frozen("#B44CFF"), Dust = Frozen("#C9B8C8");
        static Brush Frozen(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }

        void InitJuice()
        {
            fxCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
            Grid.SetRow(fxCanvas, 1);
            scene.Children.Add(fxCanvas);
            int at = scene.Children.IndexOf(sprite);
            for (int i = 0; i < trail.Length; i++)
            {
                trailScale[i] = new ScaleTransform(); trailRot[i] = new RotateTransform(); trailShift[i] = new TranslateTransform();
                var g = new TransformGroup(); g.Children.Add(trailScale[i]); g.Children.Add(trailRot[i]); g.Children.Add(trailShift[i]);
                trail[i] = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = g, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                Grid.SetRow(trail[i], 1);
                scene.Children.Insert(at, trail[i]);
            }
        }

        // Called once a frame after the animation's own motion has been applied.
        void ApplyJuice(double t, double dt)
        {
            if (fxState != state) { fxState = state; fxNext.Clear(); fxOnce.Clear(); KillTiedParticles(); }
            // Where she is moving, measured from the window itself.
            if (!double.IsNaN(lastLeft) && dt > 0)
            {
                double vx = (w.Left - lastLeft) / dt, vy = (w.Top - lastTop) / dt;
                if (Abs(vx) > 6000 || Abs(vy) > 6000) { vx = 0; vy = 0; } // a jump between screens
                if (velY > 380 && Abs(vy) < 60 && !dragging) Land(t);
                velX += (vx - velX) * Min(1.0, dt * 14); velY += (vy - velY) * Min(1.0, dt * 14);
            }
            lastLeft = w.Left; lastTop = w.Top;
            posHistory.Add(new[] { t, w.Left, w.Top });
            while (posHistory.Count > 0 && t - posHistory[0][0] > 0.2) posHistory.RemoveAt(0);
            if (!effectsEnabled) { HideTrail(); return; }

            bool wild = spec.Has("noWobble") || state == "thrown" || state == "spin" || state == "tumble";
            // A springy pop when an animation starts.
            if (!wild && phase < 0.6)
            {
                double k = 0.07 * Exp(-phase * 7) * Cos(phase * 18);
                scale.ScaleY *= 1 + k; scale.ScaleX *= 1 - k * 0.6;
            }
            // Squash on landing, then a wobble back.
            double sinceLand = t - landAt;
            if (sinceLand < 0.7)
            {
                double k = 0.13 * Exp(-sinceLand * 8) * Cos(sinceLand * 17);
                scale.ScaleY *= 1 - k; scale.ScaleX *= 1 + k * 0.7; shift.Y += k * 40;
            }
            // Lean into fast moves and stretch a little along them.
            double speed = Sqrt(velX * velX + velY * velY);
            if (!wild && state != "windowperch")
            {
                rotation.Angle += Max(-10.0, Min(10.0, velX / 55));
                scale.ScaleY *= 1 + Min(0.06, speed / 10000);
            }
            UpdateTrail(t, speed);
            Emit(t);
            UpdateParticles(dt);
        }

        void Land(double t)
        {
            if (t - landAt < 0.4) return;
            landAt = t;
            if (!effectsEnabled) return;
            for (int i = 0; i < 7; i++)
            {
                var p = Body(0.5, -1);
                double side = i % 2 == 0 ? 1 : -1;
                Spawn(Shard(Dust, 7 + rng.Next(6)), p.X + side * rng.Next(5, 25), p.Y, side * (60 + rng.Next(90)), -30 - rng.Next(50), 0.55, 160);
            }
        }

        void UpdateTrail(double t, double speed)
        {
            bool fast = speed > 520 && !isHidden && sprite.Opacity > 0.5;
            for (int i = 0; i < trail.Length; i++)
            {
                if (!fast) { trail[i].Visibility = Visibility.Collapsed; continue; }
                double lag = 0.035 * (i + 1);
                var past = posHistory.LastOrDefault(h => t - h[0] >= lag) ?? posHistory.FirstOrDefault();
                if (past == null) { trail[i].Visibility = Visibility.Collapsed; continue; }
                trail[i].Source = sprite.Source;
                trail[i].Width = sprite.ActualWidth; trail[i].Height = sprite.ActualHeight;
                trailScale[i].ScaleX = scale.ScaleX * facingFlip.ScaleX; trailScale[i].ScaleY = scale.ScaleY; trailRot[i].Angle = rotation.Angle;
                trailShift[i].X = shift.X + (past[1] - w.Left); trailShift[i].Y = shift.Y + (past[2] - w.Top);
                trail[i].Opacity = Min(1.0, (speed - 520) / 600) * new[] { 0.32, 0.18, 0.09 }[i];
                trail[i].Visibility = Visibility.Visible;
            }
        }
        void HideTrail() { foreach (var i in trail) i.Visibility = Visibility.Collapsed; foreach (var p in particles) fxCanvas.Children.Remove(p.el); particles.Clear(); }

        // A point on her drawing (fractions of the sprite canvas), in effect-canvas coordinates.
        // y < 0 means her feet.
        Point Body(double fx, double fy)
        {
            if (fy < 0) fy = poseIndex < spriteFeet.Count ? spriteFeet[poseIndex] - 0.02 : 0.9;
            try { return sprite.TransformToVisual(fxCanvas).Transform(new Point(fx * sprite.ActualWidth, fy * sprite.ActualHeight)); }
            catch { return new Point(fx * sprite.ActualWidth, fy * sprite.ActualHeight); }
        }
        double Unit => Max(0.4, sprite.ActualHeight / 430.0);

        bool Every(string key, double interval)
        {
            double next;
            if (!fxNext.TryGetValue(key, out next)) { fxNext[key] = phase + interval; return true; }
            if (phase < next) return false;
            fxNext[key] = phase + interval; return true;
        }
        bool Once(string key, double at) { if (phase < at || fxOnce.Contains(key)) return false; fxOnce.Add(key); return true; }

        FrameworkElement Glyph(string text, Brush brush, double size)
        {
            return new TextBlock { Text = text, Foreground = brush, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = size * Unit, RenderTransformOrigin = new Point(0.5, 0.5) };
        }
        FrameworkElement Shard(Brush brush, double size)
        {
            double s = size * Unit;
            return new Polygon { Fill = brush, Points = new PointCollection { new Point(0, s * 0.5), new Point(s * 0.35, 0), new Point(s * 0.7, s * 0.5), new Point(s * 0.35, s * 1.3) }, RenderTransformOrigin = new Point(0.5, 0.5) };
        }
        FrameworkElement Ring(Brush brush, double size)
        {
            double s = size * Unit;
            return new Ellipse { Width = s, Height = s, Stroke = brush, StrokeThickness = 2.5, RenderTransformOrigin = new Point(0.5, 0.5) };
        }
        FrameworkElement Bit(Brush brush, double wdt, double hgt)
        {
            return new Rectangle { Width = wdt * Unit, Height = hgt * Unit, Fill = brush, RenderTransformOrigin = new Point(0.5, 0.5) };
        }

        Particle Spawn(FrameworkElement el, double x, double y, double vx, double vy, double life, double gravity = 0, double spin = 0, double grow = 0, bool tied = false)
        {
            if (particles.Count > 70) { var old = particles[0]; fxCanvas.Children.Remove(old.el); particles.RemoveAt(0); }
            el.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform() } };
            var u = Unit;
            var p = new Particle { el = el, x = x, y = y, vx = vx * u, vy = vy * u, life = life, gravity = gravity * u, spin = spin, grow = grow, tied = tied, angle = rng.Next(-20, 20) };
            el.IsHitTestVisible = false; el.Opacity = 0;
            fxCanvas.Children.Add(el); particles.Add(p);
            return p;
        }

        void KillTiedParticles()
        {
            foreach (var p in particles.Where(x => x.tied).ToList()) { fxCanvas.Children.Remove(p.el); particles.Remove(p); }
        }

        void UpdateParticles(double dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.age += dt;
                if (p.age >= p.life) { fxCanvas.Children.Remove(p.el); particles.RemoveAt(i); continue; }
                p.vy += p.gravity * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.angle += p.spin * dt;
                double k = p.age / p.life;
                double sway = p.wobble != 0 ? Sin(p.age * 4) * p.wobble * Unit : 0;
                double w0 = p.el.ActualWidth, h0 = p.el.ActualHeight;
                Canvas.SetLeft(p.el, p.x + sway - w0 / 2); Canvas.SetTop(p.el, p.y - h0 / 2);
                p.el.Opacity = Min(1.0, p.age / 0.08) * (k > 0.6 ? (1 - k) / 0.4 : 1);
                var g = (TransformGroup)p.el.RenderTransform;
                double s = p.size * (1 + p.grow * k);
                ((ScaleTransform)g.Children[0]).ScaleX = s; ((ScaleTransform)g.Children[0]).ScaleY = s;
                ((RotateTransform)g.Children[1]).Angle = p.angle;
            }
        }

        double R(double a, double b) => a + rng.NextDouble() * (b - a);
        double Facing => scale.ScaleX >= 0 ? 1 : -1; // +1: drawn as is, -1: mirrored

        // The particles each animation makes.
        void Emit(double t)
        {
            if (busyMode || isHidden || paused) return;
            Point head = Body(0.5, visorFraction - 0.04), top = Body(0.5, Min(0.4, ArtTop(poseIndex) + 0.02)), chest = Body(0.5, 0.55), feet = Body(0.5, -1);
            switch (state)
            {
                case "nap":
                    if (Every("z", 1.4)) { var p = Spawn(Glyph("z", White, R(14, 22)), head.X + 40 * Unit * Facing, head.Y - 20, 14 * Facing, -26, 3.0, 0, 0, 0.6); p.wobble = 6; }
                    break;
                case "sleepy":
                    if (Every("z", 2.4)) { var p = Spawn(Glyph("z", White, 14), head.X + 35 * Unit * Facing, head.Y - 15, 10 * Facing, -20, 2.4, 0, 0, 0.4); p.wobble = 5; }
                    break;
                case "yawn":
                    if (Once("puff", 0.6)) Spawn(Ring(White, 14), head.X, head.Y + 30 * Unit, 0, -10, 0.9, 0, 0, 2.5);
                    if (phase > 1.6 && Every("z", 2.2)) Spawn(Glyph("z", White, 13), head.X + 30 * Unit * Facing, head.Y - 15, 10 * Facing, -22, 2.0, 0, 0, 0.4).wobble = 4;
                    break;
                case "meditate":
                    if (Every("aura", 0.45)) Spawn(Shard(Green, R(6, 10)), chest.X + R(-70, 70) * Unit, feet.Y - R(0, 40) * Unit, 0, -R(25, 45), 2.2);
                    if (Every("ring", 2.6)) Spawn(Ring(Green, 30), chest.X, chest.Y, 0, 0, 1.6, 0, 0, 4);
                    break;
                case "heartbeat":
                    if (Every("pulse", 0.75)) Spawn(Ring(Pink, 26), chest.X, chest.Y, 0, 0, 0.9, 0, 0, 3.5);
                    break;
                case "headpat":
                    if (Every("heart", 0.5)) Spawn(Glyph("♥", Pink, R(14, 20)), head.X + R(-45, 45) * Unit, head.Y - R(10, 40) * Unit, R(-10, 10), -R(35, 60), 1.3, 0, R(-20, 20), 0.3).wobble = 4;
                    if (Every("spark", 0.3)) Spawn(Glyph("✦", Yellow, R(10, 16)), top.X + R(-50, 50) * Unit, top.Y + R(0, 40) * Unit, R(-15, 15), -R(30, 55), 1.0, 0, R(-90, 90));
                    break;
                case "celebrate":
                case "gamerwin":
                    if (Every("confetti", 0.55)) Confetti(top, 8);
                    break;
                case "cheers":
                    if (Once("pop", 1.2)) { Confetti(top, 14); Spawn(Glyph("✦", Yellow, 18), top.X, top.Y, 0, -30, 0.9, 0, 120); }
                    break;
                case "bow":
                    if (Once("ta", 1.4)) for (int i = 0; i < 6; i++) Spawn(Glyph("✦", i % 2 == 0 ? Yellow : Cyan, R(10, 15)), head.X + R(-60, 60) * Unit, head.Y + R(-40, 20) * Unit, R(-30, 30), -R(30, 70), 1.0, 40, R(-90, 90));
                    break;
                case "laughing":
                    if (Every("ha", 0.42)) { double side = rng.Next(2) == 0 ? -1 : 1; Spawn(Glyph("ha", Pink, R(13, 17)), head.X + side * 55 * Unit, head.Y - 10, side * 40, -40, 0.9, 30, side * 30); }
                    break;
                case "giggle":
                    if (Every("hi", 0.6)) { double side = rng.Next(2) == 0 ? -1 : 1; Spawn(Glyph("hi", Pink, 12), head.X + side * 50 * Unit, head.Y, side * 30, -35, 0.8, 30); }
                    break;
                case "dizzy":
                    if (Every("star", 0.16)) { double a = phase * 5; Spawn(Glyph("✦", Yellow, 13), head.X + Cos(a) * 55 * Unit, head.Y - 35 * Unit + Sin(a) * 14 * Unit, 0, 0, 0.35, 0, 0, -0.3); }
                    break;
                case "failedattack":
                    if (Once("burst", 0.4)) for (int i = 0; i < 8; i++) Spawn(Shard(Red, R(7, 11)), chest.X, chest.Y, R(-160, 160), R(-160, 40), 0.7, 260, R(-300, 300));
                    if (phase > 1.4 && Every("star", 0.18)) { double a = phase * 5; Spawn(Glyph("✦", Yellow, 12), head.X + Cos(a) * 50 * Unit, head.Y - 30 * Unit + Sin(a) * 12 * Unit, 0, 0, 0.35, 0, 0, -0.3); }
                    break;
                case "shiver":
                    if (Every("ice", 0.14)) { double side = rng.Next(2) == 0 ? -1 : 1; Spawn(Shard(Cyan, R(5, 8)), chest.X + side * R(40, 70) * Unit, chest.Y + R(-60, 40) * Unit, side * R(20, 50), R(-20, 20), 0.45, 60); }
                    if (Every("brr", 1.8)) Spawn(Glyph("brr", Cyan, 13), head.X - 60 * Unit * Facing, head.Y - 20, -20 * Facing, -25, 1.2);
                    break;
                case "wilt":
                    if (Every("sweat", 0.9)) Spawn(Glyph("◆", Cyan, 11), head.X + R(-50, 50) * Unit, head.Y + R(-10, 20) * Unit, R(-20, 20), -40, 1.0, 320);
                    if (Every("heat", 1.1)) Spawn(Glyph("~", Yellow, 18), top.X + R(-60, 60) * Unit, top.Y, 0, -30, 1.6, 0, 0, 0.3).wobble = 8;
                    break;
                case "sip":
                    if (Every("steam", 0.7)) Spawn(Glyph("~", White, 16), head.X - 30 * Unit * Facing, head.Y + 20 * Unit, 0, -28, 1.5, 0, 0, 0.4).wobble = 6;
                    break;
                case "charge":
                    if (Every("in", 0.06))
                    {
                        double a = R(0, 2 * PI), r = 95 * Unit;
                        Spawn(Shard(rng.Next(2) == 0 ? Yellow : Green, 6), chest.X + Cos(a) * r, chest.Y + Sin(a) * r, -Cos(a) * 150, -Sin(a) * 150, 0.6, 0, 200);
                    }
                    if (Once("full", 5)) for (int i = 0; i < 12; i++) { double a = i * PI / 6; Spawn(Shard(Yellow, 9), chest.X, chest.Y, Cos(a) * 200, Sin(a) * 200, 0.6, 0, 300); }
                    break;
                case "rocket":
                    if (Every("fire", 0.04)) Spawn(Shard(rng.Next(3) == 0 ? Yellow : Pink, R(6, 10)), feet.X + R(-15, 15) * Unit, feet.Y, R(-30, 30), R(120, 220), 0.4, 0, R(-200, 200), -0.6);
                    if (Once("launch", 0.7)) Spawn(Ring(White, 30), feet.X, feet.Y, 0, 0, 0.7, 0, 0, 3);
                    break;
                case "dash":
                case "zigzag":
                case "figure8":
                case "orbit":
                    if (Every("streak", 0.05) && Abs(velX) + Abs(velY) > 250)
                    {
                        double len = Sqrt(velX * velX + velY * velY);
                        var bit = Bit(state == "orbit" ? Purple : Cyan, 26, 2.5);
                        var p = Spawn(bit, chest.X - velX / len * 50 * Unit + R(-30, 30) * Unit, chest.Y - velY / len * 50 * Unit + R(-60, 40) * Unit, 0, 0, 0.3);
                        p.angle = Atan2(velY, velX) * 180 / PI;
                    }
                    break;
                case "hop":
                    if (Every("hopdust", PI / 5)) StepDust(feet, 3);
                    break;
                case "grumpy":
                    if (Every("stomp", PI / 5)) StepDust(feet, 4);
                    if (Every("mad", 1.5)) Spawn(Glyph("#", Red, 18), head.X + 45 * Unit * Facing, head.Y - 40 * Unit, 0, -15, 0.8, 0, 0, 0.3);
                    break;
                case "patrol":
                    if (Every("step", 0.35)) StepDust(feet, 2);
                    break;
                case "tiptoe":
                case "sneak":
                    if (Every("step", 0.45)) Spawn(Shard(Dust, 4), feet.X + R(-10, 10) * Unit, feet.Y, R(-15, 15), -10, 0.4);
                    break;
                case "spin":
                case "tumble":
                    if (Every("trail", 0.07)) Spawn(Glyph("✦", rng.Next(2) == 0 ? White : Cyan, R(8, 13)), chest.X + R(-50, 50) * Unit, chest.Y + R(-60, 60) * Unit, 0, 0, 0.6, 0, 90);
                    break;
                case "glitch":
                case "teleport":
                case "possess":
                case "casting":
                case "repaircursor":
                case "sabotage":
                case "arrive":
                    if (Every("bw", 0.11))
                    {
                        if (rng.Next(3) == 0) Spawn(Bit(rng.Next(2) == 0 ? Red : Frozen("#E0090509"), R(40, 90), R(2, 5)), chest.X + R(-40, 40) * Unit, chest.Y + R(-160, 120) * Unit, 0, 0, 0.12);
                        else Spawn(rng.Next(2) == 0 ? Shard(Red, R(5, 9)) : Glyph(rng.Next(2) == 0 ? "0" : "1", Red, R(10, 14)), chest.X + R(-80, 80) * Unit, chest.Y + R(-120, 80) * Unit, R(-40, 40), -R(20, 60), 0.7, 0, R(-120, 120));
                    }
                    break;
                case "scan":
                case "scanwindow":
                case "filescan":
                    if (Once("line", 0)) { var p = Spawn(Bit(Green, 150, 3), head.X, head.Y, 0, 0, 999, 0, 0, 0, true); p.el.Opacity = 0.8; }
                    var line = particles.FirstOrDefault(p => p.tied);
                    if (line != null) { var c = Body(0.5, 0.2 + 0.6 * (0.5 + 0.5 * Sin(phase * 2.2))); line.x = c.X; line.y = c.Y; line.vx = 0; line.vy = 0; line.age = 0.3; }
                    break;
                case "rain":
                    if (Every("drop", 0.12)) Spawn(Glyph(rng.Next(2) == 0 ? "0" : "1", Green, R(10, 14)), top.X + R(-120, 120) * Unit, top.Y - 40 * Unit, 0, R(90, 140), 1.6);
                    break;
                case "gaming":
                    if (Every("px", 0.7)) Spawn(Bit(rng.Next(2) == 0 ? Cyan : Yellow, 6, 6), chest.X + R(-20, 20) * Unit, chest.Y, R(-30, 30), -R(40, 70), 0.8, 60);
                    break;
                case "bossfight":
                    if (Every("hit", 0.3)) Spawn(Shard(rng.Next(2) == 0 ? Red : Yellow, R(6, 9)), chest.X + R(-25, 25) * Unit, chest.Y, R(-90, 90), -R(60, 140), 0.6, 300, R(-300, 300));
                    break;
                case "dance":
                    if (Every("note", 0.45)) { double side = rng.Next(2) == 0 ? -1 : 1; Spawn(Glyph("♪", rng.Next(2) == 0 ? Pink : Cyan, R(14, 20)), head.X + side * 60 * Unit, head.Y, side * 15, -40, 1.3, 0, side * 20).wobble = 5; }
                    break;
                case "remind":
                    if (Every("bang", 0.9)) Spawn(Glyph("!", Yellow, 24), top.X, top.Y, 0, -20, 0.7, 0, 0, 0.5);
                    break;
                case "attentionbait":
                    if (Every("psst", 1.2)) Spawn(Glyph("psst", Cyan, 12), head.X + 55 * Unit * Facing, head.Y - 20, 10 * Facing, -15, 1.0);
                    break;
                case "baitjudge":
                case "sideglance":
                    if (Once("dots", 0.8)) Spawn(Glyph("...", White, 16), top.X + 30 * Unit * Facing, top.Y + 20 * Unit, 0, -8, 1.6);
                    break;
                case "peek":
                case "peekbehind":
                case "upsidedown":
                    if (Every("q", 3.0)) Spawn(Glyph("?", Yellow, 20), top.X + 40 * Unit * Facing, top.Y + 20 * Unit, 0, -15, 1.0, 0, 0, 0.3);
                    break;
                case "caught":
                    if (Once("bang", 0.05)) Spawn(Glyph("!", Yellow, 26), top.X, top.Y + 20 * Unit, 0, -30, 0.8, 0, 0, 0.6);
                    break;
                case "shaken":
                    if (Every("fling", 0.2)) { double side = rng.Next(2) == 0 ? -1 : 1; Spawn(Shard(Cyan, 6), head.X + side * 40 * Unit, head.Y, side * R(80, 150), -R(40, 100), 0.6, 400); }
                    break;
                case "impact":
                case "swat":
                case "ballbat":
                    if (Once("hit", 0.08)) { var c = Body(0.5 - 0.3 * Facing, 0.5); Spawn(Ring(state == "impact" ? Red : Yellow, 26), c.X, c.Y, 0, 0, 0.45, 0, 0, 2.5); for (int i = 0; i < 6; i++) { double a = i * PI / 3; Spawn(Shard(Yellow, 7), c.X, c.Y, Cos(a) * 170, Sin(a) * 170, 0.4, 0, 300); } }
                    break;
                case "stretch":
                case "breaktime":
                    if (Every("pop", 1.2)) Spawn(Glyph("✧", Yellow, 14), top.X + R(-40, 40) * Unit, top.Y, 0, -25, 0.9, 0, 60);
                    break;
                case "wave":
                case "welcomeback":
                case "friendwave":
                    if (Every("hi", 0.6)) { var c = Body(0.5 - 0.28 * Facing, 0.32); Spawn(Glyph("✦", Yellow, R(9, 13)), c.X + R(-15, 15) * Unit, c.Y + R(-15, 15) * Unit, 0, -25, 0.7, 0, 90); }
                    break;
                case "smug":
                    if (Every("glint", 3.0)) Spawn(Glyph("✦", White, 15), head.X + 30 * Unit * Facing, head.Y - 5 * Unit, 0, 0, 0.6, 0, 180, 0.6);
                    break;
            }
        }

        void Confetti(Point at, int count)
        {
            var colors = new[] { Pink, Green, Cyan, Yellow, Purple };
            for (int i = 0; i < count; i++)
                Spawn(Bit(colors[rng.Next(colors.Length)], R(5, 8), R(8, 12)), at.X + R(-30, 30) * Unit, at.Y, R(-150, 150), -R(120, 260), 1.4, 420, R(-400, 400));
        }

        void StepDust(Point feet, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double side = i % 2 == 0 ? 1 : -1;
                Spawn(Shard(Dust, R(5, 8)), feet.X + side * R(5, 25) * Unit, feet.Y, side * R(40, 90), -R(10, 40), 0.45, 120);
            }
        }
    }
}

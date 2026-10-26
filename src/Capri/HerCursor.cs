using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static System.Math;

namespace Capri
{
    // Her own cursor: a neon pointer that she flies around your screen for a while, "clicking" on
    // things with a little ripple and a tag. It's only a picture in a click-through window: your
    // real cursor and your apps are never touched.
    partial class Pet
    {
        Window herCursorWin; Canvas herCursorCanvas; Polygon herArrow; TranslateTransform herArrowShift;
        TextBlock herCursorTag; Border herCursorTagBox;
        readonly List<Shape> herRipples = new List<Shape>();
        public bool herCursorActive; public bool herCursorEnabled = true;
        double herCursorUntil, herLegStart, herLegLength, herNextClick, herClickAt = -10;
        Point herFrom, herTo, herPos;
        int herLegs;
        static readonly string[] HerTags = { "> ACCESS", "> MINE", "> SCAN", "> PING", "> HACKED", "> NOPE", "> OPEN", "> 0x1F", "> CLICK" };
        const double HerWinSize = 220;

        void InitHerCursor()
        {
            herCursorCanvas = new Canvas { Width = HerWinSize, Height = HerWinSize, IsHitTestVisible = false };
            herArrowShift = new TranslateTransform();
            // Angular arrow in her colours: hot pink body, green edge, a glitchy offset ghost.
            var pts = new PointCollection { new Point(0, 0), new Point(0, 30), new Point(8, 23), new Point(13, 35), new Point(19, 32), new Point(14, 21), new Point(24, 21) };
            var ghost = new Polygon { Points = pts, Fill = Frozen("#665EF6FF"), RenderTransform = new TranslateTransform(-3, 1) };
            herArrow = new Polygon { Points = pts, Fill = Frozen("#FF2A6D"), Stroke = Frozen("#1EF07A"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Miter };
            herArrow.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 42, 109), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
            var group = new Canvas { RenderTransform = herArrowShift };
            group.Children.Add(ghost); group.Children.Add(herArrow);
            herCursorTag = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Frozen("#FCEE0A") };
            herCursorTagBox = new Border { Child = herCursorTag, Background = Frozen("#E6070A12"), BorderBrush = Frozen("#FF2A6D"), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(5, 1, 5, 1), Visibility = Visibility.Collapsed };
            Canvas.SetLeft(herCursorTagBox, 26); Canvas.SetTop(herCursorTagBox, 30);
            group.Children.Add(herCursorTagBox);
            herCursorCanvas.Children.Add(group);
            herCursorWin = new Window
            {
                Width = HerWinSize, Height = HerWinSize, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Content = herCursorCanvas, Title = "Capri cursor"
            };
            herCursorWin.SourceInitialized += (s, e) => { try { CapriCursor.MakeOverlay(new WindowInteropHelper(herCursorWin).Handle); } catch { } };
        }

        // Where her hand roughly is, in screen DIPs.
        Point HerHand() => new Point(w.Left + w.Width / 2 - direction * w.Width * 0.22, w.Top + w.Height * 0.48);

        public void StartHerCursor(bool manual)
        {
            if (App.Friend || App.SelfTest || herCursorActive) return;
            if (!manual && (busyMode || QuietNow || !CanReact)) return;
            PrepareManualAction(); routine.Clear();
            herCursorActive = true; herCursorUntil = Now + 13 + rng.NextDouble() * 5; herLegs = 0;
            herPos = HerHand(); herFrom = herPos; NextHerLeg();
            BeginState("casting"); until = herCursorUntil + 0.5;
            ShowRemark("hercursor", true);
            try { herCursorWin.Show(); } catch { }
        }

        void NextHerLeg()
        {
            var a = WorkArea();
            herFrom = herPos; herLegStart = Now;
            bool home = Now >= herCursorUntil;
            herTo = home ? HerHand() : new Point(a.Left + 40 + rng.NextDouble() * Max(1.0, a.Width - 120), a.Top + 40 + rng.NextDouble() * Max(1.0, a.Height - 120));
            double dist = Sqrt(Pow(herTo.X - herFrom.X, 2) + Pow(herTo.Y - herFrom.Y, 2));
            herLegLength = Max(0.45, Min(1.6, dist / 900));
            herNextClick = home ? double.PositiveInfinity : herLegStart + herLegLength + 0.15;
            herLegs++;
        }

        void StopHerCursor()
        {
            herCursorActive = false;
            foreach (var r in herRipples) herCursorCanvas.Children.Remove(r);
            herRipples.Clear(); herCursorTagBox.Visibility = Visibility.Collapsed;
            try { herCursorWin.Hide(); } catch { }
        }

        void UpdateHerCursor(double now)
        {
            if (!herCursorActive) return;
            if (isHidden || paused || dragging || exiting || busyMode) { StopHerCursor(); return; }
            double k = Min(1.0, (now - herLegStart) / herLegLength);
            double e = k < 0.5 ? 4 * k * k * k : 1 - Pow(-2 * k + 2, 3) / 2;   // ease in-out
            // a slight curve so it doesn't move like a ruler
            double bend = Sin(k * PI) * 40 * (herLegs % 2 == 0 ? 1 : -1);
            double dx = herTo.X - herFrom.X, dy = herTo.Y - herFrom.Y, len = Max(1.0, Sqrt(dx * dx + dy * dy));
            herPos = new Point(herFrom.X + dx * e - dy / len * bend, herFrom.Y + dy * e + dx / len * bend);
            if (k >= 1)
            {
                if (now >= herNextClick) { HerClick(now); herNextClick = double.PositiveInfinity; }
                if (now - herClickAt > 0.7 || herNextClick == double.PositiveInfinity && now >= herCursorUntil)
                {
                    if (now >= herCursorUntil && Sqrt(Pow(herPos.X - HerHand().X, 2) + Pow(herPos.Y - HerHand().Y, 2)) < 30) { StopHerCursor(); ChooseState(); return; }
                    NextHerLeg();
                }
            }
            // the window sits with the arrow tip near its middle so ripples have room
            herCursorWin.Left = herPos.X - HerWinSize / 2; herCursorWin.Top = herPos.Y - HerWinSize / 2;
            herArrowShift.X = HerWinSize / 2; herArrowShift.Y = HerWinSize / 2;
            double press = Max(0.0, 1 - (now - herClickAt) / 0.18);
            herArrow.RenderTransform = new ScaleTransform(1 - 0.18 * press, 1 - 0.18 * press);
            herArrow.Opacity = 0.85 + 0.15 * Sin(now * 30) * (rng.NextDouble() < 0.06 ? 1 : 0);
            for (int i = herRipples.Count - 1; i >= 0; i--)
            {
                var r = herRipples[i]; double age = now - (double)r.Tag;
                if (age > 0.6) { herCursorCanvas.Children.Remove(r); herRipples.RemoveAt(i); continue; }
                double size = 8 + age * 90; r.Width = size; r.Height = size; r.Opacity = 1 - age / 0.6;
                Canvas.SetLeft(r, HerWinSize / 2 - size / 2); Canvas.SetTop(r, HerWinSize / 2 - size / 2);
            }
            if (herCursorTagBox.Visibility == Visibility.Visible && now - herClickAt > 1.1) herCursorTagBox.Visibility = Visibility.Collapsed;
            // she watches it
            direction = herPos.X < w.Left + w.Width / 2 ? 1.0 : -1.0;
        }

        void HerClick(double now)
        {
            herClickAt = now;
            foreach (var color in new[] { "#FF2A6D", "#5EF6FF" })
            {
                var ring = new Ellipse { Stroke = Frozen(color), StrokeThickness = color == "#FF2A6D" ? 2.5 : 1.5, Tag = now + (color == "#FF2A6D" ? 0 : 0.08) };
                herCursorCanvas.Children.Insert(0, ring); herRipples.Add(ring);
            }
            herCursorTag.Text = HerTags[rng.Next(HerTags.Length)];
            herCursorTagBox.Visibility = Visibility.Visible;
            if (sfxEnabled && rng.NextDouble() < 0.5) PlaySfx("ballhit.wav", 0.35, false);
        }
    }
}

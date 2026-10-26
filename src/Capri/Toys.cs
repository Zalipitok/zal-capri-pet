using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Capri
{
    // The toy ball: throw it around, she fetches it, bats it back, and sometimes steals it.
    partial class Pet
    {
        Window ballWin; ScaleTransform ballSquashT; RotateTransform ballSpin;
        public bool ballShown, ballCarried, ballDragging; public double ballX, ballY, ballVX, ballVY;
        double ballSquash, ballLastHit = -10; int ballHits, ballHitsAllowed = 3;
        List<double[]> ballSamples = new List<double[]>();
        const double BallSize = 44;
        Grid ballGrid;
        // Where the ball sits in the drawings that hold it (fractions of the sprite canvas).
        static readonly Dictionary<string, Point> BallHold = new Dictionary<string, Point>(); // none of the current drawings has a ball in her arms
        double ballStashX, ballStashY;

        public bool BallAvailable => ballShown && !ballCarried && !ballDragging;

        void InitToys()
        {
            ballWin = LoadXaml<Window>("Ball.xml");
            var grid = (Grid)ballWin.FindName("Ball"); ballGrid = grid;
            var group = (TransformGroup)grid.RenderTransform;
            ballSquashT = (ScaleTransform)group.Children[0]; ballSpin = (RotateTransform)group.Children[1];
            ballWin.LocationChanged += (s, e) =>
            {
                if (!ballDragging) return;
                ballSamples.Add(new[] { Now, ballWin.Left, ballWin.Top });
                while (ballSamples.Count > 12) ballSamples.RemoveAt(0);
            };
            ballWin.MouseLeftButtonDown += (s, e) =>
            {
                if (state == "ballguard" && CanReact) { BeginState("grumpy"); ShowRemark("balltaken", true); }
                if (ballCarried)
                {
                    ballCarried = false;
                    if (CanReact) BeginState("grumpy");
                    ShowRemark("balltaken", true);
                }
                ballDragging = true; ballSamples.Clear();
                try { ballWin.DragMove(); } catch { }
                ballDragging = false;
                ballX = ballWin.Left; ballY = ballWin.Top;
                var v = SampleVelocity(ballSamples, Now) ?? new[] { 0.0, 0.0 };
                ballVX = Math.Max(-3500.0, Math.Min(3500.0, v[0])); ballVY = Math.Max(-3500.0, Math.Min(3500.0, v[1]));
                double speed = Math.Sqrt(ballVX * ballVX + ballVY * ballVY);
                if (speed > 300 && CanReact && state != "chaseball" && state != "ballbat" && state != "catchgame")
                {
                    RegisterInteraction(); PrepareManualAction(); routine.Clear(); StartBallChase();
                    if (rng.NextDouble() < 0.5) ShowRemark("ballfetch");
                }
            };
            ballWin.MouseRightButtonUp += (s, e) => SetBallShown(false);
            ballWin.SourceInitialized += (s, e) =>
            {
                try { var h = new WindowInteropHelper(ballWin).Handle; CapriCursor.SetWindowLong(h, -20, CapriCursor.GetWindowLong(h, -20) | 0x08000000 | 0x80); } catch { }
            };

            paths["chaseball"] = () =>
            {
                if (!BallAvailable) { BeginState("smug"); return; }
                double cx = ballX + BallSize / 2, cy = ballY + BallSize / 2, mid = w.Left + w.Width / 2;
                double side = mid < cx ? -1.0 : 1.0;
                targetX = cx - w.Width / 2 + side * w.Width * 0.12;
                targetY = cy + 30 - (w.Height - SpriteFoot(poseIndex));
                if (BallTouch()) HitBall();
            };
            paths["ballsteal"] = () =>
            {
                targetX = ballStashX; targetY = ballStashY;
                if (!ballCarried) { BeginState("grumpy"); return; }
                if (Math.Abs(targetX - w.Left) + Math.Abs(targetY - w.Top) < 8 || phase > 6)
                {
                    ballCarried = false; ballVX = 0; ballVY = 0;
                    BeginState("ballguard");
                    ShowRemark("ballhide", true);
                }
            };
            motions["chaseball"] = () => { rotation.Angle = -direction * 14; scale.ScaleX = direction * 1.04; scale.ScaleY = 0.95; };
            motions["ballsteal"] = () => { rotation.Angle = -direction * 6; shift.Y = -Math.Abs(Math.Sin(phase * 6)) * 5; };
            motions["ballguard"] = () => { rotation.Angle = -6 + Math.Sin(phase * 1.5) * 2; };
        }

        Rect BallArea()
        {
            return WorkAreaAtDip(ballX + BallSize / 2, ballY + BallSize / 2) ?? WorkArea();
        }

        public void SetBallShown(bool on)
        {
            ballShown = on; ballCarried = false;
            if (on)
            {
                ballX = w.Left + w.Width / 2 - BallSize / 2; ballY = w.Top + 90;
                ballVX = (rng.NextDouble() - 0.5) * 500; ballVY = -300;
                ballWin.Left = ballX; ballWin.Top = ballY;
                if (!isHidden) ballWin.Show();
                ShowRemark("ballout");
            }
            else
            {
                ballWin.Hide();
                if (state == "chaseball" || state == "ballbat" || state == "ballsteal") ChooseState();
            }
        }

        bool BallTouch()
        {
            double cx = ballX + BallSize / 2, cy = ballY + BallSize / 2;
            double feet = w.Top + w.Height - SpriteFoot(poseIndex), top = feet - sprite.ActualHeight * 0.75;
            return Math.Abs(cx - (w.Left + w.Width / 2)) < Math.Max(30.0, w.Width * 0.3) && cy > top && cy < feet + BallSize / 2;
        }

        public void StartBallChase()
        {
            if (!BallAvailable) return;
            ballHits = 0; ballHitsAllowed = rng.Next(2, 5);
            BeginState("chaseball");
        }

        public void HitBall()
        {
            double now = Now;
            if (now - ballLastHit < 0.4) return;
            ballLastHit = now; ballHits++;
            double cx = ballX + BallSize / 2, side = cx >= w.Left + w.Width / 2 ? 1.0 : -1.0;
            if (ballHits >= ballHitsAllowed && rng.NextDouble() < 0.45)
            {
                ballCarried = true; ballVX = 0; ballVY = 0;
                var a = WorkArea();
                ballStashX = rng.Next(2) == 0 ? a.Left : a.Right - w.Width; ballStashY = FloorTop(a);
                BeginState("ballsteal");
                ShowRemark("ballsteal", true);
                return;
            }
            ballVX = side * (450 + rng.NextDouble() * 550); ballVY = -(600 + rng.NextDouble() * 600); ballSquash = 1.0;
            PlaySfx("ballhit.wav", 0.6);
            if (ballHits >= ballHitsAllowed + 2) { BeginState("smug"); ShowRemark("ballbored"); }
            else { BeginState("ballbat"); if (rng.NextDouble() < 0.3) ShowRemark("ballbat"); }
        }

        void UpdateBall(double now, double step)
        {
            if (!ballShown) return;
            if (isHidden || exiting) { if (ballWin.IsVisible) ballWin.Hide(); return; }
            if (!ballWin.IsVisible) ballWin.Show();
            if (ballDragging) return;
            if (ballCarried && state != "ballsteal" && !dragging) { ballCarried = false; ballVX = 0; ballVY = 0; }
            // Her own drawing shows the ball in her arms: the real one sits on top of it, almost
            // invisible but still there to grab.
            Point hold = new Point();
            bool drawn = newSprites && animPoses.ContainsKey(state) && BallHold.TryGetValue(state, out hold) && (state == "ballguard" || ballCarried) && !isHidden;
            ballGrid.Opacity = drawn ? 0.02 : 1;
            if (drawn)
            {
                try
                {
                    var p = sprite.TransformToVisual(w).Transform(new Point(hold.X * sprite.ActualWidth, hold.Y * sprite.ActualHeight));
                    ballX = w.Left + p.X - BallSize / 2; ballY = w.Top + p.Y - BallSize / 2; ballVX = 0; ballVY = 0;
                    ballWin.Left = ballX; ballWin.Top = ballY; ballSpin.Angle = 0;
                    return;
                }
                catch { }
            }
            if (ballCarried)
            {
                double feet = w.Top + w.Height - SpriteFoot(poseIndex);
                ballX = w.Left + w.Width / 2 - direction * w.Width * 0.2 - BallSize / 2;
                ballY = feet - sprite.ActualHeight * 0.42;
                ballWin.Left = ballX; ballWin.Top = ballY; ballSpin.Angle = 0;
                return;
            }
            var a = BallArea(); double bounce = tuning.ballBounce;
            ballVY += tuning.gravity * 0.8 * step;
            double nx = ballX + ballVX * step, ny = ballY + ballVY * step;
            if (nx < a.Left) { nx = a.Left; ballVX = Math.Abs(ballVX) * bounce; ballSquash = 0.6; }
            else if (nx > a.Right - BallSize) { nx = a.Right - BallSize; ballVX = -Math.Abs(ballVX) * bounce; ballSquash = 0.6; }
            if (ny < a.Top) { ny = a.Top; ballVY = Math.Abs(ballVY) * bounce; }
            double floor = a.Bottom - BallSize;
            if (ny >= floor)
            {
                ny = floor;
                if (ballVY > 140) { ballSquash = Math.Min(1.0, ballVY / 900); ballVY = -ballVY * bounce; } else ballVY = 0;
                ballVX *= Math.Max(0.0, 1 - 1.4 * step);
                if (Math.Abs(ballVX) < 4) ballVX = 0;
            }
            ballX = nx; ballY = ny;
            ballSpin.Angle = (ballSpin.Angle + ballVX * step / (BallSize / 2) * 180 / Math.PI) % 360;
            ballSquash = Math.Max(0.0, ballSquash - step * 6);
            ballSquashT.ScaleY = 1 - 0.22 * ballSquash; ballSquashT.ScaleX = 1 + 0.15 * ballSquash;
            if (Math.Abs(ballWin.Left - ballX) > 0.2 || Math.Abs(ballWin.Top - ballY) > 0.2) { ballWin.Left = ballX; ballWin.Top = ballY; }
        }
    }
}

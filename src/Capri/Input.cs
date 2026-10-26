using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Capri
{
    // Mouse: a click is less than 5 px of travel; anything more is a drag (and a fast release a throw).
    partial class Pet
    {
        public bool mouseDown; Point? downPoint;
        bool shakeAngry; double shakeNext; int shakeDirection, shakeTurns; double shakeTravel, shakeWindow, shakeLastX;

        void InitInput()
        {
            w.LocationChanged += (s, e) =>
            {
                if (!dragging) return;
                double now = Now;
                dragSamples.Add(new[] { now, w.Left, w.Top });
                while (dragSamples.Count > 12) dragSamples.RemoveAt(0);
                if (now - shakeWindow > 1.4) { shakeWindow = now; shakeTurns = 0; }
                double delta = w.Left - shakeLastX; shakeLastX = w.Left;
                if (Math.Abs(delta) < 2) return;
                int dir = Math.Sign(delta);
                if (dir != shakeDirection) { if (shakeTravel >= 35) shakeTurns++; shakeTravel = 0; shakeDirection = dir; }
                shakeTravel += Math.Abs(delta);
                if (shakeTurns >= 4 && now >= shakeNext)
                {
                    shakeAngry = true; shakeNext = now + 8; AddMemory("shakes");
                    nextHoldRemark = now + 8; messageUntil = 0;
                    SetPose(AnimPose("shaken", 20)); ShowRemark("shaken");
                }
            };
            w.PreviewMouseLeftButtonDown += (s, e) =>
            {
                RegisterInteraction();
                if (RegisterGameCatch()) { mouseDown = false; e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { InvokeSwat(); mouseDown = false; e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { InvokeHeadPat(); mouseDown = false; e.Handled = true; return; }
                mouseDown = true; downPoint = e.GetPosition(w);
                heldLongEnough = false; nextHoldRemark = Now + 0.35;
                if (e.ClickCount == 2) { BeginState("dance"); PlayVoice("click"); mouseDown = false; }
            };
            w.MouseMove += (s, e) =>
            {
                if (!mouseDown || e.LeftButton != MouseButtonState.Pressed || downPoint == null) return;
                var point = e.GetPosition(w);
                if (Math.Abs(point.X - downPoint.Value.X) + Math.Abs(point.Y - downPoint.Value.Y) < 5) return;
                mouseDown = false; dragging = true; dragSamples.Clear();
                shakeAngry = false; shakeLastX = w.Left; shakeDirection = 0; shakeTravel = 0; shakeTurns = 0; shakeWindow = Now;
                SetPose(AnimPose("thrown", 2)); PlayVoice("drag");
                heldLongEnough = true;
                ShowRemark("hold"); nextHoldRemark = Now + 5;
                try { w.DragMove(); } catch { }
                RegisterInteraction(); dragging = false;
                var velocity = throwPhysics ? SampleVelocity(dragSamples, Now) : null;
                double speed = velocity != null ? Math.Sqrt(velocity[0] * velocity[0] + velocity[1] * velocity[1]) : 0;
                if (speed > tuning.throwSpeed) StartThrow(Math.Max(-4000.0, Math.Min(4000.0, velocity[0])), Math.Max(-4000.0, Math.Min(4000.0, velocity[1])), "thrown");
                else
                {
                    ClampPet();
                    if (shakeAngry) { shakeAngry = false; BeginState("shaken"); caughtCooldown = until + 1; }
                    else if (TryDropPerch()) { }
                    else { BeginState("smug"); ShowRemark("release"); }
                }
            };
            w.MouseLeftButtonUp += (s, e) =>
            {
                RegisterInteraction();
                if (mouseDown)
                {
                    mouseDown = false;
                    if (state != "wakeglare") BeginState("wave");
                    if (heldLongEnough) ShowRemark("release"); else PlayVoice("click");
                }
            };
            w.MouseLeave += (s, e) => { if (Mouse.LeftButton != MouseButtonState.Pressed) mouseDown = false; };
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.SourceInitialized += (s, e) => { petHandle = new WindowInteropHelper(w).Handle; RegisterSummonHotkey(); };
            var a = WorkArea();
            SetPetSize(petSize);
            w.Left = a.Right - w.Width - 40; w.Top = a.Bottom - w.Height - 40;
        }
    }
}

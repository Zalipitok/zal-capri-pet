using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using static System.Math;

namespace Capri
{
    // Per-frame target updaters ("path" in behaviours.json) and animations ("motion"; defaults to the
    // state id). Motions run after the base pose and the default hover bob are applied.
    partial class Pet
    {
        public Dictionary<string, Action> paths = new Dictionary<string, Action>();
        public Dictionary<string, Action> motions = new Dictionary<string, Action>();

        void InitMotions()
        {
            paths["watch"] = () =>
            {
                double dx = cursorX - (w.Left + w.Width / 2);
                if (Abs(dx) > 12) direction = dx < 0 ? 1.0 : -1.0;
            };
            paths["creep"] = () =>
            {
                double dx = cursorX - (w.Left + w.Width / 2);
                if (Abs(dx) > 12) direction = dx < 0 ? 1.0 : -1.0;
                int side = dx < 0 ? 1 : -1;
                if (rivalRetries > 0 || (escapeStreak % 2) == 1) side = -side;
                targetX = cursorX - w.Width / 2 + side * 85;
                targetY = cursorY - (w.Height + 100) / 2 + 30;
            };
            paths["chase"] = () => { targetX = w.Left + mouse.X - w.Width / 2; targetY = w.Top + mouse.Y + 25; };
            paths["orbit"] = () => { targetX = anchorX + Cos(phase * 1.4) * 85; targetY = anchorY + Sin(phase * 1.4) * 60; };
            paths["patrol"] = () => { targetX = area.Left + (area.Width - w.Width) * (0.5 + 0.45 * Sin(phase * 0.8)); targetY = FloorTop(area, PoseFor("patrol")); };
            paths["zigzag"] = () => { targetX = anchorX + Sin(phase * 2) * 140; targetY = anchorY + Sin(phase * 3) * 65; };
            paths["figure8"] = () => { targetX = anchorX + Sin(phase) * 110; targetY = anchorY + Sin(phase * 2) * 50; };
            paths["bounce"] = () => { targetX = anchorX + Sin(phase) * 120; targetY = anchorY - Abs(Sin(phase * 2)) * 70; };
            paths["rocket"] = () => { targetX = anchorX; targetY = anchorY - 180; };
            paths["landing"] = () => { targetX = anchorX; targetY = FloorTop(area, PoseFor("landing")); };
            // Sit on a window's title bar and ride along when it moves; fall off when it goes away.
            paths["windowperch"] = () =>
            {
                var r = WindowRectDip(perchHandle);
                if (r == null || CapriWindows.Maximized(perchHandle) || r.Value.Top - PerchRoom() < area.Top - 2) { StartThrow(0, 0, "fell"); return; }
                targetX = r.Value.Left + perchOffsetX; targetY = r.Value.Top - w.Height + SpriteFoot(PerchPose) + tuning.perchSink;
                if (!perchArrived && Abs(targetX - w.Left) + Abs(targetY - w.Top) <= 4) perchArrived = true;
                if (perchArrived) { w.Left = targetX; w.Top = targetY; }
            };
            // Gravity, wall bounces and a floor at the bottom of the current monitor's work area.
            paths["thrown"] = () =>
            {
                throwVY += tuning.gravity * dt;
                throwVX *= Max(0.0, 1 - 0.5 * dt);
                double nx = w.Left + throwVX * dt, ny = w.Top + throwVY * dt;
                double maxX = area.Right - w.Width, floor = Min(FloorTop(area, PoseFor("landing")), FloorTop(area));
                if (nx < area.Left) { nx = area.Left; throwVX = Abs(throwVX) * tuning.wallBounce; throwSquash = 1; }
                else if (nx > maxX) { nx = maxX; throwVX = -Abs(throwVX) * tuning.wallBounce; throwSquash = 1; }
                if (ny < area.Top) { ny = area.Top; throwVY = Abs(throwVY) * 0.4; }
                bool landed = false;
                if (ny >= floor)
                {
                    ny = floor;
                    if (throwVY > 420) { throwVY = -throwVY * tuning.floorBounce; throwVX *= 0.7; throwSquash = 1; } else landed = true;
                }
                w.Left = nx; w.Top = ny;
                if (Abs(throwVX) > 4) direction = throwVX < 0 ? 1.0 : -1.0;
                throwSquash = Max(0.0, throwSquash - dt * 4);
                if (landed || phase > 6) FinishThrow();
            };

            motions["headpat"] = () =>
            {
                double pat = Sin(Min(1.0, phase / 3.2) * PI);
                rotation.Angle = direction * 5 * pat; shift.Y = 3 * Abs(Sin(phase * 7)) * pat; scale.ScaleY = 1 - 0.025 * pat;
            };
            motions["swat"] = () =>
            {
                if (phase < 0.65)
                {
                    double kick = Sin(Min(1.0, phase / 0.65) * PI);
                    shift.X = -direction * kick * 22; shift.Y = -kick * 20; rotation.Angle = direction * kick * 20; scale.ScaleY = 1 - kick * 0.08;
                }
                else if (phase < 2.1) { rotation.Angle = -direction * 7; shift.Y = 0; }
                else { rotation.Angle = -direction * 3; shift.Y = 0; }
            };
            motions["attentionbait"] = () =>
            {
                rotation.Angle = direction * (2 + Sin(phase * 4) * 2);
                var p = LocalCursor(); double gap = Sqrt(Pow(p.X - w.Width / 2, 2) + Pow(p.Y - (w.Height + 100) / 2, 2));
                if (phase > 0.8 && gap < 125) BeginState("baitjudge");
            };
            motions["baitjudge"] = () => { shift.Y = 0; rotation.Angle = -direction * 4; };
            motions["scanwindow"] = () =>
            {
                rotation.Angle = -direction * 5; shift.Y = Sin(phase * 4) * 2;
                sabotagePanel.Visibility = Visibility.Visible; sabotageText.Text = "WINDOW SCAN // SIMULATION  " + Min(100.0, (int)(phase / 6 * 100)) + "%";
            };
            motions["teleport"] = () =>
            {
                sprite.Opacity = Abs(Cos(phase * 9)); shift.X = Sin(phase * 40) * 8;
                if (phase > 0.8 && !teleported) { teleported = true; w.Left = targetX; w.Top = targetY; }
            };
            motions["upsidedown"] = () => { rotation.Angle = 180 + Sin(phase * 2) * 5; shift.Y = Sin(phase * 2) * 5; };
            motions["failedattack"] = () => { rotation.Angle = Min(1.0, phase / 1.2) * 210; shift.X = direction * Sin(Min(phase / 3, 1.0) * PI) * 35; shift.Y = Sin(phase * 3) * 12; };
            motions["windup"] = () => { scale.ScaleY = 0.86; scale.ScaleX = direction * 1.05; rotation.Angle = direction * 12; shift.X = -direction * 10; };
            motions["impact"] = () => { scale.ScaleY = 0.94; rotation.Angle = -direction * 8; shift.X = Sin(phase * 20) * 5 * Exp(-phase * 5); };
            motions["watch"] = () => { rotation.Angle = direction * 3; shift.Y = Sin(phase * 1.2) * 2; };
            motions["creep"] = () => { rotation.Angle = direction * 5; scale.ScaleY = 0.96; shift.Y = -Abs(Sin(phase * 4)) * 3; };
            motions["pounce"] = () => { rotation.Angle = -direction * 12; scale.ScaleY = 0.92; shift.Y = -Sin(Min(1.0, phase / 1.0) * PI) * 18; };
            motions["caught"] = () => { rotation.Angle = 0; shift.X = 0; shift.Y = 0; };
            motions["beckon"] = () => { rotation.Angle = -3 + Sin(phase * 3) * 2; shift.X = direction * Sin(phase * 3) * 3; };
            motions["casting"] = () =>
            {
                double c = phase % 3.0;
                shift.X = c < 1.2 ? -direction * 6 : direction * 8 * Exp(-(c - 1.2) * 4);
                rotation.Angle = direction * 3 * Sin(phase * 2);
            };
            motions["sideglance"] = () => { rotation.Angle = -direction * 3; shift.Y = Sin(phase * 1.2) * 2; };
            motions["sip"] = () => { rotation.Angle = -4 + 3 * Sin(phase * 1.2); shift.Y = Sin(phase) * 3; effect.Opacity = 0.4 + 0.5 * Abs(Sin(phase)); };
            motions["gaming"] = () => { rotation.Angle = Sin(phase * 3) * 3; shift.Y = Sin(phase * 2) * 3; effect.Text = "LEVEL " + (1 + (int)Floor(phase / 3)); scale.ScaleY = 1 + Sin(phase * 12) * 0.008; };
            motions["laughing"] = () => { rotation.Angle = Sin(phase * 7) * 6; shift.Y = -Abs(Sin(phase * 9)) * 7; scale.ScaleY = 0.97 + 0.03 * Sin(phase * 9); };
            motions["yawn"] = () => { scale.ScaleY = 0.96 + 0.06 * Sin(Min(PI, phase * 0.6)); rotation.Angle = Sin(phase) * 3; };
            motions["cheers"] = () => { shift.Y = -Abs(Sin(phase * 2)) * 10; rotation.Angle = -8 + 4 * Sin(phase * 2); };
            motions["bossfight"] = () => { rotation.Angle = Sin(phase * 8) * 6; shift.X = Sin(phase * 18) * 2; effect.Text = "BOSS HP " + Max(0.0, 100 - (int)(phase * 10)); };
            motions["sleepy"] = () => { rotation.Angle = Sin(phase * 0.7) * 8; shift.Y = Sin(phase) * 4; };
            motions["gamerwin"] = () => { shift.Y = -Abs(Sin(phase * 4)) * 12; rotation.Angle = Sin(phase * 4) * 6; };
            motions["hop"] = () => { shift.Y = -Abs(Sin(phase * 5)) * 20; scale.ScaleY = 1 - 0.06 * Cos(phase * 10); };
            motions["tiptoe"] = () => { rotation.Angle = Sin(phase * 5) * 4; shift.Y = -Abs(Sin(phase * 5)) * 5; };
            motions["patrol"] = () => { rotation.Angle = Sin(phase * 4) * 3; };
            motions["zigzag"] = () => { rotation.Angle = Cos(phase * 2) * 15; };
            motions["figure8"] = () => { rotation.Angle = Cos(phase) * 12; };
            motions["bounce"] = () => { scale.ScaleY = 0.94 + 0.06 * Cos(phase * 4); };
            motions["rocket"] = () => { scale.ScaleX = direction * 0.8; scale.ScaleY = 1.04; shift.X = Sin(phase * 30) * 2; };
            motions["landing"] = () => { scale.ScaleY = 0.88 + 0.12 * (1 - Exp(-phase * 2)); scale.ScaleX = direction * (1 + 0.06 * Exp(-phase * 2)); };
            motions["stretch"] = () => { scale.ScaleY = 0.98 + 0.06 * Sin(phase * 0.6); rotation.Angle = Sin(phase) * 5; };
            motions["bow"] = () => { rotation.Angle = 16 * Sin(Min(PI, phase)); scale.ScaleY = 1 - 0.12 * Sin(Min(PI, phase)); };
            motions["giggle"] = () => { shift.Y = -Abs(Sin(phase * 10)) * 6; rotation.Angle = Sin(phase * 8) * 3; };
            motions["grumpy"] = () => { shift.Y = -Abs(Sin(phase * 5)) * 7; rotation.Angle = Sign(Sin(phase * 5)) * 4; };
            motions["dizzy"] = () => { rotation.Angle = Sin(phase * 3) * 17; shift.X = Cos(phase * 3) * 8; };
            motions["scan"] = () => { rotation.Angle = Sin(phase) * 9; effect.Text = "[ " + new string('.', (int)(phase * 3) % 5) + " ]"; };
            motions["charge"] = () => { shift.X = Sin(phase * 32) * Min(4.0, phase); effect.Text = "CHARGE " + Min(100.0, (int)(phase * 20)) + "%"; };
            motions["spin"] = () => { rotation.Angle = phase * 150; scale.ScaleX = direction * 0.52; scale.ScaleY = 0.52; };
            motions["heartbeat"] = () => { scale.ScaleY = 1 + 0.025 * Sin(phase * 8); effect.Opacity = 0.5 + 0.5 * Abs(Sin(phase * 4)); };
            motions["rain"] = () => { effect.Text = ((int)(phase * 3) % 2) == 0 ? "1 0 1 0" : "0 1 0 1"; rotation.Angle = -7; };
            motions["celebrate"] = () => { shift.Y = -Abs(Sin(phase * 5)) * 14; rotation.Angle = Sin(phase * 5) * 9; };
            motions["sneak"] = () => { scale.ScaleY = 0.85; rotation.Angle = direction * 9; sprite.Opacity = 0.75; };
            motions["meditate"] = () => { shift.Y = Sin(phase * 0.8) * 9; scale.ScaleY = 0.98 + 0.015 * Sin(phase * 1.2); };
            motions["shiver"] = () => { shift.X = Sin(phase * 40) * 3; rotation.Angle = Sin(phase * 30) * 2; };
            motions["pendulum"] = () => { rotation.Angle = Sin(phase * 1.8) * 15; shift.X = Sin(phase * 1.8) * 8; };
            motions["jelly"] = () => { scale.ScaleY = 0.94 + 0.06 * Sin(phase * 8); scale.ScaleX = direction * (1 - 0.05 * Sin(phase * 8)); };
            motions["wave"] = () => { rotation.Angle = Sin(phase * 7) * 6; shift.Y = Sin(phase * 3) * 5; };
            motions["smug"] = () => { rotation.Angle = -6 + Sin(phase * 1.5) * 2; scale.ScaleY = 1 + Sin(phase * 2) * 0.012; };
            motions["tumble"] = () => { rotation.Angle = (phase * 200) % 360; scale.ScaleX = direction * 0.55; scale.ScaleY = 0.55; shift.Y = Sin(phase * 4) * 10; };
            motions["peek"] = () => { rotation.Angle = direction * 12; shift.X = Sin(phase * 2.5) * 7; };
            motions["dash"] = () => { rotation.Angle = -direction * 15; scale.ScaleX = direction * 1.06; scale.ScaleY = 0.92; };
            motions["orbit"] = () => { rotation.Angle = Sin(phase * 2) * 15; };
            // A bouncy jam: a hop and a lean on every pose change.
            motions["dance"] = () =>
            {
                double beat = (phase % 0.45) / 0.45, side = ((int)(phase / 0.45)) % 2 == 0 ? 1.0 : -1.0;
                shift.Y = -Abs(Sin(beat * PI)) * 10; rotation.Angle = side * 6 * Sin(beat * PI); scale.ScaleY = 1 - 0.04 * Cos(beat * PI * 2);
            };
            motions["glitch"] = () => { if (rng.NextDouble() < 0.28) { shift.X = rng.Next(-9, 10); sprite.Opacity = 0.55; scale.ScaleX = direction * 1.08; } };
            motions["nap"] = () => { rotation.Angle = Sin(t * 0.8) * 2; scale.ScaleY = 0.94 + Sin(t * 1.6) * 0.02; shift.Y = 0; sprite.Opacity = 0.8; };
            motions["glide"] = () => { rotation.Angle = -direction * 6; scale.ScaleY = 1 + Sin(t * 2) * 0.012; };
            motions["float"] = () => { rotation.Angle = Sin(t * 1.7) * 4; scale.ScaleY = 1 + Sin(t * 2) * 0.015; };
            motions["arrive"] = () => { sprite.Opacity = Min(1.0, phase / 1.6); shift.X = Sin(phase * 18) * 4 * (1 - Min(1.0, phase / 1.6)); };
            motions["dock"] = () => { shift.Y = Sin(phase) * 1.5; rotation.Angle = 0; };
            motions["glare"] = () => { rotation.Angle = -direction * 4; shift.Y = 0; };
            motions["sabotage"] = () =>
            {
                sabotagePanel.Visibility = Visibility.Visible;
                sabotageText.Text = state == "sabotage" ? "SIGNAL LOST  //  SIMULATION" : "SIGNAL RESTORED  //  SIMULATION";
            };
            motions["filescan"] = () =>
            {
                rotation.Angle = -direction * 5; shift.Y = Sin(phase * 4) * 2;
                double progress = Min(1.0, phase / 3.6); int bars = (int)(progress * 10);
                sabotagePanel.Visibility = Visibility.Visible;
                sabotageText.Text = "FILE SCAN // SIMULATION\n" + scanName + "\n[" + new string('#', bars) + new string('.', 10 - bars) + "] " + (int)(progress * 100) + "%";
            };
            motions["scanverdict"] = () =>
            {
                rotation.Angle = -6 + Sin(phase * 1.5) * 2;
                sabotagePanel.Visibility = Visibility.Visible;
                sabotageText.Text = "THREAT LEVEL " + scanScore + "/10  //  SIMULATION\n" + scanName;
            };
            // Sitting: no hover bob, just breathing.
            motions["perch"] = () => { shift.Y = 0; scale.ScaleY = 1 + Sin(phase * 1.6) * 0.008; };
            motions["taskbar"] = () => { shift.Y = 0; scale.ScaleY = 1 + Sin(phase * 1.6) * 0.008; };
            motions["windowperch"] = () => { if (perchArrived) { shift.Y = Sin(phase * 1.5) * 1.2; rotation.Angle = 0; } };
            motions["thrown"] = () =>
            {
                if (shakeAngry) SetPose(AnimPose("shaken", 20));
                rotation.Angle = Max(-28.0, Min(28.0, -throwVX / 45)); shift.Y = 0;
                scale.ScaleY = 1 - 0.12 * throwSquash; scale.ScaleX = direction * (1 + 0.08 * throwSquash);
            };
            motions["weather"] = () => { rotation.Angle = -direction * 4; shift.Y = Sin(phase * 1.5) * 2; };
            motions["wilt"] = () =>
            {
                double sag = Min(1.0, phase / 2.0);
                rotation.Angle = direction * (4 + 3 * sag) + Sin(phase * 0.9) * 2; scale.ScaleY = 1 - 0.05 * sag; shift.Y = 6 * sag;
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;

namespace Capri
{
    // Capri herself. The partial class is split by topic, like the old lib\*.ps1 files:
    // Pet.cs (window, sprites), Data.cs, Screens.cs, Speech.cs, Memory.cs, States.cs, Motions.cs,
    // Awareness.cs, Play.cs, Toys.cs, Reminders.cs, Weather.cs, Friend.cs, Powers.cs, Focus.cs,
    // Notes.cs, Update.cs, Chat.cs, SettingsWindow.cs, Menu.cs, Input.cs, Loop.cs, SelfTest.cs.
    partial class Pet
    {
        public static Pet Current;
        public readonly Random rng = new Random();
        public readonly Stopwatch clock = Stopwatch.StartNew();
        public double Now => clock.Elapsed.TotalSeconds;

        // Window and visual parts
        public Window w;
        Image sprite, ghost;
        Grid scene;
        Ellipse shadow;
        Border bubble, sabotagePanel, focusBadge;
        TextBlock words, effect, sabotageText, focusText;
        Canvas wall;
        Rectangle[] wallBars = new Rectangle[10];
        Window cursorFx;
        Canvas cursorCanvas;
        Polyline[] cursorArcs = new Polyline[6], petArcs = new Polyline[6];
        IntPtr cursorFxHandle = IntPtr.Zero;
        public IntPtr petHandle = IntPtr.Zero;
        ScaleTransform scale; RotateTransform rotation; TranslateTransform shift;
        System.Windows.Media.MediaPlayer sfxPlayer = new System.Windows.Media.MediaPlayer();

        // Sprites
        public List<BitmapSource> poses = new List<BitmapSource>();
        List<string> posePaths = new List<string>();
        public BitmapSource Frame(int index)
        {
            if (poses[index] == null) poses[index] = ReadPng(posePaths[index]);
            return poses[index];
        }
        public List<double> spriteFeet;
        public int musicPoseStart = -1;
        public Dictionary<int, int[]> mouthPoses = new Dictionary<int, int[]>();
        public Dictionary<int, int[]> eyePoses = new Dictionary<int, int[]>();
        public Dictionary<int, int> hpPoses = new Dictionary<int, int>();
        public Dictionary<int, int[]> hpBobPoses = new Dictionary<int, int[]>();
        // Per-animation art (sprites\anim-<state>.png): one drawing for each animation, plus its
        // blink, talking-mouth and headphone variants where they exist.
        public Dictionary<string, int> animPoses = new Dictionary<string, int>();
        public Dictionary<int, int> blinkPoses = new Dictionary<int, int>();
        public Dictionary<int, double> poseFacing = new Dictionary<int, double>(); ScaleTransform facingFlip = new ScaleTransform();
        public bool newSprites = true; int curAnimPose = -1;
        public int AnimPose(string id, int fallback) { int p; return newSprites && animPoses.TryGetValue(id, out p) ? p : fallback; }
        public double visorFraction = 0.41, spriteScale = 1.0;
        int decodeHeight;
        public int poseIndex;
        double lastPoseChange, fadeStart = -1;

        public Pet() { Current = this; }

        // The speech bubble sits in a row above the sprite frame, but her drawing starts well below
        // the top of the frame, so it is slid down to just above her head (and kept on screen).
        TranslateTransform bubbleShift = new TranslateTransform(); bool bubbleSettled;
        void PlaceBubble(double dt)
        {
            if (bubble.Visibility != Visibility.Visible || sprite.ActualHeight <= 0) { bubbleSettled = false; return; }
            try
            {
                double bottom = bubble.TranslatePoint(new Point(0, bubble.ActualHeight), scene).Y - bubbleShift.Y;
                double head = sprite.TranslatePoint(new Point(sprite.ActualWidth / 2, ArtTop(poseIndex) * sprite.ActualHeight), scene).Y;
                double target = Math.Max(0.0, head + 4 - bottom);
                double sceneTop = scene.TransformToAncestor(w).Transform(new Point(0, 0)).Y;
                double screenTop = w.Top + sceneTop + bottom - bubble.ActualHeight;
                target = Math.Max(target, WorkArea().Top + 2 - screenTop);
                if (!bubbleSettled) { bubbleShift.Y = target; bubbleSettled = true; }
                else bubbleShift.Y += (target - bubbleShift.Y) * Math.Min(1.0, dt * 6);
            }
            catch { }
        }

        static T LoadXaml<T>(string name) where T : class
        {
            using (var stream = typeof(Pet).Assembly.GetManifestResourceStream(name))
            using (var reader = XmlReader.Create(stream))
                return (T)XamlReader.Load(reader);
        }

        public void Start()
        {
            Directory.SetCurrentDirectory(App.Root);
            LoadEarlyTuning();
            BuildWindow();
            LoadSprites();
            InitJuice();
            LoadData();
            InitScreens();
            InitSpeech();
            InitMemory();
            InitStates();
            InitAwareness();
            InitPlay();
            InitToys();
            InitReminders();
            InitWeather();
            InitFriend();
            InitPowers();
            InitHerCursor();
            InitFocus();
            InitNotes();
            InitUpdate();
            InitChat();
            InitSettingsWindow();
            BuildMenu();
            BuildTray();
            InitInput();
            InitLoop();
            RestoreStayPut();
            activeSince = Now;
            StartMemorySession();
            BeginState("arrive");
            if (App.SelfTest) w.ContentRendered += (s, e) => w.Dispatcher.BeginInvoke(new Action(RunSelfTest), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            w.Show();
            if (!App.SelfTest) { timer.Start(); ShowStatus("right-click me for controls"); }
        }

        // ---------------------------------------------------------------------------------
        // Window and effect layers
        void BuildWindow()
        {
            w = LoadXaml<Window>("Window.xml");
            if (App.Friend)
            {
                foreach (var tb in FindAll<TextBlock>(w)) if (tb.Text == "CAPRI:") tb.Text = "ECHO:";
            }
            effect = (TextBlock)w.FindName("Effect"); effect.Visibility = Visibility.Collapsed;
            sprite = (Image)w.FindName("Sprite"); bubble = (Border)w.FindName("Bubble"); bubble.RenderTransform = bubbleShift;
            // The bubble's cut-corner frame is sized in code: a stretched path would make it as big as its drawing.
            var bubbleFrame = (System.Windows.Shapes.Path)w.FindName("BubbleFrame");
            var bubbleGrid = (Grid)bubble.Child;
            bubbleGrid.SizeChanged += (s, e) => bubbleFrame.Data = CutCorners(bubbleGrid.ActualWidth, Math.Max(0, bubbleGrid.ActualHeight - 12), 10); words = (TextBlock)w.FindName("Words");
            scene = (Grid)w.FindName("Scene"); shadow = (Ellipse)w.FindName("Shadow");
            focusBadge = (Border)w.FindName("FocusBadge"); focusText = (TextBlock)w.FindName("FocusText");
            sabotagePanel = (Border)w.FindName("SabotagePanel"); sabotageText = (TextBlock)w.FindName("SabotageText");
            wall = (Canvas)w.FindName("Blackwall");
            var conv = new BrushConverter();
            for (int i = 0; i < 10; i++)
            {
                var bar = new Rectangle { Fill = (Brush)conv.ConvertFromString(i % 3 == 0 ? "#DD090509" : "#DDB50025"), Height = i % 3 == 0 ? 7 : 2 };
                wall.Children.Add(bar); wallBars[i] = bar;
            }
            wall.Visibility = Visibility.Collapsed;
            // A small non-activating, click-through effect window follows the real pointer.
            cursorFx = new Window
            {
                Width = 160, Height = 160, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, Topmost = true
            };
            cursorCanvas = new Canvas { IsHitTestVisible = false, Width = 160, Height = 160 };
            var ring = new Ellipse { Width = 66, Height = 66, Stroke = Brushes.Crimson, StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 3, 2 } };
            Canvas.SetLeft(ring, 47); Canvas.SetTop(ring, 47); cursorCanvas.Children.Add(ring);
            cursorFx.Content = cursorCanvas;
            cursorFx.SourceInitialized += (s, e) => { cursorFxHandle = new WindowInteropHelper(cursorFx).Handle; CapriCursor.MakeOverlay(cursorFxHandle); };
            for (int i = 0; i < 6; i++)
            {
                foreach (var canvas in new[] { cursorCanvas, wall })
                {
                    var arc = new Polyline { Stroke = Brushes.Crimson, StrokeThickness = 2.5, IsHitTestVisible = false };
                    arc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Red, BlurRadius = 6, ShadowDepth = 0 };
                    canvas.Children.Add(arc);
                    if (canvas == cursorCanvas) cursorArcs[i] = arc; else petArcs[i] = arc;
                }
            }
            sfxPlayer.MediaEnded += (s, e) => sfxPlayer.Close();
            sfxPlayer.MediaFailed += (s, e) => sfxPlayer.Close();
            var group = (TransformGroup)sprite.RenderTransform;
            scale = (ScaleTransform)group.Children[0]; rotation = (RotateTransform)group.Children[1]; shift = (TranslateTransform)group.Children[2];
            // Some drawings face right while the rest of the app assumes left: those are mirrored first.
            group.Children.Insert(0, facingFlip);
            ghost = (Image)w.FindName("Ghost"); ghost.RenderTransform = sprite.RenderTransform;
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.HighQuality);
        }

        public static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var d = child as DependencyObject; if (d == null) continue;
                if (d is T) yield return (T)d;
                foreach (var x in FindAll<T>(d)) yield return x;
            }
        }

        // ---------------------------------------------------------------------------------
        // Sprites: sprites\frame-00..32.png plus derived frames (headphones, mouths, eyes), all
        // made by the tools\*.py scripts. Frames are decoded only as large as she can be shown.
        BitmapSource ReadPng(string path)
        {
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeHeight > 0) image.DecodePixelHeight = decodeHeight;
            image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
            if (!App.Friend) return image;
            // Her friend has different colours (tuning.json friendHue).
            try
            {
                var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                int stride = bgra.PixelWidth * 4; var pixels = new byte[stride * bgra.PixelHeight]; bgra.CopyPixels(pixels, stride, 0);
                CapriTint.HueShift(pixels, friendHue);
                var tinted = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
                tinted.Freeze(); return tinted;
            }
            catch { return image; }
        }

        int[] AddFrameSet(string[] names, double feet)
        {
            string dir = App.PathOf("sprites");
            var paths = names.Select(n => System.IO.Path.Combine(dir, n)).ToArray();
            if (paths.Any(p => !File.Exists(p))) return null;
            int first = poses.Count;
            // Decoded the first time she shows them: most of these frames are rarely needed.
            foreach (var p in paths) { poses.Add(null); posePaths.Add(p); spriteFeet.Add(feet); }
            return Enumerable.Range(first, paths.Length).ToArray();
        }

        void LoadSprites()
        {
            string dir = App.PathOf("sprites");
            var meta = Json.LoadDict(System.IO.Path.Combine(dir, "frames.json"));
            double metaHeight = Json.Num(Json.Get(meta, "height"), 897);
            // Decode size: tall enough for her saved size on this screen's scaling, with some headroom.
            double savedSize = 100;
            try { var s = Json.LoadDict(App.PathOf("voice-settings.json")); if (s != null && Json.Get(s, "petSize") != null) savedSize = Json.Num(Json.Get(s, "petSize")); } catch { }
            double dpi = 1.0;
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiY / 96.0; } catch { }
            decodeHeight = (int)Math.Min(metaHeight, Math.Max(320.0, Math.Ceiling(300.0 * (metaHeight / 768.0) * savedSize / 100.0 * dpi * 1.25)));
            for (int n = 0; n <= 32; n++) { var path = System.IO.Path.Combine(dir, "frame-" + n.ToString("D2") + ".png"); poses.Add(null); posePaths.Add(path); }
            spriteFeet = Json.List(Json.Get(meta, "feet")).Select(f => Json.Num(f)).ToList();
            var visor = Json.List(Json.Get(meta, "visor"));
            if (visor.Count > 1) visorFraction = Json.Num(visor[1]);
            var music = Json.Get(meta, "music");
            if (music != null)
            {
                var set = AddFrameSet(Enumerable.Range(0, 4).Select(i => "music-" + i + ".png").ToArray(), Json.Num(Json.Get(music, "feet")));
                if (set != null) musicPoseStart = set[0];
            }
            foreach (var b in Json.List(Json.Get(meta, "headphones")))
            {
                int pose = (int)Json.Num(b);
                var set = AddFrameSet(new[] { "hp-" + pose + ".png" }, spriteFeet[pose]);
                if (set != null) hpPoses[pose] = set[0];
            }
            foreach (var b in Json.List(Json.Get(meta, "headphoneBob")))
            {
                int pose = (int)Json.Num(b);
                if (!hpPoses.ContainsKey(pose)) continue;
                var set = AddFrameSet(new[] { "hpbob-" + pose + "-L.png", "hpbob-" + pose + "-R.png" }, spriteFeet[pose]);
                if (set != null) hpBobPoses[pose] = set;
            }
            var mouths = Json.Dict(Json.Get(meta, "mouths"));
            if (mouths != null) foreach (var key in mouths.Keys)
                {
                    int pose = int.Parse(key);
                    var set = AddFrameSet(new[] { "mouth-" + pose + "-1.png", "mouth-" + pose + "-2.png" }, spriteFeet[pose]);
                    if (set != null) mouthPoses[pose] = set;
                }
            var eyes = Json.Dict(Json.Get(meta, "eyes"));
            if (eyes != null) foreach (var key in eyes.Keys)
                {
                    int pose = int.Parse(key);
                    var set = AddFrameSet(Enumerable.Range(0, 8).Select(i => "eye-" + pose + "-" + i + ".png").ToArray(), spriteFeet[pose]);
                    if (set != null) eyePoses[pose] = set;
                }
            var anims = Json.Dict(Json.Get(meta, "anims"));
            if (anims != null) foreach (var kv in anims)
                {
                    var info = kv.Value; double feet = Json.Num(Json.Get(info, "feet"), 0.9);
                    if (Json.Get(info, "use") != null) continue; // borrows another animation's drawing (below)
                    var set = AddFrameSet(new[] { "anim-" + kv.Key + ".png" }, feet);
                    if (set == null) continue;
                    int basePose = set[0]; animPoses[kv.Key] = basePose;
                    // The new drawings face right (the original poses faced left); "facing": "left" opts one out.
                    bool right = Json.Str(Json.Get(info, "facing")) != "left";
                    if (right) poseFacing[basePose] = -1;
                    if (Json.Bool(Json.Get(info, "blink")) == true) { var b = AddFrameSet(new[] { "anim-" + kv.Key + "-blink.png" }, feet); if (b != null) { blinkPoses[basePose] = b[0]; if (right) poseFacing[b[0]] = -1; } }
                    if (Json.Bool(Json.Get(info, "mouth")) == true) { var m = AddFrameSet(new[] { "anim-" + kv.Key + "-mouth-1.png", "anim-" + kv.Key + "-mouth-2.png" }, feet); if (m != null) { mouthPoses[basePose] = m; if (right) foreach (var i in m) poseFacing[i] = -1; } }
                    if (Json.Bool(Json.Get(info, "headphones")) == true) { var h = AddFrameSet(new[] { "anim-" + kv.Key + "-hp.png" }, feet); if (h != null) { hpPoses[basePose] = h[0]; if (right) poseFacing[h[0]] = -1; } }
                }
            if (anims != null) foreach (var kv in anims)
                {
                    int target; var use = Json.Str(Json.Get(kv.Value, "use"));
                    if (use != "" && animPoses.TryGetValue(use, out target)) animPoses[kv.Key] = target;
                }
            // The shared canvas has more room around her than the old 768 px cells.
            spriteScale = metaHeight / 768.0;
            sprite.Source = Frame(0); poseIndex = 0;
        }

        // Smooth pose changes: the previous frame fades out on top of the new one.
        public bool smoothPoses = true;
        public void SetPose(int index, bool instant = false)
        {
            // While music plays she keeps her headphones on (not mid-sentence); some poses nod on the beat.
            if (musicEnabled && musicPlaying && !voiceBusy && hpPoses.ContainsKey(index))
            {
                int b = index; index = hpPoses[b];
                int[] bob;
                if (hpBobPoses.TryGetValue(b, out bob))
                {
                    int step = GetMusicStep(Now);
                    if (step == 0) index = bob[0]; else if (step == 2) index = bob[1];
                }
            }
            if (index == poseIndex || index < 0 || index >= poses.Count) return;
            double now = Now, fade = tuning.crossfadeSeconds;
            if (smoothPoses && !instant && fade > 0 && index != 13 && poseIndex != 13 && now - lastPoseChange > 0.25)
            {
                ghost.Source = sprite.Source; ghost.Opacity = sprite.Opacity; ghost.Visibility = Visibility.Visible; fadeStart = now;
            }
            sprite.Source = Frame(index); poseIndex = index; lastPoseChange = now;
            double f; facingFlip.ScaleX = poseFacing.TryGetValue(index, out f) ? f : 1.0;
        }

        void UpdatePoseFade()
        {
            if (fadeStart < 0) return;
            double k = 1.0 - (Now - fadeStart) / Math.Max(0.01, tuning.crossfadeSeconds);
            if (k <= 0 || !smoothPoses) { ghost.Visibility = Visibility.Collapsed; fadeStart = -1; }
            else ghost.Opacity = k * sprite.Opacity;
        }
    }
}

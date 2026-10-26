using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Capri
{
    // The netrunner look for her windows: borderless, cut corners, a hot-pink rail down the side,
    // a title tag like "CAPRI // SETTINGS", and the control styles from Ui\Cyber.xml.
    partial class Pet
    {
        static ResourceDictionary cyberTheme;
        static ResourceDictionary CyberTheme
        {
            get
            {
                if (cyberTheme == null) { try { cyberTheme = LoadXaml<ResourceDictionary>("Cyber.xml"); } catch { cyberTheme = new ResourceDictionary(); } }
                return cyberTheme;
            }
        }

        // Geometry for a panel with two cut corners (top-left and bottom-right), sized at layout time.
        static Geometry CutCorners(double wdt, double hgt, double cut)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cut, 0), true, true);
                c.LineTo(new Point(wdt, 0), true, false); c.LineTo(new Point(wdt, hgt - cut), true, false);
                c.LineTo(new Point(wdt - cut, hgt), true, false); c.LineTo(new Point(0, hgt), true, false); c.LineTo(new Point(0, cut), true, false);
            }
            g.Freeze(); return g;
        }

        // Wraps a dialog's content in the cyber frame. Call before Show().
        public void CyberChrome(Window win, string tag, string subtitle = null)
        {
            var content = win.Content as UIElement;
            win.Content = null;
            win.WindowStyle = WindowStyle.None; win.AllowsTransparency = true; win.Background = Brushes.Transparent;
            if (win.ResizeMode != ResizeMode.NoResize) win.ResizeMode = ResizeMode.CanResizeWithGrip;
            win.Resources.MergedDictionaries.Add(CyberTheme);
            win.Foreground = (Brush)CyberTheme["Cy.Text"];

            var root = new Grid { Margin = new Thickness(10) };   // room for the glow
            var frame = new Path { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Fill = (Brush)CyberTheme["Cy.Bg"], Stroke = (Brush)CyberTheme["Cy.Cyan"], StrokeThickness = 1.2, Opacity = 0.98 };
            frame.Effect = new DropShadowEffect { Color = Color.FromRgb(63, 230, 245), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.35 };
            root.SizeChanged += (s, e) => frame.Data = CutCorners(root.ActualWidth, root.ActualHeight, 16);
            root.Children.Add(frame);
            var rail = new Rectangle { Width = 3, Fill = (Brush)CyberTheme["Cy.Pink"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 26, 0, 24) };
            root.Children.Add(rail);

            var dock = new DockPanel { Margin = new Thickness(16, 10, 14, 12) };
            var bar = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.SizeAll };
            DockPanel.SetDock(bar, Dock.Top);
            Func<string, string, Action, Button> chromeButton = (text, tip, act) =>
            {
                var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(9, 1, 9, 2), Margin = new Thickness(4, 0, 0, 0), FontSize = 13 };
                b.Click += (s, e) => act(); return b;
            };
            var close = chromeButton("×", "Close", () => win.Close());
            DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close);
            var min = chromeButton("–", "Minimise", () => win.WindowState = WindowState.Minimized);
            DockPanel.SetDock(min, Dock.Right); bar.Children.Add(min);
            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = tag.ToUpperInvariant(), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            titles.Children.Add(new TextBlock { Text = "  " + (subtitle ?? "v" + appVersion), FontSize = 11, Foreground = (Brush)CyberTheme["Cy.Dim"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 0, 0) });
            bar.Children.Add(titles);
            bar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) try { win.DragMove(); } catch { } };
            dock.Children.Add(bar);
            if (content != null) dock.Children.Add(content);
            root.Children.Add(dock);
            win.Content = root;
        }

        // A section tag like "BRAIN >" in yellow, used inside the dialogs.
        public static TextBlock CyberHeading(string text) => new TextBlock { Text = text.ToUpperInvariant() + " >", FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Frozen("#FCEE0A"), Margin = new Thickness(0, 4, 0, 6) };
    }
}

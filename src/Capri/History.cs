using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Capri
{
    // "Our history": what she remembers about the two of you, and what's left to unlock.
    partial class Pet
    {
        Window historyWindow;

        public void ShowHistoryWindow()
        {
            if (historyWindow != null) { historyWindow.Activate(); return; }
            UpdateMemoryDay();
            var mono = new FontFamily("Consolas");
            var yellow = HexBrush("#FCEE0A"); var cyan = HexBrush("#5EF6FF"); var dim = HexBrush("#8A97AD");
            var win = new Window { Title = "Capri // Our history", Width = 560, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = HexBrush("#0D121B"), Foreground = Brushes.White, ResizeMode = ResizeMode.CanResizeWithGrip };
            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
            win.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

            Func<string, TextBlock> heading = t => new TextBlock { Text = t, FontFamily = mono, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = yellow, Margin = new Thickness(0, 14, 0, 6) };
            root.Children.Add(new TextBlock { Text = "WHAT CAPRI REMEMBERS", FontFamily = mono, FontWeight = FontWeights.Bold, FontSize = 18, Foreground = yellow });
            string met = memText["firstMet"]; DateTime metDate;
            string metLine = DateTime.TryParseExact(met, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out metDate)
                ? "Together since " + metDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + " (" + Math.Max(0, (int)(DateTime.Today - metDate).TotalDays) + " days ago)"
                : "We only just met.";
            root.Children.Add(new TextBlock { Text = metLine, Foreground = dim, Margin = new Thickness(0, 2, 0, 0) });

            // Big numbers across the top.
            var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var t in new[] { new object[] { "DAYS", Mem("days") }, new object[] { "SESSIONS", Mem("sessions") }, new object[] { "HEAD PATS", Mem("pets") }, new object[] { "FOCUS MIN", Mem("focusMinutes") } })
            {
                var tile = new Border { BorderBrush = HexBrush("#2A3550"), BorderThickness = new Thickness(1), Background = HexBrush("#121a28"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 8, 10, 8) };
                var s = new StackPanel(); tile.Child = s;
                s.Children.Add(new TextBlock { Text = Convert.ToString(t[1]), FontFamily = mono, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = cyan });
                s.Children.Add(new TextBlock { Text = (string)t[0], FontFamily = mono, FontSize = 10, Foreground = dim });
                tiles.Children.Add(tile);
            }
            root.Children.Add(tiles);

            Action<string, List<string[]>> table = (title, rows) =>
            {
                root.Children.Add(heading(title));
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                for (int i = 0; i < rows.Count; i++)
                {
                    grid.RowDefinitions.Add(new RowDefinition());
                    var name = new TextBlock { Text = rows[i][0], Margin = new Thickness(0, 2, 12, 2) };
                    var value = new TextBlock { Text = rows[i][1], FontFamily = mono, Foreground = cyan, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
                    Grid.SetRow(name, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
                    grid.Children.Add(name); grid.Children.Add(value);
                }
                root.Children.Add(grid);
            };
            Func<string, string> withToday = key => { int today = Mem("today" + char.ToUpper(key[0]) + key.Substring(1)); return Mem(key) + (today > 0 ? "  (" + today + " today)" : ""); };
            table("TOGETHER", new List<string[]>
            {
                new[] { "Head pats", withToday("pets") }, new[] { "Throws", withToday("throws") }, new[] { "Shakes", Mem("shakes").ToString() },
                new[] { "Swats", withToday("swats") }, new[] { "Welcome-backs", Mem("returns").ToString() }, new[] { "Files scanned", Mem("scans").ToString() },
            });
            table("RIVALS", new List<string[]>
            {
                new[] { "Cursor catches", rivalWins.ToString() }, new[] { "Cursor escapes", rivalEscapes.ToString() },
                new[] { "Catch-me games", Mem("games").ToString() }, new[] { "Best catch-me score", Mem("bestCatch").ToString() },
            });
            table("FOCUS", new List<string[]>
            {
                new[] { "Sessions finished", Mem("focusSessions").ToString() }, new[] { "Minutes focused", Mem("focusMinutes").ToString() },
                new[] { "Streak", Mem("focusStreak") + (Mem("focusStreak") == 1 ? " day" : " days") + (memText["lastFocusDay"] != "" ? "  (last: " + memText["lastFocusDay"] + ")" : "") },
            });

            var have = UnlockedIds();
            root.Children.Add(heading("UNLOCKS  " + have.Count + " / " + unlocks.Count));
            foreach (var u in unlocks)
            {
                bool got = have.Contains(Json.Str(Json.Get(u, "id")));
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
                var mark = new TextBlock { Text = got ? "[x] " : "[ ] ", FontFamily = mono, Foreground = got ? yellow : dim, VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
                if (got) row.Children.Add(new TextBlock { Text = Json.Str(Json.Get(u, "title")), TextWrapping = TextWrapping.Wrap });
                else
                {
                    var need = Json.Dict(Json.Get(u, "need")) ?? new Dictionary<string, object>();
                    var parts = need.Select(kv => { int goal = (int)Json.Num(kv.Value); int now = Stat(kv.Key); return Math.Min(now, goal) + "/" + goal + " " + (StatNames.ContainsKey(kv.Key) ? StatNames[kv.Key] : kv.Key); });
                    var s = new StackPanel();
                    s.Children.Add(new TextBlock { Text = "???", Foreground = dim });
                    s.Children.Add(new TextBlock { Text = string.Join(", ", parts), FontSize = 11, Foreground = dim, TextWrapping = TextWrapping.Wrap });
                    // Progress toward the furthest-off requirement.
                    double progress = need.Count == 0 ? 0 : need.Min(kv => Math.Min(1.0, Stat(kv.Key) / Math.Max(1.0, Json.Num(kv.Value))));
                    s.Children.Add(new ProgressBar { Value = progress * 100, Height = 4, Margin = new Thickness(0, 3, 0, 0), Foreground = yellow, Background = HexBrush("#1d2638"), BorderThickness = new Thickness(0), MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left, Width = 260 });
                    row.Children.Add(s);
                }
                root.Children.Add(row);
            }
            win.Closed += (s, e) => { historyWindow = null; RegisterInteraction(); };
            historyWindow = win;
            CyberChrome(win, "Capri // Memory", "our history");
            win.Show();
        }
    }
}

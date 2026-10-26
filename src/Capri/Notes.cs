using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Capri
{
    class StickyNote { public string id; public Window win; public TextBox box; public bool pinned; }

    // Sticky notes she leaves on your screen; they come back after a restart (notes.json).
    partial class Pet
    {
        public List<StickyNote> notes = new List<StickyNote>();
        string notesPath; public string notesError = ""; double noteSaveDue; List<object> pendingNotes = new List<object>();

        void InitNotes()
        {
            notesPath = App.PathOf("notes.json");
            if (!App.SelfTest && !App.Friend)
            {
                try { if (File.Exists(notesPath)) pendingNotes = Json.List(Json.Load(notesPath)); }
                catch (Exception e) { notesError = e.Message; }
                w.ContentRendered += (s, e) => { try { ShowSavedNotes(); } catch { } };
            }
        }

        void SaveNotes()
        {
            if (App.SelfTest || App.Friend) return;
            try
            {
                if (notes.Count == 0) { if (File.Exists(notesPath)) File.Delete(notesPath); return; }
                Json.Save(notesPath, notes.Select(n => new Dictionary<string, object> { { "text", n.box.Text }, { "x", Math.Round(n.win.Left) }, { "y", Math.Round(n.win.Top) }, { "pinned", n.pinned } }).ToList());
            }
            catch { }
        }

        public bool NewStickyNote(string text, double? x = null, double? y = null, bool restore = false, bool pinned = false)
        {
            if (notes.Count >= 20) return false;
            var note = new StickyNote { id = Guid.NewGuid().ToString("N") };
            var win = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = false, Width = 210, SizeToContent = SizeToContent.Height, Title = "Capri note" };
            var frame = new Border { Background = HexBrush("#F2101722"), BorderBrush = HexBrush("#FCEE0A"), BorderThickness = new Thickness(1) };
            var stack = new StackPanel(); frame.Child = stack;
            var header = new DockPanel { Background = HexBrush("#FCEE0A"), Cursor = Cursors.SizeAll };
            var close = new TextBlock { Text = " x ", FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            // Pinned notes stay on top of other windows.
            var pin = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 10, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand, Padding = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pin, Dock.Right); header.Children.Add(pin);
            Action showPin = () =>
            {
                pin.Text = note.pinned ? "PINNED" : "PIN";
                pin.Background = note.pinned ? HexBrush("#101722") : Brushes.Transparent;
                pin.Foreground = note.pinned ? HexBrush("#FCEE0A") : Brushes.Black;
                pin.ToolTip = note.pinned ? "Stays on top. Click to unpin." : "Keep this note on top of other windows";
                win.Topmost = note.pinned;
            };
            note.pinned = pinned;
            pin.MouseLeftButtonDown += (s, e) => { e.Handled = true; note.pinned = !note.pinned; showPin(); SaveNotes(); };
            header.Children.Add(new TextBlock { Text = "CAPRI // NOTE", FontFamily = new FontFamily("Consolas"), FontSize = 10, FontWeight = FontWeights.Bold, Padding = new Thickness(6, 2, 0, 2) });
            stack.Children.Add(header);
            var box = new TextBox
            {
                Text = text ?? "", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 50, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = HexBrush("#E8FBFF"), CaretBrush = HexBrush("#FCEE0A"), FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(8, 6, 8, 8), MaxLength = 500
            };
            stack.Children.Add(box);
            win.Content = frame;
            note.win = win; note.box = box;
            showPin();
            header.MouseLeftButtonDown += (s, e) => { try { win.DragMove(); } catch { } SaveNotes(); };
            close.MouseLeftButtonDown += (s, e) => { e.Handled = true; RemoveStickyNote(note); };
            box.TextChanged += (s, e) => noteSaveDue = Now + 2;
            if (x == null)
            {
                var a = WorkArea();
                x = w.Left - 230 >= a.Left ? w.Left - 230 : w.Left + w.Width + 10;
                y = Math.Max(a.Top, Math.Min(a.Bottom - 160, w.Top + w.Height * 0.45));
            }
            win.Left = x.Value; win.Top = y.Value;
            notes.Add(note);
            win.Show();
            if (!restore) { if (string.IsNullOrEmpty(text)) { win.Activate(); box.Focus(); } SaveNotes(); }
            return true;
        }

        public void RemoveStickyNote(StickyNote note)
        {
            if (!notes.Remove(note)) return;
            note.win.Close(); SaveNotes();
        }

        void UpdateNotes(double now)
        {
            if (noteSaveDue > 0 && now >= noteSaveDue) { noteSaveDue = 0; SaveNotes(); }
        }

        public void LeaveNote()
        {
            PrepareManualAction(); routine.Clear();
            if (NewStickyNote("")) { BeginState("wave"); ShowRemark("notedone", true); }
            else ShowStatus("That's 20 notes already. Clear a few first.");
        }

        // A note saved on a monitor that's gone comes back onto the screen.
        void ShowSavedNotes()
        {
            double left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop;
            double right = left + SystemParameters.VirtualScreenWidth - 210, bottom = top + SystemParameters.VirtualScreenHeight - 80;
            foreach (var n in pendingNotes)
            {
                double x = Math.Max(left, Math.Min(right, Json.Num(Json.Get(n, "x")))), y = Math.Max(top, Math.Min(bottom, Json.Num(Json.Get(n, "y"))));
                NewStickyNote(Json.Str(Json.Get(n, "text")), x, y, true, Json.Bool(Json.Get(n, "pinned")) == true);
            }
            pendingNotes.Clear();
        }
    }
}

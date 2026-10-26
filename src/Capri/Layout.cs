using System;
using System.IO;
using System.Linq;

namespace Capri
{
    // Where her files live:
    //   Capri.exe, README.txt                        the app
    //   data\    behaviours, remarks, tuning, ...   her data (tuning.json is yours to edit)
    //   save\    settings, memory, reminders, notes, chat, backups   yours, kept on updates
    //   logs\    error logs and the self-test report
    //   sprites\ sounds\ voices\
    // Older versions kept everything in one folder; Tidy() moves things into place once.
    static class Layout
    {
        static readonly string[] DataFiles = { "behaviours.json", "remarks.json", "app-reactions.json", "unlocks.json", "games.json", "voice-pack.json", "menu-theme.xaml", "VERSION", "tuning.json" };
        static readonly string[] SaveFiles = { "voice-settings.json", "memory.json", "reminders.json", "notes.json", "chat-history.json", "chat-key.dat" };
        // Left behind by earlier versions and no longer used.
        static readonly string[] Obsolete = { "Capri.ps1", "Capri.vbs", "Start-Capri.bat", "Run-SelfTest.bat", "sprites-before-after.png", "pose-shaken.png",
            "poses-casting-clean.png", "poses-clean.png", "poses-daily.png", "poses-extra-clean.png", "poses-faces-clean.png", "poses-interaction.png", "poses-life-clean.png", "poses-motion.png",
            "poses.png", "poses-extra.png", "poses-faces.png", "poses-life.png", "poses-casting.png" };
        static readonly string[] OldTools = { "Build-Exe.ps1", "make_eye_frames.py", "make_mouth_frames.py", "make_music_frames.py", "normalize_sprites.py", "enhance_sprites.py" };

        static bool Is(string[] list, string name) => list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

        public static string Folder(string name)
        {
            if (Is(DataFiles, name)) return "data";
            if (Is(SaveFiles, name) || name.StartsWith("backup-before-", StringComparison.OrdinalIgnoreCase)) return "save";
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "selftest-report.txt", StringComparison.OrdinalIgnoreCase)) return "logs";
            return "";
        }

        // A bare file name goes to its folder; anything with a folder in it is left alone.
        public static string PathOf(string root, string relative)
        {
            if (relative.IndexOfAny(new[] { '\\', '/' }) >= 0) return Path.Combine(root, relative);
            string folder = Folder(relative);
            if (folder == "") return Path.Combine(root, relative);
            string placed = Path.Combine(root, folder, relative), loose = Path.Combine(root, relative);
            // Her data still loose next to the exe (a copy run from the source folder): read it there.
            if (folder == "data" && !File.Exists(placed) && File.Exists(loose)) return loose;
            return placed;
        }

        public static void Tidy(string root)
        {
            foreach (var dir in new[] { "save", "logs" }) try { Directory.CreateDirectory(Path.Combine(root, dir)); } catch { }
            foreach (var file in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(file), folder = Folder(name);
                try
                {
                    if (Is(Obsolete, name)) { File.Delete(file); continue; }
                    if (folder == "") continue;
                    string target = Path.Combine(root, folder, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    bool yours = folder != "data" || string.Equals(name, "tuning.json", StringComparison.OrdinalIgnoreCase);
                    if (File.Exists(target))
                    {
                        // Her data: the copy in data\ is the current one. Your files and tuning: yours win.
                        if (yours) { File.Copy(file, target, true); File.Delete(file); }
                        else File.Delete(file);
                    }
                    else File.Move(file, target);
                }
                catch { }
            }
            foreach (var dir in Directory.GetDirectories(root, "backup-before-*"))
                try { var target = Path.Combine(root, "save", Path.GetFileName(dir)); if (!Directory.Exists(target)) Directory.Move(dir, target); } catch { }
            var tools = Path.Combine(root, "tools");
            if (Directory.Exists(tools))
            {
                foreach (var f in OldTools) try { File.Delete(Path.Combine(tools, f)); } catch { }
                try { if (!Directory.EnumerateFileSystemEntries(tools).Any()) Directory.Delete(tools); } catch { }
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Capri
{
    static class App
    {
        public static string Root;          // the exe's folder: data files, sprites, sounds live here
        public static string ExePath;
        public static bool SelfTest;
        public static bool Friend;          // a second Capri visiting the first (Interact > Invite a friend)

        [STAThread]
        static int Main(string[] args)
        {
            ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Root = Path.GetDirectoryName(ExePath);
            Directory.SetCurrentDirectory(Root);
            SelfTest = args.Any(a => a.TrimStart('-', '/').Equals("selftest", StringComparison.OrdinalIgnoreCase));
            Friend = !SelfTest && args.Any(a => a.TrimStart('-', '/').Equals("friend", StringComparison.OrdinalIgnoreCase));

            // After an update the new Capri waits for the old one to finish closing.
            int afterIndex = Array.FindIndex(args, a => a.TrimStart('-', '/').Equals("after", StringComparison.OrdinalIgnoreCase));
            int oldPid;
            if (afterIndex >= 0 && afterIndex + 1 < args.Length && int.TryParse(args[afterIndex + 1], out oldPid))
            {
                try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(60000); } catch { }
            }

            // One Capri at a time (the self-test and a friend run next to her).
            string mutexName = SelfTest ? "Local\\CapriSelfTest" : Friend ? "Local\\CapriFriend" : "Local\\CapriDesktopCompanion";
            bool isNew;
            var mutex = new Mutex(true, mutexName, out isNew);
            if (!isNew)
            {
                if (!Friend) MessageBox.Show("Capri is already running. Look for her icon in the system tray.", "Capri");
                return 0;
            }
            if (!SelfTest && !Friend) try { Layout.Tidy(Root); } catch { }
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log("runtime-error.log", e.Exception.ToString());
                    e.Handled = true;
                    try { Pet.Current?.ShowProblem("Something went wrong: " + e.Exception.Message + " (see logs\\runtime-error.log)"); } catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("runtime-error.log", Convert.ToString(e.ExceptionObject));
                var pet = new Pet();
                pet.Start();
                app.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Log("startup-error.log", ex.ToString());
                MessageBox.Show("Capri could not start:\n\n" + ex.Message + "\n\nDetails were written to logs\\startup-error.log.", "Capri");
                return 1;
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }

        public static void Log(string file, string text)
        {
            try { var path = PathOf(file); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.AppendAllText(path, DateTime.Now.ToString("o") + " " + text + "\r\n"); } catch { }
        }

        public static string PathOf(string relative) => Layout.PathOf(Root, relative);
    }
}

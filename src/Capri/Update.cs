using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace Capri
{
    // Updates from GitHub releases (tuning.json updateRepo): a daily check, and Debug and help >
    // Install update, which keeps your personal files, backs up what it replaces and restarts her.
    partial class Pet
    {
        public Version appVersion = new Version(0, 0, 0);
        public Version updateVersion; string updateUrl = ""; public string updateError = "";
        double nextUpdateCheck = 90; bool updateBusy;
        static readonly string[] KeepOnUpdate = { "voice-settings.json", "memory.json", "reminders.json", "notes.json", "chat-key.dat", "chat-history.json", "tuning.json" };

        void InitUpdate()
        {
            try { var v = File.ReadAllText(App.PathOf("VERSION")).Trim(); appVersion = new Version(v); } catch { }
            try { var old = App.ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        }

        void UpdateUpdates(double now)
        {
            if (App.Friend || App.SelfTest || !autoUpdate || now < nextUpdateCheck) return;
            nextUpdateCheck = now + 6 * 3600;
            if (memText["lastUpdateCheck"] != DateTime.Now.ToString("yyyy-MM-dd")) CheckForUpdates(false);
        }

        public async void CheckForUpdates(bool manual)
        {
            if (updateBusy || App.Friend || App.SelfTest) return;
            updateBusy = true;
            try
            {
                memText["lastUpdateCheck"] = DateTime.Now.ToString("yyyy-MM-dd"); memoryDirty = true;
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + tuning.updateRepo + "/releases/latest");
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                var response = await Http.SendAsync(request);
                if ((int)response.StatusCode != 200)
                {
                    updateError = (int)response.StatusCode == 404 ? "no public release found for " + tuning.updateRepo : "GitHub answered " + (int)response.StatusCode;
                    if (manual) ShowProblem("Update check: " + updateError);
                    return;
                }
                var release = Json.Parse(await response.Content.ReadAsStringAsync());
                var tag = Json.Str(Json.Get(release, "tag_name")).TrimStart('v', 'V');
                var asset = Json.List(Json.Get(release, "assets")).FirstOrDefault(a => Json.Str(Json.Get(a, "name")).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                if (asset == null) { updateError = "the release has no zip"; return; }
                var latest = new Version(tag);
                updateError = "";
                if (latest > appVersion)
                {
                    updateVersion = latest; updateUrl = Json.Str(Json.Get(asset, "browser_download_url"));
                    try { tray?.ShowBalloonTip(8000, "Capri " + latest + " is out", "Right-click her > Debug and help > Install update.", System.Windows.Forms.ToolTipIcon.Info); } catch { }
                    ShowRemark("updateavailable", true, latest.ToString());
                }
                else if (manual) ShowStatus("Up to date (version " + appVersion + ").", 4);
            }
            catch (Exception e) { updateError = (e.GetBaseException() ?? e).Message; if (manual) ShowProblem("Update check failed: " + updateError); }
            finally { updateBusy = false; }
        }

        public async void InstallUpdate()
        {
            if (updateVersion == null) { CheckForUpdates(true); ShowStatus("Checking for updates..."); return; }
            var answer = MessageBox.Show("Install Capri " + updateVersion + "? (You have " + appVersion + ".)\n\nYour settings, memory, notes, reminders, voices and tuning.json are kept. Replaced files are backed up to backup-before-" + updateVersion + ". She restarts when it's done.", "Capri update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            ShowStatus("Downloading the update...", 10);
            string work = Path.Combine(Path.GetTempPath(), "capri-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                var zip = await Http.GetByteArrayAsync(updateUrl);
                Directory.CreateDirectory(work);
                var zipPath = Path.Combine(work, "update.zip"); File.WriteAllBytes(zipPath, zip);
                var unpacked = Path.Combine(work, "files"); ZipFile.ExtractToDirectory(zipPath, unpacked);
                var marker = Directory.GetFiles(unpacked, "Capri.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault()
                    ?? Directory.GetFiles(unpacked, "Capri.ps1", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
                if (marker == null) throw new Exception("the download does not look like Capri.");
                var source = Path.GetDirectoryName(marker);
                var backup = App.PathOf("backup-before-" + updateVersion);
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(source.Length).TrimStart('\\', '/');
                    if (KeepOnUpdate.Contains(relative, StringComparer.OrdinalIgnoreCase) || relative.StartsWith("voices", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = App.PathOf(relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                    {
                        var saved = Path.Combine(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        File.Copy(target, saved, true);
                        // The running exe can't be overwritten, but it can be moved out of the way.
                        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(App.ExePath), StringComparison.OrdinalIgnoreCase))
                        { var old = target + ".old"; if (File.Exists(old)) File.Delete(old); File.Move(target, old); }
                    }
                    File.Copy(file, target, true);
                }
                // Start again once this Capri has gone.
                Process.Start(new ProcessStartInfo(App.ExePath, "--after " + Process.GetCurrentProcess().Id) { UseShellExecute = false, WorkingDirectory = App.Root });
                ShowRemark("updating", true);
                exitingForUpdate = true;
                w.Close();
            }
            catch (Exception e) { ShowProblem("Update failed: " + e.Message + " Nothing that matters was changed."); }
            finally { try { Directory.Delete(work, true); } catch { } }
        }
        public bool exitingForUpdate;
    }
}

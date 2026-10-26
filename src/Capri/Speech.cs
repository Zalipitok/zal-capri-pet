using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Capri
{
    class VoiceClip
    {
        public string language, id, file, subtitle, speechMotion = "";
        public List<string> events = new List<string>();
        public int? speechPose; public double speechTempo = 1.0; public bool needsReview;
        public string Key => language + ":" + id;
    }

    // Notices, remarks, settings and voice playback.
    partial class Pet
    {
        // Notices: operational messages show in her bubble; problems also as a tray notification.
        public void ShowStatus(string text, double seconds = 3.5)
        {
            if (isHidden || voiceBusy) return;
            words.Text = text; bubble.Visibility = Visibility.Visible;
            messageUntil = Now + seconds;
        }

        public void ShowProblem(string text)
        {
            if (!voiceBusy && !isHidden) { words.Text = text; bubble.Visibility = Visibility.Visible; messageUntil = Now + 6; }
            if (tray != null && Now >= nextNoticeAt)
            {
                nextNoticeAt = Now + 30;
                try { tray.ShowBalloonTip(6000, "Capri", text, System.Windows.Forms.ToolTipIcon.Warning); } catch { }
            }
        }

        public void RegisterInteraction()
        {
            lastInteraction = Now;
            if (idleRest != "awake")
            {
                idleRest = "awake"; sleepReason = "";
                mood = "irritated"; nextMood = lastInteraction + 45;
                BeginState("wakeglare");
                caughtCooldown = lastInteraction + 5;
                nextRemark = lastInteraction + 30;
                ScheduleYap();
            }
        }

        // ---------------------------------------------------------------------------------
        // Remarks (remarks.json): shuffled without repeats per pool.
        public double subtitleUntil; public bool subtitles = true; string activeCaption = "";
        public bool remarksEnabled = true; string lastRemark = "";
        Dictionary<string, Queue<string>> remarkBags = new Dictionary<string, Queue<string>>();
        public double nextRemark = 22, nextHoldRemark; bool heldLongEnough;
        public Dictionary<string, List<string>> remarkPack = new Dictionary<string, List<string>>();

        public List<string> RemarkLines(string kind)
        {
            List<string> lines;
            return remarkPack.TryGetValue(kind, out lines) ? lines : new List<string>();
        }

        static string Format(string text, object value)
        {
            if (value == null) return text;
            try { return value is object[] ? string.Format(text, (object[])value) : string.Format(text, value); } catch { return text; }
        }

        // force is for things you opted into (reminders, games): they show in Busy mode and with remarks off.
        public bool ShowRemark(string kind, bool force = false, object value = null)
        {
            if (!force && (busyMode || !remarksEnabled)) return false;
            if (paused || isHidden || menu.IsOpen) return false;
            double now = Now;
            if (voiceBusy || now < subtitleUntil) return false;
            Queue<string> bag;
            if (!remarkBags.TryGetValue(kind, out bag)) { bag = new Queue<string>(); remarkBags[kind] = bag; }
            if (bag.Count == 0)
            {
                var lines = RemarkLines(kind);
                if (lines.Count == 0) return false;
                foreach (var l in Shuffle(lines)) bag.Enqueue(l);
                if (bag.Count > 1 && bag.Peek() == lastRemark) bag.Enqueue(bag.Dequeue());
            }
            lastRemark = bag.Dequeue();
            words.Text = Format(lastRemark, value); bubble.Visibility = Visibility.Visible;
            messageUntil = now + 4.5;
            nextRemark = now + 35 + rng.NextDouble() * 30;
            return true;
        }

        // A line as text without showing it (the chat window uses these).
        public string RemarkText(string kind, object value = null)
        {
            var lines = RemarkLines(kind);
            if (lines.Count == 0) return "";
            return Format(lines[rng.Next(lines.Count)], value);
        }

        public List<T> Shuffle<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = list[i]; list[i] = list[j]; list[j] = t; }
            return list;
        }

        // ---------------------------------------------------------------------------------
        // Voices and settings
        public string language = "en"; public bool muted; public double volume = 0.5; public bool liveVolume;
        public double voiceNext; public bool voiceBusy; string lastClip = "";
        Dictionary<string, double> voiceLastPlayed = new Dictionary<string, double>();
        public string chatter = "normal";
        public List<string> approvedVoices = new List<string>();
        public List<VoiceClip> voicePack = new List<VoiceClip>();
        public int voicesMissing;
        public double nextYap = 12;
        public string settingsPath;
        public bool sfxEnabled = true, talkingMouth = true;

        static readonly string[] SavedBools = { "facialMotion", "chase", "blackwallEnabled", "sfxEnabled", "windowPerch", "throwPhysics", "timeOfDay", "remarksEnabled", "subtitles", "muted", "smoothPoses", "musicEnabled", "appAware", "hotkeyEnabled", "talkingMouth", "eyesFollow", "batteryReactions", "shadowEnabled", "beatSync", "dndAware", "weatherEnabled", "chatEnabled", "chatVoice", "possessWindows", "cursorRepairs", "autoUpdate", "focusBreaks", "newSprites", "effectsEnabled", "herCursorEnabled", "stayPut", "avoidWork" };

        void InitSpeech()
        {
            var pack = Json.LoadDict(App.PathOf("remarks.json"));
            foreach (var kv in pack) if (!kv.Key.StartsWith("_")) remarkPack[kv.Key] = Json.List(kv.Value).Select(x => Json.Str(x)).Where(x => x != "").ToList();
            foreach (var entry in Json.List(Json.Load(App.PathOf("voice-pack.json"))))
            {
                var clip = new VoiceClip
                {
                    language = Json.Str(Json.Get(entry, "language")), id = Json.Str(Json.Get(entry, "id")), file = Json.Str(Json.Get(entry, "file")),
                    subtitle = Json.Str(Json.Get(entry, "subtitle")), speechMotion = Json.Str(Json.Get(entry, "speech_motion")),
                    needsReview = Json.Bool(Json.Get(entry, "needs_review")) == true
                };
                foreach (var e in Json.List(Json.Get(entry, "events"))) clip.events.Add(Json.Str(e));
                if (Json.Get(entry, "speech_pose") != null) clip.speechPose = (int)Json.Num(Json.Get(entry, "speech_pose"));
                if (Json.Get(entry, "speech_tempo") != null) clip.speechTempo = Json.Num(Json.Get(entry, "speech_tempo"));
                // Clips whose file isn't there (a copy shared without the voices folder) are left out.
                if (!File.Exists(App.PathOf(clip.file))) { voicesMissing++; continue; }
                voicePack.Add(clip);
            }
            approvedVoices = voicePack.Where(c => !c.needsReview).Select(c => c.Key).ToList();
            settingsPath = App.PathOf("voice-settings.json");
            try
            {
                if (File.Exists(settingsPath))
                {
                    var saved = Json.LoadDict(settingsPath);
                    var rates = Json.Dict(Json.Get(saved, "activityRates"));
                    if (rates != null) foreach (var key in activityRates.Keys.ToList()) if (rates.ContainsKey(key)) activityRates[key] = (int)Math.Max(0, Math.Min(200, Json.Num(rates[key])));
                    if (Json.Get(saved, "rivalEscapes") != null) rivalEscapes = Math.Max(0, (int)Json.Num(Json.Get(saved, "rivalEscapes")));
                    if (Json.Get(saved, "rivalWins") != null) rivalWins = Math.Max(0, (int)Json.Num(Json.Get(saved, "rivalWins")));
                    if (Json.Get(saved, "escapeStreak") != null) escapeStreak = Math.Max(0, Math.Min(5, (int)Json.Num(Json.Get(saved, "escapeStreak"))));
                    if (Json.Get(saved, "petSize") != null) petSize = Math.Max(65.0, Math.Min(160.0, Json.Num(Json.Get(saved, "petSize"))));
                    foreach (var name in SavedBools)
                    {
                        var b = Json.Bool(Json.Get(saved, name));
                        if (b != null) SetBoolSetting(name, b.Value);
                    }
                    double sleep = Json.Num(Json.Get(saved, "idleSleepAfter"), -1);
                    if (new[] { 0.0, 120, 300, 600 }.Contains(sleep)) idleSleepAfter = sleep;
                    var lang = Json.Str(Json.Get(saved, "language")); if (lang == "en" || lang == "ja") language = lang;
                    var ch = Json.Str(Json.Get(saved, "chatter")); if (new[] { "off", "quiet", "normal", "chatty" }.Contains(ch)) chatter = ch;
                    var fm = Json.Str(Json.Get(saved, "fullscreenMode")); if (new[] { "hide", "busy", "ignore" }.Contains(fm)) fullscreenMode = fm;
                    int br = (int)Json.Num(Json.Get(saved, "breakMinutes"), -1); if (new[] { 0, 25, 50, 90 }.Contains(br)) breakMinutes = br;
                    int rev = (int)Json.Num(Json.Get(saved, "voicePackRevision"), 0);
                    if ((rev >= 1 && rev <= 3) && Json.Get(saved, "approvedVoices") != null) approvedVoices = Json.List(Json.Get(saved, "approvedVoices")).Select(x => Json.Str(x)).Where(x => x != "").ToList();
                    if (rev != 2 && rev != 3) foreach (var key in new[] { "ja:your_turn", "ja:had_fun" }) if (!approvedVoices.Contains(key)) approvedVoices.Add(key);
                    if (Json.Get(saved, "volume") != null) volume = Math.Max(0.0, Math.Min(1.0, Json.Num(Json.Get(saved, "volume"))));
                    var city = Json.Get(saved, "weatherCity") as string; if (city != null && city.Length <= 80) weatherCity = city;
                    var unit = Json.Str(Json.Get(saved, "weatherUnit")); if (unit == "C" || unit == "F") weatherUnit = unit;
                    var qh = Json.Str(Json.Get(saved, "quietHours")); if (QuietHourChoices.Contains(qh)) quietHours = qh;
                    if (Json.Get(saved, "stayX") != null && Json.Get(saved, "stayY") != null) { stayX = Json.Num(Json.Get(saved, "stayX")); stayY = Json.Num(Json.Get(saved, "stayY")); }
                }
            }
            catch { } // Invalid settings reset to defaults.
            SetVolume(volume);
        }

        public bool GetBoolSetting(string name)
        {
            var f = typeof(Pet).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return f != null && (bool)f.GetValue(this);
        }
        void SetBoolSetting(string name, bool value)
        {
            var f = typeof(Pet).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(this, value);
        }

        public void SaveSettings()
        {
            if (App.SelfTest || App.Friend) return; // the self-test and the friend never overwrite your settings
            try
            {
                var d = new SortedDictionary<string, object>
                {
                    ["activityRates"] = new SortedDictionary<string, object>(activityRates.ToDictionary(k => k.Key, k => (object)k.Value)),
                    ["rivalEscapes"] = rivalEscapes, ["rivalWins"] = rivalWins, ["escapeStreak"] = escapeStreak,
                    ["idleSleepAfter"] = idleSleepAfter, ["petSize"] = petSize, ["language"] = language, ["volume"] = volume, ["chatter"] = chatter,
                    ["approvedVoices"] = approvedVoices.ToArray(), ["voicePackRevision"] = 3, ["fullscreenMode"] = fullscreenMode, ["breakMinutes"] = breakMinutes,
                    ["weatherCity"] = weatherCity, ["weatherUnit"] = weatherUnit, ["quietHours"] = quietHours
                };
                foreach (var name in SavedBools) d[name] = GetBoolSetting(name);
                if (!double.IsNaN(stayX)) { d["stayX"] = Math.Round(stayX); d["stayY"] = Math.Round(stayY); }
                Json.Save(settingsPath, d);
            }
            catch { ShowProblem("Settings could not be saved."); }
        }

        // Volume goes to Capri's audio session, so changes reach a clip that's already playing.
        void UpdateSfxVolume() { sfxPlayer.Volume = liveVolume && volume > 0 ? Math.Min(1.0, 0.5 / volume) : Math.Min(0.5, volume); }
        public void SetVolume(double level)
        {
            volume = Math.Max(0.0, Math.Min(1.0, level));
            liveVolume = false;
            try { liveVolume = CapriDesktop.SetAppVolume(volume); } catch { }
            UpdateSfxVolume();
        }

        // ---------------------------------------------------------------------------------
        // Speaking poses and the talking mouth
        int speechPose = -1; string speechMotion = "still"; double speechTempo = 1.0, speechStarted;
        bool speechVisible; int speechRestorePose; int[] speechEnvelope;
        CapriVoicePlayback voiceJob; public string lastAudioError = "";

        void EndSpeechPose()
        {
            int[] mouth;
            mouthPoses.TryGetValue(speechPose, out mouth);
            if (speechVisible && (poseIndex == speechPose || (mouth != null && mouth.Contains(poseIndex)))) SetPose(speechRestorePose);
            speechEnvelope = null; speechVisible = false; speechPose = -1;
        }

        void ApplySpeechPose()
        {
            if (!speechVisible || !voiceBusy || speechPose < 0) return;
            if (dragging || mouseDown || spec.Has("noSpeechPose")) return;
            int pose = curAnimPose >= 0 ? curAnimPose : speechPose;
            int[] mouth;
            if (talkingMouth && speechEnvelope != null && mouthPoses.TryGetValue(pose, out mouth))
            {
                int i = (int)Math.Floor((Now - speechStarted - tuning.mouthDelay) * 30.0);
                if (i >= 0 && i < speechEnvelope.Length && speechEnvelope[i] > 0) pose = mouth[speechEnvelope[i] - 1];
            }
            SetPose(pose, true);
            double v = (Now - speechStarted) * speechTempo;
            scale.ScaleX = direction; scale.ScaleY = 1; rotation.Angle = 0; shift.X = 0; shift.Y = 0; sprite.Opacity = 1;
            switch (speechMotion)
            {
                case "beckon": rotation.Angle = direction * (2 + Math.Sin(v * 4) * 2); break;
                case "tilt": rotation.Angle = direction * 4; shift.Y = Math.Sin(v * 2) * 1.5; break;
                case "nod": shift.Y = Math.Sin(v * 5) * 2; break;
                case "laugh": shift.Y = -Math.Abs(Math.Sin(v * 11)) * 4; rotation.Angle = Math.Sin(v * 7) * 2; break;
            }
        }

        void ReportAudioError(string detail)
        {
            lastAudioError = detail;
            App.Log("audio-error.log", detail);
            ShowProblem("Audio problem: " + detail);
        }

        public void StopVoice()
        {
            if (voiceJob != null) { voiceJob.Stop(); voiceJob = null; }
            voiceBusy = false; EndSpeechPose();
            subtitleUntil = 0; messageUntil = 0; bubble.Visibility = Visibility.Collapsed;
        }

        public void ScheduleYap()
        {
            int seconds = chatter == "quiet" ? rng.Next(100, 181) : chatter == "chatty" ? rng.Next(18, 36) : rng.Next(45, 91);
            nextYap = activityRates["talking"] <= 0 ? double.PositiveInfinity : Now + seconds * 100.0 / activityRates["talking"];
        }

        public void PlayVoice(string eventName, bool preview = false, string clipId = "")
        {
            if ((busyMode || isHidden || voiceChooserOpen || activityRates["talking"] <= 0) && !preview) return;
            if (muted) { if (preview) ShowStatus("Voice is muted."); return; }
            double now = Now;
            if (voiceBusy) { if (preview) StopVoice(); else return; }
            if (!preview && (paused || now < voiceNext)) return;
            var clips = voicePack.Where(c => c.language == language && (eventName == "random" || preview || c.events.Contains(eventName))).ToList();
            if (!preview || clipId == "") clips = clips.Where(c => approvedVoices.Contains(c.Key)).ToList();
            if (clipId != "") clips = clips.Where(c => c.id == clipId).ToList();
            if (clips.Count == 0) { if (preview) ShowStatus("That voice clip was not found."); return; }
            if (!preview)
            {
                clips = clips.Where(c => { double at; double delay = c.Key == "en:problem" ? 180 : 90; return !voiceLastPlayed.TryGetValue(c.Key, out at) || now - at >= delay; }).ToList();
                if (clips.Count == 0) return;
                var unheard = clips.Where(c => !voiceLastPlayed.ContainsKey(c.Key)).ToList();
                if (unheard.Count > 0) clips = unheard;
                else clips = new List<VoiceClip> { clips.OrderBy(c => voiceLastPlayed[c.Key]).First() };
            }
            var clip = clips[rng.Next(clips.Count)];
            try
            {
                StopVoice();
                activeCaption = clip.subtitle;
                var path = App.PathOf(clip.file);
                if (!File.Exists(path)) throw new Exception("Missing clip: " + path);
                speechRestorePose = poseIndex;
                speechPose = clip.speechPose ?? 12; speechMotion = clip.speechMotion; speechTempo = clip.speechTempo;
                var bytes = File.ReadAllBytes(path);
                StartSpeechBytes(bytes, subtitles ? activeCaption : null);
                lastClip = clip.id; voiceNext = now + 8;
                voiceLastPlayed[clip.Key] = now;
                ScheduleYap();
            }
            catch (Exception e) { StopVoice(); ReportAudioError(e.Message); }
        }

        void StartSpeechBytes(byte[] bytes, string caption)
        {
            if (!liveVolume) bytes = CapriPcmAudio.WithVolume(bytes, volume);
            speechEnvelope = null;
            try { speechEnvelope = CapriPcmAudio.MouthLevels(CapriPcmAudio.Envelope(bytes, 30.0), tuning.mouthHalf, tuning.mouthWide); } catch { }
            voiceJob = new CapriVoicePlayback(bytes);
            voiceBusy = true; speechVisible = true; speechStarted = Now;
            if (caption != null) { words.Text = caption; bubble.Visibility = Visibility.Visible; subtitleUntil = double.PositiveInfinity; messageUntil = double.PositiveInfinity; }
            voiceJob.Start();
            lastAudioError = "";
        }

        // Speech that isn't a recorded clip (her chat replies read by a Windows voice).
        public bool PlaySpeechWav(byte[] bytes, string caption)
        {
            if (muted || isHidden || bytes == null || bytes.Length < 100) return false;
            try
            {
                StopVoice();
                activeCaption = caption; speechRestorePose = poseIndex;
                speechPose = 12; speechMotion = "nod"; speechTempo = 1.0;
                StartSpeechBytes(bytes, caption);
                voiceNext = Now + 8; ScheduleYap();
                return true;
            }
            catch (Exception e) { StopVoice(); ReportAudioError(e.Message); return false; }
        }
    }
}

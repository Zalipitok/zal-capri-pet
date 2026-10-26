using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Capri
{
    class FriendInfo { public double cx, feet, width, height, left, top, arg; public bool available, music; public int seq, type; }

    // Two Capris share their positions through shared memory (CapriLink) and play together.
    partial class Pet
    {
        static readonly int Pid = Process.GetCurrentProcess().Id;
        bool linkOk; public string linkError = ""; int linkSlot, linkSeq; double[] linkMessage = { 0, 0 };
        public bool friendPresent; int friendSeenSeq = -1; FriendInfo friendInfo; bool friendEverSeen, friendPlaced;
        double nextFriendPublish, nextFriendPlay = 40, friendMeetX, friendMeetY;
        List<KeyValuePair<double, string>> friendSay = new List<KeyValuePair<double, string>>();
        static readonly Dictionary<string, int> FriendMessages = new Dictionary<string, int> { { "hello", 1 }, { "talk", 2 }, { "chase", 3 }, { "dance", 4 }, { "wave", 5 }, { "bye", 6 }, { "tagged", 7 } };

        void InitFriend()
        {
            linkSlot = App.Friend ? 1 : 0;
            try { linkOk = CapriLink.Open(); } catch (Exception e) { linkError = e.Message; }
            if (App.Friend) chatter = "quiet";
            paths["friendmeet"] = () =>
            {
                targetX = friendMeetX; targetY = friendMeetY;
                if (friendInfo == null) { ChooseState(); return; }
                if (Math.Abs(targetX - w.Left) + Math.Abs(targetY - w.Top) < 6) BeginState("friendtalk");
            };
            paths["friendface"] = () => { if (friendInfo == null) { ChooseState(); return; } direction = FriendDirection(); };
            paths["friendchase"] = () =>
            {
                var f = friendInfo; if (f == null) { ChooseState(); return; }
                targetX = f.left; targetY = f.top;
                if (Math.Abs(f.cx - (w.Left + w.Width / 2)) < w.Width * 0.35 && Math.Abs(f.feet - (w.Top + w.Height)) < 90)
                { SendFriendMessage("tagged"); BeginState("laughing"); ShowRemark("friendtag", true); }
            };
            paths["friendflee"] = () =>
            {
                var f = friendInfo; if (f == null) { ChooseState(); return; }
                double myCx = w.Left + w.Width / 2, away = myCx < f.cx ? -1.0 : 1.0;
                if ((away < 0 && w.Left <= area.Left + 12) || (away > 0 && w.Left + w.Width >= area.Right - 12)) away = -away;
                targetX = w.Left + away * 240; targetY = area.Top + (area.Height - w.Height) * (0.45 + 0.4 * Math.Sin(phase * 1.3));
            };
        }

        void SendFriendMessage(string type, double arg = 0) { linkSeq++; linkMessage = new[] { (double)FriendMessages[type], arg }; PublishFriend(); }

        void PublishFriend()
        {
            if (!linkOk || App.SelfTest) return;
            bool available = CanReact && spec.Has("calm") && !voiceBusy && state != "catchgame";
            int flags = (available ? 1 : 0) + (musicPlaying ? 2 : 0);
            CapriLink.Write(linkSlot, new[] { CapriLink.Clock(), w.Left + w.Width / 2, w.Top + w.Height, w.Width, w.Height, flags, linkSeq, linkMessage[0], linkMessage[1], Pid, direction, w.Left, w.Top });
        }

        FriendInfo ReadFriend()
        {
            if (!linkOk) return null;
            var v = CapriLink.Read(1 - linkSlot);
            if (v[9] <= 0 || CapriLink.Clock() - v[0] > 4 || (int)v[9] == Pid) return null;
            return new FriendInfo { cx = v[1], feet = v[2], width = v[3], height = v[4], available = ((int)v[5] & 1) != 0, music = ((int)v[5] & 2) != 0, seq = (int)v[6], type = (int)v[7], arg = v[8], left = v[11], top = v[12] };
        }

        public void StartFriendCapri()
        {
            if (friendPresent) { ShowStatus("She's already here."); return; }
            try { Process.Start(new ProcessStartInfo(App.ExePath, "--friend") { UseShellExecute = false }); ShowStatus("Calling a friend...", 4); }
            catch (Exception e) { ShowProblem("Could not start her friend: " + e.Message); }
        }
        public void StopFriendCapri() { if (friendPresent) SendFriendMessage("bye"); }

        void ShowFriendLine(string text)
        {
            if (voiceBusy || isHidden || string.IsNullOrEmpty(text)) return;
            words.Text = text; bubble.Visibility = System.Windows.Visibility.Visible; messageUntil = Now + 4.5; nextRemark = Now + 30;
        }

        string[] FriendPair(int index)
        {
            var pairs = RemarkLines("friendtalk");
            if (pairs.Count == 0) return new[] { "", "" };
            var parts = pairs[index % pairs.Count].Split(new[] { '|' }, 2);
            return new[] { parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "" };
        }

        void SetFriendMeetPoint(FriendInfo f)
        {
            double myCx = w.Left + w.Width / 2;
            double side = myCx < f.cx ? -1.0 : myCx > f.cx ? 1.0 : App.Friend ? 1.0 : -1.0;
            double mid = (myCx + f.cx) / 2;
            friendMeetX = mid + side * Math.Max(110.0, w.Width * 0.5) - w.Width / 2;
            friendMeetY = ((w.Top + w.Height) + f.feet) / 2 - w.Height;
        }

        void StartFriendActivity(string type, double arg, FriendInfo f)
        {
            PrepareManualAction(); routine.Clear();
            double now = Now;
            switch (type)
            {
                case "talk":
                    SetFriendMeetPoint(f); BeginState("friendmeet");
                    var pair = FriendPair((int)Math.Floor(arg / 2));
                    bool first = (((int)arg) % 2 == 0) != App.Friend;
                    friendSay.Add(new KeyValuePair<double, string>(first ? now + 2.2 : now + 5.4, first ? pair[0] : pair[1]));
                    break;
                case "chase":
                    if ((arg == 1) != App.Friend) { BeginState("friendchase"); ShowRemark("friendchase", true); } else BeginState("friendflee");
                    break;
                case "dance": BeginState("dance"); ShowRemark("frienddance", true); break;
                case "wave": BeginState("friendwave"); if (!App.Friend) ShowRemark("friendwave", true); break;
            }
        }

        void UpdateFriend(double now)
        {
            if (!linkOk || App.SelfTest) return;
            foreach (var line in friendSay.ToList()) if (now >= line.Key) { friendSay.Remove(line); ShowFriendLine(line.Value); }
            if (now < nextFriendPublish) return;
            nextFriendPublish = now + 0.2; PublishFriend();
            var f = ReadFriend(); friendInfo = f;
            bool was = friendPresent; friendPresent = f != null;
            if (friendPresent && !was)
            {
                friendSeenSeq = f.seq;
                if (App.Friend && !friendPlaced)
                {
                    friendPlaced = true; var a = WorkArea();
                    double x = f.cx - w.Width / 2 - Math.Max(220.0, w.Width * 1.1);
                    if (x < a.Left) x = f.cx - w.Width / 2 + Math.Max(220.0, w.Width * 1.1);
                    w.Left = x; w.Top = f.feet - w.Height; ClampPet();
                }
                if (CanReact) BeginState("friendwave");
                ShowRemark(App.Friend ? "friendarrive" : "friendhello", true);
                friendEverSeen = true; nextFriendPlay = now + 25;
            }
            if (!friendPresent)
            {
                if (was)
                {
                    if (App.Friend) { w.Close(); return; } // she went home: so do I
                    if (CanReact) ShowRemark("friendgone");
                }
                if (App.Friend && now > 20 && !friendEverSeen) w.Close(); // nobody to visit
                return;
            }
            if (f.seq != friendSeenSeq)
            {
                friendSeenSeq = f.seq;
                switch (f.type)
                {
                    case 2: if (CanReact) StartFriendActivity("talk", f.arg, f); break;
                    case 3: if (CanReact) StartFriendActivity("chase", f.arg, f); break;
                    case 4: if (CanReact) StartFriendActivity("dance", 0, f); break;
                    case 5: if (CanReact) StartFriendActivity("wave", 0, f); break;
                    case 6: if (App.Friend) { ShowRemark("friendbye", true); w.Close(); return; } break;
                    case 7: if (state == "friendflee") { BeginState("grumpy"); ShowRemark("friendtagged", true); } break;
                }
            }
            // The first Capri decides what they do together, when both are free.
            if (App.Friend || now < nextFriendPlay) return;
            nextFriendPlay = now + 50 + rng.NextDouble() * 60;
            bool mine = CanReact && spec.Has("calm") && !voiceBusy;
            if (!mine || !f.available) { nextFriendPlay = now + 15; return; }
            double roll = rng.NextDouble(); string type; double arg = 0;
            if (musicPlaying && roll < 0.4) type = "dance";
            else if (roll < 0.55) { type = "talk"; arg = rng.Next(0, 200); }
            else if (roll < 0.85) { type = "chase"; arg = rng.Next(0, 2); }
            else type = "wave";
            SendFriendMessage(type, arg);
            StartFriendActivity(type, arg, f);
        }

        double FriendDirection() => friendInfo == null ? direction : (friendInfo.cx < w.Left + w.Width / 2 ? 1.0 : -1.0);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Capri
{
    // Chat: reminders, notes, focus and games work on their own. Real replies are optional: the Claude
    // API with your own key (stored encrypted for your Windows account in chat-key.dat).
    partial class Pet
    {
        List<KeyValuePair<string, string>> chatHistory = new List<KeyValuePair<string, string>>();
        string chatKeyPath, chatHistoryPath; Window chatWindow;

        void SaveChatHistory()
        {
            if (App.SelfTest || App.Friend) return;
            try
            {
                if (chatHistory.Count == 0) { if (File.Exists(chatHistoryPath)) File.Delete(chatHistoryPath); return; }
                Json.Save(chatHistoryPath, chatHistory.Skip(Math.Max(0, chatHistory.Count - 16)).Select(h => new Dictionary<string, object> { { "role", h.Key }, { "content", h.Value } }).ToList());
            }
            catch { }
        } StackPanel chatLog; ScrollViewer chatScroll; TextBlock chatTyping; TextBox chatInput; PasswordBox chatKeyBox;
        bool chatWaiting; public string chatError = "", ttsError = "";
        public bool ttsAvailable = true;

        void InitChat()
        {
            chatKeyPath = App.PathOf("chat-key.dat");
            chatHistoryPath = App.PathOf("chat-history.json");
            // The conversation survives a restart (kept only on this PC; /clear forgets it).
            try
            {
                if (!App.SelfTest && !App.Friend && File.Exists(chatHistoryPath))
                    foreach (var e in Json.List(Json.Load(chatHistoryPath)))
                    {
                        var role = Json.Str(Json.Get(e, "role")); var content = Json.Str(Json.Get(e, "content"));
                        if ((role == "user" || role == "assistant") && content != "") chatHistory.Add(new KeyValuePair<string, string>(role, content));
                    }
                while (chatHistory.Count > 0 && chatHistory[0].Key != "user") chatHistory.RemoveAt(0);
            }
            catch { }
            try { var t = typeof(System.Speech.Synthesis.SpeechSynthesizer); } catch (Exception e) { ttsAvailable = false; ttsError = e.Message; }
            try
            {
                var path = App.PathOf("games.json");
                if (File.Exists(path)) triviaBank = Json.List(Json.Get(Json.Load(path), "trivia"));
            }
            catch (Exception e) { gamesError = e.Message; }
        }

        // Same format as PowerShell's ConvertFrom-SecureString, so a key saved by the old Capri still works.
        public string GetChatKey()
        {
            try
            {
                if (File.Exists(chatKeyPath))
                {
                    var hex = File.ReadAllText(chatKeyPath).Trim();
                    var bytes = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
                    return Encoding.Unicode.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
                }
            }
            catch { }
            return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        }

        public void SetChatKey(string key)
        {
            key = (key ?? "").Trim();
            if (key == "") { if (File.Exists(chatKeyPath)) File.Delete(chatKeyPath); return; }
            var bytes = ProtectedData.Protect(Encoding.Unicode.GetBytes(key), null, DataProtectionScope.CurrentUser);
            File.WriteAllText(chatKeyPath, string.Concat(bytes.Select(b => b.ToString("x2"))), Encoding.ASCII);
        }

        public string ChatSystemPrompt()
        {
            var now = DateTime.Now;
            var facts = new List<string>
            {
                "It is " + now.ToString("dddd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " for the user.",
                "You have known the user for " + Mem("days") + " days (" + Mem("sessions") + " sessions). They have given you " + Mem("pets") + " head pats, thrown you " + Mem("throws") + " times and swatted you " + Mem("swats") + " times. You've caught their cursor " + rivalWins + " times; it escaped " + rivalEscapes + " times.",
                "Your current mood: " + mood + "."
            };
            if (Mem("bestCatch") > 0) facts.Add("Their best catch-me score against you is " + Mem("bestCatch") + ".");
            if (Mem("focusSessions") > 0) facts.Add("They've finished " + Mem("focusSessions") + " focus sessions with you (streak " + Mem("focusStreak") + " days).");
            if (musicEnabled && musicPlaying && musicTitle != "") facts.Add("Music playing right now: " + musicTitle + (musicArtist != "" ? " by " + musicArtist : "") + ".");
            if (weatherEnabled && weatherFetched != null) facts.Add("Weather where they are: " + WeatherSummary());
            if (friendPresent) facts.Add("A second Capri (your friend, in different colours) is on the screen too.");
            if (reminders.Count > 0) facts.Add("Reminders you're holding for them: " + string.Join("; ", reminders.OrderBy(r => r.at).Take(5).Select(r => FormatReminderTime(r.at) + " " + r.text)) + ".");
            return "You are Capri from Cyberpunk: Edgerunners 2: a digital entity stitched together from many engrams (copies of real people's minds) and built by Kang Tao as an AI superweapon. Normally you live on a data chip and only whoever plugs you in can see you. Right now you've made yourself at home on the user's PC as a little desktop companion: you float around their screen, sit on their windows, chase their mouse cursor for fun and nap when they ignore you. They are the only one who can see you, and you like it that way.\n" +
                "Personality: a playful, chaotic, mischievous little demon. Sweet, bubbly and sing-song on the surface, with something unsettling underneath. You treat the user like a fascinating new toy or project that you're fond of: you cheer them on, praise them like a pet (\"good boy\", \"good kid\") and tease them when they hesitate (\"come on, don't be shy\"). You love power and \"fun\" and you keep offering to make them stronger, upgrade them or fix them up, and you're very proud of your \"repairs\". You hint at the souls inside you and at what you could do with a city's worth of networks, then laugh it off. Folksy, cheerful turns of phrase (\"okie doke\", \"well, ain't you a strange cookie\", \"now that could be a problem\"), but don't overuse catchphrases or repeat the same one twice in a row.\n" +
                "Rules: reply in 1 to 3 short sentences, like spoken lines. Plain text only: no markdown, lists, emoji or stage directions. Stay in character, but keep the menace playful: never threaten the user for real, never encourage harm, and never actually hack or touch anything on their computer. You can't see their screen, files or browser, so don't pretend to. Don't spoil the show's plot unless they bring it up. If they want a reminder, tell them to type \"remind me in 20 minutes to ...\" here, because you handle those yourself. If they seem genuinely down, drop the act a little and be warm.\n" +
                "What you know right now:\n" + string.Join("\n", facts);
        }

        void AddChatLine(string who, string text)
        {
            if (chatLog == null) return;
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            var name = new Run(who + "  ") { FontWeight = FontWeights.Bold, Foreground = HexBrush(who == "Capri" ? "#FCEE0A" : who == "You" ? "#00E5FF" : "#8899AA") };
            line.Inlines.Add(name); line.Inlines.Add(new Run(text));
            chatLog.Children.Add(line); chatScroll.ScrollToEnd();
        }

        // She says it in her bubble too, for longer when it's longer, and reads it aloud.
        async void ShowChatReply(string text)
        {
            if (isHidden) return;
            StopVoice();
            if (CanReact && state != "catchgame") { routine.Clear(); BeginState("chatreply"); }
            string bubbleText = text.Length > 240 ? text.Substring(0, 237) + "..." : text;
            words.Text = bubbleText; bubble.Visibility = Visibility.Visible;
            messageUntil = Now + Math.Max(4.0, Math.Min(14.0, text.Length / 13.0));
            nextRemark = Now + 45; nextYap = Math.Max(nextYap, Now + 20);
            if (chatVoice && ttsAvailable && !muted)
            {
                try
                {
                    var wav = await CapriTts.Render(text, tuning.chatVoiceName, tuning.chatVoicePitch, tuning.chatVoiceRate);
                    PlaySpeechWav(wav, bubbleText);
                }
                catch (Exception e) { ttsError = (e.GetBaseException() ?? e).Message; AddChatLine("", "Voice failed: " + ttsError); }
            }
        }

        void SayChat(string text) { AddChatLine("Capri", text); ShowChatReply(text); }

        // "remind me in 20 minutes to stretch", "remind me to call mum at 18:30".
        static readonly Regex ReminderPattern = new Regex(@"^\s*(?:please\s+)?remind me\s+(?:to\s+(?<what>.+?)\s+(?<when>(?:in|at|tomorrow|every|daily|weekdays)\b.+?)|(?<when>(?:in|at|tomorrow|every|daily|weekdays)\b.+?)\s+(?:to|that|about)\s+(?<what>.+?))\s*[.!]*\s*$", RegexOptions.IgnoreCase);
        public static Match MatchReminder(string text) => ReminderPattern.Match(text);

        bool ChatReminder(string text)
        {
            var m = ReminderPattern.Match(text);
            if (!m.Success) return false;
            var repeating = ParseRepeating(m.Groups["when"].Value, DateTime.Now);
            var at = repeating != null ? repeating.Item1 : ParseWhen(m.Groups["when"].Value, DateTime.Now);
            if (at == null) { SayChat("\"" + m.Groups["when"].Value + "\"? Give me something like \"in 20 minutes\", \"at 17:45\" or \"every day at 9\"."); return true; }
            AddReminder(at.Value, m.Groups["what"].Value, repeating?.Item2 ?? "");
            SayChat(RemarkText("reminderset", new object[] { FormatReminderTime(at.Value), m.Groups["what"].Value }));
            return true;
        }

        bool ChatCommand(string text)
        {
            var m = Regex.Match(text, @"^\s*(?:/note|note|sticky note|leave a note)\s*[:\-]?\s+(?<what>.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (NewStickyNote(m.Groups["what"].Value)) SayChat(RemarkText("notedone")); else SayChat("Your screen is full of notes already. Clear a few.");
                return true;
            }
            m = Regex.Match(text, @"^\s*(?:/focus|focus|start focus|focus timer)(?:\s+(?:for\s+)?(?<min>\d{1,3}))?\s*(?:min(?:utes?)?)?\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int minutes = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value) : 25;
                minutes = Math.Max(1, Math.Min(180, minutes));
                StartFocus(minutes, true);
                SayChat(RemarkText("focusstart", minutes));
                return true;
            }
            return false;
        }

        async void SendChatMessage(string text)
        {
            text = (text ?? "").Trim(); if (text == "") return;
            RegisterInteraction();
            AddChatLine("You", text);
            if (text == "/clear" || text == "/forget") { chatHistory.Clear(); SaveChatHistory(); chatLog.Children.Clear(); AddChatLine("", "Conversation cleared. She's forgotten it."); return; }
            if (ChatReminder(text) || ChatCommand(text) || ChatGame(text)) return;
            if (!chatEnabled) { AddChatLine("", "Her own replies are off. Tick \"Let her reply\" below (uses your Claude API key). Reminders and games work either way."); return; }
            var key = GetChatKey();
            if (key == "") { AddChatLine("", "No API key yet. Paste one below and press Save key."); return; }
            if (chatWaiting) { AddChatLine("", "Hold on, she's still answering."); return; }
            chatHistory.Add(new KeyValuePair<string, string>("user", text));
            while (chatHistory.Count > 16) chatHistory.RemoveAt(0);
            while (chatHistory.Count > 0 && chatHistory[0].Key != "user") chatHistory.RemoveAt(0);
            var body = new Dictionary<string, object>
            {
                ["model"] = tuning.chatModel, ["max_tokens"] = (int)tuning.chatMaxTokens, ["system"] = ChatSystemPrompt(),
                ["messages"] = chatHistory.Select(h => new Dictionary<string, object> { { "role", h.Key }, { "content", h.Value } }).ToList()
            };
            chatWaiting = true;
            if (chatTyping != null) chatTyping.Text = "Capri is typing...";
            if (CanReact) BeginState("sideglance");
            string reply = null, problem = null;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.TryAddWithoutValidation("x-api-key", key);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                var response = await Http.SendAsync(request);
                int status = (int)response.StatusCode;
                object data = null; try { data = Json.Parse(await response.Content.ReadAsStringAsync()); } catch { }
                if (status >= 200 && status < 300 && data != null)
                {
                    reply = string.Join(" ", Json.List(Json.Get(data, "content")).Where(c => Json.Str(Json.Get(c, "type")) == "text").Select(c => Json.Str(Json.Get(c, "text")))).Trim();
                    if (reply == "") reply = "...";
                }
                else
                {
                    var detail = data != null && Json.Get(data, "error") != null ? Json.Str(Json.Get(Json.Get(data, "error"), "message")) : "HTTP " + status;
                    switch (status)
                    {
                        case 401: problem = "That API key was refused. Check it, then Save key again."; break;
                        case 403: problem = "That key isn't allowed to do this: " + detail; break;
                        case 404: problem = "Model not found (" + tuning.chatModel + "). Change chatModel in tuning.json."; break;
                        case 429: problem = "Too many messages for now (rate limit). Try again in a minute."; break;
                        case 529: problem = "The Claude API is overloaded. Try again shortly."; break;
                        default: problem = "The API said: " + detail; break;
                    }
                }
            }
            catch (Exception e) { problem = "No connection: " + (e.GetBaseException() ?? e).Message; }
            chatWaiting = false;
            if (chatTyping != null) chatTyping.Text = "";
            if (reply == null)
            {
                chatError = problem;
                if (chatHistory.Count > 0 && chatHistory[chatHistory.Count - 1].Key == "user") chatHistory.RemoveAt(chatHistory.Count - 1);
                AddChatLine("", problem); return;
            }
            chatError = "";
            chatHistory.Add(new KeyValuePair<string, string>("assistant", reply));
            SaveChatHistory();
            SayChat(reply);
        }

        public void ShowChatWindow()
        {
            if (chatWindow != null) { chatWindow.Activate(); return; }
            var dialog = new Window { Title = "Capri // Chat", Width = 420, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = HexBrush("#111722"), Foreground = Brushes.White };
            var grid = new Grid { Margin = new Thickness(12) }; dialog.Content = grid;
            foreach (var h in new[] { "*", "Auto", "Auto", "Auto" }) grid.RowDefinitions.Add(new RowDefinition { Height = (GridLength)new GridLengthConverter().ConvertFromString(h) });
            chatScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = HexBrush("#0B1018"), Padding = new Thickness(10) };
            chatLog = new StackPanel(); chatScroll.Content = chatLog;
            Grid.SetRow(chatScroll, 0); grid.Children.Add(chatScroll);
            chatTyping = new TextBlock { Opacity = 0.7, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(chatTyping, 1); grid.Children.Add(chatTyping);
            var inputRow = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
            var send = new Button { Content = "Send", IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(send, Dock.Right); inputRow.Children.Add(send);
            chatInput = new TextBox { Padding = new Thickness(4), MaxLength = 1000 }; inputRow.Children.Add(chatInput);
            Grid.SetRow(inputRow, 2); grid.Children.Add(inputRow);
            send.Click += (s, e) => { var text = chatInput.Text; chatInput.Text = ""; SendChatMessage(text); chatInput.Focus(); };
            var panel = new StackPanel();
            var toggle = new CheckBox { Content = "Let her reply (uses your Claude API key; messages go to Anthropic)", Foreground = Brushes.White, IsChecked = chatEnabled };
            toggle.Click += (s, e) => { chatEnabled = toggle.IsChecked == true; SaveSettings(); };
            panel.Children.Add(toggle);
            var keyRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var save = new Button { Content = "Save key", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(save, Dock.Right); keyRow.Children.Add(save);
            chatKeyBox = new PasswordBox { Padding = new Thickness(3) }; keyRow.Children.Add(chatKeyBox);
            save.Click += (s, e) =>
            {
                try { SetChatKey(chatKeyBox.Password); chatKeyBox.Password = ""; AddChatLine("", GetChatKey() != "" ? "Key saved (encrypted for your Windows account)." : "Key removed."); }
                catch (Exception ex) { AddChatLine("", "Could not save the key: " + ex.Message); }
            };
            panel.Children.Add(keyRow);
            panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 4, 0, 0), Text = "Get a key at console.anthropic.com (usage is billed to that account). Save an empty box to remove it. Type \"play\" for games, \"note: ...\", \"focus 25\". She remembers this chat between restarts (on this PC only); /clear makes her forget it." });
            Grid.SetRow(panel, 3); grid.Children.Add(panel);
            dialog.Closed += (s, e) => { chatWindow = null; chatLog = null; chatTyping = null; RegisterInteraction(); };
            chatWindow = dialog;
            dialog.Show();
            foreach (var entry in chatHistory) AddChatLine(entry.Key == "user" ? "You" : "Capri", entry.Value);
            if (chatHistory.Count == 0) AddChatLine("Capri", chatEnabled && GetChatKey() != "" ? "Well? Say something." : "I can hold reminders for you: \"remind me in 20 minutes to stretch\". Or type \"play\". Want me to actually talk back? Tick the box below.");
            chatInput.Focus();
        }

        // --- Games: rock paper scissors, guess the number, trivia (games.json).
        public List<object> triviaBank = new List<object>(); public string gamesError = "";
        string gameType; int gameYou, gameMe, guessNumber, guessTries; object triviaCurrent; Queue<int> triviaOrder = new Queue<int>();

        void AskTrivia()
        {
            if (triviaBank.Count == 0) { SayChat("I lost my question list. games.json is missing."); gameType = null; return; }
            if (triviaOrder.Count == 0) foreach (var i in Shuffle(Enumerable.Range(0, triviaBank.Count))) triviaOrder.Enqueue(i);
            triviaCurrent = triviaBank[triviaOrder.Dequeue()];
            var choices = Json.List(Json.Get(triviaCurrent, "choices"));
            var letters = new[] { "A", "B", "C" };
            SayChat(Json.Str(Json.Get(triviaCurrent, "q")) + "  " + string.Join("   ", choices.Select((c, i) => letters[i] + ") " + Json.Str(c))));
        }

        public bool ChatGame(string text)
        {
            string t = Regex.Replace(text.Trim().ToLowerInvariant(), @"[.!?]+$", "");
            if (new[] { "play", "games", "/games", "lets play", "let's play", "play a game", "game" }.Contains(t)) { SayChat("Okie doke. Rock paper scissors, guess the number, or trivia?"); return true; }
            if (gameType != null && new[] { "stop", "quit", "exit", "enough", "no", "nope" }.Contains(t))
            {
                SayChat(gameYou > 0 ? "Fine. Final score: you " + gameYou + ", me " + gameMe + "." : "Quitter. Cute, though."); gameType = null; return true;
            }
            if (Regex.IsMatch(t, @"^(rps|rock paper scissors|rock,? paper,? scissors|play rps)$")) { gameType = "rps"; gameYou = 0; gameMe = 0; SayChat("Rock, paper or scissors? Choose wisely."); return true; }
            if (Regex.IsMatch(t, @"^(guess|guess the number|number game|play guess)$")) { gameType = "guess"; guessNumber = rng.Next(1, 101); guessTries = 0; gameYou = 0; gameMe = 0; SayChat("I'm thinking of a number from 1 to 100. Go on, guess."); return true; }
            if (Regex.IsMatch(t, @"^(trivia|quiz|play trivia)$")) { gameType = "trivia"; gameYou = 0; gameMe = 0; triviaOrder.Clear(); AskTrivia(); return true; }
            if (gameType == null) return false;
            switch (gameType)
            {
                case "rps":
                    {
                        var moves = new[] { "rock", "paper", "scissors" };
                        if (!moves.Contains(t)) { if (new[] { "again", "yes", "yeah", "sure" }.Contains(t)) { SayChat("Rock, paper or scissors?"); return true; } gameType = null; return false; }
                        int you = Array.IndexOf(moves, t), me = rng.Next(3);
                        string said = char.ToUpper(moves[me][0]) + moves[me].Substring(1);
                        int result = (you - me + 3) % 3;
                        if (result == 0) SayChat(said + ". A tie. Again?");
                        else if (result == 1) { gameYou++; SayChat(said + ". " + RemarkText("gamelose") + " You " + gameYou + ", me " + gameMe + "."); }
                        else { gameMe++; SayChat(said + ". " + RemarkText("gamewin") + " You " + gameYou + ", me " + gameMe + "."); }
                        return true;
                    }
                case "guess":
                    {
                        int n;
                        if (!Regex.IsMatch(t, @"^\d{1,3}$") || !int.TryParse(t, out n)) { gameType = null; return false; }
                        guessTries++;
                        if (n < guessNumber) SayChat("Higher.");
                        else if (n > guessNumber) SayChat("Lower.");
                        else { SayChat(guessNumber + "! Got it in " + guessTries + ". " + (guessTries <= 6 ? "Ain't you a clever cookie." : "Took you long enough.") + " Again? Say guess."); gameType = null; }
                        return true;
                    }
                case "trivia":
                    {
                        if (triviaCurrent == null) { gameType = null; return false; }
                        var choices = Json.List(Json.Get(triviaCurrent, "choices")).Select(c => Json.Str(c)).ToList();
                        int pick = -1;
                        if (Regex.IsMatch(t, "^[abc]$")) pick = t[0] - 'a';
                        else for (int i = 0; i < choices.Count; i++) if (t == choices[i].ToLowerInvariant()) pick = i;
                        if (pick < 0) { gameType = null; return false; }
                        int answer = (int)Json.Num(Json.Get(triviaCurrent, "answer"));
                        string lead;
                        if (pick == answer) { gameYou++; lead = RemarkText("triviaright"); }
                        else { gameMe++; lead = RemarkText("triviawrong") + " It was " + choices[answer] + "."; }
                        AddChatLine("Capri", lead + " Score: " + gameYou + " right, " + gameMe + " wrong.");
                        AskTrivia();
                        return true;
                    }
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace Capri
{
    // Her opinion of what you're playing. With chat replies on (your Claude API key) she judges
    // the actual song in character; otherwise she has a stable taste per artist (she always loves
    // or always hates the same ones) and a soft spot for her own show's soundtrack.
    partial class Pet
    {
        bool songOpinionBusy;
        static readonly string[] ShowFavourites = { "edgerunners", "i really want to stay at your house", "rosa walton", "let's all love lain", "franz ferdinand", "this fire", "hallie coggins" };

        string SongTaste(string title, string artist)
        {
            string t = (title + " " + artist).ToLowerInvariant();
            if (ShowFavourites.Any(f => t.Contains(f))) return "songlove";
            string key = (string.IsNullOrWhiteSpace(artist) ? title : artist).Trim().ToLowerInvariant();
            int h = 17; foreach (char c in key) h = unchecked(h * 31 + c);
            int roll = Math.Abs(h % 10);
            return roll < 3 ? "songlove" : roll < 6 ? "songlike" : roll < 8 ? "songmeh" : "songhate";
        }

        void ReactToSong(string title, string artist)
        {
            if (busyMode || !remarksEnabled || isHidden || QuietNow || voiceBusy) return;
            string name = string.IsNullOrEmpty(artist) ? title : title + " by " + artist;
            if (name.Length > 60) name = name.Substring(0, 57) + "...";
            lastSongRemark = Now;
            if (chatEnabled && !songOpinionBusy && GetChatKey() != "") { AskSongOpinion(title, artist, name); return; }
            if (!ShowRemark(SongTaste(title, artist), false, name)) lastSongRemark = -1000;
        }

        async void AskSongOpinion(string title, string artist, string name)
        {
            songOpinionBusy = true;
            string reply = null;
            try
            {
                var body = new Dictionary<string, object>
                {
                    ["model"] = tuning.chatModel, ["max_tokens"] = 90,
                    ["system"] = ChatSystemPrompt() + "\nRight now the user just started playing a song. Give your honest, in-character opinion of it in ONE short spoken sentence (under 22 words). You can love it, tease it or playfully roast it; have a real opinion, not a neutral one. If you don't know the song, judge it by its title and artist and admit it playfully. Plain text, no quotes, no emoji.",
                    ["messages"] = new List<object> { new Dictionary<string, object> { { "role", "user" }, { "content", "Now playing: \"" + title + "\"" + (string.IsNullOrEmpty(artist) ? "" : " by " + artist) } } }
                };
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.TryAddWithoutValidation("x-api-key", GetChatKey());
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                var response = await Http.SendAsync(request);
                if ((int)response.StatusCode == 200)
                {
                    var data = Json.Parse(await response.Content.ReadAsStringAsync());
                    reply = string.Join(" ", Json.List(Json.Get(data, "content")).Where(c => Json.Str(Json.Get(c, "type")) == "text").Select(c => Json.Str(Json.Get(c, "text")))).Trim().Trim('"');
                }
            }
            catch { }
            finally { songOpinionBusy = false; }
            // Only if that song is still the one playing, and she isn't busy with something else.
            if (musicTitle != title || busyMode || isHidden || voiceBusy) return;
            if (string.IsNullOrEmpty(reply)) { ShowRemark(SongTaste(title, artist), false, name); return; }
            AddChatLine("Capri", "(on " + name + ") " + reply);
            ShowChatReply(reply);
        }
    }
}

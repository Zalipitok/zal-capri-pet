// Her chat replies read aloud with a Windows voice (System.Speech). Rendered to 16-bit PCM WAV on a
// worker thread so the animation never stalls; the WAV then plays like her recorded lines (with the
// talking mouth).
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
public static class CapriTts {
 public static string[] Voices() {
  using (SpeechSynthesizer synth = new SpeechSynthesizer()) {
   var list = new System.Collections.Generic.List<string>();
   foreach (InstalledVoice v in synth.GetInstalledVoices()) if (v.Enabled) list.Add(v.VoiceInfo.Name);
   return list.ToArray();
  }
 }
 public static Task<byte[]> Render(string text, string voiceName, string pitch, string rate) {
  return Task.Factory.StartNew<byte[]>(delegate() {
   using (SpeechSynthesizer synth = new SpeechSynthesizer()) {
    bool chosen = false;
    if (!String.IsNullOrEmpty(voiceName)) { try { synth.SelectVoice(voiceName); chosen = true; } catch (Exception) { } }
    if (!chosen) { try { synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult); } catch (Exception) { } }
    MemoryStream pcm = new MemoryStream();
    synth.SetOutputToAudioStream(pcm, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
    string ssml = "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\"><prosody pitch=\"" + Escape(pitch) + "\" rate=\"" + Escape(rate) + "\">" + Escape(text) + "</prosody></speak>";
    try { synth.SpeakSsml(ssml); }
    catch (Exception) { pcm.SetLength(0); synth.Speak(text); }
    synth.SetOutputToNull();
    return Wav(pcm.ToArray(), 22050);
   }
  });
 }
 static string Escape(string s) {
  if (s == null) return "";
  return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
 }
 static byte[] Wav(byte[] data, int rate) {
  MemoryStream m = new MemoryStream(); BinaryWriter w = new BinaryWriter(m);
  w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
  w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
  w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
  w.Flush(); return m.ToArray();
 }
}

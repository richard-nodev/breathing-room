using System;
using System.Speech.Synthesis;

namespace BreathingRoom
{
    // Spoken lines, used when the cat can't be seen (exclusive-fullscreen games, or the cat is hidden).
    public static class Sound
    {
        static SpeechSynthesizer synth;

        public static void Say(string text)
        {
            try
            {
                if (synth == null)
                {
                    synth = new SpeechSynthesizer();
                    synth.SetOutputToDefaultAudioDevice();
                    synth.Volume = 85;
                    try { synth.SelectVoiceByHints(VoiceGender.Female); } catch { }
                }
                synth.SpeakAsyncCancelAll();
                synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                Store.Log("Speech failed: " + ex.Message);
            }
        }
    }
}

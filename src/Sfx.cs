using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BreathingRoom
{
    // One recording that ships inside the exe (listed in assets/sounds/sounds.txt).
    public class SoundClip
    {
        public string Id, Kind, Label;
        public string[] Defaults;
    }

    // The cat's voice. Every sound is a real recording (CC0, see assets/sounds/CREDITS.txt) embedded
    // in the exe. Each moment (logging, "only a few minutes", picked up, happy, waking, petting) plays
    // only the clips the user ticked in the sound picker; until they pick, the defaults from
    // sounds.txt apply. Files the user drops into their own "sounds" folder win over everything.
    public static class Sfx
    {
        public static readonly string[] MeowMoments = { "trill", "question", "mew", "happy", "yawn" };
        const int PicksVersion = 3;

        static readonly Random Rng = new Random();
        static readonly Dictionary<string, float[][]> cache = new Dictionary<string, float[][]>();
        static readonly Dictionary<string, float[]> clipCache = new Dictionary<string, float[]>();
        static List<SoundClip> pool;

        static bool CatOn { get { return Store.Data.Settings.CatSounds; } }

        public static string CustomFolder { get { return Path.Combine(Store.Dir, "sounds"); } }

        static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("sounds." + name);
        }

        public static List<SoundClip> Pool
        {
            get
            {
                if (pool != null) return pool;
                pool = new List<SoundClip>();
                try
                {
                    using (Stream s = Resource("sounds.txt"))
                    {
                        if (s == null) return pool;
                        using (var reader = new StreamReader(s, Encoding.UTF8))
                        {
                            string raw;
                            while ((raw = reader.ReadLine()) != null)
                            {
                                string line = raw.Trim();
                                if (line.Length == 0 || line.StartsWith("#")) continue;
                                string[] f = line.Split('|');
                                if (f.Length < 4) continue;
                                pool.Add(new SoundClip
                                {
                                    Id = f[0],
                                    Kind = f[1],
                                    Label = f[2],
                                    Defaults = f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Store.Log("Could not read the sound list: " + ex.Message);
                }
                return pool;
            }
        }

        public static float[] Load(SoundClip clip)
        {
            float[] data;
            if (clipCache.TryGetValue(clip.Id, out data)) return data;
            using (Stream s = Resource(clip.Id + ".wav"))
                data = s == null ? null : Audio.LoadWav(s, clip.Id);
            clipCache[clip.Id] = data;
            return data;
        }

        // ---------- the user's picks ----------

        static bool DefaultPick(string moment, string id)
        {
            SoundClip c = Pool.Find(x => x.Id == id);
            return c != null && Array.IndexOf(c.Defaults, moment) >= 0;
        }

        public static bool IsPicked(string moment, string id)
        {
            // Game sounds aren't in the picker; they always use their defaults.
            if (moment == "bounce" || moment == "pop") return DefaultPick(moment, id);
            List<string> picks = Store.Data.Settings.SoundPicks;
            return picks == null ? DefaultPick(moment, id) : picks.Contains(moment + ":" + id);
        }

        public static void SetPicked(string moment, string id, bool on)
        {
            Settings s = Store.Data.Settings;
            if (s.SoundPicks == null)
            {
                // First change: write the defaults out so they become the user's own list.
                s.SoundPicks = new List<string>();
                foreach (string m in MeowMoments.Concat(new[] { "purr" }))
                    foreach (SoundClip c in Pool)
                        if (DefaultPick(m, c.Id)) s.SoundPicks.Add(m + ":" + c.Id);
            }
            string key = moment + ":" + id;
            s.SoundPicks.Remove(key);
            if (on) s.SoundPicks.Add(key);
            Store.Save();
            Reload();
        }

        public static void ResetPicks()
        {
            Store.Data.Settings.SoundPicks = null;
            Store.Save();
            Reload();
        }

        // Keeps every choice the user made across versions: v2 added the sleepy yawns (ticked once,
        // the user asked for them), v3 removed the synthesized sounds.
        public static void Migrate()
        {
            Settings s = Store.Data.Settings;
            if (s.SoundPicksVersion >= PicksVersion) return;
            if (s.SoundPicks != null)
            {
                if (s.SoundPicksVersion < 2)
                    foreach (SoundClip c in Pool)
                        if (Array.IndexOf(c.Defaults, "yawn") >= 0 && !s.SoundPicks.Contains("yawn:" + c.Id))
                            s.SoundPicks.Add("yawn:" + c.Id);
                s.SoundPicks.RemoveAll(p => p.EndsWith(":synth"));
            }
            // v3 also turned the synthesized Windows voice off by default; it stays available in Settings.
            if (s.SoundPicksVersion < 3 && s.Voice == "auto") s.Voice = "off";
            s.SoundPicksVersion = PicksVersion;
            Store.Save();
            Reload();
        }

        static float[][] UserFiles(string name)
        {
            try
            {
                if (!Directory.Exists(CustomFolder)) return null;
                var clips = new List<float[]>();
                foreach (string file in Directory.GetFiles(CustomFolder, name + "*.wav"))
                {
                    float[] clip = Audio.LoadWav(file);
                    if (clip != null && clip.Length > 0) clips.Add(clip);
                }
                return clips.Count > 0 ? clips.ToArray() : null;
            }
            catch (Exception ex)
            {
                Store.Log("Could not load sounds from " + CustomFolder + ": " + ex.Message);
                return null;
            }
        }

        // Everything a moment may play. Empty means that moment stays quiet.
        static float[][] Resolve(string moment)
        {
            float[][] own = UserFiles(moment);
            if (own != null) return own;
            string kind = moment == "purr" ? "purr" : moment == "bounce" || moment == "pop" ? "game" : "meow";
            var clips = new List<float[]>();
            foreach (SoundClip c in Pool)
            {
                if (c.Kind != kind || !IsPicked(moment, c.Id)) continue;
                float[] data = Load(c);
                if (data != null && data.Length > 0) clips.Add(data);
            }
            return clips.ToArray();
        }

        static float[] Get(string moment)
        {
            float[][] set;
            if (!cache.TryGetValue(moment, out set))
            {
                set = Resolve(moment);
                cache[moment] = set;
            }
            return set.Length == 0 ? null : set[Rng.Next(set.Length)];
        }

        public static void Reload()
        {
            cache.Clear();
        }

        // Plays a clip from the sound picker, even when cat sounds are switched off.
        public static void Preview(float[] clip, bool loop)
        {
            Audio.StopLoop("preview", 0.15);
            if (clip == null) return;
            if (!loop)
            {
                Audio.Play(clip, 0.6f);
                return;
            }
            Audio.Loop("preview", clip, 0.5f, 0.3);
            var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            stop.Tick += delegate { stop.Stop(); Audio.StopLoop("preview", 0.8); };
            stop.Start();
        }

        // Which clips each moment will use (used by test runs).
        public static string Describe()
        {
            var lines = new List<string>();
            foreach (string name in MeowMoments.Concat(new[] { "purr" }))
            {
                var ids = Pool.Where(c => (c.Kind == "purr") == (name == "purr") && IsPicked(name, c.Id)).Select(c => c.Id).ToList();
                lines.Add(name + ": " + (UserFiles(name) != null ? "user files" : ids.Count == 0 ? "(quiet)" : string.Join(", ", ids)));
            }
            lines.Add("pool: " + Pool.Count + " clips, all loadable: " + Pool.All(c => Load(c) != null).ToString(CultureInfo.InvariantCulture));
            return string.Join(Environment.NewLine, lines);
        }

        static void Cat(string moment, float volume)
        {
            if (CatOn) Audio.Play(Get(moment), volume);
        }

        public static void Trill() { Cat("trill", 0.5f); }
        public static void Mew() { Cat("mew", 0.42f); }
        public static void Question() { Cat("question", 0.42f); }
        public static void Happy() { Cat("happy", 0.4f); }
        public static void Yawn() { Cat("yawn", 0.36f); }

        // Real recordings for the games (a ping-pong ball, a bubble), with the cat sounds' switch.
        public static void Bounce(double strength) { Cat("bounce", (float)Math.Max(0.05, Math.Min(0.5, strength * 0.5))); }
        public static void Pop() { Cat("pop", 0.35f); }

        public static void PurrOn(float level)
        {
            if (CatOn) Audio.Loop("purr", Get("purr"), level, 0.8);
        }

        public static void PurrOff()
        {
            Audio.StopLoop("purr", 1.0);
        }
    }
}

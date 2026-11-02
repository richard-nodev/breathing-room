using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BreathingRoom
{
    // One logged cigarette. Check-in answers are filled in later, only if the user answered.
    public class SmokeEvent
    {
        public string Id { get; set; }
        public string Time { get; set; }
        public string App { get; set; }
        public bool Fullscreen { get; set; }
        public bool Retro { get; set; }
        public string Decided { get; set; }   // "decided" | "hand"
        public string Want { get; set; }      // "alot" | "bit" | "notreally"
        public string Taste { get; set; }     // "good" | "meh" | "didnt"
        public string Moment { get; set; }    // "gaming" | "working" | "bored" | "stressed"

        // On the continuous clock: use for ordering and gaps.
        [ScriptIgnore]
        public DateTime At
        {
            get { return Store.ParseTime(Time); }
            set { Time = Store.FormatTime(value); }
        }

        // What the clock on the wall said when it happened: use for showing and exporting times.
        [ScriptIgnore]
        public DateTime Wall
        {
            get { return Store.WallTime(Time); }
        }
    }

    public class Settings
    {
        public Settings()
        {
            PetName = "Mochi";
            ShowPet = true;
            Afterglow = true;
            AfterglowEdge = "top";
            Badge = false;
            BadgeCorner = "topright";
            Hotkey = "Ctrl+Alt+S";
            Checkins = "sometimes";
            Voice = "off";
            CatSounds = true;
            RememberApps = true;
            Volume = 70;
            LastGame = "fetch";
            BreakMinutes = 60;
            CatCoat = "ginger";
            CatAccessory = "none";
            Currency = "$";
            PackSize = 20;
            PetX = double.NaN;
            PetY = double.NaN;
            CheckUpdates = true;
        }

        public string PetName { get; set; }
        public bool ShowPet { get; set; }
        public bool Afterglow { get; set; }
        public string AfterglowEdge { get; set; }   // "top" | "bottom"
        public bool Badge { get; set; }
        public string BadgeCorner { get; set; }     // "topright" | "topleft" | "bottomright" | "bottomleft"
        public string Hotkey { get; set; }
        public string Checkins { get; set; }        // "often" | "sometimes" | "never"
        public string Voice { get; set; }           // "auto" | "always" | "off"
        public bool CatSounds { get; set; }
        public bool RememberApps { get; set; }      // store the foreground app's name with each log
        public int Volume { get; set; }             // 0..100
        public int BreakMinutes { get; set; }       // nudge to get up after this long at the PC; 0 = off
        public string CatCoat { get; set; }         // see Looks.Coats
        public string CatAccessory { get; set; }    // see Looks.Accessories
        public string LastGame { get; set; }
        public string LastScene { get; set; }
        public List<string> SoundPicks { get; set; }   // "moment:clipId"; null = the defaults in sounds.txt
        public int SoundPicksVersion { get; set; }
        public double PackPrice { get; set; }
        public int PackSize { get; set; }
        public string Currency { get; set; }
        public double PetX { get; set; }
        public double PetY { get; set; }
        public int PetLayout { get; set; }          // 2 = PetX/PetY are for the small cat window
        public bool CheckUpdates { get; set; }      // ask GitHub about once a day whether there's a newer version
        public string LastUpdateCheck { get; set; } // UTC, round-trip format
        public string QuietVersion { get; set; }    // "Not now" on this version: the cat won't mention it again
    }

    public class AppData
    {
        public AppData()
        {
            Version = 1;
            Smokes = new List<SmokeEvent>();
            ChangedMind = new List<string>();
            RodeOut = new List<string>();
            Shown = new List<string>();
            Settings = new Settings();
            Best = new Dictionary<string, int>();
        }

        public int Version { get; set; }
        public Dictionary<string, int> Best { get; set; }
        public List<string> RodeOut { get; set; }     // times an urge passed after "breathe with me"
        public int Friendship { get; set; }
        public string FriendshipDay { get; set; }
        public int FriendshipToday { get; set; }
        public List<string> Shown { get; set; }       // one-time notes already shown (weekly recap, first week)
        public bool Welcomed { get; set; }
        public List<SmokeEvent> Smokes { get; set; }
        public List<string> ChangedMind { get; set; }
        public Settings Settings { get; set; }
        public string LastCheckin { get; set; }
        public string LastDriftNotice { get; set; }
        public string LastMorningHello { get; set; }
    }

    public static class Store
    {
        public static string Dir { get; private set; }
        public static string FilePath { get; private set; }
        public static AppData Data { get; private set; }

        public static void Init()
        {
            string custom = Environment.GetEnvironmentVariable("BR_DATA_DIR");
            Dir = !string.IsNullOrEmpty(custom)
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreathingRoom");
            Directory.CreateDirectory(Dir);
            FilePath = Path.Combine(Dir, "data.json");
            Data = LoadFrom(FilePath);
        }

        public static bool IsTestMode
        {
            get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BR_DATA_DIR")); }
        }

        static JavaScriptSerializer Serializer()
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        // Files that couldn't be read and couldn't be copied aside yet. Saving waits for them.
        static readonly List<string> unsaved = new List<string>();

        static bool KeepAside(string path)
        {
            try
            {
                File.Copy(path, path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
                return true;
            }
            catch (Exception ex)
            {
                Log("Could not keep a copy of " + path + ": " + ex.Message);
                return false;
            }
        }

        // At logon an antivirus scan can hold the file for a moment, so a failed read is retried.
        static string ReadWithRetry(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return File.ReadAllText(path, Encoding.UTF8); }
                catch (IOException)
                {
                    if (attempt >= 5) throw;
                    System.Threading.Thread.Sleep(300);
                }
            }
        }

        public static AppData LoadFrom(string path)
        {
            AppData data = null;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    data = Serializer().Deserialize<AppData>(ReadWithRetry(candidate));
                    if (data != null) break;
                    Log("Empty data in " + candidate);
                }
                catch (Exception ex)
                {
                    Log("Could not read " + candidate + ": " + ex.Message);
                }
                // Never let a later save overwrite a file that couldn't be read: keep a copy aside.
                // If even that fails (still locked), Save() keeps trying before it writes anything.
                if (!KeepAside(candidate)) unsaved.Add(candidate);
            }
            if (data == null) data = new AppData();
            if (data.Smokes == null) data.Smokes = new List<SmokeEvent>();
            if (data.ChangedMind == null) data.ChangedMind = new List<string>();
            if (data.Settings == null) data.Settings = new Settings();
            if (data.Best == null) data.Best = new Dictionary<string, int>();
            if (data.RodeOut == null) data.RodeOut = new List<string>();
            if (data.Shown == null) data.Shown = new List<string>();
            data.Smokes = data.Smokes.Where(s => s != null && s.At != DateTime.MinValue).OrderBy(s => s.At).ToList();
            return data;
        }

        // Write to a temp file first so a crash mid-write never eats the history.
        public static void Save()
        {
            if (unsaved.Count > 0)
            {
                unsaved.RemoveAll(p => !File.Exists(p) || KeepAside(p));
                if (unsaved.Count > 0) return;
            }
            try
            {
                string json = Serializer().Serialize(Data);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(FilePath))
                    File.Replace(tmp, FilePath, FilePath + ".bak", true);
                else
                    File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Log("Save failed: " + ex.Message);
            }
        }

        public static void AddSmoke(SmokeEvent ev)
        {
            if (string.IsNullOrEmpty(ev.Id)) ev.Id = Guid.NewGuid().ToString("N");
            Data.Smokes.Add(ev);
            Data.Smokes = Data.Smokes.OrderBy(s => s.At).ToList();
            Save();
        }

        public static void RemoveSmoke(string id)
        {
            Data.Smokes.RemoveAll(s => s.Id == id);
            Save();
        }

        // Wipes the history but keeps settings. Saved twice so the backup copy is clean too.
        public static void ClearHistory()
        {
            Data.Smokes.Clear();
            Data.ChangedMind.Clear();
            Data.RodeOut.Clear();
            Data.Shown.Clear();
            Data.Best.Clear();
            Data.LastCheckin = null;
            Data.LastDriftNotice = null;
            Data.LastMorningHello = null;
            Save();
            Save();
        }

        public static SmokeEvent FindSmoke(string id)
        {
            return Data.Smokes.FirstOrDefault(s => s.Id == id);
        }

        public static List<DateTime> Times()
        {
            return Data.Smokes.Select(s => s.At).ToList();
        }

        // Times are stored as exact instants with their UTC offset. In memory they live on one
        // continuous local clock (today's UTC offset), so ordering and gaps stay right across a
        // daylight-saving change, when the same wall-clock hour happens twice.
        static TimeSpan Offset { get { return TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow); } }

        public static string FormatTime(DateTime t)
        {
            DateTime utc = DateTime.SpecifyKind(t - Offset, DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToLocalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        public static DateTime WallTime(string s)
        {
            DateTimeOffset t;
            if (string.IsNullOrEmpty(s) ||
                !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t))
                return DateTime.MinValue;
            return t.LocalDateTime;
        }

        public static DateTime ParseTime(string s)
        {
            DateTimeOffset t;
            if (string.IsNullOrEmpty(s) ||
                !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t))
                return DateTime.MinValue;
            return DateTime.SpecifyKind(t.UtcDateTime + Offset, DateTimeKind.Local);
        }

        public static void Log(string message)
        {
            try
            {
                string dir = Dir ?? Path.GetTempPath();
                File.AppendAllText(Path.Combine(dir, "errors.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}

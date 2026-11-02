using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace BreathingRoom
{
    // Updates from the project's GitHub releases.
    //
    // Once a day (Settings can turn it off) it asks GitHub for the latest release: the same public
    // page anyone can open, and nothing about the user is sent. When there is a newer version the
    // cat mentions it once, and About and the tray offer "Update now". That downloads the release
    // zip, checks it against the release's SHA-256 file, puts the new exe in place of this one (a
    // running exe can be renamed aside, so nothing needs admin rights) and restarts. If anything
    // goes wrong, the old exe stays where it was.
    public static class Updater
    {
        public class Release
        {
            public Version Version;
            public string Tag, PageUrl, ZipUrl, ShaUrl;
        }

        const string UpdatedFlag = "--updated-from";

        // A newer release than this one, once a check has found it.
        public static Release Available { get; private set; }

        // Raised on the UI thread the first time a check finds a given newer version.
        public static event Action<Release> Found;

        // What "Update now" does (the controller sets it: it needs to quit the app afterwards).
        public static Action<Release> Install;

        // This copy was started by an update (see AfterUpdate).
        public static bool JustUpdated { get; private set; }

        static DispatcherTimer timer;
        static bool checking;
        static readonly List<Action<Release, string>> waiting = new List<Action<Release, string>>();

        // Raised on the UI thread when "Update now" didn't work (the reason in plain words).
        public static event Action<string> Failed;

        public static void ReportFailure(string error)
        {
            if (Failed != null) Failed(error);
        }

        static Updater()
        {
            // GitHub only speaks TLS 1.2 and newer; older defaults of .NET 4.x would fail.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
        }

        public static Version Current { get { return Normal(new Version(AppInfo.Version)); } }

        static Version Normal(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        // "owner/name" from AppInfo.ProjectUrl, or null until the project has a GitHub page.
        static string Repo
        {
            get
            {
                const string prefix = "https://github.com/";
                string url = AppInfo.ProjectUrl;
                if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
                string[] parts = url.Substring(prefix.Length).Trim('/').Split('/');
                return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0 ? parts[0] + "/" + parts[1] : null;
            }
        }

        // Test runs can point the check at a local server instead of GitHub.
        static string TestUrl
        {
            get { return Store.IsTestMode ? Environment.GetEnvironmentVariable("BR_UPDATE_URL") : null; }
        }

        // Test runs never ask GitHub: only the local server they name, if any.
        static string ApiUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(TestUrl)) return TestUrl;
                string repo = Repo;
                return repo == null || Store.IsTestMode ? null : "https://api.github.com/repos/" + repo + "/releases/latest";
            }
        }

        // Whether the app has somewhere to look for updates at all (About shows the section then).
        public static bool Enabled { get { return Repo != null || !string.IsNullOrEmpty(TestUrl); } }

        // Downloads come only from this project's own releases (or the test server).
        static bool Trusted(string url)
        {
            if (!string.IsNullOrEmpty(TestUrl))
                return url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase);
            string repo = Repo;
            return repo != null && url.StartsWith("https://github.com/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- checking ----------

        static DateTime LastCheck
        {
            get
            {
                DateTime t;
                return DateTime.TryParse(Store.Data.Settings.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)
                    ? t.ToUniversalTime() : DateTime.MinValue;
            }
        }

        // A first look a minute after startup, then about once a day while the app runs.
        public static void Start()
        {
            if (ApiUrl == null || timer != null) return;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Store.IsTestMode ? 5 : 60) };
            timer.Tick += delegate
            {
                timer.Interval = TimeSpan.FromHours(1);
                Settings s = Store.Data.Settings;
                if (!s.CheckUpdates) return;
                if (!Store.IsTestMode && (DateTime.UtcNow - LastCheck).TotalHours < 23) return;
                Check(null);
            };
            timer.Start();
        }

        // Asks for the latest release in the background; done(newer release or null, error or null)
        // runs on the UI thread.
        public static void Check(Action<Release, string> done)
        {
            string api = ApiUrl;
            if (api == null)
            {
                if (done != null) done(Available, Store.IsTestMode ? "test runs don't go online" : "no project page yet");
                return;
            }
            if (done != null) waiting.Add(done);
            if (checking) return;   // the check already under way answers everyone
            checking = true;
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            var thread = new Thread(() =>
            {
                Release latest = null;
                string error = null;
                try { latest = Fetch(api); }
                catch (Exception ex) { error = Plain(ex); }
                ui.BeginInvoke(new Action(delegate
                {
                    checking = false;
                    if (error == null)
                    {
                        Store.Data.Settings.LastUpdateCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        Store.Save();
                    }
                    else Store.Log("Update check failed: " + error);
                    // Only a release that can actually be installed counts, and one that was
                    // taken back stops being offered.
                    Release newer = latest != null && latest.Version > Current && latest.ZipUrl != null && latest.ShaUrl != null ? latest : null;
                    if (error == null)
                    {
                        bool fresh = newer != null && (Available == null || Available.Version != newer.Version);
                        Available = newer;
                        if (fresh && Found != null) Found(newer);
                    }
                    var answer = new List<Action<Release, string>>(waiting);
                    waiting.Clear();
                    foreach (var a in answer) a(newer, error);
                }));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        static string Plain(Exception ex)
        {
            var web = ex as WebException;
            if (web != null && web.Status != WebExceptionStatus.ProtocolError) return "couldn't reach GitHub";
            return ex.Message;
        }

        static Release Fetch(string api)
        {
            var data = new JavaScriptSerializer().DeserializeObject(Text(api)) as Dictionary<string, object>;
            if (data == null) throw new InvalidDataException("GitHub sent something unexpected");
            string tag = Field(data, "tag_name");
            Version v;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out v)) throw new InvalidDataException("the latest release has no version number");
            var release = new Release { Version = Normal(v), Tag = tag, PageUrl = Field(data, "html_url") };
            object assets;
            if (data.TryGetValue("assets", out assets) && assets is object[])
                foreach (object o in (object[])assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Field(a, "name"), url = Field(a, "browser_download_url");
                    if (name == null || url == null || !Trusted(url)) continue;
                    if (name.EndsWith("-windows.zip", StringComparison.OrdinalIgnoreCase)) release.ZipUrl = url;
                    else if (name.EndsWith("-windows.zip.sha256", StringComparison.OrdinalIgnoreCase)) release.ShaUrl = url;
                }
            return release;
        }

        static string Field(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }

        static HttpWebRequest Request(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "BreathingRoom/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json, application/octet-stream, */*";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            req.AllowAutoRedirect = true;   // release files redirect to GitHub's download servers
            return req;
        }

        static string Text(string url)
        {
            using (var res = (HttpWebResponse)Request(url).GetResponse())
            using (var reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // ---------- installing ----------

        // Downloads, checks and swaps in the new exe in the background. progress(0..1) and
        // finished(error or null) run on the UI thread; after a null error, call Restart.
        public static void Download(Release release, Action<double> progress, Action<string> finished)
        {
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            var thread = new Thread(() =>
            {
                string error = null;
                try { Fetch(release, ui, progress); }
                catch (Exception ex)
                {
                    error = Plain(ex);
                    Store.Log("Update failed: " + ex);
                }
                ui.BeginInvoke(new Action(delegate { finished(error); }));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        static string Folder { get { return Path.Combine(Path.GetTempPath(), "BreathingRoom-update"); } }

        static void Fetch(Release release, Dispatcher ui, Action<double> progress)
        {
            if (release.ZipUrl == null) throw new InvalidDataException("this release has no Windows download");
            if (release.ShaUrl == null) throw new InvalidDataException("this release has no checksum, so it can't be checked");
            string dir = Folder;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "update.zip");

            using (var res = (HttpWebResponse)Request(release.ZipUrl).GetResponse())
            using (var input = res.GetResponseStream())
            using (var output = File.Create(zip))
            {
                long total = res.ContentLength, done = 0;
                var buffer = new byte[81920];
                int n;
                double shown = -1;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, n);
                    done += n;
                    if (total > 0 && progress != null && (double)done / total - shown >= 0.02)
                    {
                        shown = (double)done / total;
                        double p = shown;
                        ui.BeginInvoke(new Action(delegate { progress(p); }));
                    }
                }
            }

            // The checksum file reads "<hex>  <file name>".
            string expected = Text(release.ShaUrl).Trim().Split(' ', '\t', '\r', '\n')[0];
            string actual;
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(zip))
                actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the download didn't match its checksum");

            string fresh = Path.Combine(dir, "BreathingRoom.exe");
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                ZipArchiveEntry entry = null;
                foreach (ZipArchiveEntry e in archive.Entries)
                    if (string.Equals(e.Name, "BreathingRoom.exe", StringComparison.OrdinalIgnoreCase)) entry = e;
                if (entry == null) throw new InvalidDataException("the download has no BreathingRoom.exe");
                entry.ExtractToFile(fresh, true);
            }
            Version built = Normal(AssemblyName.GetAssemblyName(fresh).Version);
            if (built < release.Version) throw new InvalidDataException("the download is version " + built + ", not " + release.Version);

            // Swap: the new exe is copied next to this one first, so the moment there's no exe under
            // the usual name is just two renames long. This exe steps aside (allowed while it runs)
            // and the new one takes its name; if that fails, this one goes back.
            string current = Assembly.GetEntryAssembly().Location, old = current + ".old", next = current + ".new";
            if (File.Exists(next)) File.Delete(next);
            File.Copy(fresh, next);
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try { File.Move(next, current); }
            catch
            {
                try { if (File.Exists(current)) File.Delete(current); } catch (IOException) { }
                File.Move(old, current);
                throw;
            }
        }

        // Starts the new exe, which waits for this one to finish closing.
        public static void Restart()
        {
            string exe = Assembly.GetEntryAssembly().Location;
            Process.Start(new ProcessStartInfo(exe, UpdatedFlag + " " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
        }

        // At the very start of Main: if an update started this copy, wait for the old one to quit
        // (it holds the single-instance lock until then).
        public static void AfterUpdate(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                int pid;
                if (args[i] != UpdatedFlag || !int.TryParse(args[i + 1], out pid)) continue;
                JustUpdated = true;
                try { Process.GetProcessById(pid).WaitForExit(15000); }
                catch (ArgumentException) { }   // already gone
            }
        }

        // Once the app is running: remove what the last update left behind.
        public static void CleanUp()
        {
            try
            {
                string exe = Assembly.GetEntryAssembly().Location;
                foreach (string leftover in new[] { exe + ".old", exe + ".new" })
                    if (File.Exists(leftover)) File.Delete(leftover);
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            }
            catch (Exception ex) { Store.Log("Update clean-up: " + ex.Message); }
        }

        // The release's page on GitHub (only ever a github.com page of this project).
        public static void OpenPage(Release release)
        {
            string url = AppInfo.ProjectUrl;
            if (release != null && release.PageUrl != null && !string.IsNullOrEmpty(url)
                && release.PageUrl.StartsWith(url.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) url = release.PageUrl;
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(url); }
            catch (Exception ex) { Store.Log("Open link failed: " + ex.Message); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BreathingRoom
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Updater.AfterUpdate(args);
            if (Store.IsTestMode)
            {
                // For measuring: BR_RENDER=software draws like a PC without a graphics card, and
                // BR_CACHE=off draws every room from scratch each frame.
                if (Environment.GetEnvironmentVariable("BR_RENDER") == "software") System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                if (Environment.GetEnvironmentVariable("BR_RENDER") == "hardware") PlayWindow.KeepHardware = true;
                if (Environment.GetEnvironmentVariable("BR_CACHE") == "off") Scenes.NoCache = true;
            }
            bool created;
            string mutexName = Store.IsTestMode ? "BreathingRoom.Test" : "BreathingRoom.SingleInstance";
            using (var instance = new Mutex(true, mutexName, out created))
            {
                // Right after an update the old copy may still be closing: wait for it to let go.
                if (!created && Updater.JustUpdated)
                {
                    try { created = instance.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created) return;
                Store.Init();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Store.Log(e.Exception.ToString()); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Store.Log(e.ExceptionObject.ToString());
                // Tray menu clicks run through WinForms, which would otherwise show its own crash dialog.
                Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
                Forms.Application.ThreadException += (s, e) => Store.Log(e.Exception.ToString());
                var controller = new Controller();
                app.Startup += delegate { controller.Start(); };
                app.Run();
            }
        }
    }

    public static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BreathingRoom";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        // If the exe moved (a new download, a rebuilt copy), point the startup entry at this copy.
        public static void Repair()
        {
            if (Store.IsTestMode) return;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    string current = k.GetValue(ValueName) as string;
                    if (current == null) return;
                    // Only when the registered exe is gone: running a stray copy once (say from
                    // Downloads) must not move the startup entry to it.
                    if (System.IO.File.Exists(current.Trim('"'))) return;
                    k.SetValue(ValueName, "\"" + Assembly.GetEntryAssembly().Location + "\"");
                }
            }
            catch (Exception ex)
            {
                Store.Log("Startup repair failed: " + ex.Message);
            }
        }

        public static void Set(bool on)
        {
            if (Store.IsTestMode) return;   // test runs must never touch the real startup list
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(ValueName, "\"" + Assembly.GetEntryAssembly().Location + "\"");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Store.Log("Startup setting failed: " + ex.Message);
            }
        }
    }

    public class GlobalHotkey : IDisposable
    {
        int id = 0x5A11;
        readonly HwndSource source;
        public event Action Pressed;
        public string Current { get; private set; }

        public GlobalHotkey()
        {
            var p = new HwndSourceParameters("BreathingRoomHotkey");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = 0;
            p.ParentWindow = new IntPtr(-3);   // message-only window
            source = new HwndSource(p);
            source.AddHook(Hook);
        }

        // The new key is registered before the old one is released, so a key that's taken by
        // another app never leaves the user without a working one.
        public bool Register(string combo)
        {
            uint mods, vk;
            if (!Parse(combo, out mods, out vk)) return false;
            int next = id == 0x5A11 ? 0x5A12 : 0x5A11;
            if (!Native.RegisterHotKey(source.Handle, next, mods | Native.MOD_NOREPEAT, vk)) return false;
            if (Current != null) Native.UnregisterHotKey(source.Handle, id);
            id = next;
            Current = combo;
            return true;
        }

        static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrEmpty(combo)) return false;
            foreach (string raw in combo.Split('+'))
            {
                string part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default:
                        Key key;
                        if (!Enum.TryParse(part, true, out key)) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        break;
                }
            }
            return vk != 0;
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == id)
            {
                handled = true;
                if (Pressed != null) Pressed();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Native.UnregisterHotKey(source.Handle, id);
            source.Dispose();
        }
    }

    // Ties the pieces together: logging, the cat's replies, check-ins, overlays and the tray.
    public class Controller
    {
        PetWindow pet;
        AfterglowWindow glow;
        BadgeWindow badge;
        GlobalHotkey hotkey;
        Forms.NotifyIcon tray;
        Forms.ToolStripMenuItem petItem, glowItem, badgeItem, updateItem;
        Updater.Release pendingUpdate;
        bool updating;
        InsightsWindow insights;
        Window settingsWindow;
        DispatcherTimer laterCheckin;
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        readonly DispatcherTimer foreground = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        bool playing;
        DateTime napUntil = DateTime.MinValue;
        string failedHotkey;
        DateTime? pendingBreakStart, pendingBreakEnd;

        // For "did one slip past me?": when the current stretch of being at the PC began, and breaks.
        DateTime sessionStart = DateTime.Now;
        DateTime breakStart;
        bool onBreak, longAway, askedThisGap;

        static Settings S { get { return Store.Data.Settings; } }
        bool PetVisible { get { return pet != null && pet.IsVisible; } }

        public void Start()
        {
            if (!Store.Data.Welcomed) new WelcomeWindow(delegate { StartMain(true); }).Show();
            else StartMain(false);
        }

        void StartMain(bool firstTime)
        {
            CatView.Coat = S.CatCoat ?? "ginger";
            CatView.Accessory = S.CatAccessory ?? "none";
            pet = new PetWindow();
            LookWindow.Changed += delegate { pet.Cat.ApplyLook(); };
            Friendship.Gained += points => { if (PetVisible) pet.Cat.Float("+♥", Ui.B("#F2708A"), 62, 6, 15); };
            Friendship.LevelUp += level =>
            {
                Sfx.Happy();
                var unlocked = Looks.UnlockedAt(level).Select(i => i.Name).ToList();
                Tell("*purrs loudly* Friendship level " + level + "." +
                     (unlocked.Count > 0 ? " New in my look: " + string.Join(", ", unlocked) + "." : ""), 10);
            };
            pet.Clicked += ShowMenu;
            pet.BreathingFinished += AskIfUrgePassed;
            glow = new AfterglowWindow();
            badge = new BadgeWindow();
            BuildTray();

            hotkey = new GlobalHotkey();
            hotkey.Pressed += delegate { Log(false, TimeSpan.Zero); };
            bool ok = hotkey.Register(S.Hotkey);
            if (!ok) failedHotkey = S.Hotkey;

            Sfx.Migrate();
            Startup.Repair();
            ApplySettings();
            tick.Tick += delegate { Tick(); };
            tick.Start();
            foreground.Tick += delegate { CheckForeground(); };
            foreground.Start();
            SystemEvents.DisplaySettingsChanged += delegate { pet.Dispatcher.BeginInvoke(new Action(Reposition)); };
            // Moving, resizing or auto-hiding the taskbar changes the work area without a display change.
            SystemParameters.StaticPropertyChanged += (s, e) => { if (e.PropertyName == "WorkArea") pet.Dispatcher.BeginInvoke(new Action(Reposition)); };
            SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode != PowerModes.Resume) return;
                pet.Dispatcher.BeginInvoke(new Action(delegate
                {
                    sessionStart = DateTime.Now;
                    onBreak = false;
                    longAway = false;
                    pendingBreakStart = pendingBreakEnd = null;
                }));
            };

            if (!ok) Tell("Hmm, another app already uses " + S.Hotkey + ". Pick a different log key in Settings.", 12);
            else if (firstTime) Tell("I'm down here. Press " + S.Hotkey + " whenever you light one.", 12);
            else if (Updater.JustUpdated) Tell("All set: I'm on version " + AppInfo.Version + " now. *stretches*", 8);
            Updater.Install = InstallUpdate;
            Updater.Found += r => { pendingUpdate = r; ShowUpdateItem(r); };
            Updater.CleanUp();
            Updater.Start();
            if (Store.IsTestMode) StartTestCommands();
        }

        // Test runs only: drive the UI by writing a word into command.txt in the data folder.
        void StartTestCommands()
        {
            string path = System.IO.Path.Combine(Store.Dir, "command.txt");
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += delegate
            {
                if (!System.IO.File.Exists(path)) return;
                string cmd;
                try
                {
                    cmd = System.IO.File.ReadAllText(path).Trim();
                    System.IO.File.Delete(path);
                }
                catch (System.IO.IOException) { return; }   // still being written: next tick
                catch (UnauthorizedAccessException) { return; }
                string[] parts = cmd.Split(':');
                DateTime now = DateTime.Now;
                switch (parts[0])
                {
                    case "log": Log(false, TimeSpan.Zero); break;
                    case "menu": ShowMenu(); break;
                    case "insights": OpenInsights(); break;
                    case "settings": OpenSettings(); break;
                    case "play": StartPlay(parts.Length > 1 ? parts[1] : null, parts.Length > 2 ? parts[2] : null); break;
                    case "breathe": pet.BreatheWithMe(); break;
                    case "retro": AskRetro(); break;
                    case "later": ScheduleLaterCheckin(Store.Data.Smokes[Store.Data.Smokes.Count - 1].Id); laterCheckin.Interval = TimeSpan.FromSeconds(1); break;
                    case "missed": askedThisGap = false; AskSlip(false, now.AddHours(-1), now); break;
                    case "back": askedThisGap = false; AskSlip(true, now.AddMinutes(-12), now); break;
                    case "sound": Sfx.Trill(); break;
                    case "voice": SoundPickerWindow.Show(); break;
                    case "look": LookWindow.Show(); break;
                    case "stretch": sittingSince = now.AddHours(-2); nextBreakNudge = DateTime.MinValue; MaybeSuggestBreak(now); break;
                    case "wave": RideTheWave(); break;
                    case "friend": Friendship.Add(parts.Length > 1 ? int.Parse(parts[1]) : 5); break;
                    case "about": AboutWindow.Show(); break;
                    case "update-check": Updater.Check(null); break;
                    case "frames":
                        System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Dir, "drawing.txt"), PlayWindow.Drawing);
                        System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Dir, "frames.txt"), PlayWindow.Frames.ToString(CultureInfo.InvariantCulture));
                        break;
                    case "update-now": if (Updater.Available != null) InstallUpdate(Updater.Available); break;
                    case "snap": Snapshots.All(cmd.Length > 5 ? cmd.Substring(5) : System.IO.Path.Combine(Store.Dir, "snaps")); break;
                    case "pick": Sfx.SetPicked(parts[1], parts[2], parts[3] == "on"); break;
                    case "soundinfo":
                        System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Dir, "soundinfo.txt"), Sfx.Describe());
                        Sfx.PurrOn(0.38f);
                        Sfx.Mew();
                        var stopPurr = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                        stopPurr.Tick += delegate { stopPurr.Stop(); Sfx.PurrOff(); };
                        stopPurr.Start();
                        break;
                    case "quit": Quit(); break;
                }
            };
            t.Start();
        }

        // ---------- settings and overlays ----------

        void ApplySettings()
        {
            Audio.Master = S.Volume / 100f;
            Sfx.Reload();
            if (!S.CatSounds) Sfx.PurrOff();

            bool napping = DateTime.Now < napUntil;
            if (S.ShowPet && !napping && !playing)
            {
                if (!pet.IsVisible)
                {
                    pet.PlaceHome(S.PetX, S.PetY);
                    pet.Show();
                }
            }
            else if (pet.IsVisible)
            {
                pet.Hide();
            }

            glow.Place(S.AfterglowEdge);
            if (!S.Afterglow) glow.Hide();
            if (!S.Badge) badge.Hide();
            RefreshOverlays(false);
            if (S.Badge) badge.Place(S.BadgeCorner);

            if (hotkey.Current != S.Hotkey && !hotkey.Register(S.Hotkey))
            {
                if (hotkey.Current != null)
                {
                    // Keep the key that works, and say so.
                    Tell("Couldn't use " + S.Hotkey + ", another app has it. Still using " + hotkey.Current + ".", 10);
                    S.Hotkey = hotkey.Current;
                    Store.Save();
                }
                else if (S.Hotkey != failedHotkey)
                {
                    Tell("Couldn't use " + S.Hotkey + ", another app has it. Pick a different log key in Settings.", 12);
                }
                failedHotkey = S.Hotkey;
            }

            petItem.Checked = S.ShowPet;
            glowItem.Checked = S.Afterglow;
            badgeItem.Checked = S.Badge;
            petItem.Text = "Show " + S.PetName;
            UpdateTrayText();
        }

        void Reposition()
        {
            if (pet.IsVisible) pet.PlaceHome(S.PetX, S.PetY);
            glow.Place(S.AfterglowEdge);
            badge.Place(S.BadgeCorner);
        }

        void RefreshOverlays(bool flare)
        {
            var times = Store.Times();
            DateTime now = DateTime.Now;
            DateTime? last = Stats.Last(times, now);
            if (S.Afterglow) glow.Refresh(last, now, flare);
            if (S.Badge) badge.Refresh(last, Stats.Today(times, now), now);
        }

        void Tell(string text, double seconds)
        {
            if (PetVisible) pet.Say(text, seconds);
            else if (tray != null) tray.ShowBalloonTip(5000, S.PetName, text.Replace("*", ""), Forms.ToolTipIcon.None);
        }

        // ---------- logging ----------

        void Log(bool retro, TimeSpan ago)
        {
            DateTime now = DateTime.Now;
            DateTime at = now - ago;
            string app = null;
            bool full = false;
            if (!retro) Native.Foreground(out app, out full);
            if (!S.RememberApps) app = null;
            bool exclusive = Native.ExclusiveFullscreen();

            var ev = new SmokeEvent { At = at, App = app, Fullscreen = full || exclusive, Retro = retro };
            Store.AddSmoke(ev);
            var times = Store.Times();

            var f = new LogFacts
            {
                Now = now,
                Today = Stats.Today(times, now),
                Gap = Stats.GapEndingAt(times, at),
                Record = Stats.IsRecordToday(times, at),
                YesterdayByNow = Stats.YesterdayByNow(times, now),
                FirstEver = times.Count == 1,
                Retro = retro,
                RetroAgo = ago
            };
            string dayKey = Stats.DayStart(now).ToString("yyyy-MM-dd");
            if (!retro && Store.Data.LastMorningHello != dayKey)
            {
                Store.Data.LastMorningHello = dayKey;
                f.MorningHello = true;
                DateTime y = Stats.DayStart(now).AddDays(-1);
                f.YesterdayTotal = Stats.CountOnDay(times, y);
                f.YesterdayLongest = Stats.LongestGap(times, y, y.AddDays(1));
            }
            if (!retro)
            {
                Drift drift = Stats.DriftCheck(times, now);
                DateTime lastNotice = Store.ParseTime(Store.Data.LastDriftNotice);
                if (drift != null && now - lastNotice > TimeSpan.FromDays(3))
                {
                    f.Drift = drift;
                    Store.Data.LastDriftNotice = Store.FormatTime(now);
                }
            }

            bool shortGap = f.Gap != null && !f.Gap.Overnight && f.Gap.Length < Stats.ShortGap;
            Question question = null;
            bool askLater = false;
            if (!retro && !f.FirstEver && S.Checkins != "never" && !RecentlyAsked(now))
            {
                double p = S.Checkins == "often" ? (shortGap ? 0.9 : 0.6) : (shortGap ? 0.66 : 0.33);
                if (Lines.Chance(p))
                {
                    if (shortGap || Lines.Chance(0.5)) question = Lines.AtLog(shortGap);
                    else askLater = true;
                }
            }
            if (question != null) Store.Data.LastCheckin = Store.FormatTime(now);
            Store.Save();

            string spoken;
            string text = Lines.ForLog(f, out spoken);
            if (f.MorningHello) text = AddOneTimeNotes(text, times, now);
            string id = ev.Id;
            CancelLaterCheckin();

            if (PetVisible && !exclusive)
            {
                if (shortGap) pet.Cat.Surprise();
                else pet.Cat.Hop(8);
                if (question != null)
                {
                    Question q = question;
                    pet.Ask(text + "\n\n" + q.Text, q.Labels, i => Answer(id, q.Key, q.Values[i]), 25,
                            "changed my mind", () => ChangedMind(id));
                }
                else
                {
                    pet.Ask(text, null, null, f.Drift != null ? 20 : 12, retro ? null : "changed my mind", () => ChangedMind(id));
                }
            }
            else if (!exclusive)
            {
                tray.ShowBalloonTip(5000, S.PetName, text.Replace("*", ""), Forms.ToolTipIcon.None);
            }

            if (S.Voice == "always" || (S.Voice == "auto" && (exclusive || !PetVisible))) Sound.Say(spoken);
            else if (shortGap) Sfx.Question();
            else Sfx.Trill();

            if (askLater) ScheduleLaterCheckin(id);
            askedThisGap = false;
            pendingBreakStart = pendingBreakEnd = null;
            RefreshViews();
            RefreshOverlays(true);
            UpdateTrayText();
        }

        // A weekly recap on the first log of a new week, and a one-week milestone, each shown once.
        static string AddOneTimeNotes(string text, List<DateTime> times, DateTime now)
        {
            string weekKey = "week:" + Stats.WeekStart(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!Store.Data.Shown.Contains(weekKey))
            {
                string note = Lines.WeeklyNote(times, now);
                if (note != null)
                {
                    Store.Data.Shown.Add(weekKey);
                    text += "\n\n" + note;
                }
            }
            if (!Store.Data.Shown.Contains("first-week"))
            {
                string note = Lines.FirstWeekNote(times, now);
                if (note != null)
                {
                    Store.Data.Shown.Add("first-week");
                    text += "\n\n" + note;
                }
            }
            Store.Save();
            return text;
        }

        static bool RecentlyAsked(DateTime now)
        {
            return now - Store.ParseTime(Store.Data.LastCheckin) < TimeSpan.FromMinutes(30);
        }

        void Answer(string id, string key, string value)
        {
            var ev = Store.FindSmoke(id);
            if (ev == null) return;
            switch (key)
            {
                case "decided": ev.Decided = value; break;
                case "want": ev.Want = value; break;
                case "taste": ev.Taste = value; break;
                case "moment": ev.Moment = value; break;
            }
            Store.Save();
            RefreshViews();
            Friendship.Add(1);
            if (PetVisible) pet.Say(Lines.Reply(key, value), 3);
        }

        void ChangedMind(string id)
        {
            Store.RemoveSmoke(id);
            Store.Data.ChangedMind.Add(Store.FormatTime(DateTime.Now));
            Store.Save();
            CancelLaterCheckin();
            RefreshViews();
            if (PetVisible) pet.Say(Lines.ChangedMind(), 3);
            RefreshOverlays(false);
            UpdateTrayText();
        }

        void ScheduleLaterCheckin(string id)
        {
            laterCheckin = new DispatcherTimer { Interval = TimeSpan.FromMinutes(4) };
            laterCheckin.Tick += delegate
            {
                CancelLaterCheckin();
                var ev = Store.FindSmoke(id);
                var latest = Store.Data.Smokes.Count > 0 ? Store.Data.Smokes[Store.Data.Smokes.Count - 1] : null;
                if (ev == null || latest == null || latest.Id != id) return;
                if (!PetVisible || pet.BubbleOpen || Native.ExclusiveFullscreen()) return;
                Question q = Lines.Later();
                Store.Data.LastCheckin = Store.FormatTime(DateTime.Now);
                Store.Save();
                pet.Ask(q.Text, q.Labels, i => Answer(id, q.Key, q.Values[i]), 30, null, null);
            };
            laterCheckin.Start();
        }

        void CancelLaterCheckin()
        {
            if (laterCheckin != null) laterCheckin.Stop();
            laterCheckin = null;
        }

        void AskRideOut()
        {
            pet.Ask("Let's let it pass.", new[] { "Breathe with me · 1 min", "Ride the wave · 3 min" }, i =>
            {
                if (i == 0) pet.BreatheWithMe();
                else RideTheWave();
            }, 15, null, null);
        }

        void RideTheWave()
        {
            WaveWindow.Show(delegate { UrgePassed(); Friendship.Add(3); }, delegate { Friendship.Add(3); Log(false, TimeSpan.Zero); });
        }

        void UrgePassed()
        {
            Store.Data.RodeOut.Add(Store.FormatTime(DateTime.Now));
            Store.Save();
            RefreshViews();
            Sfx.Happy();
            if (PetVisible) pet.Say(Lines.Pick("Okay. *stretches*", "Nice and quiet then. *purrs*", "Cool. *settles in*"), 3);
        }

        // After a full "breathe with me": did the urge pass? Either answer is fine; "it passed"
        // is counted so the user can see it in "How it's going".
        void AskIfUrgePassed()
        {
            Friendship.Add(3);
            if (!PetVisible) return;
            pet.Ask("Still want one, or did it pass?", new[] { "It passed", "I'll have one" }, i =>
            {
                if (i == 0) UrgePassed();
                else Log(false, TimeSpan.Zero);
            }, 25, null, null);
        }

        void AskRetro()
        {
            DateTime now = DateTime.Now;
            AskWhen(new[] { "Just now", "~10 min ago", "~30 min ago", "~1 hour ago" },
                    new[] { now, now.AddMinutes(-10), now.AddMinutes(-30), now.AddMinutes(-60) });
        }

        void AskWhen(string[] labels, DateTime[] times)
        {
            pet.Ask("When was it?", labels, i => Log(true, DateTime.Now - times[i]), 20, null, null);
        }

        // ---------- "did one slip past me?" ----------

        // Back from a short break, already past your usual gap, and nothing logged: probably a cigarette.
        void CheckSlipAfterBreak(DateTime start, DateTime end)
        {
            if (askedThisGap) return;
            var times = Store.Times();
            DateTime? last = Stats.Last(times, end);
            if (last == null || last.Value >= start) return;
            if (end - start > TimeSpan.FromHours(3) || Stats.DayStart(start) != Stats.DayStart(end))
            {
                sessionStart = end;
                return;
            }
            if (end - last.Value < Stats.UsualGap(times, end)) return;
            if (!AskSlip(true, start, end))
            {
                // The cat was busy (talking, hidden, a fullscreen game): ask on a later tick.
                pendingBreakStart = start;
                pendingBreakEnd = end;
            }
        }

        // At the PC the whole time, and this gap is clearly longer than your own usual ones.
        void CheckSlipLongGap(DateTime now)
        {
            if (askedThisGap) return;
            var times = Store.Times();
            DateTime? last = Stats.Last(times, now);
            if (last == null) return;
            DateTime from = last.Value > sessionStart ? last.Value : sessionStart;
            if (now - from < Stats.SlipThreshold(times, now)) return;
            AskSlip(false, from, now);
        }

        // A long stretch at the PC without a break (4+ min away resets it): the cat suggests getting
        // up. Asked once per stretch; "not now" asks again after another full interval.
        DateTime sittingSince = DateTime.Now, nextBreakNudge = DateTime.MinValue;

        void MaybeSuggestBreak(DateTime now)
        {
            if (S.BreakMinutes <= 0) return;
            if (onBreak || now < nextBreakNudge) return;
            if (now - sittingSince < TimeSpan.FromMinutes(S.BreakMinutes)) return;
            if (!PetVisible || pet.BubbleOpen || playing || Native.ExclusiveFullscreen()) return;
            nextBreakNudge = now.AddMinutes(S.BreakMinutes);
            string time = Stats.Duration(now - sittingSince);
            Sfx.Trill();
            pet.Ask(Lines.Pick(
                    "You've been at it for " + time + ". Want to stand up for a bit? Stretch, a glass of water, some fresh air. *stretches*",
                    time + " in that chair. Your legs might like a little walk. I'll keep it warm for you.",
                    "Break time? A few steps, a window, a stretch. Tired eyes feel better after, and the urges usually do too."),
                new[] { "Good idea", "Not now" }, i =>
                {
                    if (i == 0)
                    {
                        Friendship.Add(2);
                        sittingSince = DateTime.Now;
                        pet.Say(Lines.Pick("Go on, I'll be right here. *curls up*", "Enjoy it. *yawns and stretches with you*"), 4);
                    }
                    else pet.Say("Okay. I'll ask again later.", 3);
                }, 40, null, null);
        }

        // Returns false if the question couldn't be shown right now.
        bool AskSlip(bool away, DateTime start, DateTime end)
        {
            if (!PetVisible || pet.BubbleOpen || playing || Native.ExclusiveFullscreen()) return false;
            askedThisGap = true;
            pendingBreakStart = pendingBreakEnd = null;
            DateTime now = DateTime.Now;
            DateTime? last = Stats.Last(Store.Times(), now);
            string since = last != null && last.Value >= sessionStart ? Stats.Duration(now - last.Value) : null;
            string text = away
                ? Lines.Pick("Welcome back. Did you have one while you were away?", "Oh, you're back. Have one while you were gone?")
                : since != null
                    ? Lines.Pick("It's been " + since + ". Did one slip past me?", "Been " + since + ". Did you light one and forget to tell me?")
                    : "Did one slip past me?";
            Sfx.Question();
            pet.Ask(text, new[] { "Nope", "Yeah, I did" }, i =>
            {
                if (i == 0)
                    pet.Say(since != null && !away ? "Okay. " + since + " and counting." : Lines.Pick("Okay. Just checking.", "Cool. *goes back to loafing*"), 3);
                else if (away)
                    AskWhen(new[] { "While I was away", "Just now", "~10 min ago" },
                            new[] { start + TimeSpan.FromTicks((end - start).Ticks / 2), DateTime.Now, DateTime.Now.AddMinutes(-10) });
                else
                    AskRetro();
            }, 45, null, null);
            return true;
        }

        // ---------- the cat's menu ----------

        void ShowMenu()
        {
            var labels = new[] { "Log one now", "Let's play", "Ride it out", "One I didn't log", "How's it going?", S.PetName + "'s look", "Nap for an hour", "Settings" };
            Sfx.Happy();
            // Clicking the cat is the quickest way to see the time since the last one.
            var times = Store.Times();
            DateTime now = DateTime.Now;
            DateTime? last = Stats.Last(times, now);
            string head = last == null
                ? Lines.Pick("Hey.", "mrrp?", "Yes?", "Hi.")
                : "Last one " + Stats.Ago(now - last.Value) + "  ·  #" + Stats.Today(times, now) + " today";
            pet.Ask(head, labels, i =>
            {
                switch (i)
                {
                    case 0: Log(false, TimeSpan.Zero); break;
                    case 1: StartPlay(null, null); break;
                    case 2: AskRideOut(); break;
                    case 3: AskRetro(); break;
                    case 4: OpenInsights(); break;
                    case 5: LookWindow.Show(); break;
                    case 6: Nap(); break;
                    case 7: OpenSettings(); break;
                }
            }, 12, null, null);
        }

        void StartPlay(string game, string scene)
        {
            if (playing) return;
            playing = true;
            pet.Hide();
            var room = new PlayWindow(game, scene);
            room.Closed += delegate
            {
                if (quitting) return;   // the app is closing every window on its way out
                playing = false;
                ApplySettings();
                if (PetVisible) pet.Say(room.Summary ?? "Maybe next time. *licks paw*", 5);
            };
            room.Show();
            room.Activate();
        }

        void Nap()
        {
            napUntil = DateTime.Now.AddHours(1);
            Sfx.Yawn();
            pet.Say("Okay. Nap time. Back in an hour.", 2.5);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            t.Tick += delegate { t.Stop(); ApplySettings(); };
            t.Start();
        }

        // Brings an already-open window back, even from the taskbar.
        static void Bring(Window w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Show();
            w.Activate();
        }

        void OpenInsights()
        {
            if (insights != null)
            {
                insights.Refresh();
                Bring(insights.Window);
                return;
            }
            insights = new InsightsWindow(delegate { RefreshOverlays(false); UpdateTrayText(); });
            insights.Window.Closed += delegate { insights = null; };
            insights.Window.Show();
            insights.Window.Activate();
        }

        // Anything that shows the history follows every change right away.
        void RefreshViews()
        {
            if (insights != null) insights.Refresh();
        }

        void OpenSettings()
        {
            if (settingsWindow != null) { Bring(settingsWindow); return; }
            settingsWindow = new SettingsWindow(ApplySettings).Window;
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        // ---------- background ----------

        void Tick()
        {
            DateTime now = DateTime.Now;
            RefreshOverlays(false);
            if (!updating) UpdateTrayText();
            MaybeOfferUpdate();
            if (Updater.Available == null && updateItem.Visible) updateItem.Visible = false;   // a release that was taken back

            int idle = Native.IdleMilliseconds();
            if (idle >= 4 * 60000)
            {
                if (!onBreak)
                {
                    onBreak = true;
                    breakStart = now.AddMilliseconds(-idle);
                }
                if (idle >= 30 * 60000) longAway = true;
            }
            else if (onBreak && idle < 60000)
            {
                onBreak = false;
                sittingSince = now;
                if (longAway && now - breakStart > TimeSpan.FromHours(3))
                {
                    sessionStart = now;
                    pendingBreakStart = pendingBreakEnd = null;
                }
                else CheckSlipAfterBreak(breakStart, now);
                longAway = false;
            }
            if (!onBreak && idle < 60000) MaybeSuggestBreak(now);

            // A "welcome back" that couldn't be asked goes stale after half an hour or a new day.
            if (pendingBreakEnd != null && (now - pendingBreakEnd.Value > TimeSpan.FromMinutes(30) || Stats.DayStart(now) != Stats.DayStart(pendingBreakEnd.Value)))
                pendingBreakStart = pendingBreakEnd = null;
            if (!onBreak && idle < 60000)
            {
                if (pendingBreakStart != null && !askedThisGap) AskSlip(true, pendingBreakStart.Value, pendingBreakEnd.Value);
                else CheckSlipLongGap(now);
            }

            if (napUntil != DateTime.MinValue && now >= napUntil)
            {
                napUntil = DateTime.MinValue;
                ApplySettings();
                if (PetVisible)
                {
                    Sfx.Trill();
                    pet.Say("*yawns* I'm back.", 3);
                }
            }
        }

        void CheckForeground()
        {
            string app;
            bool full;
            Native.Foreground(out app, out full);
            pet.Playing = full && app != null && app != Native.Desktop;
        }

        // ---------- tray ----------

        void BuildTray()
        {
            tray = new Forms.NotifyIcon { Text = "Breathing Room", Visible = true };
            try
            {
                using (var big = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location))
                    tray.Icon = new System.Drawing.Icon(big, Forms.SystemInformation.SmallIconSize);
            }
            catch
            {
                tray.Icon = System.Drawing.SystemIcons.Application;
            }

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Log one now", null, delegate { Log(false, TimeSpan.Zero); });
            var retro = new Forms.ToolStripMenuItem("One I didn't log");
            var labels = new[] { "Just now", "About 10 min ago", "About 30 min ago", "About an hour ago" };
            var minutes = new[] { 0, 10, 30, 60 };
            for (int i = 0; i < labels.Length; i++)
            {
                int m = minutes[i];
                retro.DropDownItems.Add(labels[i], null, delegate { Log(true, TimeSpan.FromMinutes(m)); });
            }
            menu.Items.Add(retro);
            menu.Items.Add("How's it going", null, delegate { OpenInsights(); });
            menu.Items.Add("Let's play", null, delegate { StartPlay(null, null); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            petItem = new Forms.ToolStripMenuItem("Show the cat");
            petItem.Click += delegate { S.ShowPet = !S.ShowPet; napUntil = DateTime.MinValue; Store.Save(); ApplySettings(); };
            glowItem = new Forms.ToolStripMenuItem("Afterglow on the screen edge");
            glowItem.Click += delegate { S.Afterglow = !S.Afterglow; Store.Save(); ApplySettings(); };
            badgeItem = new Forms.ToolStripMenuItem("Corner badge");
            badgeItem.Click += delegate { S.Badge = !S.Badge; Store.Save(); ApplySettings(); };
            menu.Items.Add(petItem);
            menu.Items.Add(glowItem);
            menu.Items.Add(badgeItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Ride the wave", null, delegate { RideTheWave(); });
            menu.Items.Add(S.PetName + "'s look…", null, delegate { LookWindow.Show(); });
            menu.Items.Add(S.PetName + "'s voice…", null, delegate { SoundPickerWindow.Show(); });
            updateItem = new Forms.ToolStripMenuItem("Update") { Visible = false };
            updateItem.Click += delegate { if (Updater.Available != null) InstallUpdate(Updater.Available); };
            menu.Items.Add(updateItem);
            menu.Items.Add("About", null, delegate { AboutWindow.Show(); });
            menu.Items.Add("Settings…", null, delegate { OpenSettings(); });
            menu.Items.Add("Quit", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenInsights(); };
        }

        void UpdateTrayText()
        {
            var times = Store.Times();
            DateTime now = DateTime.Now;
            DateTime? last = Stats.Last(times, now);
            string text = last == null
                ? "Breathing Room"
                : "Last one " + Stats.Ago(now - last.Value) + " · #" + Stats.Today(times, now) + " today";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        // ---------- updates ----------

        void ShowUpdateItem(Updater.Release r)
        {
            updateItem.Text = "Update to version " + r.Version + "…";
            updateItem.Visible = true;
        }

        // Mentioned once per version, and only while the user is around and not in a fullscreen
        // game; the tray and About keep offering it after that.
        void MaybeOfferUpdate()
        {
            Updater.Release r = pendingUpdate;
            if (r == null || updating) return;
            if (Updater.Available == null)
            {
                pendingUpdate = null;   // taken back since
                return;
            }
            if (S.QuietVersion == r.Version.ToString())
            {
                pendingUpdate = null;
                return;
            }
            if (!PetVisible || playing || pet.Playing || pet.BubbleOpen || Native.IdleMilliseconds() > 30000) return;
            pendingUpdate = null;
            S.QuietVersion = r.Version.ToString();
            Store.Save();
            Sfx.Trill();
            pet.Ask("There's a new version of me (" + r.Version + "). Want it? Takes a few seconds.", new[] { "Update now", "What's new", "Not now" }, i =>
            {
                if (i == 0) InstallUpdate(r);
                else if (i == 1) Updater.OpenPage(r);
            }, 40, null, null);
        }

        void InstallUpdate(Updater.Release r)
        {
            if (updating) return;
            updating = true;
            pendingUpdate = null;
            if (PetVisible) pet.Say("Fetching the new version… *carries it in by the scruff*", 60);
            Updater.Download(r, p => { tray.Text = "Downloading the update… " + (int)Math.Round(p * 100) + "%"; }, error =>
            {
                updating = false;
                if (error == null)
                {
                    try
                    {
                        Updater.Restart();
                        Quit();
                    }
                    catch (Exception ex)
                    {
                        Store.Log("Restart after update failed: " + ex);
                        UpdateTrayText();
                        Tell("The new version is in place. It starts the next time I do.", 10);
                    }
                    return;
                }
                UpdateTrayText();
                Updater.ReportFailure(error);
                Tell("The update didn't work (" + error + "). Nothing changed. The new version is also on the project page.", 12);
            });
        }

        bool quitting;

        void Quit()
        {
            quitting = true;
            tray.Visible = false;
            tray.Dispose();
            hotkey.Dispose();
            Application.Current.Shutdown();
        }
    }
}

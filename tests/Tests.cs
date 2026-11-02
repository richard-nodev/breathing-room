using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BreathingRoom;

// Logic tests. Built and run by build.cmd before every build; a failure stops the build.
static class Tests
{
    static int failures;
    static int passes;

    static void Check(bool ok, string name)
    {
        if (ok) passes++;
        else { failures++; Console.WriteLine("FAIL: " + name); }
    }

    static void Equal<T>(T expected, T actual, string name)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        if (!ok) Console.WriteLine("  expected " + expected + ", got " + actual);
        Check(ok, name);
    }

    static DateTime T(int day, int hour, int minute)
    {
        return new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Local);
    }

    static int Main()
    {
        // Day boundary at 05:00
        Equal(T(1, 5, 0).AddDays(-1), Stats.DayStart(T(1, 4, 59)), "04:59 belongs to the previous day");
        Equal(T(1, 5, 0), Stats.DayStart(T(1, 5, 0)), "05:00 starts a new day");
        Equal(T(1, 5, 0), Stats.DayStart(T(1, 23, 30)), "23:30 is the same day");

        // Overnight gaps
        Check(Stats.IsOvernight(T(1, 23, 30), T(2, 6, 10)), "night sleep is overnight");
        Check(Stats.IsOvernight(T(2, 4, 30), T(2, 5, 10)), "crossing 05:00 is overnight even if short");
        Check(Stats.IsOvernight(T(2, 12, 0), T(2, 19, 0)), "7h gap is overnight");
        Check(!Stats.IsOvernight(T(2, 12, 0), T(2, 13, 0)), "1h daytime gap is not overnight");

        // Counts
        var times = new List<DateTime> { T(1, 9, 0), T(1, 10, 0), T(1, 22, 0), T(2, 2, 0), T(2, 9, 30), T(2, 10, 0) };
        Equal(2, Stats.Today(times, T(2, 11, 0)), "today count (2nd, after 05:00)");
        Equal(2, Stats.YesterdayByNow(times, T(2, 11, 0)), "yesterday by 11:00 = 09:00 and 10:00");
        Equal(4, Stats.CountOnDay(times, T(1, 5, 0)), "day 1 includes 02:00 next morning");
        Equal(T(2, 10, 0), Stats.Last(times, T(2, 11, 0)).Value, "last smoke");

        // Gap ending at a time
        var g = Stats.GapEndingAt(times, T(2, 10, 0));
        Equal(TimeSpan.FromMinutes(30), g.Length, "gap ending at 10:00");
        Check(!g.Overnight, "30 min gap not overnight");
        Check(Stats.GapEndingAt(times, T(1, 9, 0)) == null, "first ever smoke has no gap");
        Check(Stats.GapEndingAt(times, T(2, 9, 30)).Overnight, "first of the day is overnight");

        // Records
        var rec = new List<DateTime> { T(3, 10, 0), T(3, 10, 30), T(3, 11, 0), T(3, 12, 0), T(3, 12, 20) };
        Check(Stats.IsRecordToday(rec, T(3, 12, 0)), "60 min beats earlier 30s");
        Check(!Stats.IsRecordToday(rec, T(3, 11, 0)), "30 min is too short to be a record");
        Check(!Stats.IsRecordToday(rec, T(3, 12, 20)), "20 min is not a record");
        var firstGap = new List<DateTime> { T(3, 10, 0), T(3, 11, 0) };
        Check(!Stats.IsRecordToday(firstGap, T(3, 11, 0)), "the first gap of the day has nothing to beat");

        // Averages ignore overnight gaps
        var avg = new List<DateTime> { T(4, 22, 0), T(4, 22, 30), T(5, 8, 0), T(5, 9, 0) };
        Equal(TimeSpan.FromMinutes(45), Stats.AverageGap(avg, T(4, 5, 0), T(6, 5, 0)).Value, "average of 30 and 60 min");
        Equal(TimeSpan.FromMinutes(60), Stats.LongestGap(avg, T(4, 5, 0), T(6, 5, 0)).Length, "longest skips overnight");

        // Drift: 7 days at 19, then 3 days at 24
        var drift = new List<DateTime>();
        DateTime start = T(1, 5, 0);
        for (int d = 0; d < 11; d++)
        {
            int perDay = d >= 8 ? 24 : 19;
            if (d == 0) perDay = 5;
            for (int i = 0; i < perDay; i++) drift.Add(start.AddDays(d).AddHours(3).AddMinutes(i * 35));
        }
        DateTime driftNow = start.AddDays(11).AddHours(5);
        var dr = Stats.DriftCheck(drift, driftNow);
        Check(dr != null, "drift detected");
        if (dr != null)
        {
            Equal(24.0, dr.Recent, "drift recent avg");
            Equal(19.0, dr.Before, "drift before avg");
        }
        Check(Stats.DriftCheck(drift, start.AddDays(5)) == null, "no drift check with under 11 days of history");
        var steady = drift.Where(t => Stats.DayStart(t) < start.AddDays(8)).ToList();
        for (int d = 8; d < 11; d++)
            for (int i = 0; i < 19; i++) steady.Add(start.AddDays(d).AddHours(3).AddMinutes(i * 35));
        Check(Stats.DriftCheck(steady, driftNow) == null, "steady pace is not drift");

        // First week average skips the partial first day
        Equal(19.0, Stats.FirstWeekAverage(drift, driftNow).Value, "first week average");
        Check(Stats.FirstWeekAverage(drift, start.AddDays(3)) == null, "no first-week average yet");
        Equal(0.0, Stats.MoneyNotSpent(drift, driftNow, 10, 20), "smoking more saves nothing");
        var fewer = drift.Where(t => Stats.DayStart(t) < start.AddDays(8)).ToList();
        for (int d = 8; d < 11; d++)
            for (int i = 0; i < 15; i++) fewer.Add(start.AddDays(d).AddHours(3).AddMinutes(i * 40));
        Equal(6.0, Stats.MoneyNotSpent(fewer, driftNow, 10, 20), "4 fewer a day for 3 days at 0.5 each");

        // Usual gap and the "did one slip past me?" threshold follow the user's own rhythm
        var rhythm = new List<DateTime>();
        for (int i = 0; i < 20; i++) rhythm.Add(T(6, 8, 0).AddMinutes(i * 30));
        rhythm.Add(T(6, 8, 0).AddMinutes(19 * 30 + 50));
        Equal(TimeSpan.FromMinutes(30), Stats.UsualGap(rhythm, T(6, 22, 0)), "usual gap is the median");
        Equal(TimeSpan.FromMinutes(37.5), Stats.SlipThreshold(rhythm, T(6, 22, 0)), "slip threshold is 1.25 x the 80th percentile");
        Equal(TimeSpan.FromMinutes(75), Stats.SlipThreshold(new List<DateTime> { T(6, 8, 0), T(6, 9, 0) }, T(6, 10, 0)), "default threshold with little data");
        var tight = new List<DateTime>();
        for (int i = 0; i < 20; i++) tight.Add(T(6, 8, 0).AddMinutes(i * 10));
        Equal(TimeSpan.FromMinutes(30), Stats.SlipThreshold(tight, T(6, 12, 0)), "threshold never below 30 min");

        // Old data files with removed settings still load
        string oldDir = Path.Combine(Path.GetTempPath(), "br-old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "data.json"), "{\"Welcomed\":true,\"Smokes\":[],\"Settings\":{\"PetName\":\"Bean\",\"Sounds\":true}}");
        var old = Store.LoadFrom(Path.Combine(oldDir, "data.json"));
        Equal("Bean", old.Settings.PetName, "old file: settings kept");
        Check(old.Settings.CatSounds && old.Settings.Volume == 70, "old file: new sound settings get defaults");
        Check(old.Best != null, "old file: best scores initialized");
        Directory.Delete(oldDir, true);

        // Duration text
        Equal("under a minute", Stats.Duration(TimeSpan.FromSeconds(20)), "duration seconds");
        Equal("12 min", Stats.Duration(TimeSpan.FromMinutes(12.7)), "duration minutes");
        Equal("1h", Stats.Duration(TimeSpan.FromMinutes(60)), "duration hour");
        Equal("1h 22m", Stats.Duration(TimeSpan.FromMinutes(82)), "duration hours and minutes");
        Equal("9m", Stats.Short(TimeSpan.FromMinutes(9.5)), "short minutes");
        Equal("2h 05m", Stats.Short(TimeSpan.FromMinutes(125)), "short hours");

        // Insights
        var data = new AppData();
        DateTime now = T(10, 20, 0);
        for (int i = 0; i < 12; i++)
        {
            var ev = new SmokeEvent { Id = "s" + i, At = T(10, 8, 0).AddMinutes(i * 50), App = i < 8 ? "valorant" : "chrome" };
            if (i < 6) ev.Decided = i < 4 ? "hand" : "decided";
            if (i < 5) ev.Taste = i < 3 ? "meh" : "good";
            if (i < 4) ev.Moment = i < 3 ? "gaming" : "bored";
            data.Smokes.Add(ev);
        }
        data.ChangedMind.Add(Store.FormatTime(T(10, 12, 0)));
        var lines = Insights.Build(data, now);
        Check(lines.Any(l => l.Contains("your hand lit it before you decided 4 times")), "insight: hand vs decided");
        Check(lines.Any(l => l.Contains("3 out of 5")), "insight: taste");
        Check(lines.Any(l => l.Contains("gaming (3 of 4)")), "insight: moment");
        Check(lines.Any(l => l.Contains("while Valorant was in front")), "insight: top app, with a proper capital");
        Check(lines.Any(l => l.Contains("changed your mind 1 time ")), "insight: changed mind");
        Check(lines.Any(l => l.StartsWith("Average gap between cigarettes so far: 50 min")), "insight: average gap");
        Check(Insights.Build(new AppData(), now).Single().StartsWith("I'll have more"), "empty insights");

        // Store round trip keeps local times and answers
        string dir = Path.Combine(Path.GetTempPath(), "br-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("BR_DATA_DIR", dir);
        Store.Init();
        Store.AddSmoke(new SmokeEvent { At = T(10, 9, 15), App = "game", Taste = "meh" });
        Store.AddSmoke(new SmokeEvent { At = T(10, 8, 15) });
        Store.Data.Settings.PetName = "Bean";
        Store.Save();
        Store.Init();
        Equal(2, Store.Data.Smokes.Count, "round trip count");
        Equal(T(10, 8, 15), Store.Data.Smokes[0].At, "round trip sorted, local time");
        Equal("meh", Store.Data.Smokes[1].Taste, "round trip answer");
        Equal("Bean", Store.Data.Settings.PetName, "round trip settings");
        Equal("top", Store.Data.Settings.AfterglowEdge, "defaults survive");
        Store.Save();
        Check(File.Exists(Path.Combine(dir, "data.json.bak")), "backup written on replace");
        Directory.Delete(dir, true);

        // Weeks start on Monday 05:00 (1 Nov 2026 is a Sunday)
        Equal(T(2, 5, 0), Stats.WeekStart(T(2, 10, 0)), "Monday morning starts the week");
        Equal(T(2, 5, 0).AddDays(-7), Stats.WeekStart(T(1, 22, 0)), "Sunday night is still last week");
        Equal(T(2, 5, 0).AddDays(-7), Stats.WeekStart(T(2, 4, 0)), "Monday 04:00 still belongs to Sunday");

        // Weekly recap and first-week milestone
        var weeks = new List<DateTime>();
        DateTime w0 = T(2, 5, 0).AddDays(-21);   // Monday three weeks back
        for (int d = 0; d < 21; d++)
        {
            int perDay = d < 7 ? 3 : d < 14 ? 20 : 18;
            for (int i = 0; i < perDay; i++) weeks.Add(w0.AddDays(d).AddHours(3).AddMinutes(i * 40));
        }
        string weekly = Lines.WeeklyNote(weeks, T(2, 8, 0));
        Equal("New week. Last week: 126 (the week before: 140).", weekly, "weekly recap text");
        Check(Lines.WeeklyNote(weeks.Where(t => t >= T(2, 5, 0).AddDays(-5)).ToList(), T(2, 8, 0)) == null, "no recap without a full last week");
        Check(Lines.WeeklyNote(weeks.Where(t => t >= T(2, 5, 0).AddDays(-10)).ToList(), T(2, 8, 0)).EndsWith("Last week: 126."), "recap without the week before when that week is incomplete");
        Check(Lines.WeeklyNote(new List<DateTime>(), T(2, 8, 0)) == null, "no recap without data");
        string firstWeek = Lines.FirstWeekNote(weeks, T(2, 8, 0));
        Check(firstWeek != null && firstWeek.Contains("first full week: 5.4 a day, usually 40 min apart"), "first-week milestone text: " + firstWeek);
        Check(Lines.FirstWeekNote(weeks.Where(t => t < w0.AddDays(5)).ToList(), w0.AddDays(5)) == null, "no milestone in the first days");
        Equal(18.0, Stats.LastSevenDaysAverage(weeks, T(2, 8, 0)).Value, "last seven days average");
        Check(Stats.LastSevenDaysAverage(weeks, w0.AddDays(3)) == null, "no last-seven average too early");

        // Log text never throws and always mentions the count
        var factsList = new[]
        {
            new LogFacts { Today = 1, FirstEver = true, Now = T(3, 9, 0) },
            new LogFacts { Today = 1, Now = T(3, 9, 0), MorningHello = true, YesterdayTotal = 18, YesterdayLongest = new Gap { Start = T(2, 9, 0), End = T(2, 10, 40) } },
            new LogFacts { Today = 7, Now = T(3, 15, 0), Gap = new Gap { Start = T(3, 14, 54), End = T(3, 15, 0) } },
            new LogFacts { Today = 8, Now = T(3, 16, 30), Gap = new Gap { Start = T(3, 15, 0), End = T(3, 16, 30) }, Record = true },
            new LogFacts { Today = 9, Now = T(3, 17, 0), Retro = true, RetroAgo = TimeSpan.FromMinutes(30) }
        };
        foreach (var f in factsList)
        {
            string spoken;
            string line = Lines.ForLog(f, out spoken);
            Check(!string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(spoken), "log line for #" + f.Today);
            if (!f.FirstEver) Check(line.Contains("#" + f.Today), "log line names the count: " + line);
        }

        // CSV export
        var csvData = new AppData();
        csvData.Smokes.Add(new SmokeEvent { Id = "a", At = T(3, 4, 30), App = "my, game", Retro = true, Taste = "meh" });
        csvData.Smokes.Add(new SmokeEvent { Id = "b", At = T(3, 9, 15), Decided = "hand" });
        string[] csv = DataTools.Csv(csvData).TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Equal(3, csv.Length, "csv has a header and one row per cigarette");
        Equal("2026-11-03T04:30:00,2026-11-02,04:30,\"my, game\",yes,,,meh,", csv[1], "csv row with an escaped app name and the 05:00 day");
        Equal("2026-11-03T09:15:00,2026-11-03,09:15,,no,hand,,,", csv[2], "csv row with an answer");

        // Urges that passed show up in the insights
        var rode = new AppData();
        rode.RodeOut.Add(Store.FormatTime(T(10, 12, 0)));
        rode.RodeOut.Add(Store.FormatTime(T(10, 18, 0)));
        Check(Insights.Build(rode, T(10, 20, 0)).Any(l => l.Contains("Urges that passed after breathing with Mochi: 2")), "insight: urges that passed");

        // Clearing the history keeps settings and leaves no copy in the backup
        string wipeDir = Path.Combine(Path.GetTempPath(), "br-wipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wipeDir);
        Environment.SetEnvironmentVariable("BR_DATA_DIR", wipeDir);
        Store.Init();
        Store.AddSmoke(new SmokeEvent { At = T(10, 9, 0) });
        Store.AddSmoke(new SmokeEvent { At = T(10, 10, 0) });
        Store.Data.Settings.PetName = "Bean";
        Store.ClearHistory();
        Equal(0, Store.LoadFrom(Path.Combine(wipeDir, "data.json")).Smokes.Count, "history cleared");
        Equal(0, Store.LoadFrom(Path.Combine(wipeDir, "data.json.bak")).Smokes.Count, "backup cleared too");
        Equal("Bean", Store.LoadFrom(Path.Combine(wipeDir, "data.json")).Settings.PetName, "settings kept after clearing");
        Directory.Delete(wipeDir, true);

        // Daylight saving: the repeated hour (1 Nov 2026 in the US) keeps real order and real gaps
        DateTime edt = Store.ParseTime("2026-11-01T01:50:00-04:00");
        DateTime est = Store.ParseTime("2026-11-01T01:10:00-05:00");
        Equal(TimeSpan.FromMinutes(20), est - edt, "gap across the clock change is the real 20 minutes");
        Equal("2026-11-01T01:50:00.0000000-04:00".Substring(0, 19), DateTimeOffset.Parse(Store.FormatTime(edt)).ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-ddTHH:mm:ss"), "time survives a save and load");

        // Times are shown and exported as the wall clock said, even from the other side of a clock change
        var summer = new AppData();
        summer.Smokes.Add(new SmokeEvent { Id = "s", Time = "2026-10-31T10:00:00.0000000-04:00" });
        string expectedWall = DateTimeOffset.Parse("2026-10-31T10:00:00-04:00").LocalDateTime.ToString("HH:mm");
        Check(DataTools.Csv(summer).Contains("," + expectedWall + ","), "csv shows the wall-clock time from summer time");

        // A data file that is locked when the app starts is never overwritten
        string lockDir = Path.Combine(Path.GetTempPath(), "br-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lockDir);
        string lockPath = Path.Combine(lockDir, "data.json");
        File.WriteAllText(lockPath, "{\"Welcomed\":true,\"Smokes\":[{\"Id\":\"x\",\"Time\":\"2026-11-01T10:00:00.0000000-05:00\"}]}");
        Environment.SetEnvironmentVariable("BR_DATA_DIR", lockDir);
        using (new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Store.Init();
            Store.Data.Settings.PetName = "Locked";
            Store.Save();
        }
        Equal(1, Store.LoadFrom(lockPath).Smokes.Count, "locked file left untouched by saves");
        Store.Save();
        Check(Directory.GetFiles(lockDir, "data.json.unreadable-*").Length == 1, "once unlocked it is copied aside before saving");
        Directory.Delete(lockDir, true);

        // An unreadable data file is kept aside, never overwritten
        string badDir = Path.Combine(Path.GetTempPath(), "br-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "data.json"), "{ this is not json");
        var fresh = Store.LoadFrom(Path.Combine(badDir, "data.json"));
        Equal(0, fresh.Smokes.Count, "unreadable file loads as empty");
        Check(Directory.GetFiles(badDir, "data.json.unreadable-*").Length == 1, "unreadable file kept as a copy");
        Directory.Delete(badDir, true);

        Console.WriteLine(passes + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}

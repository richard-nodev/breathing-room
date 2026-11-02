using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BreathingRoom
{
    public class Gap
    {
        public DateTime Start;
        public DateTime End;
        public bool Overnight;
        public TimeSpan Length { get { return End - Start; } }
    }

    public class Drift
    {
        public double Recent;
        public double Before;
    }

    // Pure counting logic. Everything takes "now" so it can be tested without a clock.
    public static class Stats
    {
        public const int DayStartHour = 5;
        public static readonly TimeSpan OvernightLength = TimeSpan.FromHours(6);
        public static readonly TimeSpan ShortGap = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan RecordMinimum = TimeSpan.FromMinutes(45);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Days run 05:00 to 05:00 so late-night gaming belongs to the evening it started in.
        public static DateTime DayStart(DateTime t)
        {
            DateTime d = t.Date.AddHours(DayStartHour);
            return t < d ? d.AddDays(-1) : d;
        }

        public static bool IsOvernight(DateTime a, DateTime b)
        {
            return b - a > OvernightLength || DayStart(a) != DayStart(b);
        }

        public static List<Gap> Gaps(IList<DateTime> sorted)
        {
            var gaps = new List<Gap>();
            for (int i = 1; i < sorted.Count; i++)
                gaps.Add(new Gap { Start = sorted[i - 1], End = sorted[i], Overnight = IsOvernight(sorted[i - 1], sorted[i]) });
            return gaps;
        }

        public static int CountIn(IList<DateTime> times, DateTime from, DateTime to)
        {
            int n = 0;
            foreach (DateTime t in times) if (t >= from && t < to) n++;
            return n;
        }

        public static int Today(IList<DateTime> times, DateTime now)
        {
            DateTime start = DayStart(now);
            return CountIn(times, start, start.AddDays(1));
        }

        public static int YesterdayByNow(IList<DateTime> times, DateTime now)
        {
            DateTime today = DayStart(now);
            DateTime yStart = today.AddDays(-1);
            return CountIn(times, yStart, yStart + (now - today) + TimeSpan.FromSeconds(1));
        }

        public static int CountOnDay(IList<DateTime> times, DateTime dayStart)
        {
            return CountIn(times, dayStart, dayStart.AddDays(1));
        }

        public static DateTime? Last(IList<DateTime> times, DateTime now)
        {
            DateTime? last = null;
            foreach (DateTime t in times)
                if (t <= now && (last == null || t > last.Value)) last = t;
            return last;
        }

        // The gap that ends at t (t must be one of the logged times).
        public static Gap GapEndingAt(IList<DateTime> sorted, DateTime t)
        {
            DateTime? prev = null;
            foreach (DateTime s in sorted)
            {
                if (s >= t) break;
                prev = s;
            }
            if (prev == null) return null;
            return new Gap { Start = prev.Value, End = t, Overnight = IsOvernight(prev.Value, t) };
        }

        // A "longest stretch today" only counts if it is long enough to mean something
        // and beats at least one earlier same-day gap.
        public static bool IsRecordToday(IList<DateTime> sorted, DateTime t)
        {
            Gap g = GapEndingAt(sorted, t);
            if (g == null || g.Overnight || g.Length < RecordMinimum) return false;
            DateTime day = DayStart(t);
            var earlier = Gaps(sorted).Where(x => !x.Overnight && x.End < t && DayStart(x.End) == day).ToList();
            if (earlier.Count == 0) return false;
            return g.Length > earlier.Max(x => x.Length);
        }

        public static TimeSpan? AverageGap(IList<DateTime> sorted, DateTime from, DateTime to)
        {
            var gaps = Gaps(sorted).Where(g => !g.Overnight && g.End >= from && g.End < to).ToList();
            if (gaps.Count == 0) return null;
            return TimeSpan.FromTicks((long)gaps.Average(g => g.Length.Ticks));
        }

        public static Gap LongestGap(IList<DateTime> sorted, DateTime from, DateTime to)
        {
            return Gaps(sorted).Where(g => !g.Overnight && g.End >= from && g.End < to)
                .OrderByDescending(g => g.Length).FirstOrDefault();
        }

        // Oldest first; the last entry is today.
        public static List<KeyValuePair<DateTime, int>> DailyCounts(IList<DateTime> times, DateTime now, int days)
        {
            var list = new List<KeyValuePair<DateTime, int>>();
            DateTime today = DayStart(now);
            for (int i = days - 1; i >= 0; i--)
            {
                DateTime d = today.AddDays(-i);
                list.Add(new KeyValuePair<DateTime, int>(d, CountOnDay(times, d)));
            }
            return list;
        }

        // Weeks start on Monday at 05:00, like days.
        public static DateTime WeekStart(DateTime t)
        {
            DateTime d = DayStart(t);
            return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        }

        public static int CountWeek(IList<DateTime> times, DateTime weekStart)
        {
            return CountIn(times, weekStart, weekStart.AddDays(7));
        }

        // Average per day over the last 7 full days, once there are 7 full days of history.
        public static double? LastSevenDaysAverage(IList<DateTime> times, DateTime now)
        {
            if (times.Count == 0) return null;
            DateTime today = DayStart(now);
            if (DayStart(times.Min()).AddDays(1) > today.AddDays(-7)) return null;
            return AverageOfDays(times, today.AddDays(-7), 7);
        }

        public static double AverageOfDays(IList<DateTime> times, DateTime firstDay, int days)
        {
            int total = 0;
            for (int i = 0; i < days; i++) total += CountOnDay(times, firstDay.AddDays(i));
            return (double)total / days;
        }

        // "Your brain is switching back": last 3 full days clearly above the 7 days before.
        public static Drift DriftCheck(IList<DateTime> times, DateTime now)
        {
            if (times.Count == 0) return null;
            DateTime today = DayStart(now);
            DateTime first = DayStart(times.Min());
            if ((today - first).TotalDays < 11) return null;
            double recent = AverageOfDays(times, today.AddDays(-3), 3);
            double before = AverageOfDays(times, today.AddDays(-10), 7);
            if (recent >= before * 1.15 && recent - before >= 2)
                return new Drift { Recent = recent, Before = before };
            return null;
        }

        // Average of the first 7 full days (the install day is usually partial, so it is skipped).
        public static double? FirstWeekAverage(IList<DateTime> times, DateTime now)
        {
            if (times.Count == 0) return null;
            DateTime first = DayStart(times.Min()).AddDays(1);
            if (DayStart(now) < first.AddDays(7)) return null;
            return AverageOfDays(times, first, 7);
        }

        public static double MoneyNotSpent(IList<DateTime> times, DateTime now, double packPrice, int packSize)
        {
            if (packPrice <= 0 || packSize <= 0) return 0;
            double? baseline = FirstWeekAverage(times, now);
            if (baseline == null) return 0;
            DateTime d = DayStart(times.Min()).AddDays(8);
            DateTime today = DayStart(now);
            double fewer = 0;
            for (; d < today; d = d.AddDays(1))
                fewer += Math.Max(0, baseline.Value - CountOnDay(times, d));
            return fewer * packPrice / packSize;
        }

        static List<TimeSpan> RecentGaps(IList<DateTime> sorted, DateTime now)
        {
            DateTime from = DayStart(now).AddDays(-6);
            return Gaps(sorted).Where(g => !g.Overnight && g.End >= from && g.End <= now)
                .Select(g => g.Length).OrderBy(x => x).ToList();
        }

        // The user's own usual gap over the last week (median). Falls back to 40 min with little data.
        public static TimeSpan UsualGap(IList<DateTime> sorted, DateTime now)
        {
            var gaps = RecentGaps(sorted, now);
            if (gaps.Count < 8) return TimeSpan.FromMinutes(40);
            return gaps[gaps.Count / 2];
        }

        // How long a gap has to run before "did one slip past me?" is worth asking: clearly longer
        // than most of the user's own recent gaps, within sane bounds.
        public static TimeSpan SlipThreshold(IList<DateTime> sorted, DateTime now)
        {
            var gaps = RecentGaps(sorted, now);
            if (gaps.Count < 8) return TimeSpan.FromMinutes(75);
            TimeSpan p80 = gaps[(int)Math.Floor(0.8 * (gaps.Count - 1))];
            return TimeSpan.FromMinutes(Math.Max(30, Math.Min(180, p80.TotalMinutes * 1.25)));
        }

        public static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.FromMinutes(1)) return "under a minute";
            if (t < TimeSpan.FromHours(1)) return ((int)t.TotalMinutes).ToString(Inv) + " min";
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            return m == 0 ? h + "h" : h + "h " + m + "m";
        }

        public static string Ago(TimeSpan t)
        {
            return t < TimeSpan.FromMinutes(1) ? "just now" : Short(t) + " ago";
        }

        public static string Short(TimeSpan t)
        {
            if (t < TimeSpan.FromHours(1)) return Math.Max(0, (int)t.TotalMinutes) + "m";
            return (int)t.TotalHours + "h " + t.Minutes.ToString("00", Inv) + "m";
        }

        public static string Num(double v)
        {
            return Math.Abs(v - Math.Round(v)) < 0.05 ? Math.Round(v).ToString(Inv) : v.ToString("0.#", Inv);
        }
    }

    // Plain-sentence observations for the "How's it going" window. Facts only, no advice.
    public static class Insights
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // "claude" -> "Claude" for program names saved before friendly app names existed.
        static string AppLabel(string name)
        {
            return name == name.ToLowerInvariant() ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        }

        static string MomentLabel(string m)
        {
            switch (m)
            {
                case "gaming": return "gaming";
                case "working": return "working";
                case "bored": return "waiting / bored";
                case "stressed": return "stressed";
                default: return m;
            }
        }

        public static List<string> Build(AppData data, DateTime now)
        {
            var lines = new List<string>();
            var smokes = data.Smokes.OrderBy(s => s.At).ToList();
            var times = smokes.Select(s => s.At).ToList();
            DateTime today = Stats.DayStart(now);
            DateTime end = now.AddSeconds(1);
            DateTime weekStart = today.AddDays(-6);
            DateTime prevWeekStart = today.AddDays(-13);

            TimeSpan? thisWeek = Stats.AverageGap(times, weekStart, end);
            TimeSpan? lastWeek = Stats.AverageGap(times, prevWeekStart, weekStart);
            if (thisWeek != null)
            {
                if (lastWeek != null)
                    lines.Add("Average gap this week: " + Stats.Duration(thisWeek.Value) + ". The week before: " + Stats.Duration(lastWeek.Value) + ".");
                else
                    lines.Add("Average gap between cigarettes so far: " + Stats.Duration(thisWeek.Value) + ".");
            }

            Gap longest = Stats.LongestGap(times, weekStart, end);
            if (longest != null && longest.Length >= TimeSpan.FromMinutes(30))
                lines.Add("Longest stretch this week: " + Stats.Duration(longest.Length) + " (" + longest.End.ToString("dddd", Inv) + ").");

            var recent = smokes.Where(s => s.At >= prevWeekStart).ToList();

            int decided = recent.Count(s => s.Decided == "decided");
            int hand = recent.Count(s => s.Decided == "hand");
            if (decided + hand >= 3)
                lines.Add("Of the " + (decided + hand) + " times I asked, your hand lit it before you decided " + hand + " time" + (hand == 1 ? "" : "s") + ".");

            int aLot = recent.Count(s => s.Want == "alot");
            int aBit = recent.Count(s => s.Want == "bit");
            int notReally = recent.Count(s => s.Want == "notreally");
            if (aLot + aBit + notReally >= 3)
                lines.Add("How much you wanted it, when I asked: a lot " + aLot + ", a bit " + aBit + ", not really " + notReally + ".");

            int tasteTotal = recent.Count(s => !string.IsNullOrEmpty(s.Taste));
            int flat = recent.Count(s => s.Taste == "meh" || s.Taste == "didnt");
            if (tasteTotal >= 3)
                lines.Add("When I asked about the taste, \"meh\" or \"didn't notice\" came up " + flat + " out of " + tasteTotal + " times.");

            var moments = recent.Where(s => !string.IsNullOrEmpty(s.Moment)).GroupBy(s => s.Moment)
                .OrderByDescending(g => g.Count()).ToList();
            int momentTotal = moments.Sum(g => g.Count());
            if (momentTotal >= 3)
                lines.Add("Most common moment you lit one: " + MomentLabel(moments[0].Key) + " (" + moments[0].Count() + " of " + momentTotal + ").");

            var apps = recent.Where(s => !string.IsNullOrEmpty(s.App)).GroupBy(s => s.App, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ToList();
            if (apps.Count > 0 && apps[0].Count() >= 3 && apps[0].Count() * 4 >= recent.Count)
                lines.Add("In the last two weeks, " + apps[0].Count() + " of " + recent.Count + " were lit while " + AppLabel(apps[0].Key) + " was in front.");

            if (recent.Count >= 10)
            {
                var byHour = new int[24];
                foreach (var s in recent) byHour[s.Wall.Hour]++;
                int bestHour = 0, bestCount = -1;
                for (int h = 0; h < 24; h++)
                {
                    int c = byHour[h] + byHour[(h + 1) % 24];
                    if (c > bestCount) { bestCount = c; bestHour = h; }
                }
                lines.Add("Busiest time of day: " + bestHour.ToString("00", Inv) + ":00 to " + ((bestHour + 2) % 24).ToString("00", Inv) + ":00 (" + bestCount + " of " + recent.Count + ").");
            }

            int changed = data.ChangedMind.Select(Store.ParseTime).Count(t => t >= weekStart);
            if (changed > 0)
                lines.Add("You changed your mind " + changed + " time" + (changed == 1 ? "" : "s") + " this week.");

            int passed = data.RodeOut.Select(Store.ParseTime).Count(t => t >= weekStart);
            if (passed > 0)
                lines.Add("Urges that passed after breathing with " + data.Settings.PetName + ": " + passed + " this week.");

            double? firstWeek = Stats.FirstWeekAverage(times, now);
            double? lastSeven = Stats.LastSevenDaysAverage(times, now);
            if (firstWeek != null && lastSeven != null && Stats.DayStart(times.Min()).AddDays(14) <= today)
                lines.Add("First full week: " + Stats.Num(firstWeek.Value) + " a day. Last 7 days: " + Stats.Num(lastSeven.Value) + " a day.");

            double money = Stats.MoneyNotSpent(times, now, data.Settings.PackPrice, data.Settings.PackSize);
            if (money >= 1)
                lines.Add("Compared to your first week, that's about " + data.Settings.Currency + Math.Round(money).ToString(Inv) + " you didn't spend.");

            Drift drift = Stats.DriftCheck(times, now);
            if (drift != null)
                lines.Add("Heads up: " + Stats.Num(drift.Recent) + " a day over the last 3 days, " + Stats.Num(drift.Before) + " a day the week before.");

            if (lines.Count == 0)
                lines.Add("I'll have more to say after a few days of logging.");
            return lines;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BreathingRoom
{
    public class LogFacts
    {
        public int Today;
        public Gap Gap;
        public bool Record;
        public int YesterdayByNow;
        public bool MorningHello;
        public int YesterdayTotal;
        public Gap YesterdayLongest;
        public bool FirstEver;
        public Drift Drift;
        public bool Retro;
        public TimeSpan RetroAgo;
        public DateTime Now;
    }

    public class Question
    {
        public string Key;
        public string Text;
        public string[] Labels;
        public string[] Values;

        public Question(string key, string text, string[] labels, string[] values)
        {
            Key = key;
            Text = text;
            Labels = labels;
            Values = values;
        }
    }

    // Everything the cat says. Plain facts, a friend's tone, never advice.
    public static class Lines
    {
        static readonly Random Rng = new Random();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Pick(params string[] options)
        {
            return options[Rng.Next(options.Length)];
        }

        public static bool Chance(double p)
        {
            return Rng.NextDouble() < p;
        }

        public static string Spoken(TimeSpan t)
        {
            if (t < TimeSpan.FromMinutes(1)) return "under a minute";
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            string minutes = m + (m == 1 ? " minute" : " minutes");
            if (h == 0) return minutes;
            string hours = h + (h == 1 ? " hour" : " hours");
            return m == 0 ? hours : hours + " " + minutes;
        }

        public static string ForLog(LogFacts f, out string spoken)
        {
            string text;
            int n = f.Today;

            if (f.Retro)
            {
                text = (f.RetroAgo < TimeSpan.FromMinutes(2) ? "Added. " : "Added the one from about " + Stats.Duration(f.RetroAgo) + " ago. ") + "#" + n + " today.";
                spoken = "Added. Number " + n + " today.";
                return text;
            }

            if (f.FirstEver)
            {
                spoken = "Got it. First one.";
                return "Got it. First one I've seen. I'll keep count from here.";
            }

            if (f.Gap == null || f.Gap.Overnight)
            {
                string hello = f.Now.Hour < 12 ? "Morning." : "Hey.";
                if (f.MorningHello && f.YesterdayTotal > 0)
                {
                    text = hello + " Yesterday: " + f.YesterdayTotal;
                    if (f.YesterdayLongest != null && f.YesterdayLongest.Length >= TimeSpan.FromMinutes(30))
                        text += ", longest stretch " + Stats.Duration(f.YesterdayLongest.Length);
                    text += ". This is #" + n + " today.";
                }
                else
                {
                    text = n == 1 ? "First one today. Noted." : "#" + n + " today. Noted.";
                }
                spoken = n == 1 ? "First one today." : "Number " + n + " today.";
                return AddDrift(text, f);
            }

            string g = Stats.Duration(f.Gap.Length);
            string gs = Spoken(f.Gap.Length);
            if (f.Gap.Length < Stats.ShortGap)
            {
                text = Pick("Oh. Only " + g + " since the last one.",
                            "Huh, " + g + " since the last one.",
                            g + " since the last one. That one came quick.") + " #" + n + " today.";
                spoken = "Only " + gs + " since the last one.";
            }
            else if (f.Record)
            {
                text = g + " since the last one. Longest stretch today. #" + n + ".";
                spoken = gs + ". Longest stretch today.";
            }
            else
            {
                text = Pick(g + " since the last one. That's #" + n + " today.",
                            "#" + n + " today. The last one was " + g + " ago.",
                            "Noted. " + g + " since the last one, #" + n + " today.",
                            "*scribbles* #" + n + " today, " + g + " since the last one.");
                spoken = gs + " since the last one. Number " + n + " today.";
            }

            if (f.YesterdayByNow > 0 && Chance(0.3))
                text += " Yesterday by now: " + f.YesterdayByNow + ".";
            return AddDrift(text, f);
        }

        static string AddDrift(string text, LogFacts f)
        {
            if (f.Drift == null) return text;
            return text + "\n\nHeads up: " + Stats.Num(f.Drift.Recent) + " a day the last 3 days, " +
                   Stats.Num(f.Drift.Before) + " the week before. Just so you know.";
        }

        // Shown once, with the first log of a Monday when there is a full previous week to report.
        public static string WeeklyNote(IList<DateTime> times, DateTime now)
        {
            if (times.Count == 0) return null;
            DateTime thisWeek = Stats.WeekStart(now), lastWeek = thisWeek.AddDays(-7), before = thisWeek.AddDays(-14);
            DateTime firstDay = Stats.DayStart(times.Min());
            if (firstDay > lastWeek) return null;
            int last = Stats.CountWeek(times, lastWeek);
            if (last == 0) return null;
            string text = "New week. Last week: " + last;
            if (firstDay <= before) text += " (the week before: " + Stats.CountWeek(times, before) + ")";
            text += ".";
            Gap longest = Stats.LongestGap(times, lastWeek, thisWeek);
            if (longest != null && longest.Length >= TimeSpan.FromMinutes(45))
                text += " Longest stretch: " + Stats.Duration(longest.Length) + ".";
            return text;
        }

        // Shown once, after the first full week of logging.
        public static string FirstWeekNote(IList<DateTime> times, DateTime now)
        {
            double? average = Stats.FirstWeekAverage(times, now);
            if (average == null) return null;
            return "One week of noticing. Your first full week: " + Stats.Num(average.Value) + " a day, usually " +
                   Stats.Duration(Stats.UsualGap(times, now)) + " apart. I'll keep watching.";
        }

        public static Question AtLog(bool shortGap)
        {
            if (shortGap || Chance(0.5))
                return new Question("decided", "Did you decide on this one, or did your hand?",
                    new[] { "I decided", "My hand did" }, new[] { "decided", "hand" });
            return new Question("want", "How much did you want it?",
                new[] { "A lot", "A bit", "Not really" }, new[] { "alot", "bit", "notreally" });
        }

        public static Question Later()
        {
            if (Chance(0.5))
                return new Question("taste", "Real talk: how's this one tasting?",
                    new[] { "Good", "Meh", "Didn't notice" }, new[] { "good", "meh", "didnt" });
            return new Question("moment", "What's going on right now?",
                new[] { "Gaming", "Working", "Waiting / bored", "Stressed" }, new[] { "gaming", "working", "bored", "stressed" });
        }

        public static string Reply(string key, string value)
        {
            if (key == "decided" && value == "hand") return Pick("Hands are sneaky.", "Ha. Sneaky hands.", "Noted. Hands do that.");
            if (key == "taste" && value == "didnt") return Pick("Happens a lot. Noted.", "Noted. Funny how that goes.");
            if (key == "want" && value == "notreally") return Pick("Interesting. Noted.", "Huh. Noted.");
            if (key == "moment") return Pick("*writes it down*", "Got it. Thanks.");
            return Pick("Got it.", "Noted.", "*nods*", "Fair enough.");
        }

        public static string ChangedMind()
        {
            return Pick("Okay, scratched it.", "Un-logged. *nods*", "Cool. Not counted.");
        }

        public static string Wake()
        {
            return Pick("*yawns*", "*stretches*", "Oh, you're back.", "mrrp.");
        }
    }
}

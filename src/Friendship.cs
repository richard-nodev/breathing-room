using System;
using System.Collections.Generic;
using System.Globalization;

namespace BreathingRoom
{
    // How close you and the cat are. It grows only from spending time together: petting, playing,
    // breathing, answering its questions. Never from smoking less or more, so it can't become a rule.
    public static class Friendship
    {
        public static readonly int[] Thresholds = { 0, 10, 30, 60, 100, 150, 220, 300 };
        public const int DailyCap = 40;

        public static event Action<int> Gained;     // points just added
        public static event Action<int> LevelUp;    // the new level

        public static int Points { get { return Store.Data.Friendship; } }

        public static int Level
        {
            get
            {
                int level = 1;
                for (int i = 0; i < Thresholds.Length; i++) if (Points >= Thresholds[i]) level = i + 1;
                return level;
            }
        }

        // Points still needed for the next level (0 at the top).
        public static int ToNext
        {
            get { return Level >= Thresholds.Length ? 0 : Thresholds[Level] - Points; }
        }

        public static void Add(int points)
        {
            if (points <= 0) return;
            AppData d = Store.Data;
            string today = Stats.DayStart(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (d.FriendshipDay != today)
            {
                d.FriendshipDay = today;
                d.FriendshipToday = 0;
            }
            int room = DailyCap - d.FriendshipToday;
            if (room <= 0) return;
            points = Math.Min(points, room);
            int before = Level;
            d.Friendship += points;
            d.FriendshipToday += points;
            Store.Save();
            if (Gained != null) Gained(points);
            if (Level > before && LevelUp != null) LevelUp(Level);
        }
    }

    // A fur color or an accessory, and the friendship level that unlocks it.
    public class LookItem
    {
        public string Id, Name;
        public int Level;

        public LookItem(string id, string name, int level)
        {
            Id = id;
            Name = name;
            Level = level;
        }
    }

    public static class Looks
    {
        public static readonly List<LookItem> Coats = new List<LookItem>
        {
            new LookItem("ginger", "Ginger", 1),
            new LookItem("grey", "Grey tabby", 2),
            new LookItem("black", "Midnight", 3),
            new LookItem("snow", "Snow", 4),
            new LookItem("calico", "Calico", 5),
            new LookItem("cream", "Cream", 6)
        };

        public static readonly List<LookItem> Accessories = new List<LookItem>
        {
            new LookItem("none", "Nothing", 1),
            new LookItem("bell", "Bell collar", 2),
            new LookItem("scarf", "Cozy scarf", 3),
            new LookItem("bow", "Bow", 4),
            new LookItem("flowers", "Flower crown", 5),
            new LookItem("hat", "Party hat", 7)
        };

        public static bool Unlocked(LookItem item)
        {
            return Friendship.Level >= item.Level;
        }

        // The new things a level unlocks, for the "level up" line.
        public static List<LookItem> UnlockedAt(int level)
        {
            var list = new List<LookItem>();
            foreach (LookItem i in Coats) if (i.Level == level) list.Add(i);
            foreach (LookItem i in Accessories) if (i.Level == level) list.Add(i);
            return list;
        }
    }
}

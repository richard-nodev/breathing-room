using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BreathingRoom
{
    // ---------- Café rush: cats drop in for a treat; pick it up at the counter and the cat serves it ----------
    //
    // Click a treat on the counter, then the guest who wants it, and the cat walks over and serves.
    // Clicks queue up (little numbers show the order) and the cat carries two treats at a time, so a
    // good plan saves trips. Each day brings more guests, less patience and a longer menu; when three
    // guests give up waiting, the café closes for the night.

    public class CafeRushGame : MiniGame
    {
        const int MaxHearts = 3, TrayMax = 2, QueueMax = 5;
        const double Walk = 640, BenchHigh = 96, GuestSize = 0.7;
        static readonly string[] TreatNames = { "Fish", "Milk", "Cake", "Cookie" };
        static readonly string[] Coats = { "grey", "black", "snow", "calico", "cream", "ginger" };
        static readonly string[] Extras = { "none", "none", "none", "bell", "scarf", "bow" };
        static readonly Brush Calm = Ui.B("#7BC67B"), Hurry = Ui.B("#F2A04E"), Late = Ui.B("#E2574C");

        enum GuestMode { Arriving, Waiting, Happy, Leaving }
        enum Do { Pick, Serve, Eat }

        class Guest
        {
            public CatView View;
            public Border Bubble;
            public StackPanel Wants;
            public Rectangle Bar;
            public readonly List<int> Order = new List<int>();
            public GuestMode Mode;
            public int Seat, Items;
            public double X, Patience, Left, LeaveAt;
            public bool Warned, Grumpy;
        }

        // One click, waiting its turn: pick up a treat, serve a guest, or eat what is being carried.
        class Job
        {
            public Do What;
            public int Item;
            public Guest Guest;
            public Border Badge;
        }

        readonly List<Guest> guests = new List<Guest>();
        readonly Guest[] seats = new Guest[4];
        readonly List<Job> jobs = new List<Job>();
        readonly List<int> tray = new List<int>();
        readonly UIElement[] plates = new UIElement[4];
        readonly UIElement[] cushions = new UIElement[4];
        Canvas carried;
        TextBlock status;
        Border endCard;
        double counterTop, benchTop, benchLeft, nowT, start = -1, nextGuest, nextDayAt, lastHappy, lastTrill;
        int day, toCome, hearts = MaxHearts, tips, streak, bestStreak, served, servedToday, grumpyToday, best, bestDay;
        bool over, between;

        // ---------- how busy each day is ----------

        int SeatsOpen { get { return Math.Min(4, day + 1); } }
        int Menu { get { return day <= 1 ? 2 : day <= 3 ? 3 : 4; } }
        int GuestsToday { get { return 4 + 2 * day; } }
        double Gap { get { return Math.Max(1.2, 3.6 - 0.35 * day); } }
        double PatienceToday { get { return Math.Max(6.5, 17 - 1.6 * day); } }
        double Doubles { get { return day < 3 ? 0 : Math.Min(0.6, 0.1 + 0.12 * (day - 2)); } }
        int Mult { get { return Math.Min(4, 1 + streak / 4); } }

        double PlateX(int i) { return 74 + 84 * i; }
        double SeatX(int j) { return benchLeft + 66 + j * (S.W - benchLeft - 126) / 3; }

        public override void Start(Stage stage)
        {
            base.Start(stage);
            best = S.Best("cafe");
            bestDay = S.Best("cafe-day");
            counterTop = S.Floor - 104;
            benchTop = S.Floor - BenchHigh;
            benchLeft = Math.Max(440, S.W * 0.44);
            Counter();
            Bench();
            carried = new Canvas { Width = 56, Height = 34, IsHitTestVisible = false };
            Panel.SetZIndex(carried, 3);
            S.Add(carried);
            status = StatusLine(false);
            CatBody cat = S.Cat;
            cat.X = 410 - CatView.W / 2;
            cat.Dir = 1;
            cat.View.Face(1);
            cat.Render();
            NewDay();
        }

        // ---------- the café ----------

        // The counter on the left with a plate for each treat; ones not on the menu yet stay hidden.
        void Counter()
        {
            const double left = 18, width = 4 * 84 + 28;
            var c = new Canvas { IsHitTestVisible = false };
            Put(c, new Rectangle { Width = width, Height = S.Floor - counterTop, Fill = Ui.B("#C98F5A"), Stroke = Ui.B("#9C6A3E"), StrokeThickness = 1.2 }, left, counterTop);
            for (double x = left + 20; x < left + width - 10; x += 28) Put(c, new Rectangle { Width = 2, Height = S.Floor - counterTop - 12, Fill = Ui.B("#B87E4C") }, x, counterTop + 8);
            Put(c, new Rectangle { Width = width + 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = Ui.B("#A86E48"), Stroke = Ui.B("#8A5A3B"), StrokeThickness = 1 }, left - 6, counterTop - 8);
            Panel.SetZIndex(c, -5);
            S.Add(c);
            for (int i = 0; i < 4; i++)
            {
                var plate = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Hidden };
                Put(plate, new Ellipse { Width = 66, Height = 14, Fill = Ui.B("#FFF8EF"), Stroke = Ui.B("#E2CDB5"), StrokeThickness = 1 }, -33, -12);
                Put(plate, Icon(i, 1.4), -21, -52);
                var name = Ui.Text(TreatNames[i], 12.5, Ui.B("#FFF3E2"));
                name.FontWeight = FontWeights.SemiBold;
                name.Width = 76;
                name.TextAlignment = TextAlignment.Center;
                Put(plate, name, -38, 18);
                Stage.Place(plate, PlateX(i), counterTop);
                Panel.SetZIndex(plate, -4);
                S.Add(plate);
                plates[i] = plate;
            }
        }

        // A long window bench on the right where the guests sit, with a cushion for each seat.
        void Bench()
        {
            var c = new Canvas { IsHitTestVisible = false };
            Put(c, new Rectangle { Width = S.W - benchLeft + 20, Height = 13, RadiusX = 3, RadiusY = 3, Fill = Ui.B("#B07A4E"), Stroke = Ui.B("#8A5A3B"), StrokeThickness = 1 }, benchLeft, benchTop);
            for (double x = benchLeft + 24; x < S.W; x += 180) Put(c, new Rectangle { Width = 10, Height = S.Floor - benchTop - 13, Fill = Ui.B("#8A5A3B") }, x, benchTop + 13);
            Panel.SetZIndex(c, -5);
            S.Add(c);
            string[] colors = { "#F4A6B4", "#9FD3C7", "#FFD07A", "#B9A7F0" };
            for (int j = 0; j < 4; j++)
            {
                var cushion = new Ellipse { Width = 84, Height = 16, Fill = Ui.B(colors[j]), Stroke = Ui.B("#33000000"), StrokeThickness = 1, Visibility = Visibility.Hidden };
                Stage.Place(cushion, SeatX(j) - 42, benchTop - 9);
                Panel.SetZIndex(cushion, -4);
                S.Add(cushion);
                cushions[j] = cushion;
            }
        }

        // The treats, drawn on a 30 x 30 grid.
        static Canvas Icon(int item, double scale)
        {
            var c = new Canvas { Width = 30, Height = 30, IsHitTestVisible = false };
            if (scale != 1) c.LayoutTransform = new ScaleTransform(scale, scale);
            switch (item)
            {
                case 0:   // fish
                    AddPath(c, "M 21 15 L 29 8 L 29 22 Z", "#7FC8F8", "#4F9CCB", 1);
                    Put(c, new Ellipse { Width = 22, Height = 13, Fill = Ui.B("#7FC8F8"), Stroke = Ui.B("#4F9CCB"), StrokeThickness = 1 }, 1, 8.5);
                    Put(c, new Ellipse { Width = 3, Height = 3, Fill = Ui.Ink }, 5, 12.5);
                    break;
                case 1:   // a bottle of milk
                    AddPath(c, "M 10 11 Q 10 8 12.5 7 L 12.5 4 L 17.5 4 L 17.5 7 Q 20 8 20 11 L 20 27 Q 20 28 19 28 L 11 28 Q 10 28 10 27 Z", "#FFFFFF", "#8FB3CF", 1.2);
                    Put(c, new Rectangle { Width = 6, Height = 3, RadiusX = 1, RadiusY = 1, Fill = Ui.B("#5DA9E9") }, 12, 2);
                    Put(c, new Rectangle { Width = 10, Height = 6, Fill = Ui.B("#BFE0FA") }, 10, 16);
                    break;
                case 2:   // a slice of cake with a cherry
                    AddPath(c, "M 4 16 L 26 11 L 26 26 L 4 26 Z", "#F6D9A8", "#C99A5B", 1);
                    AddPath(c, "M 4 21 L 26 19", null, "#FFFFFF", 2);
                    AddPath(c, "M 4 16 L 26 11 L 26 8 Q 15 9 4 13 Z", "#FF9EB8", "#E07A98", 1);
                    Put(c, new Ellipse { Width = 6, Height = 6, Fill = Ui.B("#E53950") }, 18, 3);
                    break;
                default:  // a cookie with a paw print
                    Put(c, new Ellipse { Width = 24, Height = 24, Fill = Ui.B("#E2B26F"), Stroke = Ui.B("#B98444"), StrokeThickness = 1.2 }, 3, 3);
                    Put(c, new Ellipse { Width = 9, Height = 7, Fill = Ui.B("#8A5A2B") }, 10.5, 15);
                    Put(c, new Ellipse { Width = 4, Height = 4, Fill = Ui.B("#8A5A2B") }, 8, 10);
                    Put(c, new Ellipse { Width = 4, Height = 4, Fill = Ui.B("#8A5A2B") }, 13, 8);
                    Put(c, new Ellipse { Width = 4, Height = 4, Fill = Ui.B("#8A5A2B") }, 18, 10);
                    break;
            }
            return c;
        }

        // A guest is a whole other cat. CatView draws itself in the shared look, so borrow it for a moment.
        CatView NewGuestView()
        {
            string coatWas = CatView.Coat, extraWas = CatView.Accessory;
            string coat;
            do coat = Coats[S.Rng.Next(Coats.Length)]; while (coat == coatWas);
            CatView.Coat = coat;
            CatView.Accessory = Extras[S.Rng.Next(Extras.Length)];
            try { return new CatView(); }
            finally
            {
                CatView.Coat = coatWas;
                CatView.Accessory = extraWas;
            }
        }

        // ---------- a day ----------

        void NewDay()
        {
            day++;
            between = false;
            toCome = GuestsToday;
            servedToday = grumpyToday = 0;
            nextGuest = nowT + 1.2;
            for (int i = 0; i < 4; i++)
            {
                bool on = i < Menu;
                if (on && plates[i].Visibility != Visibility.Visible && day > 1)
                    S.Float("new!", PlateX(i) - 16, counterTop - 86, Ui.OrangeDeep, 16);
                plates[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
            for (int j = 0; j < 4; j++) cushions[j].Visibility = j < SeatsOpen ? Visibility.Visible : Visibility.Hidden;
            Banner("Day " + day, DayNote(), day == 1 ? 4.5 : 2.8, false);
            if (day > 1) Sfx.Trill();
            UpdateStatus();
        }

        string DayNote()
        {
            switch (day)
            {
                case 1: return "Click a treat on the counter, then the cat who wants it.";
                case 2: return "Cake's on the menu, and there's a third seat.";
                case 3: return "Four seats now, and some cats want two treats.";
                case 4: return "Cookies too! " + Name + " can carry two at once.";
                default: return Lines.Pick("Busier than ever.", "The whole street is hungry.", "Rush hour!");
            }
        }

        void EndDay(double t)
        {
            between = true;
            nextDayAt = t + 3.2;
            while (jobs.Count > 0) RemoveJob(0);
            int bonus = 20 * day;
            tips += bonus;
            string sub = "Served " + servedToday + " of " + GuestsToday + "   ·   +" + bonus + " tips";
            if (grumpyToday == 0 && hearts < MaxHearts)
            {
                hearts++;
                sub += "   ·   perfect day, +♥";
            }
            else if (grumpyToday == 0) sub += "   ·   perfect day!";
            Banner("Day " + day + " done!", sub, 3.0, false);
            Sfx.Happy();
            UpdateStatus();
        }

        int Present()
        {
            int n = 0;
            foreach (Guest g in guests)
                if (g.Mode == GuestMode.Arriving || g.Mode == GuestMode.Waiting) n++;
            return n;
        }

        Guest Neediest()
        {
            Guest worst = null;
            foreach (Guest g in guests)
                if (g.Mode == GuestMode.Waiting && (worst == null || g.Left / g.Patience < worst.Left / worst.Patience)) worst = g;
            return worst;
        }

        public override void Frame(double t, double dt)
        {
            nowT = t;
            if (start < 0)
            {
                start = t;
                nextGuest = t + 1.6;
            }
            CatBody cat = S.Cat;
            cat.Step(dt);
            if (!over)
            {
                if (between && t >= nextDayAt) NewDay();
                if (!between && toCome > 0 && t >= nextGuest) Arrive(t);
                Work(dt);
            }
            Guests(t, dt);
            if (!over && !between && toCome == 0 && Present() == 0) EndDay(t);

            // Whatever is being carried rides on the cat's head, between the ears.
            Stage.Place(carried, cat.Mid - 28, S.Floor - CatView.H + 3 - cat.Lift - 8);
            Guest watch = Neediest();
            if (watch != null) cat.Look(new Point(SeatX(watch.Seat), benchTop - 40));
            else if (S.MouseKnown) cat.Look(S.Mouse);
            cat.Render();
        }

        // ---------- the guests ----------

        void Arrive(double t)
        {
            var free = new List<int>();
            for (int j = 0; j < SeatsOpen; j++) if (seats[j] == null) free.Add(j);
            if (free.Count == 0) return;   // full: the next one waits outside until a seat frees up
            var g = new Guest { Seat = free[S.Rng.Next(free.Count)], X = S.W + 70, Mode = GuestMode.Arriving };
            g.Items = S.Rng.NextDouble() < Doubles ? 2 : 1;
            for (int k = 0; k < g.Items; k++) g.Order.Add(S.Rng.Next(Menu));
            g.Patience = PatienceToday * (g.Items == 2 ? 1.35 : 1);
            g.View = NewGuestView();
            g.View.RenderTransform = new ScaleTransform(GuestSize, GuestSize, CatView.W / 2, 97);
            g.View.IsHitTestVisible = false;
            Panel.SetZIndex(g.View, -2);
            S.Add(g.View);
            g.View.Face(-1);
            g.View.SetWalking(true);
            seats[g.Seat] = g;
            guests.Add(g);
            Place(g);
            toCome--;
            nextGuest = t + Gap * (0.75 + S.Rng.NextDouble() * 0.5);
        }

        void Place(Guest g)
        {
            // Paws on the bench, like CatBody.Render does for the floor.
            Canvas.SetLeft(g.View, g.X - CatView.W / 2);
            Canvas.SetTop(g.View, benchTop - CatView.H + 3);
        }

        void Guests(double t, double dt)
        {
            CatBody cat = S.Cat;
            for (int i = guests.Count - 1; i >= 0; i--)
            {
                Guest g = guests[i];
                switch (g.Mode)
                {
                    case GuestMode.Arriving:
                        double seat = SeatX(g.Seat);
                        g.X = Math.Max(seat, g.X - 260 * dt);
                        Place(g);
                        if (g.X <= seat) Sit(g, t);
                        break;

                    case GuestMode.Waiting:
                        if (over) break;
                        g.Left -= dt;
                        Bar(g);
                        if (!g.Warned && g.Left < g.Patience * 0.3)
                        {
                            g.Warned = true;
                            S.Float("!", SeatX(g.Seat) + 26, benchTop - 150, Late, 24);
                            g.View.FlickTail();
                        }
                        if (g.Left <= 0) GiveUp(g);
                        else g.View.LookAt(cat.Mid - g.X, S.Floor - 50 - (benchTop - 45));
                        break;

                    case GuestMode.Happy:
                        if (t >= g.LeaveAt)
                        {
                            if (seats[g.Seat] == g) seats[g.Seat] = null;
                            g.Mode = GuestMode.Leaving;
                            g.View.SetHappy(false);
                            g.View.Face(1);
                            g.View.SetWalking(true);
                        }
                        break;

                    case GuestMode.Leaving:
                        g.X += (g.Grumpy ? 380 : 280) * dt;
                        Place(g);
                        if (g.X > S.W + 80)
                        {
                            g.View.SetWalking(false);   // the walk repeats forever, even off the screen
                            S.Remove(g.View);
                            guests.RemoveAt(i);
                        }
                        break;
                }
            }
        }

        void Sit(Guest g, double t)
        {
            g.Mode = GuestMode.Waiting;
            g.Left = g.Patience;
            g.View.SetWalking(false);
            g.View.Face(-1);
            ShowOrder(g);
            if (t - lastTrill > 4 && S.Rng.NextDouble() < 0.4)
            {
                lastTrill = t;
                Sfx.Trill();
            }
        }

        // A bubble over the guest with what it wants and how long it will wait.
        void ShowOrder(Guest g)
        {
            g.Wants = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var track = new Canvas { Width = 52, Height = 6, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            track.Children.Add(new Rectangle { Width = 52, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Ui.B("#EADBCB") });
            g.Bar = new Rectangle { Width = 52, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Calm };
            track.Children.Add(g.Bar);
            var panel = new StackPanel();
            panel.Children.Add(g.Wants);
            panel.Children.Add(track);
            g.Bubble = new Border
            {
                Background = Ui.B("#FFFDF8"),
                BorderBrush = Ui.B("#E2CDB5"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(7, 5, 7, 6),
                Width = 74,
                Child = panel,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(g.Bubble, 2);
            Stage.Place(g.Bubble, SeatX(g.Seat) - 37, benchTop - 126);
            S.Add(g.Bubble);
            ShowWants(g);
        }

        static void ShowWants(Guest g)
        {
            g.Wants.Children.Clear();
            foreach (int item in g.Order) g.Wants.Children.Add(Icon(item, 0.9));
        }

        static void Bar(Guest g)
        {
            double k = Math.Max(0, g.Left / g.Patience), w = 52 * k;
            if (Math.Abs(g.Bar.Width - w) > 0.4) g.Bar.Width = w;
            Brush b = k > 0.5 ? Calm : k > 0.25 ? Hurry : Late;
            if (g.Bar.Fill != b) g.Bar.Fill = b;
        }

        void GiveUp(Guest g)
        {
            double x = SeatX(g.Seat);
            Leave(g, true);
            hearts--;
            grumpyToday++;
            streak = 0;
            S.Float(Lines.Pick("hmph.", "too slow…", "*leaves*"), x - 30, benchTop - 120, S.Hint, 16);
            Sfx.Mew();
            UpdateStatus();
            if (hearts <= 0) CloseCafe();
        }

        void Leave(Guest g, bool grumpy)
        {
            if (g.Bubble != null)
            {
                S.Remove(g.Bubble);
                g.Bubble = null;
            }
            g.Grumpy = grumpy;
            if (grumpy)
            {
                if (seats[g.Seat] == g) seats[g.Seat] = null;
                g.Mode = GuestMode.Leaving;
                g.View.Face(1);
                g.View.SetWalking(true);
            }
            else g.Mode = GuestMode.Happy;   // keeps its seat until it gets up, so no one sits on it
            for (int i = jobs.Count - 1; i >= 0; i--) if (jobs[i].Guest == g) RemoveJob(i);
        }

        // ---------- the cat at work ----------

        // Works through the clicks in order: walk to the plate or the guest, then do it.
        void Work(double dt)
        {
            CatBody cat = S.Cat;
            if (cat.Jumping) return;
            if (jobs.Count == 0)
            {
                cat.Stop();
                return;
            }
            Job job = jobs[0];
            double to = job.What == Do.Pick ? PlateX(job.Item) : job.What == Do.Serve ? SeatX(job.Guest.Seat) : cat.Mid;
            if (!cat.WalkTo(to, Walk, dt)) return;
            // Off the list first: serving the last treat sends the guest off, which drops its jobs.
            RemoveJob(0);
            if (job.What == Do.Pick) Pick(job.Item);
            else if (job.What == Do.Serve) Serve(job.Guest);
            else Eat();
        }

        void Pick(int item)
        {
            CatBody cat = S.Cat;
            if (tray.Count >= TrayMax)
            {
                S.Float("paws full!", cat.Mid - 34, S.Floor - 150, S.Hint, 15);
                return;
            }
            tray.Add(item);
            ShowTray();
            cat.View.Hop(8);
            Sfx.Bounce(0.25);
        }

        void Serve(Guest g)
        {
            CatBody cat = S.Cat;
            int k = -1;
            for (int i = 0; i < tray.Count && k < 0; i++) if (g.Order.Contains(tray[i])) k = i;
            double x = SeatX(g.Seat);
            if (k < 0)
            {
                // Nothing on the tray that this guest asked for.
                g.Left = Math.Max(1, g.Left - 2);
                streak = 0;
                Sfx.Question();
                S.Float(tray.Count == 0 ? "paws empty…" : "that's not mine", x - 50, benchTop - 150, S.Hint, 15);
                UpdateStatus();
                return;
            }
            int item = tray[k];
            tray.RemoveAt(k);
            g.Order.Remove(item);
            ShowTray();
            cat.Jump(-460, 0);   // a little hop up to the bench
            Fly(item, cat.Mid, S.Floor - 118, x, benchTop - 46);
            if (g.Order.Count > 0) ShowWants(g);
            else Served(g);
        }

        void Served(Guest g)
        {
            double k = Math.Max(0, g.Left / g.Patience), x = SeatX(g.Seat);
            streak++;
            bestStreak = Math.Max(bestStreak, streak);
            served++;
            servedToday++;
            int pts = (10 * g.Items + (int)Math.Round(10 * k)) * Mult;
            string note = null;
            if (k > 0.7)
            {
                pts += 5;
                note = Lines.Pick("purrfect!", "so quick!", "purrfect!");
            }
            else if (k < 0.2)
            {
                pts += 5;
                note = "just in time!";
            }
            tips += pts;
            Leave(g, false);
            g.LeaveAt = nowT + 1.1;
            g.View.SetHappy(true);
            g.View.Hop(8);
            g.View.Purr();
            S.Float("+" + pts, x - 14, benchTop - 118, Ui.OrangeDeep, 20);
            if (note != null) S.Float(note, x - 40, benchTop - 146, Ui.OrangeDeep, 15);
            Sfx.Pop();
            if (streak % 4 == 0)
            {
                S.Float("streak " + streak + "!  ×" + Mult, S.Cat.Mid - 50, S.Floor - 170, Ui.OrangeDeep, 18);
                if (nowT - lastHappy > 2)
                {
                    lastHappy = nowT;
                    Sfx.Happy();
                }
            }
            UpdateStatus();
        }

        // Clicking the cat while it carries something: it eats it. Clears the tray, costs a little time.
        void Eat()
        {
            if (tray.Count == 0) return;
            tray.Clear();
            ShowTray();
            S.Cat.View.Purr();
            S.Float(Lines.Pick("nom nom", "*munch*", "mine now"), S.Cat.Mid - 30, S.Floor - 150, Ui.OrangeDeep, 15);
        }

        void ShowTray()
        {
            carried.Children.Clear();
            if (tray.Count == 0) return;
            Put(carried, new Ellipse { Width = 54, Height = 8, Fill = Ui.B("#ECE6DC"), Stroke = Ui.B("#B9AFA2"), StrokeThickness = 1 }, 1, 25);
            for (int i = 0; i < tray.Count; i++) Put(carried, Icon(tray[i], 0.85), tray.Count == 1 ? 15 : 2 + i * 26, 1);
        }

        void Fly(int item, double x0, double y0, double x1, double y1)
        {
            var icon = Icon(item, 0.85);
            Panel.SetZIndex(icon, 4);
            Stage.Place(icon, x0 - 13, y0);
            S.Add(icon);
            var time = TimeSpan.FromSeconds(0.25);
            var across = new DoubleAnimation(x0 - 13, x1 - 13, time);
            var up = new DoubleAnimation(y0, y1, time) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            up.Completed += delegate { S.Remove(icon); };
            icon.BeginAnimation(Canvas.LeftProperty, across);
            icon.BeginAnimation(Canvas.TopProperty, up);
        }

        // ---------- clicks ----------

        public override void Down(Point p)
        {
            if (over) return;
            for (int i = 0; i < Menu; i++)
                if (Math.Abs(p.X - PlateX(i)) < 40 && p.Y > counterTop - 60 && p.Y < counterTop + 14)
                {
                    Queue(Do.Pick, i, null);
                    return;
                }
            for (int j = 0; j < SeatsOpen; j++)
            {
                Guest g = seats[j];
                if (g != null && g.Mode == GuestMode.Waiting && Math.Abs(p.X - SeatX(j)) < 52 && p.Y > benchTop - 132 && p.Y < benchTop + 16)
                {
                    Queue(Do.Serve, -1, g);
                    return;
                }
            }
            CatBody cat = S.Cat;
            double top = S.Floor - CatView.H + 3 - cat.Lift;
            if (tray.Count > 0 && p.X > cat.X + 12 && p.X < cat.X + CatView.W - 12 && p.Y > top + 20 && p.Y < S.Floor + 4)
                Queue(Do.Eat, -1, null);
        }

        void Queue(Do what, int item, Guest g)
        {
            if (jobs.Count >= QueueMax) return;
            var job = new Job { What = what, Item = item, Guest = g };
            if (what != Do.Eat)
            {
                job.Badge = new Border
                {
                    Width = 19,
                    Height = 19,
                    CornerRadius = new CornerRadius(9.5),
                    Background = Ui.OrangeDeep,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1.5),
                    IsHitTestVisible = false,
                    Child = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, FontFamily = Ui.Font, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                Panel.SetZIndex(job.Badge, 6);
                S.Add(job.Badge);
            }
            jobs.Add(job);
            Renumber();
        }

        void RemoveJob(int i)
        {
            if (jobs[i].Badge != null) S.Remove(jobs[i].Badge);
            jobs.RemoveAt(i);
            Renumber();
        }

        // Numbers on the plates and guests in the order the cat will get to them; several on the
        // same one sit side by side.
        void Renumber()
        {
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < jobs.Count; i++)
            {
                Job job = jobs[i];
                if (job.Badge == null) continue;
                ((TextBlock)job.Badge.Child).Text = (i + 1).ToString(CultureInfo.InvariantCulture);
                string key = job.What == Do.Pick ? "p" + job.Item : "s" + job.Guest.Seat;
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                if (job.What == Do.Pick) Stage.Place(job.Badge, PlateX(job.Item) + 18 + n * 16, counterTop - 66);
                else Stage.Place(job.Badge, SeatX(job.Guest.Seat) + 30 + n * 16, benchTop - 138);
            }
        }

        // ---------- the end ----------

        void CloseCafe()
        {
            over = true;
            while (jobs.Count > 0) RemoveJob(0);
            foreach (Guest g in guests)
                if (g.Mode == GuestMode.Arriving || g.Mode == GuestMode.Waiting) Leave(g, true);
            S.Cat.Stop();
            bool record = S.SetBest("cafe", tips);
            if (S.SetBest("cafe-day", day)) bestDay = day;
            best = Math.Max(best, tips);
            endCard = EndCard(tips + " tips",
                "Closed on day " + day + (record ? "   ·   new best! *happy tail*" : "   ·   best " + best),
                served + " cat" + (served == 1 ? "" : "s") + " served   ·   longest streak " + bestStreak + "   ·   furthest day " + bestDay,
                "Open again", null, delegate { Restart(); });
            if (record) Sfx.Happy();
            UpdateStatus();
        }

        void Restart()
        {
            if (endCard != null)
            {
                S.Remove(endCard);
                endCard = null;
            }
            foreach (Guest g in guests)
            {
                g.View.SetWalking(false);
                S.Remove(g.View);
                if (g.Bubble != null) S.Remove(g.Bubble);
            }
            guests.Clear();
            for (int j = 0; j < seats.Length; j++) seats[j] = null;
            while (jobs.Count > 0) RemoveJob(0);
            tray.Clear();
            ShowTray();
            day = tips = streak = bestStreak = served = 0;
            hearts = MaxHearts;
            over = between = false;
            NewDay();
        }

        // Lives, the day and how it is going; the bar at the top only has room for the tips.
        void UpdateStatus()
        {
            string s = Hearts(hearts, MaxHearts) + "     Day " + day + ":  " + servedToday + " of " + GuestsToday + " served";
            if (streak > 0) s += "     streak " + streak + (Mult > 1 ? "  ×" + Mult : "");
            status.Text = s;
        }

        public override void Stop()
        {
            base.Stop();
            foreach (Guest g in guests) g.View.SetWalking(false);
        }

        public override string ScoreText { get { return "Tips " + tips + "    Best " + Math.Max(best, tips); } }

        public override string Summary
        {
            get
            {
                if (served == 0) return null;
                return "I served " + served + " cat" + (served == 1 ? "" : "s") + " at the café" + (day > 1 ? ", all the way to day " + day : "") + ". *tired but proud*";
            }
        }
    }
}

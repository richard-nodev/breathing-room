using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BreathingRoom
{
    // ---------- Rooftop run: one night across town, from sunset to sunrise ----------
    //
    // An endless runner. The cat keeps its spot on the left and the town scrolls past. Space or a
    // click jumps; holding it keeps gravity light for a moment, so a tap hops a short chimney and a
    // hold clears a tall one or a wide gap.
    //
    // It is made to absorb, not to stress. The run is a journey through eight parts of town, each
    // with its own sky and one new thing to do: washing lines, bouncy awnings, a balloon ride, a
    // night train, lantern lines to slide along, the harbor, the gardens at sunrise. A lap takes
    // about six minutes. Speed stays moderate and eases off after a slip, and a heart comes back
    // in every new part of town. After the busy market comes a calm balloon ride, whose waves of
    // fish rise for about four seconds and fall for about six.

    public class RooftopRunGame : MiniGame
    {
        // Jump physics in px and seconds (gravity is stronger than the room's: running wants snappy jumps).
        const double G = 2500, JumpSpeed = 700, HoldTime = 0.24, HoldGravity = 0.4;
        const double BounceSpeed = 1000;     // off an awning
        const double PxPerMeter = 25;
        const double Feet = 18;              // how far past an edge the paws still find the roof
        const double Claw = 14;              // how far below an edge the cat can still pull itself up
        const double ClawRising = 40;        // ... and while still going up, it scrambles the rest of the way
        const double Body = 20, Tall = 54;   // the cat's box at its running size (half width, height)
        // Bumping into things is judged a little inside that box (a round cat, ears that bend), so a
        // jump that looks like it made it did make it.
        const double Waist = 15, Scrape = 10, Ears = 6;
        const double MaxSpeed = 500;
        const double BlendMeters = 90;       // how long the sky takes to turn into the next part's
        const double BalloonLow = 140, BalloonHigh = 285, BalloonSpeed = 260;
        const int MaxLives = 3;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        enum Kind { Town, Laundry, Market, Sky, Train, Festival, Harbor, Garden }

        // One part of town. Sun and moon heights run from 0 (on the horizon) to 1 (high up); below
        // 0 they are setting, and gone at -0.12.
        class Leg
        {
            public string Name, Hint, Place;
            public Kind Kind;
            public double Meters, Speed, Stars, SunFrom, SunTo, MoonFrom, MoonTo, Lit;
            public Color Top, Mid, Low, Wall, Far, Near;
        }

        static Leg L(string name, string place, string hint, Kind kind, double meters, double speed, string top, string mid, string low,
            double stars, double sunFrom, double sunTo, double moonFrom, double moonTo, string wall, string far, string near, double lit)
        {
            return new Leg
            {
                Name = name, Place = place, Hint = hint, Kind = kind, Meters = meters, Speed = speed,
                Top = Ui.C(top), Mid = Ui.C(mid), Low = Ui.C(low), Stars = stars,
                SunFrom = sunFrom, SunTo = sunTo, MoonFrom = moonFrom, MoonTo = moonTo,
                Wall = Ui.C(wall), Far = Ui.C(far), Near = Ui.C(near), Lit = lit
            };
        }

        static readonly Leg[] Route =
        {
            L("Old town", "the old town", "Space or click to jump. Hold it to jump higher.", Kind.Town, 520, 285,
                "#3D4380", "#B4688E", "#F4A866", 0.05, 0.3, 0.04, -1, -1, "#6E5068", "#5C4672", "#45355C", 0.28),
            L("Laundry lanes", "the laundry lanes", "Run under the washing lines. Jumping gets you tangled.", Kind.Laundry, 560, 300,
                "#29316A", "#5E4C88", "#C47E86", 0.35, -0.2, -0.3, 0.06, 0.3, "#5C4D72", "#3C3766", "#2E2A52", 0.3),
            L("Market street", "the market", "Fall onto a striped awning and it bounces you back up.", Kind.Market, 600, 320,
                "#18204E", "#33356E", "#6A4C80", 0.7, -1, -1, 0.3, 0.5, "#51476B", "#2A2E5A", "#20214A", 0.32),
            L("Balloon drift", "the balloon ride", "Hold to float up, let go to drift down. Nothing to bump into up here.", Kind.Sky, 330, 260,
                "#0E1640", "#22306A", "#3C4C88", 1, -1, -1, 0.55, 0.65, "#3F3B64", "#1E2654", "#181E46", 0.22),
            L("Night train", "the night train", "Hop down onto the train. Stay low under the signals.", Kind.Train, 640, 340,
                "#0B1433", "#1E2A5E", "#34407C", 1, -1, -1, 0.65, 0.7, "#3A3458", "#1B2350", "#151B41", 0.2),
            L("Lantern festival", "the lantern festival", "Jump up to a lantern line and slide along it.", Kind.Festival, 600, 340,
                "#0A0F2A", "#1E1E4E", "#4E3060", 0.9, -1, -1, 0.7, 0.55, "#3D3258", "#23214B", "#1B173B", 0.38),
            L("Harbor", "the harbor", "Hop along the container stacks. The gulls don't mind.", Kind.Harbor, 640, 350,
                "#0E1A3A", "#1E3358", "#33507A", 0.75, -1, -1, 0.45, 0.22, "#2E3E5E", "#1C2C4A", "#14223A", 0.2),
            L("Sunrise gardens", "the sunrise gardens", "Morning's coming. Mind the flower pots.", Kind.Garden, 600, 330,
                "#4C6CAA", "#D498B0", "#FFCB90", 0.12, -0.02, 0.32, 0.18, -0.3, "#7C6A8A", "#7A6890", "#5E5080", 0.1),
        };

        static readonly double LapMeters = SumMeters();

        static double SumMeters()
        {
            double m = 0;
            foreach (Leg l in Route) m += l.Meters;
            return m;
        }

        class Fish
        {
            public UIElement El;
            public double X, Lift;   // its middle, in the world
            public bool Golden, Done;
        }

        // On a roof: something solid the cat can stand on (a chimney, a crate, a flower pot), or
        // something hanging across the way at head height (washing, a train signal, a crane hook)
        // that it has to run under.
        class Block
        {
            public double X0, X1, Top, Bottom, Closest = double.MaxValue;
            public bool Overhead, Passed, Stood, Hit;
        }

        // A shop's awning over the street before a roof: falling onto it bounces the cat back up.
        class Pad
        {
            public double X0, X1, Top;
        }

        // A lantern line from one roof down to the next: jump up to it and the cat slides along.
        class Zip
        {
            public double X0, Y0, X1, Y1;
            public double Y(double x) { return Y0 + (Y1 - Y0) * (x - X0) / (X1 - X0); }
        }

        // One building (or train carriage, or stack of containers). X is in the world (distance run
        // plus the cat's spot), Top is the height of the roof above the room's floor line, like the
        // cat's Lift.
        class Roof
        {
            public Canvas El;
            public TranslateTransform Move;
            public double X0, X1, Top;
            public Kind Kind;
            public Pad Pad;       // over the street before this roof
            public Zip Zip;       // from the last roof to this one
            public bool ZipOut;   // the next roof gets a line from this one
            public readonly List<Block> Blocks = new List<Block>();
            public readonly List<Fish> Catch = new List<Fish>();
        }

        // A pigeon or a gull sitting on a roof, until the cat comes close.
        class Bird
        {
            public Canvas El;
            public UIElement Sit, Fly;
            public ScaleTransform Flap;
            public double X, Lift, Vx, Vy, Since;
            public bool Flying;
        }

        // A firework, a shooting star or a flock of birds crossing the sky.
        class Burst
        {
            public UIElement El;
            public ScaleTransform Grow;
            public TranslateTransform Move;
            public double Born, Life, X, Y, Vx, Vy;
            public int Type;   // 0 firework, 1 shooting star, 2 flock, 3 a balloon let go
        }

        // A strip of scenery behind the roofs, drawn twice side by side so it can scroll forever.
        class Strip
        {
            public UIElement El;
            public TranslateTransform Shift;
            public double Speed, Period;
            public Kind Kind;
            public bool Always;
        }

        enum Mode { Run, Balloon, Zip, Falling, Over }

        readonly List<Roof> roofs = new List<Roof>();
        readonly List<Bird> birds = new List<Bird>();
        readonly List<Burst> bursts = new List<Burst>();
        readonly List<Strip> strips = new List<Strip>();
        readonly ScaleTransform small = new ScaleTransform(0.72, 0.72, CatView.W / 2, 97);
        readonly RotateTransform tilt = new RotateTransform(0, CatView.W / 2, 62);
        readonly Random rng = new Random();
        LinearGradientBrush skyFill;
        SolidColorBrush farInk, nearInk;
        Brush halo;
        UIElement stars;
        Canvas sun, moon, balloon;
        TranslateTransform balloonMove;
        TextBlock status;
        Border endCard;
        Mode mode = Mode.Run;
        Zip zip;
        double nowT, dist, speed, catX, lift, vUp, holdFor, ease = 1, dpi = 1;
        double coyoteUntil, bufferUntil, safeUntil, fallingSince, overAt, lastPop, statusAt, lookAt, flagX, flagWorld;
        double nextShoot, nextFirework, nextFlock, nextThought, waveX = double.NaN, freeFrom;
        bool air, holding, keyHeld, flagPlaced, flagPassed, resumed, purring;
        int lives, fishPts, bonus, fishCount, streak, bestStreak, best, bestM, cards, newCards;
        int legSerial = -1, balloonSerial = -1, farthest;

        public override bool OwnScenery { get { return true; } }

        public override void Start(Stage stage)
        {
            base.Start(stage);
            best = S.Best("run");
            bestM = S.Best("run-m");
            cards = S.Best("run-cards");
            var glow = new RadialGradientBrush(Color.FromArgb(150, 255, 226, 122), Color.FromArgb(0, 255, 226, 122));
            glow.Freeze();
            halo = glow;
            dpi = Math.Max(1, VisualTreeHelper.GetDpi(S.Layer).DpiScaleX);
            Scenery();

            // Smaller while running, so there is more of the town ahead to see.
            CatBody cat = S.Cat;
            var turn = new TransformGroup();
            turn.Children.Add(small);
            turn.Children.Add(tilt);
            cat.View.RenderTransform = turn;
            cat.Dir = 1;
            cat.View.Face(1);
            catX = Math.Round(S.W * 0.24);
            cat.X = catX - CatView.W / 2;
            status = StatusLine(true);
            Reset();
            Banner("Rooftop run", "Space or click to jump. Hold it to jump higher.", 3.6, true);
        }

        // ---------- the sky and the town behind the roofs ----------

        void Back(UIElement e, double left, double top, int z)
        {
            Stage.Place(e, left, top);
            Panel.SetZIndex(e, z);
            S.Add(e);
        }

        static void Cache(UIElement e)
        {
            if (!Scenes.NoCache) e.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        }

        void Scenery()
        {
            skyFill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            skyFill.GradientStops.Add(new GradientStop(Route[0].Top, 0));
            skyFill.GradientStops.Add(new GradientStop(Route[0].Mid, 0.55));
            skyFill.GradientStops.Add(new GradientStop(Route[0].Low, 0.9));
            Back(new Rectangle { Width = S.W, Height = S.H, Fill = skyFill }, 0, 0, -40);

            var dots = new GeometryGroup();
            for (int i = 0; i < 80; i++)
            {
                double r = 0.6 + rng.NextDouble() * 0.9;
                dots.Children.Add(new EllipseGeometry(new Point(rng.NextDouble() * S.W, Stage.Top + rng.NextDouble() * S.H * 0.5), r, r));
            }
            dots.Freeze();
            var starPath = new Path { Data = dots, Fill = Brushes.White, Opacity = 0 };
            Cache(starPath);
            stars = starPath;
            Back(starPath, 0, 0, -39);

            sun = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
            Put(sun, new Ellipse { Width = 260, Height = 260, Fill = new RadialGradientBrush(Color.FromArgb(120, 255, 200, 120), Color.FromArgb(0, 255, 190, 110)) }, -130, -130);
            Put(sun, new Ellipse { Width = 62, Height = 62, Fill = new RadialGradientBrush(Ui.C("#FFF4D2"), Ui.C("#FFC46E")) }, -31, -31);
            Cache(sun);
            Back(sun, 0, 0, -38);

            moon = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
            Put(moon, new Ellipse { Width = 150, Height = 150, Fill = new RadialGradientBrush(Color.FromArgb(60, 255, 241, 196), Color.FromArgb(0, 255, 241, 196)) }, -75, -75);
            var face = new Path { Data = Geometry.Parse("M 0 -30 A 30 30 0 1 0 0 30 A 21 30 0 1 1 0 -30 Z"), Fill = new RadialGradientBrush(Ui.C("#FFF8DC"), Ui.C("#F2E2A8")) };
            Put(moon, face, 6, 0);
            Cache(moon);
            Back(moon, 0, 0, -38);

            farInk = new SolidColorBrush(Route[0].Far);
            nearInk = new SolidColorBrush(Route[0].Near);
            AddStrip(Skyline(farInk, 160, 320, 0.05), 0.1, S.W, Kind.Town, true, -34, 0);
            AddStrip(Cranes(), 0.14, S.W, Kind.Harbor, false, -33, 0);
            AddStrip(Skyline(nearInk, 90, 230, 0.09), 0.25, S.W, Kind.Town, true, -32, 0);
            AddStrip(Hills(), 0.3, S.W, Kind.Garden, false, -31, 0);
            AddStrip(Garlands(), 0.35, S.W, Kind.Festival, false, -30, 0);
            AddStrip(Water(), 0.4, S.W, Kind.Harbor, false, -29, S.Floor - 34);
            AddStrip(Poles(), 0.6, 320, Kind.Train, false, -28, 0);
            AddStrip(Track(), 1, 26, Kind.Train, false, -27, S.Floor + 30);
        }

        void AddStrip(UIElement e, double speed, double period, Kind kind, bool always, int z, double top)
        {
            var shift = new TranslateTransform();
            e.RenderTransform = shift;
            e.IsHitTestVisible = false;
            Cache(e);
            if (!always)
            {
                e.Opacity = 0;
                e.Visibility = Visibility.Collapsed;
            }
            Back(e, 0, top, z);
            strips.Add(new Strip { El = e, Shift = shift, Speed = speed, Period = period, Kind = kind, Always = always });
        }

        // A row of buildings one room wide, twice, with a few pitched roofs, water tanks and lit windows.
        Canvas Skyline(Brush ink, double low, double high, double windowChance)
        {
            var c = new Canvas { Width = S.W * 2, Height = S.H };
            var blocks = new GeometryGroup();
            var lights = new GeometryGroup();
            double x = 0;
            while (x < S.W)
            {
                double bw = Math.Min(S.W - x, 40 + rng.Next(80)), bh = low + rng.NextDouble() * (high - low), top = S.H - bh;
                for (int copy = 0; copy < 2; copy++)
                {
                    double ox = x + copy * S.W;
                    blocks.Children.Add(new RectangleGeometry(new Rect(ox, top, bw, bh)));
                    if (bw > 50 && (int)(bh + x) % 5 == 0)
                        blocks.Children.Add(Poly(ox - 2, top, ox + bw / 2, top - 22, ox + bw + 2, top));          // a pitched roof
                    else if (bw > 44 && (int)(bh + x) % 7 == 1)
                    {
                        blocks.Children.Add(new RectangleGeometry(new Rect(ox + bw * 0.6, top - 16, 14, 13), 3, 3));   // a water tank
                        blocks.Children.Add(new RectangleGeometry(new Rect(ox + bw * 0.6 + 2, top - 4, 2, 4)));
                        blocks.Children.Add(new RectangleGeometry(new Rect(ox + bw * 0.6 + 10, top - 4, 2, 4)));
                    }
                }
                for (double wy = top + 12; wy < S.H - 40; wy += 20)
                    for (double wx = x + 8; wx < x + bw - 10; wx += 14)
                        if (rng.NextDouble() < windowChance)
                            for (int copy = 0; copy < 2; copy++) lights.Children.Add(new RectangleGeometry(new Rect(wx + copy * S.W, wy, 4, 6)));
                x += bw + rng.Next(3) * 6;
            }
            c.Children.Add(new Path { Data = blocks, Fill = ink });
            c.Children.Add(new Path { Data = lights, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 214, 140)) });
            return c;
        }

        static PathGeometry Poly(params double[] xy)
        {
            var f = new PathFigure { StartPoint = new Point(xy[0], xy[1]), IsClosed = true, IsFilled = true };
            for (int i = 2; i + 1 < xy.Length; i += 2) f.Segments.Add(new LineSegment(new Point(xy[i], xy[i + 1]), true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            return g;
        }

        // Container cranes and ships against the sky, for the harbor.
        Canvas Cranes()
        {
            var c = new Canvas { Width = S.W * 2, Height = S.H };
            var steel = new GeometryGroup();
            var lamps = new GeometryGroup();
            double ground = S.Floor - 30;
            for (int copy = 0; copy < 2; copy++)
            {
                double ox = copy * S.W;
                foreach (double f in new[] { 0.12, 0.47, 0.8 })
                {
                    double x = ox + S.W * f, top = ground - 250 - f * 40;
                    steel.Children.Add(new RectangleGeometry(new Rect(x, top, 7, ground - top)));            // legs
                    steel.Children.Add(new RectangleGeometry(new Rect(x + 58, top, 7, ground - top)));
                    steel.Children.Add(Poly(x, top + 40, x + 65, top + 40, x + 7, top + 90, x, top + 90));     // bracing
                    steel.Children.Add(new RectangleGeometry(new Rect(x - 70, top - 4, 230, 9)));            // the boom
                    steel.Children.Add(Poly(x + 4, top - 4, x + 32, top - 40, x + 36, top - 40, x + 62, top - 4));
                    steel.Children.Add(new RectangleGeometry(new Rect(x + 120, top + 5, 2, 70)));            // the cable
                    lamps.Children.Add(new EllipseGeometry(new Point(x + 34, top - 42), 2.5, 2.5));
                }
                foreach (double f in new[] { 0.3, 0.65 })
                {
                    double x = ox + S.W * f;
                    steel.Children.Add(Poly(x, ground - 26, x + 160, ground - 26, x + 145, ground, x + 12, ground));   // a hull
                    steel.Children.Add(new RectangleGeometry(new Rect(x + 96, ground - 50, 40, 24)));
                    steel.Children.Add(new RectangleGeometry(new Rect(x + 112, ground - 66, 8, 16)));
                    for (int i = 0; i < 4; i++) lamps.Children.Add(new RectangleGeometry(new Rect(x + 100 + i * 9, ground - 44, 4, 4)));
                }
            }
            c.Children.Add(new Path { Data = steel, Fill = Ui.B("#16243E") });
            c.Children.Add(new Path { Data = lamps, Fill = Ui.B("#FF8A6A") });
            return c;
        }

        // Rolling hills with trees, dark against the dawn, for the gardens.
        Canvas Hills()
        {
            var c = new Canvas { Width = S.W * 2, Height = S.H };
            var land = new GeometryGroup { FillRule = FillRule.Nonzero };
            double ground = S.Floor - 60;
            for (int copy = 0; copy < 2; copy++)
            {
                double ox = copy * S.W;
                land.Children.Add(new EllipseGeometry(new Point(ox + S.W * 0.2, ground + 60), S.W * 0.34, 110));
                land.Children.Add(new EllipseGeometry(new Point(ox + S.W * 0.72, ground + 70), S.W * 0.4, 120));
                for (int i = 0; i < 9; i++)
                {
                    double x = ox + 40 + i * S.W / 9.0 + (i * 37 % 23), h = 26 + (i * 53 % 30);
                    land.Children.Add(new EllipseGeometry(new Point(x, ground - h * 0.4 - 30 + (i % 3) * 14), h * 0.55, h * 0.7));
                    land.Children.Add(new RectangleGeometry(new Rect(x - 2, ground - 30 + (i % 3) * 14, 4, 40)));
                }
            }
            var fill = new LinearGradientBrush(Ui.C("#5E5E86"), Ui.C("#3E3A62"), 90);
            c.Children.Add(new Path { Data = land, Fill = fill });
            c.Children.Add(new Rectangle { Width = S.W * 2, Height = S.H - ground - 40, Fill = Ui.B("#3E3A62") });
            Canvas.SetTop(c.Children[1], ground + 40);
            return c;
        }

        // Strings of colored lights between far buildings, for the festival.
        Canvas Garlands()
        {
            var c = new Canvas { Width = S.W * 2, Height = S.H };
            var wire = new GeometryGroup();
            string[] colors = { "#FF8A65", "#FFD54F", "#81D4FA", "#F48FB1", "#AED581" };
            var bulbs = new GeometryGroup[colors.Length];
            for (int i = 0; i < bulbs.Length; i++) bulbs[i] = new GeometryGroup();
            for (int copy = 0; copy < 2; copy++)
            {
                double ox = copy * S.W;
                for (int s = 0; s < 4; s++)
                {
                    double x0 = ox + s * S.W / 4.0, x1 = x0 + S.W / 4.0, y0 = S.H - 250 + (s % 2) * 30, y1 = S.H - 230 - (s % 2) * 20, sag = 40;
                    var f = new PathFigure { StartPoint = new Point(x0, y0) };
                    f.Segments.Add(new QuadraticBezierSegment(new Point((x0 + x1) / 2, Math.Max(y0, y1) + sag), new Point(x1, y1), true));
                    var g = new PathGeometry();
                    g.Figures.Add(f);
                    wire.Children.Add(g);
                    for (int i = 1; i < 12; i++)
                    {
                        double t = i / 12.0, mx = (x0 + x1) / 2, my = Math.Max(y0, y1) + sag;
                        double bx = (1 - t) * (1 - t) * x0 + 2 * (1 - t) * t * mx + t * t * x1;
                        double by = (1 - t) * (1 - t) * y0 + 2 * (1 - t) * t * my + t * t * y1;
                        bulbs[(i + s) % colors.Length].Children.Add(new EllipseGeometry(new Point(bx, by + 4), 3, 3.5));
                    }
                }
            }
            c.Children.Add(new Path { Data = wire, Stroke = Ui.B("#141030"), StrokeThickness = 1.2 });
            for (int i = 0; i < colors.Length; i++) c.Children.Add(new Path { Data = bulbs[i], Fill = Ui.B(colors[i]) });
            return c;
        }

        // The water between the stacks at the harbor, with the moon's light on it.
        Canvas Water()
        {
            double h = S.H - (S.Floor - 34);
            var c = new Canvas { Width = S.W * 2, Height = h };
            c.Children.Add(new Rectangle { Width = S.W * 2, Height = h, Fill = new LinearGradientBrush(Ui.C("#22406A"), Ui.C("#0E1C32"), 90) });
            var glints = new GeometryGroup();
            for (int copy = 0; copy < 2; copy++)
                for (int i = 0; i < 46; i++)
                {
                    double x = copy * S.W + (i * 97 % 1000) / 1000.0 * S.W, y = 6 + (i * 31 % 70) / 70.0 * (h - 10);
                    glints.Children.Add(new RectangleGeometry(new Rect(x, y, 10 + i % 4 * 8, 1.5)));
                }
            c.Children.Add(new Path { Data = glints, Fill = new SolidColorBrush(Color.FromArgb(90, 210, 225, 255)) });
            return c;
        }

        // Telegraph poles and wires along the railway: they rush past faster than the town.
        Canvas Poles()
        {
            var c = new Canvas { Width = S.W + 640, Height = S.H };
            var wood = new GeometryGroup();
            var wires = new GeometryGroup();
            double top = S.Floor - 230;
            for (double x = 40; x < S.W + 640; x += 320)
            {
                wood.Children.Add(new RectangleGeometry(new Rect(x, top, 6, S.Floor + 40 - top)));
                wood.Children.Add(new RectangleGeometry(new Rect(x - 22, top + 14, 50, 5)));
                foreach (double ix in new[] { -18.0, -6, 12, 24 }) wood.Children.Add(new RectangleGeometry(new Rect(x + ix, top + 8, 3, 6)));
                foreach (double y in new[] { top + 10, top + 22 })
                {
                    var f = new PathFigure { StartPoint = new Point(x + 3, y) };
                    f.Segments.Add(new QuadraticBezierSegment(new Point(x + 163, y + 22), new Point(x + 323, y), true));
                    var g = new PathGeometry();
                    g.Figures.Add(f);
                    wires.Children.Add(g);
                }
            }
            c.Children.Add(new Path { Data = wires, Stroke = Ui.B("#0C1026"), StrokeThickness = 1.2 });
            c.Children.Add(new Path { Data = wood, Fill = Ui.B("#121630") });
            return c;
        }

        // Rails on gravel under the train.
        Canvas Track()
        {
            double h = Math.Max(10, S.H - (S.Floor + 30));
            var c = new Canvas { Width = S.W + 52, Height = h };
            c.Children.Add(new Rectangle { Width = S.W + 52, Height = h, Fill = new LinearGradientBrush(Ui.C("#2A2A3A"), Ui.C("#161622"), 90) });
            var ties = new GeometryGroup();
            for (double x = 0; x < S.W + 52; x += 26) ties.Children.Add(new RectangleGeometry(new Rect(x, 6, 14, 5)));
            c.Children.Add(new Path { Data = ties, Fill = Ui.B("#3A3030") });
            c.Children.Add(new Rectangle { Width = S.W + 52, Height = 3, Fill = Ui.B("#8A8EA6") });
            Canvas.SetTop(c.Children[2], 4);
            return c;
        }

        // ---------- the journey ----------

        // The part of town at world x, which lap it is, and how far into that part (in meters).
        Leg LegAt(double x, out int lap, out double into, out int index)
        {
            double m = Math.Max(0, (x - catX) / PxPerMeter);
            lap = (int)(m / LapMeters);
            m -= lap * LapMeters;
            index = 0;
            while (index < Route.Length - 1 && m >= Route[index].Meters)
            {
                m -= Route[index].Meters;
                index++;
            }
            into = m;
            return Route[index];
        }

        double LegStartX(int lap, int index)
        {
            double m = lap * LapMeters;
            for (int i = 0; i < index; i++) m += Route[i].Meters;
            return catX + m * PxPerMeter;
        }

        // How fast the cat runs at world x on a good day: a little quicker each lap, never frantic.
        double TargetAt(double x)
        {
            int lap, index;
            double into;
            Leg leg = LegAt(x, out lap, out into, out index);
            if (leg.Kind == Kind.Sky) return leg.Speed;
            return Math.Min(MaxSpeed, leg.Speed + 35 * Math.Min(lap, 4) + 15 * into / leg.Meters);
        }

        // The sky, the far town and the strips of scenery follow the part of town in the middle of the view.
        void Weather()
        {
            int lap, index;
            double into;
            double mid = dist + S.W * 0.5;
            Leg cur = LegAt(mid, out lap, out into, out index);
            Leg prev = lap == 0 && index == 0 ? cur : Route[(index + Route.Length - 1) % Route.Length];
            double k = Smooth(Math.Min(1, into / BlendMeters)), f = into / cur.Meters;
            // Cranes, hills and the like swap sooner than the sky turns, so two places never mix for long.
            double ks = Smooth(Math.Min(1, into / 35));
            skyFill.GradientStops[0].Color = Mix(prev.Top, cur.Top, k);
            skyFill.GradientStops[1].Color = Mix(prev.Mid, cur.Mid, k);
            skyFill.GradientStops[2].Color = Mix(prev.Low, cur.Low, k);
            farInk.Color = Mix(prev.Far, cur.Far, k);
            nearInk.Color = Mix(prev.Near, cur.Near, k);
            double starsNow = prev.Stars + (cur.Stars - prev.Stars) * k;
            if (Math.Abs(stars.Opacity - starsNow) > 0.01) stars.Opacity = starsNow;
            Sky(sun, prev.SunTo + (Lerp(cur.SunFrom, cur.SunTo, f) - prev.SunTo) * k, S.W * 0.7);
            Sky(moon, prev.MoonTo + (Lerp(cur.MoonFrom, cur.MoonTo, f) - prev.MoonTo) * k, S.W * 0.82);
            foreach (Strip s in strips)
            {
                if (s.Always) continue;
                double w = (cur.Kind == s.Kind ? ks : 0) + (prev.Kind == s.Kind ? 1 - ks : 0);
                if (Math.Abs(s.El.Opacity - w) > 0.01) s.El.Opacity = w;
                Visibility v = w > 0.01 ? Visibility.Visible : Visibility.Collapsed;
                if (s.El.Visibility != v) s.El.Visibility = v;
            }
        }

        void Sky(Canvas body, double height, double x)
        {
            double horizon = S.Floor - 60, top = Stage.Top + 70;
            double o = Math.Max(0, Math.Min(1, (height + 0.12) / 0.12));
            if (Math.Abs(body.Opacity - o) > 0.01) body.Opacity = o;
            Visibility v = o > 0.01 ? Visibility.Visible : Visibility.Collapsed;
            if (body.Visibility != v) body.Visibility = v;
            double y = Math.Round(horizon - height * (horizon - top));
            if (Canvas.GetTop(body) != y) Canvas.SetTop(body, y);
            if (Canvas.GetLeft(body) != x) Canvas.SetLeft(body, x);
        }

        static double Lerp(double a, double b, double k) { return a + (b - a) * k; }

        static double Smooth(double k) { return k * k * (3 - 2 * k); }

        static Color Mix(Color a, Color b, double k)
        {
            return Color.FromArgb((byte)Lerp(a.A, b.A, k), (byte)Lerp(a.R, b.R, k), (byte)Lerp(a.G, b.G, k), (byte)Lerp(a.B, b.B, k));
        }

        static Color Shade(Color c, double k)
        {
            // k > 0 lightens toward white, k < 0 darkens toward black.
            return k >= 0 ? Mix(c, Colors.White, k) : Mix(c, Colors.Black, -k);
        }

        // A new part of town under the cat's paws: its name, a heart back, and a postcard from the last one.
        void NewLeg(int serial, Leg leg, int lap, int index, double t)
        {
            bool first = legSerial < 0;
            int previous = legSerial;
            legSerial = serial;
            farthest = Math.Max(farthest, serial);
            if (first) return;
            string sub = lap > 0 && index == 0 ? "Lap " + (lap + 1) + ". A little quicker this time." : leg.Hint;
            if (previous >= 0 && serial > previous)
            {
                int done = previous % Route.Length;
                bonus += 50;
                if ((cards & (1 << done)) == 0)
                {
                    cards |= 1 << done;
                    newCards++;
                    S.SetBest("run-cards", cards);
                    sub += "\nA postcard from " + Route[done].Place + " (" + Count(cards) + " of " + Route.Length + ")";
                }
                if (lives > 0 && lives < MaxLives && mode != Mode.Over)
                {
                    lives++;
                    S.Float("♥ back", catX - 20, S.Floor - lift - 120, Ui.B("#FFB3C1"), 16);
                }
            }
            Sfx.Trill();
            Banner(leg.Name, sub, 3.4, true);
        }

        static int Count(int bits)
        {
            int n = 0;
            for (; bits != 0; bits &= bits - 1) n++;
            return n;
        }

        // ---------- a run ----------

        void Reset()
        {
            foreach (Roof r in roofs) S.Remove(r.El);
            roofs.Clear();
            foreach (Bird b in birds) S.Remove(b.El);
            birds.Clear();
            foreach (Burst b in bursts) S.Remove(b.El);
            bursts.Clear();
            DropBalloon(false);
            if (endCard != null)
            {
                S.Remove(endCard);
                endCard = null;
            }
            mode = Mode.Run;
            zip = null;
            dist = speed = vUp = 0;
            ease = 1;
            air = holding = flagPlaced = flagPassed = false;
            lives = MaxLives;
            fishPts = bonus = fishCount = streak = bestStreak = newCards = 0;
            safeUntil = coyoteUntil = bufferUntil = freeFrom = 0;
            legSerial = balloonSerial = -1;
            farthest = 0;
            waveX = double.NaN;
            statusAt = lookAt = -1;
            nextShoot = nowT + 20;
            nextFirework = nextFlock = 0;
            nextThought = nowT + 40;
            // A little flag on the roofs where the best run ended.
            flagX = bestM > 0 ? catX + bestM * PxPerMeter : -1;
            Generate();
            lift = roofs[0].Top;
            CatBody cat = S.Cat;
            Panel.SetZIndex(cat.View, 5);
            cat.View.Opacity = 1;
            tilt.Angle = 0;
            Weather();
        }

        int Meters { get { return (int)(dist / PxPerMeter); } }
        int Score { get { return Meters + fishPts + bonus; } }
        int Mult { get { return Math.Min(5, 1 + streak / 8); } }

        public override void Frame(double t, double dt)
        {
            nowT = t;
            if (resumed)
            {
                // Back from a pause: a moment to find the rhythm again.
                resumed = false;
                safeUntil = Math.Max(safeUntil, t + 1.2);
                if (mode == Mode.Balloon) Purr(true);
            }
            CatBody cat = S.Cat;
            Generate();
            double x = dist + catX;
            if (mode != Mode.Over)
            {
                int lap, index;
                double into;
                Leg leg = LegAt(x, out lap, out into, out index);
                int serial = lap * Route.Length + index;
                if (serial != legSerial) NewLeg(serial, leg, lap, index, t);
                if (leg.Kind == Kind.Sky && balloonSerial != serial && (mode == Mode.Run || mode == Mode.Zip)) StartBalloon(serial);

                double target = mode == Mode.Balloon ? BalloonSpeed : TargetAt(x) * ease * (mode == Mode.Zip ? 1.3 : 1);
                speed = speed < target ? Math.Min(target, speed + (dist < 100 ? 520 : 240) * dt) : Math.Max(target, speed - 300 * dt);
                ease = Math.Min(1, ease + 0.05 * dt);
                dist += speed * dt;
                Retire();
                x = dist + catX;
                switch (mode)
                {
                    case Mode.Run: Run(x, t, dt); break;
                    case Mode.Balloon: Float(x, t, dt); break;
                    case Mode.Zip: Slide(x, t); break;
                    default: Fallen(x, t, dt); break;
                }
                if (flagPlaced && !flagPassed && mode != Mode.Falling && x >= flagWorld)
                {
                    flagPassed = true;
                    Sfx.Happy();
                    S.Float("past your best!", catX - 50, S.Floor - lift - 120, Gold, 17);
                }
                Ambience(x, t, dt);
            }
            else
                foreach (Bird b in birds) Canvas.SetLeft(b.El, Math.Round(b.X - dist));   // the end of a run can move the view
            Bursts(t);
            Fish next = Fishes(x, t);

            // Whole device pixels, so the cached roofs stay crisp while they scroll.
            foreach (Roof r in roofs) r.Move.X = Math.Round((r.X0 - dist) * dpi) / dpi;
            foreach (Strip s in strips)
                if (s.El.Visibility == Visibility.Visible) s.Shift.X = -Math.Round((dist * s.Speed) % s.Period * dpi) / dpi;
            if (t - lookAt > 0.05)
            {
                lookAt = t;
                Weather();
            }
            cat.Lift = lift;
            cat.Render();
            double angle = mode == Mode.Falling ? 12
                : mode == Mode.Zip ? 9
                : mode == Mode.Balloon ? Math.Sin(t * 1.3) * 4
                : air ? -Math.Max(-14, Math.Min(14, vUp * 0.02)) : 0;
            if (Math.Abs(tilt.Angle - angle) > 0.3) tilt.Angle = angle;
            // Blinks while it is safe after a slip.
            double seen = mode == Mode.Run && t < safeUntil && (int)(t * 12) % 2 == 0 ? 0.35 : 1;
            if (cat.View.Opacity != seen) cat.View.Opacity = seen;
            if (balloon != null)
            {
                balloonMove.X = Math.Round(catX - 2);
                balloonMove.Y = Math.Round(S.Floor - lift - Tall - 4 + Math.Sin(t * 1.7) * 2);
            }
            if (next != null && next.X - x < 260) cat.Look(new Point(next.X - dist, S.Floor - next.Lift));
            else cat.Look(new Point(catX + 240, S.Floor - lift - 30));

            if (t - statusAt > 0.1)
            {
                statusAt = t;
                int lap, index;
                double into;
                Leg leg = LegAt(x, out lap, out into, out index);
                string s = leg.Name + "   ·   " + Meters.ToString("N0") + " m";
                if (streak > 0) s += "   ·   fish streak " + streak + (Mult > 1 ? "  ×" + Mult : "");
                if (status.Text != s) status.Text = s;
            }
        }

        static readonly Brush Pale = Ui.B("#DCE4FF"), Gold = Ui.B("#FFE27A"), Ouch = Ui.B("#FFB3B3");

        void Run(double x, double t, double dt)
        {
            if (air)
            {
                bool floaty = holding && holdFor < HoldTime && vUp > 0;
                if (holding) holdFor += dt;
                vUp -= G * (floaty ? HoldGravity : 1) * dt;
                double before = lift;
                lift += vUp * dt;
                if (vUp <= 0)
                {
                    Pad pad = PadAt(x);
                    if (pad != null && before >= pad.Top && lift <= pad.Top) Bounce(pad);
                    else
                    {
                        double s = Surface(x, before);
                        if (!double.IsNaN(s) && lift <= s) Land(s, x, t);
                    }
                }
                Zip line = ZipAt(x);
                if (air && line != null && x > line.X0 + 6 && x < line.X1 - 80)
                {
                    double head = lift + Tall, y = line.Y(x);
                    if (head > y - 18 && head < y + 14) Grab(line);
                }
            }
            else
            {
                double s = Surface(x, lift);
                if (double.IsNaN(s) || s < lift - 0.5)
                {
                    // Off the edge: a moment's grace to still jump.
                    air = true;
                    vUp = 0;
                    holdFor = HoldTime;
                    coyoteUntil = t + 0.14;
                }
            }
            if (mode != Mode.Run) return;
            Bump(x, t);
            if (mode != Mode.Run) return;
            if (air)
            {
                Roof under = RoofAt(x), ahead = Ahead(x);
                if (under != null && lift < under.Top - 0.5)
                {
                    // Reached a roof just below its edge: claws in, and up. Any lower is the wall.
                    if (lift > under.Top - (vUp > 0 ? ClawRising : Claw)) Land(under.Top, x, t);
                    else Fall(t);
                }
                else if (under == null && t >= coyoteUntil && ahead != null && lift < ahead.Top - Claw && (vUp <= 0 || x + Body >= ahead.X0) && !Caught(x))
                    Fall(t);   // over an alley and already too low for the next roof: it won't make it
            }
            S.Cat.View.SetWalking(!air && mode == Mode.Run);
        }

        // The highest roof or block top under x that is no higher than maxLift (NaN over an alley).
        double Surface(double x, double maxLift)
        {
            double top = double.NaN;
            foreach (Roof r in roofs)
            {
                if (x < r.X0 - Feet || x > r.X1 + Feet) continue;
                if (r.Top <= maxLift + 0.5 && (double.IsNaN(top) || r.Top > top)) top = r.Top;
                foreach (Block b in r.Blocks)
                    if (!b.Overhead && x >= b.X0 - 12 && x <= b.X1 + 12 && b.Top <= maxLift + 0.5 && (double.IsNaN(top) || b.Top > top)) top = b.Top;
            }
            return top;
        }

        Roof RoofAt(double x)
        {
            foreach (Roof r in roofs)
                if (x >= r.X0 - Feet && x <= r.X1 + Feet) return r;
            return null;
        }

        Roof Ahead(double x)
        {
            foreach (Roof r in roofs)
                if (r.X0 - Feet > x) return r;
            return null;
        }

        Pad PadAt(double x)
        {
            foreach (Roof r in roofs)
                if (r.Pad != null && x >= r.Pad.X0 && x <= r.Pad.X1) return r.Pad;
            return null;
        }

        // An awning below and ahead in this street can still catch the cat.
        bool Caught(double x)
        {
            foreach (Roof r in roofs)
                if (r.Pad != null && x <= r.Pad.X1 && x >= r.Pad.X0 - 300 && lift > r.Pad.Top - 4) return true;
            return false;
        }

        Zip ZipAt(double x)
        {
            foreach (Roof r in roofs)
                if (r.Zip != null && x >= r.Zip.X0 && x <= r.Zip.X1) return r.Zip;
            return null;
        }

        void Jump()
        {
            air = true;
            vUp = JumpSpeed;
            holdFor = 0;
            coyoteUntil = bufferUntil = 0;
            S.Cat.View.SetWalking(false);
        }

        void Land(double s, double x, double t)
        {
            lift = s;
            vUp = 0;
            air = false;
            holdFor = HoldTime;
            foreach (Roof r in roofs)
            {
                // Only just made it: the paws are hanging over the edge.
                if (s == r.Top && x >= r.X0 - Feet && x < r.X0 + 6)
                {
                    bonus += 15;
                    S.Float("close one! +15", catX - 40, S.Floor - s - 110, Gold, 16);
                }
                foreach (Block b in r.Blocks)
                    if (!b.Overhead && s == b.Top && x >= b.X0 - 12 && x <= b.X1 + 12) b.Stood = true;
            }
            if (t < bufferUntil) Jump();   // pressed a moment before landing
        }

        void Bounce(Pad pad)
        {
            lift = pad.Top;
            vUp = BounceSpeed;
            holdFor = HoldTime;
            air = true;
            Sfx.Bounce(0.7);
            S.Float(Lines.Pick("boing!", "wheee!", "boing!"), catX - 30, S.Floor - lift - 100, Gold, 17);
        }

        // Chimneys, crates, washing lines and signals: a slip if the cat runs into one, a bonus for a whisker-close jump.
        void Bump(double x, double t)
        {
            foreach (Roof r in roofs)
            {
                if (r.X1 < x - 80 || r.X0 > x + 80) continue;
                foreach (Block b in r.Blocks)
                {
                    if (x + Waist <= b.X0 || x - Waist >= b.X1)
                    {
                        if (!b.Passed && x - Waist >= b.X1)
                        {
                            b.Passed = true;
                            if (!b.Overhead && !b.Hit && !b.Stood && b.Closest < 12)
                            {
                                bonus += 10;
                                S.Float("whisker close! +10", catX - 60, S.Floor - lift - 110, Gold, 16);
                            }
                        }
                        continue;
                    }
                    if (b.Overhead)
                    {
                        if (lift + Tall - Ears > b.Bottom && lift < b.Top) Slip(b, t, r.Kind == Kind.Laundry || r.Kind == Kind.Town ? "tangled!" : "oof!");
                    }
                    else if (!b.Stood)
                    {
                        if (lift < b.Top - Scrape) Slip(b, t, Lines.Pick("oof!", "bonk!", "oof!"));
                        else b.Closest = Math.Min(b.Closest, lift - b.Top);
                    }
                }
            }
        }

        void Slip(Block b, double t, string text)
        {
            if (b.Hit) return;
            b.Hit = true;
            if (t < safeUntil) return;
            lives--;
            streak = 0;
            safeUntil = t + 1.6;
            ease = 0.8;   // slows down for a bit after a slip, so one mistake doesn't lead to the next
            Sfx.Question();
            S.Float(text, catX - 24, S.Floor - lift - 100, Ouch, 18);
            if (lives <= 0) Finish();
        }

        void Fall(double t)
        {
            mode = Mode.Falling;
            lives--;
            streak = 0;
            ease = 0.8;
            fallingSince = t;
            holding = false;
            vUp = Math.Min(vUp, 0);
            Panel.SetZIndex(S.Cat.View, -1);   // down into the alley, behind the next building
            S.Cat.View.SetWalking(false);
            Sfx.Mew();
        }

        void Fallen(double x, double t, double dt)
        {
            if (S.Floor - lift - 70 < S.H)
            {
                vUp -= G * dt;
                lift += vUp * dt;
                return;
            }
            if (lives <= 0)
            {
                Finish();
                return;
            }
            // Back on the next roof with room to land, and a moment of safety.
            Roof r = RoofAt(x);
            if (r == null || x < r.X0 + 30 || r.X1 - x < 0.5 * speed + 120) return;
            if (t - fallingSince < 3)
                foreach (Block b in r.Blocks)
                    if (b.X1 > x - 40 && b.X0 < x + 0.45 * speed + 60) return;
            mode = Mode.Run;
            air = true;
            lift = r.Top + 80;
            vUp = 0;
            holdFor = HoldTime;
            safeUntil = t + 1.8;
            Panel.SetZIndex(S.Cat.View, 5);
            string line = r.Kind == Kind.Harbor ? Lines.Pick("*shakes off the water*", "splash! all fine") : Lines.Pick("*lands on feet*", "still got it", "nine lives");
            S.Float(line, catX - 50, S.Floor - r.Top - 160, Pale, 15);
        }

        // ---------- the balloon and the lantern lines ----------

        void StartBalloon(int serial)
        {
            balloonSerial = serial;
            mode = Mode.Balloon;
            zip = null;
            air = true;
            vUp = 120;
            S.Cat.View.SetWalking(false);
            balloonMove = new TranslateTransform();
            balloon = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false, RenderTransform = balloonMove };
            AddPath(balloon, "M 0 0 C -4 -20 6 -40 0 -62", null, "#E8E2D0", 1.2);
            var skin = new RadialGradientBrush(Ui.C("#FF9AA8"), Ui.C("#D9485E")) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.35), RadiusX = 0.7, RadiusY = 0.7 };
            Put(balloon, new Ellipse { Width = 46, Height = 54, Fill = skin }, -23, -114);
            AddPath(balloon, "M -4 -60 L 4 -60 L 0 -66 Z", "#C23E52", null, 0);
            Put(balloon, new Ellipse { Width = 10, Height = 15, Fill = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), RenderTransform = new RotateTransform(-25) }, -12, -104);
            Cache(balloon);
            Panel.SetZIndex(balloon, 4);
            S.Add(balloon);
            Purr(true);
            S.Float("a balloon!", catX - 30, S.Floor - lift - 120, Gold, 17);
        }

        void Float(double x, double t, double dt)
        {
            // Hold to rise, let go to sink: slow on purpose.
            vUp += (holding ? 420 : -300) * dt;
            vUp = Math.Max(-120, Math.Min(140, vUp));
            lift += vUp * dt;
            if (lift < BalloonLow) { lift = BalloonLow; vUp = Math.Max(0, vUp); }
            if (lift > BalloonHigh) { lift = BalloonHigh; vUp = Math.Min(0, vUp); }
            int lap, index;
            double into;
            Leg leg = LegAt(x, out lap, out into, out index);
            if (leg.Kind == Kind.Sky) return;
            // Past the ride: let go over the next roof and drop onto it.
            Roof r = RoofAt(x);
            if (r == null || r.Kind == Kind.Sky || lift < r.Top + 30) return;
            mode = Mode.Run;
            air = true;
            vUp = 0;
            holdFor = HoldTime;
            DropBalloon(true);
            S.Float("*lets go*", catX - 30, S.Floor - lift - 120, Pale, 15);
        }

        void DropBalloon(bool away)
        {
            Purr(false);
            if (balloon == null) return;
            Canvas b = balloon;
            balloon = null;
            if (!away)
            {
                S.Remove(b);
                return;
            }
            // Off it goes, up into the night.
            bursts.Add(new Burst { El = b, Move = balloonMove, Born = nowT, Life = 3.5, X = balloonMove.X, Y = balloonMove.Y, Vx = -45, Vy = -130, Type = 3 });
        }

        void Purr(bool on)
        {
            if (on == purring) return;
            purring = on;
            if (on) Sfx.PurrOn(0.22f);
            else Sfx.PurrOff();
        }

        void Grab(Zip line)
        {
            mode = Mode.Zip;
            zip = line;
            air = true;
            vUp = 0;
            S.Cat.View.SetWalking(false);
            Sfx.Trill();
            S.Float("wheee!", catX - 20, S.Floor - lift - 90, Gold, 17);
        }

        void Slide(double x, double t)
        {
            lift = zip.Y(x) - Tall + 3;
            if (x < zip.X1 - 40) return;
            LetGo();
        }

        void LetGo()
        {
            mode = Mode.Run;
            zip = null;
            air = true;
            vUp = 60;
            holdFor = HoldTime;
        }

        // ---------- fish ----------

        // Takes the fish the cat touches; a fish left behind ends the streak. Returns the next one ahead.
        Fish Fishes(double x, double t)
        {
            double mid = lift + 28;
            Fish next = null;
            bool catching = mode == Mode.Run || mode == Mode.Balloon || mode == Mode.Zip;
            foreach (Roof r in roofs)
                foreach (Fish f in r.Catch)
                {
                    if (f.Done) continue;
                    if (catching && Math.Abs(f.X - x) < 34 && Math.Abs(f.Lift - mid) < (mode == Mode.Balloon ? 44 : 38)) Take(f, t);
                    else if (f.X < x - 40)
                    {
                        f.Done = true;
                        if (!f.Golden && streak > 0)
                        {
                            if (streak >= 8) S.Float("streak lost", catX - 30, S.Floor - lift - 100, Pale, 14);
                            streak = 0;
                        }
                    }
                    else if (f.X > x && (next == null || f.X < next.X)) next = f;
                }
            return next;
        }

        void Take(Fish f, double t)
        {
            f.Done = true;
            f.El.Visibility = Visibility.Hidden;
            fishCount++;
            streak++;
            bestStreak = Math.Max(bestStreak, streak);
            int pts = (f.Golden ? 40 : 5) * Mult;
            fishPts += pts;
            S.Float("+" + pts, f.X - dist - 10, S.Floor - f.Lift - 46, f.Golden ? Gold : Pale, f.Golden ? 20 : 15);
            if (f.Golden)
            {
                Sfx.Happy();
                S.Float("golden fish!", f.X - dist - 40, S.Floor - f.Lift - 72, Gold, 17);
            }
            else if (t - lastPop > 0.09)
            {
                lastPop = t;
                Sfx.Pop();
            }
            if (streak % 8 == 0)
            {
                S.Float("fish streak ×" + Mult + "!", catX - 50, S.Floor - lift - 130, Gold, 18);
                if (!f.Golden) Sfx.Happy();
            }
        }

        // ---------- life around the roofs ----------

        void Ambience(double x, double t, double dt)
        {
            for (int i = birds.Count - 1; i >= 0; i--)
            {
                Bird b = birds[i];
                if (!b.Flying && b.X - x < 130 && b.X > x - 60)
                {
                    b.Flying = true;
                    b.Since = t;
                    b.Vx = speed * (0.4 + rng.NextDouble() * 0.4);
                    b.Vy = 140 + rng.NextDouble() * 80;
                    b.Sit.Visibility = Visibility.Collapsed;
                    b.Fly.Visibility = Visibility.Visible;
                }
                if (b.Flying)
                {
                    b.X += b.Vx * dt;
                    b.Lift += b.Vy * dt;
                    b.Flap.ScaleY = (int)((t - b.Since) * 10) % 2 == 0 ? 1 : -0.6;
                }
                double sx = b.X - dist, sy = S.Floor - b.Lift;
                if (sx < -60 || sy < -40)
                {
                    S.Remove(b.El);
                    birds.RemoveAt(i);
                    continue;
                }
                Canvas.SetLeft(b.El, Math.Round(sx));
                Canvas.SetTop(b.El, Math.Round(sy));
            }

            int lap, index;
            double into;
            Leg leg = LegAt(dist + S.W * 0.5, out lap, out into, out index);
            if (leg.Stars >= 0.7 && t >= nextShoot)
            {
                nextShoot = t + 22 + rng.NextDouble() * 24;
                ShootingStar(t);
            }
            if (leg.Kind == Kind.Festival && into > 30 && t >= nextFirework)
            {
                nextFirework = t + 1.4 + rng.NextDouble() * 1.8;
                Firework(t);
            }
            if (leg.Kind == Kind.Garden && into > 40 && t >= nextFlock)
            {
                nextFlock = t + 10 + rng.NextDouble() * 8;
                Flock(t);
            }

            // Now and then the cat has a thought.
            if (mode == Mode.Run && !air && t >= nextThought)
            {
                nextThought = t + 35 + rng.NextDouble() * 30;
                string line = leg.Kind == Kind.Harbor ? Lines.Pick("*sniff* fish market nearby", "the boats are asleep")
                    : leg.Kind == Kind.Garden ? Lines.Pick("birds are waking up", "it smells like morning")
                    : leg.Kind == Kind.Festival ? Lines.Pick("ooh, lanterns", "so many lights")
                    : leg.Kind == Kind.Train ? Lines.Pick("*whiskers in the wind*", "next stop: fish")
                    : Lines.Pick("nice view up here", "*happy trot*", "the town's so quiet", "I could do this all night");
                S.Float(line, catX - 40, S.Floor - lift - 112, Pale, 14);
            }
        }

        // Fireworks, shooting stars, flocks and a balloon let go: things that cross the sky and are gone.
        void Bursts(double t)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                Burst b = bursts[i];
                double age = (t - b.Born) / b.Life;
                if (age >= 1)
                {
                    S.Remove(b.El);
                    bursts.RemoveAt(i);
                    continue;
                }
                if (b.Type == 0)
                {
                    double grow = 1 - Math.Pow(1 - age, 3);
                    b.Grow.ScaleX = b.Grow.ScaleY = 0.15 + 0.85 * grow;
                    b.Move.Y = age * age * 26;
                    b.El.Opacity = age < 0.6 ? 1 : 1 - (age - 0.6) / 0.4;
                }
                else
                {
                    b.Move.X = b.X + b.Vx * (t - b.Born);
                    b.Move.Y = b.Y + b.Vy * (t - b.Born);
                    if (b.Type == 1) b.El.Opacity = Math.Sin(age * Math.PI);
                    else if (b.Type == 2) b.Grow.ScaleY = (int)((t - b.Born) * 5) % 2 == 0 ? 1 : 0.5;
                    else b.El.Opacity = 1 - age * age;
                }
            }
        }

        void ShootingStar(double t)
        {
            var streakBrush = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(230, 255, 255, 255), 0);
            var line = new Rectangle { Width = 90, Height = 2, Fill = streakBrush, RadiusX = 1, RadiusY = 1 };
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(new RotateTransform(28));
            group.Children.Add(move);
            line.RenderTransform = group;
            double x0 = S.W * (0.3 + rng.NextDouble() * 0.5), y0 = Stage.Top + 20 + rng.NextDouble() * 80;
            Back(line, 0, 0, -36);
            bursts.Add(new Burst { El = line, Move = move, Born = t, Life = 0.9, X = x0, Y = y0, Vx = 520, Vy = 280, Type = 1 });
            if (mode == Mode.Run && rng.NextDouble() < 0.35) S.Float("*makes a wish*", catX - 40, S.Floor - lift - 112, Pale, 14);
        }

        void Firework(double t)
        {
            string[] colors = { "#FFD54F", "#FF8A65", "#81D4FA", "#F48FB1", "#C5E1A5", "#FFF59D" };
            Brush ink = Ui.B(colors[rng.Next(colors.Length)]);
            Brush ink2 = Ui.B(colors[rng.Next(colors.Length)]);
            var c = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
            var sparks = new GeometryGroup();
            var sparks2 = new GeometryGroup();
            for (int i = 0; i < 18; i++)
            {
                double a = i * Math.PI * 2 / 18;
                sparks.Children.Add(new EllipseGeometry(new Point(Math.Cos(a) * 58, Math.Sin(a) * 58), 2.6, 2.6));
                sparks2.Children.Add(new EllipseGeometry(new Point(Math.Cos(a + 0.17) * 36, Math.Sin(a + 0.17) * 36), 2, 2));
            }
            c.Children.Add(new Ellipse { Width = 150, Height = 150, Fill = new RadialGradientBrush(Color.FromArgb(70, 255, 230, 180), Color.FromArgb(0, 255, 230, 180)), Margin = new Thickness(-75, -75, 0, 0) });
            c.Children.Add(new Path { Data = sparks, Fill = ink });
            c.Children.Add(new Path { Data = sparks2, Fill = ink2 });
            var grow = new ScaleTransform(0.15, 0.15);
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(grow);
            group.Children.Add(move);
            c.RenderTransform = group;
            Back(c, S.W * (0.25 + rng.NextDouble() * 0.7), Stage.Top + 60 + rng.NextDouble() * (S.Floor - Stage.Top - 330), -36);
            bursts.Add(new Burst { El = c, Grow = grow, Move = move, Born = t, Life = 1.5, Type = 0 });
        }

        void Flock(double t)
        {
            var c = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
            var wings = new GeometryGroup();
            for (int i = 0; i < 5; i++)
            {
                double bx = i * 18, by = Math.Abs(i - 2) * 9;
                var f = new PathFigure { StartPoint = new Point(bx - 7, by - 3) };
                f.Segments.Add(new QuadraticBezierSegment(new Point(bx - 3, by - 5), new Point(bx, by), true));
                f.Segments.Add(new QuadraticBezierSegment(new Point(bx + 3, by - 5), new Point(bx + 7, by - 3), true));
                var g = new PathGeometry();
                g.Figures.Add(f);
                wings.Children.Add(g);
            }
            c.Children.Add(new Path { Data = wings, Stroke = Ui.B("#4A3A5A"), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
            var grow = new ScaleTransform(1, 1, 0, 0);
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(grow);
            group.Children.Add(move);
            c.RenderTransform = group;
            Back(c, 0, 0, -35);
            double y = Stage.Top + 60 + rng.NextDouble() * 120;
            bursts.Add(new Burst { El = c, Grow = grow, Move = move, Born = t, Life = 16, X = S.W + 40, Y = y, Vx = -70 - rng.NextDouble() * 30, Vy = -4, Type = 2 });
        }

        Bird AddBird(double x, double top, bool gull)
        {
            var b = new Bird { X = x, Lift = top };
            b.El = new Canvas { Width = 1, Height = 1, IsHitTestVisible = false };
            string body = gull ? "#F2F2F0" : "#9A9AAE", wing = gull ? "#BFC4CE" : "#7C7C92";
            var sit = new Canvas();
            AddPath(sit, "M -12 -6 L -18 -9 L -16 -4 Z", wing, null, 0);
            sit.Children.Add(new Ellipse { Width = 18, Height = 11, Fill = Ui.B(body), Margin = new Thickness(-12, -12, 0, 0) });
            sit.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Ui.B(body), Margin = new Thickness(1, -17, 0, 0) });
            AddPath(sit, "M 8 -13 L 12 -12 L 8 -11 Z", gull ? "#F2B33D" : "#5A4A3A", null, 0);
            AddPath(sit, "M -7 -9 Q -2 -12 3 -8", null, wing, 2.2);
            AddPath(sit, "M -3 -2 L -3 0 M 1 -2 L 1 0", null, "#D07A5A", 1);
            b.Sit = sit;
            b.El.Children.Add(sit);
            var fly = new Canvas { Visibility = Visibility.Collapsed };
            fly.Children.Add(new Ellipse { Width = 16, Height = 8, Fill = Ui.B(body), Margin = new Thickness(-8, -10, 0, 0) });
            var w = AddPath(fly, "M -2 -7 Q -8 -20 -16 -16 M 2 -7 Q 8 -20 16 -16", null, wing, 2.4);
            b.Flap = new ScaleTransform(1, 1, 0, -7);
            w.RenderTransform = b.Flap;
            b.Fly = fly;
            b.El.Children.Add(fly);
            Panel.SetZIndex(b.El, 2);
            S.Add(b.El);
            Canvas.SetLeft(b.El, x - dist);
            Canvas.SetTop(b.El, S.Floor - top);
            birds.Add(b);
            return b;
        }

        // ---------- the roofs ahead ----------

        void Generate()
        {
            while (roofs.Count == 0 || roofs[roofs.Count - 1].X1 < dist + S.W + 300) AddRoof();
        }

        void Retire()
        {
            // Awnings and lantern lines reach back over the street, so keep a roof a while after it passes.
            while (roofs.Count > 1 && roofs[0].X1 < dist - 450)
            {
                S.Remove(roofs[0].El);
                roofs.RemoveAt(0);
            }
        }

        void AddRoof()
        {
            var r = new Roof();
            Roof last = roofs.Count > 0 ? roofs[roofs.Count - 1] : null;
            if (last == null)
            {
                // A long first roof, to find the rhythm on.
                r.Kind = Kind.Town;
                r.X0 = -60;
                r.X1 = catX + S.W * 0.95;
                r.Top = 70;
                Row(r, r.X1 - 320, 4, r.Top);
                freeFrom = r.X1;
            }
            else
            {
                int lap, index;
                double into;
                Leg leg = LegAt(last.X1 + 1, out lap, out into, out index);
                // Gaps are sized for the slowest the cat may be going here (just after a slip), so
                // there is always a way across.
                double v = 0.8 * Math.Max(290, TargetAt(last.X1 + 1));
                r.Kind = leg.Kind;
                switch (leg.Kind)
                {
                    case Kind.Train: Carriage(r, last, v, lap, index); break;
                    case Kind.Harbor: Containers(r, last, v, lap); break;
                    case Kind.Sky: Low(r, last, lap, index); break;
                    default: Building(r, last, v, leg.Kind, lap); break;
                }
            }
            Draw(r);
            roofs.Add(r);
        }

        double Gap(Roof last, double top, double v, double far)
        {
            double min = 46 + 0.08 * v, max = Math.Max(min, far * Reach(v, Math.Max(0, top - last.Top)));
            return min + rng.NextDouble() * (max - min);
        }

        void Building(Roof r, Roof last, double v, Kind kind, int lap)
        {
            double swing = kind == Kind.Town ? (lap == 0 ? 50 : 110) : kind == Kind.Market ? 80 : kind == Kind.Garden ? 100 : 150;
            double rise = (rng.NextDouble() - 0.45) * swing;
            r.Top = Math.Max(40, Math.Min(200, last.Top + rise));
            double far = Math.Min(0.68, (kind == Kind.Town ? 0.42 : kind == Kind.Laundry || kind == Kind.Garden ? 0.5 : 0.56) + 0.05 * lap);
            if (last.Kind == Kind.Sky || last.Kind == Kind.Train) far = 0.45;
            if (kind == Kind.Market && last.Kind == Kind.Market && rng.NextDouble() < 0.65)
            {
                // A street with an awning: wide enough to need it, the awning catches a fall.
                r.Top = Math.Max(50, Math.Min(170, last.Top + (rng.NextDouble() - 0.6) * 60));
                double street = Math.Max(Gap(last, r.Top, v, 0.6), 170 + rng.NextDouble() * 80);
                r.X0 = last.X1 + street;
                r.Pad = new Pad { X0 = last.X1 + street * 0.22, X1 = r.X0 - 90, Top = Math.Min(last.Top, r.Top) - 70 };
                // Fish along the bounce, a hint and a reward.
                double t0 = 0.1;
                for (int i = 0; i < 5; i++)
                {
                    double tt = t0 + i * 0.1, h = BounceSpeed * tt - 0.5 * G * tt * tt;
                    double fx = (r.Pad.X0 + r.Pad.X1) / 2 + v * 1.1 * tt;
                    if (fx > r.X0 + 120) break;
                    r.Catch.Add(new Fish { X = fx, Lift = r.Pad.Top + h + 28 });
                }
            }
            else if (kind == Kind.Festival && last.ZipOut)
            {
                // A lantern line from the last roof down onto this one, over the alley.
                r.Top = Math.Max(40, Math.Min(last.Top + 10, last.Top + rise));
                r.X0 = last.X1 + Gap(last, r.Top, v, far);
                r.Zip = new Zip { X0 = last.X1 - 170, Y0 = last.Top + 150, X1 = r.X0 + 150, Y1 = r.Top + 104 };
                for (double fx = r.Zip.X0 + 60; fx < r.Zip.X1 - 50; fx += 42)
                    r.Catch.Add(new Fish { X = fx, Lift = r.Zip.Y(fx) - Tall + 31 });
            }
            else r.X0 = last.X1 + Gap(last, r.Top, v, far);
            r.X1 = r.X0 + Math.Max(260, v * (kind == Kind.Town && lap == 0 ? 1.6 + rng.NextDouble() : 1.3 + rng.NextDouble() * 1.4));
            if (kind == Kind.Festival && rng.NextDouble() < 0.55) r.ZipOut = true;
            if (rng.NextDouble() < 0.55 && r.Pad == null && r.Zip == null)
                Arc(r, last.X1 - 0.08 * v, last.Top, v, true, r.X0 + 30);   // fish along a good jump over the alley
            Obstacles(r, v, kind, lap);
            if ((kind == Kind.Town || kind == Kind.Laundry || kind == Kind.Garden) && rng.NextDouble() < 0.4) Perch(r, false);
        }

        // Somewhere free on the roof for a bird to sit.
        void Perch(Roof r, bool gull)
        {
            for (int tries = 0; tries < 6; tries++)
            {
                double x = r.X0 + 60 + rng.NextDouble() * Math.Max(1, r.X1 - r.X0 - 120);
                bool free = true;
                foreach (Block b in r.Blocks)
                    if (x > b.X0 - 30 && x < b.X1 + 30) free = false;
                if (!free) continue;
                AddBird(x, r.Top, gull);
                return;
            }
        }

        // Rows of chimneys and the like, with room to land before the first one, and after the last
        // a held jump over it still comes down with time to spare before the next alley.
        void Obstacles(Roof r, double v, Kind kind, int lap)
        {
            double x = Math.Max(r.X0 + 0.75 * v + 60, r.Zip != null ? r.Zip.X1 + 0.6 * v : 0);
            double end = r.X1 - (r.ZipOut ? 240 : 0.8 * v + 60);
            double spacing = Spacing(v, lap);
            bool easy = lives <= 1;
            while (x < end)
            {
                double roll = rng.NextDouble();
                if ((kind == Kind.Laundry && roll < 0.32 || kind == Kind.Town && lap > 0 && roll < 0.12) && end - x > 240)
                {
                    double w = 170 + rng.NextDouble() * 60;
                    r.Blocks.Add(new Block { X0 = x, X1 = x + w, Bottom = r.Top + 92, Top = r.Top + 138, Overhead = true });
                    Row(r, x + 20, (int)(w / 40), r.Top);   // stay low and they're yours
                    x += w + spacing;
                }
                else if (roll < (kind == Kind.Town && lap == 0 ? 0.62 : 0.8))
                {
                    bool tallOk = !easy && !(kind == Kind.Town && lap == 0);
                    bool tall = tallOk && rng.NextDouble() < (kind == Kind.Garden || lap == 0 ? 0.3 : 0.4);
                    bool twin = !easy && !tall && (kind != Kind.Town || lap > 0) && rng.NextDouble() < (lap == 0 ? 0.2 : 0.28);
                    double cw = kind == Kind.Market ? 54 + rng.Next(14) : kind == Kind.Garden ? 46 + rng.Next(20) : 34 + rng.Next(11);
                    double h = tall ? (kind == Kind.Garden ? 74 : 88) : (kind == Kind.Market ? 34 : 38), span = cw;
                    r.Blocks.Add(new Block { X0 = x, X1 = x + cw, Top = r.Top + h });
                    if (twin)
                    {
                        r.Blocks.Add(new Block { X0 = x + cw + 64, X1 = x + 2 * cw + 64, Top = r.Top + h });
                        span = 2 * cw + 64;
                    }
                    double roll2 = rng.NextDouble();
                    bool held = tall || twin;
                    if (held && roll2 < 0.18)
                        r.Catch.Add(new Fish { X = x + span / 2, Lift = r.Top + 182, Golden = true });   // only a full jump at the right moment
                    else if (roll2 < 0.55)
                        Arc(r, x + span / 2 - v * (held ? HeldApex : JumpSpeed / G), r.Top, v, held, x + span + 0.4 * v);
                    x += span + spacing * (0.9 + rng.NextDouble() * 0.5);
                }
                else
                {
                    Row(r, x, 3 + rng.Next(2), r.Top);
                    x += 150 + spacing * 0.4;
                }
            }
        }

        // Room from one obstacle to the next: generous on the first lap, a little less after. Down to
        // the last heart or two it quietly gets roomier again, so one slip doesn't lead to the next.
        double Spacing(double v, int lap)
        {
            double k = 1.3 - 0.07 * Math.Min(lap, 3);
            if (lives <= 1) k *= 1.5;
            else if (lives == 2) k *= 1.15;
            return v * k;
        }

        // The night train: carriages close enough together to run across, with crates on the roofs
        // and signals overhead.
        void Carriage(Roof r, Roof last, double v, int lap, int index)
        {
            r.Top = 64;
            if (last.Kind == Kind.Train) r.X0 = last.X1 + 26;
            else r.X0 = Math.Max(last.X1 + Gap(last, r.Top, v, 0.45), LegStartX(lap, index) - 20);
            r.X1 = r.X0 + 340 + rng.Next(5) * 24;
            if (last.Kind != Kind.Train) freeFrom = r.X0 + 0.75 * v + 60;
            double spacing = Spacing(v, lap);
            double x = Math.Max(freeFrom, r.X0 + 40);
            while (x < r.X1 - 90)
            {
                double roll = rng.NextDouble();
                if (roll < 0.3 && x + 120 < r.X1 - 20)
                {
                    r.Blocks.Add(new Block { X0 = x, X1 = x + 26, Bottom = r.Top + 92, Top = r.Top + 150, Overhead = true });
                    x += 26 + spacing;
                }
                else if (roll < 0.8)
                {
                    bool tall = lap > 0 && lives > 1 && rng.NextDouble() < 0.35;
                    double w = 40 + rng.Next(12);
                    r.Blocks.Add(new Block { X0 = x, X1 = x + w, Top = r.Top + (tall ? 72 : 36) });
                    if (rng.NextDouble() < 0.5) Arc(r, x + w / 2 - v * (tall ? HeldApex : JumpSpeed / G), r.Top, v, tall, x + w + 0.4 * v);
                    x += w + spacing * (0.9 + rng.NextDouble() * 0.5);
                }
                else
                {
                    Row(r, x, 3, r.Top);
                    x += 110 + spacing * 0.4;
                }
            }
            freeFrom = x;
        }

        // The harbor: stacks of shipping containers over the water, one to four high.
        void Containers(Roof r, Roof last, double v, int lap)
        {
            int high = Math.Max(1, Math.Min(4, (int)Math.Round((last.Top - 40) / 34) + rng.Next(5) - 2));
            if (last.Kind != Kind.Harbor) high = Math.Max(1, Math.Min(3, (int)Math.Round((last.Top - 40) / 34)));
            r.Top = 40 + 34 * high;
            r.X0 = last.X1 + Gap(last, r.Top, v, Math.Min(0.62, 0.52 + 0.04 * lap));
            r.X1 = r.X0 + 118 * (3 + rng.Next(3));
            double x = r.X0 + 0.75 * v + 60, end = r.X1 - 0.8 * v - 60;
            double spacing = Spacing(v, lap);
            while (x < end)
            {
                double roll = rng.NextDouble();
                if (roll < 0.25 && end - x > 60)
                {
                    r.Blocks.Add(new Block { X0 = x, X1 = x + 30, Bottom = r.Top + 94, Top = r.Top + 160, Overhead = true });   // a crane hook
                    x += 30 + spacing;
                }
                else if (roll < 0.75)
                {
                    bool box = rng.NextDouble() < 0.4 && end - x > 118;
                    double w = box ? 118 : 40 + rng.Next(10), h = box ? 34 : 34;
                    r.Blocks.Add(new Block { X0 = x, X1 = x + w, Top = r.Top + h });
                    if (!box && rng.NextDouble() < 0.5) Arc(r, x + w / 2 - v * JumpSpeed / G, r.Top, v, false, x + w + 0.4 * v);
                    if (box) Row(r, x + 14, 3, r.Top + h);
                    x += w + spacing * (0.9 + rng.NextDouble() * 0.5);
                }
                else
                {
                    Row(r, x, 3, r.Top);
                    x += 110 + spacing * 0.4;
                }
            }
            if (rng.NextDouble() < 0.45) Perch(r, true);
        }

        // Under the balloon: low roofs far below, and fish in slow waves up in the air.
        void Low(Roof r, Roof last, int lap, int index)
        {
            r.Top = 40 + rng.NextDouble() * 60;
            r.X0 = last.X1 + 40 + rng.NextDouble() * 120;
            double end = LegStartX(lap, index + 1);
            r.X1 = Math.Min(r.X0 + 220 + rng.NextDouble() * 320, end - 40);
            if (r.X1 < r.X0 + 120) r.X1 = r.X0 + 120;
            double start = LegStartX(lap, index), v = Route[index].Speed;
            if (double.IsNaN(waveX) || waveX < start) waveX = start + v * 1.5;
            // One wave every ten seconds: up for four, down for six, like a slow breath. A golden fish at each top.
            while (waveX < r.X1 && waveX < end - v)
            {
                double p = ((waveX - start - v * 1.5) / (v * 10)) % 1;
                double y = p < 0.4 ? -Math.Cos(Math.PI * p / 0.4) : Math.Cos(Math.PI * (p - 0.4) / 0.6);
                bool top = p >= 0.38 && p < 0.42;
                r.Catch.Add(new Fish { X = waveX, Lift = 213 + 68 * y + 28, Golden = top });
                waveX += v * 0.4;
            }
        }

        static readonly double HeldApex = HoldTime + (JumpSpeed - G * HoldGravity * HoldTime) / G;

        // Height of a jump t seconds after leaving the ground, held all the way or just tapped.
        static double Height(double t, bool held)
        {
            if (!held || t <= HoldTime)
            {
                double g = held ? G * HoldGravity : G;
                return JumpSpeed * t - 0.5 * g * t * t;
            }
            double h1 = Height(HoldTime, true), v1 = JumpSpeed - G * HoldGravity * HoldTime, s = t - HoldTime;
            return h1 + v1 * s - 0.5 * G * s * s;
        }

        // How far a full held jump carries at speed v before it comes back down to dh above where it left.
        static double Reach(double v, double dh)
        {
            double v1 = JumpSpeed - G * HoldGravity * HoldTime;
            double apex = Height(HoldTime, true) + v1 * v1 / (2 * G);
            if (dh >= apex) return 0;
            return v * (HeldApex + Math.Sqrt(2 * (apex - dh) / G));
        }

        void Row(Roof r, double x, int n, double top)
        {
            for (int i = 0; i < n; i++) r.Catch.Add(new Fish { X = x + i * 36, Lift = top + 28 });
        }

        // Fish along the path of a jump that leaves the ground at takeoff.
        void Arc(Roof r, double takeoff, double from, double v, bool held, double stopX)
        {
            for (int i = 1; i <= 5; i++)
            {
                double t = i * 0.12, h = Height(t, held), x = takeoff + v * t;
                if (h < 10 || x > stopX) break;
                r.Catch.Add(new Fish { X = x, Lift = from + h + 28 });
            }
        }

        // ---------- drawing the roofs ----------

        static readonly Brush Bricks = MakeBricks();

        static Brush MakeBricks()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Ui.B("#9A5A48"), null, new RectangleGeometry(new Rect(0, 0, 18, 10))));
            g.Children.Add(new GeometryDrawing(Ui.B("#A86655"), null, new RectangleGeometry(new Rect(10, 6, 7, 3.5))));
            g.Children.Add(new GeometryDrawing(Ui.B("#8C503F"), null, new RectangleGeometry(new Rect(1, 1, 6, 3.5))));
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(18, 0.5)));
            lines.Children.Add(new LineGeometry(new Point(0, 5.5), new Point(18, 5.5)));
            lines.Children.Add(new LineGeometry(new Point(8.5, 0.5), new Point(8.5, 5.5)));
            lines.Children.Add(new LineGeometry(new Point(0.5, 5.5), new Point(0.5, 10)));
            g.Children.Add(new GeometryDrawing(null, new Pen(Ui.B("#6E3A2F"), 1), lines));
            var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 18, 10), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze();
            return b;
        }

        static readonly Dictionary<Color, Brush> Corrugated = new Dictionary<Color, Brush>();

        // Container steel: ribs every few pixels.
        static Brush Ribs(Color color)
        {
            Brush b;
            if (Corrugated.TryGetValue(color, out b)) return b;
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, 7, 10))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Shade(color, -0.22)), null, new RectangleGeometry(new Rect(5, 0, 2, 10))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Shade(color, 0.12)), null, new RectangleGeometry(new Rect(0, 0, 1.2, 10))));
            var tile = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 7, 10), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            tile.Freeze();
            Corrugated[color] = tile;
            return tile;
        }

        static Brush Wash(Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, 90);
            b.Freeze();
            return b;
        }

        // One roof (with what stands on it and what hangs over the street before it) as a single
        // canvas that slides left. Its origin is the roof's left end at roof height. It never
        // changes while it is on screen (except fish being caught), so it is drawn once and cached.
        void Draw(Roof r)
        {
            int lap, index;
            double into;
            Leg leg = LegAt(r.X0 + 1, out lap, out into, out index);
            double w = r.X1 - r.X0, y = S.Floor - r.Top, deep = S.H - y + 10;
            var c = new Canvas { Width = w, Height = deep, IsHitTestVisible = false };
            if (r.Pad != null) Awning(c, r);
            switch (r.Kind)
            {
                case Kind.Train: Train(c, r, w); break;
                case Kind.Harbor: Stack(c, r, w, deep); break;
                default: House(c, r, w, deep, leg); break;
            }
            if (r.Zip != null) Lanterns(c, r);
            foreach (Block b in r.Blocks)
            {
                double bx = b.X0 - r.X0, bw = b.X1 - b.X0, h = b.Top - r.Top;
                if (b.Overhead)
                {
                    if (r.Kind == Kind.Train) Signal(c, bx, bw, b.Bottom - r.Top);
                    else if (r.Kind == Kind.Harbor) Hook(c, bx, bw, b.Bottom - r.Top);
                    else Laundry(c, bx, bw, b.Top - r.Top, b.Bottom - r.Top);
                }
                else if (r.Kind == Kind.Train || r.Kind == Kind.Harbor) Crate(c, bx, bw, h, r.Kind == Kind.Harbor && bw > 100);
                else if (r.Kind == Kind.Market) FruitCrate(c, bx, bw, h);
                else if (r.Kind == Kind.Garden) Planter(c, bx, bw, h);
                else Chimney(c, bx, bw, h);
            }
            if (flagX >= 0 && !flagPlaced && flagX < r.X1 && r.Kind != Kind.Sky)
            {
                flagPlaced = true;
                flagWorld = Math.Max(flagX, r.X0 + 12);
                double fx = flagWorld - r.X0;
                Put(c, new Rectangle { Width = 2.5, Height = 54, Fill = Ui.B("#E8E2D0") }, fx, -54);
                AddPath(c, "M 0 0 L 28 7 L 0 14 Z", "#F2A04E", null, 0).RenderTransform = new TranslateTransform(fx + 2.5, -54);
                Put(c, Ui.Text("best", 11, Pale), fx + 6, -38);
            }
            foreach (Fish f in r.Catch) f.El = Put(c, FishShape(f.Golden), f.X - r.X0 - 16, r.Top - f.Lift - 9);
            Cache(c);
            r.Move = new TranslateTransform(r.X0 - dist, 0);
            c.RenderTransform = r.Move;
            Stage.Place(c, 0, y);
            S.Add(c);
            r.El = c;
        }

        // A house: a wall lit from the sky above, rows of windows (some lit, a few with the TV on),
        // a drainpipe, and a ledge along the top.
        void House(Canvas c, Roof r, double w, double deep, Leg leg)
        {
            Color wall = Shade(leg.Wall, (rng.NextDouble() - 0.5) * 0.14);
            if (r.Kind == Kind.Sky) wall = Shade(leg.Wall, -0.15);
            Put(c, new Rectangle { Width = w, Height = deep, Fill = Wash(Shade(wall, 0.06), Shade(wall, -0.35)) }, 0, 0);
            var lit = new GeometryGroup();
            var tv = new GeometryGroup();
            var dark = new GeometryGroup();
            var frames = new GeometryGroup();
            var sills = new GeometryGroup();
            double ww = 14, wh = 20, step = 38;
            double margin = (w - Math.Floor((w - 24) / step) * step) / 2;
            for (double wy = 26; wy < deep - 34; wy += 64)
                for (double wx = margin; wx < w - 20; wx += step)
                {
                    if ((int)(wx / step + wy) % 7 == 3) continue;   // a bit of bare wall
                    var pane = new Rect(wx, wy, ww, wh);
                    double roll = rng.NextDouble();
                    if (roll < leg.Lit) lit.Children.Add(new RectangleGeometry(pane));
                    else if (roll < leg.Lit + 0.025) tv.Children.Add(new RectangleGeometry(pane));
                    else dark.Children.Add(new RectangleGeometry(pane));
                    frames.Children.Add(new RectangleGeometry(new Rect(wx + ww / 2 - 0.6, wy, 1.2, wh)));
                    frames.Children.Add(new RectangleGeometry(new Rect(wx, wy + wh * 0.42, ww, 1.2)));
                    sills.Children.Add(new RectangleGeometry(new Rect(wx - 2, wy + wh, ww + 4, 3)));
                }
            bool day = leg.Kind == Kind.Garden;
            c.Children.Add(new Path { Data = dark, Fill = day ? Wash(Ui.C("#C9B4CE"), Ui.C("#8E7CA0")) : Wash(Shade(wall, -0.5), Shade(wall, -0.62)) });
            c.Children.Add(new Path { Data = lit, Fill = Wash(Ui.C("#FFE9B0"), Ui.C("#F2B565")) });
            c.Children.Add(new Path { Data = tv, Fill = Wash(Ui.C("#BFD8FF"), Ui.C("#7FA6E8")) });
            c.Children.Add(new Path { Data = frames, Fill = new SolidColorBrush(Shade(wall, -0.3)) });
            c.Children.Add(new Path { Data = sills, Fill = new SolidColorBrush(Shade(wall, 0.18)) });
            if (w > 300 && rng.NextDouble() < 0.6)
            {
                double px = rng.NextDouble() < 0.5 ? 6 : w - 12;
                Put(c, new Rectangle { Width = 5, Height = deep, Fill = new SolidColorBrush(Shade(wall, -0.25)) }, px, 2);
                for (double by = 30; by < deep; by += 70) Put(c, new Rectangle { Width = 9, Height = 3, Fill = new SolidColorBrush(Shade(wall, -0.4)) }, px - 2, by);
            }
            // The ledge: a lighter cap, a highlight where the sky catches it, a shadow under it.
            Put(c, new Rectangle { Width = w, Height = 10, Fill = Wash(Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) }, 0, 3);
            Put(c, new Rectangle { Width = w + 10, Height = 9, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Shade(wall, 0.12)) }, -5, -6);
            Put(c, new Rectangle { Width = w + 8, Height = 1.5, Fill = new SolidColorBrush(Shade(wall, 0.4)) }, -4, -6);
            if (r.Kind == Kind.Festival)
            {
                // Little lights along the ledge.
                var bulbs = new GeometryGroup();
                for (double bx = 8; bx < w - 4; bx += 18) bulbs.Children.Add(new EllipseGeometry(new Point(bx, 8 + 3 * Math.Sin(bx * 0.17)), 2.2, 2.6));
                c.Children.Add(new Path { Data = bulbs, Fill = Ui.B("#FFD98A") });
            }
            if (r.Kind == Kind.Garden)
            {
                // Ivy down the wall and flowers in a window box or two.
                for (double vx = 10 + rng.Next(40); vx < w - 30; vx += 140 + rng.Next(120))
                {
                    double len = 40 + rng.Next(70);
                    AddPath(c, string.Format(Inv, "M {0} 0 Q {1} {2} {3} {4}", vx, vx - 10, len * 0.5, vx + 4, len), null, "#4E7A4E", 3);
                    for (double ly = 8; ly < len; ly += 12) Put(c, new Ellipse { Width = 7, Height = 5, Fill = Ui.B(ly % 24 < 12 ? "#5E9A5A" : "#4E8A4E") }, vx - 4 + Math.Sin(ly) * 3, ly);
                }
            }
        }

        static void Chimney(Canvas c, double x, double w, double h)
        {
            Put(c, new Rectangle { Width = w, Height = h, Fill = Bricks }, x, -h);
            Put(c, new Rectangle { Width = w * 0.38, Height = h, Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)) }, x + w * 0.62, -h);
            Put(c, new Rectangle { Width = w + 8, Height = 7, RadiusX = 1.5, RadiusY = 1.5, Fill = Ui.B("#7A4A3E") }, x - 4, -h - 5);
            Put(c, new Rectangle { Width = w + 8, Height = 1.5, Fill = Ui.B("#9A6656") }, x - 4, -h - 5);
            Put(c, new Rectangle { Width = 8, Height = 6, Fill = Ui.B("#B0705A") }, x + w * 0.2, -h - 10);
            Put(c, new Rectangle { Width = w + 4, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }, x - 2, -2);
        }

        static void Crate(Canvas c, double x, double w, double h, bool container)
        {
            if (container)
            {
                Color steel = Ui.C(((int)x % 3) == 0 ? "#3E6A9A" : ((int)x % 3) == 1 ? "#B5483B" : "#4E8A5A");
                Put(c, new Rectangle { Width = w, Height = h, Fill = Ribs(steel) }, x, -h);
                Put(c, new Rectangle { Width = w, Height = 2, Fill = new SolidColorBrush(Shade(steel, 0.3)) }, x, -h);
                return;
            }
            Put(c, new Rectangle { Width = w, Height = h, Fill = Wash(Ui.C("#B8895A"), Ui.C("#8E6440")), Stroke = Ui.B("#6E4A2E"), StrokeThickness = 1 }, x, -h);
            AddPath(c, string.Format(Inv, "M {0} {1} L {2} {3} M {0} {3} L {2} {1}", x + 3, -h + 3, x + w - 3, -3), null, "#7E5634", 2);
            Put(c, new Rectangle { Width = w + 4, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }, x - 2, -2);
        }

        static void FruitCrate(Canvas c, double x, double w, double h)
        {
            Put(c, new Rectangle { Width = w, Height = h, Fill = Wash(Ui.C("#C79A62"), Ui.C("#9A7046")), Stroke = Ui.B("#6E4A2E"), StrokeThickness = 1 }, x, -h);
            Put(c, new Rectangle { Width = w, Height = 1.5, Fill = Ui.B("#6E4A2E") }, x, -h * 0.5);
            string[] fruit = { "#F2A04E", "#E5484D", "#F7D154", "#8BC34A" };
            string color = fruit[(int)(x / 7) % fruit.Length];
            for (double fx = x + 6; fx < x + w - 6; fx += 9)
                Put(c, new Ellipse { Width = 10, Height = 9, Fill = Ui.B(color) }, fx - 1, -h - 6 + ((int)fx % 2) * 2);
            Put(c, new Rectangle { Width = w + 4, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }, x - 2, -2);
        }

        static void Planter(Canvas c, double x, double w, double h)
        {
            double box = Math.Min(24, h * 0.55);
            Put(c, new Rectangle { Width = w, Height = box, Fill = Wash(Ui.C("#C27A4E"), Ui.C("#9A5A38")), RadiusX = 2, RadiusY = 2 }, x, -box);
            Put(c, new Rectangle { Width = w + 4, Height = 4, Fill = Ui.B("#D08A5E"), RadiusX = 1.5, RadiusY = 1.5 }, x - 2, -box - 2);
            double green = h - box;
            var bush = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int i = 0; i < 4; i++)
            {
                double bx = x + w * (0.2 + 0.2 * i);
                bush.Children.Add(new EllipseGeometry(new Point(bx, -box - green * 0.45), w * 0.2 + 4, green * 0.55 + 2));
            }
            c.Children.Add(new Path { Data = bush, Fill = Wash(Ui.C("#6FB06A"), Ui.C("#3E7A44")) });
            if (h < 50)
                for (double fx = x + 7; fx < x + w - 4; fx += 11)
                    Put(c, new Ellipse { Width = 6, Height = 6, Fill = Ui.B(((int)fx % 3) == 0 ? "#FF8FB1" : "#FFE27A") }, fx, -h + 2 + ((int)fx % 2) * 4);
            Put(c, new Rectangle { Width = w + 4, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }, x - 2, -2);
        }

        static void Laundry(Canvas c, double x, double w, double line, double hang)
        {
            foreach (double px in new[] { x - 10, x + w + 7 })
            {
                Put(c, new Rectangle { Width = 3, Height = line + 6, Fill = Ui.B("#8C8FA8") }, px, -line - 6);
                Put(c, new Rectangle { Width = 13, Height = 2.5, Fill = Ui.B("#8C8FA8") }, px - 5, -line - 6);
            }
            AddPath(c, string.Format(Inv, "M {0} {1} Q {2} {3} {4} {1}", x - 8, -line - 4, x + w / 2, -line + 12, x + w + 8), null, "#C9CCE0", 1.4);
            string[] colors = { "#F28B82", "#FFF1C4", "#8EC5FF", "#B5E3A1", "#F7B8D8" };
            int n = Math.Max(2, (int)(w / 46));
            for (int i = 0; i < n; i++)
            {
                // Pegged to the sagging line, hanging down to where the cat would bump into it.
                double f = (i + 0.5) / n, ix = x + f * w - 12, top = -line - 4 + 32 * f * (1 - f), drop = -hang - top;
                string color = colors[(i + (int)w) % colors.Length];
                if (i % 3 == 0)   // a little shirt
                    AddPath(c, string.Format(Inv, "M {0} {1} L {2} {1} L {3} {4} L {5} {4} L {5} {6} L {7} {6} L {7} {4} L {8} {4} Z",
                        ix, top, ix + 24, ix + 28, top + 9, ix + 21, top + drop, ix + 3, ix - 4), color, "#55000000", 1);
                else if (i % 3 == 1)   // a towel with a stripe
                {
                    Put(c, new Rectangle { Width = 22, Height = drop, Fill = Ui.B(color), Stroke = Ui.B("#55000000"), StrokeThickness = 1 }, ix + 1, top);
                    Put(c, new Rectangle { Width = 22, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)) }, ix + 1, top + drop - 8);
                }
                else   // a sock
                    AddPath(c, string.Format(Inv, "M {0} {1} L {2} {1} L {2} {3} L {4} {5} L {4} {6} L {0} {6} Z",
                        ix + 6, top, ix + 16, top + drop - 8, ix + 22, top + drop - 2, top + drop), color, "#55000000", 1);
                Put(c, new Rectangle { Width = 3, Height = 5, Fill = Ui.B("#C9A27A") }, ix + 10, top - 3);
            }
        }

        // A railway signal on its post beside the track, the lamp hanging over the train at head height.
        static void Signal(Canvas c, double x, double w, double bottom)
        {
            Put(c, new Rectangle { Width = 7, Height = bottom + 44, Fill = Wash(Ui.C("#3A3E56"), Ui.C("#22263A")) }, x + w + 16, -bottom - 44);
            Put(c, new Rectangle { Width = w + 36, Height = 7, Fill = Ui.B("#2E3248") }, x - 6, -bottom - 44);
            Put(c, new Rectangle { Width = 4, Height = 10, Fill = Ui.B("#2E3248") }, x + w / 2 - 2, -bottom - 40);
            Put(c, new Rectangle { Width = w, Height = 34, RadiusX = 5, RadiusY = 5, Fill = Ui.B("#1C1E2C") }, x, -bottom - 34);
            Put(c, new Ellipse { Width = 34, Height = 34, Fill = new RadialGradientBrush(Color.FromArgb(150, 255, 90, 80), Color.FromArgb(0, 255, 90, 80)) }, x + w / 2 - 17, -bottom - 30);
            Put(c, new Ellipse { Width = 10, Height = 10, Fill = Ui.B("#FF6A5A") }, x + w / 2 - 5, -bottom - 18);
        }

        // A crane's hook hanging on its cable, low enough to bump into.
        static void Hook(Canvas c, double x, double w, double bottom)
        {
            Put(c, new Rectangle { Width = 2, Height = 400, Fill = Ui.B("#3A4258") }, x + w / 2 - 1, -bottom - 400 + 10);
            Put(c, new Rectangle { Width = w, Height = 16, RadiusX = 3, RadiusY = 3, Fill = Ui.B("#E0B03A") }, x, -bottom - 16);
            AddPath(c, string.Format(Inv, "M {0} {1} q 0 10 -7 10 q -7 0 -7 -6", x + w / 2, -bottom), null, "#8C8FA0", 3);
        }

        // A striped market canopy strung across the street, sagging a little, with a scalloped edge.
        void Awning(Canvas c, Roof r)
        {
            Pad p = r.Pad;
            Roof last = roofs.Count > 0 ? roofs[roofs.Count - 1] : null;
            // Drawn just over the part that bounces (it stops short of the wall, so a cat bounced near the
            // far side still clears the building's edge), and tied to both walls with ropes.
            double x0 = p.X0 - r.X0 - 6, x1 = p.X1 - r.X0 + 6, y = r.Top - p.Top;   // y: below the roof line
            double wall = last != null ? last.X1 - r.X0 : x0 - 30;
            Color a = Ui.C(rng.NextDouble() < 0.5 ? "#D9474C" : "#3E8E6A"), b = Ui.C("#FFF1DC");
            int stripes = Math.Max(6, (int)((x1 - x0) / 20));
            double sw = (x1 - x0) / stripes;
            AddPath(c, string.Format(Inv, "M {0} {1} L {2} {3} M {4} {1} L {5} {3}", x0 + 2, y - 9, wall, y - 46, x1 - 2, 0), null, "#3A3040", 1.4);
            var light = new GeometryGroup();
            var deep = new GeometryGroup();
            for (int i = 0; i < stripes; i++)
            {
                double sx = x0 + i * sw;
                double ya = Sag(sx, x0, x1, y), yb = Sag(sx + sw, x0, x1, y);
                var stripe = Poly(sx, ya, sx + sw, yb, sx + sw, yb + 22, sx, ya + 22);
                (i % 2 == 0 ? deep : light).Children.Add(stripe);
                // The scalloped hem under each stripe.
                (i % 2 == 0 ? deep : light).Children.Add(new EllipseGeometry(new Point(sx + sw / 2, (ya + yb) / 2 + 22), sw / 2, 5.5));
            }
            c.Children.Add(new Path { Data = deep, Fill = Wash(Shade(a, 0.1), Shade(a, -0.2)) });
            c.Children.Add(new Path { Data = light, Fill = Wash(b, Shade(b, -0.12)) });
            // Its shadow on the street below.
            Put(c, new Rectangle { Width = x1 - x0, Height = 26, Fill = Wash(Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) }, x0, y + 30);
        }

        static double Sag(double x, double x0, double x1, double y)
        {
            double f = (x - x0) / (x1 - x0);
            return y - 10 + 14 * 4 * f * (1 - f);
        }

        // Two poles and the lantern line between them, from the last roof down onto this one.
        void Lanterns(Canvas c, Roof r)
        {
            Zip z = r.Zip;
            double ax = z.X0 - r.X0, ay = r.Top - z.Y0, bx = z.X1 - r.X0, by = r.Top - z.Y1;
            Roof last = roofs.Count > 0 ? roofs[roofs.Count - 1] : null;
            double lastSurface = last != null ? r.Top - last.Top : 0;
            Put(c, new Rectangle { Width = 5, Height = lastSurface - ay + 40, Fill = Ui.B("#5A4636") }, ax - 2.5, ay - 40);
            Put(c, new Rectangle { Width = 5, Height = -by + 40, Fill = Ui.B("#5A4636") }, bx - 2.5, by - 40);
            // A higher string with paper lanterns, and the line itself (the one to grab).
            AddPath(c, string.Format(Inv, "M {0} {1} Q {2} {3} {4} {5}", ax, ay - 36, (ax + bx) / 2, (ay + by) / 2 - 10, bx, by - 36), null, "#2A2238", 1.2);
            string[] colors = { "#FF7A5A", "#FFB04E", "#FF5A7A" };
            int n = Math.Max(3, (int)((bx - ax) / 70));
            for (int i = 1; i < n; i++)
            {
                double t = (double)i / n, lx = ax + (bx - ax) * t;
                double ly = (1 - t) * (1 - t) * (ay - 36) + 2 * (1 - t) * t * ((ay + by) / 2 - 10) + t * t * (by - 36);
                Put(c, new Ellipse { Width = 40, Height = 40, Fill = new RadialGradientBrush(Color.FromArgb(90, 255, 180, 110), Color.FromArgb(0, 255, 180, 110)) }, lx - 20, ly - 12);
                Put(c, new Ellipse { Width = 15, Height = 19, Fill = Wash(Ui.C(colors[i % 3]), Shade(Ui.C(colors[i % 3]), -0.3)) }, lx - 7.5, ly);
                Put(c, new Rectangle { Width = 7, Height = 2.5, Fill = Ui.B("#3A2A20") }, lx - 3.5, ly - 1);
            }
            AddPath(c, string.Format(Inv, "M {0} {1} L {2} {3}", ax, ay, bx, by), null, "#E8D8B8", 2.2);
        }

        // A train carriage: a rounded roof, a row of lit windows, and the wheels underneath.
        static void Train(Canvas c, Roof r, double w)
        {
            string[] bodies = { "#7E2F35", "#2F5E4E", "#2F3F6E" };
            Color body = Ui.C(bodies[(int)(r.X0 / 400) % bodies.Length]);
            // The wheels sit on the rail of the track strip (34 px under the floor line; the roof is 64 above it).
            Put(c, new Rectangle { Width = w, Height = 72, Fill = Wash(Shade(body, 0.08), Shade(body, -0.3)) }, 0, 4);
            Put(c, new Rectangle { Width = w + 4, Height = 10, RadiusX = 5, RadiusY = 5, Fill = Wash(Ui.C("#8A8EA0"), Ui.C("#5A5E72")) }, -2, -4);
            Put(c, new Rectangle { Width = w, Height = 3, Fill = Ui.B("#E8D8B8") }, 0, 50);
            var panes = new GeometryGroup();
            for (double x = 22; x < w - 40; x += 40) panes.Children.Add(new RectangleGeometry(new Rect(x, 16, 26, 24), 3, 3));
            c.Children.Add(new Path { Data = panes, Fill = Wash(Ui.C("#FFE9B0"), Ui.C("#E8A858")), Stroke = new SolidColorBrush(Shade(body, -0.45)), StrokeThickness = 1.5 });
            Put(c, new Rectangle { Width = 16, Height = 48, Fill = new SolidColorBrush(Shade(body, -0.2)), Stroke = new SolidColorBrush(Shade(body, -0.45)), StrokeThickness = 1 }, w - 30, 14);
            Put(c, new Rectangle { Width = w - 20, Height = 12, Fill = Ui.B("#1A1A24") }, 10, 74);
            var wheels = new GeometryGroup();
            foreach (double x in new[] { 34.0, 62, w - 62, w - 34 }) wheels.Children.Add(new EllipseGeometry(new Point(x, 88), 10, 10));
            c.Children.Add(new Path { Data = wheels, Fill = Ui.B("#2E2E3A"), Stroke = Ui.B("#5A5A6A"), StrokeThickness = 2 });
            Put(c, new Rectangle { Width = 14, Height = 6, Fill = Ui.B("#1A1A24") }, -10, 68);
        }

        // Containers stacked on the quay, and the quay wall going down into the water.
        void Stack(Canvas c, Roof r, double w, double deep)
        {
            double quay = r.Top - 40;   // the quay's top, below the stack
            string[] steel = { "#3E6A9A", "#B5483B", "#4E8A5A", "#D98A3A", "#7A7E8E", "#8E3E6A" };
            for (double row = 0; row < quay - 0.5; row += 34)
                for (double x = 0; x < w - 1; x += 118)
                {
                    Color s = Ui.C(steel[rng.Next(steel.Length)]);
                    double cw = Math.Min(116, w - x - 2);
                    Put(c, new Rectangle { Width = cw, Height = 33, Fill = Ribs(s) }, x + 1, row);
                    Put(c, new Rectangle { Width = cw, Height = 2, Fill = new SolidColorBrush(Shade(s, 0.35)) }, x + 1, row);
                    Put(c, new Rectangle { Width = cw, Height = 4, Fill = new SolidColorBrush(Shade(s, -0.4)) }, x + 1, row + 29);
                    if (rng.NextDouble() < 0.5)
                    {
                        Put(c, new Rectangle { Width = 2, Height = 28, Fill = new SolidColorBrush(Shade(s, -0.45)) }, x + cw - 14, row + 2);
                        Put(c, new Rectangle { Width = 2, Height = 28, Fill = new SolidColorBrush(Shade(s, -0.45)) }, x + cw - 7, row + 2);
                    }
                }
            Put(c, new Rectangle { Width = w + 20, Height = deep - quay, Fill = Wash(Ui.C("#4A5468"), Ui.C("#262C3A")) }, -10, quay);
            Put(c, new Rectangle { Width = w + 20, Height = 4, Fill = Ui.B("#6A7488") }, -10, quay);
            for (double x = 10; x < w; x += 140) Put(c, new Rectangle { Width = 12, Height = 9, RadiusX = 4, RadiusY = 4, Fill = Ui.B("#2A2E3A") }, x, quay - 8);
            // Tires hung on the quay wall as fenders.
            for (double x = 40; x < w - 20; x += 180) Put(c, new Ellipse { Width = 22, Height = 22, Stroke = Ui.B("#1A1A22"), StrokeThickness = 6 }, x, quay + 16);
        }

        Canvas FishShape(bool golden)
        {
            var fish = new Canvas { Width = 32, Height = 18, IsHitTestVisible = false };
            if (golden) Put(fish, new Ellipse { Width = 60, Height = 46, Fill = halo }, -14, -14);
            string body = golden ? "#FFD54A" : "#7FC8F8", belly = golden ? "#FFF0A8" : "#C9E8FF", fin = golden ? "#F2B33D" : "#5AAAE0";
            AddPath(fish, "M 22 9 L 32 1 L 30 9 L 32 17 Z", fin, null, 0);
            AddPath(fish, "M 1 9 C 4 1 18 0 25 9 C 18 18 4 17 1 9 Z", body, "#30000000", 1);
            AddPath(fish, "M 4 10 C 9 15 17 15 22 10 Z", belly, null, 0);
            AddPath(fish, "M 10 3 L 15 0 L 17 4 Z", fin, null, 0);
            Put(fish, new Ellipse { Width = 3.5, Height = 3.5, Fill = Ui.Ink }, 5, 6);
            Put(fish, new Ellipse { Width = 1.4, Height = 1.4, Fill = Brushes.White }, 5.6, 6.4);
            return fish;
        }

        // ---------- input, pausing and the end ----------

        void Press()
        {
            if (mode == Mode.Over)
            {
                if (nowT > overAt + 0.9) Reset();
                return;
            }
            holding = true;
            if (mode == Mode.Zip)
            {
                // Jumping off the line on purpose.
                LetGo();
                Jump();
                return;
            }
            if (mode != Mode.Run) return;
            if (!air || nowT < coyoteUntil) Jump();
            else bufferUntil = nowT + 0.16;
        }

        void Release()
        {
            holding = false;
            holdFor = HoldTime;   // one long jump per press: letting go ends it
        }

        public override void Down(Point p) { Press(); }

        public override void Up(Point p) { Release(); }

        public override bool OnKey(Key key, bool down)
        {
            if (key != Key.Space && key != Key.Up && key != Key.W) return false;
            if (!down)
            {
                keyHeld = false;
                Release();
            }
            else if (!keyHeld)   // held keys repeat; one jump per press
            {
                keyHeld = true;
                Press();
            }
            return true;
        }

        public override void Paused(bool on)
        {
            // A key let go while the window was in the background never arrives.
            keyHeld = false;
            Release();
            if (on)
            {
                Purr(false);
                S.Cat.View.SetWalking(false);
            }
            else resumed = true;
        }

        void Finish()
        {
            bool fell = mode == Mode.Falling;
            mode = Mode.Over;
            overAt = nowT;
            holding = false;
            zip = null;
            DropBalloon(true);
            // Back on its feet for the end of the run, even after a fall.
            double x = dist + catX;
            double s = fell ? double.NaN : Surface(x, lift + 0.5);
            if (double.IsNaN(s))
            {
                Roof r = RoofAt(x) ?? Ahead(x);
                if (r != null && x < r.X0 + 20) dist += r.X0 + 40 - x;   // the next frame fills in the roofs ahead
                s = r != null ? r.Top : 70;
            }
            lift = s;
            air = false;
            vUp = 0;
            CatBody cat = S.Cat;
            Panel.SetZIndex(cat.View, 5);
            cat.View.Opacity = 1;
            cat.View.SetWalking(false);
            tilt.Angle = 0;

            int score = Score, meters = Meters;
            bool record = S.SetBest("run", score);
            if (S.SetBest("run-m", meters)) bestM = meters;
            best = Math.Max(best, score);
            string far = farthest >= Route.Length ? "round town " + (farthest / Route.Length == 1 ? "once" : farthest / Route.Length + " times") + ", then " + Route[farthest % Route.Length].Place
                : "as far as " + Route[farthest].Place;
            endCard = EndCard("Ran " + meters.ToString("N0") + " m",
                record ? "Score " + score.ToString("N0") + "   ·   new best! *proud tail*" : "Score " + score.ToString("N0") + "   ·   best " + best.ToString("N0"),
                fishCount + " fish   ·   " + far + "\npostcards " + Count(cards) + " of " + Route.Length + (newCards > 0 ? " (" + newCards + " new)" : ""),
                "Run again", "or press Space", delegate { Reset(); });
            if (record) Sfx.Happy();
        }

        public override void Stop()
        {
            base.Stop();
            Purr(false);
            CatView view = S.Cat.View;
            view.ClearValue(UIElement.RenderTransformProperty);
            view.ClearValue(Panel.ZIndexProperty);
            view.ClearValue(UIElement.OpacityProperty);
        }

        public override string ScoreText
        {
            get
            {
                int score = Score;
                string lifeText = mode == Mode.Over ? "" : Hearts(lives, MaxLives) + "    ";
                return "Score " + score.ToString("N0") + "    " + lifeText + "Best " + Math.Max(best, score).ToString("N0");
            }
        }

        public override string Summary
        {
            get
            {
                if (Meters < 10) return null;
                string far = farthest >= Route.Length ? ", all the way round town and on to " + Route[farthest % Route.Length].Place
                    : farthest > 0 ? ", all the way to " + Route[farthest].Place : " over the rooftops";
                return "I ran " + Meters.ToString("N0") + " m" + far + (fishCount > 0 ? " and caught " + fishCount + " fish" : "") + ". *pant pant*";
            }
        }
    }
}

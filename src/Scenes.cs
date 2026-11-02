using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    // The rooms the play window can be in. Everything is drawn with shapes; a few things move
    // (clouds, butterflies, fireflies, a few stars, lamps) with slow, low-frame-rate animations.
    // Rooms follow the local time of day (see Phase); the rooftop is always at night.
    //
    // A room is drawn once: every run of still things between two moving ones becomes a single
    // cached bitmap (see Bake), so a frame only redraws what actually moves.
    public static class Scenes
    {
        public static readonly string[] Ids = { "living", "garden", "night", "cafe" };
        public static readonly string[] Names = { "Living room", "Garden", "Rooftop at night", "Little café" };
        static readonly Random Rng = new Random();

        // Set to draw a room at a chosen hour (e.g. for pictures) instead of the clock's.
        public static int? HourOverride;

        // Test runs only: draw everything every frame, to measure what the caching saves.
        public static bool NoCache;

        public static string Phase()
        {
            int hour = HourOverride.HasValue ? HourOverride.Value : DateTime.Now.Hour;
            hour = ((hour % 24) + 24) % 24;
            if (hour >= 6 && hour < 10) return "morning";
            if (hour >= 10 && hour < 17) return "day";
            if (hour >= 17 && hour < 21) return "evening";
            return "night";
        }

        public static string Name(string id)
        {
            int i = Array.IndexOf(Ids, id);
            return i < 0 ? Names[0] : Names[i];
        }

        public static double FloorHeight(string id)
        {
            switch (id)
            {
                case "garden": return 62;
                case "night": return 56;
                case "cafe": return 54;
                default: return 48;
            }
        }

        // Rooms that are dark enough that hints should be written in a light color.
        public static bool IsDark(string id)
        {
            return id == "night" || (id == "garden" && Phase() == "night");
        }

        public static Canvas Build(string id, double w, double h)
        {
            var c = new Canvas { Width = w, Height = h, ClipToBounds = true };
            string phase = Draw(c, id, w, h);
            if (id == "night") return c;

            // A window left open across 17:00 should see the sunset, so check once a minute and
            // redraw only when the phase actually changes. The timer runs only while the room is shown.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += delegate
            {
                if (Phase() == phase) return;
                c.Children.Clear();
                phase = Draw(c, id, w, h);
            };
            c.Loaded += delegate { timer.Start(); };
            c.Unloaded += delegate { timer.Stop(); };
            return c;
        }

        static string Draw(Canvas c, string id, double w, double h)
        {
            string phase = Phase();
            double floor = h - FloorHeight(id);
            switch (id)
            {
                case "garden": Garden(c, w, h, floor, phase); break;
                case "night": Night(c, w, h, floor); break;
                case "cafe": Cafe(c, w, h, floor, phase); break;
                default: Living(c, w, h, floor, phase); break;
            }
            Bake(c, w, h);
            return phase;
        }

        static readonly object LiveTag = new object();

        // Marks something that moves, so it stays out of the cached bitmaps.
        static T Live<T>(T e) where T : FrameworkElement
        {
            e.Tag = LiveTag;
            return e;
        }

        // The still parts are grouped, in drawing order, into canvases that are drawn once and kept
        // as bitmaps; the moving parts stay as they are between them.
        static void Bake(Canvas c, double w, double h)
        {
            if (NoCache) return;
            var items = new List<UIElement>();
            if (c.Background != null)
            {
                items.Add(new Rectangle { Width = w, Height = h, Fill = c.Background });
                c.Background = null;
            }
            foreach (UIElement e in c.Children) items.Add(e);
            c.Children.Clear();
            Canvas group = null;
            foreach (UIElement e in items)
            {
                var fe = e as FrameworkElement;
                if (fe != null && fe.Tag == LiveTag)
                {
                    c.Children.Add(e);
                    group = null;
                    continue;
                }
                if (group == null)
                {
                    group = new Canvas { Width = w, Height = h, ClipToBounds = true, CacheMode = new BitmapCache { SnapsToDevicePixels = true } };
                    c.Children.Add(group);
                }
                group.Children.Add(e);
            }
        }

        static T Pick<T>(string phase, T morning, T day, T evening, T night)
        {
            switch (phase)
            {
                case "morning": return morning;
                case "evening": return evening;
                case "night": return night;
                default: return day;
            }
        }

        // ---------- drawing helpers ----------

        static T Put<T>(Canvas c, T e, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
            return e;
        }

        static Rectangle Box(Canvas c, double x, double y, double w, double h, string color, double radius)
        {
            return Box(c, x, y, w, h, Ui.B(color), radius);
        }

        static Rectangle Box(Canvas c, double x, double y, double w, double h, Brush fill, double radius)
        {
            return Put(c, new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, RadiusX = radius, RadiusY = radius }, x, y);
        }

        static Ellipse Oval(Canvas c, double x, double y, double w, double h, Brush fill)
        {
            return Put(c, new Ellipse { Width = w, Height = h, Fill = fill }, x, y);
        }

        static Path Shape(Canvas c, string data, string fill, string stroke, double thickness)
        {
            return Shape(c, data, fill == null ? null : Ui.B(fill), stroke, thickness);
        }

        static Path Shape(Canvas c, string data, Brush fill, string stroke, double thickness)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke == null ? null : Ui.B(stroke),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            c.Children.Add(p);
            return p;
        }

        static Path Geo(Canvas c, Geometry g, Brush fill)
        {
            var p = new Path { Data = g, Fill = fill };
            c.Children.Add(p);
            return p;
        }

        static string F(string format, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
        }

        static Brush Vertical(string top, string bottom)
        {
            var b = new LinearGradientBrush(Ui.C(top), Ui.C(bottom), 90);
            b.Freeze();
            return b;
        }

        static Brush Vertical(string top, string middle, string bottom)
        {
            var stops = new GradientStopCollection { new GradientStop(Ui.C(top), 0), new GradientStop(Ui.C(middle), 0.55), new GradientStop(Ui.C(bottom), 1) };
            var b = new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1));
            b.Freeze();
            return b;
        }

        static Brush Across(string left, string right)
        {
            var b = new LinearGradientBrush(Ui.C(left), Ui.C(right), 0);
            b.Freeze();
            return b;
        }

        // Clear above 'from' and solid below 'to' (fractions of the height): a wash that spares the sky.
        static Brush Fade(string color, double from, double to)
        {
            Color solid = Ui.C(color), clear = Color.FromArgb(0, solid.R, solid.G, solid.B);
            from = Math.Max(0, Math.Min(1, from));
            to = Math.Max(from, Math.Min(1, to));
            var b = new LinearGradientBrush(new GradientStopCollection { new GradientStop(clear, from), new GradientStop(solid, to) }, new Point(0, 0), new Point(0, 1));
            b.Freeze();
            return b;
        }

        static Brush Glow(Color color, byte alpha)
        {
            var b = new RadialGradientBrush(Color.FromArgb(alpha, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
            b.Freeze();
            return b;
        }

        // The soft dark patch where something stands on the floor.
        static void Contact(Canvas c, double cx, double y, double w, double h, byte alpha)
        {
            Oval(c, cx - w / 2, y - h / 2, w, h, Glow(Color.FromRgb(60, 30, 10), alpha));
        }

        // A see-through wash over the room. Cats and games live on a layer above the room, so it never dims them.
        static Rectangle Tint(Canvas c, double w, double h, Brush fill, double opacity)
        {
            return Put(c, new Rectangle { Width = w, Height = h, Fill = fill, Opacity = opacity, IsHitTestVisible = false }, 0, 0);
        }

        // A pointed leaf, its base at (x, y), leaning by angle degrees, with a lighter midrib.
        static void Leaf(Canvas c, double x, double y, double length, double width, double angle, string color, string rib)
        {
            var leaf = new Canvas();
            var body = new Path
            {
                Data = Geometry.Parse(F("M 0 0 C {0} {1} {2} {3} 0 {4} C {5} {3} {6} {1} 0 0 Z", width, -length * 0.25, width * 0.8, -length * 0.75, -length, -width * 0.8, -width)),
                Fill = new LinearGradientBrush(Ui.C(color), Shade(Ui.C(color), -0.25), 0)
            };
            leaf.Children.Add(body);
            leaf.Children.Add(new Path { Data = Geometry.Parse(F("M 0 -2 L 0 {0}", -length * 0.85)), Stroke = Ui.B(rib), StrokeThickness = 1.1, Opacity = 0.7 });
            leaf.RenderTransform = new RotateTransform(angle);
            Put(c, leaf, x, y);
        }

        static Color Shade(Color c, double k)
        {
            Color to = k >= 0 ? Colors.White : Colors.Black;
            double a = Math.Abs(k);
            return Color.FromArgb(c.A, (byte)(c.R + (to.R - c.R) * a), (byte)(c.G + (to.G - c.G) * a), (byte)(c.B + (to.B - c.B) * a));
        }

        static void Loop(Animatable target, DependencyProperty prop, double from, double to, double seconds, bool reverse, double delay)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = reverse,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(delay)
            };
            if (reverse) a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            Timeline.SetDesiredFrameRate(a, 20);
            target.BeginAnimation(prop, a);
        }

        static void LoopOpacity(UIElement target, double from, double to, double seconds, double delay)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(delay)
            };
            Timeline.SetDesiredFrameRate(a, 15);
            target.BeginAnimation(UIElement.OpacityProperty, a);
        }

        static void Cached(UIElement e)
        {
            if (!NoCache) e.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        }

        // A cloud: puffs in one shape (so overlaps aren't brighter), lit from above, a soft grey underside.
        static Canvas Cloud(double scale, string top, string bottom, double opacity)
        {
            var g = new Canvas { Width = 130 * scale, Height = 56 * scale };
            var puffs = new GeometryGroup { FillRule = FillRule.Nonzero };
            puffs.Children.Add(new EllipseGeometry(new Point(32 * scale, 36 * scale), 30 * scale, 17 * scale));
            puffs.Children.Add(new EllipseGeometry(new Point(56 * scale, 24 * scale), 26 * scale, 22 * scale));
            puffs.Children.Add(new EllipseGeometry(new Point(84 * scale, 28 * scale), 24 * scale, 19 * scale));
            puffs.Children.Add(new EllipseGeometry(new Point(104 * scale, 38 * scale), 24 * scale, 14 * scale));
            puffs.Children.Add(new RectangleGeometry(new Rect(28 * scale, 36 * scale, 80 * scale, 16 * scale), 8 * scale, 8 * scale));
            g.Children.Add(new Path { Data = puffs, Fill = Vertical(top, top, bottom) });
            g.Opacity = opacity;
            return g;
        }

        // Most stars hold still at their own brightness (and are cached); only 'twinkling' of them move.
        static void Stars(Canvas c, double x, double y, double w, double h, int count, int twinkling)
        {
            var still = new GeometryGroup();
            for (int i = 0; i < count; i++)
            {
                double r = 0.6 + Rng.NextDouble() * 1.1;
                still.Children.Add(new EllipseGeometry(new Point(x + Rng.NextDouble() * w, y + Rng.NextDouble() * h), r, r));
            }
            Geo(c, still, new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)));
            for (int i = 0; i < twinkling; i++)
            {
                double size = 2 + Rng.NextDouble() * 1.6;
                var star = Live(Oval(c, x + Rng.NextDouble() * w, y + Rng.NextDouble() * h, size, size, Brushes.White));
                LoopOpacity(star, 0.2, 1, 1.5 + Rng.NextDouble() * 2.5, -Rng.NextDouble() * 3);
            }
        }

        // Centered on (x, y); r = 32 is the rooftop's moon, the other rooms use smaller ones.
        static void Moon(Canvas c, double x, double y, double r, byte halo)
        {
            double g = r * 3.125;
            Oval(c, x - g, y - g, g * 2, g * 2, Glow(Ui.C("#FFF2C8"), halo));
            var face = new RadialGradientBrush(Ui.C("#FFF8DC"), Ui.C("#EEDDA6")) { GradientOrigin = new Point(0.35, 0.35) };
            Oval(c, x - r, y - r, r * 2, r * 2, face);
            Brush crater = new SolidColorBrush(Color.FromArgb(70, 190, 170, 110));
            Oval(c, x - r * 0.45, y - r * 0.5, r * 0.4, r * 0.32, crater);
            Oval(c, x + r * 0.15, y + r * 0.1, r * 0.3, r * 0.26, crater);
            Oval(c, x - r * 0.2, y + r * 0.45, r * 0.18, r * 0.15, crater);
        }

        static void Drift(Canvas c, FrameworkElement e, double y, double from, double to, double seconds)
        {
            var t = new TranslateTransform();
            e.RenderTransform = t;
            Cached(e);
            Put(c, Live(e), 0, y);
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
            a.BeginTime = TimeSpan.FromSeconds(-Rng.NextDouble() * seconds);
            Timeline.SetDesiredFrameRate(a, 20);
            t.BeginAnimation(TranslateTransform.XProperty, a);
        }

        // A floor seen from a little above: seams that open out toward the viewer.
        static void Boards(Canvas c, double w, double h, double floor, string light, string dark, string seam, double spacing)
        {
            Box(c, 0, floor, w, h - floor, Vertical(dark, light), 0);
            var lines = new GeometryGroup();
            double cx = w / 2;
            for (double x = -w * 0.3; x < w * 1.3; x += spacing)
            {
                double bottom = cx + (x - cx) * 1.35;
                lines.Children.Add(new LineGeometry(new Point(x, floor), new Point(bottom, h)));
            }
            // A few board ends, staggered.
            for (double x = -w * 0.3 + spacing * 0.4; x < w * 1.3; x += spacing * 2.3)
            {
                double y = floor + (h - floor) * (0.35 + (x * 7 % 30) / 100);
                double f = (y - floor) / (h - floor);
                double a = x + (cx + (x - cx) * 1.35 - x) * f;
                lines.Children.Add(new LineGeometry(new Point(a, y), new Point(a + spacing * (1 + 0.35 * f), y)));
            }
            c.Children.Add(new Path { Data = lines, Stroke = Ui.B(seam), StrokeThickness = 1.2, Opacity = 0.55 });
            // The back of the floor sits in the wall's shadow.
            Box(c, 0, floor, w, 10, Vertical("#40000000", "#00000000"), 0);
        }

        // ---------- rooms ----------

        static void Living(Canvas c, double w, double h, double floor, string phase)
        {
            c.Background = Vertical("#FFF6EA", "#FFE7CC");
            // Wallpaper: a faint stripe with a small sprig.
            var paper = new DrawingGroup();
            paper.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new RectangleGeometry(new Rect(0, 0, 18, 46))));
            paper.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(30, 210, 150, 110)), null, new EllipseGeometry(new Point(33, 23), 2, 3.4)));
            var tile = new DrawingBrush(paper) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 46, 46), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None, Viewbox = new Rect(0, 0, 46, 46), ViewboxUnits = BrushMappingMode.Absolute };
            tile.Freeze();
            Box(c, 0, 0, w, floor, tile, 0);
            // Light comes from the window: the wall is a touch brighter on that side.
            Box(c, 0, 0, w, floor, Across("#22FFFFFF", "#00FFFFFF"), 0);

            Boards(c, w, h, floor, "#E2B37C", "#C99560", "#9E6C3E", 92);
            // Skirting board.
            Box(c, 0, floor - 14, w, 14, Vertical("#FFF8EE", "#E9D3B6"), 0);
            Box(c, 0, floor - 15, w, 1.5, "#FFFFFF", 0);

            // Window onto the sky outside, with a sill, deep frame and gathered curtains.
            double wx = w * 0.07, wy = h * 0.14;
            var sky = new Canvas { Width = 190, Height = 140, ClipToBounds = true };
            WindowSky(sky, phase);
            Put(c, Live(sky), wx, wy);
            Put(c, new Border { Width = 190, Height = 140, BorderBrush = Ui.B("#F3DFC4"), BorderThickness = new Thickness(8), CornerRadius = new CornerRadius(6) }, wx, wy);
            Box(c, wx + 8, wy + 8, 174, 6, Vertical("#33000000", "#00000000"), 0);   // the frame's shadow on the glass
            Box(c, wx + 92, wy, 6, 140, "#F3DFC4", 0);
            Box(c, wx, wy + 67, 190, 6, "#F3DFC4", 0);
            Box(c, wx - 12, wy + 136, 214, 10, Vertical("#FFF6EA", "#E3C8A6"), 3);
            Box(c, wx - 8, wy + 146, 206, 8, Vertical("#30000000", "#00000000"), 0);
            if (phase == "day" || phase == "morning")
            {
                // Sunlight through the window, falling across the floor.
                string d = F("M {0} {1} L {2} {1} L {3} {4} L {5} {4} Z", wx + 30, floor + 2, wx + 180, wx + 330, h, wx + 110);
                Shape(c, d, new LinearGradientBrush(Color.FromArgb(70, 255, 250, 225), Color.FromArgb(10, 255, 250, 225), 90), null, 0);
            }
            else if (phase == "evening")
            {
                string d = F("M {0} {1} L {2} {1} L {3} {4} L {5} {4} Z", wx + 70, floor + 2, wx + 200, wx + 420, h, wx + 220);
                Shape(c, d, new LinearGradientBrush(Color.FromArgb(60, 255, 180, 110), Color.FromArgb(0, 255, 180, 110), 90), null, 0);
            }
            Box(c, wx - 30, wy - 16, 250, 7, Vertical("#C49A72", "#9C7250"), 3);
            Oval(c, wx - 36, wy - 18, 11, 11, Ui.B("#B88A62"));
            Oval(c, wx + 215, wy - 18, 11, 11, Ui.B("#B88A62"));
            Curtain(c, wx - 26, wy - 10, 40, 176, false);
            Curtain(c, wx + 176, wy - 10, 40, 176, true);

            // A picture of a fish (the cat's favorite subject), with a mat and a shadow on the wall.
            double px = w * 0.4, py = h * 0.17;
            Box(c, px + 4, py + 6, 104, 82, "#26000000", 6);
            Put(c, new Border { Width = 104, Height = 82, Background = Ui.B("#FBF4E8"), BorderBrush = Vertical("#D9A56A", "#A8723E"), BorderThickness = new Thickness(7), CornerRadius = new CornerRadius(3) }, px, py);
            Box(c, px + 17, py + 15, 70, 52, Vertical("#CFEAFB", "#9FD0EE"), 1);
            Shape(c, F("M {0} {1} C {2} {3} {4} {3} {5} {1} C {4} {6} {2} {6} {0} {1} Z M {5} {1} L {7} {8} L {7} {9} Z", px + 30, py + 42, px + 38, py + 33, px + 54, px + 62, py + 51, px + 72, py + 35, py + 49), "#F2A04E", null, 0);
            Oval(c, px + 36, py + 39, 3.5, 3.5, Ui.B("#3A2A20"));
            Shape(c, F("M {0} {1} q 6 4 14 0", px + 22, py + 60), (Brush)null, "#7FB6D8", 1.4);

            // Sofa: rounded, lit from the window side, with a shadow on the floor.
            double sx = w * 0.56;
            Contact(c, sx + 125, floor - 2, 330, 26, 110);
            Box(c, sx, floor - 122, 250, 76, Vertical("#EE9C86", "#D9806A"), 24);
            Box(c, sx + 10, floor - 118, 230, 8, "#33FFFFFF", 4);
            var cushionA = Box(c, sx + 32, floor - 108, 70, 46, Vertical("#FFE2AE", "#F2C482"), 16);
            cushionA.RenderTransform = new RotateTransform(-7, 35, 23);
            var cushionB = Box(c, sx + 150, floor - 108, 66, 44, Vertical("#B6DEC6", "#8FC4A6"), 16);
            cushionB.RenderTransform = new RotateTransform(6, 33, 22);
            Box(c, sx + 6, floor - 66, 238, 40, Vertical("#F6AE98", "#E39079"), 14);
            Box(c, sx + 122, floor - 64, 2, 34, "#22000000", 0);   // the seam between the seat cushions
            Box(c, sx - 14, floor - 88, 40, 68, Vertical("#EA9079", "#CF6F59"), 18);
            Box(c, sx + 224, floor - 88, 40, 68, Vertical("#EA9079", "#CF6F59"), 18);
            Box(c, sx - 10, floor - 86, 32, 6, "#33FFFFFF", 3);
            Box(c, sx - 14, floor - 30, 278, 14, Vertical("#D97862", "#B95E4A"), 6);
            Box(c, sx + 4, floor - 16, 8, 16, Vertical("#8A5A3B", "#5E3A24"), 2);
            Box(c, sx + 238, floor - 16, 8, 16, Vertical("#8A5A3B", "#5E3A24"), 2);

            // A leafy plant in a pot.
            double gx = w * 0.025;
            Contact(c, gx + 33, floor - 1, 96, 16, 110);
            foreach (double angle in new[] { -52.0, -30, -12, 6, 24, 44, -40, 34 })
            {
                double len = 70 + (angle * 7 % 20) + (Math.Abs(angle) < 20 ? 22 : 0);
                Leaf(c, gx + 33, floor - 70, len, 13, angle, Math.Abs(angle) < 20 ? "#4F9E5E" : "#6CBB78", "#CFEBC8");
            }
            Shape(c, F("M {0} {1} L {2} {1} L {3} {4} L {5} {4} Z", gx, floor - 72, gx + 66, gx + 56, floor, gx + 10), Across("#D9845E", "#A9563A"), null, 0);
            Box(c, gx - 4, floor - 78, 74, 12, Vertical("#D07A55", "#B0603F"), 4);
            Box(c, gx + 2, floor - 66, 62, 4, "#22000000", 0);

            // A round rug with a border.
            Oval(c, w * 0.26, floor - 12, w * 0.48, 34, Glow(Ui.C("#5A3010"), 50));
            Oval(c, w * 0.27, floor - 11, w * 0.46, 28, Ui.B("#E8A890"));
            Oval(c, w * 0.29, floor - 8, w * 0.42, 22, Ui.B("#F6CDBA"));
            Put(c, new Ellipse { Width = w * 0.36, Height = 14, Stroke = Ui.B("#E8A890"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 } }, w * 0.32, floor - 4);

            // Evening goes amber and night dim blue; the lamp is drawn after so it still lights the room.
            if (phase == "evening") Tint(c, w, h, Ui.B("#FF9A3C"), 0.1);
            else if (phase == "night") Tint(c, w, h, Ui.B("#1C2A66"), 0.24);

            // Floor lamp: off in the day, faint in the morning, a warm pool of light once it gets dark.
            double lx = w * 0.91, glow = Pick<double>(phase, 230, 0, 260, 290);
            bool dark = phase == "evening" || phase == "night";
            if (glow > 0) Oval(c, lx + 5 - glow / 2, h * 0.12 + 115 - glow / 2, glow, glow, Glow(Ui.C("#FFE9A0"), Pick<byte>(phase, 45, 0, 150, 190)));
            if (dark) Oval(c, lx - 110, floor - 20, 230, 44, Glow(Ui.C("#FFD98A"), 130));
            Contact(c, lx + 2, floor - 2, 70, 12, 120);
            Box(c, lx, h * 0.27, 5, floor - h * 0.27, Across("#B08A6A", "#6E5038"), 2);
            Oval(c, lx - 18, floor - 8, 41, 11, Vertical("#9A7A60", "#6E5038"));
            bool off = phase == "day";
            Shape(c, F("M {0} {1} L {2} {1} L {3} {4} L {5} {4} Z", lx - 20, h * 0.2, lx + 25, lx + 38, h * 0.29, lx - 33),
                off ? Vertical("#F8EBCB", "#E6D2A6") : Vertical("#FFF0B8", "#FFD36A"), off ? "#DCC28C" : "#E8C66A", 1.5);
        }

        // A curtain hanging in soft folds, gathered toward the window's edge.
        static void Curtain(Canvas c, double x, double y, double w, double h, bool right)
        {
            var folds = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            string[] tones = { "#F7C0AA", "#EBA48C", "#F9C8B3", "#E59C84", "#F5BCA5" };
            for (int i = 0; i < tones.Length; i++) folds.GradientStops.Add(new GradientStop(Ui.C(tones[right ? tones.Length - 1 - i : i]), i / (double)(tones.Length - 1)));
            folds.Freeze();
            Box(c, x, y, w, h, folds, 10);
            Box(c, x, y, w, 8, "#22000000", 4);
        }

        // The view through the living-room window (190 x 140; the frame and bars are drawn over it).
        static void WindowSky(Canvas sky, string phase)
        {
            switch (phase)
            {
                case "morning":
                    sky.Background = Vertical("#C9E5FA", "#FFE7CC");
                    Oval(sky, -40, 80, 270, 120, Glow(Ui.C("#FFD08A"), 190));
                    Drift(sky, Cloud(0.7, "#FFFFFF", "#F2E2DA", 0.95), 22, -100, 210, 34);
                    break;
                case "evening":
                    // The sun sinking below the sill, with a pink cloud thin enough to show the sunset.
                    sky.Background = Vertical("#E48AA6", "#FFB66E");
                    Oval(sky, -10, 80, 140, 120, Glow(Ui.C("#FFE1A0"), 220));
                    Oval(sky, 38, 116, 44, 44, Ui.B("#FFC56E"));
                    Drift(sky, Cloud(0.7, "#FFE4E1", "#E8A6B0", 0.75), 20, -100, 210, 34);
                    break;
                case "night":
                    sky.Background = Vertical("#0E1638", "#2A3768");
                    Stars(sky, 8, 8, 174, 100, 10, 2);
                    Moon(sky, 150, 34, 11, 90);
                    break;
                default:
                    sky.Background = Vertical("#B9E0FF", "#EAF6FF");
                    Drift(sky, Cloud(0.7, "#FFFFFF", "#DCE8F4", 0.95), 22, -100, 210, 34);
                    break;
            }
            // Treetops and a roof across the street, for depth.
            string far = phase == "night" ? "#1A2448" : phase == "evening" ? "#9A6A7E" : "#9CC8A8";
            var view = new GeometryGroup { FillRule = FillRule.Nonzero };
            view.Children.Add(new EllipseGeometry(new Point(30, 138), 40, 26));
            view.Children.Add(new EllipseGeometry(new Point(62, 132), 26, 22));
            view.Children.Add(new RectangleGeometry(new Rect(110, 112, 70, 40)));
            view.Children.Add(new EllipseGeometry(new Point(176, 136), 30, 20));
            sky.Children.Add(new Path { Data = view, Fill = Ui.B(far) });
            Shape(sky, "M 104 114 L 145 92 L 186 114 Z", phase == "night" ? "#141C3A" : phase == "evening" ? "#8A5A6E" : "#C98A72", null, 0);
        }

        static void Garden(Canvas c, double w, double h, double floor, string phase)
        {
            bool night = phase == "night";
            switch (phase)
            {
                case "morning": c.Background = Vertical("#B4DCF8", "#E2F1FB", "#FFE4C8"); break;
                case "evening": c.Background = Vertical("#6E6CB4", "#F49AA4", "#FFC47E"); break;
                case "night": c.Background = Vertical("#0D1533", "#33427A"); break;
                default: c.Background = Vertical("#8FCFFF", "#E8F7FF"); break;
            }

            if (night)
            {
                // Clear sky: no clouds, the moon where the sun would be. Stars stay above the treetops.
                Stars(c, 0, 0, w, floor - 200, 40, 6);
                Moon(c, w * 0.8, h * 0.17, 26, 100);
            }
            else
            {
                // The sun is low and soft in the morning and sinks, orange, behind the hill in the evening.
                double sx = w * 0.84, sy = h * 0.17, size = 64;
                string body = "#FFF3B0", rim = "#FFD95A", halo = "#FFF3B0";
                if (phase == "morning") { sx = w * 0.8; sy = floor - 190; size = 58; body = "#FFF6D8"; rim = "#FFE2A0"; halo = "#FFF1D4"; }
                else if (phase == "evening") { sy = floor - 116; size = 78; body = "#FFC27A"; rim = "#FF8A45"; halo = "#FFB56E"; }
                Oval(c, sx - 140, sy - 140, 280, 280, Glow(Ui.C(halo), 150));
                Oval(c, sx - 70, sy - 70, 140, 140, Glow(Ui.C(halo), 200));
                Oval(c, sx - size / 2, sy - size / 2, size, size, new RadialGradientBrush(Ui.C(body), Ui.C(rim)));

                // Sunset clouds turn pink and thin out so the sky shows through them.
                string top = phase == "evening" ? "#FFE6E0" : "#FFFFFF", under = phase == "evening" ? "#E7A3AE" : "#D8E6F2";
                double thick = phase == "evening" ? 0.75 : 0.96;
                Drift(c, Cloud(1.1, top, under, thick), 76, -180, w + 40, 80);
                Drift(c, Cloud(0.75, top, under, thick), 132, -180, w + 40, 105);
                Drift(c, Cloud(0.9, top, under, thick), 98, -180, w + 40, 130);
            }

            // Far hills fade into the sky; the near ones are greener.
            string farHill = Pick(phase, "#A6D3B0", "#9FD39E", "#B78E98", "#1E2A54");
            string midHill = Pick(phase, "#95CE88", "#93CF84", "#A08478", "#1A2448");
            Oval(c, w * 0.35, floor - 150, w * 0.9, 300, Ui.B(farHill));
            // Small trees standing along the far hill's crest.
            var trees = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int i = 0; i < 10; i++)
            {
                double tx = w * (0.48 + i * 0.05), dx = (tx - w * 0.8) / (w * 0.45);
                double ground = floor - 150 * Math.Sqrt(Math.Max(0, 1 - dx * dx)) + 3;
                double size = 8 + (i * 5 % 7);
                trees.Children.Add(new EllipseGeometry(new Point(tx, ground - size * 1.1), size * 0.75, size * 1.05));
                trees.Children.Add(new RectangleGeometry(new Rect(tx - 1.2, ground - size * 0.4, 2.4, size * 0.4 + 2)));
            }
            Geo(c, trees, new SolidColorBrush(Shade(Ui.C(farHill), -0.2)));
            Oval(c, -w * 0.15, floor - 120, w * 0.75, 260, Vertical(Shade(Ui.C(midHill), 0.1).ToString(), midHill));
            Oval(c, w * 0.45, floor - 95, w * 0.8, 240, Vertical(Shade(Ui.C(midHill), 0.06).ToString(), Shade(Ui.C(midHill), -0.06).ToString()));

            Tree(c, w * 0.09, floor, night);

            // Picket fence: rails behind, pickets lit on one side, a shadow on the grass.
            Box(c, 0, floor - 52, w, 8, Vertical("#F4F4F0", "#D8D8D2"), 2);
            Box(c, 0, floor - 26, w, 8, Vertical("#F4F4F0", "#D8D8D2"), 2);
            var pickets = new GeometryGroup();
            var shades = new GeometryGroup();
            for (double x = 6; x < w; x += 30)
            {
                pickets.Children.Add(Geometry.Parse(F("M {0} {1} L {0} {2} L {3} {4} L {5} {2} L {5} {1} Z", x, floor, floor - 62, x + 9, floor - 72, x + 18)));
                shades.Children.Add(new RectangleGeometry(new Rect(x + 13, floor - 62, 5, 62)));
            }
            Geo(c, pickets, Vertical("#FFFFFF", "#EEEEE8"));
            Geo(c, shades, new SolidColorBrush(Color.FromArgb(30, 60, 70, 90)));

            // Flowers along the fence: petals around a center, a leaf on the stem.
            string[] colors = { "#FF8FB1", "#FFD84D", "#B48CFF", "#FF9F6B", "#FFFFFF" };
            for (double x = w * 0.22; x < w * 0.98; x += 34 + Rng.Next(30))
            {
                double stem = 18 + Rng.Next(16);
                Shape(c, F("M {0} {1} Q {2} {3} {0} {4}", x, floor, x - 4, floor - stem / 2, floor - stem), (Brush)null, "#4E9E4A", 2);
                Leaf(c, x - 1, floor - stem * 0.4, 10, 4, -50, "#5DAE50", "#A8DCA0");
                string petal = colors[Rng.Next(colors.Length)];
                var flower = new GeometryGroup { FillRule = FillRule.Nonzero };
                for (int k = 0; k < 5; k++)
                {
                    double a = k * Math.PI * 2 / 5;
                    flower.Children.Add(new EllipseGeometry(new Point(x + Math.Cos(a) * 5, floor - stem - 5 + Math.Sin(a) * 5), 4, 4));
                }
                Geo(c, flower, Ui.B(petal));
                Oval(c, x - 2.5, floor - stem - 7.5, 5, 5, Ui.B(petal == "#FFD84D" ? "#E08A2A" : "#FFE27A"));
            }

            // Grass: lighter at the back, with tufts and a few daisies.
            Box(c, 0, floor, w, h - floor, Vertical("#86D277", "#6CBF5E"), 0);
            Box(c, 0, floor, w, 6, Vertical("#40336633", "#00336633"), 0);
            var blades = new GeometryGroup();
            for (double x = 10; x < w; x += 18 + Rng.Next(26))
            {
                double y = floor + 12 + Rng.Next((int)Math.Max(1, h - floor - 18));
                for (int k = -2; k <= 2; k++)
                    blades.Children.Add(Geometry.Parse(F("M {0} {1} Q {2} {3} {4} {5}", x + k * 2.2, y, x + k * 2.6 + 1, y - 6, x + k * 4.2, y - 9 - (2 - Math.Abs(k)) * 2.5)));
            }
            c.Children.Add(new Path { Data = blades, Stroke = Ui.B("#4FA046"), StrokeThickness = 1.3, StrokeEndLineCap = PenLineCap.Round });
            for (int i = 0; i < 9; i++)
            {
                double x = Rng.NextDouble() * w, y = floor + 10 + Rng.NextDouble() * (h - floor - 16);
                Oval(c, x - 3, y - 3, 6, 6, Ui.B("#FFFFFF"));
                Oval(c, x - 1.2, y - 1.2, 2.4, 2.4, Ui.B("#FFD84D"));
            }

            if (night)
            {
                // Moonlight: the ground and fence go dim blue, fading out above the tree so the stars stay bright.
                Tint(c, w, h, Fade("#101A4A", (floor - 360) / h, (floor - 190) / h), 0.45);
                for (int i = 0; i < 5; i++) Firefly(c, w * (0.18 + i * 0.16) + Rng.Next(30), floor - 40 - Rng.Next(90));
                return;
            }
            if (phase == "evening") Tint(c, w, h, Ui.B("#FF8A3C"), 0.07);
            Butterfly(c, w * 0.35, h * 0.42, "#FFB3D1");
            Butterfly(c, w * 0.62, h * 0.33, "#9FD3FF");
        }

        // An apple tree: a tapering trunk with two branches, a crown in lit and shaded clumps.
        static void Tree(Canvas c, double x, double floor, bool night)
        {
            Contact(c, x + 14, floor + 4, 200, 26, 70);
            Shape(c, F("M {0} {1} C {2} {3} {4} {5} {6} {7} L {8} {7} C {9} {5} {10} {3} {11} {1} Z",
                x - 6, floor, x + 2, floor - 70, x + 4, floor - 140, x + 6, floor - 210, x + 22, x + 24, x + 26, x + 34), Across("#A87A50", "#7A5232"), null, 0);
            Shape(c, F("M {0} {1} Q {2} {3} {4} {5}", x + 10, floor - 150, x - 20, floor - 175, x - 40, floor - 200), (Brush)null, "#8A5E3A", 7);
            Shape(c, F("M {0} {1} Q {2} {3} {4} {5}", x + 18, floor - 160, x + 50, floor - 185, x + 70, floor - 215), (Brush)null, "#8A5E3A", 6);
            string dark = night ? "#2C5A3C" : "#3E9A54", mid = night ? "#356A46" : "#4FAE60", light = night ? "#3E7650" : "#6CC779", top = night ? "#4A8058" : "#86D68E";
            var clumps = new[]
            {
                new double[] { -60, -250, 70, 54 }, new double[] { 40, -262, 72, 58 }, new double[] { -10, -300, 80, 62 },
                new double[] { -70, -300, 56, 46 }, new double[] { 60, -310, 60, 50 }, new double[] { -20, -345, 62, 48 }, new double[] { 30, -340, 54, 42 }
            };
            Oval(c, x - 92, floor - 352, 210, 140, Ui.B(dark));   // the crown's body, so no sky shows between the clumps
            foreach (double[] k in clumps) Oval(c, x + k[0] - k[2] / 2 + 14, floor + k[1] - k[3] / 2 + 16, k[2] + 8, k[3] + 8, Ui.B(dark));
            foreach (double[] k in clumps) Oval(c, x + k[0] - k[2] / 2 + 8, floor + k[1] - k[3] / 2 + 6, k[2], k[3], new RadialGradientBrush(Ui.C(light), Ui.C(mid)) { GradientOrigin = new Point(0.3, 0.25) });
            Oval(c, x - 34, floor - 360, 70, 40, Glow(Ui.C(top), 140));
            foreach (var apple in new[] { new Point(x - 40, floor - 250), new Point(x + 52, floor - 282), new Point(x + 16, floor - 228), new Point(x - 8, floor - 318) })
            {
                Oval(c, apple.X, apple.Y, 14, 14, new RadialGradientBrush(Ui.C("#FF9A88"), Ui.C("#E04E40")) { GradientOrigin = new Point(0.35, 0.3) });
                Shape(c, F("M {0} {1} l 1 -4", apple.X + 7, apple.Y + 1), (Brush)null, "#6E4A2A", 1.4);
            }
        }

        // A tiny glowing dot that wanders slowly and fades in and out.
        static void Firefly(Canvas c, double x, double y)
        {
            var f = new Canvas { Width = 18, Height = 18 };
            Oval(f, 0, 0, 18, 18, Glow(Ui.C("#E6FF8C"), 170));
            Oval(f, 7.25, 7.25, 3.5, 3.5, Ui.B("#F8FFD2"));
            var move = new TranslateTransform();
            f.RenderTransform = move;
            Cached(f);
            Put(c, Live(f), x, y);
            Loop(move, TranslateTransform.XProperty, -60, 60, 12 + Rng.NextDouble() * 6, true, -Rng.NextDouble() * 12);
            Loop(move, TranslateTransform.YProperty, -16, 16, 5 + Rng.NextDouble() * 3, true, -Rng.NextDouble() * 5);
            LoopOpacity(f, 0.15, 1, 1.8 + Rng.NextDouble() * 1.5, -Rng.NextDouble() * 3);
        }

        static void Butterfly(Canvas c, double x, double y, string color)
        {
            var b = new Canvas { Width = 26, Height = 20 };
            var wings = new Canvas { Width = 26, Height = 20 };
            Brush wing = new RadialGradientBrush(Shade(Ui.C(color), 0.3), Ui.C(color));
            Oval(wings, 0, 0, 13, 13, wing);
            Oval(wings, 13, 0, 13, 13, wing);
            Oval(wings, 2, 10, 10, 9, wing).Opacity = 0.85;
            Oval(wings, 14, 10, 10, 9, wing).Opacity = 0.85;
            var flap = new ScaleTransform(1, 1, 13, 10);
            wings.RenderTransform = flap;
            b.Children.Add(wings);
            Box(b, 12, 3, 2.5, 15, "#5A4A3A", 1);
            var move = new TranslateTransform();
            b.RenderTransform = move;
            Put(c, Live(b), x, y);
            Loop(flap, ScaleTransform.ScaleXProperty, 1, 0.25, 0.16, true, 0);
            Loop(move, TranslateTransform.XProperty, -150, 150, 6 + Rng.NextDouble() * 3, true, -Rng.NextDouble() * 5);
            Loop(move, TranslateTransform.YProperty, -45, 45, 2.7 + Rng.NextDouble(), true, -Rng.NextDouble() * 2);
        }

        static void Night(Canvas c, double w, double h, double floor)
        {
            c.Background = Vertical("#121835", "#2C2C5C", "#4A3A6E");
            Stars(c, 0, 0, w, h * 0.6, 90, 12);
            Moon(c, w * 0.8, h * 0.18, 32, 110);

            // Far skyline, then the near one with lit windows, a few of them going on and off.
            var far = new GeometryGroup();
            for (double x = -10; x < w; x += 40 + Rng.Next(60)) far.Children.Add(new RectangleGeometry(new Rect(x, floor - 140 - Rng.Next(120), 38 + Rng.Next(40), 260)));
            Geo(c, far, Ui.B("#262B52"));
            var blocks = new GeometryGroup();
            var lit = new GeometryGroup();
            var tops = new GeometryGroup();
            var flicker = new List<Rect>();
            double bx = -10;
            while (bx < w)
            {
                double bw = 50 + Rng.Next(70), bh = 70 + Rng.Next(170);
                blocks.Children.Add(new RectangleGeometry(new Rect(bx, floor - bh, bw, bh)));
                tops.Children.Add(new RectangleGeometry(new Rect(bx - 2, floor - bh - 3, bw + 4, 4)));
                if (bw > 70 && Rng.NextDouble() < 0.3)
                {
                    // A water tank on legs.
                    tops.Children.Add(new RectangleGeometry(new Rect(bx + bw * 0.5, floor - bh - 26, 18, 16), 4, 4));
                    tops.Children.Add(new RectangleGeometry(new Rect(bx + bw * 0.5 + 2, floor - bh - 10, 2, 8)));
                    tops.Children.Add(new RectangleGeometry(new Rect(bx + bw * 0.5 + 14, floor - bh - 10, 2, 8)));
                }
                for (double wy = floor - bh + 12; wy < floor - 20; wy += 22)
                    for (double wx = bx + 8; wx < bx + bw - 10; wx += 16)
                    {
                        if (Rng.NextDouble() > 0.3) continue;
                        if (flicker.Count < 8 && Rng.NextDouble() < 0.1) flicker.Add(new Rect(wx, wy, 6, 9));
                        else lit.Children.Add(new RectangleGeometry(new Rect(wx, wy, 6, 9), 1, 1));
                    }
                bx += bw + 4;
            }
            Geo(c, blocks, Vertical("#2A3158", "#1E2344"));
            Geo(c, tops, Ui.B("#353C66"));
            Geo(c, lit, Vertical("#FFE7A8", "#F2BE6A"));
            foreach (Rect r in flicker)
            {
                var win = Live(Box(c, r.X, r.Y, r.Width, r.Height, "#FFD98A", 1));
                LoopOpacity(win, 0.05, 0.9, 6 + Rng.NextDouble() * 8, -Rng.NextDouble() * 8);
            }

            // String lights across the sky: the glow and most bulbs hold still, a few shimmer.
            double y0 = h * 0.06, sag = h * 0.13;
            var shimmer = new List<Point>();
            Shape(c, F("M 0 {0} Q {1} {2} {3} {4}", y0, w / 2, y0 + sag * 2, w, y0 + 10), (Brush)null, "#20203A", 1.5);
            for (int i = 1; i < 16; i++)
            {
                double t = i / 16.0;
                double lx = 2 * (1 - t) * t * (w / 2) + t * t * w;
                double ly = (1 - t) * (1 - t) * y0 + 2 * (1 - t) * t * (y0 + sag * 2) + t * t * (y0 + 10);
                Oval(c, lx - 14, ly - 8, 28, 28, Glow(Ui.C("#FFE6A0"), 130));
                Box(c, lx - 2, ly - 2, 4, 4, "#3A3448", 1);
                if (i % 4 == 1) shimmer.Add(new Point(lx, ly));
                else Oval(c, lx - 4, ly + 1, 8, 11, Bulb);
            }

            // The rooftop: a ledge catching the moonlight, a brick chimney and a TV aerial.
            Box(c, 0, floor, w, h - floor, Vertical("#4E4462", "#3A3248"), 0);
            Box(c, 0, floor - 7, w, 11, Vertical("#6E6290", "#544870"), 2);
            Box(c, 0, floor - 7, w, 1.5, "#8C80AE", 0);
            Box(c, 0, floor + 4, w, 8, Vertical("#40000000", "#00000000"), 0);
            double cx = w * 0.12;
            Box(c, cx, floor - 96, 46, 92, Bricks(), 2);
            Box(c, cx + 28, floor - 96, 18, 92, "#33000000", 0);
            Box(c, cx - 6, floor - 104, 58, 12, Vertical("#8A6A6A", "#6A4A4A"), 3);
            Box(c, cx + 8, floor - 114, 10, 10, Vertical("#B0705A", "#8A5040"), 2);
            Box(c, cx + 26, floor - 112, 10, 8, Vertical("#B0705A", "#8A5040"), 2);
            Shape(c, F("M {0} {1} L {0} {2} M {3} {4} L {5} {4} M {6} {7} L {8} {7} M {9} {10} L {0} {11}", w * 0.9, floor - 4, floor - 120,
                w * 0.9 - 22, floor - 100, w * 0.9 + 22, w * 0.9 - 14, floor - 84, w * 0.9 + 14, w * 0.9 - 30, floor - 60, floor - 110), (Brush)null, "#8D86A8", 2.5);

            // The shimmering bulbs go last, so everything still in this room makes only a few cached layers.
            foreach (Point b in shimmer)
            {
                var bulb = Live(Oval(c, b.X - 4, b.Y + 1, 8, 11, Bulb));
                LoopOpacity(bulb, 0.65, 1, 2 + Rng.NextDouble() * 2, -Rng.NextDouble() * 2);
            }
        }

        static readonly Brush Bulb = MakeBulb();

        static Brush MakeBulb()
        {
            var b = new RadialGradientBrush(Ui.C("#FFF6D0"), Ui.C("#FFD27A"));
            b.Freeze();
            return b;
        }

        // Brick, for chimneys.
        static Brush bricks;

        static Brush Bricks()
        {
            if (bricks != null) return bricks;
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Ui.B("#7E4A44"), null, new RectangleGeometry(new Rect(0, 0, 18, 10))));
            g.Children.Add(new GeometryDrawing(Ui.B("#8C5650"), null, new RectangleGeometry(new Rect(10, 6, 7, 3.5))));
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(18, 0.5)));
            lines.Children.Add(new LineGeometry(new Point(0, 5.5), new Point(18, 5.5)));
            lines.Children.Add(new LineGeometry(new Point(8.5, 0.5), new Point(8.5, 5.5)));
            lines.Children.Add(new LineGeometry(new Point(0.5, 5.5), new Point(0.5, 10)));
            g.Children.Add(new GeometryDrawing(null, new Pen(Ui.B("#5A3430"), 1), lines));
            var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 18, 10), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze();
            bricks = b;
            return b;
        }

        static void Cafe(Canvas c, double w, double h, double floor, string phase)
        {
            c.Background = phase == "day" ? Vertical("#FFF4E4", "#F8E4C8") : Vertical("#FBEBD4", "#F1D9B8");
            // A thin picture rail high up, and wood panelling below a chair rail.
            Box(c, 0, h * 0.09, w, 5, Vertical("#E9CFA8", "#D2B088"), 0);
            Box(c, 0, floor - 126, w, 126, Vertical("#C9935E", "#A87444"), 0);
            for (double x = 14; x < w - 40; x += 92)
            {
                Box(c, x, floor - 108, 78, 92, Vertical("#B98352", "#C99560"), 6);
                Box(c, x, floor - 108, 78, 3, "#33000000", 2);
                Box(c, x, floor - 19, 78, 3, "#33FFFFFF", 2);
            }
            Box(c, 0, floor - 130, w, 10, Vertical("#B47A50", "#8E5A38"), 2);
            Box(c, 0, floor - 130, w, 1.5, "#D6A57A", 0);

            // Chalkboard menu, in a wooden frame.
            double bx = w * 0.06, by = h * 0.16;
            var board = new StackPanel();
            board.Children.Add(Chalk("MENU", 20, FontWeights.Bold));
            board.Children.Add(Chalk("Fish ........ 3", 14, FontWeights.Normal));
            board.Children.Add(Chalk("More fish ... 3", 14, FontWeights.Normal));
            board.Children.Add(Chalk("Warm milk ... 2", 14, FontWeights.Normal));
            board.Children.Add(Chalk("Naps ....... free", 14, FontWeights.Normal));
            Box(c, bx + 5, by + 7, 230, 178, "#30000000", 8);
            Put(c, new Border
            {
                Width = 230,
                Height = 178,
                Background = new RadialGradientBrush(Ui.C("#3A4A42"), Ui.C("#26302B")),
                BorderBrush = Vertical("#A8724A", "#7A4E33"),
                BorderThickness = new Thickness(8),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 8, 10, 8),
                Child = board
            }, bx, by);

            // A shelf of jars.
            double sx = w * 0.745, sy = h * 0.24;
            Box(c, sx, sy, 230, 8, Vertical("#9A6A48", "#7A4E33"), 2);
            Box(c, sx + 4, sy + 8, 222, 6, "#22000000", 0);
            string[] jars = { "#F7C6C7", "#C7E3F7", "#FFE3A3", "#CDEBC6", "#E6D1F2" };
            for (int i = 0; i < jars.Length; i++)
            {
                double jh = 30 + (i % 2) * 12, jx = sx + 10 + i * 44;
                Box(c, jx, sy - jh, 30, jh, Across(jars[i], Shade(Ui.C(jars[i]), -0.18).ToString()), 6);
                Box(c, jx + 5, sy - jh + 6, 4, jh - 12, "#66FFFFFF", 2);
                Box(c, jx - 2, sy - jh - 5, 34, 7, Vertical("#C69A70", "#9E7250"), 3);
            }

            // Counter with an espresso machine, cups and a cake.
            double cx = w * 0.62;
            Contact(c, cx + 155, floor, 360, 18, 90);
            Box(c, cx, floor - 112, 310, 112, Vertical("#B07648", "#8E5A34"), 0);
            for (int i = 0; i < 3; i++)
            {
                Box(c, cx + 14 + i * 98, floor - 98, 86, 82, Vertical("#BC8256", "#A46E44"), 8);
                Box(c, cx + 14 + i * 98, floor - 98, 86, 3, "#33000000", 3);
            }
            Box(c, cx - 10, floor - 126, 330, 14, Vertical("#8A5A3C", "#6A4228"), 4);
            Box(c, cx - 10, floor - 126, 330, 2, "#B07E5A", 0);
            Box(c, cx + 20, floor - 178, 56, 52, Across("#7A7A7E", "#4E4E52"), 6);
            Box(c, cx + 30, floor - 168, 36, 16, Vertical("#C8CCD2", "#8E9298"), 3);
            Box(c, cx + 42, floor - 150, 12, 10, "#3A3A3E", 2);
            Box(c, cx + 24, floor - 176, 6, 46, "#33FFFFFF", 3);
            foreach (double ux in new[] { 110.0, 145, 180 })
            {
                Oval(c, cx + ux - 4, floor - 129, 28, 6, Ui.B("#E8E2DA"));
                Box(c, cx + ux, floor - 146, 20, 18, Across("#FFFFFF", "#E2DDD6"), 3);
                Shape(c, F("M {0} {1} q 8 1 0 10", cx + ux + 20, floor - 142), (Brush)null, "#F2EEE8", 2.2);
            }
            Oval(c, cx + 228, floor - 133, 64, 10, Vertical("#F4F0EA", "#D8D0C4"));
            Box(c, cx + 240, floor - 160, 40, 30, Vertical("#FBC4CE", "#EFA0B0"), 8);
            Box(c, cx + 240, floor - 164, 40, 8, "#FFFFFF", 4);
            Box(c, cx + 240, floor - 150, 40, 3, "#E58A9E", 0);
            Oval(c, cx + 255, floor - 172, 10, 10, new RadialGradientBrush(Ui.C("#FF6A6A"), Ui.C("#C8303A")) { GradientOrigin = new Point(0.35, 0.3) });

            // A little table with two chairs, a cup and a flower.
            double tx = w * 0.27;
            Contact(c, tx + 10, floor - 1, 230, 16, 90);
            Chair(c, tx - 70, floor, false);
            Chair(c, tx + 92, floor, true);
            Box(c, tx + 6, floor - 64, 8, 60, Across("#8A6048", "#5E3E2A"), 2);
            Oval(c, tx - 15, floor - 8, 50, 10, Vertical("#7A5238", "#5E3E2A"));
            Oval(c, tx - 52, floor - 76, 124, 22, Vertical("#FBF2E6", "#E6D6C2"));
            Box(c, tx - 20, floor - 92, 16, 14, Across("#FFFFFF", "#E2DDD6"), 3);
            Box(c, tx + 20, floor - 100, 10, 22, Across("#D8EEFA", "#A8CCE6"), 3);
            Shape(c, F("M {0} {1} q 3 -8 0 -14", tx + 25, floor - 100), (Brush)null, "#4E9E4A", 1.6);
            Oval(c, tx + 18, floor - 120, 14, 14, new RadialGradientBrush(Ui.C("#FFB0C8"), Ui.C("#F07898")));

            // Checkered floor, laid toward the viewer, darker where it meets the wall.
            Box(c, 0, floor, w, h - floor, "#EADBC8", 0);
            var dark = new GeometryGroup();
            double rows = 3, cxv = w / 2;
            for (int row = 0; row < rows; row++)
            {
                double y0 = floor + (h - floor) * row / rows, y1 = floor + (h - floor) * (row + 1) / rows;
                double s0 = 1 + 0.35 * row / rows, s1 = 1 + 0.35 * (row + 1) / rows;
                for (int col = -14; col < 30; col++)
                {
                    if ((row + col) % 2 != 0) continue;
                    double x0 = col * 40, x1 = x0 + 40;
                    dark.Children.Add(Geometry.Parse(F("M {0} {1} L {2} {1} L {3} {4} L {5} {4} Z",
                        cxv + (x0 - cxv) * s0, y0, cxv + (x1 - cxv) * s0, cxv + (x1 - cxv) * s1, y1, cxv + (x0 - cxv) * s1)));
                }
            }
            Geo(c, dark, Ui.B("#D6BE9E"));
            Box(c, 0, floor, w, h - floor, Vertical("#30000000", "#00FFFFFF"), 0);

            // Daylight lifts the room a touch; after dark it warms up and the lamps carry the light.
            if (phase == "day") Tint(c, w, h, Ui.B("#FFFFFF"), 0.06);
            else if (phase == "evening") Tint(c, w, h, Ui.B("#FFA24A"), 0.06);
            else if (phase == "night") Tint(c, w, h, Ui.B("#E07028"), 0.13);

            // Hanging lamps that sway a little, drawn over the wash so they stay the brightest thing in the room.
            double reach = Pick<double>(phase, 140, 124, 160, 170);
            Brush light = Glow(Ui.C(Pick(phase, "#FFE6A8", "#FFE6A8", "#FFD98C", "#FFD27E")), Pick<byte>(phase, 150, 105, 200, 225));
            Brush shade = Vertical("#3E4C46", "#22302A");
            foreach (double fx in new[] { 0.38, 0.52, 0.66 })
            {
                double lx = w * fx, len = h * 0.2 + Rng.Next(20);
                var lamp = new Canvas { Width = 1, Height = 1 };
                Oval(lamp, -reach / 2, len + 30 - reach / 2, reach, reach, light);
                Shape(lamp, F("M 0 0 L 0 {0}", len), (Brush)null, "#3A2A20", 1.5);
                Box(lamp, -4, len - 4, 8, 6, "#2A2420", 2);
                Shape(lamp, F("M -8 {0} L 8 {0} L 26 {1} Q 0 {2} -26 {1} Z", len, len + 22, len + 26), shade, null, 0);
                Shape(lamp, F("M -5 {0} L -20 {1}", len + 3, len + 20), (Brush)null, "#33FFFFFF", 2);
                Oval(lamp, -7, len + 16, 14, 11, new RadialGradientBrush(Ui.C("#FFFBE0"), Ui.C("#FFE28A")));
                var swing = new RotateTransform(0, 0, 0);
                lamp.RenderTransform = swing;
                Cached(lamp);
                Put(c, Live(lamp), lx, 0);
                Loop(swing, RotateTransform.AngleProperty, -1.6, 1.6, 2.8 + Rng.NextDouble() * 1.4, true, -Rng.NextDouble() * 2);
            }
        }

        static TextBlock Chalk(string text, double size, FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe Print, Comic Sans MS, Segoe UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = Ui.B("#F2F2EC"),
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        static void Chair(Canvas c, double x, double floor, bool flip)
        {
            double back = flip ? x + 36 : x;
            Box(c, back, floor - 110, 8, 110, Across("#8A6048", "#5E3E2A"), 3);
            Box(c, flip ? x + 6 : x + 8, floor - 104, 22, 5, "#7A5238", 2);
            Box(c, flip ? x + 6 : x + 8, floor - 88, 22, 5, "#7A5238", 2);
            Box(c, x, floor - 56, 44, 9, Vertical("#8A6048", "#6A4632"), 3);
            Box(c, flip ? x : x + 36, floor - 50, 6, 50, Across("#7A5238", "#5E3E2A"), 2);
        }
    }
}

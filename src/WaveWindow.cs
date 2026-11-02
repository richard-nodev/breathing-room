using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    // "Ride the wave" (urge surfing): an urge rises, peaks and falls on its own, usually within a few
    // minutes. Three calm minutes of watching that happen, with the cat purring. Never required.
    public class WaveWindow
    {
        const double Seconds = 180, GraphW = 500, GraphH = 190;
        static WaveWindow open;

        readonly Window window;
        readonly TextBlock prompt, clock;
        readonly Ellipse marker;
        readonly Canvas graph = new Canvas { Width = GraphW, Height = GraphH };
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        DateTime started = DateTime.Now;
        readonly Action passed, haveOne;
        readonly StackPanel ending = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        bool finished;
        readonly Path ridden;

        public static void Show(Action passed, Action haveOne)
        {
            if (open != null) { open.window.Activate(); return; }
            open = new WaveWindow(passed, haveOne);
            open.window.Show();
            open.window.Activate();
        }

        // For README pictures: the window part-way through the wave, silent and not ticking.
        internal static Window CreateForSnapshot(double atSeconds)
        {
            var w = new WaveWindow(null, null);
            w.timer.Stop();
            Sfx.PurrOff();
            w.started = DateTime.Now.AddSeconds(-atSeconds);
            w.timer.Start();
            w.timer.Stop();
            w.Tick();
            return w.window;
        }

        void Tick()
        {
            Step(ridden);
        }

        // How strong the urge usually is at time t (0..1): up for about a minute, a plateau, then down.
        public static double Strength(double t)
        {
            double u = Math.Max(0, Math.Min(1, t / Seconds));
            if (u < 0.38) { double k = u / 0.38; return 0.12 + 0.88 * k * k * (3 - 2 * k); }
            if (u < 0.55) return 1;
            double f = (u - 0.55) / 0.45;
            return 1 - 0.9 * f * f * (3 - 2 * f);
        }

        static Point At(double t)
        {
            return new Point(t / Seconds * GraphW, GraphH - 12 - Strength(t) * (GraphH - 30));
        }

        WaveWindow(Action passed, Action haveOne)
        {
            this.passed = passed;
            this.haveOne = haveOne;
            window = Ui.PaperWindow("Ride the wave", 580, 560);
            var page = new StackPanel { Margin = new Thickness(36, 22, 36, 24) };
            page.Children.Add(Ui.Heading("Ride the wave", 24));
            page.Children.Add(Ui.Text("An urge rises, peaks and falls by itself, usually within a few minutes. You don't have to fight it or do anything. Just watch it pass.", 13.5, Ui.Soft));

            // The whole wave, faint; the part already ridden is drawn over it in color.
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, GraphH), true, true);
                for (double t = 0; t <= Seconds; t += 2) ctx.LineTo(At(t), true, true);
                ctx.LineTo(new Point(GraphW, GraphH), true, false);
            }
            area.Freeze();
            graph.Children.Add(new Path { Data = area, Fill = Ui.B("#F7E3CC") });
            ridden = new Path
            {
                Data = area,
                Fill = new LinearGradientBrush(Ui.C("#F6B26B"), Ui.C("#F9D9B4"), 90),
                Clip = new RectangleGeometry(new Rect(0, 0, 0, GraphH))
            };
            graph.Children.Add(ridden);
            marker = new Ellipse { Width = 18, Height = 18, Fill = Ui.OrangeDeep, Stroke = Brushes.White, StrokeThickness = 3 };
            graph.Children.Add(marker);
            graph.Margin = new Thickness(0, 22, 0, 10);
            page.Children.Add(graph);

            prompt = Ui.Heading("", 17);
            prompt.MinHeight = 48;
            page.Children.Add(prompt);
            clock = Ui.Text("", 13, Ui.Soft);
            page.Children.Add(clock);

            ending.Margin = new Thickness(0, 14, 0, 0);
            ending.Children.Add(Ui.Primary("It passed", delegate { Finish(true); }));
            var have = Ui.Pill("I'll have one", delegate { Finish(false); });
            have.Margin = new Thickness(10, 0, 0, 0);
            have.Padding = new Thickness(16, 7, 16, 7);
            ending.Children.Add(have);
            page.Children.Add(ending);

            var stop = Ui.Link("Stop here", delegate { window.Close(); });
            stop.Margin = new Thickness(0, 14, 0, 0);
            page.Children.Add(stop);

            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            timer.Tick += delegate { Step(ridden); };
            window.Closed += delegate { timer.Stop(); Sfx.PurrOff(); open = null; };
            Step(ridden);
            timer.Start();
            Sfx.PurrOn(0.2f);
        }

        void Step(Path ridden)
        {
            double t = Math.Min(Seconds, (DateTime.Now - started).TotalSeconds);
            Point p = At(t);
            Canvas.SetLeft(marker, p.X - 9);
            Canvas.SetTop(marker, p.Y - 9);
            ((RectangleGeometry)ridden.Clip).Rect = new Rect(0, 0, p.X, GraphH);
            prompt.Text = t < 25 ? "Notice where you feel it. Hands? Chest? Mouth?"
                        : t < 65 ? "It's rising. You don't have to do anything about it."
                        : t < 100 ? "This is about the top. It won't get much bigger."
                        : t < 140 ? "It's already going down, all by itself."
                        : t < Seconds ? "Smaller now. Still here, still fine."
                        : "That was the whole wave.";
            int left = (int)Math.Ceiling(Seconds - t);
            clock.Text = left > 0 ? (left / 60) + ":" + (left % 60).ToString("00") + " left" : "Still want one, or did it pass?";
            if (t >= Seconds && !finished)
            {
                finished = true;
                timer.Stop();
                Sfx.PurrOff();
                ending.Visibility = Visibility.Visible;
            }
        }

        void Finish(bool itPassed)
        {
            window.Close();
            if (itPassed) { if (passed != null) passed(); }
            else if (haveOne != null) haveOne();
        }
    }
}

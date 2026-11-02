using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    static class OverlayWindow
    {
        public static void Setup(Window w)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.Topmost = true;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.ResizeMode = ResizeMode.NoResize;
            w.Focusable = false;
            w.SourceInitialized += delegate { Native.MakeOverlay(w, true); };
            var top = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            top.Tick += delegate { if (w.IsVisible) Native.KeepOnTop(w); };
            w.Loaded += delegate { top.Start(); };
        }
    }

    // A warm glow on the screen edge that starts bright when a cigarette is logged and fades over
    // about an hour. No number and no "allowed now" moment: it just remembers the last one.
    public class AfterglowWindow : Window
    {
        public const double FadeMinutes = 25, GoneAfterMinutes = 80;
        readonly Rectangle glow = new Rectangle();
        string edge = "top";

        public AfterglowWindow()
        {
            Title = "Breathing Room afterglow";
            OverlayWindow.Setup(this);
            Height = 26;
            Content = glow;
            var ends = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            ends.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            ends.GradientStops.Add(new GradientStop(Colors.Black, 0.12));
            ends.GradientStops.Add(new GradientStop(Colors.Black, 0.88));
            ends.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            glow.OpacityMask = ends;
        }

        public void Place(string where)
        {
            edge = where == "bottom" ? "bottom" : "top";
            Rect wa = SystemParameters.WorkArea;
            Left = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Top = edge == "bottom" ? wa.Bottom - Height : 0;
            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, edge == "top" ? 0 : 1),
                EndPoint = new Point(0.5, edge == "top" ? 1 : 0)
            };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(235, 255, 150, 60), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 170, 80), 0.25));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 190, 110), 1));
            glow.Fill = fill;
        }

        public static double Strength(TimeSpan since)
        {
            double minutes = since.TotalMinutes;
            if (minutes < 0 || minutes > GoneAfterMinutes) return 0;
            return 0.95 * Math.Exp(-minutes / FadeMinutes);
        }

        public void Refresh(DateTime? last, DateTime now, bool flare)
        {
            double target = last == null ? 0 : Strength(now - last.Value);
            if (target < 0.02)
            {
                if (IsVisible) Hide();
                return;
            }
            if (!IsVisible) Show();
            if (flare)
            {
                var a = new DoubleAnimation(1, target, TimeSpan.FromSeconds(4));
                BeginAnimation(OpacityProperty, a);
            }
            else
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = target;
            }
        }
    }

    // Optional tiny pill: "12m · #7". For people who like a number.
    public class BadgeWindow : Window
    {
        readonly TextBlock text;

        public BadgeWindow()
        {
            Title = "Breathing Room badge";
            OverlayWindow.Setup(this);
            SizeToContent = SizeToContent.WidthAndHeight;
            text = new TextBlock { FontFamily = Ui.Font, FontSize = 12.5, Foreground = Ui.B("#FFF3E6") };
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 43, 29, 20)),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 4, 10, 5),
                Margin = new Thickness(2),
                Child = text
            };
            SizeChanged += delegate { Place(Store.Data.Settings.BadgeCorner); };
        }

        public void Place(string corner)
        {
            Rect wa = SystemParameters.WorkArea;
            const double margin = 12;
            Left = corner != null && corner.EndsWith("left") ? wa.Left + margin : wa.Right - ActualWidth - margin;
            Top = corner != null && corner.StartsWith("bottom") ? wa.Bottom - ActualHeight - margin : wa.Top + margin;
        }

        public void Refresh(DateTime? last, int today, DateTime now)
        {
            if (last == null)
            {
                text.Text = "no cigarettes logged yet";
            }
            else
            {
                TimeSpan since = now - last.Value;
                string when = since.TotalHours >= 24 ? "1d+" : since.TotalMinutes < 1 ? "just now" : Stats.Short(since);
                text.Text = when + "  ·  #" + today + " today";
            }
            if (!IsVisible) Show();
        }
    }
}

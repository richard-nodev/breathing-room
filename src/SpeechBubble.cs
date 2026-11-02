using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BreathingRoom
{
    // The cat's speech bubble, in its own small window that exists only while the cat is talking.
    // Keeping it out of the cat's window lets that always-on window stay tiny: a transparent window
    // is redrawn in full on every animation frame, so its size is what costs CPU.
    public class SpeechBubble : Window
    {
        const double MaxBubbleWidth = 272, Shadow = 14, TailWidth = 18;

        readonly Grid outer = new Grid();
        readonly Border bubble;
        readonly StackPanel body = new StackPanel();
        readonly Path tail;
        bool open;

        public bool IsOpen { get { return open; } }

        public SpeechBubble()
        {
            Title = "Breathing Room bubble";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            bubble = new Border
            {
                Background = Ui.B("#FFFDF8"),
                BorderBrush = Ui.B("#EBCFB0"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 10, 14, 11),
                MaxWidth = MaxBubbleWidth,
                Child = body,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.22, Color = Ui.C("#5A3A20") }
            };
            tail = new Path
            {
                Data = Geometry.Parse("M 0 0 L 9 11 L 18 0"),
                Fill = Ui.B("#FFFDF8"),
                Stroke = Ui.B("#EBCFB0"),
                StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, -1.6, 0, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(bubble);
            stack.Children.Add(tail);
            outer.Margin = new Thickness(Shadow, Shadow, Shadow, 0);
            outer.Children.Add(stack);
            Content = outer;

            SourceInitialized += delegate { Native.MakeOverlay(this, false); };
        }

        static Rect Work { get { return SystemParameters.WorkArea; } }

        // anchor: the screen point (DIPs) the tail should point at, the top of the cat's head.
        public void Open(IEnumerable<UIElement> items, Point anchor)
        {
            body.Children.Clear();
            foreach (UIElement item in items) body.Children.Add(item);
            open = true;
            Place(anchor);
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            if (!IsVisible) Show();
            Native.KeepOnTop(this);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        public void Place(Point anchor)
        {
            Rect wa = Work;
            bubble.MaxWidth = Math.Max(160, Math.Min(MaxBubbleWidth, wa.Width - 2 * Shadow - 8));
            // The tail's old offset (from a wider bubble) must not widen this measurement.
            tail.Margin = new Thickness(0, -1.6, 0, 0);
            outer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // DesiredSize includes the shadow margin around the bubble (none below the tail).
            double w = outer.DesiredSize.Width, h = outer.DesiredSize.Height;
            double left = Math.Max(wa.Left - Shadow + 4, Math.Min(wa.Right - w + Shadow - 4, anchor.X - w / 2));
            double top = Math.Max(wa.Top - Shadow, anchor.Y - h);
            Left = left;
            Top = top;
            double bubbleWidth = w - 2 * Shadow;
            double tailX = anchor.X - left - Shadow - TailWidth / 2;
            tail.Margin = new Thickness(Math.Max(14, Math.Min(bubbleWidth - TailWidth - 14, tailX)), -1.6, 0, 0);
        }

        public void FadeOut()
        {
            if (!open) return;
            open = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += delegate { if (!open) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        }

        public void HideNow()
        {
            open = false;
            BeginAnimation(OpacityProperty, null);
            Hide();
        }

        // For README pictures: the bubble with these items, taken out of the window (never shown),
        // laid out with the tail pointing at its middle. Width and height include the shadow margin.
        internal FrameworkElement Detach(IEnumerable<UIElement> items, out double width, out double height)
        {
            body.Children.Clear();
            foreach (UIElement item in items) body.Children.Add(item);
            Content = null;
            outer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = outer.DesiredSize.Width;
            height = outer.DesiredSize.Height;
            tail.Margin = new Thickness((width - 2 * Shadow) / 2 - TailWidth / 2, -1.6, 0, 0);
            return outer;
        }
    }
}

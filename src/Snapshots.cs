using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreathingRoom
{
    // Test mode only ("snap" in command.txt): renders the app's own windows straight to PNG for the
    // README, so the pictures never contain anything from the desktop behind them.
    public static class Snapshots
    {
        const double Scale = 1.5;

        public static void All(string dir)
        {
            Directory.CreateDirectory(dir);
            Hero(Path.Combine(dir, "hero.png"));
            Bubble(Path.Combine(dir, "bubble-log.png"), "Huh, 6 min since the last one. #7 today.", null, "changed my mind");
            Bubble(Path.Combine(dir, "bubble-question.png"), "Did you decide on this one, or did your hand?", new[] { "I decided", "My hand did" }, null);
            Bubble(Path.Combine(dir, "bubble-slip.png"), "Welcome back. Did you have one while you were away?", new[] { "Nope", "Yeah, I did" }, null);
            Page(Path.Combine(dir, "insights.png"), new InsightsWindow(null).Window, 560, 1010);
            Page(Path.Combine(dir, "settings.png"), new SettingsWindow(null).Window, 560);
            Page(Path.Combine(dir, "voice.png"), SoundPickerWindow.Create(), 880);
            Page(Path.Combine(dir, "about.png"), AboutWindow.Create(), 500);
            Page(Path.Combine(dir, "welcome.png"), WelcomeWindow.CreateForSnapshot(), 520);
            Page(Path.Combine(dir, "look.png"), LookWindow.Create(), 640);
            Page(Path.Combine(dir, "wave.png"), WaveWindow.CreateForSnapshot(85), 580);
            Play(Path.Combine(dir, "play-fetch.png"), "fetch", "living", null, 1.1);
            Play(Path.Combine(dir, "play-laser.png"), "laser", "garden", new Point(420, 400), 1.45);
            Play(Path.Combine(dir, "play-treats.png"), "treats", "night", null, 4.5);
            Play(Path.Combine(dir, "play-bubbles.png"), "bubbles", "cafe", null, 7.0);
            Play(Path.Combine(dir, "play-mouse.png"), "mouse", "living", new Point(500, 450), 1.5);
            Play(Path.Combine(dir, "play-hide.png"), "hide", "garden", new Point(500, 450), 3.0);
            Play(Path.Combine(dir, "play-flight.png"), "flight", "night", new Point(300, 260), 9.0);
            Play(Path.Combine(dir, "play-run.png"), "run", "night", null, 2.0);
            Play(Path.Combine(dir, "play-cafe.png"), "cafe", "cafe", new Point(640, 300), 7.5);
        }

        static void Save(Visual visual, double width, double height, Brush background, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            if (background != null)
            {
                var back = new DrawingVisual();
                using (var dc = back.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
                bitmap.Render(back);
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }

        // Twice, because some elements (the speech bubble) position themselves after their first measure.
        static void Layout(FrameworkElement e, double width, double height)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                e.Measure(new Size(width, double.IsNaN(height) ? double.PositiveInfinity : height));
                e.Arrange(new Rect(0, 0, width, double.IsNaN(height) ? e.DesiredSize.Height : height));
                e.UpdateLayout();
            }
        }

        // A whole scrolling page (margins included), cut at maxHeight if it is longer.
        static void Page(string path, Window w, double width, double maxHeight)
        {
            var scroller = w.Content as ScrollViewer;
            var page = (FrameworkElement)(scroller != null ? scroller.Content : w.Content);
            if (scroller != null) scroller.Content = null; else w.Content = null;
            Layout(page, width, double.NaN);
            Save(page, width, Math.Min(maxHeight, page.DesiredSize.Height), Ui.Paper, path);
            w.Close();
        }

        static void Page(string path, Window w, double width)
        {
            Page(path, w, width, double.MaxValue);
        }

        static Canvas PetWithBubble(string text, string[] chips, string link)
        {
            var pet = new PetWindow();
            pet.Left = 300;
            pet.Top = 300;
            pet.Ask(text, chips, null, 30, link, null);
            Canvas composed = pet.Compose();
            pet.Close();
            return composed;
        }

        // The cat and its bubble on a dark "desktop", cropped to what is actually drawn.
        static void Bubble(string path, string text, string[] chips, string link)
        {
            var root = PetWithBubble(text, chips, link);
            var host = new Canvas();
            host.Children.Add(root);
            Layout(host, 600, 500);
            Rect drawn = VisualTreeHelper.GetDescendantBounds(root);
            const double pad = 28;
            double w = Math.Ceiling(drawn.Width + 2 * pad), h = Math.Ceiling(drawn.Height + 2 * pad);
            Canvas.SetLeft(root, pad - drawn.X);
            Canvas.SetTop(root, pad - drawn.Y);
            host.Width = w;
            host.Height = h;
            Layout(host, w, h);
            Save(host, w, h, new LinearGradientBrush(Ui.C("#2E3448"), Ui.C("#1C2030"), 90), path);
        }

        static void Hero(string path)
        {
            const double w = 1280, h = 640;
            var host = new Canvas { Width = w, Height = h };
            host.Background = new LinearGradientBrush(Ui.C("#FFF6EA"), Ui.C("#FFE2C4"), 90);

            var glow = new Border
            {
                Width = w,
                Height = 34,
                Background = new LinearGradientBrush(Color.FromArgb(235, 255, 150, 60), Color.FromArgb(0, 255, 190, 110), 90)
            };
            host.Children.Add(glow);

            var text = new StackPanel { Width = 700 };
            var title = Ui.Heading("Breathing Room", 72);
            text.Children.Add(title);
            var tagline = Ui.Text("A desk cat that helps you notice your smoking.\nNo rules. No lectures. Just what it sees.", 28, Ui.Soft);
            tagline.Margin = new Thickness(4, 6, 0, 30);
            tagline.LineHeight = 42;
            text.Children.Add(tagline);
            var chips = new WrapPanel();
            foreach (string chip in new[] { "One key to log", "Facts, not rules", "Real purrs", "100% private", "Free & open source" })
            {
                chips.Children.Add(new Border
                {
                    Background = Ui.B("#FFE9D0"),
                    BorderBrush = Ui.B("#F2C08A"),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(18, 7, 18, 8),
                    Margin = new Thickness(0, 0, 12, 12),
                    Child = Ui.Text(chip, 21, Ui.Ink)
                });
            }
            text.Children.Add(chips);
            Canvas.SetLeft(text, 80);
            Canvas.SetTop(text, 150);
            host.Children.Add(text);

            var pet = PetWithBubble("12 min since the last one. That's #7 today.", null, "changed my mind");
            pet.RenderTransform = new ScaleTransform(1.65, 1.65);
            Canvas.SetLeft(pet, 720);
            Canvas.SetTop(pet, 110);
            host.Children.Add(pet);

            Layout(host, w, h);
            Save(host, w, h, null, path);
        }

        static void Play(string path, string game, string scene, Point? mouse, double seconds)
        {
            var room = new PlayWindow(game, scene);
            room.Simulate(mouse, seconds);
            var root = (FrameworkElement)room.Content;
            room.Content = null;
            room.Close();
            Layout(root, root.Width, root.Height);
            Save(root, root.Width, root.Height, null, path);
        }
    }
}

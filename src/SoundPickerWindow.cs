using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreathingRoom
{
    // "Mochi's voice": listen to every recording and tick when the cat may use it. Only the user can
    // tell which ones sound like a cat, so nothing here is decided for them beyond the defaults.
    public class SoundPickerWindow
    {
        static readonly string[] MeowHeaders = { "When you log one", "Only a few min since the last", "Picked up & playing", "Happy (menu, catches)", "Waking up" };
        static SoundPickerWindow open;

        readonly Window window;
        readonly StackPanel page = new StackPanel { Margin = new Thickness(26, 20, 26, 24) };

        // For README pictures: the window, built but not shown.
        internal static Window Create()
        {
            return new SoundPickerWindow().window;
        }

        public static void Show()
        {
            if (open != null)
            {
                if (open.window.WindowState == WindowState.Minimized) open.window.WindowState = WindowState.Normal;
                open.window.Activate();
                return;
            }
            open = new SoundPickerWindow();
            open.window.Show();
            open.window.Activate();
        }

        SoundPickerWindow()
        {
            window = Ui.PaperWindow(Store.Data.Settings.PetName + "'s voice", 880, 800);
            window.ResizeMode = ResizeMode.CanResize;
            window.MinWidth = 640;
            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            window.Closed += delegate { Audio.StopLoop("preview", 0.3); open = null; };
            Build();
        }

        void Build()
        {
            page.Children.Clear();
            string name = Store.Data.Settings.PetName;
            page.Children.Add(Ui.Heading(name + "'s voice", 24));
            var intro = Ui.Text("Press ▶ to listen. Tick when " + name + " may use each sound. Unticked sounds never play, and if nothing is ticked for a moment, " + name + " stays quiet then. Changes apply right away.", 13.5, Ui.Soft);
            intro.Margin = new Thickness(0, 4, 0, 4);
            page.Children.Add(intro);
            if (!Store.Data.Settings.CatSounds)
                page.Children.Add(Note("Cat sounds are switched off in Settings, so you'll only hear these here."));

            var meows = Sfx.Pool.Where(c => c.Kind == "meow").ToList();
            page.Children.Add(Section("Meows"));
            var grid = Table(MeowHeaders);
            foreach (SoundClip c in meows) AddRow(grid, c, Sfx.MeowMoments);
            page.Children.Add(grid);

            page.Children.Add(Section("Purr, when you pet " + name));
            var purrs = Table(new[] { "Use" });
            foreach (SoundClip c in Sfx.Pool.Where(x => x.Kind == "purr")) AddRow(purrs, c, new[] { "purr" });
            page.Children.Add(purrs);

            var credit = Ui.Text("All of these are real cat recordings by Joseph Sardin (BigSoundBank, CC0 public domain).", 12, Ui.Soft);
            credit.Margin = new Thickness(0, 12, 0, 0);
            page.Children.Add(credit);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            buttons.Children.Add(Ui.Primary("Done", delegate { window.Close(); }));
            var reset = Ui.Pill("Back to the defaults", delegate { Sfx.ResetPicks(); Build(); });
            reset.Margin = new Thickness(12, 0, 0, 0);
            reset.Padding = new Thickness(14, 7, 14, 7);
            buttons.Children.Add(reset);
            page.Children.Add(buttons);
        }

        static TextBlock Section(string title)
        {
            var t = Ui.Heading(title, 16);
            t.Margin = new Thickness(0, 18, 0, 6);
            return t;
        }

        static TextBlock Note(string text)
        {
            var t = Ui.Text(text, 12.5, Ui.OrangeDeep);
            t.Margin = new Thickness(0, 2, 0, 2);
            return t;
        }

        static Grid Table(string[] headers)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
            foreach (string h in headers) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(headers.Length == 1 ? 80 : 96) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < headers.Length; i++)
            {
                var t = Ui.Text(headers[i], 12, Ui.Soft);
                t.TextAlignment = TextAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Bottom;
                t.Margin = new Thickness(4, 0, 4, 4);
                Grid.SetColumn(t, i + 2);
                g.Children.Add(t);
            }
            return g;
        }

        // One sound: play button, label, and a tick box per moment.
        static void AddRow(Grid g, SoundClip clip, string[] moments)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (r % 2 == 1)
            {
                var stripe = new Rectangle { Fill = Ui.PaperDeep, RadiusX = 8, RadiusY = 8 };
                Grid.SetRow(stripe, r);
                Grid.SetColumnSpan(stripe, g.ColumnDefinitions.Count);
                g.Children.Add(stripe);
            }

            var play = Ui.Pill("▶", delegate { Sfx.Preview(Sfx.Load(clip), clip.Kind == "purr"); });
            play.Margin = new Thickness(4, 4, 4, 4);
            play.Padding = new Thickness(10, 3, 10, 3);
            Grid.SetRow(play, r);
            g.Children.Add(play);

            var text = Ui.Text(clip.Label, 13.5, Ui.Ink);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetRow(text, r);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            string id = clip.Id;
            for (int i = 0; i < moments.Length; i++)
            {
                string moment = moments[i];
                var box = new CheckBox
                {
                    IsChecked = Sfx.IsPicked(moment, id),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    LayoutTransform = new ScaleTransform(1.25, 1.25)
                };
                box.Checked += delegate { Sfx.SetPicked(moment, id, true); };
                box.Unchecked += delegate { Sfx.SetPicked(moment, id, false); };
                Grid.SetRow(box, r);
                Grid.SetColumn(box, i + 2);
                g.Children.Add(box);
            }
        }
    }
}

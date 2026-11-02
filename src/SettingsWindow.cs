using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreathingRoom
{
    public class SettingsWindow
    {
        public static readonly string[] Hotkeys = { "Ctrl+Alt+S", "Ctrl+Alt+C", "Ctrl+Shift+S", "Ctrl+Alt+Space", "Ctrl+Alt+F12", "Pause" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly Window window;
        readonly Action applied;
        readonly StackPanel page = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
        Grid grid;
        readonly TextBox name, price, pack, currency;
        readonly CheckBox showPet, glow, badge, catSounds, startup, rememberApps, updates;
        readonly Slider volume;
        readonly ComboBox glowEdge, badgeCorner, hotkey, checkins, voice, breaks;
        bool saved;

        public Window Window { get { return window; } }

        public SettingsWindow(Action onApplied)
        {
            applied = onApplied;
            Settings s = Store.Data.Settings;
            window = Ui.PaperWindow("Breathing Room settings", 560, 820);
            page.Children.Add(Ui.Heading("Settings", 24));

            Section(s.PetName);
            name = Box(s.PetName);
            Row("Name", name);
            showPet = Check("Show on screen", s.ShowPet);
            Row("", showPet);
            breaks = Combo(new[] { "After 45 min at the PC", "After 1 hour", "After 90 min", "Never" }, new[] { "45", "60", "90", "0" }, s.BreakMinutes.ToString(Inv));
            Row("Stretch breaks", breaks);

            Section("On screen");
            glow = Check("A glow that fades after each one", s.Afterglow);
            Row("Afterglow", glow);
            glowEdge = Combo(new[] { "Top of the screen", "Bottom of the screen" }, new[] { "top", "bottom" }, s.AfterglowEdge);
            Row("", glowEdge);
            badge = Check("Tiny “12m · #7 today” pill", s.Badge);
            Row("Corner badge", badge);
            badgeCorner = Combo(new[] { "Top right", "Top left", "Bottom right", "Bottom left" },
                                new[] { "topright", "topleft", "bottomright", "bottomleft" }, s.BadgeCorner);
            Row("", badgeCorner);

            Section("Logging");
            hotkey = Combo(Hotkeys, Hotkeys, s.Hotkey);
            Row("Log key", hotkey);
            checkins = Combo(new[] { "Sometimes", "Often", "Never" }, new[] { "sometimes", "often", "never" }, s.Checkins);
            Row("Little questions", checkins);
            rememberApps = Check("Remember which app was in front", s.RememberApps);
            Row("Context", rememberApps);
            Row("", Note("Shows you patterns like “8 of 12 were lit while a game was open”. Stays on this PC."));

            Section("Sound");
            catSounds = Check("Purrs and meows", s.CatSounds);
            Row("Cat sounds", catSounds);
            volume = new Slider { Minimum = 0, Maximum = 100, Value = s.Volume, SmallChange = 5, LargeChange = 10 };
            volume.ValueChanged += delegate { Audio.Master = (float)(volume.Value / 100); };
            Row("Volume", volume);
            var soundLinks = new StackPanel { Orientation = Orientation.Horizontal };
            soundLinks.Children.Add(Ui.Link("Choose " + s.PetName + "'s sounds…", delegate { SoundPickerWindow.Show(); }));
            var sample = Ui.Link("Play a sample", delegate
            {
                bool cat = Store.Data.Settings.CatSounds;
                Store.Data.Settings.CatSounds = true;
                Sfx.Trill();
                Store.Data.Settings.CatSounds = cat;
            });
            sample.Margin = new Thickness(18, 7, 0, 0);
            soundLinks.Children.Add(sample);
            Row("", soundLinks);
            voice = Combo(new[] { "Off", "Only when I can't see " + s.PetName, "Always" }, new[] { "off", "auto", "always" }, s.Voice);
            Row("Spoken facts", voice);
            Row("", Note("Uses the Windows voice. Handy in exclusive-fullscreen games, which hide every window."));

            Section("Startup and updates");
            startup = Check("Start with Windows", Startup.IsEnabled());
            Row("", startup);
            updates = Check("Tell me when a new version is out", s.CheckUpdates);
            Row("", updates);
            Row("", Note("About once a day it asks GitHub whether there's a newer version. Nothing about you is sent."));

            Section("Money (optional)");
            Row("", Note("Only used for one line in “How it's going”."));
            price = Box(s.PackPrice > 0 ? s.PackPrice.ToString(Inv) : "");
            Row("Price of a pack", price);
            pack = Box(s.PackSize.ToString(Inv));
            Row("Cigarettes per pack", pack);
            currency = Box(s.Currency);
            Row("Currency symbol", currency);

            Section("Your data");
            Row("", Note("Everything stays on this PC. No account, no tracking."));
            var dataLinks = new StackPanel { Orientation = Orientation.Horizontal };
            dataLinks.Children.Add(Ui.Link("Export to CSV…", delegate { DataTools.Export(window); }));
            var folder = Ui.Link("Open data folder", delegate { DataTools.OpenFolder(Store.Dir); });
            folder.Margin = new Thickness(18, 7, 0, 0);
            dataLinks.Children.Add(folder);
            var wipe = Ui.Link("Delete my history…", delegate { if (DataTools.DeleteHistory(window) && applied != null) applied(); });
            wipe.Margin = new Thickness(18, 7, 0, 0);
            dataLinks.Children.Add(wipe);
            Row("", dataLinks);

            var buttons = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
            buttons.Children.Add(Ui.Primary("Save", delegate { saved = true; Save(); }));
            var cancel = Ui.Pill("Cancel", delegate { window.Close(); });
            cancel.Margin = new Thickness(10, 0, 0, 0);
            cancel.Padding = new Thickness(16, 7, 16, 7);
            buttons.Children.Add(cancel);
            var about = Ui.Link("About Breathing Room", delegate { AboutWindow.Show(); });
            about.VerticalAlignment = VerticalAlignment.Center;
            about.Margin = new Thickness(0, 0, 0, 0);
            DockPanel.SetDock(about, Dock.Right);
            buttons.Children.Add(about);
            page.Children.Add(buttons);

            window.Closed += delegate { if (!saved) Audio.Master = Store.Data.Settings.Volume / 100f; };
            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        void Section(string title)
        {
            var t = Ui.Heading(title, 15.5);
            t.Margin = new Thickness(0, 18, 0, 4);
            page.Children.Add(t);
            grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            page.Children.Add(grid);
        }

        static TextBlock Note(string text)
        {
            return Ui.Text(text, 12, Ui.Soft);
        }

        void Row(string label, FrameworkElement control)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Text(label, 13.5, Ui.Soft);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 6, 10, 6);
            Grid.SetRow(l, r);
            grid.Children.Add(l);
            control.Margin = new Thickness(0, control is TextBlock ? 0 : 4, 0, control is TextBlock ? 4 : 4);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        static TextBox Box(string value)
        {
            return new TextBox { Text = value, FontSize = 13.5, Padding = new Thickness(6, 4, 6, 4), BorderBrush = Ui.Line, Background = Brushes.White };
        }

        static CheckBox Check(string text, bool value)
        {
            return new CheckBox { Content = text, IsChecked = value, FontSize = 13.5, Foreground = Ui.Ink, VerticalContentAlignment = VerticalAlignment.Center };
        }

        static ComboBox Combo(string[] labels, string[] values, string current)
        {
            var c = new ComboBox { FontSize = 13.5 };
            for (int i = 0; i < labels.Length; i++)
            {
                c.Items.Add(new ComboBoxItem { Content = labels[i], Tag = values[i] });
                if (values[i] == current) c.SelectedIndex = i;
            }
            if (c.SelectedIndex < 0) c.SelectedIndex = 0;
            return c;
        }

        static string Value(ComboBox c)
        {
            var item = c.SelectedItem as ComboBoxItem;
            return item == null ? null : (string)item.Tag;
        }

        void Save()
        {
            Settings s = Store.Data.Settings;
            s.PetName = string.IsNullOrWhiteSpace(name.Text) ? "Mochi" : name.Text.Trim();
            s.ShowPet = showPet.IsChecked == true;
            s.BreakMinutes = int.Parse(Value(breaks), Inv);
            s.Afterglow = glow.IsChecked == true;
            s.AfterglowEdge = Value(glowEdge);
            s.Badge = badge.IsChecked == true;
            s.BadgeCorner = Value(badgeCorner);
            s.Hotkey = Value(hotkey);
            s.Checkins = Value(checkins);
            s.RememberApps = rememberApps.IsChecked == true;
            s.Voice = Value(voice);
            s.CatSounds = catSounds.IsChecked == true;
            s.Volume = (int)Math.Round(volume.Value);

            double p;
            s.PackPrice = double.TryParse(price.Text.Replace(',', '.'), NumberStyles.Float, Inv, out p) && p > 0 ? p : 0;
            int n;
            s.PackSize = int.TryParse(pack.Text, out n) && n > 0 ? n : 20;
            s.Currency = string.IsNullOrWhiteSpace(currency.Text) ? "$" : currency.Text.Trim();

            s.CheckUpdates = updates.IsChecked == true;
            Startup.Set(startup.IsChecked == true);
            Store.Save();
            window.Close();
            if (applied != null) applied();
        }
    }
}

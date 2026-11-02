using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreathingRoom
{
    // First run: one screen, one promise. No questionnaire.
    public class WelcomeWindow
    {
        readonly Window window;
        readonly TextBox name;
        readonly CheckBox startup;
        bool finished;

        public WelcomeWindow(Action done)
        {
            window = Ui.PaperWindow("Breathing Room", 520, 660);
            var page = new StackPanel { Margin = new Thickness(36, 18, 36, 26) };

            var cat = new CatView { LayoutTransform = new ScaleTransform(1.25, 1.25), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 6) };
            page.Children.Add(cat);
            var hi = Ui.Heading("Hi. I'm Mochi.", 26);
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            hi.Margin = new Thickness(0, 0, 0, 16);
            page.Children.Add(hi);

            string key = Store.Data.Settings.Hotkey;
            page.Children.Add(Para("I'll hang out in the corner of your screen. Whenever you light one, press " + key + ". That's the whole deal."));
            page.Children.Add(Para("I won't tell you when to smoke, or when not to. I'll just tell you what I notice, and once in a while ask you something. Ignore me whenever you like."));
            page.Children.Add(Para("Tip: stick a little note that says " + key + " on your lighter. Hands remember better than heads."));

            var nameRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var label = Ui.Text("Call me", 14, Ui.Soft);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 10, 0);
            nameRow.Children.Add(label);
            name = new TextBox { Text = "Mochi", FontSize = 14, Padding = new Thickness(6, 4, 6, 4), BorderBrush = Ui.Line, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            nameRow.Children.Add(name);
            page.Children.Add(nameRow);

            startup = new CheckBox { Content = "Start with Windows", IsChecked = true, FontSize = 14, Foreground = Ui.Ink, Margin = new Thickness(0, 14, 0, 0) };
            page.Children.Add(startup);

            var go = Ui.Primary("Let's go", delegate { Finish(); });
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 22, 0, 0);
            go.IsDefault = true;
            page.Children.Add(go);

            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            window.Closed += delegate
            {
                if (!finished) Finish();
                if (done != null) done();
            };
        }

        static TextBlock Para(string text)
        {
            var t = Ui.Text(text, 15, Ui.Ink);
            t.Margin = new Thickness(0, 0, 0, 12);
            t.LineHeight = 22;
            return t;
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            string n = name.Text == null ? "" : name.Text.Trim();
            Store.Data.Settings.PetName = n.Length == 0 ? "Mochi" : n;
            Store.Data.Welcomed = true;
            Startup.Set(startup.IsChecked == true);
            Store.Save();
            if (window.IsVisible) window.Close();
        }

        // For README pictures: the window, built but not shown, and closing it changes nothing.
        internal static Window CreateForSnapshot()
        {
            var welcome = new WelcomeWindow(null);
            welcome.finished = true;
            return welcome.window;
        }

        public void Show()
        {
            window.Show();
            window.Activate();
        }
    }
}

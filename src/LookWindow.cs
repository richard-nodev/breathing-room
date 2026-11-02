using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreathingRoom
{
    // "Mochi's look": fur colors and accessories, unlocked by friendship (time spent together).
    public class LookWindow
    {
        public static event Action Changed;
        static LookWindow open;

        readonly Window window;
        readonly StackPanel page = new StackPanel { Margin = new Thickness(28, 20, 28, 24) };
        readonly CatView preview = new CatView { LayoutTransform = new ScaleTransform(1.5, 1.5), Margin = new Thickness(6, 24, 26, 0) };

        public static void Show()
        {
            if (open != null)
            {
                if (open.window.WindowState == WindowState.Minimized) open.window.WindowState = WindowState.Normal;
                open.window.Activate();
                return;
            }
            open = new LookWindow();
            open.window.Show();
            open.window.Activate();
        }

        internal static Window Create()
        {
            return new LookWindow().window;
        }

        LookWindow()
        {
            window = Ui.PaperWindow(Store.Data.Settings.PetName + "'s look", 640, 640);
            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            window.Closed += delegate { open = null; };
            Build();
        }

        void Build()
        {
            page.Children.Clear();
            string name = Store.Data.Settings.PetName;

            var top = new DockPanel();
            DetachPreview();
            top.Children.Add(preview);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Heading(name + "'s look", 24));
            int level = Friendship.Level;
            var hearts = new TextBlock { FontSize = 22, Margin = new Thickness(0, 8, 0, 2) };
            for (int i = 1; i <= Friendship.Thresholds.Length; i++)
                hearts.Inlines.Add(new System.Windows.Documents.Run("♥ ") { Foreground = i <= level ? Ui.B("#F2708A") : Ui.B("#EBD9C6") });
            info.Children.Add(hearts);
            info.Children.Add(Ui.Heading("Friendship level " + level, 15));
            string next = Friendship.ToNext > 0 ? Friendship.ToNext + " more to level " + (level + 1) + "." : "The highest level. Best friends.";
            info.Children.Add(Ui.Text(next, 13, Ui.Soft));
            var why = Ui.Text("It grows when you pet " + name + ", play, breathe together and answer its little questions. Never from how much or how little you smoke.", 12.5, Ui.Soft);
            why.Margin = new Thickness(0, 8, 0, 0);
            info.Children.Add(why);
            top.Children.Add(info);
            page.Children.Add(top);

            page.Children.Add(Section("Coat"));
            page.Children.Add(Choices(Looks.Coats, Store.Data.Settings.CatCoat, true));
            page.Children.Add(Section("Accessory"));
            page.Children.Add(Choices(Looks.Accessories, Store.Data.Settings.CatAccessory, false));

            var done = Ui.Primary("Done", delegate { window.Close(); });
            done.HorizontalAlignment = HorizontalAlignment.Left;
            done.Margin = new Thickness(0, 22, 0, 0);
            page.Children.Add(done);
        }

        void DetachPreview()
        {
            var parent = preview.Parent as Panel;
            if (parent != null) parent.Children.Remove(preview);
        }

        static TextBlock Section(string title)
        {
            var t = Ui.Heading(title, 16);
            t.Margin = new Thickness(0, 20, 0, 6);
            return t;
        }

        WrapPanel Choices(List<LookItem> items, string current, bool coat)
        {
            var panel = new WrapPanel();
            foreach (LookItem item in items)
            {
                LookItem it = item;
                bool unlocked = Looks.Unlocked(it);
                var b = Ui.Pill(unlocked ? it.Name : it.Name + "  ·  level " + it.Level, delegate
                {
                    if (coat) Store.Data.Settings.CatCoat = it.Id; else Store.Data.Settings.CatAccessory = it.Id;
                    Store.Save();
                    CatView.Coat = Store.Data.Settings.CatCoat;
                    CatView.Accessory = Store.Data.Settings.CatAccessory;
                    preview.ApplyLook();
                    if (Changed != null) Changed();
                    Build();
                });
                b.FontSize = 13.5;
                b.Padding = new Thickness(14, 6, 14, 6);
                b.Margin = new Thickness(0, 0, 8, 8);
                b.IsEnabled = unlocked;
                if (!unlocked) b.Opacity = 0.5;
                if (it.Id == current)
                {
                    b.Background = Ui.Orange;
                    b.FontWeight = FontWeights.SemiBold;
                }
                panel.Children.Add(b);
            }
            return panel;
        }
    }
}

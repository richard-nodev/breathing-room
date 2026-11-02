using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreathingRoom
{
    public static class AboutWindow
    {
        static Window open;

        public static void Show()
        {
            if (open != null)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            var w = Create();
            w.Closed += delegate { open = null; };
            open = w;
            w.Show();
            w.Activate();
        }

        internal static Window Create()
        {
            var w = Ui.PaperWindow("About " + AppInfo.Name, 500, 640);
            var page = new StackPanel { Margin = new Thickness(32, 18, 32, 24) };

            var cat = new CatView { LayoutTransform = new ScaleTransform(0.9, 0.9), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            page.Children.Add(cat);
            var title = Ui.Heading(AppInfo.Name, 24);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            page.Children.Add(title);
            var version = Ui.Text("Version " + AppInfo.Version + "  ·  free and open source (MIT)", 12.5, Ui.Soft);
            version.HorizontalAlignment = HorizontalAlignment.Center;
            page.Children.Add(version);
            var tagline = Ui.Text("A desk cat that helps you notice your smoking. No rules, no lectures. Just what it sees.", 14, Ui.Ink);
            tagline.TextAlignment = TextAlignment.Center;
            tagline.Margin = new Thickness(0, 12, 0, 4);
            page.Children.Add(tagline);

            if (Updater.Enabled) Updates(w, page);

            Section(page, "Private by design",
                "Everything stays on this PC, in one file you can export or delete from Settings. No account, no tracking. " +
                (Updater.Enabled ? "The only thing it ever asks the internet is whether a new version is out (about once a day; you can turn that off in Settings)." : "It never goes online."));
            Section(page, "Not medical advice",
                "Breathing Room is a noticing tool, not a treatment. If you ever want help to cut down or quit, free and friendly support exists: your local quitline or doctor is a good start.");
            Section(page, "Thanks",
                "Cat recordings by Joseph Sardin (BigSoundBank), released as CC0 public domain. Drawn, coded and named with care.");

            if (!string.IsNullOrEmpty(AppInfo.ProjectUrl))
            {
                var link = Ui.Link("Project page", delegate
                {
                    try { System.Diagnostics.Process.Start(AppInfo.ProjectUrl); } catch (Exception ex) { Store.Log("Open link failed: " + ex.Message); }
                });
                link.Margin = new Thickness(0, 12, 0, 0);
                page.Children.Add(link);
            }

            var close = Ui.Primary("Close", delegate { w.Close(); });
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Margin = new Thickness(0, 20, 0, 0);
            close.IsCancel = true;
            page.Children.Add(close);

            w.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return w;
        }

        // "Check for updates", and "Update now" once a newer version is known.
        static void Updates(Window w, Panel page)
        {
            var h = Ui.Heading("Updates", 14.5);
            h.Margin = new Thickness(0, 16, 0, 2);
            page.Children.Add(h);
            var status = Ui.Text("", 13.5, Ui.Soft);
            page.Children.Add(status);
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            page.Children.Add(row);
            Action<Updater.Release, string> show = null;
            show = (release, error) =>
            {
                row.Children.Clear();
                if (release != null)
                {
                    status.Text = "Version " + release.Version + " is out. Updating takes a few seconds and keeps all your data.";
                    var now = Ui.Primary("Update now", delegate
                    {
                        status.Text = "Updating… I'll restart in a moment.";
                        row.Children.Clear();
                        if (Updater.Install != null) Updater.Install(release);
                    });
                    row.Children.Add(now);
                    var notes = Ui.Link("What's new", delegate { Updater.OpenPage(release); });
                    notes.Margin = new Thickness(16, 7, 0, 0);
                    row.Children.Add(notes);
                    return;
                }
                status.Text = error != null ? "Couldn't check just now (" + error + ")." : "You have the latest version.";
                row.Children.Add(Ui.Link("Check for updates", delegate
                {
                    status.Text = "Checking…";
                    row.Children.Clear();
                    Updater.Check((r, e) => show(r, e));
                }));
            };
            // If the update doesn't go through, say so here too and offer it again.
            Action<string> failed = error =>
            {
                Updater.Release r = Updater.Available;
                show(r, null);
                if (r != null) status.Text = "That didn't work (" + error + "). Nothing changed, so you can try again.";
            };
            Updater.Failed += failed;
            w.Closed += delegate { Updater.Failed -= failed; };
            if (Updater.Available != null) show(Updater.Available, null);
            else
            {
                status.Text = "This is version " + AppInfo.Version + ".";
                row.Children.Add(Ui.Link("Check for updates", delegate
                {
                    status.Text = "Checking…";
                    row.Children.Clear();
                    Updater.Check((r, e) => show(r, e));
                }));
            }
        }

        static void Section(Panel page, string heading, string body)
        {
            var h = Ui.Heading(heading, 14.5);
            h.Margin = new Thickness(0, 16, 0, 2);
            page.Children.Add(h);
            page.Children.Add(Ui.Text(body, 13.5, Ui.Soft));
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreathingRoom
{
    // "How's it going": what the cat has noticed, in sentences. No scores, no grades.
    public class InsightsWindow
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly Window window;
        readonly StackPanel page = new StackPanel { Margin = new Thickness(28, 22, 28, 26) };
        readonly Action changed;
        DockPanel header;
        TextBlock subtitle;

        public InsightsWindow(Action onChanged)
        {
            changed = onChanged;
            window = Ui.PaperWindow("How's it going", 560, 760);
            window.ResizeMode = ResizeMode.CanResize;
            window.MinWidth = 460;
            window.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Build();

            // Never show an old picture: rebuild whenever the window comes back to the front, and
            // once a minute while it is open so "last one ... ago" keeps moving.
            // Only when something changed: rebuilding under the mouse would swallow the click
            // that activated the window (e.g. on "remove").
            window.Activated += delegate { if (Signature() != builtFor) Build(); };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += delegate { if (window.IsVisible && window.WindowState != WindowState.Minimized) Build(); };
            timer.Start();
            window.Closed += delegate { timer.Stop(); };
        }

        public Window Window { get { return window; } }

        public void Refresh()
        {
            Build();
        }

        string builtFor;

        // Changes whenever anything shown here would change (data, or the minute for "ago").
        static string Signature()
        {
            var smokes = Store.Data.Smokes;
            return smokes.Count + "|" + (smokes.Count > 0 ? smokes[smokes.Count - 1].Id + smokes[smokes.Count - 1].Taste + smokes[smokes.Count - 1].Decided : "") +
                   "|" + Store.Data.RodeOut.Count + "|" + Store.Data.ChangedMind.Count + "|" + DateTime.Now.ToString("yyyyMMddHHmm", Inv);
        }

        void Build()
        {
            builtFor = Signature();
            page.Children.Clear();
            DateTime now = DateTime.Now;
            var times = Store.Times();
            string name = Store.Data.Settings.PetName;

            // Built once: rebuilding happens often, and each new cat would keep its own timers.
            if (header == null)
            {
                header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                header.Children.Add(new CatView { LayoutTransform = new ScaleTransform(0.62, 0.62), Margin = new Thickness(-6, -8, 8, 0) });
                var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titles.Children.Add(Ui.Heading("How it's going", 24));
                subtitle = Ui.Text("", 13.5, Ui.Soft);
                titles.Children.Add(subtitle);
                header.Children.Add(titles);
            }
            subtitle.Text = "Just what " + name + " has noticed. No grades.";
            page.Children.Add(header);

            DateTime? last = Stats.Last(times, now);
            var cards = new UniformGrid3();
            cards.Add("Today", Stats.Today(times, now).ToString(Inv));
            cards.Add("Yesterday by now", Stats.YesterdayByNow(times, now).ToString(Inv));
            cards.Add("Last one", last == null ? "—" : Stats.Ago(now - last.Value));
            page.Children.Add(cards.Element);

            page.Children.Add(Section("Last 14 days"));
            page.Children.Add(Chart(Stats.DailyCounts(times, now, 14)));

            page.Children.Add(Section("When you smoke"));
            page.Children.Add(Ui.Text("The last four weeks, by day and hour. Darker means more.", 12.5, Ui.Soft));
            page.Children.Add(Heatmap(now));

            page.Children.Add(Section("What I've noticed"));
            foreach (string line in Insights.Build(Store.Data, now))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
                var dot = new Ellipse { Width = 7, Height = 7, Fill = Ui.Orange, Margin = new Thickness(2, 7, 11, 0), VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(dot);
                row.Children.Add(Ui.Text(line, 14.5, Ui.Ink));
                page.Children.Add(row);
            }

            page.Children.Add(Section("Recent log"));
            page.Children.Add(Ui.Text("Logged one by mistake? Remove it here.", 12.5, Ui.Soft));
            var recent = Store.Data.Smokes.OrderByDescending(s => s.At).Take(40).ToList();
            if (recent.Count == 0) page.Children.Add(Ui.Text("Nothing yet.", 13.5, Ui.Soft));
            foreach (var s in recent)
            {
                var ev = s;
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var remove = Ui.Link("remove", delegate
                {
                    // Asked first: the list shifts up after a removal, so a double-click would
                    // otherwise remove the next entry too.
                    var answer = MessageBox.Show(window, "Remove the one from " + Describe(ev, DateTime.Now) + "?",
                        "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (answer != MessageBoxResult.Yes) return;
                    Store.RemoveSmoke(ev.Id);
                    if (changed != null) changed();
                    Build();
                });
                remove.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(Ui.Text(Describe(ev, now), 13, Ui.Ink));
                page.Children.Add(row);
            }
        }

        static string Describe(SmokeEvent s, DateTime now)
        {
            // Shown as the clock said at the time, even after a daylight-saving change.
            DateTime wall = s.Wall;
            DateTime day = Stats.DayStart(wall);
            DateTime today = Stats.DayStart(now);
            string when = day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : wall.ToString("ddd d MMM", Inv);
            string text = when + " " + wall.ToString("HH:mm", Inv);
            if (!string.IsNullOrEmpty(s.App)) text += "  ·  " + Native.AppLabel(s.App);
            if (s.Retro) text += "  ·  added later";
            if (s.Decided == "hand") text += "  ·  hand did it";
            if (s.Decided == "decided") text += "  ·  decided";
            if (!string.IsNullOrEmpty(s.Taste)) text += "  ·  taste: " + (s.Taste == "didnt" ? "didn't notice" : s.Taste);
            if (!string.IsNullOrEmpty(s.Want)) text += "  ·  wanted: " + (s.Want == "alot" ? "a lot" : s.Want == "bit" ? "a bit" : "not really");
            if (!string.IsNullOrEmpty(s.Moment)) text += "  ·  " + (s.Moment == "bored" ? "waiting / bored" : s.Moment);
            return text;
        }

        // Days (Monday first) by hours of the wall clock; a late-night cigarette counts toward the
        // evening it belongs to, like everywhere else (days start at 05:00).
        static FrameworkElement Heatmap(DateTime now)
        {
            var counts = new int[7, 24];
            int max = 0;
            DateTime from = Stats.DayStart(now).AddDays(-27);
            foreach (SmokeEvent s in Store.Data.Smokes)
            {
                DateTime wall = s.Wall;
                DateTime day = Stats.DayStart(wall);
                if (day < from) continue;
                int row = ((int)day.DayOfWeek + 6) % 7;
                max = Math.Max(max, ++counts[row, wall.Hour]);
            }
            const double cell = 18.5, gap = 1.5, labelW = 30;
            var canvas = new Canvas { Width = labelW + 24 * cell, Height = 7 * cell + 20, Margin = new Thickness(0, 8, 0, 0) };
            string[] days = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
            Color empty = Ui.C("#F3E6D6"), low = Ui.C("#FFD9A8"), high = Ui.C("#D9622B");
            for (int r = 0; r < 7; r++)
            {
                var label = Ui.Text(days[r], 11, Ui.Soft);
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, r * cell + 1);
                canvas.Children.Add(label);
                for (int h = 0; h < 24; h++)
                {
                    int n = counts[r, h];
                    Color c = empty;
                    if (n > 0)
                    {
                        double k = max <= 1 ? 1 : (n - 1) / (double)(max - 1);
                        c = Color.FromRgb((byte)(low.R + (high.R - low.R) * k), (byte)(low.G + (high.G - low.G) * k), (byte)(low.B + (high.B - low.B) * k));
                    }
                    var box = new Rectangle
                    {
                        Width = cell - gap,
                        Height = cell - gap,
                        RadiusX = 3,
                        RadiusY = 3,
                        Fill = new SolidColorBrush(c),
                        ToolTip = days[r] + " " + h.ToString("00", Inv) + ":00  ·  " + n
                    };
                    Canvas.SetLeft(box, labelW + h * cell);
                    Canvas.SetTop(box, r * cell);
                    canvas.Children.Add(box);
                }
            }
            foreach (int h in new[] { 0, 6, 12, 18 })
            {
                var t = Ui.Text(h.ToString("00", Inv) + ":00", 10.5, Ui.Soft);
                Canvas.SetLeft(t, labelW + h * cell);
                Canvas.SetTop(t, 7 * cell + 3);
                canvas.Children.Add(t);
            }
            return canvas;
        }

        static TextBlock Section(string title)
        {
            var t = Ui.Heading(title, 16);
            t.Margin = new Thickness(0, 22, 0, 10);
            return t;
        }

        static FrameworkElement Chart(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<DateTime, int>> days)
        {
            const double height = 130, barGap = 6;
            int max = Math.Max(1, days.Max(d => d.Value));
            var grid = new Grid { Height = height + 34 };
            for (int i = 0; i < days.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                bool today = i == days.Count - 1;
                var col = new DockPanel { Margin = new Thickness(barGap / 2, 0, barGap / 2, 0) };
                var label = Ui.Text(days[i].Key.ToString("ddd", Inv).Substring(0, 2), 11, today ? Ui.Ink : Ui.Soft);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextWrapping = TextWrapping.NoWrap;
                DockPanel.SetDock(label, Dock.Bottom);
                col.Children.Add(label);

                var stack = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom };
                double barH = days[i].Value == 0 ? 2 : Math.Max(6, height * days[i].Value / max);
                var bar = new Border
                {
                    Height = barH,
                    CornerRadius = new CornerRadius(5, 5, 2, 2),
                    Background = today ? Ui.Orange : Ui.B("#F2CFA6")
                };
                DockPanel.SetDock(bar, Dock.Bottom);
                stack.Children.Add(bar);
                if (days[i].Value > 0)
                {
                    var num = Ui.Text(days[i].Value.ToString(Inv), 11, Ui.Soft);
                    num.HorizontalAlignment = HorizontalAlignment.Center;
                    num.TextWrapping = TextWrapping.NoWrap;
                    num.Margin = new Thickness(0, 0, 0, 2);
                    DockPanel.SetDock(num, Dock.Bottom);
                    stack.Children.Add(num);
                }
                col.Children.Add(stack);
                Grid.SetColumn(col, i);
                grid.Children.Add(col);
            }
            return grid;
        }

        // Three soft cards in a row.
        class UniformGrid3
        {
            public readonly Grid Element = new Grid();

            public void Add(string label, string value)
            {
                int i = Element.ColumnDefinitions.Count;
                Element.ColumnDefinitions.Add(new ColumnDefinition());
                var box = new StackPanel();
                box.Children.Add(Ui.Text(label, 12.5, Ui.Soft));
                var v = Ui.Heading(value, 26);
                v.TextWrapping = TextWrapping.NoWrap;
                box.Children.Add(v);
                var card = new Border
                {
                    Background = Ui.PaperDeep,
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(14, 10, 14, 12),
                    Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                    Child = box
                };
                Grid.SetColumn(card, i);
                Element.Children.Add(card);
            }
        }
    }
}

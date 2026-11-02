using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    // The play window: a room (see Scenes) and a mini-game, with the cat in every game.
    public class PlayWindow : Window
    {
        static readonly string[] GameIds = { "fetch", "laser", "treats", "bubbles", "mouse", "hide", "flight", "run", "cafe" };
        // One word each, so nine games, the room and the score still fit on one bar at 980 px.
        static readonly string[] GameNames = { "Fetch", "Laser", "Treats", "Bubbles", "Mouse", "Hide", "Flight", "Run", "Café" };

        readonly Canvas root = new Canvas { ClipToBounds = true };
        readonly Canvas sceneLayer = new Canvas();
        readonly Canvas gameLayer = new Canvas();
        readonly CatView cat = new CatView();
        readonly StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock scoreText = Ui.Text("", 13.5, Ui.Ink);
        readonly Button roomButton;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly double w, h;
        double floor, lastT, lastFrame, together;
        string sceneId, gameId;
        MiniGame game;
        bool paused, wasActive;
        UIElement pauseCard;

        public string Summary { get; private set; }

        // Game frames drawn so far (the test command "frames" writes it out, to measure cost per frame).
        public static long Frames;

        // How this PC draws the play window best, worked out once per run of the app. Drawing on the
        // graphics card is usually fastest, but some PCs, virtual machines and remote desktops manage
        // only a few frames a second that way and run far smoother drawing in software. In software
        // the cached room bitmaps cost more than they save, so caching goes off with it.
        static string drawing;   // null until measured, then "hardware" or "software"
        public static string Drawing { get { return drawing ?? "not measured yet"; } }
        public static bool KeepHardware;   // test runs only: never switch
        static bool CacheForcedOff { get { return Store.IsTestMode && Environment.GetEnvironmentVariable("BR_CACHE") == "off"; } }
        HwndTarget target;
        double resumedAt = -1;   // the click that brings the window back isn't also a move in the game
        int probe;               // 1: timing the graphics card, 2: timing software drawing
        double probeFrom, probeCard, probeCardCost;
        TimeSpan probeCpu;
        int probeFrames;

        public PlayWindow(string startGame, string startScene)
        {
            if (drawing == "software") Scenes.NoCache = true;
            Rect wa = SystemParameters.WorkArea;
            w = Math.Min(980, wa.Width * 0.9);
            h = Math.Min(600, wa.Height * 0.86);
            Title = "Play";
            Width = w;
            Height = h;
            Left = wa.Left + (wa.Width - w) / 2;
            Top = wa.Top + (wa.Height - h) / 2;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            Content = root;
            root.Width = w;
            root.Height = h;
            root.Children.Add(sceneLayer);
            root.Children.Add(gameLayer);

            var bar = new DockPanel { LastChildFill = true };
            var done = Ui.Pill("Done", delegate { Close(); });
            done.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(done, Dock.Right);
            bar.Children.Add(done);
            // A new round of the same game, separate from changing the room.
            var again = Ui.Pill("New round", delegate { SetGame(gameId); });
            again.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(again, Dock.Right);
            bar.Children.Add(again);
            scoreText.VerticalAlignment = VerticalAlignment.Center;
            scoreText.TextWrapping = TextWrapping.NoWrap;
            scoreText.Margin = new Thickness(12, 0, 4, 0);
            DockPanel.SetDock(scoreText, Dock.Right);
            bar.Children.Add(scoreText);
            // Nine games, the room and the score share one bar, so its pills are a size smaller.
            for (int i = 0; i < GameIds.Length; i++)
            {
                string id = GameIds[i];
                var chip = Ui.Pill(GameNames[i], delegate { SetGame(id); });
                Compact(chip);
                chip.Margin = new Thickness(0, 0, 4, 0);
                chip.Tag = id;
                chips.Children.Add(chip);
            }
            roomButton = Ui.Pill("", delegate { NextScene(); });
            Compact(roomButton);
            roomButton.Margin = new Thickness(6, 0, 0, 0);
            chips.Children.Add(roomButton);
            bar.Children.Add(chips);
            var hud = new Border
            {
                Width = w - 24,
                Background = new SolidColorBrush(Color.FromArgb(242, 255, 250, 242)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(8, 6, 8, 6),
                Child = bar
            };
            Canvas.SetLeft(hud, 12);
            Canvas.SetTop(hud, 10);
            root.Children.Add(hud);

            string scene = Array.IndexOf(Scenes.Ids, startScene) >= 0 ? startScene : NextSceneAfter(Store.Data.Settings.LastScene);
            string first = Array.IndexOf(GameIds, startGame) >= 0 ? startGame : Store.Data.Settings.LastGame;
            SetScene(scene);
            SetGame(Array.IndexOf(GameIds, first) >= 0 ? first : "fetch");

            SourceInitialized += delegate
            {
                Native.RoundCorners(this);
                var source = PresentationSource.FromVisual(this) as HwndSource;
                target = source != null ? source.CompositionTarget : null;
                if (target == null || KeepHardware || RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly) return;
                if (drawing == "software") target.RenderMode = RenderMode.SoftwareOnly;
                else if (drawing == null) probe = 1;
            };
            root.MouseLeftButtonDown += (s, e) =>
            {
                if (game == null) return;
                if (resumedAt >= 0 && clock.Elapsed.TotalSeconds - resumedAt < 0.4)
                {
                    resumedAt = -1;
                    return;
                }
                game.Down(e.GetPosition(root));
                root.CaptureMouse();
            };
            root.MouseMove += (s, e) => { if (game != null) game.Move(e.GetPosition(root)); };
            root.MouseLeftButtonUp += (s, e) => { root.ReleaseMouseCapture(); if (game != null) game.Up(e.GetPosition(root)); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            // The game hears keys first (the rooftop run jumps on Space).
            PreviewKeyDown += (s, e) => { if (game != null && e.Key != Key.Escape && game.OnKey(e.Key, true)) e.Handled = true; };
            PreviewKeyUp += (s, e) => { if (game != null && game.OnKey(e.Key, false)) e.Handled = true; };
            // In the background the game waits (and costs nothing) until the window is back in front.
            // Only after it was in front once: a window opened without focus should still play.
            Activated += delegate { wasActive = true; Pause(false); };
            Deactivated += delegate { if (wasActive) Pause(true); };
            CompositionTarget.Rendering += OnFrame;
            Closed += delegate
            {
                if (!paused) CompositionTarget.Rendering -= OnFrame;
                // Closed halfway through timing software drawing: the next window starts fresh.
                if (drawing != "software") Scenes.NoCache = CacheForcedOff;
                EndGame();
                Store.Data.Settings.LastGame = gameId;
                Store.Data.Settings.LastScene = sceneId;
                Store.Save();
            };
        }

        static void Compact(Button b)
        {
            b.FontSize = 11.5;
            b.Padding = new Thickness(7, 3, 7, 3);
        }

        static string NextSceneAfter(string id)
        {
            int i = Array.IndexOf(Scenes.Ids, id);
            return Scenes.Ids[(i + 1) % Scenes.Ids.Length];
        }

        void NextScene()
        {
            SetScene(NextSceneAfter(sceneId));
            SetGame(gameId);
        }

        void SetScene(string id)
        {
            sceneId = id;
            floor = h - Scenes.FloorHeight(id);
            sceneLayer.Children.Clear();
            sceneLayer.Children.Add(Scenes.Build(id, w, h));
            roomButton.Content = "Room: " + Scenes.Name(id);
        }

        void Pause(bool on)
        {
            if (on == paused || game == null) return;
            paused = on;
            game.Paused(on);
            if (on)
            {
                CompositionTarget.Rendering -= OnFrame;
                clock.Stop();   // game time stands still too, so a timed round isn't over on return
                var text = Ui.Heading("Paused", 22);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                var line = Ui.Text("Click to carry on", 13.5, Ui.Soft);
                line.HorizontalAlignment = HorizontalAlignment.Center;
                var panel = new StackPanel();
                panel.Children.Add(text);
                panel.Children.Add(line);
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(235, 255, 250, 242)),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(26, 10, 26, 12),
                    Child = panel,
                    IsHitTestVisible = false
                };
                card.Measure(new Size(w, h));
                Canvas.SetLeft(card, (w - card.DesiredSize.Width) / 2);
                Canvas.SetTop(card, h * 0.42);
                root.Children.Add(card);
                pauseCard = card;
            }
            else
            {
                if (pauseCard != null) root.Children.Remove(pauseCard);
                pauseCard = null;
                clock.Start();
                lastT = resumedAt = clock.Elapsed.TotalSeconds;
                probeFrom = 0;   // start timing afresh
                CompositionTarget.Rendering += OnFrame;
            }
        }

        // Times two seconds of frames on the graphics card. If that's choppy, it times two seconds
        // in software and keeps software if it's smoother, or about as smooth for less work.
        void Probe(double t)
        {
            if (probeFrom == 0)
            {
                probeFrom = t + 0.6;   // let it settle first
                probeFrames = -1;
                return;
            }
            if (t < probeFrom) return;
            if (probeFrames < 0) probeCpu = Process.GetCurrentProcess().TotalProcessorTime;
            probeFrames++;
            if (t - probeFrom < 2) return;
            double fps = probeFrames / (t - probeFrom);
            double cost = (Process.GetCurrentProcess().TotalProcessorTime - probeCpu).TotalMilliseconds / Math.Max(1, probeFrames);
            if (probe == 1)
            {
                if (fps >= 40)
                {
                    Decide("hardware");
                    return;
                }
                probeCard = fps;
                probeCardCost = cost;
                Software(true);
                probe = 2;
                probeFrom = 0;
                return;
            }
            bool smoother = fps >= probeCard * 1.2, asSmooth = fps >= probeCard * 0.85, cheaper = cost <= probeCardCost * 1.25;
            if (smoother || (asSmooth && cheaper)) Decide("software");
            else
            {
                Software(false);
                Decide("hardware");
            }
        }

        void Decide(string how)
        {
            drawing = how;
            probe = 0;
        }

        void Software(bool on)
        {
            target.RenderMode = on ? RenderMode.SoftwareOnly : RenderMode.Default;
            Scenes.NoCache = on || CacheForcedOff;
            if (on) Uncache(root);
            else if (game != null && !game.OwnScenery) SetScene(sceneId);   // back on the card: the room cached again
        }

        static void Uncache(DependencyObject d)
        {
            var e = d as UIElement;
            if (e != null && e.CacheMode != null) e.CacheMode = null;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Uncache(VisualTreeHelper.GetChild(d, i));
        }

        void EndGame()
        {
            if (game == null) return;
            game.Stop();
            if (game.Summary != null) Summary = game.Summary;
        }

        void SetGame(string id)
        {
            EndGame();
            gameLayer.Children.Clear();
            gameId = id;
            switch (id)
            {
                case "laser": game = new LaserGame(); break;
                case "treats": game = new TreatsGame(); break;
                case "bubbles": game = new BubblesGame(); break;
                case "mouse": game = new ToyMouseGame(); break;
                case "hide": game = new HideSeekGame(); break;
                case "flight": game = new NightFlightGame(); break;
                case "run": game = new RooftopRunGame(); break;
                case "cafe": game = new CafeRushGame(); break;
                default: game = new FetchGame(); break;
            }
            cat.SetWalking(false);
            cat.SetHappy(false);
            cat.Face(-1);
            // A game with its own sky covers the room completely, so the room isn't drawn under it.
            bool own = game.OwnScenery;
            if (own && sceneLayer.Children.Count > 0) sceneLayer.Children.Clear();
            else if (!own && sceneLayer.Children.Count == 0) SetScene(sceneId);
            var stage = new Stage(gameLayer, cat, w, h, floor);
            stage.Hint = !own && Scenes.IsDark(sceneId) ? Ui.B("#E6E2F6") : Ui.Soft;
            // Up on the roofs or in the air there is no floor for a shadow to fall on.
            stage.Cat.ShowShadow = !own;
            stage.Cat.Render();
            game.Start(stage);
            if (paused) game.Paused(true);
            root.Cursor = game.HideCursor ? Cursors.None : Cursors.Hand;
            // Night flight and the rooftop run bring their own sky, so changing the room would do nothing there.
            roomButton.Visibility = game.OwnScenery ? Visibility.Collapsed : Visibility.Visible;
            foreach (UIElement child in chips.Children)
            {
                var b = child as Button;
                if (b == null || b.Tag == null) continue;
                bool on = (string)b.Tag == id;
                b.Background = on ? Ui.Orange : Ui.B("#FFE9D0");
                b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // For README pictures: plays the game by hand for a while, without showing the window.
        internal void Simulate(Point? mouse, double seconds)
        {
            CompositionTarget.Rendering -= OnFrame;
            if (game == null) return;
            if (mouse != null) game.Move(mouse.Value);
            for (double t = 0; t < seconds; t += 1.0 / 60) game.Frame(t, 1.0 / 60);
            scoreText.Text = game.ScoreText;
        }

        void OnFrame(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            // 60 to 85 frames a second is plenty: on a 120 Hz or faster screen, frames in between are
            // skipped. The limit sits well below 60 and 75 Hz so those never lose a frame to jitter.
            if (t - lastFrame < 1.0 / 100) return;
            lastFrame = t;
            double step = t - lastT;
            lastT = t;
            if (game == null) return;
            // Playing together counts toward friendship: a point per half minute with the window in front.
            if (IsActive)
            {
                together += Math.Min(0.25, step);
                if (together >= 30)
                {
                    together -= 30;
                    Friendship.Add(1);
                }
            }
            Frames++;
            if (probe > 0) Probe(t);
            // The game moves in steps of about 1/60 s, several per frame when frames are few, so
            // it keeps real time on a slow PC instead of going into slow motion. A long hitch
            // (over a tenth of a second) isn't caught up on.
            double span = Math.Min(0.1, step);
            int steps = Math.Max(1, (int)Math.Round(span * 60));
            for (int i = steps - 1; i >= 0 && span > 0; i--) game.Frame(t - i * span / steps, span / steps);
            string s = game.ScoreText;
            if (scoreText.Text != s) scoreText.Text = s;
        }
    }

    // What every game gets: the layer to draw in, the cat, and the size of the room.
    public class Stage
    {
        public readonly Canvas Layer;
        public readonly CatBody Cat;
        public readonly double W, H, Floor;
        public readonly Random Rng = new Random();
        public const double Top = 62;   // below the bar at the top
        public Point Mouse;
        public bool MouseKnown;
        public Brush Hint = Ui.Soft;   // for words written on the room: light on dark rooms

        public Stage(Canvas layer, CatView view, double w, double h, double floor)
        {
            Layer = layer;
            W = w;
            H = h;
            Floor = floor;
            Cat = new CatBody(view, floor, w);
            layer.Children.Add(Cat.Shadow);
            layer.Children.Add(view);
            Cat.Render();
        }

        public T Add<T>(T e) where T : UIElement
        {
            Layer.Children.Add(e);
            return e;
        }

        public void Remove(UIElement e)
        {
            Layer.Children.Remove(e);
        }

        public static void Place(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
        }

        public void Float(string text, double x, double y, Brush brush, double size)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size, FontWeight = FontWeights.Bold, FontFamily = Ui.Font };
            var move = new TranslateTransform();
            tb.RenderTransform = move;
            Place(tb, x, y);
            Layer.Children.Add(tb);
            var up = new DoubleAnimation(0, -40, TimeSpan.FromSeconds(1.1));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.1)) { BeginTime = TimeSpan.Zero };
            fade.Completed += delegate { Layer.Children.Remove(tb); };
            move.BeginAnimation(TranslateTransform.YProperty, up);
            tb.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public int Best(string key)
        {
            int v;
            return Store.Data.Best.TryGetValue(key, out v) ? v : 0;
        }

        public bool SetBest(string key, int value)
        {
            if (value <= Best(key)) return false;
            Store.Data.Best[key] = value;
            Store.Save();
            return true;
        }
    }

    // The cat as a body in the room: walks along the floor, jumps in arcs, looks at things.
    public class CatBody
    {
        public const double Gravity = 1900;
        static readonly Brush ShadowInk = MakeShadow();
        public readonly CatView View;
        // A soft shadow on the floor under the cat: it shrinks and fades as the cat jumps.
        public readonly Ellipse Shadow = new Ellipse { Width = 84, Height = 16, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5) };
        public bool ShowShadow = true;
        readonly ScaleTransform shadowSize = new ScaleTransform();
        readonly double floor, roomWidth;
        public double X, Lift, Vx, Vy;
        public bool Jumping;
        public int Dir = -1;

        public CatBody(CatView view, double floor, double roomWidth)
        {
            View = view;
            this.floor = floor;
            this.roomWidth = roomWidth;
            X = roomWidth * 0.62;
            Shadow.Fill = ShadowInk;
            Shadow.RenderTransform = shadowSize;
        }

        static Brush MakeShadow()
        {
            var b = new RadialGradientBrush(Color.FromArgb(80, 60, 30, 10), Color.FromArgb(0, 60, 30, 10));
            b.Freeze();
            return b;
        }

        public double Mid { get { return X + CatView.W / 2; } }
        public Point Mouth { get { return new Point(X + CatView.Mouth.X, floor - CatView.H + CatView.Mouth.Y - Lift); } }
        public Point Paws { get { return new Point(Mid, floor - 12 - Lift); } }
        public Point Eye { get { return new Point(X + 55, floor - CatView.H + 56 - Lift); } }

        // Returns true once it is there.
        public bool WalkTo(double target, double speed, double dt)
        {
            double d = target - Mid;
            if (Math.Abs(d) < 3)
            {
                View.SetWalking(false);
                return true;
            }
            Dir = Math.Sign(d);
            View.Face(Dir);
            if (!Jumping) View.SetWalking(true);
            X = Clamp(X + Dir * Math.Min(Math.Abs(d), speed * dt));
            return false;
        }

        public void Stop()
        {
            View.SetWalking(false);
        }

        public void Jump(double vy, double vx)
        {
            if (Jumping) return;
            Jumping = true;
            Vy = vy;
            Vx = vx;
            View.SetWalking(false);
            if (Math.Abs(vx) > 1) { Dir = Math.Sign(vx); View.Face(Dir); }
        }

        // Advances a jump. Returns true on the frame it lands.
        public bool Step(double dt)
        {
            if (!Jumping) return false;
            Vy += Gravity * dt;
            Lift -= Vy * dt;
            X = Clamp(X + Vx * dt);
            if (Lift > 0) return false;
            Lift = 0;
            Jumping = false;
            Vx = Vy = 0;
            return true;
        }

        double Clamp(double x)
        {
            return Math.Max(-8, Math.Min(roomWidth - CatView.W + 8, x));
        }

        public void Look(Point p)
        {
            View.LookAt(p.X - Eye.X, p.Y - Eye.Y);
        }

        public void Render()
        {
            Canvas.SetLeft(View, X);
            // The paws end 3 px above the drawing's bottom edge; without this the cat hovers.
            Canvas.SetTop(View, floor - CatView.H + 3 - Lift);
            bool show = ShowShadow && View.Visibility == Visibility.Visible;
            Visibility seen = show ? Visibility.Visible : Visibility.Collapsed;
            if (Shadow.Visibility != seen) Shadow.Visibility = seen;
            if (!show) return;
            double k = 1 / (1 + Math.Max(0, Lift) / 140);
            Canvas.SetLeft(Shadow, Mid - 42);
            Canvas.SetTop(Shadow, floor - 10);
            if (Math.Abs(shadowSize.ScaleX - k) > 0.01)
            {
                shadowSize.ScaleX = shadowSize.ScaleY = k;
                Shadow.Opacity = k;
            }
        }
    }

    public abstract class MiniGame
    {
        protected Stage S;
        public virtual void Start(Stage stage) { S = stage; }
        public abstract void Frame(double t, double dt);
        public virtual void Down(Point p) { }
        public virtual void Move(Point p) { S.Mouse = p; S.MouseKnown = true; }
        public virtual void Up(Point p) { }
        public virtual void Stop() { S.Cat.Stop(); }
        public abstract string ScoreText { get; }
        public virtual string Summary { get { return null; } }
        public virtual bool HideCursor { get { return false; } }
        protected string Name { get { return Store.Data.Settings.PetName; } }

        // Games that draw their own world over the room (then the room button has nothing to change).
        public virtual bool OwnScenery { get { return false; } }

        // Keys reach the game before anything else; true when the game used the key.
        public virtual bool OnKey(Key key, bool down) { return false; }

        // The window went into the background (on) or came back. Frames stop in between.
        public virtual void Paused(bool on) { }

        UIElement banner;

        // Lives as hearts, full ones then empty ones.
        protected static string Hearts(int left, int of)
        {
            left = Math.Max(0, Math.Min(of, left));
            return new string('♥', left) + new string('♡', of - left);
        }

        // A small line at the top left, under the bar, for what the score line has no room for.
        protected TextBlock StatusLine(bool dark)
        {
            var text = new TextBlock { FontFamily = Ui.Font, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = dark ? Ui.B("#DCE4FF") : Ui.Ink };
            var pill = new Border
            {
                Background = dark ? new SolidColorBrush(Color.FromArgb(120, 10, 16, 40)) : new SolidColorBrush(Color.FromArgb(215, 255, 250, 242)),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(11, 3, 11, 4),
                Child = text,
                IsHitTestVisible = false
            };
            Stage.Place(pill, 20, Stage.Top - 2);
            Panel.SetZIndex(pill, 20);
            S.Add(pill);
            return text;
        }

        // A title and a line under it in the middle of the room that fade by themselves (a new
        // level, a new day). Dark for night skies, paper for rooms.
        protected void Banner(string title, string sub, double seconds, bool dark)
        {
            if (banner != null) S.Remove(banner);
            var panel = new StackPanel();
            var big = Ui.Heading(title, 24);
            big.Foreground = dark ? Ui.B("#FFE9B0") : Ui.Ink;
            big.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(big);
            if (sub != null)
            {
                var line = Ui.Text(sub, 14.5, dark ? Ui.B("#DCE4FF") : Ui.Soft);
                line.HorizontalAlignment = HorizontalAlignment.Center;
                line.TextAlignment = TextAlignment.Center;
                panel.Children.Add(line);
            }
            var card = new Border
            {
                Background = dark ? new SolidColorBrush(Color.FromArgb(150, 12, 18, 46)) : new SolidColorBrush(Color.FromArgb(225, 255, 250, 242)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 8, 22, 10),
                Child = panel,
                IsHitTestVisible = false,
                MaxWidth = S.W - 80
            };
            card.Measure(new Size(S.W, S.H));
            Stage.Place(card, (S.W - card.DesiredSize.Width) / 2, S.H * 0.19);
            Panel.SetZIndex(card, 25);
            S.Add(card);
            banner = card;
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Max(0, seconds - 0.5)))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            Timeline.SetDesiredFrameRate(fade, 30);
            fade.Completed += delegate
            {
                S.Remove(card);
                if (banner == card) banner = null;
            };
            card.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // The card at the end of a round: how it went, the best so far, and a button to go again.
        protected Border EndCard(string title, string sub, string detail, string button, string hint, Action again)
        {
            if (banner != null)
            {
                S.Remove(banner);
                banner = null;
            }
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var head = Ui.Heading(title, 26);
            head.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(head);
            foreach (string line in new[] { sub, detail })
            {
                if (line == null) continue;
                var small = Ui.Text(line, line == sub ? 14 : 12.5, Ui.Soft);
                small.HorizontalAlignment = HorizontalAlignment.Center;
                small.TextAlignment = TextAlignment.Center;
                small.Margin = new Thickness(0, 2, 0, 0);
                panel.Children.Add(small);
            }
            // Not focusable, so a key held for the game doesn't press it.
            var go = Ui.Primary(button, delegate { again(); });
            go.Focusable = false;
            go.Margin = new Thickness(0, 14, 0, 0);
            go.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(go);
            if (hint != null)
            {
                var tip = Ui.Text(hint, 12, Ui.Soft);
                tip.HorizontalAlignment = HorizontalAlignment.Center;
                tip.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(tip);
            }
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 250, 242)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(30, 18, 30, 18),
                Width = 360,
                Child = panel
            };
            Stage.Place(card, S.W / 2 - 180, S.H * 0.2);
            Panel.SetZIndex(card, 30);
            S.Add(card);
            return card;
        }

        // For the toys and furniture the games draw out of plain shapes.
        protected static Path AddPath(Canvas c, string data, string fill, string stroke, double thickness)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill == null ? null : Ui.B(fill),
                Stroke = stroke == null ? null : Ui.B(stroke),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            c.Children.Add(p);
            return p;
        }

        protected static T Put<T>(Canvas c, T e, double x, double y) where T : UIElement
        {
            Stage.Place(e, x, y);
            c.Children.Add(e);
            return e;
        }
    }

    // ---------- Fetch: throw the ball, the cat brings it back to wherever your cursor is ----------

    public class FetchGame : MiniGame
    {
        const double R = 13, G = 1700;
        enum Mode { Idle, Chasing, Returning }
        Canvas ball;
        readonly RotateTransform spin = new RotateTransform(0, R, R);
        readonly List<KeyValuePair<double, Point>> samples = new List<KeyValuePair<double, Point>>();
        Vector pos, vel;
        bool holding, thrown = true;
        Mode mode = Mode.Idle;
        double returnX, idleUntil, now;
        int fetches, catches;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            ball = new Canvas { Width = R * 2, Height = R * 2, RenderTransform = spin };
            ball.Children.Add(new Ellipse
            {
                Width = R * 2,
                Height = R * 2,
                Fill = new RadialGradientBrush(Ui.C("#F1FA7A"), Ui.C("#B9CF2C")) { GradientOrigin = new Point(0.35, 0.3) },
                Stroke = Ui.B("#93A622"),
                StrokeThickness = 1.2
            });
            ball.Children.Add(new Path { Data = Geometry.Parse("M 4 6 Q 13 13 4 21 M 22 6 Q 13 13 22 21"), Stroke = Brushes.White, StrokeThickness = 1.8, Opacity = 0.85 });
            S.Add(ball);
            pos = new Vector(S.W * 0.35, S.Floor - 200);
        }

        bool Resting { get { return pos.Y >= S.Floor - R - 0.5 && Math.Abs(vel.Y) < 1 && Math.Abs(vel.X) < 20; } }

        public override void Down(Point p)
        {
            if ((new Vector(p.X, p.Y) - pos).Length > R * 2.6) return;
            holding = true;
            if (mode == Mode.Returning) mode = Mode.Idle;
            samples.Clear();
        }

        public override void Move(Point p)
        {
            base.Move(p);
            if (!holding) return;
            pos = new Vector(Math.Max(R, Math.Min(S.W - R, p.X)), Math.Max(Stage.Top, Math.Min(S.Floor - R, p.Y)));
            samples.Add(new KeyValuePair<double, Point>(now, p));
            if (samples.Count > 12) samples.RemoveAt(0);
        }

        public override void Up(Point p)
        {
            if (!holding) return;
            holding = false;
            thrown = true;
            vel = new Vector();
            foreach (var s in samples)
            {
                if (now - s.Key > 0.09) continue;
                double dt = Math.Max(0.016, now - s.Key);
                vel = new Vector((p.X - s.Value.X) / dt, (p.Y - s.Value.Y) / dt);
                break;
            }
            if (vel.Length > 2600) vel *= 2600 / vel.Length;
        }

        public override void Frame(double t, double dt)
        {
            now = t;
            CatBody cat = S.Cat;
            cat.Step(dt);
            StepBall(dt);

            double dx = pos.X - cat.Mid;
            switch (mode)
            {
                case Mode.Idle:
                    cat.Stop();
                    if (!holding && thrown && t > idleUntil)
                    {
                        if (Resting && Math.Abs(dx) <= 26) PickUp(false);
                        else mode = Mode.Chasing;
                    }
                    break;

                case Mode.Chasing:
                    if (holding) { mode = Mode.Idle; break; }
                    cat.WalkTo(pos.X, 470, dt);
                    bool reachable = pos.Y < S.Floor - 95 && pos.Y > S.Floor - 230;
                    if (!cat.Jumping && reachable && Math.Abs(dx) < 50 && vel.Y > -150) cat.Jump(-780, 0);
                    Point m = cat.Mouth;
                    if ((pos - new Vector(m.X, m.Y)).Length < 34) PickUp(!Resting && pos.Y < S.Floor - 40);
                    else if (Resting && Math.Abs(dx) < 20) PickUp(false);
                    break;

                case Mode.Returning:
                    if (cat.WalkTo(returnX, 430, dt) && !cat.Jumping)
                    {
                        mode = Mode.Idle;
                        idleUntil = t + 1.2;
                        vel = new Vector(cat.Dir * 70, -260);
                        fetches++;
                        cat.View.Hop(8);
                        if (fetches % 3 == 0)
                        {
                            Sfx.Happy();
                            cat.View.Float(Lines.Pick("again!", "mrrp!", "more!"), Ui.OrangeDeep, 70, 0, 15);
                        }
                    }
                    break;
            }

            if (mode == Mode.Idle && !thrown && S.MouseKnown) cat.Look(S.Mouse);
            else if (mode != Mode.Returning) cat.Look(new Point(pos.X, pos.Y));
            cat.Render();
            Stage.Place(ball, pos.X - R, pos.Y - R);
        }

        void StepBall(double dt)
        {
            if (holding) return;
            if (mode == Mode.Returning)
            {
                Point m = S.Cat.Mouth;
                pos = new Vector(m.X + S.Cat.Dir * 4, m.Y + 2);
                return;
            }
            vel.Y += G * dt;
            pos += vel * dt;
            if (pos.X < R) { pos.X = R; vel.X = Math.Abs(vel.X) * 0.75; }
            if (pos.X > S.W - R) { pos.X = S.W - R; vel.X = -Math.Abs(vel.X) * 0.75; }
            if (pos.Y < Stage.Top) { pos.Y = Stage.Top; vel.Y = Math.Abs(vel.Y) * 0.6; }
            if (pos.Y >= S.Floor - R)
            {
                pos.Y = S.Floor - R;
                if (vel.Y > 110) Sfx.Bounce(vel.Y / 1400);
                if (vel.Y > 0) vel.Y = vel.Y > 110 ? -vel.Y * 0.55 : 0;
                vel.X *= Math.Pow(0.4, dt);
                if (Math.Abs(vel.X) < 6) vel.X = 0;
            }
            spin.Angle += vel.X * dt * 4.4;
        }

        void PickUp(bool inAir)
        {
            if (inAir)
            {
                catches++;
                Sfx.Happy();
                S.Cat.View.Float(Lines.Pick("nice!", "got it!", "!!"), Ui.OrangeDeep, 60, 0, 16);
            }
            mode = Mode.Returning;
            thrown = false;
            returnX = Math.Max(90, Math.Min(S.W - 90, S.MouseKnown ? S.Mouse.X : S.W / 2));
            vel = new Vector();
        }

        public override string ScoreText { get { return "Fetches " + fetches + "    Catches " + catches; } }

        public override string Summary
        {
            get
            {
                if (fetches == 0) return null;
                return "*pant pant* " + fetches + " fetch" + (fetches == 1 ? "" : "es") +
                       (catches > 0 ? ", " + catches + " caught in the air" : "") + ". Fun.";
            }
        }
    }

    // ---------- Laser: move the red dot, the cat stalks, wiggles and pounces ----------

    public class LaserGame : MiniGame
    {
        Canvas dot;
        bool crouching, caughtThisJump;
        double crouchUntil, cooldownUntil, nextHop, crouchX;
        int pounces, caught;
        Point? lastCatch;

        public override bool HideCursor { get { return true; } }

        public override void Start(Stage stage)
        {
            base.Start(stage);
            dot = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            var glow = new Ellipse { Width = 40, Height = 40, Fill = new RadialGradientBrush(Color.FromArgb(150, 255, 40, 40), Color.FromArgb(0, 255, 40, 40)) };
            Stage.Place(glow, -20, -20);
            dot.Children.Add(glow);
            var core = new Ellipse { Width = 10, Height = 10, Fill = Ui.B("#FF2B2B") };
            Stage.Place(core, -5, -5);
            dot.Children.Add(core);
            S.Add(dot);
            S.Float("Move the mouse to tease " + Name, S.W / 2 - 110, S.H * 0.35, S.Hint, 16);
        }

        public override void Move(Point p)
        {
            base.Move(p);
            bool inside = p.Y > Stage.Top && p.Y < S.H && p.X > 0 && p.X < S.W;
            dot.Visibility = inside ? Visibility.Visible : Visibility.Hidden;
            Stage.Place(dot, p.X, Math.Min(S.Floor - 4, p.Y));
        }

        public override void Frame(double t, double dt)
        {
            CatBody cat = S.Cat;
            bool visible = S.MouseKnown && dot.Visibility == Visibility.Visible;
            Point d = new Point(S.Mouse.X, Math.Min(S.Floor - 4, S.Mouse.Y));

            if (cat.Jumping)
            {
                Point m = cat.Mouth;
                if (visible && !caughtThisJump && (Math.Abs(m.X - d.X) < 40 && Math.Abs(m.Y - d.Y) < 46))
                    Catch(cat);
                if (cat.Step(dt))
                {
                    Point p = cat.Paws;
                    if (visible && !caughtThisJump && Math.Abs(p.X - d.X) < 46 && Math.Abs(p.Y - d.Y) < 40) Catch(cat);
                }
            }
            else if (!visible)
            {
                crouching = false;
                cat.Stop();
            }
            else
            {
                double dx = d.X - cat.Mid;
                bool low = d.Y > S.Floor - 210;
                if (crouching)
                {
                    cat.X = crouchX + Math.Sin(t * 45) * 1.2;   // butt wiggle
                    if (t >= crouchUntil)
                    {
                        crouching = false;
                        double height = Math.Max(30, Math.Min(190, S.Floor - d.Y - 20));
                        double vy = -Math.Sqrt(2 * CatBody.Gravity * height);
                        double flight = 2 * -vy / CatBody.Gravity;
                        cat.X = crouchX;
                        cat.Jump(vy, Math.Max(-900, Math.Min(900, dx / (flight * 0.85))));
                        caughtThisJump = false;
                        pounces++;
                        Sfx.Trill();
                        cooldownUntil = t + 0.7;
                    }
                }
                else if (low && Math.Abs(dx) < 170 && Math.Abs(dx) > 8 && t > cooldownUntil)
                {
                    crouching = true;
                    crouchX = cat.X;
                    crouchUntil = t + 0.3 + S.Rng.NextDouble() * 0.45;
                    cat.Stop();
                    cat.Dir = Math.Sign(dx);
                    cat.View.Face(cat.Dir);
                }
                else if (Math.Abs(dx) > 30)
                {
                    cat.WalkTo(d.X, low ? 430 : 300, dt);
                }
                else
                {
                    cat.Stop();
                    if (!low && t > nextHop)
                    {
                        nextHop = t + 1.4 + S.Rng.NextDouble() * 1.5;
                        cat.View.Hop(12);
                        if (S.Rng.NextDouble() < 0.3) Sfx.Question();
                    }
                }
            }
            if (visible) cat.Look(d);
            cat.Render();
        }

        void Catch(CatBody cat)
        {
            caughtThisJump = true;
            // A dot left sitting still only counts once; it has to move before it can be caught again.
            Point d = S.Mouse;
            if (lastCatch != null && (new Vector(d.X - lastCatch.Value.X, d.Y - lastCatch.Value.Y)).Length < 60) return;
            lastCatch = d;
            caught++;
            Sfx.Happy();
            cat.View.Float(Lines.Pick("got it!", "mine!", "gotcha"), Ui.OrangeDeep, 55, 0, 16);
        }

        public override string ScoreText { get { return "Pounces " + pounces + "    Caught " + caught; } }

        public override string Summary
        {
            get
            {
                if (caught > 0) return "Got the red dot " + caught + " time" + (caught == 1 ? "" : "s") + ". It'll be back. *narrows eyes*";
                return pounces > 0 ? "That dot is fast. *flicks tail*" : null;
            }
        }
    }

    // ---------- Treats: fish fall from the sky, steer the cat with the mouse to catch them ----------

    public class TreatsGame : MiniGame
    {
        const double RoundSeconds = 75;
        static readonly Brush GoldenGlow = MakeGlow();

        static Brush MakeGlow()
        {
            var b = new RadialGradientBrush(Color.FromArgb(170, 255, 230, 128), Color.FromArgb(0, 255, 230, 128));
            b.Freeze();
            return b;
        }

        class Treat
        {
            public Canvas El;
            public double X, Y, Vy, Phase;
            public bool Golden;
        }

        readonly List<Treat> treats = new List<Treat>();
        double start = -1, nextSpawn, nowT;
        int score, best;
        bool over;
        Border endCard;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            best = S.Best("treats");
            S.Float("Move the mouse to steer " + Name, S.W / 2 - 100, S.H * 0.35, S.Hint, 16);
        }

        double Elapsed(double t) { return start < 0 ? 0 : t - start; }

        public override void Frame(double t, double dt)
        {
            nowT = t;
            if (start < 0) { start = t; nextSpawn = t + 1; }
            CatBody cat = S.Cat;
            if (!over && Elapsed(t) >= RoundSeconds) Finish();

            if (!over && t >= nextSpawn)
            {
                double progress = Math.Min(1, Elapsed(t) / 60);
                Spawn(progress);
                nextSpawn = t + (1.0 - 0.55 * progress) * (0.8 + S.Rng.NextDouble() * 0.4);
            }

            if (S.MouseKnown && !over) cat.WalkTo(S.Mouse.X, 700, dt);
            else cat.Stop();

            Treat lowest = null;
            for (int i = treats.Count - 1; i >= 0; i--)
            {
                Treat tr = treats[i];
                tr.Y += tr.Vy * dt;
                tr.Phase += dt * 3;
                double x = tr.X + Math.Sin(tr.Phase) * 10;
                Stage.Place(tr.El, x - 17, tr.Y - 9);
                if (tr.Y > S.Floor - 72 && tr.Y < S.Floor - 20 && Math.Abs(x - cat.Mid) < 46 && !over)
                {
                    score += tr.Golden ? 3 : 1;
                    if (tr.Golden) Sfx.Happy();
                    S.Float(tr.Golden ? "+3" : "+1", x - 8, tr.Y - 30, tr.Golden ? Ui.B("#E0A100") : Ui.OrangeDeep, 18);
                    cat.View.Hop(5);
                    S.Remove(tr.El);
                    treats.RemoveAt(i);
                    continue;
                }
                if (tr.Y > S.Floor - 6)
                {
                    S.Remove(tr.El);
                    treats.RemoveAt(i);
                    continue;
                }
                if (lowest == null || tr.Y > lowest.Y) lowest = tr;
            }
            if (lowest != null) cat.Look(new Point(lowest.X, lowest.Y));
            else if (S.MouseKnown) cat.Look(S.Mouse);
            cat.Render();
        }

        void Spawn(double progress)
        {
            bool golden = S.Rng.NextDouble() < 0.1;
            var fish = new Canvas { Width = 34, Height = 18, IsHitTestVisible = false };
            string body = golden ? "#FFD54A" : (S.Rng.Next(2) == 0 ? "#7FC8F8" : "#FF9E80");
            // A soft glow behind a golden one (a drawn halo: a blur effect would cost every frame it falls).
            if (golden) Put(fish, new Ellipse { Width = 62, Height = 46, Fill = GoldenGlow }, -14, -14);
            fish.Children.Add(new Path { Data = Geometry.Parse("M 24 9 L 34 1 L 32 9 L 34 17 Z"), Fill = Ui.B(body), Stroke = Ui.B("#33000000"), StrokeThickness = 1 });
            fish.Children.Add(new Path { Data = Geometry.Parse("M 1 9 C 4 1 19 0 27 9 C 19 18 4 17 1 9 Z"), Fill = Ui.B(body), Stroke = Ui.B("#33000000"), StrokeThickness = 1 });
            fish.Children.Add(new Path { Data = Geometry.Parse("M 4 10 C 9 15 18 15 24 10 Z"), Fill = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)) });
            var eye = new Ellipse { Width = 3.5, Height = 3.5, Fill = Ui.Ink };
            Stage.Place(eye, 6, 6);
            fish.Children.Add(eye);
            if (!Scenes.NoCache) fish.CacheMode = new BitmapCache();
            var tr = new Treat
            {
                El = fish,
                X = 40 + S.Rng.NextDouble() * (S.W - 80),
                Y = Stage.Top,
                Vy = 150 + 180 * progress + S.Rng.NextDouble() * 40,
                Phase = S.Rng.NextDouble() * 6,
                Golden = golden
            };
            S.Add(fish);
            treats.Add(tr);
        }

        void Finish()
        {
            over = true;
            S.Cat.Stop();
            bool record = S.SetBest("treats", score);
            best = Math.Max(best, score);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var title = Ui.Heading("Caught " + score + "!", 26);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            var sub = Ui.Text(record ? "New best. *proud tail*" : "Best: " + best, 14, Ui.Soft);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(sub);
            var again = Ui.Primary("Again", delegate { Restart(); });
            again.Margin = new Thickness(0, 14, 0, 0);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(again);
            endCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 250, 242)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(34, 20, 34, 22),
                Child = panel
            };
            Stage.Place(endCard, S.W / 2 - 120, S.H * 0.26);
            endCard.Width = 240;
            S.Add(endCard);
            Sfx.Happy();
        }

        void Restart()
        {
            if (endCard != null) S.Remove(endCard);
            foreach (var tr in treats) S.Remove(tr.El);
            treats.Clear();
            score = 0;
            over = false;
            start = -1;
        }

        public override string ScoreText
        {
            get
            {
                string time = over ? "" : Math.Max(0, (int)Math.Ceiling(RoundSeconds - Elapsed(nowT))) + "s    ";
                return "Caught " + score + "    " + time + "Best " + Math.Max(best, score);
            }
        }

        public override string Summary
        {
            get { return score > 0 ? "Nom. " + score + " treat" + (score == 1 ? "" : "s") + ". *licks lips*" : null; }
        }
    }

    // ---------- Bubbles: pop them before the cat does ----------

    public class BubblesGame : MiniGame
    {
        class Bubble
        {
            public Canvas El;
            public double X, Y, R, Vy, Phase, Sway;
        }

        class Bit
        {
            public Ellipse El;
            public double X, Y, Vx, Vy, Life;
        }

        readonly List<Bubble> bubbles = new List<Bubble>();
        readonly List<Bit> bits = new List<Bit>();
        double nextSpawn;
        int mine, theirs;
        Bubble target;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            S.Float("Click bubbles. " + Name + " is playing too.", S.W / 2 - 130, S.H * 0.35, S.Hint, 16);
        }

        public override void Down(Point p)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble b = bubbles[i];
                double x = b.X + Math.Sin(b.Phase) * b.Sway;
                if ((new Vector(p.X - x, p.Y - b.Y)).Length > b.R + 4) continue;
                mine++;
                Pop(i, x);
                return;
            }
        }

        public override void Frame(double t, double dt)
        {
            if (t >= nextSpawn)
            {
                Spawn();
                nextSpawn = t + 0.55 + S.Rng.NextDouble() * 0.6;
            }
            CatBody cat = S.Cat;
            bool landed = cat.Step(dt);
            if (landed) target = null;

            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble b = bubbles[i];
                b.Y -= b.Vy * dt;
                b.Phase += dt * 1.6;
                double x = b.X + Math.Sin(b.Phase) * b.Sway;
                Stage.Place(b.El, x - b.R, b.Y - b.R);
                if (cat.Jumping)
                {
                    Point m = cat.Mouth;
                    if ((new Vector(m.X - x, m.Y - 20 - b.Y)).Length < b.R + 26)
                    {
                        theirs++;
                        if (S.Rng.NextDouble() < 0.35) Sfx.Happy();
                        Pop(i, x);
                        continue;
                    }
                }
                if (b.Y < Stage.Top - b.R)
                {
                    S.Remove(b.El);
                    bubbles.RemoveAt(i);
                }
            }

            // The cat goes for the lowest bubble it can reach.
            if (!cat.Jumping)
            {
                if (target == null || !bubbles.Contains(target) || target.Y < S.Floor - 240)
                {
                    target = null;
                    foreach (Bubble b in bubbles)
                        if (b.Y > S.Floor - 240 && (target == null || b.Y > target.Y)) target = b;
                }
                if (target != null)
                {
                    double tx = target.X + Math.Sin(target.Phase) * target.Sway;
                    bool under = cat.WalkTo(tx, 330, dt) || Math.Abs(tx - cat.Mid) < 30;
                    double height = S.Floor - target.Y - 40;
                    if (under && height < 200)
                    {
                        double vy = -Math.Sqrt(2 * CatBody.Gravity * Math.Max(40, height));
                        cat.Jump(vy, (tx - cat.Mid) * 2);
                    }
                }
                else cat.Stop();
            }
            if (target != null) cat.Look(new Point(target.X, target.Y));
            else if (S.MouseKnown) cat.Look(S.Mouse);

            for (int i = bits.Count - 1; i >= 0; i--)
            {
                Bit bit = bits[i];
                bit.Life -= dt;
                bit.X += bit.Vx * dt;
                bit.Y += bit.Vy * dt;
                bit.Vy += 300 * dt;
                bit.El.Opacity = Math.Max(0, bit.Life / 0.45);
                Stage.Place(bit.El, bit.X, bit.Y);
                if (bit.Life <= 0)
                {
                    S.Remove(bit.El);
                    bits.RemoveAt(i);
                }
            }
            cat.Render();
        }

        void Spawn()
        {
            double r = 14 + S.Rng.NextDouble() * 22;
            var el = new Canvas { Width = r * 2, Height = r * 2, IsHitTestVisible = false };
            var fill = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(10, 255, 255, 255), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(40, 190, 230, 255), 0.7));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 170, 220), 1));
            el.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), StrokeThickness = 1.3 });
            var shine = new Ellipse { Width = r * 0.5, Height = r * 0.35, Fill = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) };
            Stage.Place(shine, r * 0.45, r * 0.4);
            el.Children.Add(shine);
            var b = new Bubble
            {
                El = el,
                R = r,
                X = 30 + S.Rng.NextDouble() * (S.W - 60),
                Y = S.Floor + r,
                Vy = 45 + S.Rng.NextDouble() * 50,
                Phase = S.Rng.NextDouble() * 6,
                Sway = 8 + S.Rng.NextDouble() * 14
            };
            S.Add(el);
            bubbles.Add(b);
        }

        void Pop(int index, double x)
        {
            Bubble b = bubbles[index];
            S.Remove(b.El);
            bubbles.RemoveAt(index);
            if (target == b) target = null;
            Sfx.Pop();
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4 + S.Rng.NextDouble() * 0.4;
                var el = new Ellipse { Width = 5, Height = 5, Fill = Ui.B(k % 2 == 0 ? "#BEE6FF" : "#FFC6E6"), IsHitTestVisible = false };
                S.Add(el);
                bits.Add(new Bit { El = el, X = x + Math.Cos(a) * b.R, Y = b.Y + Math.Sin(a) * b.R, Vx = Math.Cos(a) * 120, Vy = Math.Sin(a) * 120, Life = 0.45 });
            }
        }

        public override string ScoreText { get { return "You " + mine + "    " + Name + " " + theirs; } }

        public override string Summary
        {
            get
            {
                if (mine + theirs == 0) return null;
                return "We popped " + (mine + theirs) + " bubbles. " + (theirs > mine ? "I won. *smug*" : theirs == mine ? "A tie!" : "You won. This time.");
            }
        }
    }

    // ---------- Toy mouse: pull the wind-up mouse back and let go, the cat hunts it ----------

    public class ToyMouseGame : MiniGame
    {
        const double MaxPull = 220, Half = 25;
        enum Mode { Watch, Crouch, Pounce, Proud }
        readonly ScaleTransform facing = new ScaleTransform(1, 1, 25, 20);
        readonly TranslateTransform bob = new TranslateTransform();
        readonly ScaleTransform keyTurn = new ScaleTransform(1, 1, 22.1, 9);
        Canvas toy, meter;
        Rectangle meterFill;
        Line pullLine;
        Mode mode = Mode.Watch;
        bool holding;
        double x, v, grabX, grabOffset, keyPhase, spring, runTime, topSpeed, crouchX, crouchUntil, cooldownUntil, proudUntil;
        int runs, caught;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            toy = new Canvas { Width = 50, Height = 34, IsHitTestVisible = false };
            var turn = new TransformGroup();
            turn.Children.Add(facing);
            turn.Children.Add(bob);
            toy.RenderTransform = turn;
            AddPath(toy, "M 14 29 C 5 32 6 21 0 23", null, "#B7A9B0", 1.6);
            Put(toy, new Rectangle { Width = 2.2, Height = 9, Fill = Ui.B("#B8902A") }, 21, 10);
            var key = new Canvas { RenderTransform = keyTurn };
            Put(key, new Ellipse { Width = 8, Height = 7, Fill = Ui.B("#F2CF6B"), Stroke = Ui.B("#B8902A"), StrokeThickness = 1.2 }, 13.5, 5.5);
            Put(key, new Ellipse { Width = 8, Height = 7, Fill = Ui.B("#F2CF6B"), Stroke = Ui.B("#B8902A"), StrokeThickness = 1.2 }, 22.7, 5.5);
            Put(key, new Ellipse { Width = 4.2, Height = 4.2, Fill = Ui.B("#B8902A") }, 20, 7);
            toy.Children.Add(key);
            Put(toy, new Ellipse { Width = 7, Height = 4, Fill = Ui.B("#7D7983") }, 17, 30);
            Put(toy, new Ellipse { Width = 7, Height = 4, Fill = Ui.B("#7D7983") }, 35, 30);
            var body = AddPath(toy, "M 12 32 C 12 20 26 14 36 18 C 42 20 47 25 48 30 C 48 32 46 33 44 33 L 14 33 C 13 33 12 33 12 32 Z", null, "#77737D", 1);
            body.Fill = new LinearGradientBrush(Ui.C("#B6B2BC"), Ui.C("#8A8691"), 90);
            Put(toy, new Ellipse { Width = 11, Height = 12, Fill = Ui.B("#A19DA7"), Stroke = Ui.B("#77737D"), StrokeThickness = 0.8 }, 29, 11);
            Put(toy, new Ellipse { Width = 6, Height = 7.5, Fill = Ui.B("#F7A8B8") }, 31.5, 13.3);
            Put(toy, new Ellipse { Width = 2.8, Height = 2.8, Fill = Ui.B("#2B1D14") }, 39.5, 22);
            AddPath(toy, "M 44 28 L 50 26 M 44 29.6 L 50 30.6", null, "#77737D", 0.6);
            Put(toy, new Ellipse { Width = 4.4, Height = 4, Fill = Ui.B("#F48FB1") }, 46, 27.5);
            // Behind the cat, so a caught mouse ends up under its paws.
            S.Layer.Children.Insert(0, toy);

            pullLine = new Line { Stroke = Ui.OrangeDeep, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            S.Layer.Children.Insert(0, pullLine);
            meter = new Canvas { Width = 46, Height = 7, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            meter.Children.Add(new Rectangle { Width = 46, Height = 7, RadiusX = 3.5, RadiusY = 3.5, Fill = new SolidColorBrush(Color.FromArgb(170, 255, 250, 242)), Stroke = Ui.OrangeDeep, StrokeThickness = 1 });
            meterFill = new Rectangle { Width = 0, Height = 7, RadiusX = 3.5, RadiusY = 3.5, Fill = Ui.Orange };
            meter.Children.Add(meterFill);
            S.Add(meter);

            x = S.W * 0.3;
            S.Float("Pull the toy mouse back and let go", S.W / 2 - 140, S.H * 0.35, S.Hint, 16);
        }

        public override void Down(Point p)
        {
            if (Math.Abs(p.X - x) > 34 || p.Y < S.Floor - 64 || p.Y > S.Floor + 14) return;
            holding = true;
            v = spring = 0;
            grabX = x;
            grabOffset = p.X - x;
            bob.Y = 0;
            pullLine.Visibility = meter.Visibility = Visibility.Visible;
        }

        public override void Move(Point p)
        {
            base.Move(p);
            if (!holding) return;
            // Pulled back along the floor like the real toy; it runs off the other way.
            x = Math.Max(Half, Math.Min(S.W - Half, p.X - grabOffset));
            x = Math.Max(grabX - MaxPull, Math.Min(grabX + MaxPull, x));
            double pull = grabX - x;
            if (Math.Abs(pull) > 2) Face(Math.Sign(pull));
            keyPhase = pull * 0.09;
        }

        public override void Up(Point p)
        {
            if (!holding) return;
            holding = false;
            pullLine.Visibility = meter.Visibility = Visibility.Hidden;
            double pull = grabX - x;
            if (Math.Abs(pull) < 14) return;   // only picked up and put down again
            // More winding runs faster and for longer, which makes it harder to catch.
            double wind = Math.Abs(pull) / MaxPull;
            topSpeed = 280 + 600 * wind;
            runTime = spring = 1 + 3 * wind;
            v = Math.Sign(pull) * topSpeed;
            runs++;
            if (S.Rng.NextDouble() < 0.3) Sfx.Trill();
        }

        public override void Frame(double t, double dt)
        {
            CatBody cat = S.Cat;
            bool landed = cat.Step(dt);
            StepToy(t, dt);
            bool running = !holding && v != 0;

            switch (mode)
            {
                case Mode.Crouch:
                    // Gone too far to leap for: back to the chase.
                    if (holding || !running || Math.Abs(Ahead(0.8) - cat.Mid) > 380)
                    {
                        cat.X = crouchX;
                        mode = Mode.Watch;
                        break;
                    }
                    cat.X = crouchX + Math.Sin(t * 45) * 1.2;   // butt wiggle
                    if (t >= crouchUntil) Pounce();
                    break;

                case Mode.Pounce:
                    // It only counts if the paws come down on the mouse while it is still running.
                    if (running && (landed || cat.Vy > 0) && cat.Lift < 45 && Math.Abs(cat.Paws.X - x) < 38) Catch(t);
                    else if (landed)
                    {
                        mode = Mode.Watch;
                        cooldownUntil = t + 0.4 + S.Rng.NextDouble() * 0.5;
                        if (running && S.Rng.NextDouble() < 0.25) Sfx.Question();
                    }
                    break;

                case Mode.Proud:
                    if (cat.Jumping) x = Math.Max(Half, Math.Min(S.W - Half, cat.Mid + cat.Dir * 18));
                    if (landed) cat.View.Hop(6);
                    if (!cat.Jumping && t >= proudUntil)
                    {
                        cat.View.SetHappy(false);
                        mode = Mode.Watch;
                    }
                    break;

                default:
                    if (holding) cat.Stop();
                    else if (running)
                    {
                        double lead = Ahead(0.8) - cat.Mid;
                        if (t > cooldownUntil && Math.Abs(lead) < 250) Crouch(t, lead);
                        else cat.WalkTo(Reachable(x + v * 0.3), Math.Abs(x - cat.Mid) > 220 ? 520 : 280, dt);
                    }
                    else
                    {
                        // A mouse that has stopped gets watched from a polite distance, never sat on.
                        double side = x < cat.Mid ? 1 : -1;
                        double spot = x + side * 95;
                        if (spot < 60 || spot > S.W - 60) spot = x - side * 95;
                        cat.WalkTo(spot, 170, dt);
                    }
                    break;
            }

            if (mode == Mode.Proud && !cat.Jumping && S.MouseKnown) cat.Look(S.Mouse);
            else cat.Look(new Point(x, S.Floor - 16));
            cat.Render();
            Stage.Place(toy, x - 25, S.Floor - 33);
            double c = Math.Cos(keyPhase);
            keyTurn.ScaleX = c < 0 ? Math.Min(-0.15, c) : Math.Max(0.15, c);   // never fully edge-on, or the key blinks out
            if (holding)
            {
                pullLine.X1 = grabX;
                pullLine.X2 = x;
                pullLine.Y1 = pullLine.Y2 = S.Floor - 3;
                meterFill.Width = 46 * Math.Abs(grabX - x) / MaxPull;
                Stage.Place(meter, x - 23, S.Floor - 54);
            }
        }

        void StepToy(double t, double dt)
        {
            if (holding || v == 0)
            {
                bob.Y = 0;
                return;
            }
            if (spring > 0)
            {
                // Slower as the spring unwinds; once it is done, a short coast to a stop.
                spring = Math.Max(0, spring - dt);
                v = Math.Sign(v) * topSpeed * (0.35 + 0.65 * spring / runTime);
            }
            else v *= Math.Pow(0.004, dt);
            x += v * dt;
            if (x < Half) { x = Half; v = Math.Abs(v); }
            if (x > S.W - Half) { x = S.W - Half; v = -Math.Abs(v); }
            if (spring == 0 && Math.Abs(v) < 15) v = 0;
            if (v != 0) Face(Math.Sign(v));
            if (spring > 0) keyPhase += dt * (4 + Math.Abs(v) / 40);
            bob.Y = v == 0 ? 0 : -Math.Abs(Math.Sin(t * 30)) * 1.5;
        }

        void Crouch(double t, double lead)
        {
            CatBody cat = S.Cat;
            mode = Mode.Crouch;
            crouchX = cat.X;
            crouchUntil = t + 0.25 + S.Rng.NextDouble() * 0.3;
            cat.Stop();
            cat.Dir = Math.Abs(lead) > 8 ? Math.Sign(lead) : (x >= cat.Mid ? 1 : -1);
            cat.View.Face(cat.Dir);
        }

        void Pounce()
        {
            CatBody cat = S.Cat;
            cat.X = crouchX;
            // Aimed at where the mouse will be when the paws come down, not where it is now.
            double d = Ahead(0.5) - cat.Mid;
            double vy = -Math.Sqrt(2 * CatBody.Gravity * (45 + Math.Min(60, Math.Abs(d) * 0.2)));
            double flight = 2 * -vy / CatBody.Gravity;
            // A fast mouse is harder to judge, so it is not a sure thing.
            double reach = Ahead(flight) - cat.Mid + (S.Rng.NextDouble() - 0.5) * (40 + Math.Abs(v) * 0.15);
            cat.Jump(vy, Math.Max(-900, Math.Min(900, reach / flight)));
            mode = Mode.Pounce;
        }

        // Roughly where the mouse will be in a moment (bounces ignored).
        double Ahead(double seconds)
        {
            return Math.Max(Half, Math.Min(S.W - Half, x + v * Math.Min(seconds, spring + 0.15)));
        }

        // Walk targets stay where the cat can get to; WalkTo would otherwise walk on the spot at a wall.
        double Reachable(double target)
        {
            return Math.Max(60, Math.Min(S.W - 60, target));
        }

        void Catch(double t)
        {
            caught++;
            v = spring = 0;
            mode = Mode.Proud;
            proudUntil = t + 1.5;
            Sfx.Happy();
            S.Cat.View.SetHappy(true);
            S.Cat.View.Float(Lines.Pick("got it!", "gotcha!", "mine!"), Ui.OrangeDeep, 55, 0, 16);
        }

        void Face(int dir)
        {
            if (facing.ScaleX != dir) facing.ScaleX = dir;
        }

        public override string ScoreText { get { return "Runs " + runs + "    Caught " + caught; } }

        public override string Summary
        {
            get
            {
                if (caught > 0) return "Caught the toy mouse " + caught + " time" + (caught == 1 ? "" : "s") + ". It's mine now. *proud*";
                return runs > 0 ? "That mouse is too quick. Wind it up again soon? *wiggles*" : null;
            }
        }
    }

    // ---------- Hide & seek: the cat hides behind something in the room, find it ----------

    public class HideSeekGame : MiniGame
    {
        enum Mode { Leaving, Sneaking, Hidden, Popping, Cheer }

        class Spot
        {
            public Canvas El;
            public readonly RotateTransform Shake = new RotateTransform();
            public double Left, Width, Height;
            public double[] Tails;   // x, y, angle for each place the tail tip can peek out from behind it
        }

        readonly List<Spot> spots = new List<Spot>();
        readonly ScaleTransform tailGrow = new ScaleTransform(1, 0, 10, 46);
        readonly RotateTransform tailSway = new RotateTransform(0, 10, 46);
        Canvas tail;
        Point tailBase;
        Spot hideAt;
        Mode mode = Mode.Leaving;
        bool front, mewed;
        int exitDir, found, tries;
        double readyAt, tailAt, tailAngle, hiddenSince, cheerUntil;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            tail = new Canvas { Width = 20, Height = 46, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            var turn = new TransformGroup();
            turn.Children.Add(tailGrow);
            turn.Children.Add(tailSway);
            tail.RenderTransform = turn;
            AddPath(tail, "M 10 46 C 10 32 3 22 9 7", null, CatView.TailColors()[0], 12);
            AddPath(tail, "M 10 46 C 10 32 3 22 9 7", null, CatView.TailColors()[1], 8.5);
            S.Add(tail);

            Spot[] made = { Box(), Plant(), Cushion(), Basket(), Cabinet() };
            for (int i = 0; i < made.Length; i++)
            {
                Spot s = made[i];
                s.Left = S.W * (i + 0.5) / made.Length - s.Width / 2;
                Stage.Place(s.El, s.Left, S.Floor - s.Height);
                S.Add(s.El);
                spots.Add(s);
            }
            CatToFront();
            exitDir = S.Cat.Mid < S.W / 2 ? -1 : 1;
            S.Float("Shh… " + Name + " is off to hide", S.W / 2 - 110, S.H * 0.3, S.Hint, 16);
        }

        static readonly Brush FloorShadow = Frozen(new RadialGradientBrush(Color.FromArgb(90, 60, 30, 10), Color.FromArgb(0, 60, 30, 10)));

        static Brush Frozen(Brush b)
        {
            b.Freeze();
            return b;
        }

        // Light from the upper left: a touch lighter on that side.
        static Brush Lit(string light, string dark)
        {
            return Frozen(new LinearGradientBrush(Ui.C(light), Ui.C(dark), 20));
        }

        Spot NewSpot(double w, double h, params double[] tails)
        {
            var s = new Spot { Width = w, Height = h, Tails = tails, El = new Canvas { Width = w, Height = h } };
            s.Shake.CenterX = w / 2;
            s.Shake.CenterY = h;
            s.El.RenderTransform = s.Shake;
            Put(s.El, new Ellipse { Width = w + 34, Height = 18, Fill = FloorShadow }, -17, h - 10);   // its shadow on the ground
            return s;
        }

        // An open cardboard box: the tail sticks up between the flaps.
        Spot Box()
        {
            Spot s = NewSpot(150, 96, 70, 30, -8, 80, 30, 8);
            Canvas c = s.El;
            AddPath(c, "M 12 16 L 0 2 L 52 6 L 62 16 Z", "#E8BC80", "#A8763E", 1.2);
            AddPath(c, "M 138 16 L 150 2 L 98 6 L 88 16 Z", "#D9A86A", "#A8763E", 1.2);
            Put(c, new Rectangle { Width = 122, Height = 6, Fill = Ui.B("#8A5E2E") }, 14, 12);
            Put(c, new Rectangle { Width = 126, Height = 80, Fill = Lit("#E6B474", "#C79052"), Stroke = Ui.B("#A8763E"), StrokeThickness = 1.2 }, 12, 16);
            Put(c, new Rectangle { Width = 126, Height = 5, Fill = Ui.B("#30000000") }, 12, 16);
            Put(c, new Rectangle { Width = 18, Height = 80, Fill = Ui.B("#28FFF4D8") }, 66, 16);   // packing tape
            Put(c, new Rectangle { Width = 30, Height = 9, RadiusX = 4.5, RadiusY = 4.5, Fill = Ui.B("#6E4822") }, 60, 36);
            return s;
        }

        // A potted plant: the tail curls out beside the pot.
        Spot Plant()
        {
            Spot s = NewSpot(96, 150, 26, 140, -62, 70, 140, 62);
            Canvas c = s.El;
            AddPath(c, "M 48 96 C 14 88 2 56 8 30 C 30 44 42 68 48 96 Z", "#5C9C5A", "#47804A", 1);
            AddPath(c, "M 48 96 C 82 88 94 56 88 30 C 66 44 54 68 48 96 Z", "#5C9C5A", "#47804A", 1);
            AddPath(c, "M 48 96 C 30 72 30 32 46 4 C 62 32 66 72 48 96 Z", "#6FAF6B", "#47804A", 1);
            AddPath(c, "M 48 96 C 24 86 14 72 16 60 C 32 66 42 78 48 96 Z", "#7DBE76", "#47804A", 1);
            AddPath(c, "M 48 96 C 72 86 82 72 80 60 C 64 66 54 78 48 96 Z", "#7DBE76", "#47804A", 1);
            AddPath(c, "M 14 104 L 82 104 L 75 150 L 21 150 Z", null, "#B35C33", 1.2).Fill = Lit("#E58A58", "#B9603A");
            Put(c, new Rectangle { Width = 84, Height = 14, RadiusX = 3, RadiusY = 3, Fill = Lit("#D8744A", "#B25632") }, 6, 92);
            Put(c, new Rectangle { Width = 68, Height = 4, Fill = Ui.B("#30000000") }, 14, 106);
            return s;
        }

        // A big floor cushion: the tail flicks up over the top.
        Spot Cushion()
        {
            Spot s = NewSpot(140, 74, 42, 30, -14, 98, 30, 14);
            Canvas c = s.El;
            var plush = new RadialGradientBrush(Ui.C("#F6C2C7"), Ui.C("#D98890")) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.8 };
            plush.Freeze();
            Put(c, new Rectangle { Width = 140, Height = 64, RadiusX = 30, RadiusY = 26, Fill = plush, Stroke = Ui.B("#C27880"), StrokeThickness = 1.4 }, 0, 10);
            AddPath(c, "M 16 30 Q 70 18 124 30", null, "#F4C4C9", 2.2);
            AddPath(c, "M 52 44 Q 70 49 88 44", null, "#D28B93", 1.4);
            Put(c, new Ellipse { Width = 9, Height = 7, Fill = Ui.B("#C27880") }, 65.5, 40);
            return s;
        }

        // A wicker basket with a handle.
        Spot Basket()
        {
            Spot s = NewSpot(124, 86, 44, 32, -12, 82, 32, 12);
            Canvas c = s.El;
            AddPath(c, "M 20 16 Q 62 -26 104 16", null, "#A87A3C", 5);
            AddPath(c, "M 4 18 L 120 18 L 108 86 L 16 86 Z", null, "#9C7038", 1.2).Fill = Lit("#D8AA68", "#B2834A");
            AddPath(c, "M 7 36 L 117 36 M 10 54 L 114 54 M 13 72 L 111 72", null, "#A87A3C", 1.2);
            AddPath(c, "M 32 20 L 34 86 M 52 20 L 53 86 M 72 20 L 71 86 M 92 20 L 90 86", null, "#B88A4C", 1);
            Put(c, new Rectangle { Width = 124, Height = 12, RadiusX = 6, RadiusY = 6, Fill = Ui.B("#B5853F"), Stroke = Ui.B("#9C7038"), StrokeThickness = 1 }, 0, 12);
            return s;
        }

        // A small cabinet: the tail peeks out at one side.
        Spot Cabinet()
        {
            Spot s = NewSpot(112, 118, 16, 98, -64, 96, 98, 64);
            Canvas c = s.El;
            Put(c, new Rectangle { Width = 104, Height = 100, Fill = Lit("#C4956A", "#A07450"), Stroke = Ui.B("#8D6440"), StrokeThickness = 1.4 }, 4, 10);
            Put(c, new Rectangle { Width = 112, Height = 10, RadiusX = 3, RadiusY = 3, Fill = Lit("#B88660", "#956844"), Stroke = Ui.B("#8D6440"), StrokeThickness = 1 }, 0, 2);
            Put(c, new Rectangle { Width = 42, Height = 80, Fill = Lit("#D4A87A", "#B58A5E"), Stroke = Ui.B("#8D6440"), StrokeThickness = 1 }, 12, 22);
            Put(c, new Rectangle { Width = 42, Height = 80, Fill = Lit("#CFA274", "#AE8458"), Stroke = Ui.B("#8D6440"), StrokeThickness = 1 }, 58, 22);
            Put(c, new Rectangle { Width = 104, Height = 5, Fill = Ui.B("#30000000") }, 4, 12);
            Put(c, new Ellipse { Width = 5, Height = 5, Fill = Ui.B("#7A5233") }, 47, 58);
            Put(c, new Ellipse { Width = 5, Height = 5, Fill = Ui.B("#7A5233") }, 60, 58);
            Put(c, new Rectangle { Width = 8, Height = 8, Fill = Ui.B("#7A5233") }, 10, 110);
            Put(c, new Rectangle { Width = 8, Height = 8, Fill = Ui.B("#7A5233") }, 94, 110);
            return s;
        }

        public override void Down(Point p)
        {
            if (mode != Mode.Hidden) return;
            Spot s = null;
            foreach (Spot k in spots)
                if (p.X > k.Left - 6 && p.X < k.Left + k.Width + 6 && p.Y > S.Floor - k.Height - 10 && p.Y < S.Floor + 10) s = k;
            if (tail.Visibility == Visibility.Visible)
            {
                double a = tailAngle * Math.PI / 180;
                if ((new Vector(p.X - tailBase.X - Math.Sin(a) * 30, p.Y - tailBase.Y + Math.Cos(a) * 30)).Length < 24) s = hideAt;
            }
            if (s == null) return;
            tries++;
            if (s == hideAt)
            {
                Found();
                return;
            }
            Wobble(s, 5);
            S.Float(Lines.Pick("not here", "not here", "only dust"), s.Left + s.Width / 2 - 30, S.Floor - s.Height - 34, S.Hint, 15);
        }

        public override void Frame(double t, double dt)
        {
            CatBody cat = S.Cat;
            bool landed = cat.Step(dt);
            switch (mode)
            {
                case Mode.Leaving:
                    // Right out of the room, so where it hides next stays a secret.
                    cat.Dir = exitDir;
                    cat.View.Face(exitDir);
                    cat.View.SetWalking(true);
                    cat.X += exitDir * 300 * dt;
                    if (cat.X < -CatView.W - 20 || cat.X > S.W + 20)
                    {
                        cat.Stop();
                        cat.View.Visibility = Visibility.Hidden;
                        mode = Mode.Sneaking;
                        readyAt = t + 1.2;
                    }
                    break;

                case Mode.Sneaking:
                    if (t >= readyAt) Hide(t);
                    break;

                case Mode.Hidden:
                    if (t >= tailAt)
                    {
                        tail.Visibility = Visibility.Visible;
                        tailGrow.ScaleY = Math.Min(1, (t - tailAt) / 0.35);
                        tailSway.Angle = tailAngle + Math.Sin(t * 2.3) * 6 + Math.Sin(t * 5.3) * 1.5;
                    }
                    // A small mew from the hiding place when it is taking a while.
                    if (!mewed && t > hiddenSince + 10)
                    {
                        mewed = true;
                        Sfx.Mew();
                        Wobble(hideAt, 2);
                    }
                    break;

                case Mode.Popping:
                    if (!front && cat.Vy > 0) CatToFront();
                    if (landed)
                    {
                        mode = Mode.Cheer;
                        cheerUntil = t + 1.5;
                        cat.View.SetHappy(true);
                        cat.View.Hop(8);
                    }
                    break;

                case Mode.Cheer:
                    if (t >= cheerUntil)
                    {
                        cat.View.SetHappy(false);
                        exitDir = cat.Mid < S.W / 2 ? -1 : 1;
                        mode = Mode.Leaving;
                    }
                    break;
            }
            if (cat.View.Visibility == Visibility.Visible && S.MouseKnown) cat.Look(S.Mouse);
            cat.Render();
        }

        void Hide(double t)
        {
            Spot last = hideAt;
            do hideAt = spots[S.Rng.Next(spots.Count)]; while (hideAt == last);
            int k = S.Rng.Next(hideAt.Tails.Length / 3) * 3;
            tailBase = new Point(hideAt.Left + hideAt.Tails[k], S.Floor - hideAt.Height + hideAt.Tails[k + 1]);
            tailAngle = hideAt.Tails[k + 2];
            Stage.Place(tail, tailBase.X - 10, tailBase.Y - 46);
            tailGrow.ScaleY = 0;
            tailAt = t + 2 + S.Rng.NextDouble() * 0.6;   // not straight away, so there is a moment to just guess
            hiddenSince = t;
            mewed = false;
            CatToBack();
            CatBody cat = S.Cat;
            cat.X = hideAt.Left + hideAt.Width / 2 - CatView.W / 2;
            cat.Lift = 0;
            mode = Mode.Hidden;
            S.Float(found + tries == 0 ? "come find me!" : Lines.Pick("ready!", "ready!", "come find me!"), S.W / 2 - 50, Stage.Top + 30, Ui.OrangeDeep, 17);
            if (S.Rng.NextDouble() < 0.6) Sfx.Trill();
        }

        void Found()
        {
            found++;
            tail.Visibility = Visibility.Hidden;
            CatBody cat = S.Cat;
            cat.View.Visibility = Visibility.Visible;
            // Pops up from behind it and lands on the side toward the middle of the room.
            cat.Jump(-700, cat.Mid < S.W / 2 ? 160 : -160);
            mode = Mode.Popping;
            Sfx.Happy();
            cat.View.Float(Lines.Pick("found me!", "found me!", "you found me!"), Ui.OrangeDeep, 18, -6, 16);
        }

        static void Wobble(Spot s, double size)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.45) };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(size, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.07))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(-size * 0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.18))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(size * 0.45, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45))));
            s.Shake.BeginAnimation(RotateTransform.AngleProperty, a);
        }

        // Behind the furniture while hiding, so it comes out from behind it; in front once it is out.
        void CatToBack()
        {
            S.Layer.Children.Remove(S.Cat.View);
            S.Layer.Children.Insert(0, S.Cat.View);
            front = false;
        }

        void CatToFront()
        {
            S.Layer.Children.Remove(S.Cat.View);
            S.Layer.Children.Add(S.Cat.View);
            front = true;
        }

        public override void Stop()
        {
            base.Stop();
            S.Cat.View.Visibility = Visibility.Visible;
        }

        public override string ScoreText { get { return "Found " + found + " · " + tries + (tries == 1 ? " try" : " tries"); } }

        public override string Summary
        {
            get
            {
                if (found > 0) return found + " time" + (found == 1 ? "" : "s") + " found. Next time I'm hiding better. *flicks tail*";
                return tries > 0 ? "Nobody found me. I'm very good at this. *peeks out*" : null;
            }
        }
    }

    // ---------- Night flight: a calm dream, float through the night sky and gather stars ----------

    public class NightFlightGame : MiniGame
    {
        const double RoundSeconds = 75;
        const string StarData = "M 17 6 L 19.7 13.3 L 27.5 13.6 L 21.4 18.4 L 23.5 25.9 L 17 21.6 L 10.5 25.9 L 12.6 18.4 L 6.5 13.6 L 14.3 13.3 Z";
        static readonly Brush StarInk = Ui.B("#FFE27A");

        class Star
        {
            public Canvas El;
            public RotateTransform Spin;
            public double X, Y, Vx, Phase;
            public bool Live;
        }

        // A cloud or a twinkling background star.
        class Drift
        {
            public UIElement El;
            public double X, Y, Speed, Phase;
        }

        readonly List<Star> stars = new List<Star>();   // reused: one that is collected or gone waits for the next
        readonly List<Drift> clouds = new List<Drift>();
        readonly List<Drift> twinkles = new List<Drift>();
        readonly ScaleTransform small = new ScaleTransform(0.8, 0.8, CatView.W / 2, CatView.H / 2);
        readonly RotateTransform tilt = new RotateTransform(0, CatView.W / 2, CatView.H / 2);
        Brush glow;
        Border endCard;
        double start = -1, nextSpawn, nowT, y, lastSound, lastTwinkle;
        int count, best, back;
        bool over;

        public override void Start(Stage stage)
        {
            base.Start(stage);
            best = S.Best("flight");
            var halo = new RadialGradientBrush(Color.FromArgb(140, 255, 226, 122), Color.FromArgb(0, 255, 226, 122));
            halo.Freeze();
            glow = halo;

            // The game's own sky, over the room and behind the cat.
            Back(new Rectangle { Width = S.W, Height = S.H, Fill = new LinearGradientBrush(Ui.C("#0B1433"), Ui.C("#2E4382"), 90) }, 0, 0);
            Back(new Ellipse { Width = 150, Height = 150, Fill = new RadialGradientBrush(Color.FromArgb(60, 255, 241, 196), Color.FromArgb(0, 255, 241, 196)) }, S.W - 205, Stage.Top - 10);
            Back(new Path { Data = Geometry.Parse("M 30 0 A 30 30 0 0 0 30 60 A 18 30 0 0 1 30 0 Z"), Fill = Ui.B("#FFF1C4") }, S.W - 150, Stage.Top + 35);
            for (int i = 0; i < 24; i++)
            {
                double size = 1.5 + S.Rng.NextDouble() * 2;
                var dot = new Ellipse { Width = size, Height = size, Fill = Brushes.White };
                Back(dot, S.Rng.NextDouble() * S.W, S.Rng.NextDouble() * (S.H - 100));
                twinkles.Add(new Drift { El = dot, Speed = 0.6 + S.Rng.NextDouble() * 1.6, Phase = S.Rng.NextDouble() * 6 });
            }
            Back(new Path
            {
                Data = Geometry.Parse("M 0 50 C 120 18 250 26 380 44 C 520 64 650 8 800 28 C 890 40 950 38 1000 30 L 1000 100 L 0 100 Z"),
                Fill = Ui.B("#16234C"),
                Stretch = Stretch.Fill,
                Width = S.W,
                Height = 74
            }, 0, S.H - 74);
            Brush cloudInk = Ui.B("#24D6DEFF");
            for (int i = 0; i < 4; i++)
            {
                double k = 0.8 + S.Rng.NextDouble() * 0.7;
                // One shape for all the puffs, so where they overlap is no brighter.
                var puffs = new GeometryGroup { FillRule = FillRule.Nonzero };
                puffs.Children.Add(new EllipseGeometry(new Point(40 * k, 30 * k), 40 * k, 17 * k));
                puffs.Children.Add(new EllipseGeometry(new Point(80 * k, 22 * k), 34 * k, 22 * k));
                puffs.Children.Add(new EllipseGeometry(new Point(120 * k, 31 * k), 36 * k, 15 * k));
                var cloud = new Drift
                {
                    El = new Path { Data = puffs, Fill = cloudInk },
                    X = S.Rng.NextDouble() * S.W,
                    Y = Stage.Top + S.Rng.NextDouble() * (S.H - Stage.Top - 160),
                    Speed = 10 + S.Rng.NextDouble() * 14
                };
                Back(cloud.El, cloud.X, cloud.Y);
                clouds.Add(cloud);
            }

            // Floats instead of walking: the position is set here every frame, never by WalkTo or Jump.
            CatBody cat = S.Cat;
            var turn = new TransformGroup();
            turn.Children.Add(small);
            turn.Children.Add(tilt);
            cat.View.RenderTransform = turn;
            cat.Dir = 1;
            cat.View.Face(1);
            y = S.Floor - CatView.H / 2;   // takes off from where it stood
            S.Float("Move the mouse up and down to fly", S.W / 2 - 130, S.H * 0.35, Ui.B("#DCE4FF"), 16);
        }

        public override bool OwnScenery { get { return true; } }

        void Back(UIElement e, double left, double top)
        {
            Stage.Place(e, left, top);
            S.Layer.Children.Insert(back++, e);
        }

        double Elapsed(double t) { return start < 0 ? 0 : t - start; }

        public override void Frame(double t, double dt)
        {
            nowT = t;
            if (start < 0) { start = t; nextSpawn = t + 1; }
            if (!over && Elapsed(t) >= RoundSeconds) Finish();
            if (!over && t >= nextSpawn)
            {
                Spawn();
                nextSpawn = t + 0.7 + S.Rng.NextDouble() * 0.8;
            }

            CatBody cat = S.Cat;
            double want = S.MouseKnown ? S.Mouse.Y : S.H * 0.45;
            want = Math.Max(Stage.Top + 46, Math.Min(S.H - 64, want));
            // Eased toward the mouse, so it drifts like in a dream instead of snapping.
            double next = y + (want - y) * (1 - Math.Exp(-dt * 3));
            if (dt > 0) tilt.Angle = Math.Max(-14, Math.Min(14, (next - y) / dt * 0.05));
            y = next;
            double cy = y + Math.Sin(t * 1.7) * 5;
            cat.X += (S.W * 0.26 - CatView.W / 2 - cat.X) * (1 - Math.Exp(-dt * 2.5));
            cat.Lift = S.Floor - CatView.H / 2 - cy;
            cat.Render();

            Star ahead = null;
            foreach (Star s in stars)
            {
                if (!s.Live) continue;
                s.X += s.Vx * dt;
                s.Phase += dt * 2;
                double sy = s.Y + Math.Sin(s.Phase) * 7;
                s.Spin.Angle += dt * 35;
                Stage.Place(s.El, s.X - 17, sy - 17);
                if (!over && (new Vector(s.X - cat.Mid, sy - cy)).Length < 50) Collect(s, sy, t);
                else if (s.X < -30) Retire(s);
                else if (s.X > cat.Mid && (ahead == null || s.X < ahead.X)) ahead = s;
            }
            cat.Look(ahead != null ? new Point(ahead.X, ahead.Y) : new Point(cat.Mid + 200, cy));

            foreach (Drift c in clouds)
            {
                c.X -= c.Speed * dt;
                if (c.X < -220)
                {
                    c.X = S.W + S.Rng.NextDouble() * 120;
                    c.Y = Stage.Top + S.Rng.NextDouble() * (S.H - Stage.Top - 160);
                }
                Stage.Place(c.El, c.X, c.Y);
            }
            // Twinkling is slow, so a dozen updates a second are plenty.
            if (t - lastTwinkle > 0.08)
            {
                lastTwinkle = t;
                foreach (Drift d in twinkles) d.El.Opacity = 0.3 + 0.35 * (1 + Math.Sin(t * d.Speed + d.Phase));
            }
        }

        void Spawn()
        {
            Star s = null;
            foreach (Star k in stars)
                if (!k.Live) { s = k; break; }
            if (s == null)
            {
                s = new Star { El = new Canvas { Width = 34, Height = 34, IsHitTestVisible = false }, Spin = new RotateTransform(0, 17, 17) };
                s.El.Children.Add(new Ellipse { Width = 34, Height = 34, Fill = glow });
                AddPath(s.El, StarData, "#FFE27A", "#FFF3B8", 1.5).RenderTransform = s.Spin;
                S.Add(s.El);
                stars.Add(s);
            }
            s.Live = true;
            s.X = S.W + 20;
            s.Y = Stage.Top + 40 + S.Rng.NextDouble() * (S.H - Stage.Top - 130);
            s.Vx = -(80 + S.Rng.NextDouble() * 130);
            s.Phase = S.Rng.NextDouble() * 6;
            Stage.Place(s.El, s.X - 17, s.Y - 17);
            s.El.Visibility = Visibility.Visible;
        }

        static void Retire(Star s)
        {
            s.Live = false;
            s.El.Visibility = Visibility.Hidden;
        }

        void Collect(Star s, double sy, double t)
        {
            count++;
            Retire(s);
            S.Float("✦", s.X - 6, sy - 12, StarInk, 13);
            S.Float("+1", s.X + 4, sy - 30, StarInk, 17);
            // A happy sound now and then, never for every star.
            if (t - lastSound > 6 && S.Rng.NextDouble() < 0.35)
            {
                lastSound = t;
                Sfx.Happy();
            }
            else if (S.Rng.NextDouble() < 0.12) S.Cat.View.Purr();
        }

        void Finish()
        {
            over = true;
            bool record = S.SetBest("flight", count);
            best = Math.Max(best, count);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var title = Ui.Heading("Collected " + count + " star" + (count == 1 ? "" : "s"), 24);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            var sub = Ui.Text(record ? "New best. *happy sigh*" : "Best: " + best, 14, Ui.Soft);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(sub);
            var again = Ui.Primary("Again", delegate { Restart(); });
            again.Margin = new Thickness(0, 14, 0, 0);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(again);
            endCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 250, 242)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(30, 20, 30, 22),
                Child = panel
            };
            Stage.Place(endCard, S.W / 2 - 150, S.H * 0.26);
            endCard.Width = 300;
            S.Add(endCard);
            Sfx.Happy();
        }

        void Restart()
        {
            if (endCard != null) S.Remove(endCard);
            foreach (Star s in stars) Retire(s);
            count = 0;
            over = false;
            start = -1;
        }

        public override void Stop()
        {
            base.Stop();
            S.Cat.View.ClearValue(UIElement.RenderTransformProperty);
        }

        public override string ScoreText
        {
            get
            {
                string time = over ? "" : Math.Max(0, (int)Math.Ceiling(RoundSeconds - Elapsed(nowT))) + "s    ";
                return "Stars " + count + "    " + time + "Best " + Math.Max(best, count);
            }
        }

        public override string Summary
        {
            get { return count > 0 ? "We flew all night. " + count + " star" + (count == 1 ? "" : "s") + " in my pocket. *sleepy purr*" : null; }
        }
    }
}

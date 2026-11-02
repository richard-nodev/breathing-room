using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    // The cat's home: a small transparent always-on-top window, just big enough for the cat to
    // hop, stretch and float a heart. The speech bubble has its own window (SpeechBubble).
    public class PetWindow : Window
    {
        const double WinW = 130, WinH = 140;
        const double CatLeft = (WinW - CatView.W) / 2, CatTop = WinH - CatView.H;
        // Where the cat sat inside the old, larger window (for positions saved by version 1).
        const double OldCatLeft = 95, OldCatTop = 240;

        public readonly CatView Cat = new CatView();
        public event Action Clicked;
        public event Action BreathingFinished;
        // A fullscreen game or video is in front: the cat keeps still and doesn't follow the mouse.
        public bool Playing
        {
            get { return playing; }
            set { playing = value; Cat.Calm = value; }
        }
        bool playing;

        readonly Canvas root = new Canvas { Width = WinW, Height = WinH };
        readonly SpeechBubble speech = new SpeechBubble();
        readonly DispatcherTimer hideTimer = new DispatcherTimer();
        readonly DispatcherTimer eyeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        readonly DispatcherTimer lifeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer walkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly DispatcherTimer topTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        readonly Random rng = new Random();

        Point lastCursor;
        bool pressed, dragged;
        Point pressCursor;
        double pressLeft, pressTop;
        DateTime hoverStart = DateTime.MaxValue;
        DateTime lastPurr = DateTime.MinValue;
        DateTime lastPetMove = DateTime.MinValue;
        DateTime lastPetPoint = DateTime.MinValue;
        bool petting;
        double walkTarget = double.NaN;
        DateTime lastWalkEnd = DateTime.MinValue;
        bool breathing;
        DispatcherTimer breathTimer;
        bool bubbleOpen;

        public double HomeLeft { get; private set; }
        public double HomeTop { get; private set; }
        public bool BubbleOpen { get { return bubbleOpen; } }

        public PetWindow()
        {
            Title = "Breathing Room";
            Width = WinW;
            Height = WinH;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Content = root;

            Canvas.SetLeft(Cat, CatLeft);
            Canvas.SetTop(Cat, CatTop);
            Cat.Cursor = Cursors.Hand;
            root.Children.Add(Cat);

            speech.MouseEnter += delegate { hideTimer.Stop(); };
            speech.MouseLeave += delegate { if (bubbleOpen) { hideTimer.Interval = TimeSpan.FromSeconds(4); hideTimer.Start(); } };
            LocationChanged += delegate { if (bubbleOpen) speech.Place(Anchor); };
            Closed += delegate { speech.Close(); };

            hideTimer.Tick += delegate { hideTimer.Stop(); HideBubble(); };
            eyeTimer.Tick += delegate { FollowCursor(); };
            lifeTimer.Tick += delegate { Live(); };
            walkTimer.Tick += delegate { WalkStep(); };
            topTimer.Tick += delegate
            {
                if (IsVisible) Native.KeepOnTop(this);
                if (speech.IsVisible) Native.KeepOnTop(speech);
            };

            Cat.MouseLeftButtonDown += OnCatDown;
            Cat.MouseMove += OnCatMove;
            Cat.MouseLeftButtonUp += OnCatUp;

            SourceInitialized += delegate { Native.MakeOverlay(this, false); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) return;
                bubbleOpen = false;
                hideTimer.Stop();
                speech.HideNow();
                petting = false;
                Cat.SetHappy(false);
                if (breathing) { breathing = false; if (breathTimer != null) breathTimer.Stop(); Cat.ResumeBreathing(); }
                Sfx.PurrOff();
            };
            Loaded += delegate { eyeTimer.Start(); lifeTimer.Start(); topTimer.Start(); };
        }

        // ---------- placement ----------

        static Rect Work { get { return SystemParameters.WorkArea; } }

        public void PlaceHome(double savedLeft, double savedTop)
        {
            Settings s = Store.Data.Settings;
            if (s.PetLayout < 2 && !double.IsNaN(savedLeft) && !double.IsNaN(savedTop))
            {
                // Saved by version 1, whose window was bigger: keep the cat where the user put it.
                savedLeft += OldCatLeft - CatLeft;
                savedTop += OldCatTop - CatTop;
                s.PetX = savedLeft;
                s.PetY = savedTop;
            }
            if (s.PetLayout < 2)
            {
                s.PetLayout = 2;
                Store.Save();
            }
            Rect wa = Work;
            double left = double.IsNaN(savedLeft) ? wa.Right - CatLeft - CatView.W - 24 : savedLeft;
            double top = double.IsNaN(savedTop) ? wa.Bottom - WinH + 6 : savedTop;
            Clamp(ref left, ref top);
            Left = HomeLeft = left;
            Top = HomeTop = top;
        }

        // The bubble opens above the cat, so the cat keeps enough room above its head for the
        // tallest bubble (the menu) instead of being covered by it near the top of the screen.
        const double Headroom = 240;

        static void Clamp(ref double left, ref double top)
        {
            Rect wa = Work;
            left = Math.Max(wa.Left - CatLeft, Math.Min(wa.Right - CatLeft - CatView.W, left));
            top = Math.Max(wa.Top + Headroom - CatTop - 14, Math.Min(wa.Bottom - WinH + 10, top));
        }

        // The top of the cat's head on screen, where the bubble's tail points.
        Point Anchor
        {
            get { return new Point(Left + CatLeft + CatView.W / 2, Top + CatTop + 14); }
        }

        // ---------- bubble ----------

        static TextBlock BubbleText(string text, double size)
        {
            // *words* are actions and render as soft italics.
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = Ui.Font, FontSize = size, Foreground = Ui.Ink };
            string[] parts = text.Split('*');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var run = new Run(parts[i]);
                if (i % 2 == 1)
                {
                    run.FontStyle = FontStyles.Italic;
                    run.Foreground = Ui.Soft;
                }
                tb.Inlines.Add(run);
            }
            return tb;
        }

        void Open(IEnumerable<UIElement> children, double seconds)
        {
            bubbleOpen = true;
            hideTimer.Stop();
            hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            hideTimer.Start();
            if (Cat.IsSleeping) Cat.SetSleeping(false);
            if (IsVisible) speech.Open(children, Anchor);
            else pending = children;
        }

        IEnumerable<UIElement> pending;
        DateTime openedAt = DateTime.MinValue;

        // A new bubble often opens right where the last one was; the second click of a double-click
        // must not answer a question the user hasn't read yet.
        bool TooSoon()
        {
            return (DateTime.Now - openedAt).TotalMilliseconds < 400;
        }

        // For README pictures: the cat with its bubble above it, on one canvas, without any windows.
        internal Canvas Compose()
        {
            double bw = 0, bh = 0;
            FrameworkElement bubbleContent = null;
            if (pending != null) bubbleContent = speech.Detach(pending, out bw, out bh);
            speech.Close();
            Content = null;
            double width = Math.Max(bw, WinW), headTop = CatTop + 14;
            var canvas = new Canvas { Width = width, Height = bh - headTop + WinH };
            if (bubbleContent != null)
            {
                Canvas.SetLeft(bubbleContent, (width - bw) / 2);
                Canvas.SetTop(bubbleContent, 0);
                canvas.Children.Add(bubbleContent);
            }
            Canvas.SetLeft(root, (width - WinW) / 2);
            Canvas.SetTop(root, bh - headTop);
            canvas.Children.Insert(0, root);
            return canvas;
        }

        public void HideBubble()
        {
            if (!bubbleOpen) return;
            bubbleOpen = false;
            hideTimer.Stop();
            speech.FadeOut();
        }

        public void Say(string text, double seconds)
        {
            Open(new UIElement[] { BubbleText(text, 13.5) }, seconds);
        }

        // A message with answer chips and an optional quiet link underneath (e.g. "changed my mind").
        public void Ask(string text, string[] labels, Action<int> answered, double seconds, string linkText, Action link)
        {
            var items = new List<UIElement>();
            if (!string.IsNullOrEmpty(text)) items.Add(BubbleText(text, 13.5));
            if (labels != null && labels.Length > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    chips.Children.Add(Ui.Pill(labels[i], delegate
                    {
                        if (TooSoon()) return;
                        HideBubble();
                        if (answered != null) answered(index);
                    }));
                }
                items.Add(chips);
            }
            if (!string.IsNullOrEmpty(linkText))
                items.Add(Ui.Link(linkText, delegate
                {
                    if (TooSoon()) return;
                    HideBubble();
                    if (link != null) link();
                }));
            Open(items, seconds);
            openedAt = DateTime.Now;
        }

        // ---------- mouse ----------

        void OnCatDown(object sender, MouseButtonEventArgs e)
        {
            pressed = true;
            dragged = false;
            pressCursor = Native.CursorDip(this);
            pressLeft = Left;
            pressTop = Top;
            Cat.CaptureMouse();
            e.Handled = true;
        }

        void OnCatMove(object sender, MouseEventArgs e)
        {
            if (!pressed) return;
            Point p = Native.CursorDip(this);
            double dx = p.X - pressCursor.X, dy = p.Y - pressCursor.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) < 5) return;
            if (!dragged)
            {
                dragged = true;
                StopWalking();
                HideBubble();
                if (petting) StopPetting();
                Sfx.Mew();
            }
            double left = pressLeft + dx, top = pressTop + dy;
            Clamp(ref left, ref top);
            Left = left;
            Top = top;
        }

        void OnCatUp(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false;
            Cat.ReleaseMouseCapture();
            if (dragged)
            {
                HomeLeft = Left;
                HomeTop = Top;
                Store.Data.Settings.PetX = Left;
                Store.Data.Settings.PetY = Top;
                Store.Save();
                Cat.Hop(6);
                Sfx.Trill();
            }
            else if (breathing)
            {
                StopBreathing("Okay. *stretches*");
            }
            else if (bubbleOpen)
            {
                HideBubble();
            }
            else if (Clicked != null)
            {
                Clicked();
            }
            e.Handled = true;
        }

        // ---------- life ----------

        void FollowCursor()
        {
            if (!IsVisible) return;
            Point p = Native.CursorDip(this);
            if (Math.Abs(p.X - lastCursor.X) + Math.Abs(p.Y - lastCursor.Y) < 1) return;
            lastCursor = p;
            if (Cat.IsSleeping || Playing) return;
            double eyeX = Left + CatLeft + 55, eyeY = Top + CatTop + 56;
            Cat.LookAt(p.X - eyeX, p.Y - eyeY);

            // Hover is checked against the cursor itself: MouseLeave never fires if the window hid under it.
            bool over = p.X > Left + CatLeft + 8 && p.X < Left + CatLeft + CatView.W - 8 &&
                        p.Y > Top + CatTop + 14 && p.Y < Top + WinH;
            if (!over)
            {
                hoverStart = DateTime.MaxValue;
                if (petting) StopPetting();
            }
            else
            {
                if (hoverStart == DateTime.MaxValue) hoverStart = DateTime.Now;
                lastPetMove = DateTime.Now;
            }
            if (over && (DateTime.Now - hoverStart).TotalSeconds > 0.6 && !pressed && !breathing)
            {
                if (!petting) StartPetting();
                Cat.LeanInto(p.X - (Left + CatLeft + CatView.W / 2));
                // The purr builds up while the petting goes on.
                if ((DateTime.Now - hoverStart).TotalSeconds > 4) Sfx.PurrOn(0.5f);
                if ((DateTime.Now - lastPurr).TotalSeconds > 0.7)
                {
                    lastPurr = DateTime.Now;
                    Cat.Purr();
                }
            }
        }

        // Petting: happy eyes, little hearts and a soft purr for as long as the hand keeps moving.
        void StartPetting()
        {
            petting = true;
            Cat.SetHappy(true);
            if ((DateTime.Now - lastPetPoint).TotalSeconds > 30)
            {
                lastPetPoint = DateTime.Now;
                Friendship.Add(1);
            }
            Sfx.PurrOn(0.38f);
            if (Lines.Chance(0.3)) Sfx.Happy();
        }

        void StopPetting()
        {
            petting = false;
            Cat.SetHappy(false);
            Cat.Relax();
            if (!breathing) Sfx.PurrOff();
        }

        void Live()
        {
            if (petting && (DateTime.Now - lastPetMove).TotalSeconds > 6) StopPetting();
            if (!IsVisible || pressed || breathing) return;
            int idle = Native.IdleMilliseconds();

            if (Cat.IsSleeping)
            {
                if (idle < 1500)
                {
                    Cat.SetSleeping(false);
                    if (Lines.Chance(0.5)) Sfx.Yawn();
                    if (!Playing && Lines.Chance(0.25)) Say(Lines.Wake(), 3);
                }
                return;
            }
            // No naps over a fullscreen game or video: a controller or a film produces no keyboard
            // or mouse input, but the user is right there.
            if (idle > 4 * 60 * 1000 && !bubbleOpen && double.IsNaN(walkTarget) && !Playing)
            {
                Cat.SetSleeping(true);
                return;
            }

            // Wander only while the user is away from the keyboard, so it never pulls focus from a game.
            if (Playing || bubbleOpen || !double.IsNaN(walkTarget)) return;
            if (idle > 20000 && (DateTime.Now - lastWalkEnd).TotalSeconds > 25 && Lines.Chance(1.0 / 20))
            {
                double target = HomeLeft + (rng.NextDouble() * 2 - 1) * 260;
                double top = Top;
                Clamp(ref target, ref top);
                StartWalking(target);
            }
            else if (idle < 2000 && Math.Abs(Left - HomeLeft) > 2)
            {
                StartWalking(HomeLeft);
            }
        }

        void StartWalking(double target)
        {
            walkTarget = target;
            Cat.Face(target > Left ? 1 : -1);
            Cat.SetWalking(true);
            walkTimer.Start();
        }

        void StopWalking()
        {
            walkTimer.Stop();
            walkTarget = double.NaN;
            Cat.SetWalking(false);
            lastWalkEnd = DateTime.Now;
        }

        void WalkStep()
        {
            if (double.IsNaN(walkTarget)) { StopWalking(); return; }
            double step = 70 * walkTimer.Interval.TotalSeconds;
            double d = walkTarget - Left;
            if (Math.Abs(d) <= step)
            {
                Left = walkTarget;
                StopWalking();
                Cat.Face(-1);
                return;
            }
            Left += Math.Sign(d) * step;
        }

        // ---------- breathe with me ----------

        public void BreatheWithMe()
        {
            if (breathing) return;
            StopWalking();
            breathing = true;
            Cat.SetSleeping(false);
            Sfx.PurrOn(0.22f);
            Say("Let's just breathe for a minute. Follow me if you want.", 4);
            int phase = 0;
            breathTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            breathTimer.Tick += delegate
            {
                if (phase >= 12)
                {
                    StopBreathing("*stretches* That was nice.");
                    if (BreathingFinished != null)
                    {
                        // Let "that was nice" sit for a moment before asking anything.
                        var ask = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                        ask.Tick += delegate { ask.Stop(); if (!breathing && BreathingFinished != null) BreathingFinished(); };
                        ask.Start();
                    }
                    return;
                }
                bool inhale = phase % 2 == 0;
                Cat.BreatheTo(inhale ? 1.17 : 1.0, inhale ? 4 : 6);
                Open(new UIElement[] { BubbleText(inhale ? "in…" : "out…", 16) }, inhale ? 4 : 6);
                breathTimer.Interval = TimeSpan.FromSeconds(inhale ? 4 : 6);
                phase++;
            };
            breathTimer.Start();
        }

        void StopBreathing(string line)
        {
            if (breathTimer != null) breathTimer.Stop();
            breathing = false;
            Sfx.PurrOff();
            Cat.ResumeBreathing();
            Say(line, 4);
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BreathingRoom
{
    // The cat, drawn with plain shapes in a 110 x 100 box. It blinks, breathes and sways its tail on
    // its own; everything else (looking, sleeping, purring, hopping) is driven from outside.
    public class CatView : Canvas
    {
        public const double W = 110, H = 100;
        public static readonly Point Mouth = new Point(55, 70);

        // The look every new cat gets (set from the user's choice in "Mochi's look").
        public static string Coat = "ginger", Accessory = "none";

        class Palette
        {
            public string Light, Dark, Outline, Tail, Stripe, Belly, Paw, Ear, Line, Iris;
            public bool Patches;
        }

        static Palette PaletteFor(string coat)
        {
            switch (coat)
            {
                case "grey": return new Palette { Light = "#EEF0F4", Dark = "#A3ABB8", Outline = "#6B7280", Tail = "#9AA2AF", Stripe = "#7C8492", Belly = "#F7F8FA", Paw = "#F1F3F6", Ear = "#F4B6B6", Line = "#2B2F38" };
                case "black": return new Palette { Light = "#5E5E6A", Dark = "#2E2E36", Outline = "#16161C", Tail = "#3A3A44", Belly = "#4C4C57", Paw = "#5A5A64", Ear = "#C98C96", Line = "#E2E2EA", Iris = "#E8D35A" };
                case "snow": return new Palette { Light = "#FFFFFF", Dark = "#E6E3DD", Outline = "#A9A39A", Tail = "#EFEDE8", Belly = "#FFFFFF", Paw = "#FFFFFF", Ear = "#F7B8C0", Line = "#3A3A3A", Iris = "#7FB8E8" };
                case "calico": return new Palette { Light = "#FFFBF4", Dark = "#EFE4D4", Outline = "#A8774A", Tail = "#F2A04E", Belly = "#FFFFFF", Paw = "#FFF6EA", Ear = "#F7A8A0", Line = "#2B1D14", Patches = true };
                case "cream": return new Palette { Light = "#FFF3E0", Dark = "#F0D0A2", Outline = "#B89064", Tail = "#EBCB9C", Stripe = "#DDB27C", Belly = "#FFF9EF", Paw = "#FFF3E2", Ear = "#F6B3A8", Line = "#3A2A20" };
                default: return new Palette { Light = "#FFDCA6", Dark = "#F2A04E", Outline = "#B8692E", Tail = "#EE9845", Stripe = "#D9823A", Belly = "#FFEBD0", Paw = "#FFDDB0", Ear = "#F7A8A0", Line = "#2B1D14" };
            }
        }

        Brush Outline, EyeInk;

        // Outline and fill of the tail in the current coat (hide & seek shows just the tail tip).
        public static string[] TailColors()
        {
            Palette p = PaletteFor(Coat);
            return new[] { p.Outline, p.Tail };
        }

        readonly Canvas body = new Canvas { Width = W, Height = H };
        readonly ScaleTransform breath = new ScaleTransform(1, 1, 55, 96);
        readonly ScaleTransform flip = new ScaleTransform(1, 1, 55, 50);
        readonly TranslateTransform hop = new TranslateTransform();
        readonly RotateTransform lean = new RotateTransform(0, 55, 96);
        readonly RotateTransform tail = new RotateTransform(0, 88, 82);
        readonly Canvas[] eyes = new Canvas[2];
        readonly TranslateTransform[] look = { new TranslateTransform(), new TranslateTransform() };
        readonly ScaleTransform[] blink = { new ScaleTransform(1, 1, 5, 6.5), new ScaleTransform(1, 1, 5, 6.5) };
        readonly Path[] lids = new Path[2];
        Path mouth;
        Ellipse mouthO;
        readonly DispatcherTimer blinkTimer = new DispatcherTimer();
        readonly DispatcherTimer tailTimer = new DispatcherTimer();
        readonly DispatcherTimer zzzTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        readonly Random rng = new Random();
        bool sleeping, happy, walking, alive;

        public bool IsSleeping { get { return sleeping; } }

        // While a game or video is in front: only blinks, nothing else moves.
        public bool Calm { get; set; }

        public CatView()
        {
            Width = W;
            Height = H;
            var group = new TransformGroup();
            group.Children.Add(breath);
            group.Children.Add(flip);
            group.Children.Add(hop);
            group.Children.Add(lean);
            body.RenderTransform = group;
            Children.Add(body);
            Draw();

            // Like a real cat it mostly sits still: blinks, and flicks its tail now and then. Anything
            // that moves on a transparent always-on-top window costs CPU on every frame, so nothing
            // loops forever, and everything stops while the cat can't be seen.
            blinkTimer.Tick += delegate { Blink(); blinkTimer.Interval = TimeSpan.FromSeconds(2.5 + rng.NextDouble() * 4); };
            tailTimer.Tick += delegate
            {
                if (!Calm) FlickTail();
                tailTimer.Interval = TimeSpan.FromSeconds(8 + rng.NextDouble() * 10);
            };
            zzzTimer.Tick += delegate { if (!Calm) Float("z", Ui.B("#8C776A"), 82 + rng.Next(6), 18, 15); };
            IsVisibleChanged += delegate { if (IsVisible) Wake(); else Rest(); };
        }

        void Wake()
        {
            if (alive) return;
            alive = true;
            blinkTimer.Interval = TimeSpan.FromSeconds(2);
            blinkTimer.Start();
            tailTimer.Interval = TimeSpan.FromSeconds(3);
            tailTimer.Start();
            if (sleeping) zzzTimer.Start();
        }

        void Rest()
        {
            alive = false;
            blinkTimer.Stop();
            tailTimer.Stop();
            zzzTimer.Stop();
            tail.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // Petting: the cat leans into the hand (toward dx, the cursor's offset from its middle) and
        // settles a little lower, like a cat pushing its head into a palm.
        public void LeanInto(double dx)
        {
            double angle = Math.Max(-8, Math.Min(8, dx * 0.16));
            if (Math.Abs(angle - leanTarget) < 1.2) return;
            leanTarget = angle;
            var a = new DoubleAnimation(angle, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(a, 24);
            lean.BeginAnimation(RotateTransform.AngleProperty, a);
            var squish = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(420));
            Timeline.SetDesiredFrameRate(squish, 24);
            breath.BeginAnimation(ScaleTransform.ScaleYProperty, squish);
        }

        public void Relax()
        {
            leanTarget = 0;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(a, 24);
            lean.BeginAnimation(RotateTransform.AngleProperty, a);
            Settle();
        }

        double leanTarget;

        // A lazy sway: out, back, a smaller one, rest.
        public void FlickTail()
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.8) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45)), new SineEase()));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0)), new SineEase()));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8)), new SineEase()));
            Timeline.SetDesiredFrameRate(a, 20);
            tail.BeginAnimation(RotateTransform.AngleProperty, a);
        }

        static Path P(string data, Brush fill, Brush stroke, double thickness)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        static Ellipse E(double x, double y, double w, double h, Brush fill)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            SetLeft(e, x);
            SetTop(e, y);
            return e;
        }

        // Redraws the cat in the current Coat and Accessory, keeping its mood.
        public void ApplyLook()
        {
            body.Children.Clear();
            Draw();
            if (sleeping) ShowLids("M 33 57 Q 38 61 43 57", "M 67 57 Q 72 61 77 57");
            else if (happy) ShowLids("M 33 59 Q 38 53 43 59", "M 67 59 Q 72 53 77 59");
        }

        void Draw()
        {
            Palette pal = PaletteFor(Coat);
            Outline = Ui.B(pal.Outline);
            EyeInk = Ui.B(pal.Line);
            var fur = new RadialGradientBrush(Ui.C(pal.Light), Ui.C(pal.Dark))
            {
                GradientOrigin = new Point(0.42, 0.3),
                Center = new Point(0.45, 0.35),
                RadiusX = 0.75,
                RadiusY = 0.8
            };
            fur.Freeze();

            var tailGroup = new Canvas { RenderTransform = tail };
            tailGroup.Children.Add(P("M 86 84 C 108 80 112 58 101 46", null, Outline, 12));
            tailGroup.Children.Add(P("M 86 84 C 108 80 112 58 101 46", null, Ui.B(pal.Tail), 8.5));
            body.Children.Add(tailGroup);

            body.Children.Add(P("M 21 52 L 27 15 L 51 37 Z", pal.Patches ? (Brush)Ui.B("#F2A04E") : fur, Outline, 2));
            body.Children.Add(P("M 59 37 L 83 15 L 89 52 Z", pal.Patches ? (Brush)Ui.B("#3A3A44") : fur, Outline, 2));
            body.Children.Add(P("M 28 43 L 30.5 24 L 43 36 Z", Ui.B(pal.Ear), null, 0));
            body.Children.Add(P("M 67 36 L 79.5 24 L 82 43 Z", Ui.B(pal.Ear), null, 0));

            var torso = E(10, 28, 90, 68, fur);
            body.Children.Add(torso);
            if (pal.Patches)
            {
                // Calico: orange and dark patches, kept inside the body's outline.
                var patches = new Canvas { Clip = new EllipseGeometry(new Point(55, 62), 45, 34) };
                patches.Children.Add(E(4, 22, 46, 34, Ui.B("#F2A04E")));
                patches.Children.Add(E(68, 44, 40, 30, Ui.B("#3A3A44")));
                patches.Children.Add(E(60, 26, 22, 14, Ui.B("#F2A04E")));
                body.Children.Add(patches);
            }
            var rim = E(10, 28, 90, 68, null);
            rim.Stroke = Outline;
            rim.StrokeThickness = 2;
            body.Children.Add(rim);
            body.Children.Add(E(34, 66, 42, 27, Ui.B(pal.Belly)));

            if (pal.Stripe != null)
                foreach (string stripe in new[] { "M 49 32 Q 51 37 49 41", "M 55 30 Q 57 36 55 42", "M 61 32 Q 63 37 61 41" })
                    body.Children.Add(P(stripe, null, Ui.B(pal.Stripe), 2.6));

            if (Accessory == "bell" || Accessory == "scarf") DrawNeckwear();

            double[] eyeX = { 33, 67 };
            for (int i = 0; i < 2; i++)
            {
                var eye = new Canvas { Width = 10, Height = 13 };
                var tg = new TransformGroup();
                tg.Children.Add(blink[i]);
                tg.Children.Add(look[i]);
                eye.RenderTransform = tg;
                if (pal.Iris != null)
                {
                    // Light eyes with a slit pupil, so a dark or white coat still has a face.
                    eye.Children.Add(E(0, 0, 10, 13, Ui.B(pal.Iris)));
                    eye.Children.Add(E(3.2, 1.5, 3.6, 10, Ui.B("#1E1E24")));
                }
                else eye.Children.Add(E(0, 0, 10, 13, Ui.B("#2B1D14")));
                eye.Children.Add(E(5.2, 2.2, 3.6, 3.6, Brushes.White));
                SetLeft(eye, eyeX[i]);
                SetTop(eye, 50);
                eyes[i] = eye;
                body.Children.Add(eye);

                lids[i] = P("M 0 0", null, EyeInk, 2.3);
                lids[i].Visibility = Visibility.Collapsed;
                body.Children.Add(lids[i]);
            }

            var cheekL = E(23, 62, 12, 7, Ui.B("#FF8C8C"));
            var cheekR = E(75, 62, 12, 7, Ui.B("#FF8C8C"));
            cheekL.Opacity = cheekR.Opacity = 0.45;
            body.Children.Add(cheekL);
            body.Children.Add(cheekR);

            mouth = P("M 49 64 Q 52 68 55 64 Q 58 68 61 64", null, EyeInk, 1.8);
            body.Children.Add(mouth);
            mouthO = E(51.5, 63, 7, 8, EyeInk);
            mouthO.Visibility = Visibility.Collapsed;
            body.Children.Add(mouthO);

            var pawL = E(33, 88, 17, 9, Ui.B(pal.Paw));
            var pawR = E(60, 88, 17, 9, Ui.B(pal.Paw));
            pawL.Stroke = pawR.Stroke = Outline;
            pawL.StrokeThickness = pawR.StrokeThickness = 1.5;
            body.Children.Add(pawL);
            body.Children.Add(pawR);

            switch (Accessory)
            {
                case "bow":
                    body.Children.Add(P("M 31 31 L 18 23 L 19 40 Z", Ui.B("#FF8FB4"), Ui.B("#D9577F"), 1.5));
                    body.Children.Add(P("M 31 31 L 44 23 L 43 40 Z", Ui.B("#FF8FB4"), Ui.B("#D9577F"), 1.5));
                    body.Children.Add(E(27.5, 27.5, 7, 7, Ui.B("#E8608C")));
                    break;
                case "flowers":
                    // Placed along the top of the head so the crown follows its curve.
                    string[] petals = { "#FF8FB1", "#FFD84D", "#B48CFF", "#FFFFFF", "#FF8FB1", "#FFD84D", "#B48CFF" };
                    for (int i = 0; i < petals.Length; i++)
                    {
                        double a = Math.PI * (1.18 + 0.64 * i / (petals.Length - 1));
                        double x = 55 + 42 * Math.Cos(a), y = 62 + 31 * Math.Sin(a);
                        body.Children.Add(E(x - 5, y - 5, 10, 10, Ui.B(petals[i])));
                        body.Children.Add(E(x - 2, y - 2, 4, 4, Ui.B("#F2A04E")));
                    }
                    body.Children.Add(E(58, 25, 7, 4, Ui.B("#6DBE7A")));
                    break;
                case "hat":
                    body.Children.Add(P("M 43 31 L 55 -8 L 67 31 Z", Ui.B("#7FC8F8"), Ui.B("#3D8FC4"), 1.5));
                    body.Children.Add(P("M 47 19 L 63 17 M 50 8 L 60 7", null, Brushes.White, 2.5));
                    body.Children.Add(E(50.5, -13, 9, 9, Ui.B("#FFD84D")));
                    break;
            }
        }

        void DrawNeckwear()
        {
            // Worn around the body: clipped to its outline so the band wraps around the sides.
            var band = new Canvas { Clip = new EllipseGeometry(new Point(55, 62), 44, 33) };
            if (Accessory == "bell")
            {
                band.Children.Add(P("M 8 70 Q 55 88 102 70", null, Ui.B("#C9333A"), 5.5));
                band.Children.Add(P("M 8 69 Q 55 86.5 102 69", null, Ui.B("#E5484D"), 3.2));
                body.Children.Add(band);
                body.Children.Add(E(50.5, 79.5, 9, 9, Ui.B("#F2C94C")));
                body.Children.Add(E(52.5, 80.5, 3, 3, Ui.B("#FFF1B0")));
                body.Children.Add(P("M 55 85 L 55 88.5", null, Ui.B("#9C7A1E"), 1.4));
            }
            else
            {
                band.Children.Add(P("M 6 68 Q 55 90 104 68", null, Ui.B("#3B73B5"), 10));
                band.Children.Add(P("M 6 66 Q 55 87 104 66", null, Ui.B("#4F8FD9"), 7));
                body.Children.Add(band);
                body.Children.Add(P("M 64 80 Q 67 91 70 99 L 78 96 Q 75 88 72 79 Z", Ui.B("#4F8FD9"), Ui.B("#3B73B5"), 1.2));
                body.Children.Add(P("M 69 89 L 75 87 M 71 94 L 77 92", null, Ui.B("#DDEBFA"), 1.5));
            }
        }

        // Back to normal size after "breathe with me".
        void Settle()
        {
            foreach (DependencyProperty prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var a = new DoubleAnimation(1, TimeSpan.FromSeconds(0.8)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(a, 20);
                breath.BeginAnimation(prop, a);
            }
        }

        void ShowLids(string leftData, string rightData)
        {
            lids[0].Data = Geometry.Parse(leftData);
            lids[1].Data = Geometry.Parse(rightData);
            for (int i = 0; i < 2; i++)
            {
                lids[i].Visibility = Visibility.Visible;
                eyes[i].Visibility = Visibility.Collapsed;
            }
        }

        void OpenEyes()
        {
            for (int i = 0; i < 2; i++)
            {
                lids[i].Visibility = Visibility.Collapsed;
                eyes[i].Visibility = Visibility.Visible;
            }
        }

        public void Blink()
        {
            if (sleeping || happy) return;
            for (int i = 0; i < 2; i++)
            {
                var a = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(85)) { AutoReverse = true };
                Timeline.SetDesiredFrameRate(a, 30);
                blink[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        // dx, dy: direction toward what the cat is looking at, in its own coordinates.
        public void LookAt(double dx, double dy)
        {
            double len = Math.Sqrt(dx * dx + dy * dy);
            double max = 3.2;
            if (len > 0.001)
            {
                double k = Math.Min(max, len / 40) / len;
                dx *= k;
                dy *= k;
            }
            dx *= flip.ScaleX;
            // Eyes move at most 3 px; skip changes too small to see so a busy mouse doesn't redraw the cat.
            if (Math.Abs(look[0].X - dx) < 0.35 && Math.Abs(look[0].Y - dy) < 0.35) return;
            for (int i = 0; i < 2; i++)
            {
                look[i].X = dx;
                look[i].Y = dy;
            }
        }

        public void SetSleeping(bool value)
        {
            if (value == sleeping) return;
            sleeping = value;
            if (value)
            {
                happy = false;
                ShowLids("M 33 57 Q 38 61 43 57", "M 67 57 Q 72 61 77 57");
                if (alive) zzzTimer.Start();
            }
            else
            {
                zzzTimer.Stop();
                OpenEyes();
                Stretch();
            }
        }

        public void SetHappy(bool value)
        {
            if (value == happy || sleeping) return;
            happy = value;
            if (value) ShowLids("M 33 59 Q 38 53 43 59", "M 67 59 Q 72 53 77 59");
            else OpenEyes();
        }

        public void Purr()
        {
            Float("♥", Ui.B("#FF7F8F"), 30 + rng.Next(50), 22, 13 + rng.Next(5));
        }

        public void Surprise()
        {
            if (sleeping) SetSleeping(false);
            SetHappy(false);
            for (int i = 0; i < 2; i++)
            {
                var a = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(160)) { AutoReverse = true, BeginTime = TimeSpan.Zero };
                a.RepeatBehavior = new RepeatBehavior(1);
                blink[i].BeginAnimation(ScaleTransform.ScaleXProperty, a);
            }
            mouth.Visibility = Visibility.Collapsed;
            mouthO.Visibility = Visibility.Visible;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += delegate { t.Stop(); mouth.Visibility = Visibility.Visible; mouthO.Visibility = Visibility.Collapsed; };
            t.Start();
            Hop(10);
        }

        public void Hop(double height)
        {
            if (walking) return;
            var a = new DoubleAnimation(0, -height, TimeSpan.FromMilliseconds(170))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            hop.BeginAnimation(TranslateTransform.YProperty, a);
        }

        public void Stretch()
        {
            var a = new DoubleAnimation(1, 1.12, TimeSpan.FromMilliseconds(380))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            breath.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        }

        // +1 = facing/moving right, -1 = left. The tail trails behind.
        public void Face(int dir)
        {
            flip.ScaleX = dir > 0 ? -1 : 1;
        }

        public void SetWalking(bool value)
        {
            if (value == walking) return;
            walking = value;
            if (value)
            {
                var a = new DoubleAnimation(0, -3.5, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(a, 20);
                hop.BeginAnimation(TranslateTransform.YProperty, a);
            }
            else
            {
                hop.BeginAnimation(TranslateTransform.YProperty, null);
                hop.Y = 0;
            }
        }

        // Slow puff-up used by "breathe with me".
        public void BreatheTo(double scale, double seconds)
        {
            var y = new DoubleAnimation(scale, TimeSpan.FromSeconds(seconds)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            var x = new DoubleAnimation(1 + (scale - 1) * 0.6, TimeSpan.FromSeconds(seconds)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(x, 30);
            Timeline.SetDesiredFrameRate(y, 30);
            breath.BeginAnimation(ScaleTransform.ScaleYProperty, y);
            breath.BeginAnimation(ScaleTransform.ScaleXProperty, x);
        }

        public void ResumeBreathing()
        {
            Settle();
        }

        public void Float(string text, Brush brush, double x, double y, double size)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size, FontWeight = FontWeights.Bold, FontFamily = Ui.Font };
            var move = new TranslateTransform();
            tb.RenderTransform = move;
            SetLeft(tb, x);
            SetTop(tb, y);
            Children.Add(tb);
            var up = new DoubleAnimation(0, -34, TimeSpan.FromSeconds(2.2));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(2.2));
            Timeline.SetDesiredFrameRate(up, 24);
            Timeline.SetDesiredFrameRate(fade, 24);
            fade.Completed += delegate { Children.Remove(tb); };
            move.BeginAnimation(TranslateTransform.YProperty, up);
            tb.BeginAnimation(OpacityProperty, fade);
        }
    }
}

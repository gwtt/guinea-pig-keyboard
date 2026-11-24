using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WheekKeys
{
    internal sealed partial class PixelPetScene : FrameworkElement
    {
        public const double SceneWidth = 400;
        public const double SceneHeight = 308;
        private const double SceneOffsetX = 9;
        private const double SettleTime = 285;
        private const double BubbleTime = 395;
        private const double BlinkDuration = 180;
        private const double ScrollDuration = 245;
        private static readonly BitmapImage BodySource = Sprite("guinea-pig.png");
        private static readonly BitmapImage PawSource = Sprite("paw.png");
        private readonly Dictionary<string, EyeArt> coatEyes = new Dictionary<string, EyeArt>();
        private readonly Dictionary<string, BitmapSource> coatPaws = new Dictionary<string, BitmapSource>();
        private EyeArt Eyes;
        private DrawnPetArt drawnArt;
        private BitmapSource Paw;
        private Brush ForelegFill;
        private Brush ForelegEdge;
        private PetSkin skin;
        private static readonly BitmapImage MouseSprite = Sprite("mouse.png");
        private static readonly SolidColorBrush Paper = Paint(255, 243, 216);
        private static readonly SolidColorBrush Umber = Paint(98, 71, 55);
        private static readonly SolidColorBrush Amber = Paint(241, 180, 151);
        private static readonly Pen InkLine = new Pen(Umber, 1);
        private static readonly Dictionary<string, FormattedText> Labels = new Dictionary<string, FormattedText>();
        private static readonly Dictionary<string, ImageSource> Portraits = new Dictionary<string, ImageSource>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random blinkRandom = new Random();
        private readonly DispatcherTimer animation;
        private readonly DispatcherTimer gaze;
        private readonly bool followCursor;
        // These names refer to the viewer's side. The guinea pig faces the viewer.
        private Stroke left;
        private Stroke right;
        private Point mousePosition;
        private Point mouseDestination;
        private double lastFrame;
        private double lastMouseUse = double.NegativeInfinity;
        private double lastClick = double.NegativeInfinity;
        private double lastScroll = double.NegativeInfinity;
        private int scrollDirection;
        private double mouseGrip;
        private bool rightButton;
        private bool settlingMouse, pendingMouseClick;
        private double slowMouseUntil = double.NegativeInfinity;
        private bool showKeyLabels = true;
        private Rect bodyBounds = new Rect(126, 39, 174, 181);
        private Vector gazeOffset;
        private Vector headOffset;
        private double nextBlink;
        private double blinkStarted = double.NegativeInfinity;
        private int blinkFrame;
        private readonly IdleSleepAnimation sleep = new IdleSleepAnimation();
        private readonly MouthAnimation mouth = new MouthAnimation();
        private bool sleepVisualActive;

        internal Point HeadTop { get { return new Point(bodyBounds.Left + bodyBounds.Width / 2 + SceneOffsetX, bodyBounds.Top - 8); } }
        internal event Action HeadPositionChanged;

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        public PixelPetScene() : this(true) { }

        public PixelPetScene(bool followCursor)
        {
            this.followCursor = followCursor;
            Width = SceneWidth;
            Height = SceneHeight;
            Skin = PetSkin.All[0];
            animation = new DispatcherTimer(DispatcherPriority.Render);
            animation.Interval = TimeSpan.FromMilliseconds(24);
            animation.Tick += delegate
            {
                double now = clock.Elapsed.TotalMilliseconds;
                left.Expire(now);
                right.Expire(now);
                AdvanceMouse(now);
                if (!left.Active && !right.Active && mouseGrip == 0 && mousePosition == mouseDestination
                    && now - lastMouseUse > 250 && !settlingMouse && !pendingMouseClick) animation.Stop();
                InvalidateVisual();
            };
            gaze = new DispatcherTimer(DispatcherPriority.Background);
            gaze.Interval = TimeSpan.FromMilliseconds(40);
            gaze.Tick += delegate { AdvanceFace(clock.Elapsed.TotalMilliseconds); };
            Loaded += delegate
            {
                blinkStarted = double.NegativeInfinity;
                blinkFrame = 0;
                double now = clock.Elapsed.TotalMilliseconds;
                sleep.RecordActivity(now);
                mouth.Reset(now);
                ScheduleBlink(now);
                UpdateGaze();
                gaze.Start();
            };
            Unloaded += delegate
            {
                animation.Stop();
                gaze.Stop();
                settlingMouse = pendingMouseClick = false;
                slowMouseUntil = double.NegativeInfinity;
                blinkFrame = 0;
                blinkStarted = double.NegativeInfinity;
            };
        }

        private void ScheduleBlink(double now)
        {
            nextBlink = now + 3000 + blinkRandom.Next(3001);
        }

        private void AdvanceFace(double now)
        {
            if (mouth.Advance(now, sleep.IsWaking(now))) InvalidateVisual();
            bool active = sleep.Progress(now) > 0 || sleep.IsWaking(now);
            if (active)
            {
                gazeOffset = new Vector();
                AdvanceHead();
                InvalidateVisual();
            }
            else
            {
                UpdateGaze(); AdvanceHead(); AdvanceBlink(now);
                if (sleepVisualActive) InvalidateVisual();
            }
            sleepVisualActive = active;
        }

        private void RecordActivity()
        {
            double now = clock.Elapsed.TotalMilliseconds;
            bool drowsy = sleep.Progress(now) > 0;
            sleep.RecordActivity(now);
            if (!drowsy) return;
            mouth.Reset(now);
            blinkStarted = double.NegativeInfinity;
            blinkFrame = 0;
            ScheduleBlink(now);
            InvalidateVisual();
        }

        private void AdvanceBlink(double now)
        {
            if (now >= nextBlink)
            {
                blinkStarted = now;
                ScheduleBlink(now);
            }
            double age = now - blinkStarted;
            int frame = 0;
            if (drawnArt != null) frame = BlinkFrame(age);
            else if (age < 40) frame = 1;
            else if (age < 120) frame = 2;
            else if (age < BlinkDuration) frame = 1;
            if (frame == blinkFrame) return;
            blinkFrame = frame;
            InvalidateVisual();
        }

        private void UpdateGaze()
        {
            if (!followCursor || drawnArt != null) return;
            CursorPoint cursor;
            if (!GetCursorPos(out cursor) || PresentationSource.FromVisual(this) == null) return;
            LookAt(PointFromScreen(new Point(cursor.X, cursor.Y)));
        }

        public void LookAt(Point cursor)
        {
            if (drawnArt != null) return;
            cursor -= new Vector(SceneOffsetX, 0);
            Vector offset = Eyes.Offset(cursor, bodyBounds);
            if (offset == gazeOffset) return;
            gazeOffset = offset;
            InvalidateVisual();
        }

        private void AdvanceHead()
        {
            Vector delta = gazeOffset - headOffset;
            if (delta.LengthSquared == 0) return;
            headOffset += delta * (0.08 + 0.27 * sleep.MotionReadiness(clock.Elapsed.TotalMilliseconds));
            if (delta.LengthSquared < 0.0025) headOffset = gazeOffset;
            InvalidateVisual();
        }

        public bool ShowKeyLabels
        {
            get { return showKeyLabels; }
            set { showKeyLabels = value; InvalidateVisual(); }
        }

        public PetSkin Skin
        {
            get { return skin; }
            set
            {
                if (value == null || value == skin) return;
                if (skin == null || skin.IsDrawn != value.IsDrawn) ResetDeskPose(value.IsDrawn);
                drawnArt = value.IsDrawn ? DrawnPetArt.For(value) : null;
                if (!value.IsDrawn) ApplyPixelCoat(value.PixelCoatId);
                RenderOptions.SetBitmapScalingMode(this, value.IsDrawn ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetEdgeMode(this, value.IsDrawn ? EdgeMode.Unspecified : EdgeMode.Aliased);
                skin = value;
                mouth.Reset(clock.Elapsed.TotalMilliseconds);
                settlingMouse = pendingMouseClick = false;
                slowMouseUntil = double.NegativeInfinity;
                RecordActivity();
                InvalidateVisual();
            }
        }

        private void ApplyPixelCoat(string coatId)
        {
            CoatArt coat = CoatArt.Find(coatId);
            if (!coatEyes.TryGetValue(coat.Id, out Eyes))
            {
                Color eyeColor = coat.Id == "black-white" || coat.Id == "tricolor"
                    || coat.Id == "longhair-tricolor" || coat.Id == "longhair-black-white"
                    ? Color.FromRgb(48, 39, 34) : coat.Ink;
                BitmapSource body = coat.BodyAsset == null ? coat.Body(BodySource) : Sprite(coat.BodyAsset);
                Eyes = new EyeArt(body, false, eyeColor);
                coatEyes.Add(coat.Id, Eyes);
                coatPaws.Add(coat.Id, coat.Paw(PawSource));
            }
            Paw = coatPaws[coat.Id];
            var fill = new SolidColorBrush(coat.Fur);
            var edge = new SolidColorBrush(coat.Ink);
            fill.Freeze();
            edge.Freeze();
            ForelegFill = fill;
            ForelegEdge = edge;
        }

        internal static ImageSource Portrait(PetSkin skin)
        {
            if (skin.IsDrawn) return DrawnPetArt.For(skin).Portrait;
            CoatArt coat = CoatArt.Find(skin.PixelCoatId);
            ImageSource cached;
            if (Portraits.TryGetValue(coat.Id, out cached)) return cached;
            BitmapSource image = coat.BodyAsset == null ? coat.Body(BodySource) : Sprite(coat.BodyAsset);
            Color ink = coat.Id == "black-white" || coat.Id == "tricolor" || coat.Id == "longhair-tricolor" || coat.Id == "longhair-black-white"
                ? Color.FromRgb(48, 39, 34) : coat.Ink;
            EyeArt eyes = new EyeArt(image, false, ink);
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open()) eyes.Draw(dc, new Rect(8, 12, 100, 104), new Vector());
            drawing.Freeze();
            var portrait = new DrawingImage(drawing);
            portrait.Freeze();
            Portraits.Add(coat.Id, portrait);
            return portrait;
        }

        public void PressKey(int scan)
        {
            RecordActivity();
            if (drawnArt != null) { PressDrawnKey(scan); return; }
            bool onRight = PixelKeyboardArt.IsRight(scan);
            Stroke hand = onRight ? left : right;
            double now = clock.Elapsed.TotalMilliseconds;
            Point start = hand.Position(now);
            if (onRight)
            {
                start = MouseHand(start, now);
                mouseGrip = 0;
                settlingMouse = pendingMouseClick = false;
                slowMouseUntil = double.NegativeInfinity;
                lastMouseUse = double.NegativeInfinity;
                lastClick = double.NegativeInfinity;
                lastScroll = double.NegativeInfinity;
            }
            hand.Start(now, start, PixelKeyboardArt.Target(scan, onRight), scan);
            Wake();
        }

        public void MoveMouse(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;
            RecordActivity();
            if (drawnArt != null) MoveDrawnMouse(dx, dy, clock.Elapsed.TotalMilliseconds);
            else
            {
                mouseDestination.X = Math.Max(10, Math.Min(46, mouseDestination.X + dx * 0.12));
                mouseDestination.Y = Math.Max(210, Math.Min(270, mouseDestination.Y + dy * 0.12));
            }
            lastMouseUse = clock.Elapsed.TotalMilliseconds;
            double readiness = sleep.MotionReadiness(lastMouseUse);
            if (readiness < 0.999) slowMouseUntil = Math.Max(slowMouseUntil, lastMouseUse + 2400 * (1 - readiness));
            settlingMouse |= readiness < 0.999 && mouseGrip < 0.9;
            if (!animation.IsEnabled) Wake();
        }

        public void ClickMouse(bool isRightButton)
        {
            RecordActivity();
            rightButton = isRightButton;
            lastMouseUse = clock.Elapsed.TotalMilliseconds;
            double readiness = sleep.MotionReadiness(lastMouseUse);
            if (readiness < 0.999) slowMouseUntil = Math.Max(slowMouseUntil, lastMouseUse + 2400 * (1 - readiness));
            if (readiness < 0.999 && mouseGrip < 0.9)
            {
                settlingMouse = pendingMouseClick = true;
                lastClick = double.NegativeInfinity;
            }
            else { pendingMouseClick = false; lastClick = lastMouseUse + (drawnArt == null ? 0 : (1 - mouseGrip) * 110); }
            Wake();
        }

        public void ScrollMouse(int delta)
        {
            if (delta == 0) return;
            RecordActivity();
            scrollDirection = Math.Sign(delta);
            lastMouseUse = lastScroll = clock.Elapsed.TotalMilliseconds;
            double readiness = sleep.MotionReadiness(lastMouseUse);
            if (readiness < 0.999) slowMouseUntil = Math.Max(slowMouseUntil, lastMouseUse + 2400 * (1 - readiness));
            settlingMouse |= readiness < 0.999 && mouseGrip < 0.9;
            Wake();
        }

        private void Wake()
        {
            if (!animation.IsEnabled)
            {
                lastFrame = clock.Elapsed.TotalMilliseconds;
                animation.Start();
            }
            InvalidateVisual();
        }

        private void AdvanceMouse(double now)
        {
            double elapsed = Math.Max(0, now - lastFrame);
            lastFrame = now;
            double readiness = sleep.MotionReadiness(now);
            if (drawnArt != null && !settlingMouse && !pendingMouseClick && now >= slowMouseUntil)
            {
                AdvanceDrawnMouse(now, elapsed);
                return;
            }
            mousePosition += (mouseDestination - mousePosition) * (1 - Math.Exp(-elapsed * readiness / (32 + (1 - readiness) * 170)));
            if ((mouseDestination - mousePosition).LengthSquared < 0.01) mousePosition = mouseDestination;

            bool tapping = left.Active && now - left.Started < 130 && left.Started > Math.Max(lastClick, lastScroll);
            double gripTarget = !tapping && (now - lastMouseUse <= 250 || settlingMouse || pendingMouseClick) ? 1 : 0;
            double release = Math.Max(0, Math.Min(1, (slowMouseUntil - now) / 600));
            double duration = gripTarget > mouseGrip ? 30 + (1 - readiness) * 350 : 53 + (1 - readiness) * 180 + release * 220;
            double ease = 1 - Math.Exp(-elapsed * readiness / duration);
            mouseGrip += (gripTarget - mouseGrip) * ease;
            if (Math.Abs(gripTarget - mouseGrip) < 0.004) mouseGrip = gripTarget;
            if (mouseGrip >= 0.9 && (settlingMouse || pendingMouseClick))
            {
                if (pendingMouseClick) lastClick = now;
                settlingMouse = pendingMouseClick = false;
                lastMouseUse = now;
            }
        }

        private Point MouseButtonPoint()
        {
            return mousePosition + new Vector(rightButton ? -6 : 6, 11);
        }

        private Point MouseHand(Point keyboardTip, double now)
        {
            double press = Contact(now - lastClick);
            Point palm = mousePosition + new Vector(3, -2);
            Point target = palm + (MouseButtonPoint() - palm) * press;
            double scroll = Math.Max(0, 1 - (now - lastScroll) / ScrollDuration);
            target.Y -= 11 * scrollDirection * scroll;
            return keyboardTip + (target - keyboardTip) * Math.Max(mouseGrip, press);
        }

        private sealed class Stroke
        {
            public readonly Point Rest;
            public Point Target, StartPoint;
            public double Started;
            public int Scan;
            public bool Active;

            public Stroke(Point rest) { Rest = Target = StartPoint = rest; }

            public void Start(double now, Point start, Point target, int scan)
            {
                StartPoint = start;
                Started = now;
                Target = target;
                Scan = scan;
                Active = true;
            }

            public Point Position(double now)
            {
                if (!Active) return Rest;
                double age = Math.Max(0, now - Started);
                if (age < 52)
                {
                    double t = age / 52;
                    Point p = StartPoint + (Target - StartPoint) * Smooth(t);
                    p.Y -= 9 * Math.Sin(t * Math.PI);
                    return p;
                }
                if (age < 102) return Target;
                if (age >= SettleTime) return Rest;
                double back = (age - 102) / (SettleTime - 102);
                Point result = Target + (Rest - Target) * Smooth(back);
                result.Y -= Math.Sin(back * Math.PI) * 5;
                return result;
            }

            public double DrawnRestWeight(double now)
            {
                if (!Active) return 1;
                double age = Math.Max(0, now - Started);
                return Smooth(Math.Max(0, Math.Min(1, (age - 102) / (SettleTime - 102))));
            }

            public double BodyAttachment(double now)
            {
                if (!Active) return 1;
                double age = Math.Max(0, now - Started);
                if (age < 52) return 1 - Smooth(age / 52);
                return Smooth(Math.Max(0, Math.Min(1, (age - 102) / (SettleTime - 102))));
            }

            public double Contact(double now)
            {
                return Active ? PixelPetScene.Contact(now - Started) : 0;
            }

            public void Expire(double now)
            {
                if (!Active || now - Started < BubbleTime) return;
                Active = false;
                Scan = 0;
                Target = StartPoint = Rest;
            }
        }

        private static double Smooth(double t) { return t * t * (3 - 2 * t); }

        private static double Contact(double age)
        {
            if (age < 35 || age > 155) return 0;
            return Math.Min(1, Math.Min((age - 35) / 17, (155 - age) / 53));
        }

        internal static BitmapImage Sprite(string name) { return LoadSprite("pixel-" + name); }

        internal static BitmapImage LoadSprite(string name)
        {
            using (Stream data = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (data == null) throw new FileNotFoundException("缺少桌宠图片", name);
                BitmapImage sprite = new BitmapImage();
                sprite.BeginInit();
                sprite.CacheOption = BitmapCacheOption.OnLoad;
                sprite.StreamSource = data;
                sprite.EndInit();
                sprite.Freeze();
                return sprite;
            }
        }

        private static SolidColorBrush Paint(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (drawnArt != null) { RenderDrawn(dc, now); return; }
            Point lp = MouseHand(left.Position(now), now);
            Point rp = right.Position(now);
            double l = left.Contact(now), r = right.Contact(now);
            double click = Contact(now - lastClick);
            double mouseReach = Math.Max(mouseGrip, click);
            double lean = Math.Round((lp.X - left.Rest.X) * (0.40 + 0.16 * mouseReach) + (rp.X - right.Rest.X) * 0.40);
            double dip = Math.Round(Math.Max(click, Math.Max(l, r)) * 3);
            lp = FollowBody(lp, lean, dip, left.BodyAttachment(now) * (1 - mouseReach));
            rp = FollowBody(rp, lean, dip, right.BodyAttachment(now));

            Vector startle = sleep.StartleOffset(now);
            startle = new Vector(Math.Round(startle.X), Math.Round(startle.Y));
            lp = new Point(Math.Round(lp.X + startle.X * 0.55), Math.Round(lp.Y + startle.Y * 0.55));
            rp = new Point(Math.Round(rp.X + startle.X * 0.55), Math.Round(rp.Y + startle.Y * 0.55));
            Rect nextBody = new Rect(126, 39, 174, 181);
            nextBody.Offset(lean + startle.X, dip + startle.Y);
            SetBodyBounds(nextBody);
            dc.PushTransform(new TranslateTransform(SceneOffsetX, 0));
            int blink = sleep.IsWaking(now) ? 0 : blinkFrame;
            double drowsiness = sleep.Expression(now);
            Eyes.Draw(dc, bodyBounds, headOffset, blink, drowsiness, sleep.EyeScale(now), mouth.Frame);
            DrawMouse(dc, click);
            PixelKeyboardArt.Draw(dc, right.Scan, r, left.Scan, l);
            // Both forelegs start on the visible chest and pass in front of the desk.
            DrawForeleg(dc, left.Rest, lp, lean + startle.X, dip + startle.Y);
            DrawForeleg(dc, right.Rest, rp, lean + startle.X, dip + startle.Y);
            DrawPaw(dc, lp, false);
            DrawPaw(dc, rp, true);
            if (showKeyLabels)
            {
                DrawBubble(dc, left, now, false);
                DrawBubble(dc, right, now, true);
            }
            sleep.Draw(dc, new Point(bodyBounds.Right + 5, bodyBounds.Top + 32), now, true);
            dc.Pop();
        }

        private void SetBodyBounds(Rect bounds)
        {
            if (bodyBounds == bounds) return;
            bodyBounds = bounds;
            if (HeadPositionChanged != null) HeadPositionChanged();
        }

        private static Point FollowBody(Point tip, double lean, double dip, double attached)
        {
            return new Point(Math.Round(tip.X + lean * attached), Math.Round(tip.Y + dip * attached));
        }

        private void DrawPaw(DrawingContext dc, Point tip, bool rightSide)
        {
            dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
            if (!rightSide) dc.PushTransform(new ScaleTransform(-1, 1));
            dc.DrawImage(Paw, new Rect(-12, -24, 25, 23));
            if (!rightSide) dc.Pop();
            dc.Pop();
        }

        private void DrawForeleg(DrawingContext dc, Point rest, Point tip, double lean, double dip)
        {
            Point carriedRest = rest + new Vector(lean, dip);
            Vector extension = tip - carriedRest;
            double reach = Math.Min(1, extension.Length / 35);
            Point shoulder = new Point(carriedRest.X + Math.Max(-38, Math.Min(38, extension.X * 0.42)),
                carriedRest.Y - 11 - 20 * reach);
            Point palm = new Point(tip.X, tip.Y - 11);
            Vector span = palm - shoulder;
            if (span.Length < 6) return;
            double bend = Math.Min(9, span.Length * 0.16);
            Geometry outer = CreateArmBand(shoulder, span, bend, 0);
            Geometry fill = CreateArmBand(shoulder, span, bend, 2);
            Geometry border = Geometry.Combine(outer, fill, GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(ForelegEdge, null, border);
            dc.DrawGeometry(ForelegFill, null, fill);
        }

        private static Geometry CreateArmBand(Point shoulder, Vector span, double bend, int inset)
        {
            // Union the curved, tapering stroke into two-pixel scanlines on the sprite grid.
            int first = (int)Math.Floor(Math.Min(shoulder.Y, shoulder.Y + span.Y) / 2) - 6;
            int last = (int)Math.Ceiling((Math.Max(shoulder.Y, shoulder.Y + span.Y) + bend) / 2) + 6;
            int[] leftEdge = new int[last - first + 1], rightEdge = new int[leftEdge.Length];
            for (int j = 0; j < leftEdge.Length; j++) leftEdge[j] = int.MaxValue;
            int steps = (int)Math.Ceiling(span.Length / 2);
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                int cx = (int)Math.Round((shoulder.X + t * span.X) / 2);
                int cy = (int)Math.Round((shoulder.Y + t * span.Y + 4 * bend * t * (1 - t)) / 2);
                // Cover the entire shoulder cap, then introduce the side outline along the arm.
                double border = inset * Math.Max(0, Math.Min(1, (t * span.Length - 8) / 12));
                double radius = 5.4 - t - border;
                for (int dy = -(int)radius; dy <= (int)radius; dy++)
                {
                    int dx = (int)Math.Sqrt(radius * radius - dy * dy);
                    int row = cy + dy - first;
                    leftEdge[row] = Math.Min(leftEdge[row], cx - dx);
                    rightEdge[row] = Math.Max(rightEdge[row], cx + dx);
                }
            }
            int top = 0, bottom = leftEdge.Length - 1;
            while (leftEdge[top] == int.MaxValue) top++;
            while (leftEdge[bottom] == int.MaxValue) bottom--;

            // A single closed fill avoids seams between scanlines at fractional desktop scales.
            var silhouette = new StreamGeometry();
            using (StreamGeometryContext path = silhouette.Open())
            {
                path.BeginFigure(new Point(leftEdge[top] * 2, (first + top) * 2), true, true);
                for (int row = top; row <= bottom; row++)
                {
                    path.LineTo(new Point(leftEdge[row] * 2, (first + row) * 2), false, false);
                    path.LineTo(new Point(leftEdge[row] * 2, (first + row + 1) * 2), false, false);
                }
                for (int row = bottom; row >= top; row--)
                {
                    path.LineTo(new Point((rightEdge[row] + 1) * 2, (first + row + 1) * 2), false, false);
                    path.LineTo(new Point((rightEdge[row] + 1) * 2, (first + row) * 2), false, false);
                }
            }
            return silhouette;
        }

        private void DrawMouse(DrawingContext dc, double press)
        {
            double x = Math.Round(mousePosition.X), y = Math.Round(mousePosition.Y);
            double down = Math.Round(press);
            dc.PushTransform(new RotateTransform(180, x, y));
            dc.DrawImage(MouseSprite, new Rect(x - 17, y - 24 + down, 34, 48 - down));
            dc.Pop();
            if (press > 0)
            {
                dc.PushOpacity(press * 0.38);
                dc.DrawEllipse(Amber, null, MouseButtonPoint(), 4.5, 6.2);
                dc.Pop();
            }
        }

        private static void DrawBubble(DrawingContext dc, Stroke stroke, double now, bool onRight)
        {
            if (!stroke.Active) return;
            string label = PixelKeyboardArt.Label(stroke.Scan);
            if (String.IsNullOrEmpty(label)) return;
            double age = now - stroke.Started;
            if (age < 22 || age >= BubbleTime) return;
            double t = (age - 22) / (BubbleTime - 22);
            double opacity = Math.Min(1, Math.Min(t * 9, (1 - t) * 3.8));
            FormattedText text;
            if (!Labels.TryGetValue(label, out text))
            {
                var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 16, Umber, 1.0);
                Labels.Add(label, text);
            }
            double w = Math.Max(29, text.Width + 13);
            double x = onRight ? Math.Max(300, stroke.Target.X + 6) : Math.Min(90, stroke.Target.X - 6);
            double y = 173 - 34 * t;
            dc.PushOpacity(opacity);
            x = Math.Round(x);
            y = Math.Round(y);
            dc.DrawRectangle(Amber, InkLine, new Rect(x - w / 2, y - 11, w, 24));
            dc.DrawRectangle(Paper, InkLine, new Rect(x - w / 2, y - 14, w, 24));
            dc.DrawText(text, new Point(x - text.Width / 2, y - 2 - text.Height / 2));
            dc.Pop();
        }
    }
}

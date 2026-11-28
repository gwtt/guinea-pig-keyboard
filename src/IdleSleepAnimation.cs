using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class IdleSleepAnimation
    {
        public const double IdleDelayMilliseconds = 30000;
        public const double CloseMilliseconds = 700;
        public const double WakeMilliseconds = 420;
        public const double MotionWakeMilliseconds = 1600;
        private double lastActivity;
        private double wakeStarted = double.NegativeInfinity;
        private double wakeAmount, wakeSleepStart;
        private static readonly Brush SleepBrush = MakeBrush();
        private static readonly Geometry[] PixelLetters = { MakePixelZ(2), MakePixelZ(2), MakePixelZ(3) };
        private static readonly FormattedText[] Letters = { MakeText(12), MakeText(14), MakeText(16) };

        public void RecordActivity(double now)
        {
            double amount = Progress(now);
            if (amount > 0)
            {
                wakeStarted = now;
                wakeAmount = amount;
                wakeSleepStart = lastActivity + IdleDelayMilliseconds + CloseMilliseconds;
            }
            lastActivity = now;
        }

        public double Progress(double now)
        {
            return Math.Max(0, Math.Min(1, (now - lastActivity - IdleDelayMilliseconds) / CloseMilliseconds));
        }

        internal double TimeSinceDozeStarted(double now)
        {
            return now - lastActivity - IdleDelayMilliseconds;
        }

        public int EyeStage(double now)
        {
            double progress = Progress(now);
            if (progress <= 0) return 0;
            if (progress < 0.35) return 1;
            return progress < 1 ? 2 : 3;
        }

        public bool IsWaking(double now)
        {
            return Progress(now) == 0 && wakeAmount > 0 && now >= wakeStarted && now - wakeStarted < WakeMilliseconds;
        }

        public double Expression(double now)
        {
            if (!IsWaking(now)) return Progress(now);
            double phase = Math.Min(1, (now - wakeStarted) / 100);
            return wakeAmount * (1 - phase * phase * (3 - 2 * phase));
        }

        public Vector StartleOffset(double now)
        {
            if (!IsWaking(now)) return new Vector();
            double age = now - wakeStarted;
            double y;
            if (age < 65) y = -7 * (1 - Math.Pow(1 - age / 65, 3));
            else if (age < 150) y = -7 + 8 * Ease((age - 65) / 85);
            else y = Math.Cos((age - 150) * Math.PI / 70) * (1 - Ease((age - 150) / (WakeMilliseconds - 150)));
            double x = 2 * Math.Sin(age * Math.PI / 45) * (1 - Ease(age / WakeMilliseconds));
            return new Vector(x * wakeAmount, y * wakeAmount);
        }

        public double EyeScale(double now)
        {
            if (!IsWaking(now)) return 1;
            double age = now - wakeStarted;
            double pop = age < 65 ? 1 - Math.Pow(1 - age / 65, 3)
                : age < 110 ? 1 : 1 - Ease((age - 110) / (WakeMilliseconds - 110));
            return 1 + 0.22 * wakeAmount * pop;
        }

        private static double Ease(double phase)
        {
            phase = Math.Max(0, Math.Min(1, phase));
            return phase * phase * (3 - 2 * phase);
        }

        public double MotionReadiness(double now)
        {
            if (Progress(now) > 0) return 1 - Progress(now);
            if (wakeAmount <= 0 || now < wakeStarted) return 1;
            double phase = Math.Max(0, Math.Min(1, (now - wakeStarted - 150) / (MotionWakeMilliseconds - 150)));
            return 1 - wakeAmount * (1 - phase * phase * (3 - 2 * phase));
        }

        public static int FrameIndex(double amount) { return Math.Max(0, Math.Min(16, (int)Math.Round(amount * 16))); }

        public static BitmapSource[] BlendEyes(BitmapSource open, BitmapSource sleepy)
        {
            int width = open.PixelWidth, height = open.PixelHeight, stride = width * 4;
            if (sleepy.PixelWidth != width || sleepy.PixelHeight != height) throw new ArgumentException("Eye frames must have the same size.");
            byte[] a = new byte[stride * height], b = new byte[a.Length];
            new FormatConvertedBitmap(open, PixelFormats.Pbgra32, null, 0).CopyPixels(a, stride, 0);
            new FormatConvertedBitmap(sleepy, PixelFormats.Pbgra32, null, 0).CopyPixels(b, stride, 0);
            var frames = new BitmapSource[17];
            frames[0] = open; frames[16] = sleepy;
            for (int step = 1; step < 16; step++)
            {
                byte[] pixels = new byte[a.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((a[i] * (16 - step) + b[i] * step + 8) / 16);
                frames[step] = BitmapSource.Create(width, height, open.DpiX, open.DpiY, PixelFormats.Pbgra32, null, pixels, stride);
                frames[step].Freeze();
            }
            return frames;
        }

        public void Draw(DrawingContext dc, Point anchor, double now, bool pixel)
        {
            bool waking = IsWaking(now);
            double opacity = Progress(now) >= 1 ? 1 : waking && wakeAmount >= 1 ? 1 - Ease((now - wakeStarted) / WakeMilliseconds) : 0;
            if (opacity <= 0) return;
            double elapsed = now - (waking ? wakeSleepStart : lastActivity + IdleDelayMilliseconds + CloseMilliseconds);
            for (int i = 0; i < 3; i++)
            {
                double phase = (elapsed / 2800.0 + i / 3.0) % 1;
                double x = anchor.X + phase * 20;
                double height = pixel ? (i == 2 ? 15 : 10) : Letters[i].Height;
                double y = anchor.Y - phase * 18 - height;
                if (pixel) { x = Math.Round(x); y = Math.Round(y); }
                dc.PushOpacity(Math.Sin(phase * Math.PI) * 0.86 * opacity);
                dc.PushTransform(new TranslateTransform(x, y));
                if (pixel) dc.DrawGeometry(SleepBrush, null, PixelLetters[i]);
                else dc.DrawText(Letters[i], new Point());
                dc.Pop();
                dc.Pop();
            }
        }

        private static Brush MakeBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(130, 105, 85));
            brush.Freeze();
            return brush;
        }

        private static FormattedText MakeText(double size)
        {
            return new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                size, SleepBrush, 1.0);
        }

        private static Geometry MakePixelZ(int cell)
        {
            string[] pattern = { "#####", "...#.", "..#..", ".#...", "#####" };
            var geometry = new GeometryGroup();
            for (int y = 0; y < pattern.Length; y++)
                for (int x = 0; x < pattern[y].Length; x++)
                    if (pattern[y][x] == '#') geometry.Children.Add(new RectangleGeometry(new Rect(x * cell, y * cell, cell, cell)));
            geometry.Freeze();
            return geometry;
        }
    }
}

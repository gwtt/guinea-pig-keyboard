using System;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal sealed partial class PixelPetScene
    {
        private double penUpdated = double.NegativeInfinity;
        private double penPressure, penAngle = -25;
        private bool penInRange, penTip;
        private static readonly Brush PenBody = FreezeBrush(77, 72, 91);
        private static readonly Brush PenLight = FreezeBrush(187, 174, 185);
        private static readonly Brush PenTip = FreezeBrush(43, 37, 50);

        private static Brush FreezeBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        internal void MovePen(PenInput.Sample sample)
        {
            if (!sample.InRange)
            {
                penInRange = false;
                if (!animation.IsEnabled) Wake();
                InvalidateVisual();
                return;
            }
            penInRange = true;
            RecordActivity();
            penUpdated = clock.Elapsed.TotalMilliseconds;
            penTip = sample.TipDown;
            penPressure = sample.Pressure.HasValue ? Math.Max(0, Math.Min(1, sample.Pressure.Value)) : 0;
            if (Math.Abs(sample.Dx) + Math.Abs(sample.Dy) > 0.01)
            {
                penAngle = Math.Max(-42, Math.Min(12, -25 + sample.Dx * 0.08));
                MoveMouse(sample.Dx, sample.Dy);
            }
            lastMouseUse = penUpdated;
            if (!animation.IsEnabled) Wake();
            InvalidateVisual();
        }

        private bool PenActive(double now) { return penInRange && now - penUpdated <= 350; }

        private void DrawPen(DrawingContext dc)
        {
            double x = Math.Round(mousePosition.X + 3), y = Math.Round(mousePosition.Y - 2);
            double press = penTip ? penPressure : 0;
            dc.PushTransform(new RotateTransform(penAngle + press * 8, x, y));
            dc.DrawRoundedRectangle(PenBody, null, new Rect(x - 3, y - 24 + press * 2, 6, 29), 2, 2);
            dc.DrawRectangle(PenLight, null, new Rect(x - 2, y - 19 + press * 2, 1, 17));
            var tip = new StreamGeometry();
            using (StreamGeometryContext path = tip.Open())
            {
                path.BeginFigure(new Point(x - 3, y + 4 + press * 2), true, true);
                path.LineTo(new Point(x + 3, y + 4 + press * 2), true, false);
                path.LineTo(new Point(x, y + 10 + press * 2), true, false);
            }
            tip.Freeze();
            dc.DrawGeometry(PenTip, null, tip);
            dc.Pop();
        }
    }
}

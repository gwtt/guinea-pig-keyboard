using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal sealed partial class PixelPetScene
    {
        private static readonly SolidColorBrush DrawnUmber = Paint(78, 53, 30);
        private static readonly SolidColorBrush DrawnAmber = Paint(229, 188, 97);
        private static readonly Pen DrawnInkLine = new Pen(DrawnUmber, 1.55);
        private static readonly Dictionary<string, FormattedText> DrawnLabels = new Dictionary<string, FormattedText>();
        private static readonly int[] MirroredGaze = { 0, 2, 1, 3, 4, 6, 5, 8, 7 };
        private double lastLook = double.NegativeInfinity;
        private Vector lookMotion;
        private int lookFrame;

        private void ResetDeskPose(bool drawn)
        {
            left = new Stroke(drawn ? new Point(172, 215) : new Point(183, 201));
            right = new Stroke(drawn ? new Point(215, 213) : new Point(239, 202));
            mousePosition = mouseDestination = drawn ? new Point(74, 234) : new Point(31, 243);
            mouseGrip = 0;
            lookFrame = 0;
            lastLook = lastMouseUse = lastClick = lastScroll = double.NegativeInfinity;
            gazeOffset = headOffset = new Vector();
        }

        private void PressDrawnKey(int scan)
        {
            bool onRight = KeyboardArt.IsRight(scan);
            Stroke hand = onRight ? left : right;
            double now = clock.Elapsed.TotalMilliseconds;
            Pose pose = DrawnPose(now);
            Point start = onRight ? pose.LeftTip : pose.RightTip;
            Point target = KeyboardArt.Target(scan, onRight);
            Stroke other = onRight ? right : left;
            double when = now;
            if (other.Active && other.Started <= now && Math.Abs(target.X - other.Target.X) > 140)
                when = Math.Max(now, other.Started + 130);
            settlingMouse = pendingMouseClick = false;
            slowMouseUntil = double.NegativeInfinity;
            lastMouseUse = lastClick = lastScroll = double.NegativeInfinity;
            hand.Start(when, start, target, scan);
            Wake();
        }

        private void MoveDrawnMouse(double dx, double dy, double now)
        {
            if (now - lastLook > 160) lookMotion = new Vector();
            lookMotion = lookMotion * 0.70 + new Vector(Math.Max(-40, Math.Min(40, dx)), Math.Max(-40, Math.Min(40, dy)));
            if (lookMotion.LengthSquared >= 3)
            {
                bool vertical = Math.Abs(lookMotion.Y) > Math.Abs(lookMotion.X) * 0.42;
                bool horizontal = Math.Abs(lookMotion.X) > Math.Abs(lookMotion.Y) * 0.42;
                if (!vertical) lookFrame = lookMotion.X < 0 ? 1 : 2;
                else if (!horizontal) lookFrame = lookMotion.Y < 0 ? 3 : 4;
                else if (lookMotion.Y < 0) lookFrame = lookMotion.X < 0 ? 5 : 6;
                else lookFrame = lookMotion.X < 0 ? 7 : 8;
            }
            lastLook = now;
            mouseDestination.X = Math.Max(65, Math.Min(82, mouseDestination.X + dx * 0.055));
            mouseDestination.Y = Math.Max(226, Math.Min(240, mouseDestination.Y + dy * 0.055));
        }

        private void AdvanceDrawnMouse(double now, double elapsed)
        {
            if (now - lastLook > 420) lookFrame = 0;
            bool holding = now - lastMouseUse < 520;
            bool typing = double.IsNegativeInfinity(lastMouseUse);
            mouseGrip = holding ? Math.Min(1, mouseGrip + elapsed / 145)
                : Math.Max(0, mouseGrip - elapsed / (typing ? 55 : 195));
            double traction = Smooth(Math.Max(0, (mouseGrip - 0.64) / 0.36));
            mousePosition += (mouseDestination - mousePosition) * (1 - Math.Exp(-elapsed / 48)) * traction;
            if ((mouseDestination - mousePosition).LengthSquared < 0.01) mousePosition = mouseDestination;
            if (!holding && mouseGrip == 0) mouseDestination = mousePosition;
        }

        private struct Pose
        {
            public Rect Body;
            public Point LeftTip, RightTip;
        }

        private Pose DrawnPose(double now)
        {
            Point lp = left.Position(now), rp = right.Position(now);
            double grip = Smooth(mouseGrip);
            double lean = Math.Max(-78, Math.Min(115, ((lp.X - left.Rest.X) + (rp.X - right.Rest.X)) * 0.84));
            lean += (mousePosition.X - 8 - 135 - lean) * grip;
            double reachY = Math.Max(0, Math.Max(lp.Y - left.Rest.Y, rp.Y - right.Rest.Y));
            double dip = Math.Min(19, reachY * 0.42) * (1 - grip) + (12 + (mousePosition.Y - 226) * 0.45) * grip;
            lp += new Vector(lean, dip * 0.68) * left.DrawnRestWeight(now);
            rp += new Vector(lean, dip * 0.68) * right.DrawnRestWeight(now);

            // 空着的前爪撑在键盘后沿，另一只绕过机身落到鼠标上。
            Point brace = new Point(153, 227);
            rp += (brace - rp) * (grip * right.DrawnRestWeight(now));
            double press = Contact(now - lastClick);
            Point mouseTip = mousePosition + new Vector(5.5 + (rightButton ? -11 : -1) * press,
                                                       2.5 + (rightButton ? -1 : 3.2) * press);
            double scroll = Math.Max(0, 1 - (now - lastScroll) / ScrollDuration);
            mouseTip.Y -= 11 * scrollDirection * scroll;
            lp += (mouseTip - lp) * grip;
            lp.Y -= Math.Sin(grip * Math.PI) * 5;
            Rect body = DrawnPetArt.RestBounds;
            body.Offset(lean, dip);
            return new Pose { Body = body, LeftTip = lp, RightTip = rp };
        }

        private static int BlinkFrame(double age)
        {
            if (age < 0 || age >= 200) return 0;
            if (age < 40 || age >= 160) return 9;
            if (age < 80 || age >= 120) return 10;
            return 11;
        }

        private void RenderDrawn(DrawingContext dc, double now)
        {
            Pose pose = DrawnPose(now);
            Vector startle = sleep.StartleOffset(now);
            pose.Body.Offset(startle);
            pose.LeftTip += startle * 0.55;
            pose.RightTip += startle * 0.55;
            SetBodyBounds(pose.Body);
            double l = left.Contact(now), r = right.Contact(now);
            double grip = Smooth(mouseGrip);
            dc.PushTransform(new TranslateTransform(SceneOffsetX, 0));
            KeyboardArt.Draw(dc, right.Scan, r, left.Scan, l);
            drawnArt.DrawMouse(dc, mousePosition, Contact(now - lastClick) * grip, rightButton);
            drawnArt.DrawPaw(dc, pose.RightTip, pose.Body, true, grip);
            drawnArt.DrawPaw(dc, pose.LeftTip, pose.Body, false, grip);
            double drowsiness = sleep.Expression(now);
            int frame = sleep.IsWaking(now) ? 0 : blinkFrame;
            if (drowsiness >= 0.65) frame = 11;
            else if (drowsiness >= 0.2 || (frame == 0 && Math.Max(l, r) > 0.45)) frame = 10;
            else if (frame == 0) frame = MirroredGaze[lookFrame];
            drawnArt.DrawFace(dc, pose.Body, frame);
            if (showKeyLabels)
            {
                DrawRoundedBubble(dc, left, now, false, pose.Body);
                DrawRoundedBubble(dc, right, now, true, pose.Body);
            }
            sleep.Draw(dc, new Point(pose.Body.Right + 5, pose.Body.Top + 32), now, false);
            dc.Pop();
        }

        private static void DrawRoundedBubble(DrawingContext dc, Stroke stroke, double now, bool onRight, Rect bodyRect)
        {
            if (!stroke.Active) return;
            string label = KeyboardArt.Label(stroke.Scan);
            if (String.IsNullOrEmpty(label)) return;
            double age = now - stroke.Started;
            if (age < 22 || age >= BubbleTime) return;
            double t = (age - 22) / (BubbleTime - 22);
            double opacity = Math.Min(1, Math.Min(t * 9, (1 - t) * 3.8));
            FormattedText text;
            if (!DrawnLabels.TryGetValue(label, out text))
            {
                var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 16, DrawnUmber, 1.0);
                DrawnLabels.Add(label, text);
            }
            double w = Math.Max(29, text.Width + 13);
            double x = onRight ? Math.Max(300, stroke.Target.X + 6) : Math.Min(90, stroke.Target.X - 6);
            double y = 173 - 34 * t;
            if (x + w / 2 > bodyRect.Left - 5 && x - w / 2 < bodyRect.Right + 5)
                y = Math.Min(y, bodyRect.Top - 18);
            dc.PushOpacity(opacity);
            dc.PushTransform(new RotateTransform(onRight ? 7 : -7, x, y));
            dc.DrawRoundedRectangle(DrawnAmber, DrawnInkLine, new Rect(x - w / 2, y - 11, w, 25), 5, 5);
            dc.DrawRoundedRectangle(Paper, DrawnInkLine, new Rect(x - w / 2, y - 14, w, 24), 5, 5);
            dc.DrawText(text, new Point(x - text.Width / 2, y - 2 - text.Height / 2));
            dc.Pop();
            dc.Pop();
        }
    }
}

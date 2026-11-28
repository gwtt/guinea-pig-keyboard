using System;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal sealed partial class PixelPetScene
    {
        private Stroke rearLeft, rearRight;
        private Stroke[] compactPaws;

        private bool RearPawsActive { get { return rearLeft != null && (rearLeft.Active || rearRight.Active); } }

        private void ResetCompactPaws(bool compact)
        {
            compactPaws = null;
            rearLeft = rearRight = null;
            if (!compact) return;
            Rect body = new Rect(135, 208 - drawnProne.BodyHeight, 129, drawnProne.BodyHeight);
            compactPaws = new Stroke[4];
            for (int i = 0; i < compactPaws.Length; i++)
                compactPaws[i] = new Stroke(new Point(drawnProne.RestTip(body, i).X, 210));
            left = compactPaws[0]; right = compactPaws[1];
            rearLeft = compactPaws[2]; rearRight = compactPaws[3];
        }

        private void ExpireRearPaws(double now)
        {
            if (rearLeft == null) return;
            rearLeft.Expire(now); rearRight.Expire(now);
        }

        private void PressCompactKey(int scan, bool onRight, double now)
        {
            Point target = PixelKeyboardArt.Target(scan, onRight);
            int foot = target.X >= 223 ? 2 + (target.Y < 244 ? 1 : 0) : 1;
            Stroke paw = compactPaws[foot];
            DrawnPose pose = CurrentCompactPose(now);
            Point[] tips = { pose.LeftTip, pose.RightTip, pose.RearLeftTip, pose.RearRightTip };
            double when = now;
            foreach (Stroke other in compactPaws)
                if (other != paw && other.Active && other.Started <= now && Math.Abs(target.X - other.Target.X) > 140)
                    when = Math.Max(when, other.Started + 130);
            lastMouseUse = lastClick = lastScroll = double.NegativeInfinity;
            settlingMouse = pendingMouseClick = false;
            slowMouseUntil = double.NegativeInfinity;
            paw.Start(when, tips[foot], target, scan);
            Wake();
        }

        private DrawnPose CurrentCompactPose(double now)
        {
            Point[] tips = new Point[4];
            double travel = 0, reachY = 0;
            for (int i = 0; i < tips.Length; i++)
            {
                tips[i] = compactPaws[i].Position(now);
                travel += tips[i].X - compactPaws[i].Rest.X;
                reachY = Math.Max(reachY, tips[i].Y - compactPaws[i].Rest.Y);
            }
            double grip = Smooth(mouseGrip);
            double lean = Math.Max(-78, Math.Min(115, travel * 0.84));
            lean += (mousePosition.X - 8 - 135 - lean) * grip;
            double dip = Math.Min(55, reachY * 0.95) * (1 - grip) + (30 + (mousePosition.Y - 235) * 0.45) * grip;
            Rect body = new Rect(135 + lean, 208 - drawnProne.BodyHeight + dip, 129, drawnProne.BodyHeight);
            for (int i = 0; i < tips.Length; i++)
                tips[i] += (drawnProne.RestTip(body, i) - tips[i]) * compactPaws[i].RestWeight(now);
            double press = Contact(now - lastClick);
            Point palm = mousePosition + new Vector(3, -2);
            Point support = new Point(palm.X + 16, drawnProne.RestTip(body, 1).Y);
            tips[1] += (support - tips[1]) * (grip * compactPaws[1].RestWeight(now));
            Point mouseTip = palm + (MouseButtonPoint() - palm) * press;
            double scroll = Math.Max(0, 1 - (now - lastScroll) / ScrollDuration);
            mouseTip.Y -= 11 * scrollDirection * scroll;
            tips[0] += (mouseTip - tips[0]) * grip;
            tips[0].Y -= Math.Sin(grip * Math.PI) * 5;
            return new DrawnPose { Body = body, LeftTip = tips[0], RightTip = tips[1], RearLeftTip = tips[2], RearRightTip = tips[3] };
        }

        private void DrawCompactFeet(DrawingContext dc, DrawnPose pose, Vector startle, double now, bool behindBody)
        {
            Point[] tips = { pose.LeftTip, pose.RightTip, pose.RearLeftTip, pose.RearRightTip };
            double[] extension = new double[4];
            double maximum = 0;
            for (int i = 0; i < tips.Length; i++)
            {
                double attached = compactPaws[i].RestWeight(now) * (i == 0 ? 1 - Smooth(mouseGrip) : 1);
                tips[i] += startle * (0.55 + 0.45 * attached);
                double distance = (tips[i] - drawnProne.RestTip(bodyBounds, i)).Length;
                extension[i] = Smooth(Math.Max(0, Math.Min(1, (distance - 1) / 5)));
                if (i == 0) extension[i] = Math.Max(extension[i], Smooth(mouseGrip));
                maximum = Math.Max(maximum, extension[i]);
            }
            int[] feet = behindBody ? new[] { 1, 3 } : new[] { 0, 2 };
            if (maximum < 0.001)
            {
                foreach (int i in feet) drawnProne.DrawRestFoot(dc, bodyBounds, i, 1);
                return;
            }
            foreach (int i in feet)
            {
                drawnProne.DrawRestFoot(dc, bodyBounds, i, 1 - extension[i]);
                if (extension[i] <= 0) continue;
                dc.PushOpacity(extension[i]);
                DrawDrawnArm(dc, tips[i], i % 2 != 0, i);
                dc.Pop();
            }
        }

        private void DrawCompactBubbles(DrawingContext dc, double now, double viewScale)
        {
            Stroke first = null, second = null;
            foreach (Stroke paw in compactPaws)
            {
                if (!paw.Active) continue;
                if (PixelKeyboardArt.IsRight(paw.Scan))
                { if (second == null || paw.Started > second.Started) second = paw; }
                else if (first == null || paw.Started > first.Started) first = paw;
            }
            if (first != null) DrawBubble(dc, desk, first, now, false, viewScale);
            if (second != null) DrawBubble(dc, desk, second, now, true, viewScale);
        }
    }
}

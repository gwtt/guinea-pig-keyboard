using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class DrawnPetArt
    {
        internal static readonly Rect RestBounds = new Rect(135, 123, 129, 85);
        private static readonly Dictionary<string, DrawnPetArt> Cache = new Dictionary<string, DrawnPetArt>();
        private static readonly Pen LimbOutline = new Pen(Brush(81, 58, 39), 1.05);
        private static readonly BitmapSource[] MouseSprites = MouseFrames();
        private readonly BitmapSource body;
        private readonly BitmapSource paw;
        private readonly BitmapSource[] eyes;
        private readonly Brush fur;
        public readonly ImageSource Portrait;

        private DrawnPetArt(string coat)
        {
            bool tricolor = coat == "tricolor";
            body = PixelPetScene.LoadSprite(tricolor ? "guinea-pig.png" : coat + "-body.png");
            paw = PixelPetScene.LoadSprite(tricolor ? "paw.png" : coat + "-paw.png");
            fur = coat == "teddy" ? Brush(168, 105, 70)
                : coat == "silver-agouti" ? Brush(161, 144, 134) : Brush(250, 228, 203);
            BitmapSource atlas = PixelPetScene.LoadSprite(coat + "-eyes.png");
            int width = atlas.PixelWidth / 4, height = atlas.PixelHeight / 3;
            eyes = new BitmapSource[12];
            for (int frame = 0; frame < eyes.Length; frame++)
            {
                eyes[frame] = new CroppedBitmap(atlas, new Int32Rect(frame % 4 * width, frame / 4 * height, width, height));
                eyes[frame].Freeze();
            }
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open()) DrawFace(dc, new Rect(0, 0, RestBounds.Width, RestBounds.Height), 0);
            var portrait = new DrawingImage(drawing);
            portrait.Freeze();
            Portrait = portrait;
        }

        public static DrawnPetArt For(PetSkin skin)
        {
            DrawnPetArt art;
            if (!Cache.TryGetValue(skin.Id, out art))
            {
                art = new DrawnPetArt(skin.NetworkId);
                Cache.Add(skin.Id, art);
            }
            return art;
        }

        public void DrawFace(DrawingContext dc, Rect bounds, int frame)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, bounds.X + bounds.Width / 2, 0));
            dc.DrawImage(body, bounds);
            dc.DrawImage(eyes[frame], new Rect(bounds.X + bounds.Width * 0.58, bounds.Y + bounds.Height * 0.25,
                bounds.Width * 0.22, bounds.Height * 0.30));
            dc.Pop();
        }

        private static BitmapSource[] MouseFrames()
        {
            var sheet = PixelPetScene.LoadSprite("mouse.png");
            var frames = new BitmapSource[3];
            int w = sheet.PixelWidth / frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new CroppedBitmap(sheet, new Int32Rect(i * w, 0, w, sheet.PixelHeight));
                frames[i].Freeze();
            }
            return frames;
        }

        public void DrawMouse(DrawingContext dc, Point position, double press, bool rightButton)
        {
            int frame = press > 0.32 ? (rightButton ? 2 : 1) : 0;
            dc.DrawImage(MouseSprites[frame], new Rect(position.X - 20, position.Y - 18, 40, 36));
        }

        public void DrawPaw(DrawingContext dc, Point tip, Rect chest, bool far, double mouseGrip)
        {
            double grip = far ? 0 : mouseGrip;
            double anchorX = chest.X + (far ? 75 : 36);
            Point shoulder = new Point(anchorX + Math.Max(-13, Math.Min(13, (tip.X - anchorX) * 0.30)),
                chest.Y + 63);
            Vector reach = tip - shoulder;
            double angle = Math.Max(-0.58, Math.Min(0.58, -Math.Atan2(reach.X, reach.Y)));
            angle += (0.52 - angle) * grip;
            Point joint = tip - new Vector(-Math.Sin(angle), Math.Cos(angle)) * 6.8;
            Vector axis = joint - shoulder;
            double length = axis.Length;
            if (length > 1)
            {
                Vector normal = new Vector(axis.Y / length, -axis.X / length);
                Point bend = shoulder + axis * 0.55 + new Vector(far ? 2.6 : -3.2, -2);
                var arm = new StreamGeometry();
                using (StreamGeometryContext path = arm.Open())
                {
                    path.BeginFigure(shoulder - normal * 6.2, true, true);
                    path.QuadraticBezierTo(bend - normal * 5.5, joint - normal * 4.5, true, true);
                    path.LineTo(joint + normal * 4.5, true, true);
                    path.QuadraticBezierTo(bend + normal * 5.5, shoulder + normal * 6.2, true, true);
                }
                dc.DrawGeometry(fur, LimbOutline, arm);
            }
            dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
            dc.PushTransform(new RotateTransform(angle * 180 / Math.PI));
            if (far) dc.PushTransform(new ScaleTransform(-1, 1));
            dc.DrawImage(paw, new Rect(-8.5 + 1.1 * grip, -15 + 2 * grip, 17 - 2.2 * grip, 15.4 - 2 * grip));
            if (far) dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

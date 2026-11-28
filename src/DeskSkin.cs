using System;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal sealed class DeskSkin
    {
        public static readonly DeskSkin[] All = {
            new DeskSkin(
                "meadow", "牧草奶酪",
                "pixel-keyboard.png", "pixel-mouse.png",
                Rgb(195, 218, 197), Rgb(255, 242, 218), Rgb(243, 194, 94), Rgb(98, 71, 55),
                Rgb(255, 243, 216), Rgb(241, 180, 151), Rgb(98, 71, 55),
                Rgb(241, 180, 151)),
            new DeskSkin(
                "peach", "蜜桃奶盖",
                "pixel-keyboard-peach.png", "pixel-mouse-peach.png",
                Rgb(245, 205, 212), Rgb(255, 246, 248), Rgb(238, 125, 153), Rgb(104, 58, 66),
                Rgb(255, 245, 248), Rgb(244, 158, 179), Rgb(104, 58, 66),
                Rgb(244, 140, 164)),
            new DeskSkin(
                "mocha", "可可黑金",
                "pixel-keyboard-mocha.png", "pixel-mouse-mocha.png",
                Rgb(79, 63, 57), Rgb(102, 83, 75), Rgb(224, 166, 75), Rgb(33, 23, 20),
                Rgb(74, 59, 53), Rgb(224, 166, 75), Rgb(245, 228, 198),
                Rgb(224, 166, 75))
        };

        public static DeskSkin Default { get { return All[0]; } }

        public readonly string Id;
        public readonly string Name;
        public readonly string PixelKeyboardResource;
        public readonly string PixelMouseResource;
        public readonly Color SwatchShell;
        public readonly Color SwatchKey;
        public readonly Color SwatchAccent;
        public readonly Color SwatchInk;
        public readonly Brush PixelBubblePaper;
        public readonly Brush PixelBubbleAccent;
        public readonly Color PixelBubbleInkColor;
        public readonly Brush PixelBubbleInk;
        public readonly Brush PixelBubbleLine;
        public readonly Brush MouseClickGlow;

        private DeskSkin(
            string id, string name,
            string pixelKb, string pixelMouse,
            Color swatchShell, Color swatchKey, Color swatchAccent, Color swatchInk,
            Color pxPaper, Color pxAccent, Color pxInk,
            Color mouseGlow)
        {
            Id = id;
            Name = name;
            PixelKeyboardResource = pixelKb;
            PixelMouseResource = pixelMouse;
            SwatchShell = swatchShell;
            SwatchKey = swatchKey;
            SwatchAccent = swatchAccent;
            SwatchInk = swatchInk;
            PixelBubblePaper = FreezeBrush(pxPaper);
            PixelBubbleAccent = FreezeBrush(pxAccent);
            PixelBubbleInkColor = pxInk;
            PixelBubbleInk = FreezeBrush(pxInk);
            PixelBubbleLine = FreezeBrush(id == "mocha" ? Rgb(33, 23, 20) : pxInk);
            MouseClickGlow = FreezeBrush(mouseGlow);
        }

        public static DeskSkin FromId(string id)
        {
            if (!String.IsNullOrEmpty(id))
                foreach (DeskSkin skin in All)
                    if (String.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
            return Default;
        }

        public ImageSource SwatchImage()
        {
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                Pen border = FreezePen(FreezeBrush(SwatchInk), 1.1);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchShell), border, new Rect(0.6, 1.6, 16.8, 10.8), 2.4, 2.4);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(2.4, 3.4, 4.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(7.4, 3.4, 4.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchAccent), null, new Rect(12.4, 3.4, 3.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(3.2, 7.3, 11.6, 3.0), 0.8, 0.8);
            }
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }
        private static SolidColorBrush FreezeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FreezePen(Brush b, double w) { var p = new Pen(b, w); p.Freeze(); return p; }
    }
}

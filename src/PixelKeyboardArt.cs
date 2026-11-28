using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal static class PixelKeyboardArt
    {
        private static readonly Rect KeyboardBounds = new Rect(64, 199, 318, 88);

        private sealed class KeySpec
        {
            public readonly int Scan;
            public readonly string Text;
            public readonly int X, Y, Width, LabelX;
            public readonly Point Center;
            public readonly Rect Bounds;

            public KeySpec(int scan, string text, int x, int y, int width, int labelX, int atlasWidth, int frameHeight)
            {
                const int height = 15;
                Scan = scan;
                Text = text;
                X = x;
                Y = y;
                Width = width;
                LabelX = labelX;
                double sx = KeyboardBounds.Width / atlasWidth;
                double sy = KeyboardBounds.Height / frameHeight;
                Bounds = new Rect(KeyboardBounds.X + x * sx, KeyboardBounds.Y + y * sy, width * sx, height * sy);
                Center = new Point(KeyboardBounds.X + (labelX + 0.5) * sx, KeyboardBounds.Y + (y + 7.5) * sy);
            }
        }

        private sealed class ThemeAtlas
        {
            public readonly BitmapSource Resting;
            public readonly Dictionary<int, BitmapSource> Pressed = new Dictionary<int, BitmapSource>();

            public ThemeAtlas(string resourceName, KeySpec[] specs)
            {
                BitmapSource atlas = PixelPetScene.LoadSprite(resourceName);
                int frameHeight = atlas.PixelHeight / 2;
                Resting = Crop(atlas, new Int32Rect(0, 0, atlas.PixelWidth, frameHeight));
                foreach (KeySpec spec in specs)
                {
                    if (!Pressed.ContainsKey(spec.Scan))
                        Pressed.Add(spec.Scan, Crop(atlas, new Int32Rect(spec.X, frameHeight + spec.Y, spec.Width, 15)));
                }
            }

            private static BitmapSource Crop(BitmapSource atlas, Int32Rect pixels)
            {
                var image = new CroppedBitmap(atlas, pixels);
                image.Freeze();
                return image;
            }
        }

        private static readonly KeySpec[] Specs;
        private static readonly Dictionary<int, KeySpec> Lookup;
        private static readonly Dictionary<string, ThemeAtlas> Themes = new Dictionary<string, ThemeAtlas>();
        private static readonly Dictionary<string, BitmapSource> Mice = new Dictionary<string, BitmapSource>();

        static PixelKeyboardArt()
        {
            BitmapSource baseAtlas = PixelPetScene.Sprite("keyboard.png");
            int w = baseAtlas.PixelWidth;
            int fh = baseAtlas.PixelHeight / 2;
            Specs = CreateSpecs(w, fh);
            Lookup = new Dictionary<int, KeySpec>();
            foreach (KeySpec key in Specs) Lookup.Add(key.Scan, key);
            Lookup.Add(0x11c, Lookup[28]);
        }

        private static KeySpec[] CreateSpecs(int w, int fh)
        {
            return new[] {
                new KeySpec(1, "Esc", 324, 71, 29, 337, w, fh),
                new KeySpec(2, "1", 299, 71, 25, 311, w, fh),
                new KeySpec(3, "2", 273, 71, 26, 285, w, fh),
                new KeySpec(4, "3", 247, 71, 26, 260, w, fh),
                new KeySpec(5, "4", 222, 71, 25, 233, w, fh),
                new KeySpec(6, "5", 196, 71, 26, 208, w, fh),
                new KeySpec(7, "6", 171, 71, 24, 183, w, fh),
                new KeySpec(8, "7", 144, 71, 26, 157, w, fh),
                new KeySpec(9, "8", 119, 71, 25, 131, w, fh),
                new KeySpec(10, "9", 94, 71, 25, 106, w, fh),
                new KeySpec(11, "0", 70, 71, 24, 82, w, fh),
                new KeySpec(12, "-", 46, 71, 24, 58, w, fh),
                new KeySpec(14, "⌫", 14, 71, 32, 31, w, fh),

                new KeySpec(15, "Tab", 314, 55, 37, 332, w, fh),
                new KeySpec(16, "Q", 289, 55, 25, 300, w, fh),
                new KeySpec(17, "W", 263, 55, 26, 275, w, fh),
                new KeySpec(18, "E", 237, 55, 26, 249, w, fh),
                new KeySpec(19, "R", 211, 55, 26, 223, w, fh),
                new KeySpec(20, "T", 185, 55, 26, 198, w, fh),
                new KeySpec(21, "Y", 158, 55, 26, 171, w, fh),
                new KeySpec(22, "U", 132, 55, 26, 145, w, fh),
                new KeySpec(23, "I", 107, 55, 25, 119, w, fh),
                new KeySpec(24, "O", 81, 55, 26, 94, w, fh),
                new KeySpec(25, "P", 55, 55, 26, 68, w, fh),
                new KeySpec(43, "\\", 16, 55, 39, 37, w, fh),

                new KeySpec(58, "Caps", 305, 39, 44, 325, w, fh),
                new KeySpec(30, "A", 279, 39, 26, 290, w, fh),
                new KeySpec(31, "S", 253, 39, 26, 265, w, fh),
                new KeySpec(32, "D", 227, 39, 26, 238, w, fh),
                new KeySpec(33, "F", 202, 39, 25, 213, w, fh),
                new KeySpec(34, "G", 177, 39, 25, 189, w, fh),
                new KeySpec(35, "H", 149, 39, 27, 162, w, fh),
                new KeySpec(36, "J", 124, 39, 25, 136, w, fh),
                new KeySpec(37, "K", 98, 39, 26, 111, w, fh),
                new KeySpec(38, "L", 72, 39, 26, 86, w, fh),
                new KeySpec(28, "Enter", 18, 39, 54, 46, w, fh),

                new KeySpec(42, "Shift", 294, 23, 53, 318, w, fh),
                new KeySpec(44, "Z", 265, 23, 29, 277, w, fh),
                new KeySpec(45, "X", 240, 23, 25, 251, w, fh),
                new KeySpec(46, "C", 215, 23, 25, 227, w, fh),
                new KeySpec(47, "V", 189, 23, 26, 201, w, fh),
                new KeySpec(48, "B", 163, 23, 25, 176, w, fh),
                new KeySpec(49, "N", 138, 23, 25, 150, w, fh),
                new KeySpec(50, "M", 112, 23, 26, 126, w, fh),
                new KeySpec(51, ",", 86, 23, 26, 99, w, fh),
                new KeySpec(52, ".", 61, 23, 25, 73, w, fh),
                new KeySpec(54, "Shift", 20, 23, 41, 42, w, fh),

                new KeySpec(29, "Ctrl", 287, 7, 30, 302, w, fh),
                new KeySpec(0x15b, "Win", 261, 7, 25, 274, w, fh),
                new KeySpec(56, "Alt", 235, 7, 25, 248, w, fh),
                new KeySpec(57, "", 104, 7, 131, 169, w, fh),
                new KeySpec(0x138, "Alt", 79, 7, 25, 91, w, fh),
                new KeySpec(0x15d, "Menu", 52, 7, 26, 65, w, fh),
                new KeySpec(0x11d, "Ctrl", 22, 7, 29, 36, w, fh)
            };
        }

        private static ThemeAtlas GetAtlas(DeskSkin desk)
        {
            desk = desk ?? DeskSkin.Default;
            ThemeAtlas atlas;
            if (!Themes.TryGetValue(desk.Id, out atlas))
            {
                atlas = new ThemeAtlas(desk.PixelKeyboardResource, Specs);
                Themes[desk.Id] = atlas;
            }
            return atlas;
        }

        public static BitmapSource MouseSprite(DeskSkin desk)
        {
            desk = desk ?? DeskSkin.Default;
            BitmapSource mouse;
            if (!Mice.TryGetValue(desk.Id, out mouse))
            {
                mouse = PixelPetScene.LoadSprite(desk.PixelMouseResource);
                Mice[desk.Id] = mouse;
            }
            return mouse;
        }

        private static KeySpec FindKey(int scan, bool right)
        {
            switch (scan)
            {
                case 13: scan = 12; break;
                case 26:
                case 27: scan = 25; break;
                case 39:
                case 40: scan = 38; break;
                case 41: scan = 1; break;
                case 53: scan = 52; break;
                case 0x15c: scan = 0x138; break;
            }
            KeySpec key;
            return scan != 0 && Lookup.TryGetValue(scan, out key) ? key : Lookup[right ? 36 : 33];
        }

        public static bool IsRight(int scan) { return KeyboardArt.IsRight(scan); }
        public static string Label(int scan) { return KeyboardArt.Label(scan); }
        public static Point Target(int scan, bool right) { return FindKey(scan, right).Center; }

        public static void Draw(DrawingContext dc, int leftScan, double leftAmount, int rightScan, double rightAmount)
        {
            Draw(dc, DeskSkin.Default, leftScan, leftAmount, rightScan, rightAmount);
        }

        public static void Draw(DrawingContext dc, DeskSkin desk, int leftScan, double leftAmount, int rightScan, double rightAmount)
        {
            ThemeAtlas atlas = GetAtlas(desk);
            dc.DrawImage(atlas.Resting, KeyboardBounds);
            KeySpec leftKey = FindKey(leftScan, false);
            KeySpec rightKey = FindKey(rightScan, true);
            if (leftAmount > 0.25) dc.DrawImage(atlas.Pressed[leftKey.Scan], leftKey.Bounds);
            if (rightAmount > 0.25 && (rightKey != leftKey || leftAmount <= 0.25))
                dc.DrawImage(atlas.Pressed[rightKey.Scan], rightKey.Bounds);
        }

        public static void DrawPressed(DrawingContext dc, DeskSkin desk, int scan, double amount, bool right)
        {
            if (amount <= 0.25) return;
            ThemeAtlas atlas = GetAtlas(desk);
            KeySpec key = FindKey(scan, right);
            dc.DrawImage(atlas.Pressed[key.Scan], key.Bounds);
        }
    }
}

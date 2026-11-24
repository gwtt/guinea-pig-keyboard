using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal static class KeyboardArt
    {
        private static readonly Rect KeyboardBounds = new Rect(86, 197, 279.84, 86.24);

        private sealed class Key
        {
            public int Scan;
            public string Text;
            public bool Right;
            public Point Center;
            public double Width;
            public Key(int scan, string text, bool right, Point center, double width)
            {
                Scan = scan;
                Text = text;
                Right = right;
                Center = center;
                Width = width;
            }
        }

        private static readonly Brush PressGlow = BrushFrom(255, 185, 74, 80);
        private static readonly Brush PressEdge = BrushFrom(125, 75, 32, 120);
        private static readonly Pen PressLine = NewPen(PressEdge, 0.9);
        private static readonly Key[] Keys = CreateKeys();
        private static readonly Dictionary<int, Key> Lookup = IndexKeys();
        private static readonly DrawingGroup Artwork = CreateArtwork();

        private static Brush BrushFrom(byte r, byte g, byte b, byte a)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
        private static Pen NewPen(Brush brush, double width)
        {
            Pen pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }
        private static Key[] CreateKeys()
        {
            List<Key> keys = new List<Key>();
            Stream data = Assembly.GetExecutingAssembly().GetManifestResourceStream("keyboard-layout.txt");
            if (data == null) throw new InvalidDataException("缺少 keyboard-layout.txt 资源。");
            using (data)
            using (StreamReader reader = new StreamReader(data))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] fields = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length != 7) throw new InvalidDataException("keyboard-layout.txt 第 " + lineNumber + " 行列数无效。");
                    try
                    {
                        int scan = int.Parse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                        bool right;
                        if (fields[3] == "R") right = true;
                        else if (fields[3] == "L") right = false;
                        else throw new FormatException("手别必须是 L 或 R。");
                        double x = double.Parse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture);
                        double y = double.Parse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture);
                        double width = double.Parse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (width <= 0) throw new FormatException("键宽必须大于零。");
                        keys.Add(new Key(scan, fields[2], right, new Point(x, y), width));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("keyboard-layout.txt 第 " + lineNumber + " 行格式无效。", ex);
                    }
                }
            }
            if (keys.Count == 0) throw new InvalidDataException("keyboard-layout.txt 中没有键位数据。");
            return keys.ToArray();
        }
        private static Dictionary<int, Key> IndexKeys()
        {
            Dictionary<int, Key> keys = new Dictionary<int, Key>();
            foreach (Key key in Keys) keys.Add(key.Scan, key);
            keys.Add(0x11c, keys[28]);
            return keys;
        }
        private static DrawingGroup CreateArtwork()
        {
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                ImageSource bitmap = PixelPetScene.LoadSprite("keyboard.png");
                dc.DrawImage(bitmap, KeyboardBounds);
            }
            group.Freeze();
            return group;
        }
        private static void DrawPressed(DrawingContext dc, Key key, double amount)
        {
            double width = Math.Max(0, key.Width - 1.5);
            double height = 7.3;
            Rect face = new Rect(key.Center.X - width / 2, key.Center.Y - height / 2 + amount * 1.2, width, height);
            dc.PushTransform(new RotateTransform(-3, key.Center.X, key.Center.Y));
            dc.PushOpacity(amount);
            dc.DrawRoundedRectangle(PressGlow, PressLine, face, 2.5, 2.5);
            dc.Pop();
            dc.Pop();
        }
        private static Key FindKey(int scan, bool right)
        {
            Key key;
            return scan != 0 && Lookup.TryGetValue(scan, out key) ? key : Lookup[right ? 36 : 33];
        }
        private static double Clamp(double amount)
        {
            return double.IsNaN(amount) ? 0 : Math.Max(0, Math.Min(1, amount));
        }
        public static void Draw(DrawingContext dc, int leftScan, double leftAmount, int rightScan, double rightAmount)
        {
            if (dc == null) throw new ArgumentNullException("dc");
            dc.DrawDrawing(Artwork);
            double left = Clamp(leftAmount);
            double right = Clamp(rightAmount);
            Key leftKey = FindKey(leftScan, false);
            Key rightKey = FindKey(rightScan, true);
            if (left > 0) DrawPressed(dc, leftKey, leftKey == rightKey ? Math.Max(left, right) : left);
            if (right > 0 && (rightKey != leftKey || left <= 0)) DrawPressed(dc, rightKey, right);
        }
        public static bool IsRight(int scan)
        {
            Key key;
            if (scan != 0 && Lookup.TryGetValue(scan, out key)) return key.Right;
            int make = scan & 0xff;
            if (make >= 0x47 && make <= 0x53) return true;
            switch (scan)
            {
                case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                case 21: case 22: case 23: case 24: case 25: case 26: case 27: case 43:
                case 35: case 36: case 37: case 38: case 39: case 40: case 28:
                case 49: case 50: case 51: case 52: case 53: case 54:
                case 0x37: case 0x45: case 0x46: case 0x135: case 0x137:
                case 0x11c: case 0x11d: case 0x138: case 0x15c: case 0x15d:
                    return true;
                default: return false;
            }
        }
        public static string Label(int scan)
        {
            Key key;
            if (scan != 0 && Lookup.TryGetValue(scan, out key)) return key.Text;
            if (scan >= 0x3b && scan <= 0x44) return "F" + (scan - 0x3a);
            switch (scan)
            {
                case 0x57: return "F11";
                case 0x58: return "F12";
                case 0x147: return "Home";
                case 0x148: return "↑";
                case 0x149: return "PgUp";
                case 0x14b: return "←";
                case 0x14d: return "→";
                case 0x14f: return "End";
                case 0x150: return "↓";
                case 0x151: return "PgDn";
                case 0x152: return "Ins";
                case 0x153: return "Del";
                case 0x11c: return "Enter";
            }
            return "";
        }
        public static Point Target(int scan, bool right)
        {
            return FindKey(scan, right).Center;
        }
    }
}

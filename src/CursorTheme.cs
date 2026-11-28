using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WheekKeys
{
    internal sealed class CursorTheme
    {
        private const uint SpiSetCursors = 0x0057;
        private const uint PersonCursorId = 32672;
        private const string CursorsRegPath = @"Control Panel\Cursors";

        private static readonly Dictionary<string, string> SystemRoles = new Dictionary<string, string>
        {
            { "Arrow", "arrow" }, { "Hand", "hand" }, { "IBeam", "ibeam" },
            { "Wait", "busy" }, { "AppStarting", "working" },
            { "Help", "help" }, { "Crosshair", "precision" }, { "NWPen", "pen" },
            { "No", "unavailable" }, { "UpArrow", "alternate" }, { "SizeAll", "move" },
            { "Pin", "location" }, { "Person", "person" },
            { "SizeNS", "resize-ns" }, { "SizeWE", "resize-we" },
            { "SizeNWSE", "resize-nwse" }, { "SizeNESW", "resize-nesw" }
        };

        private static readonly string[] LegacyDefaultRoles = { "Arrow", "Hand", "IBeam", "Wait", "AppStarting" };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursorFromFile(string fileName);

        [DllImport("user32.dll")]
        private static extern bool SetSystemCursor(IntPtr cursor, uint id);

        [DllImport("user32.dll")]
        private static extern bool DestroyCursor(IntPtr cursor);

        internal struct RoleSpec
        {
            internal readonly string Role;
            internal readonly int HotspotX64;
            internal readonly int HotspotY64;

            internal RoleSpec(string role, int hx64, int hy64)
            {
                Role = role;
                HotspotX64 = hx64;
                HotspotY64 = hy64;
            }
        }

        internal static readonly CursorTheme SystemDefault = new CursorTheme(
            "system", "系统默认", "恢复系统原生指针",
            new Color[] { Color.FromArgb(245, 245, 245), Color.FromArgb(80, 80, 80), Color.FromArgb(190, 190, 190) },
            false, new RoleSpec[0]);

        internal static readonly CursorTheme[] All = new CursorTheme[]
        {
            SystemDefault,
            new CursorTheme(
                "tricolor", "奶油三花", "极简办公 · 锐利低遮挡",
                new Color[] { Color.FromArgb(255, 246, 232), Color.FromArgb(227, 168, 100), Color.FromArgb(107, 91, 83) },
                false,
                CreateRoles(6, 4, 24, 6)),
            new CursorTheme(
                "carrot", "牧草胡萝卜", "清爽食物 · 天然尖头",
                new Color[] { Color.FromArgb(240, 138, 56), Color.FromArgb(127, 176, 105), Color.FromArgb(255, 246, 232) },
                false,
                CreateRoles(6, 6, 20, 8)),
            new CursorTheme(
                "pixel", "复古像素猪", "桌宠同款 · 锐利像素风",
                new Color[] { Color.FromArgb(255, 246, 232), Color.FromArgb(242, 182, 160), Color.FromArgb(62, 39, 22) },
                true,
                CreateRoles(4, 4, 22, 10)),
            new CursorTheme(
                "piggy-pixel", "三花猪猪", "像素三花 · 手绘指针",
                new Color[] { Color.FromArgb(255, 243, 218), Color.FromArgb(244, 162, 167), Color.FromArgb(85, 46, 35) },
                true,
                new RoleSpec[]
                {
                    new RoleSpec("arrow", 4, 4), new RoleSpec("help", 4, 4),
                    new RoleSpec("working", 4, 4), new RoleSpec("busy", 50, 28),
                    new RoleSpec("ibeam", 20, 32), new RoleSpec("precision", 32, 32),
                    new RoleSpec("pen", 6, 56), new RoleSpec("unavailable", 48, 16),
                    new RoleSpec("hand", 2, 14), new RoleSpec("alternate", 32, 2),
                    new RoleSpec("location", 32, 60), new RoleSpec("person", 30, 14),
                    new RoleSpec("move", 32, 32), new RoleSpec("resize-we", 32, 32),
                    new RoleSpec("resize-ns", 32, 32), new RoleSpec("resize-nwse", 32, 32),
                    new RoleSpec("resize-nesw", 32, 32)
                })
        };

        private static RoleSpec[] CreateRoles(int arrowX, int arrowY, int handX, int handY)
        {
            return new RoleSpec[]
            {
                new RoleSpec("arrow", arrowX, arrowY), new RoleSpec("hand", handX, handY),
                new RoleSpec("ibeam", 32, 32), new RoleSpec("busy", 32, 32),
                new RoleSpec("resize-ns", 32, 32), new RoleSpec("resize-we", 32, 32),
                new RoleSpec("resize-nwse", 32, 32), new RoleSpec("resize-nesw", 32, 32)
            };
        }

        internal readonly string Id;
        internal readonly string Name;
        internal readonly string Description;
        internal readonly Color[] Swatch;
        internal readonly bool PixelArt;
        internal readonly RoleSpec[] Roles;

        private CursorTheme(string id, string name, string description, Color[] swatch, bool pixelArt, RoleSpec[] roles)
        {
            Id = id;
            Name = name;
            Description = description;
            Swatch = swatch;
            PixelArt = pixelArt;
            Roles = roles;
        }

        internal bool IsSystem { get { return Id == SystemDefault.Id; } }

        internal static CursorTheme FromId(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (CursorTheme theme in All)
                {
                    if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) return theme;
                }
            }
            return SystemDefault;
        }

        internal static string Apply(CursorTheme theme, string dataDirectory)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorsRegPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户指针配置项。");
                return ApplyTheme(theme, dataDirectory, key, personChanged => RefreshSystemCursors(key, personChanged));
            }
        }

        private static string ApplyTheme(CursorTheme theme, string dataDirectory, RegistryKey key, Action<bool> refresh)
        {
            if (theme == null) theme = SystemDefault;
            string backupPath = Path.Combine(dataDirectory, "cursor-backup.txt");
            if (theme.IsSystem)
            {
                Dictionary<string, string> saved = ReadBackup(backupPath);
                // Older backups may omit roles that were never replaced.
                foreach (string name in LegacyDefaultRoles)
                    if (!saved.ContainsKey(name)) saved.Add(name, "");
                WriteSystemCursors(key, saved, refresh);
                try { if (File.Exists(backupPath)) File.Delete(backupPath); }
                catch { }
                return "已恢复系统默认指针";
            }

            Directory.CreateDirectory(dataDirectory);
            string cursorsRoot = Path.Combine(dataDirectory, "cursors");
            string themeDir = Path.Combine(cursorsRoot, theme.Id);
            Directory.CreateDirectory(themeDir);

            Dictionary<string, string> writtenFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (RoleSpec spec in theme.Roles)
            {
                string resourceName = "cursor-" + theme.Id + "-" + spec.Role + ".png";
                byte[] png64 = ReadResourceBytes(resourceName);
                if (png64 == null) throw new InvalidOperationException("缺少内置指针素材：" + resourceName);
                byte[] png32 = ResizePng(png64, 32, 32, theme.PixelArt);
                byte[] curBytes = BuildMultiResCur(
                    png32, 32, 32, Math.Max(0, spec.HotspotX64 / 2), Math.Max(0, spec.HotspotY64 / 2),
                    png64, 64, 64, Math.Max(0, spec.HotspotX64), Math.Max(0, spec.HotspotY64));
                string curPath = Path.Combine(themeDir, spec.Role + ".cur");
                File.WriteAllBytes(curPath, curBytes);
                writtenFiles[spec.Role] = curPath;
            }

            // Older themes use their busy artwork for background work as well.
            if (!writtenFiles.ContainsKey("working")) writtenFiles.Add("working", writtenFiles["busy"]);

            Dictionary<string, string> changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> role in SystemRoles)
            {
                string file;
                if (writtenFiles.TryGetValue(role.Value, out file)) changes.Add(role.Key, file);
            }
            Dictionary<string, string> backup = EnsureBackup(key, backupPath, cursorsRoot, changes.Keys);
            foreach (KeyValuePair<string, string> original in backup)
            {
                if (!changes.ContainsKey(original.Key) && IsWheekCursorPath(ReadCursorPath(key, original.Key), cursorsRoot))
                    changes.Add(original.Key, original.Value);
            }
            WriteSystemCursors(key, changes, refresh);

            return "已应用「" + theme.Name + "」· 可随时切回系统默认";
        }

        private static Dictionary<string, string> ReadBackup(string backupPath)
        {
            Dictionary<string, string> saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(backupPath)) return saved;
            foreach (string line in File.ReadAllLines(backupPath))
            {
                int sep = line.IndexOf('=');
                string name = sep < 0 ? line : line.Substring(0, sep);
                if (SystemRoles.ContainsKey(name))
                    saved[name] = sep < 0 ? null : line.Substring(sep + 1);
            }
            return saved;
        }

        private static Dictionary<string, string> EnsureBackup(
            RegistryKey key, string backupPath, string cursorsRoot, IEnumerable<string> names)
        {
            Dictionary<string, string> saved = ReadBackup(backupPath);
            bool changed = false;
            foreach (string name in names)
            {
                if (saved.ContainsKey(name)) continue;
                string raw = ReadCursorPath(key, name);
                saved.Add(name, IsWheekCursorPath(raw, cursorsRoot) ? "" : raw);
                changed = true;
            }
            if (!changed) return saved;
            List<string> lines = new List<string>();
            // A bare name records that the registry value originally did not exist.
            foreach (KeyValuePair<string, string> pair in saved)
                lines.Add(pair.Value == null ? pair.Key : pair.Key + "=" + pair.Value);
            string pending = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(pending, lines.ToArray());
                if (File.Exists(backupPath)) File.Replace(pending, backupPath, null);
                else File.Move(pending, backupPath);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return saved;
        }

        private static string ReadCursorPath(RegistryKey key, string name)
        {
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        private static bool IsWheekCursorPath(string file, string root)
        {
            return file != null && file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteCursorPath(RegistryKey key, string name, string file)
        {
            if (file == null) key.DeleteValue(name, false);
            else key.SetValue(name, file, RegistryValueKind.ExpandString);
        }

        private static void WriteSystemCursors(RegistryKey key, Dictionary<string, string> changes, Action<bool> refresh)
        {
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (KeyValuePair<string, string> change in changes)
                {
                    previous.Add(change.Key, ReadCursorPath(key, change.Key));
                    WriteCursorPath(key, change.Key, change.Value);
                }
                refresh(changes.ContainsKey("Person"));
            }
            catch
            {
                foreach (KeyValuePair<string, string> original in previous)
                    WriteCursorPath(key, original.Key, original.Value);
                try { refresh(previous.ContainsKey("Person")); }
                catch { }
                throw;
            }
        }

        private static void RefreshSystemCursors(RegistryKey key, bool personChanged)
        {
            // The cursor paths have already been saved to the registry.
            if (!SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0))
                throw new InvalidOperationException("Windows 无法刷新系统指针。");

            // Some Windows versions skip Person when reloading the scheme.
            if (!personChanged) return;
            string cursorDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors");
            string path = key.GetValue("Person") as string;
            IntPtr cursor = string.IsNullOrEmpty(path)
                ? IntPtr.Zero
                : LoadCursorFromFile(Path.Combine(cursorDirectory, path));
            if (cursor == IntPtr.Zero)
                cursor = LoadCursorFromFile(Path.Combine(cursorDirectory, "aero_person.cur"));
            if (cursor != IntPtr.Zero && !SetSystemCursor(cursor, PersonCursorId))
                DestroyCursor(cursor);
        }

        private static byte[] ReadResourceBytes(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static byte[] ResizePng(byte[] pngBytes, int width, int height, bool pixelArt)
        {
            using (MemoryStream srcStream = new MemoryStream(pngBytes))
            using (Bitmap src = new Bitmap(srcStream))
            using (Bitmap dst = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = pixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = pixelArt ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                g.SmoothingMode = pixelArt ? SmoothingMode.None : SmoothingMode.AntiAlias;
                g.DrawImage(src, new Rectangle(0, 0, width, height));
                using (MemoryStream outStream = new MemoryStream())
                {
                    dst.Save(outStream, ImageFormat.Png);
                    return outStream.ToArray();
                }
            }
        }

        internal static byte[] BuildMultiResCur(
            byte[] png1, int w1, int h1, int hx1, int hy1,
            byte[] png2, int w2, int h2, int hx2, int hy2)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // ICONDIR header: reserved=0, type=2 (CUR), count=2
                bw.Write((ushort)0);
                bw.Write((ushort)2);
                bw.Write((ushort)2);

                int headerAndEntries = 6 + 16 * 2;
                int offset1 = headerAndEntries;
                int offset2 = offset1 + png1.Length;

                WriteCurDirEntry(bw, w1, h1, hx1, hy1, png1.Length, offset1);
                WriteCurDirEntry(bw, w2, h2, hx2, hy2, png2.Length, offset2);

                bw.Write(png1);
                bw.Write(png2);
                bw.Flush();
                return ms.ToArray();
            }
        }

        private static void WriteCurDirEntry(BinaryWriter bw, int width, int height, int hx, int hy, int byteLength, int offset)
        {
            bw.Write((byte)(width >= 256 ? 0 : width));
            bw.Write((byte)(height >= 256 ? 0 : height));
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((ushort)hx);
            bw.Write((ushort)hy);
            bw.Write((uint)byteLength);
            bw.Write((uint)offset);
        }
    }
}

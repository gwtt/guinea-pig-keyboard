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
        private const uint SpifUpdateIniFile = 0x01;
        private const uint SpifSendChange = 0x02;
        private const string CursorsRegPath = @"Control Panel\Cursors";

        private static readonly Dictionary<string, string> SystemRoles = new Dictionary<string, string>
        {
            { "Arrow", "arrow" }, { "Hand", "hand" }, { "IBeam", "ibeam" },
            { "Wait", "busy" }, { "AppStarting", "busy" },
            { "SizeNS", "resize-ns" }, { "SizeWE", "resize-we" },
            { "SizeNWSE", "resize-nwse" }, { "SizeNESW", "resize-nesw" }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

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
                CreateRoles(4, 4, 22, 10))
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
            if (theme == null) theme = SystemDefault;
            string backupPath = Path.Combine(dataDirectory, "cursor-backup.txt");
            if (theme.IsSystem)
            {
                RestoreFromBackup(backupPath);
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

            EnsureBackup(backupPath, cursorsRoot);

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorsRegPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户指针配置项。");
                foreach (KeyValuePair<string, string> role in SystemRoles)
                    key.SetValue(role.Key, writtenFiles[role.Value], RegistryValueKind.ExpandString);
            }

            RefreshSystemCursors();
            return "已应用「" + theme.Name + "」· 可随时切回系统默认";
        }

        private static Dictionary<string, string> ReadBackup(string backupPath)
        {
            Dictionary<string, string> saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(backupPath)) return saved;
            foreach (string line in File.ReadAllLines(backupPath))
            {
                int sep = line.IndexOf('=');
                if (sep > 0) saved[line.Substring(0, sep)] = line.Substring(sep + 1);
            }
            return saved;
        }

        private static void EnsureBackup(string backupPath, string cursorsRoot)
        {
            Dictionary<string, string> saved = ReadBackup(backupPath);
            bool changed = false;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CursorsRegPath, false))
            {
                foreach (string name in SystemRoles.Keys)
                {
                    if (saved.ContainsKey(name)) continue;
                    string raw = key == null ? "" : (key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "");
                    if (raw.IndexOf(cursorsRoot, StringComparison.OrdinalIgnoreCase) >= 0) raw = "";
                    saved.Add(name, raw);
                    changed = true;
                }
            }
            if (!changed) return;
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in saved) lines.Add(pair.Key + "=" + pair.Value);
            string pending = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(pending, lines.ToArray());
                if (File.Exists(backupPath)) File.Replace(pending, backupPath, null);
                else File.Move(pending, backupPath);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        private static void RestoreFromBackup(string backupPath)
        {
            Dictionary<string, string> saved = ReadBackup(backupPath);

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorsRegPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户指针配置项。");
                foreach (string name in SystemRoles.Keys)
                {
                    string value;
                    // Older versions only backed up the resize cursors they could replace.
                    if (!saved.TryGetValue(name, out value) && name.StartsWith("Size", StringComparison.Ordinal)) continue;
                    key.SetValue(name, value ?? "", RegistryValueKind.ExpandString);
                }
            }

            try { if (File.Exists(backupPath)) File.Delete(backupPath); }
            catch { }

            RefreshSystemCursors();
        }

        private static void RefreshSystemCursors()
        {
            SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);
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

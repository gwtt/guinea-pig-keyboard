using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal static class WheekPackLoader
    {
        private const int FileLimit = 4 * 1024 * 1024, TotalLimit = 12 * 1024 * 1024;
        private const int ArchiveLimit = TotalLimit + 1024 * 1024, ManifestLimit = 64 * 1024, ImageSideLimit = 2048;

        internal static void LoadInstalled(string directory)
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*.wheek"))
                {
                    try
                    {
                        PetSkin skin = Read(ReadFile(path));
                        if (Path.GetFileName(path) == skin.Id.Substring("wheek-".Length) + ".wheek") PetSkin.Register(skin);
                    }
                    catch (Exception e) { if (!IsPackError(e)) throw; }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static PetSkin Install(string path, string directory)
        {
            byte[] contents = ReadFile(path);
            PetSkin skin = Read(contents);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, skin.Id.Substring("wheek-".Length) + ".wheek");
            string pending = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(pending, contents);
                if (File.Exists(destination)) File.Replace(pending, destination, null);
                else File.Move(pending, destination);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            PetSkin.Register(skin);
            return skin;
        }

        internal static bool Uninstall(string id, string directory)
        {
            PetSkin skin = PetSkin.Find(id);
            if (skin == null || !skin.IsCustom || skin.Id != id ||
                !Regex.IsMatch(id, @"\Awheek-[a-z0-9-]+\z")) return false;
            string path = Path.Combine(directory, id.Substring("wheek-".Length) + ".wheek");
            File.Delete(path);
            return PetSkin.Unregister(id);
        }

        internal static bool IsPackError(Exception e)
        {
            return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException ||
                e is InvalidOperationException || e is NotSupportedException || e is FormatException ||
                e is System.Runtime.InteropServices.COMException || e is System.Security.SecurityException;
        }

        internal static void EnableDrop(UIElement target, Action<string> install)
        {
            target.AllowDrop = true;
            target.PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = DropPath(e.Data) == null ? DragDropEffects.None : e.AllowedEffects & DragDropEffects.Copy;
                e.Handled = true;
            };
            target.PreviewDrop += delegate(object sender, DragEventArgs e)
            {
                string path = DropPath(e.Data);
                e.Effects = path == null ? DragDropEffects.None : e.AllowedEffects & DragDropEffects.Copy;
                if (e.Effects == DragDropEffects.Copy) install(path);
                e.Handled = true;
            };
        }

        private static string DropPath(IDataObject data)
        {
            string[] paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Length == 1 && IsPackPath(paths[0]) ? paths[0] : null;
        }

        private static bool IsPackPath(string path)
        {
            return path != null && path.EndsWith(".wheek", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] ReadFile(string path)
        {
            if (!IsPackPath(path)) throw new InvalidDataException("请选择 .wheek 外观包。");
            using (Stream file = File.OpenRead(path)) return ReadLimited(file, ArchiveLimit);
        }

        private static PetSkin Read(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (zip.Entries.Count > 128) throw new InvalidDataException("外观包内的文件过多。");
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.Length < 0 || entry.Length > FileLimit || (total += entry.Length) > TotalLimit)
                        throw new InvalidDataException("外观包单文件不得超过 4 MB，总解压大小不得超过 12 MB。");
                    if (entries.ContainsKey(entry.FullName)) throw new InvalidDataException("外观包内有重名文件。");
                    entries.Add(entry.FullName, entry);
                }
                byte[] manifest = ReadEntry(entries, "manifest.json", ManifestLimit);
                if (manifest == null) throw new InvalidDataException("外观包缺少 manifest.json。");
                string json = new UTF8Encoding(false, true).GetString(manifest).TrimStart('\uFEFF');
                var fields = new JavaScriptSerializer { MaxJsonLength = ManifestLimit, RecursionLimit = 8 }
                    .DeserializeObject(json) as Dictionary<string, object>;
                if (fields == null) throw new InvalidDataException("外观包的 manifest.json 格式无效。");
                string id = Field(fields, "id", 64);
                string name = Field(fields, "name", 64);
                if (id == null || !Regex.IsMatch(id, @"\A[a-zA-Z0-9-]+\z") || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("外观包需要合法的 id 和非空的 name。");
                string mode = Field(fields, "mode", 8) ?? "pixel";
                if (mode != "pixel") throw new InvalidDataException("仅支持像素外观包。");
                PetSkin skin = PetSkin.CreateCustom(id.ToLowerInvariant(), name.Trim(), Field(fields, "baseCoat", 64));
                skin.Author = Field(fields, "author", 80);
                skin.CustomFurColor = ReadColor(fields, "furColor");
                skin.CustomInkColor = ReadColor(fields, "inkColor");
                skin.CustomEyeColor = ReadColor(fields, "eyeColor");
                skin.CustomBody = ReadImage(entries, "body.png");
                skin.CustomPaw = ReadImage(entries, "paw.png");
                skin.CustomMouth = ReadImage(entries, "mouth.png");
                return skin;
            }
        }

        private static string Field(Dictionary<string, object> fields, string name, int length)
        {
            object value;
            if (!fields.TryGetValue(name, out value) || value == null) return null;
            string text = value as string;
            if (text == null || text.Length > length || Array.Exists(text.ToCharArray(), char.IsControl))
                throw new InvalidDataException("外观包中的 " + name + " 无效。");
            return text;
        }

        private static Color? ReadColor(Dictionary<string, object> fields, string name)
        {
            string value = Field(fields, name, 7);
            if (value == null) return null;
            uint rgb;
            if (value.Length != 7 || value[0] != '#' ||
                !uint.TryParse(value.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                throw new InvalidDataException("外观包中的 " + name + " 必须使用 #RRGGBB。");
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        private static BitmapImage ReadImage(Dictionary<string, ZipArchiveEntry> entries, string name)
        {
            byte[] bytes = ReadEntry(entries, name, FileLimit);
            if (bytes == null) return null;
            byte[] header = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            if (bytes.Length < 33) throw new InvalidDataException(name + " 不是有效的 PNG。");
            for (int i = 0; i < header.Length; i++)
                if (bytes[i] != header[i]) throw new InvalidDataException(name + " 不是有效的 PNG。");
            for (int offset = 16; offset <= 20; offset += 4)
            {
                int size = bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
                if (size <= 0 || size > ImageSideLimit) throw new InvalidDataException(name + " 的长宽必须在 1 至 2048 像素之间。");
            }
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private static byte[] ReadEntry(Dictionary<string, ZipArchiveEntry> entries, string name, int limit)
        {
            ZipArchiveEntry entry;
            if (!entries.TryGetValue(name, out entry)) return null;
            if (entry.Length > limit) throw new InvalidDataException(name + " 超出大小限制。");
            using (Stream stream = entry.Open())
            {
                byte[] result = ReadLimited(stream, (int)entry.Length);
                if (result.Length != entry.Length) throw new InvalidDataException(name + " 的解压长度不一致。");
                return result;
            }
        }

        private static byte[] ReadLimited(Stream input, int limit)
        {
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + count > limit) throw new InvalidDataException("外观包超出大小限制。");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }
    }
}

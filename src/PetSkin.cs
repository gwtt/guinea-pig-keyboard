using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class PetSkin
    {
        public static readonly PetSkin[] All = {
            new PetSkin("pixel-tricolor", "三花", "tricolor", "tricolor"),
            new PetSkin("pixel-black-white", "黑白", "black-white", "silver-agouti"),
            new PetSkin("pixel-golden-white", "黄白", "golden-white", "gold-white"),
            new PetSkin("pixel-buff", "浅金棕", "white", "gold-white"),
            new PetSkin("pixel-cream", "奶油", "cream", "californian"),
            new PetSkin("pixel-chocolate", "巧克力", "chocolate", "teddy"),
            new PetSkin("pixel-californian", "加州", "californian", "californian"),
            new PetSkin("pixel-longhair-gold-white", "长毛黄白", "longhair-template", "longhair"),
            new PetSkin("pixel-longhair-tricolor", "长毛三花", "longhair-tricolor", "longhair"),
            new PetSkin("pixel-longhair-black-white", "长毛黑白", "longhair-black-white", "longhair"),
            new PetSkin("pixel-longhair-buff", "长毛浅金棕", "longhair-buff", "longhair"),
            new PetSkin("pixel-longhair-cream", "长毛奶油", "longhair-cream", "longhair"),
            new PetSkin("pixel-longhair-chocolate", "长毛巧克力", "longhair-chocolate", "longhair"),
            new PetSkin("pixel-longhair-californian", "长毛加州", "longhair-californian", "longhair"),
            new PetSkin("prone-tricolor", "趴趴三花", "tricolor", "tricolor", true),
            new PetSkin("prone-black-white", "趴趴黑白", "black-white", "silver-agouti", true),
            new PetSkin("prone-golden-white", "趴趴黄白", "golden-white", "gold-white", true),
            new PetSkin("prone-cream", "趴趴奶油", "cream", "californian", true),
            new PetSkin("prone-californian", "趴趴白加州", "californian", "californian", true, "californian"),
            new PetSkin("prone-teddy", "趴趴美泰", "chocolate", "teddy", true, "teddy"),
            new PetSkin("prone-silver-agouti", "趴趴雕灰", "black-white", "silver-agouti", true, "silver-agouti"),
            new PetSkin("prone-longhair", "趴趴长毛猪", "longhair-template", "longhair", true, "longhair")
        };

        private static readonly SortedDictionary<string, PetSkin> custom = new SortedDictionary<string, PetSkin>(StringComparer.Ordinal);
        private static volatile IReadOnlyList<PetSkin> available = Array.AsReadOnly(All);
        public static IReadOnlyList<PetSkin> Available { get { return available; } }

        public readonly string Id;
        public readonly string Name;
        public readonly string PixelCoatId;
        public readonly string NetworkId;
        public readonly string DrawnArtId;
        public readonly bool IsProne;
        public bool IsCustom { get { return BaseSkin != null; } }
        public string Group { get { return IsCustom ? "自定义" : IsProne ? "趴趴猪" : Id.StartsWith("pixel-longhair-", StringComparison.Ordinal) ? "长毛" : "短毛"; } }
        public PetSkin BaseSkin { get; private set; }
        public BitmapImage CustomBody { get; internal set; }
        public BitmapImage CustomPaw { get; internal set; }
        public BitmapImage CustomMouth { get; internal set; }
        public Color? CustomFurColor { get; internal set; }
        public Color? CustomInkColor { get; internal set; }
        public Color? CustomEyeColor { get; internal set; }
        public string Author { get; internal set; }

        private PetSkin(string id, string name, string pixelCoatId, string networkId, bool prone = false, string drawnArtId = null)
        {
            Id = id;
            Name = name;
            PixelCoatId = pixelCoatId;
            NetworkId = networkId;
            DrawnArtId = drawnArtId;
            IsProne = prone;
        }

        internal static PetSkin CreateCustom(string id, string name, string baseCoat)
        {
            PetSkin basis = Find(baseCoat);
            if (basis == null || basis.IsCustom) basis = All[0];
            if (basis.DrawnArtId != null)
                foreach (PetSkin pixel in All)
                    if (!pixel.IsProne && pixel.PixelCoatId == basis.PixelCoatId) { basis = pixel; break; }
            return new PetSkin("wheek-" + id, name, basis.PixelCoatId, basis.NetworkId, basis.IsProne) { BaseSkin = basis };
        }

        internal static void Register(PetSkin skin)
        {
            lock (custom)
            {
                custom[skin.Id] = skin;
                RefreshAvailable();
            }
        }

        internal static bool Unregister(string id)
        {
            lock (custom)
            {
                if (!custom.Remove(id)) return false;
                RefreshAvailable();
                return true;
            }
        }

        private static void RefreshAvailable()
        {
            List<PetSkin> skins = new List<PetSkin>(All);
            skins.AddRange(custom.Values);
            available = skins.AsReadOnly();
        }

        public static PetSkin FromId(string id) { return Find(id) ?? All[0]; }

        public static PetSkin Find(string id)
        {
            string replacement = null;
            switch (id)
            {
                case "drawn-tricolor": case "tricolor": replacement = "pixel-tricolor"; break;
                case "drawn-californian": case "californian": replacement = "pixel-californian"; break;
                case "drawn-gold-white": case "gold-white": replacement = "pixel-golden-white"; break;
                case "drawn-teddy": case "teddy": replacement = "pixel-chocolate"; break;
                case "drawn-silver-agouti": case "silver-agouti": replacement = "pixel-black-white"; break;
                case "drawn-longhair": case "longhair": replacement = "pixel-longhair-gold-white"; break;
                case "black-white": replacement = "pixel-black-white"; break;
                case "golden-white": replacement = "pixel-golden-white"; break;
                case "white": replacement = "pixel-buff"; break;
                case "cream": replacement = "pixel-cream"; break;
                case "chocolate": replacement = "pixel-chocolate"; break;
                case "longhair-template": replacement = "pixel-longhair-gold-white"; break;
                case "longhair-tricolor": replacement = "pixel-longhair-tricolor"; break;
                case "longhair-black-white": replacement = "pixel-longhair-black-white"; break;
                case "longhair-buff": replacement = "pixel-longhair-buff"; break;
                case "longhair-cream": replacement = "pixel-longhair-cream"; break;
                case "longhair-chocolate": replacement = "pixel-longhair-chocolate"; break;
                case "longhair-californian": replacement = "pixel-longhair-californian"; break;
            }
            foreach (PetSkin skin in Available)
                if (skin.Id == (replacement ?? id)) return skin;
            return null;
        }
    }
}

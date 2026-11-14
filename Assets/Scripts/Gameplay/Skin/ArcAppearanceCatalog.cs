using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Arcade.Gameplay
{
    [Serializable]
    public sealed class AppearanceSelection
    {
        public string Track, CriticalLine, SingleLine, Diamond, Combo;
    }

    [Serializable]
    public class AppearanceOption
    {
        public string Id, Name;
        public int Side;
        public override string ToString() => Name;
    }

    public static class ArcAppearanceCatalog
    {
        internal sealed class ImageAsset
        {
            public string File;
            public float PixelsPerUnit = 100;
            public Vector2 Pivot = new Vector2(.5f, .5f);
        }
        internal sealed class Item : AppearanceOption
        {
            public ImageAsset Sprite;
            public Color Color = Color.white;
            public bool IsEmpty;
            public ImageAsset Left, Right;
            public Vector2 Scale = Vector2.one;
            public Vector3 PositionLeft, PositionRight;
            public byte LightOpacity, DarkOpacity;
            public float Length = 50, ScrollSpeed = 1;
            public int SrcBlend = 1, DstBlend = 1;
        }
        internal sealed class NoteSet
        {
            public ImageAsset Tap, Hold, Highlight, ArcTap;
        }
        internal sealed class Particle
        {
            public string Name;
            public Color Start, End;
            public float Scale;
        }
        internal sealed class Diamond : AppearanceOption
        {
            public float MultiplyScale = 1, PivotY = .5f;
            public bool CanvasRectUseDesignResolutionSize, UseInternalBlendFactorSettings;
            public int MinOpacity = 50, MaxOpacity = 100;
            public Anchor AnchorSettings = new Anchor();
            public Blend BlendFactorSettings = new Blend();
        }
        internal sealed class Anchor { public int AnchorType; public float AnchorPositionX, AnchorPositionY; }
        internal sealed class Blend { public int SrcFactor = 1, DstFactor = 10; }
        internal sealed class Data
        {
            public List<Item> Tracks, CriticalLines, SingleLines, ComboColors, HitEffects;
            public List<NoteSet> Notes;
            public List<Particle> Particles;
            public ImageAsset BaseDiamond;
        }
        private static Data data;
        private static List<Diamond> diamonds;
        public static string Root => Path.Combine(Application.streamingAssetsPath, "Appearance");
        internal static Data Loaded { get { EnsureLoaded(); return data; } }
        public static IReadOnlyList<AppearanceOption> Tracks => Loaded.Tracks;
        public static IReadOnlyList<AppearanceOption> CriticalLines => Loaded.CriticalLines;
        public static IReadOnlyList<AppearanceOption> SingleLines => Loaded.SingleLines;
        public static IReadOnlyList<AppearanceOption> ComboColors => Loaded.ComboColors;
        public static IReadOnlyList<AppearanceOption> Diamonds { get { EnsureLoaded(); return diamonds; } }
        internal static IReadOnlyList<Diamond> DiamondItems { get { EnsureLoaded(); return diamonds; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { data = null; diamonds = null; }

        private static void EnsureLoaded()
        {
            if (data != null) return;
            data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(Path.Combine(Root, "Catalogue.json")));
            diamonds = new List<Diamond>
            {
                new Diamond { Id = "none", Name = "none" },
                new Diamond { Id = "diamond", Name = "diamond", MultiplyScale = 1, PivotY = 1,
                    UseInternalBlendFactorSettings = true }
            };
        }
        public static int GetTrackSide(string id)
        {
            var option = Find(Loaded.Tracks, id);
            return option?.Side ?? -1;
        }
        internal static Item Find(List<Item> list, string id) => list.Find(item => item.Id == id);
    }
}

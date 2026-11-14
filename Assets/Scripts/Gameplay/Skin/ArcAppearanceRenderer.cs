using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using Arcade.Compose;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Gameplay
{
    // One selected appearance is resident. Metadata never decodes the full catalogue.
    [DefaultExecutionOrder(31010)]
    internal sealed class ArcAppearanceRenderer : MonoBehaviour
    {
        private sealed class LoadedImage { public Sprite Sprite; public Texture2D Texture; }
        private readonly Dictionary<string, LoadedImage> images = new Dictionary<string, LoadedImage>();
        private readonly Dictionary<string, HashSet<string>> groups = new Dictionary<string, HashSet<string>>();
        private HashSet<string> loadingGroup;
        private ArcSkinManager owner;
        private AdeSkinHost.ThemeSideData basis;
        private AppearanceSelection selection;
        private Side side;
        private SpriteRenderer left, right;
        private Material wallMaterial, diamondMaterial;
        private Image diamond;
        private RectTransform diamondNode;
        private ArcAppearanceCatalog.Item wall;
        private ArcAppearanceCatalog.Diamond diamondConfig;
        private float wallPhase, wallVelocity;
        private int previousTiming = int.MinValue;
        private Vector2 lastDesign;
        private float lastDiamondAlpha = -1;

        internal void CaptureBase(ArcSkinManager manager, AdeSkinHost.ThemeSideData theme, Side value)
        {
            owner = manager; basis = theme; side = value;
            Apply(selection, value);
        }
        internal void Apply(AppearanceSelection value, Side valueSide)
        {
            selection = value; side = valueSide;
            if (basis == null) return;
            Begin("appearance");
            var data = ArcAppearanceCatalog.Loaded;
            string trackId = value?.Track ?? ((int)side == 2 ? "Colorless" : (int)side == 4 ? "Dark Lephon" : null);
            var track = ArcAppearanceCatalog.Find(data.Tracks, trackId);
            Sprite trackSprite = track == null ? basis.Track.value : Load(track.Sprite, basis.Track.value);
            foreach (var renderer in owner.TrackComponents) renderer.sprite = trackSprite;
            // Extra lanes follow the selected track's actual side, independent of note skins.
            int trackSide = track?.Side ?? (int)side;
            var defaults = AdeSkinHost.Instance.rawDefaultData;
            foreach (var renderer in owner.TrackExtraComponents)
                renderer.sprite = track == null ? basis.TrackExtra.value : IsDark(trackSide) ? defaults.TrackExtraConflict : defaults.TrackExtraLight;
            string criticalId = value?.CriticalLine ?? ((int)side == 2 ? "colorless" : null);
            var critical = ArcAppearanceCatalog.Find(data.CriticalLines, criticalId);
            foreach (var renderer in owner.CriticalLines) renderer.sprite = critical == null ? basis.CriticalLine.value : Load(critical.Sprite, basis.CriticalLine.value);
            foreach (var renderer in owner.CriticalLineExtras)
                renderer.sprite = critical == null ? basis.CriticalLineExtra.value : IsDark(trackSide) ? defaults.CriticalLineExtraConflict : defaults.CriticalLineExtraLight;
            string comboId = value?.Combo ?? ((int)side == 2 ? "colorless" : (int)side == 4 ? "conflict" : null);
            var combo = ArcAppearanceCatalog.Find(data.ComboColors, comboId);
            owner.ComboText.color = combo?.Color ?? basis.ComboTextColor;
            ApplyWalls(ArcAppearanceCatalog.Find(data.SingleLines, value?.SingleLine ?? "none"));
            ApplyDiamond(value?.Diamond ?? "none");
            Finish();
            previousTiming = int.MinValue;
        }
        internal void Extras(int note, string hitEffect, string particle, Side valueSide)
        {
            Begin("extras");
            var data = ArcAppearanceCatalog.Loaded;
            if (note >= 0 && note < data.Notes.Count)
            {
                var set = data.Notes[note];
                ArcTapNoteManager.Instance.SetTapNoteSkin(Load(set.Tap, AdeSkinHost.Instance.rawDefaultData.TapNoteLight));
                ArcHoldNoteManager.Instance.SetHoldNoteSkin(Load(set.Hold, AdeSkinHost.Instance.rawDefaultData.HoldNoteLight),
                    Load(set.Highlight, AdeSkinHost.Instance.rawDefaultData.HoldNoteHighlightLight));
                ArcArcManager.Instance.SetArcTapSkin(Load(set.ArcTap).texture);
            }
            var hit = ArcAppearanceCatalog.Find(data.HitEffects, hitEffect);
            if (hit != null)
            {
                ArcEffectManager.Instance.SetTapEffectTexture(Load(hit.Sprite).texture);
                ArcEffectManager.Instance.SetTapEffectConflictBlend((int)valueSide == 1);
            }
            var longEffect = data.Particles.Find(p => p.Name == particle);
            if (longEffect != null)
            {
                ArcEffectManager.Instance.SetParticleArcColor(longEffect.Start, longEffect.End);
                ArcLongNoteEffect.AppearanceScale = longEffect.Scale / 2.1f;
            }
            else ArcLongNoteEffect.AppearanceScale = 1;
            Finish();
        }
        private static bool IsDark(int value) => value == 1 || value == 4;
        private void Begin(string group)
        {
            if (!groups.TryGetValue(group, out loadingGroup)) groups.Add(group, loadingGroup = new HashSet<string>());
            loadingGroup.Clear();
        }
        private void Finish()
        {
            loadingGroup = null;
            var unused = new List<string>();
            foreach (var pair in images)
            {
                bool pinned = false;
                foreach (var group in groups.Values) if (group.Contains(pair.Key)) { pinned = true; break; }
                if (!pinned) unused.Add(pair.Key);
            }
            foreach (string key in unused) { Destroy(images[key].Sprite); Destroy(images[key].Texture); images.Remove(key); }
        }
        private Sprite Load(ArcAppearanceCatalog.ImageAsset image, Sprite geometry = null)
        {
            if (image == null || string.IsNullOrEmpty(image.File)) return null;
            string root = Path.GetFullPath(ArcAppearanceCatalog.Root) + Path.DirectorySeparatorChar;
            string file = Path.GetFullPath(Path.Combine(root, image.File));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Appearance image must be inside its pack.");
            // The scene's transforms were authored against the existing sprite's anchor.
            // TextureImporter alignment can override its serialized spritePivot value;
            // preserve the actual runtime geometry when changing a scene skin.
            Vector2 pivot = geometry ? new Vector2(geometry.pivot.x / geometry.rect.width, geometry.pivot.y / geometry.rect.height) : image.Pivot;
            float pixelsPerUnit = geometry ? geometry.pixelsPerUnit : image.PixelsPerUnit;
            string key = file + "|" + pivot.x.ToString("R", CultureInfo.InvariantCulture) + "," + pivot.y.ToString("R", CultureInfo.InvariantCulture)
                + "|" + pixelsPerUnit.ToString("R", CultureInfo.InvariantCulture);
            loadingGroup?.Add(key);
            if (!images.TryGetValue(key, out var entry))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(file), wrapMode = TextureWrapMode.Repeat };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file), true)) { Destroy(texture); throw new IOException("Cannot decode appearance image: " + file); }
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
                images.Add(key, entry = new LoadedImage { Sprite = sprite, Texture = texture });
            }
            return entry.Sprite;
        }
        private void ApplyWalls(ArcAppearanceCatalog.Item item)
        {
            wall = item;
            bool visible = item != null && !item.IsEmpty;
            if (!visible) { if (left) left.gameObject.SetActive(false); if (right) right.gameObject.SetActive(false); return; }
            if (!wallMaterial)
            {
                wallMaterial = new Material(Resources.Load<Shader>("AffdataEdit/AppearanceWall"));
                left = CreateWall("Left decoration"); right = CreateWall("Right decoration");
            }
            wallMaterial.SetInt("_SrcFactor", item.SrcBlend);
            wallMaterial.SetInt("_DstFactor", item.DstBlend);
            SetWall(left, Load(item.Left), item.PositionLeft, new Vector3(140, 90, -90), item);
            SetWall(right, Load(item.Right), item.PositionRight, new Vector3(40, 90, -90), item);
            wallMaterial.SetFloat("_Alpha", (IsDark((int)side) ? item.DarkOpacity : item.LightOpacity) / 255f);
        }
        private SpriteRenderer CreateWall(string name)
        {
            var renderer = new GameObject(name, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            renderer.transform.SetParent(transform, false);
            renderer.gameObject.layer = owner.TrackComponents[0].gameObject.layer;
            renderer.sharedMaterial = wallMaterial;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.sortingLayerID = owner.TrackComponents[0].sortingLayerID;
            renderer.sortingOrder = owner.TrackComponents[0].sortingOrder + 1;
            return renderer;
        }
        private static void SetWall(SpriteRenderer renderer, Sprite sprite, Vector3 position, Vector3 angle, ArcAppearanceCatalog.Item item)
        {
            renderer.sprite = sprite;
            renderer.size = new Vector2(sprite.texture.width / sprite.pixelsPerUnit, item.Length);
            renderer.transform.localPosition = position;
            renderer.transform.localEulerAngles = angle;
            renderer.transform.localScale = new Vector3(item.Scale.x, item.Scale.y, 1);
            renderer.gameObject.SetActive(true);
        }
        private void ApplyDiamond(string id)
        {
            diamondConfig = null;
            foreach (var item in ArcAppearanceCatalog.DiamondItems) if (item.Id == id) { diamondConfig = item; break; }
            if (diamondConfig == null || id == "none") { if (diamond) diamond.enabled = false; return; }
            if (!diamond)
            {
                diamondNode = new GameObject("Appearance diamond", typeof(RectTransform)).GetComponent<RectTransform>();
                diamondNode.SetParent(owner.Background.transform.parent, false);
                diamondNode.SetSiblingIndex(owner.Background.transform.GetSiblingIndex() + 1);
                diamondNode.anchorMin = diamondNode.anchorMax = new Vector2(.5f, .5f);
                diamond = new GameObject("Image", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                diamond.transform.SetParent(diamondNode, false); diamond.raycastTarget = false;
                diamondMaterial = new Material(Resources.Load<Shader>("AffdataEdit/AppearanceDiamond"));
                diamond.material = diamondMaterial;
            }
            diamond.sprite = Load(ArcAppearanceCatalog.Loaded.BaseDiamond); diamond.enabled = true;
            diamond.rectTransform.pivot = new Vector2(.5f, diamondConfig.PivotY);
            Vector2 anchor = diamondConfig.AnchorSettings.AnchorType == 0 ? new Vector2(.5f, 1) : new Vector2(.5f, .5f);
            diamond.rectTransform.anchorMin = diamond.rectTransform.anchorMax = anchor;
            diamond.rectTransform.anchoredPosition = new Vector2(diamondConfig.AnchorSettings.AnchorPositionX, diamondConfig.AnchorSettings.AnchorPositionY);
            diamond.rectTransform.sizeDelta = diamond.sprite.rect.size * diamondConfig.MultiplyScale;
            int src = diamondConfig.BlendFactorSettings.SrcFactor, dst = diamondConfig.BlendFactorSettings.DstFactor;
            if (diamondConfig.UseInternalBlendFactorSettings) { src = 1; dst = IsDark((int)side) ? 1 : 10; }
            diamondMaterial.SetInt("_SrcFactor", src); diamondMaterial.SetInt("_DstFactor", dst);
            lastDesign = Vector2.zero;
            lastDiamondAlpha = -1;
        }
        private void LateUpdate()
        {
            var game = ArcGameplayManager.Instance;
            if (wall != null && !wall.IsEmpty && wallMaterial && game)
            {
                if (previousTiming == int.MinValue || game.ChartTiming < previousTiming) wallVelocity = ArcTimingManager.Instance.SettingVelocity * ArcTimingManager.Instance.CurrentSpeed;
                if (game.IsPlaying)
                {
                    float velocity = ArcTimingManager.Instance.SettingVelocity * ArcTimingManager.Instance.CurrentSpeed;
                    wallVelocity += (velocity - wallVelocity) / 30;
                    wallPhase = (wallPhase + Time.deltaTime * 3 * Mathf.Max(wallVelocity * wall.ScrollSpeed, 100) / 512) % 1;
                    wallMaterial.SetFloat("_Phase", wallPhase);
                }
                previousTiming = game.ChartTiming;
            }
            if (!diamond || !diamond.enabled || diamondConfig == null || !ArcCameraManager.Instance) return;
            Vector2 design = ArcCameraManager.Instance.DesignResolution;
            if (design != lastDesign)
            {
                lastDesign = design;
                Vector2 size = new Vector2(1280, 960);
                float offset = 0;
                if (diamondConfig.CanvasRectUseDesignResolutionSize && diamondConfig.AnchorSettings.AnchorType == 0)
                {
                    if (Mathf.Abs(design.y - 720) < .001f && design.x > 1280) offset = design.x * -50 / 1560;
                    design /= design.x / 1280;
                    if (design.y < 960) size = design;
                }
                diamondNode.sizeDelta = size;
                diamondNode.anchoredPosition = new Vector2(0, -offset);
            }
            // Scalar pulse, no tweens, coroutines, allocations or intermediate render targets.
            float pulse = Mathf.PingPong(Time.time / .7f, 1);
            float alpha = Mathf.Lerp(diamondConfig.MinOpacity, diamondConfig.MaxOpacity, pulse) / 255f;
            if (!Mathf.Approximately(alpha, lastDiamondAlpha)) { diamondMaterial.SetFloat("_Alpha", alpha); lastDiamondAlpha = alpha; }
        }
        private void OnDestroy()
        {
            foreach (var image in images.Values) { Destroy(image.Sprite); Destroy(image.Texture); }
            if (wallMaterial) Destroy(wallMaterial);
            if (diamondMaterial) Destroy(diamondMaterial);
            if (diamondNode) Destroy(diamondNode.gameObject);
        }
    }
}

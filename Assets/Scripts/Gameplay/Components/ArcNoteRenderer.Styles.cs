using System.Collections.Generic;
using UnityEngine;

namespace Arcade.Gameplay
{
    public sealed partial class ArcNoteRenderer
    {
        private struct SpriteStyle
        {
            public Matrix4x4 Flip;
            public Quaternion Rotation;
            public string Layer;
            public int Queue;
        }
        private struct MaterialStyle
        {
            public Texture Texture;
            public Vector4 Uv;
            public int Queue;
        }
        private struct SpriteGeometry
        {
            public Mesh Mesh;
            public Texture Texture;
            public Bounds Bounds;
        }
        private readonly Dictionary<SpriteRenderer, SpriteStyle> spriteStyles = new Dictionary<SpriteRenderer, SpriteStyle>();
        private readonly Dictionary<Material, MaterialStyle> materialStyles = new Dictionary<Material, MaterialStyle>();
        private readonly Dictionary<Sprite, SpriteGeometry> spriteGeometry = new Dictionary<Sprite, SpriteGeometry>();
        private SpriteRenderer normalTapShadow, sfxTapShadow;
        private int normalTapSubmeshes, sfxTapSubmeshes;
        private Matrix4x4 arcLayerMatrix;
        private void PrepareStyles()
        {
            spriteStyles.Clear(); materialStyles.Clear(); spriteGeometry.Clear();
            var manager = ArcArcManager.Instance;
            normalTapShadow = manager.ArcTapShadowStyle(false);
            sfxTapShadow = manager.ArcTapShadowStyle(true);
            normalTapSubmeshes = ArcNoteMeshes.Cube.subMeshCount;
            sfxTapSubmeshes = ArcNoteMeshes.Sfx.subMeshCount;
            arcLayerMatrix = manager.ArcLayer.localToWorldMatrix;
        }
        private SpriteStyle GetStyle(SpriteRenderer style)
        {
            if (spriteStyles.TryGetValue(style, out var result)) return result;
            result = new SpriteStyle {
                Flip = Matrix4x4.Scale(new Vector3(style.flipX ? -1 : 1, style.flipY ? -1 : 1, 1)),
                Rotation = style.transform.localRotation, Layer = style.sortingLayerName, Queue = style.sharedMaterial.renderQueue
            };
            spriteStyles.Add(style, result);
            return result;
        }
        private MaterialStyle GetStyle(Material material)
        {
            if (materialStyles.TryGetValue(material, out var result)) return result;
            Vector2 scale = material.mainTextureScale, offset = material.mainTextureOffset;
            result = new MaterialStyle { Texture = material.mainTexture, Uv = new Vector4(scale.x, scale.y, offset.x, offset.y), Queue = material.renderQueue };
            materialStyles.Add(material, result);
            return result;
        }
        private SpriteGeometry GetSprite(Sprite sprite)
        {
            if (spriteGeometry.TryGetValue(sprite, out var result)) return result;
            result = new SpriteGeometry { Mesh = ArcNoteMeshes.Sprite(sprite), Texture = sprite.texture, Bounds = sprite.bounds };
            spriteGeometry.Add(sprite, result);
            return result;
        }
    }
}

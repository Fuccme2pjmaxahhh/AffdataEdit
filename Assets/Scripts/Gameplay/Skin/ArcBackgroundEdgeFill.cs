using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Arcade.Gameplay
{
    // Extend only uncovered background pixels. No readable texture, CPU blur,
    // duplicate full-size texture or render target is needed when the image changes.
    [DefaultExecutionOrder(32000)]
    internal sealed class ArcBackgroundEdgeFill : MonoBehaviour
    {
        internal Image Source;
        private RawImage fill;
        private Material material;

        private void LateUpdate()
        {
            if (!Source) return;
            if (!fill)
            {
                fill = new GameObject("Background edge extension", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                fill.gameObject.layer = Source.gameObject.layer;
                fill.transform.SetParent(Source.transform.parent, false);
                fill.transform.SetSiblingIndex(Source.transform.GetSiblingIndex());
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = Vector2.one;
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                fill.raycastTarget = false;
                material = new Material(Resources.Load<Shader>("AffdataEdit/BackgroundEdgeFill"));
                fill.material = material;
            }
            var sprite = Source.overrideSprite;
            fill.enabled = Source.isActiveAndEnabled && sprite;
            if (!fill.enabled) return;
            fill.texture = sprite.texture;
            fill.color = Source.color;
            Rect rect = Source.rectTransform.rect;
            material.SetVector("_ImageRect", new Vector4(rect.x, rect.y, rect.width, rect.height));
            material.SetVector("_ImageUV", DataUtility.GetOuterUV(sprite));
            material.SetMatrix("_FillToImage", Source.transform.worldToLocalMatrix * fill.transform.localToWorldMatrix);
        }

        private void OnDestroy()
        {
            if (fill) Destroy(fill.gameObject);
            if (material) Destroy(material);
        }
    }
}


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
        private Canvas backgroundCanvas;

        private void LateUpdate()
        {
            if (!Source) return;
            if (!backgroundCanvas) backgroundCanvas = Source.GetComponentInParent<Canvas>();
            var cameraManager = ArcCameraManager.Instance;
            if (backgroundCanvas && cameraManager && cameraManager.GameplayCamera)
                FitCanvas(backgroundCanvas, cameraManager.GameplayCamera, cameraManager.DesignResolution);
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

        // A ScreenSpaceCamera canvas at distance 1000 can retain perspective-sized
        // geometry when the editor switches to an orthographic camera. Keep the same
        // background plane/layers, but fit it explicitly to the current projection.
        internal static void FitCanvas(Canvas canvas, Camera camera, Vector2 designSize)
        {
            float distance = Mathf.Clamp(canvas.planeDistance, camera.nearClipPlane + .01f, camera.farClipPlane - .01f);
            Vector3 lowerLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, distance));
            Vector3 lowerRight = camera.ViewportToWorldPoint(new Vector3(1, 0, distance));
            Vector3 upperLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, distance));
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var rect = (RectTransform)canvas.transform;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = designSize;
            rect.SetPositionAndRotation((lowerRight + upperLeft) * .5f, camera.transform.rotation);
            Vector3 parentScale = rect.parent ? rect.parent.lossyScale : Vector3.one;
            rect.localScale = new Vector3(Vector3.Distance(lowerLeft, lowerRight) / designSize.x / parentScale.x,
                Vector3.Distance(lowerLeft, upperLeft) / designSize.y / parentScale.y, 1 / parentScale.z);
        }

        private void OnDestroy()
        {
            if (fill) Destroy(fill.gameObject);
            if (material) Destroy(material);
        }
    }
}


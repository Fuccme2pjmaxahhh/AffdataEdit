using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // ScrollRect still owns content size/value. Only the handle's native drag geometry is
    // enlarged, so a tiny viewport/content ratio remains usable on very long lists.
    [DisallowMultipleComponent, DefaultExecutionOrder(32700)]
    public sealed class AdeScrollbarThumb : MonoBehaviour
    {
        private Scrollbar bar;
        private ScrollRect owner;
        private Canvas canvas;
        private readonly Vector3[] corners = new Vector3[4];
        private float requestedSize = 1, appliedSize = -1;
        public float MinimumPixels = 20;

        public static void Ensure(Scrollbar scrollbar)
        {
            if (!scrollbar) return;
            var helper = scrollbar.GetComponent<AdeScrollbarThumb>();
            if (!helper) helper = scrollbar.gameObject.AddComponent<AdeScrollbarThumb>();
            helper.bar = scrollbar;
            helper.owner = scrollbar.GetComponentInParent<ScrollRect>();
            helper.canvas = scrollbar.GetComponentInParent<Canvas>();
            helper.Apply();
        }
        private void OnEnable()
        {
            // Ensure the layout registry runs first in this event. It may rewrite bar.size.
            _ = CanvasUpdateRegistry.instance;
            Canvas.willRenderCanvases += Apply;
        }
        private void OnDisable() => Canvas.willRenderCanvases -= Apply;
        private void LateUpdate() => Apply();
        private void Apply()
        {
            if (!bar || !bar.handleRect) return;
            var track = bar.handleRect.parent as RectTransform;
            if (!track) return;
            bool vertical = bar.direction == Scrollbar.Direction.BottomToTop || bar.direction == Scrollbar.Direction.TopToBottom;
            int axis = vertical ? 1 : 0;
            track.GetWorldCorners(corners);
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float pixels = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, corners[0]),
                RectTransformUtility.WorldToScreenPoint(camera, corners[vertical ? 1 : 3]));
            if (pixels <= 0) return;
            if (!Mathf.Approximately(bar.size, appliedSize)) requestedSize = bar.size;
            // Recompute the natural fraction for ScrollRects, including after viewport resizing.
            // The full [0,1] value still maps to the unchanged content scroll range.
            if (owner && owner.content && owner.viewport
                && (vertical ? owner.verticalScrollbar == bar : owner.horizontalScrollbar == bar))
            {
                float viewScale = owner.viewport.lossyScale[axis];
                float scale = viewScale == 0 ? 1 : Mathf.Abs(owner.content.lossyScale[axis] / viewScale);
                float length = owner.content.rect.size[axis] * scale;
                requestedSize = length > 0 ? Mathf.Clamp01(owner.viewport.rect.size[axis] / length) : 1;
            }
            appliedSize = Mathf.Clamp01(Mathf.Max(requestedSize, MinimumPixels / pixels));
            if (!Mathf.Approximately(bar.size, appliedSize)) bar.size = appliedSize;
        }
    }
}

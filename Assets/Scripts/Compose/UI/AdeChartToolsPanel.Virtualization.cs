using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeChartToolsPanel
    {
        private const int VirtualOverscan = 2;
        private int groupSlots;
        private float lastGroupViewport = -1;

        private static int VirtualRowCount(float height, float pitch, int count)
            => Mathf.Min(count, Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, height) / pitch)) + VirtualOverscan * 2 + 1);

        private static int VirtualFirst(float offset, float pitch, int count, int rows)
            => Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0, offset) / pitch) - VirtualOverscan, 0, Mathf.Max(0, count - rows));

        // Absolute rows keep content bounds constant while scrolling. Replacing giant layout
        // spacers after ScrollRect.LateUpdate otherwise leaves a frame with stale row positions
        // and feeds the stale preferred height back into the following layout pass.
        private static void VirtualContent(RectTransform content, int count, float pitch)
        {
            var layout = content.GetComponent<LayoutGroup>();
            if (layout) layout.enabled = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter) fitter.enabled = false;
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            float height = Mathf.Max(0, count * pitch - (pitch - ControlHeight));
            if (!Mathf.Approximately(content.rect.height, height))
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        private static void VirtualRow(RectTransform row, int index, float pitch)
        {
            row.anchorMin = new Vector2(0, 1); row.anchorMax = Vector2.one;
            row.pivot = new Vector2(.5f, 1);
            row.anchoredPosition = new Vector2(0, -index * pitch);
            row.sizeDelta = new Vector2(0, ControlHeight);
        }

        private void RefreshVirtualViewport()
        {
            if (groupScroll && groupScroll.viewport && groupList && !groupsCollapsed)
            {
                float height = groupScroll.viewport.rect.height;
                if (!Mathf.Approximately(height, lastGroupViewport))
                {
                    lastGroupViewport = height;
                    groupFirst = -1;
                }
                ScrollGroups();
            }
            foreach (var page in controlPages)
                if (page != null && page.Scroll && page.Scroll.gameObject.activeInHierarchy) ScrollControls(page);
        }
        private void SynchronizeVirtualRows()
        {
            // Runs after wheel easing and the canvas layout pass, before geometry is rendered.
            // It never rebuilds chart data or touches the Timing/SC/Camera edit fields.
            if (ready && expanded) RefreshVirtualViewport();
        }
    }
}

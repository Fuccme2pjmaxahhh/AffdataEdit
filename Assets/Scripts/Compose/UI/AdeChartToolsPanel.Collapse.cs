using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // Timing page: the group list and the Timing/属性 editor below it can each be collapsed with a chevron;
    // the other one then takes over the freed height. Also gives both list scrollbars a visible handle.
    public sealed partial class AdeChartToolsPanel
    {
        private const float GroupListDefaultHeight = 188;
        private bool groupsCollapsed, editCollapsed, groupLayoutDirty = true;
        private float groupListHeight = GroupListDefaultHeight, groupViewportHeight;
        private Button groupChevron, editChevron;
        private readonly System.Collections.Generic.List<Image> scrollHandles = new System.Collections.Generic.List<Image>();

        // Rows needed to fill the (possibly enlarged) group list, plus one for partial rows while scrolling.
        private int GroupRowCount => VirtualRowCount(groupScroll && groupScroll.viewport && groupScroll.viewport.rect.height > 0
            ? groupScroll.viewport.rect.height : groupListHeight, ControlHeight + 4, groups.Count);

        private void BindCollapsibles()
        {
            // Group list: chevron at the start of its header ("Timing · …  + 新建").
            var groupHeader = groupCount.transform.parent;
            groupChevron = IconButton(groupHeader, "折叠时间组列表", "ChevronDown", () => { groupsCollapsed = !groupsCollapsed; ApplyCollapse(); });
            groupChevron.transform.SetAsFirstSibling();
            // Editor: the Timing/属性 tabs move into a row with a chevron in front of them.
            var tabsRow = (RectTransform)groupTabs[0].transform.parent;
            var row = Rect("时间组编辑行", tabsRow.parent, typeof(HorizontalLayoutGroup));
            row.SetSiblingIndex(tabsRow.GetSiblingIndex());
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            float tabsHeight = LayoutUtility.GetPreferredHeight(tabsRow);
            if (tabsHeight <= 0) tabsHeight = 48;
            Size(row, -1, tabsHeight);
            editChevron = IconButton(row, "折叠编辑区", "ChevronDown", () => { editCollapsed = !editCollapsed; ApplyCollapse(); });
            tabsRow.SetParent(row, false);
            Size(tabsRow, -1, tabsHeight);
            FixScrollbar(groupScroll);
            FixScrollbar(timingScroll);
            ApplyCollapse();
        }

        private void ApplyCollapse()
        {
            groupScroll.gameObject.SetActive(!groupsCollapsed);
            if (groupsCollapsed) deleteConfirmation.gameObject.SetActive(false);
            for (int i = 0; i < groupSections.Length; i++) groupSections[i].gameObject.SetActive(!editCollapsed && i == activeGroupTab);
            SetChevron(groupChevron, groupsCollapsed);
            SetChevron(editChevron, editCollapsed);
            groupLayoutDirty = timingLayoutDirty = true;
        }

        // Down when open, pointing right when collapsed.
        private static void SetChevron(Button button, bool collapsed)
        {
            if (!button) return;
            var icon = button.transform.Find("Icon");
            if (icon) icon.localEulerAngles = new Vector3(0, 0, collapsed ? 90 : 0);
        }

        // With the editor collapsed the group list fills the page; otherwise it keeps its normal height.
        private void ResizeGroupList()
        {
            if (!expanded || activePage != Page.Groups || lastChart == null || groupsCollapsed) return;
            float viewport = ((RectTransform)pages[0].parent).rect.height;
            if (!groupLayoutDirty && !(editCollapsed && !Mathf.Approximately(viewport, groupViewportHeight))) return;
            groupLayoutDirty = false;
            groupViewportHeight = viewport;
            float target = Mathf.Min(groups.Count, 4) * (ControlHeight + 4) - 4;
            if (editCollapsed)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(pages[0]);
                float other = LayoutUtility.GetPreferredHeight(pages[0]) - LayoutUtility.GetPreferredHeight((RectTransform)groupScroll.transform);
                float content = Mathf.Max(1, groups.Count) * (ControlHeight + 4) - 4;
                target = Mathf.Max(GroupListDefaultHeight, Mathf.Min(content, viewport - other));
            }
            var size = groupScroll.GetComponent<LayoutElement>() ?? groupScroll.gameObject.AddComponent<LayoutElement>();
            if (Mathf.Approximately(size.preferredHeight, target)) return;
            size.minHeight = size.preferredHeight = target;
            groupListHeight = target;
            groupFirst = -1;
            dirty = true; // builds extra virtual rows for the taller list
            LayoutRebuilder.ForceRebuildLayoutImmediate(pages[0]);
        }

        // The scene's list scrollbars had no handle graphic (only a grey track), so nothing visible moved.
        private void FixScrollbar(ScrollRect scroll)
        {
            var bar = scroll ? scroll.verticalScrollbar : null;
            if (!bar || !bar.handleRect) return;
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(1, .5f);
            rect.sizeDelta = new Vector2(6, 0); rect.anchoredPosition = Vector2.zero;
            var track = bar.GetComponent<Image>();
            if (track) track.color = Color.clear;
            var handle = bar.handleRect.GetComponent<Image>() ?? bar.handleRect.gameObject.AddComponent<Image>();
            handle.raycastTarget = true;
            bar.targetGraphic = handle;
            bar.transition = Selectable.Transition.None;
            scrollHandles.Add(handle);
            // Keep the rows clear of the bar.
            scroll.viewport.offsetMax = new Vector2(-10, scroll.viewport.offsetMax.y);
            AdeScrollbarThumb.Ensure(bar);
            StyleScrollbars();
        }

        private void StyleScrollbars()
        {
            foreach (var handle in scrollHandles)
                if (handle) handle.color = new Color(Muted.r, Muted.g, Muted.b, .6f);
        }
    }
}

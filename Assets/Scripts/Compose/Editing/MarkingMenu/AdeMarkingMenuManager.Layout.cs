using System.Collections.Generic;
using Arcade.Compose.MarkingMenu;
using Arcade.Util.UnityExtension;
using UnityEngine;
using UnityEngine.UI;
using Arcade.Compose.Operation;

namespace Arcade.Compose
{
	public partial class AdeMarkingMenuManager
	{
		private readonly List<GameObject> menuPanels = new List<GameObject>();
        private MarkingMenuItem[] ringArcTapItems;

        private MarkingMenuItem[] ArrangeRingItems(MarkingMenuItem[] items)
        {
            var create = AdeClickToCreate.Instance;
            if (!create) return items;
            var list = new List<MarkingMenuItem>();
            foreach (var item in items)
            {
                if (create.ClickToCreateItems.Length > 7 && item == create.ClickToCreateItems[7]) continue;
                if (item == create.ClickToCreateItems[4])
                {
                    if (ringArcTapItems == null)
                    {
                        ringArcTapItems = new[] { CloneVerticalItem(item, "RingSingleArcTap", "单Arctap"), CloneVerticalItem(item, "RingArcTap", "Arctap") };
                        for (int i = 0; i < 2; i++)
                        {
                            var mode = i == 0 ? ClickToCreateArctapMode.Single : ClickToCreateArctapMode.OnArc;
                            ringArcTapItems[i].OnConfirmed.AddListener(() => { create.SetClickToCreateMode(ClickToCreateMode.ArcTap); create.SetClickToCreateArctapMode(mode); });
                        }
                    }
                    list.AddRange(ringArcTapItems);
                }
                else if (item.name == "ArcType" && System.Array.Exists(items, n => n.name == "S")) list.AddRange(item.SubItems);
                else list.Add(item);
            }
            string[] curveNames = { "S", "SI", "SO", "B", "SOSO", "SOSI", "SISO", "SISI" };
            if (list.Count == 10 && System.Array.TrueForAll(curveNames, name => list.Exists(item => item.name == name)))
            {
                var sides = list.FindAll(item => System.Array.IndexOf(curveNames, item.name) < 0);
                var ordered = new List<MarkingMenuItem> { sides[0] };
                for (int i = 0; i < curveNames.Length; i++)
                {
                    if (i == 4) ordered.Add(sides[1]);
                    ordered.Add(list.Find(item => item.name == curveNames[i]));
                }
                return ordered.ToArray();
            }
            return list.ToArray();
        }

		private static float GetRingRadius(MarkingMenuItem[] items, Vector2[] directions)
		{
			return GetNonOverlappingScale(items, directions, .72f * (50 + 12 * Mathf.Max(6, items.Length)));
        }

        private static float GetNonOverlappingScale(MarkingMenuItem[] items, Vector2[] directions, float radius)
        {
            const float gap = 8;
			// Enlarge the whole ring to fit the actual button bounds.
			for (int i = 0; i < items.Length; i++)
			{
				for (int j = 0; j < i; j++)
				{
					Vector2 delta = directions[i] - directions[j];
					Vector2 size = (items[i].ItemRectTransform.rect.size + items[j].ItemRectTransform.rect.size) / 2;
					float horizontal = Mathf.Abs(delta.x) > .0001f ? (size.x + gap) / Mathf.Abs(delta.x) : float.PositiveInfinity;
					float vertical = Mathf.Abs(delta.y) > .0001f ? (size.y + gap) / Mathf.Abs(delta.y) : float.PositiveInfinity;
					radius = Mathf.Max(radius, Mathf.Min(horizontal, vertical));
				}
			}
			return radius;
		}

        private static float RingLayoutCost(MarkingMenuItem[] items, Vector2[] directions)
        {
            float radius = GetRingRadius(items, directions);
            float cost = radius * radius;
            // Break ties between several equally tight pairs, so both can improve.
            for (int i = 0; i < items.Length; i++) for (int j = 0; j < i; j++)
            {
                Vector2 delta = directions[i] - directions[j];
                Vector2 size = (items[i].ItemRectTransform.rect.size + items[j].ItemRectTransform.rect.size) / 2;
                float x = Mathf.Abs(delta.x) > .0001f ? (size.x + 8) / Mathf.Abs(delta.x) : float.PositiveInfinity;
                float y = Mathf.Abs(delta.y) > .0001f ? (size.y + 8) / Mathf.Abs(delta.y) : float.PositiveInfinity;
                float needed = Mathf.Min(x, y);
                cost += .001f * needed * needed;
            }
            return cost;
        }

        private static void BalanceRingSpacing(MarkingMenuItem[] items, Vector2[] directions, float degreeDelta)
        {
            var original = new float[items.Length];
            for (int i = 0; i < items.Length; i++) original[i] = items[i].Degree;
            float limit = Mathf.Min(12, Mathf.Abs(degreeDelta) * .28f);
            float cost = RingLayoutCost(items, directions);
            for (int pass = 0; pass < 16; pass++)
            {
                float step = pass < 8 ? 2 : pass < 12 ? 1 : .25f;
                // Move mirrored pairs together, using exactly the visible item count.
                int slots = items.Length;
                for (int i = 1; i * 2 < slots; i++)
                {
                    if (Mathf.Abs(Mathf.Cos(original[i] * Mathf.Deg2Rad)) < .0001f) continue;
                    int partner = slots - i;
                    if (partner >= items.Length) continue;
                    float oldDegree = items[i].Degree, bestDegree = oldDegree;
                    Vector2 bestDirection = directions[i], bestPartnerDirection = directions[partner];
                    float bestCost = cost, oldRadius = GetRingRadius(items, directions);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        float degree = Mathf.Clamp(oldDegree + sign * step, original[i] - limit, original[i] + limit);
                        float partnerDegree = original[partner] - (degree - original[i]);
                        directions[i] = new Vector2(Mathf.Cos(degree * Mathf.Deg2Rad), Mathf.Sin(degree * Mathf.Deg2Rad));
                        directions[partner] = new Vector2(Mathf.Cos(partnerDegree * Mathf.Deg2Rad), Mathf.Sin(partnerDegree * Mathf.Deg2Rad));
                        float candidate = RingLayoutCost(items, directions);
                        if (candidate < bestCost && GetRingRadius(items, directions) <= oldRadius)
                        {
                            bestCost = candidate; bestDegree = degree;
                            bestDirection = directions[i]; bestPartnerDirection = directions[partner];
                        }
                    }
                    directions[i] = bestDirection; directions[partner] = bestPartnerDirection;
                    items[i].Degree = bestDegree;
                    items[partner].Degree = original[partner] - (bestDegree - original[i]);
                    cost = bestCost;
                }
            }
        }

		public void ShowAt(Vector2 position, MarkingMenuItem[] items, float from = 0, float to = 360)
		{
			if (verticalStyle)
			{
				ShowVerticalPage(position, items, null);
				return;
			}
            items = ArrangeRingItems(items);
			int itemCount = items.Length;
			if (itemCount == 0) return;
            // Every visible action occupies one slot; odd menus have no empty slot.
            int slots = itemCount;
            float degreeDelta = -(to - from) / slots;
            var directions = new Vector2[itemCount];
            for (int i = 0; i < itemCount; ++i)
            {
                float degree = from + degreeDelta * i + (itemCount > 4 ? 180 : 0);
                directions[i] = new Vector2(Mathf.Cos(degree * Mathf.Deg2Rad), Mathf.Sin(degree * Mathf.Deg2Rad));
                items[i].Show(verticalStyle);
                items[i].Degree = degree;
            }
            float radius = GetRingRadius(items, directions);
            if (itemCount > 2 && Mathf.Approximately(to - from, 360))
            {
                BalanceRingSpacing(items, directions, degreeDelta);
                radius = GetRingRadius(items, directions);
            }
            for (int i = 0; i < itemCount; ++i)
                items[i].Position = position + radius * directions[i];
            shownItems.Push(new MenuPage(items, position));
        }
		public void ShowSubMenuAt(Vector2 position, MarkingMenuItem[] items, float from = 0, float to = 360)
		{
			if (verticalStyle)
			{
				ShowVerticalPage(position, items, null);
				return;
			}
            int itemCount = items.Length;
            if (itemCount == 0) return;
            float degreeDelta = (to - from) / Mathf.Max(1, itemCount - 1);
            var offsets = new Vector2[itemCount];
            for (int i = 0; i < itemCount; ++i)
            {
                float degree = from + degreeDelta * i;
                float length = .85f * MathfExtension.EvaluateEllipse(degree, 180, 100);
                items[i].Show(verticalStyle);
                offsets[i] = new Vector2(length * Mathf.Cos(degree * Mathf.Deg2Rad), length * Mathf.Sin(degree * Mathf.Deg2Rad));
                items[i].Degree = degree;
            }
            float fit = GetNonOverlappingScale(items, offsets, 1);
            for (int i = 0; i < itemCount; ++i)
                items[i].Position = position + fit * offsets[i];
			shownItems.Push(new MenuPage(items, position));
		}
		private void ShowVerticalPage(Vector2 origin, MarkingMenuItem[] items, MarkingMenuItem parent)
		{
			int depth = shownItems.Count;
			foreach (var item in items) item.Show(true, depth);
			LayoutVerticalMenu(depth, origin, items, parent);
			shownItems.Push(new MenuPage(items, origin));
		}
		private void LayoutVerticalMenu(int depth, Vector2 origin, MarkingMenuItem[] items, MarkingMenuItem parent)
		{
			if (items.Length == 0) return;
			const float padding = 0, rowSpacing = 0;
			// A row holds one item, or a left/right pair; a right item with VerticalWidth is a fixed-width button.
			var rows = new List<(MarkingMenuItem Left, MarkingMenuItem Right)>();
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i].VerticalColumn == 1 && i + 1 < items.Length && items[i + 1].VerticalColumn == 2)
					rows.Add((items[i], items[++i]));
				else rows.Add((items[i], null));
			}
			float width = 260, height = 60;
			foreach (var (left, right) in rows)
			{
				float rowWidth = left.ItemRectTransform.rect.width;
				if (right)
				{
					rowWidth = right.VerticalWidth > 0 ? rowWidth + right.VerticalWidth
						: Mathf.Max(rowWidth, right.ItemRectTransform.rect.width) * 2;
					height = Mathf.Max(height, right.ItemRectTransform.rect.height);
				}
				width = Mathf.Max(width, rowWidth);
				height = Mathf.Max(height, left.ItemRectTransform.rect.height);
			}
			int count = rows.Count;
			Vector2 size = new Vector2(width + padding * 2, height * count + rowSpacing * (count - 1) + padding * 2);
			Vector2 available = ArcadeComposeManager.Instance.EditorCanvas.rect.size;
			Vector2 topLeft = origin + new Vector2(8, -8);
			if (parent)
			{
				// Cascade beside the parent row: to the right when it fits, otherwise to the left.
				var parentPanel = (RectTransform)menuPanels[depth - 1].transform;
				float left = parentPanel.anchoredPosition.x, right = left + parentPanel.sizeDelta.x;
				topLeft.x = right + size.x <= available.x - padding ? right : left - size.x;
				topLeft.y = parent.Position.y + parent.ItemRectTransform.rect.height / 2 + padding;
			}
			topLeft.x = Mathf.Clamp(topLeft.x, padding, Mathf.Max(padding, available.x - size.x - padding));
			topLeft.y = Mathf.Clamp(topLeft.y, Mathf.Min(size.y + padding, available.y - padding), available.y - padding);
			for (int row = 0; row < count; row++)
			{
				var (left, right) = rows[row];
				float leftWidth = !right ? width : right.VerticalWidth > 0 ? width - right.VerticalWidth : width / 2;
				float y = -padding - height * (row + .5f) - rowSpacing * row;
				// Only the panel's outer corners clip the edge-to-edge row highlights.
				int corners = (row == 0 ? 3 : 0) | (row == count - 1 ? 12 : 0);
				left.SetRowSize(leftWidth, height, row < count - 1, right ? corners & 5 : corners);
				left.Position = topLeft + new Vector2(padding + leftWidth / 2, y);
				if (!right) continue;
				right.SetRowSize(width - leftWidth, height, row < count - 1, corners & 10);
				right.Position = topLeft + new Vector2(padding + (leftWidth + width) / 2, y);
			}
			var root = ArcadeComposeManager.Instance.EditorCanvas;
			while (menuPanels.Count <= depth)
			{
				var panel = new GameObject("VerticalContextMenuPanel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(Shadow));
				panel.layer = root.gameObject.layer;
				panel.transform.SetParent(root, false);
				var canvas = panel.GetComponent<Canvas>();
				canvas.overrideSorting = true;
				canvas.sortingLayerID = items[0].GetComponent<Canvas>().sortingLayerID;
				canvas.sortingOrder = MenuSortingOrder + menuPanels.Count * 2;
				menuPanels.Add(panel);
			}
			var menuPanel = menuPanels[depth];
			var rect = (RectTransform)menuPanel.transform;
			rect.anchorMin = rect.anchorMax = Vector2.zero;
			rect.pivot = new Vector2(0, 1);
			rect.anchoredPosition = topLeft;
			rect.sizeDelta = size;
			var background = menuPanel.GetComponent<Image>();
			background.sprite = AdeUiTheme.VerticalContextMenuSurface;
			background.type = Image.Type.Sliced;
			background.color = Color.white;
			var shadow = menuPanel.GetComponent<Shadow>();
			shadow.effectColor = new Color(0, 0, 0, AdeUiTheme.Dark ? .24f : .12f);
			shadow.effectDistance = new Vector2(0, -2);
			menuPanel.SetActive(true);
		}
	}
}

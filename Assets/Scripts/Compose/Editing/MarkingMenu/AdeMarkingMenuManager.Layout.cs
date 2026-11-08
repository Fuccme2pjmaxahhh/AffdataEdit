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
            return list.ToArray();
        }

		private static float GetRingRadius(MarkingMenuItem[] items, Vector2[] directions)
		{
			float radius = 50 + 12 * Mathf.Max(6, items.Length);
			const float gap = 12;
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

		private static void BalanceRingSpacing(MarkingMenuItem[] items, Vector2[] directions, float radius, float degreeDelta)
		{
			var extents = new float[items.Length];
			float average = 0;
			for (int i = 0; i < items.Length; i++)
			{
				int next = (i + 1) % items.Length;
				float middle = (items[i].Degree + degreeDelta / 2) * Mathf.Deg2Rad;
				Vector2 size = (items[i].ItemRectTransform.rect.size + items[next].ItemRectTransform.rect.size) / 2;
				// Project both button bounds onto the line joining their centers.
				extents[i] = size.x * Mathf.Abs(Mathf.Sin(middle)) + size.y * Mathf.Abs(Mathf.Cos(middle));
				average += extents[i] / items.Length;
			}
			float correction = 0;
			float limit = Mathf.Min(3, degreeDelta * .1f);
			for (int i = 0; i < items.Length; i++)
			{
				// Apply only a little of the edge-spacing correction; retain the familiar ring.
				float degree = items[i].Degree + Mathf.Clamp(correction * .3f, -limit, limit);
				items[i].Degree = degree;
				directions[i] = new Vector2(Mathf.Cos(degree * Mathf.Deg2Rad), Mathf.Sin(degree * Mathf.Deg2Rad));
				correction += (extents[i] - average) / radius * Mathf.Rad2Deg;
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
            int slots = itemCount > 4 ? itemCount + itemCount % 2 : itemCount;
			float degreeDelta = -(to - from) / slots;
			var directions = new Vector2[itemCount];
            var offsets = new Vector2[itemCount];
			for (int i = 0; i < itemCount; ++i)
			{
				float degree = OptimizeRingDegree(from + degreeDelta * i + (itemCount > 4 ? 180 : 0));
				directions[i] = new Vector2(Mathf.Cos(degree * Mathf.Deg2Rad), Mathf.Sin(degree * Mathf.Deg2Rad));
				items[i].Show(verticalStyle);
				items[i].Degree = degree;
                offsets[i] = directions[i] * RingEllipseRadius(degree, slots);
			}
            float fit = 1;
            for (int i = 0; i < itemCount; i++) for (int j = 0; j < i; j++)
            {
                var delta = offsets[i] - offsets[j];
                var size = (items[i].ItemRectTransform.rect.size + items[j].ItemRectTransform.rect.size) / 2;
                float x = Mathf.Abs(delta.x) > .0001f ? (size.x + 12) / Mathf.Abs(delta.x) : float.PositiveInfinity;
                float y = Mathf.Abs(delta.y) > .0001f ? (size.y + 12) / Mathf.Abs(delta.y) : float.PositiveInfinity;
                fit = Mathf.Max(fit, Mathf.Min(x, y));
            }
			for (int i = 0; i < itemCount; ++i)
				items[i].Position = position + fit * offsets[i];
			shownItems.Push(new MenuPage(items, position));
		}
        internal static float OptimizeRingDegree(float degree)
        {
            float turns = Mathf.Floor(degree / 180);
            float local = degree - turns * 180;
            float x = local / 90 - 1;
            float eased = Mathf.Sin(Mathf.Pow(Mathf.Abs(x), 1.2f) * Mathf.PI / 2);
            return (Mathf.Sign(x) * eased + 1) * 90 + turns * 180;
        }

        internal static float RingEllipseRadius(float degree, int count)
        {
            if (count > 4) count += count % 2;
            float a = Mathf.Max(6, count) * 10 + 60;
            float b = Mathf.Max(3, count) * 12 + 50;
            float sin = Mathf.Sin(degree * Mathf.Deg2Rad), cos = Mathf.Cos(degree * Mathf.Deg2Rad);
            return a * b / Mathf.Sqrt(a * a * sin * sin + b * b * cos * cos);
        }
		public void ShowSubMenuAt(Vector2 position, MarkingMenuItem[] items, float from = 0, float to = 360)
		{
			if (verticalStyle)
			{
				ShowVerticalPage(position, items, null);
				return;
			}
			int itemCount = items.Length;
			float degreeDelta = (to - from) / (itemCount - 1 == 0 ? 1 : itemCount - 1);
			for (int i = 0; i < itemCount; ++i)
			{
				float degree = from + degreeDelta * i;
				float length = MathfExtension.EvaluateEllipse(degree, 180, 100);
				items[i].Show(verticalStyle);
				items[i].Position = position + new Vector2(length * Mathf.Cos(degree * Mathf.Deg2Rad), length * Mathf.Sin(degree * Mathf.Deg2Rad));
				items[i].Degree = degree;
			}
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

using System.Collections.Generic;
using Arcade.Compose.MarkingMenu;
using Arcade.Util.UnityExtension;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
	public partial class AdeMarkingMenuManager
	{
		private readonly List<GameObject> menuPanels = new List<GameObject>();

		private float GetOptimizedDegree(float degree)
		{
			float remain = 0;
			float normalized = degree;
			while (normalized >= 180)
			{
				normalized -= 180;
				remain += 180;
			}
			while (normalized < 0)
			{
				normalized += 180;
				remain -= 180;
			}

			float d = normalized / 90f - 1;
			bool neg = d < 0;
			d = Mathf.Abs(d);
			d = Mathf.Abs(Mathf.Sin(Mathf.PI * Mathf.Pow(d, 1.2f) / 2));
			d = (d * (neg ? -1 : 1) + 1) * 90f;
			d += remain;

			return d;
		}
		private float GetEllipseLength(float degree, float itemCount)
		{
			if (itemCount > 4) itemCount += itemCount % 2;
			return MathfExtension.EvaluateEllipse(degree, 60 + 10 * Mathf.Max(6, itemCount), 50 + 12 * Mathf.Max(3, itemCount));
		}

		public void ShowAt(Vector2 position, MarkingMenuItem[] items, float from = 0, float to = 360)
		{
			if (verticalStyle)
			{
				ShowVerticalPage(position, items, null);
				return;
			}
			int itemCount = items.Length;
			float degreeDelta = (to - from) / (itemCount > 4 ? itemCount % 2 + itemCount : itemCount);
			for (int i = 0; i < itemCount; ++i)
			{
				float degree = from + degreeDelta * i + (itemCount > 4 ? 180 : 0);
				degree = GetOptimizedDegree(degree);
				float length = GetEllipseLength(degree, itemCount);
				items[i].Show(verticalStyle);
				items[i].Position = position + new Vector2(length * Mathf.Cos(degree * Mathf.Deg2Rad), length * Mathf.Sin(degree * Mathf.Deg2Rad));
				items[i].Degree = degree;
			}
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

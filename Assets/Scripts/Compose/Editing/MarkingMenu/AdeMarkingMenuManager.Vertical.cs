using System.Collections.Generic;
using Affdata.Core.Chart;
using Arcade.Compose.MarkingMenu;
using Arcade.Compose.Operation;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.Events;

namespace Arcade.Compose
{
	public partial class AdeMarkingMenuManager
	{
		private const float VerticalButtonWidth = 48;
		private MarkingMenuItem arcMoreItem;
		private MarkingMenuItem[] arcTapItems;

		// The vertical menu regroups click-to-create items: Arc applies its last settings while a
		// square button beside it opens the Arc options. ArcTap modes and 不创建/退出 share rows.
		private MarkingMenuItem[] ArrangeVerticalItems(MarkingMenuItem[] items)
		{
			var create = AdeClickToCreate.Instance;
			var list = new List<MarkingMenuItem>(items);
			foreach (var item in list) item.VerticalColumn = 0;
			var arc = create.ClickToCreateItems[3];
			int index = list.IndexOf(arc);
			if (index >= 0)
			{
				EnsureVerticalArcItems(arc);
				arc.VerticalColumn = 1;
				arcMoreItem.VerticalColumn = 2;
				list.Insert(index + 1, arcMoreItem);
				var tap = create.ClickToCreateItems[1];
				var hold = create.ClickToCreateItems[2];
				if (list.Contains(tap) && list.Contains(hold))
				{
					// Tap/Hold share a row; Arc and its options button keep a separate row.
					list.Remove(tap); list.Remove(hold);
					index = list.IndexOf(arc);
					list.InsertRange(index, new[] { tap, hold });
					tap.VerticalColumn = 1; hold.VerticalColumn = 2;
				}
			}
			var arcTap = create.ClickToCreateItems[4];
			index = list.IndexOf(arcTap);
			if (index >= 0)
			{
				if (arcTapItems == null)
				{
					arcTapItems = new[]
					{
						CloneVerticalItem(arcTap, "ContextMenuArcTap", "ArcTap"),
						CloneVerticalItem(arcTap, "ContextMenuSingleArcTap", "单 ArcTap")
					};
					for (int i = 0; i < arcTapItems.Length; i++)
					{
						var mode = (ClickToCreateArctapMode)i;
						arcTapItems[i].IconKind = () => mode == ClickToCreateArctapMode.Single ? "create-single-arctap" : "create-arctap";
						arcTapItems[i].OnConfirmed.AddListener(() =>
						{
							create.SetClickToCreateMode(ClickToCreateMode.ArcTap);
							create.SetClickToCreateArctapMode(mode);
						});
					}
				}
				arcTapItems[0].VerticalColumn = 1;
				arcTapItems[1].VerticalColumn = 2;
				list.RemoveAt(index);
				list.InsertRange(index, arcTapItems);
			}
			var none = create.ClickToCreateItems[0];
			var exit = create.ClickToCreateItems[5];
			if (list.Contains(none) && list.Remove(exit))
			{
				list.Insert(list.IndexOf(none) + 1, exit);
				none.VerticalColumn = 1;
				exit.VerticalColumn = 2;
			}
			return list.ToArray();
		}

		private void EnsureVerticalArcItems(MarkingMenuItem arc)
		{
			if (arcMoreItem) return;
			var create = AdeClickToCreate.Instance;
			MarkingMenuItem Find(MarkingMenuItem[] items, string name) => System.Array.Find(items, item => item && item.name == name);
			var isVoid = Find(arc.SubItems, "ArcIsVoid");
			var more = Find(arc.SubItems, "ArcType");
			var s = Find(arc.SubItems, "S");
			var si = Find(arc.SubItems, "SI");
			var so = Find(arc.SubItems, "SO");
			var b = Find(arc.SubItems, "B");
			var combined = more ? more.SubItems : System.Array.Empty<MarkingMenuItem>();

			if (isVoid)
			{
				isVoid.VerticalCaption = "黑线";
				isVoid.KeepsMenuOpen = true;
				isVoid.IconKind = () => create.ArcIsVoid ? "check" : null;
			}
			void Curve(MarkingMenuItem item, ArcCurveType type, string caption = null)
			{
				if (!item) return;
				item.VerticalCaption = caption;
				item.IconKind = () => create.ArcCurveType == type ? "check" : null;
			}
			Curve(s, ArcCurveType.S);
			Curve(b, ArcCurveType.B);
			Curve(Find(combined, "SISI"), ArcCurveType.SiSi, "SiSi");
			Curve(Find(combined, "SISO"), ArcCurveType.SiSo, "SiSo");
			Curve(Find(combined, "SOSI"), ArcCurveType.SoSi, "SoSi");
			Curve(Find(combined, "SOSO"), ArcCurveType.SoSo, "SoSo");
			MarkingMenuItem Derived(MarkingMenuItem item, ArcCurveType type, params MarkingMenuItem[] children)
			{
				if (!item) return null;
				Curve(item, type, type.ToString());
				item.VerticalLeaf = true;
				item.VerticalColumn = 1;
				var options = CloneVerticalItem(item, "ContextMenu" + type + "Options", "");
				options.VerticalLeaf = false;
				options.VerticalColumn = 2;
				options.VerticalWidth = VerticalButtonWidth;
				options.VerticalSubItems = children;
				options.OnConfirmed.AddListener(item.OnConfirmed.Invoke);
				return options;
			}
			var siMore = Derived(si, ArcCurveType.Si, Find(combined, "SISI"), Find(combined, "SISO"));
			var soMore = Derived(so, ArcCurveType.So, Find(combined, "SOSI"), Find(combined, "SOSO"));
			arc.VerticalLeaf = true;

			// Common colors are direct choices; the remaining colors share a submenu.
			var colors = new MarkingMenuItem[ArcColors.BlackId + 1];
			for (int id = 0; id < colors.Length; id++)
			{
				int value = id;
				colors[id] = CloneVerticalItem(arc, "ContextMenuArcColor" + id, AdeClickToCreate.ArcColorName(id));
				colors[id].OnConfirmed.AddListener(() => create.SetArcColor(value));
				colors[id].IconKind = () => create.ArcColor == value ? "check" : null;
			}
			var colorMenu = CloneVerticalItem(arc, "ContextMenuArcColors", "");
			colorMenu.VerticalLeaf = false;
			colorMenu.VerticalWidth = VerticalButtonWidth;
			colorMenu.VerticalSubItems = new[] { colors[ArcColors.GreenId], colors[ArcColors.GrayId], colors[ArcColors.BlackId] };
			colors[ArcColors.BlueId].VerticalColumn = 1;
			colors[ArcColors.RedId].VerticalColumn = 2;
			colorMenu.VerticalColumn = 3;

			arcMoreItem = CloneVerticalItem(arc, "ContextMenuArcOptions", "");
			arcMoreItem.OnConfirmed.AddListener(arc.OnConfirmed.Invoke);
			arcMoreItem.VerticalLeaf = false;
			arcMoreItem.VerticalWidth = VerticalButtonWidth;
			var items = new List<MarkingMenuItem> { isVoid, colors[ArcColors.BlueId], colors[ArcColors.RedId], colorMenu, s, b, si, siMore, so, soMore };
			items.RemoveAll(item => !item);
			arcMoreItem.VerticalSubItems = items.ToArray();
		}

		private static MarkingMenuItem CloneVerticalItem(MarkingMenuItem template, string name, string text)
		{
			var item = Instantiate(template, template.transform.parent);
			item.name = name;
			// Runtime decorations cloned from the template would be orphaned; the clone builds its own.
			for (int i = item.transform.childCount - 1; i >= 0; i--)
			{
				var child = item.transform.GetChild(i);
				if (child.name == "ContextMenuIcon" || child.name == "SubmenuArrow" || child.name == "ContextMenuRowDivider")
					DestroyImmediate(child.gameObject);
			}
			item.StartupText = text;
			item.HasSubMenu = false;
			item.SubItems = System.Array.Empty<MarkingMenuItem>();
			item.OnHangOver = new UnityEvent();
			item.OnConfirmed = new UnityEvent();
			item.Text = text;
			return item;
		}
	}
}

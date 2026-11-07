using System.Collections.Generic;
using UnityEngine;
using Arcade.Util.UnityExtension;
using Arcade.Compose.MarkingMenu;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Arcade.Compose
{
	public abstract class AdeMarkingMenuItemProvider : MonoBehaviour
	{
		public virtual bool IsOnlyMarkingMenu { get => false; }
		public virtual MarkingMenuItem[] MarkingMenuItems { get => null; }
	}
	public partial class AdeMarkingMenuManager : MonoBehaviour
	{
		public static AdeMarkingMenuManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		public static bool BlocksEditorInput => Instance && (Instance.isOn || Instance.consumedFrame == Time.frameCount);
		public bool IsOpen => isOn;
		internal const int MenuSortingOrder = 30;

		public MarkingMenuLine Line;
		public List<AdeMarkingMenuItemProvider> Providers = new List<AdeMarkingMenuItemProvider>();

		private readonly Stack<MenuPage> shownItems = new Stack<MenuPage>();
		private readonly List<MarkingMenuItem> parentItems = new List<MarkingMenuItem>();
		private bool verticalStyle;
		private struct Placement
		{
			public Vector2 Position;
			public float Degree;
		}
		private sealed class MenuPage
		{
			public readonly MarkingMenuItem[] Items;
			public readonly Vector2 Origin;
			public readonly Placement[] Layout;
			public MenuPage(MarkingMenuItem[] items, Vector2 origin)
			{
				Items = items; Origin = origin;
				Layout = new Placement[items.Length];
				for (int i = 0; i < items.Length; i++) Layout[i] = new Placement { Position = items[i].Position, Degree = items[i].Degree };
			}
		}
		private GameObject inputBlocker;
		private MarkingMenuItem backItem;
		private ArcChart openedChart;
		private bool keepOpen;
		private int consumedFrame = -1;

		private bool isOn = false;
		private void Awake()
		{
			Instance = this;
		}
		private void Update()
		{
			var mouse = Mouse.current;
			if (mouse == null) return;
			var keyboard = Keyboard.current;
			bool dialogOpen = AdeDialogManager.Instance && AdeDialogManager.Instance.Opening.childCount > 0;
			if (isOn && (!ArcadeComposeManager.Instance.IsEditorMode || dialogOpen
				|| openedChart != ArcGameplayManager.Instance.Chart))
			{
				Exit();
				return;
			}
			if (isOn && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
			{
				Exit();
				return;
			}
			if (mouse.rightButton.wasPressedThisFrame)
			{
				if (isOn) Exit();
				else if (!dialogOpen && ArcadeComposeManager.Instance.IsEditorMode && PointerOverChart())
					Enter();
				return;
			}
			if (!isOn) return;
			if (keepOpen)
			{
				if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame) ReturnParentMenu();
				else if (verticalStyle) CheckVertical();
				return;
			}
			if (!mouse.rightButton.isPressed)
			{
				var target = PointerTarget();
				var selected = target ? target.GetComponentInParent<MarkingMenuItem>() : null;
				if (verticalStyle ? VerticalPageDepth(selected) < 0 : System.Array.IndexOf(shownItems.Peek().Items, selected) < 0) selected = null;
				Exit();
				if (selected) selected.OnConfirmed.Invoke();
			}
			else Check();
		}

		private void LateUpdate()
		{
			// Retain the raycast shield through the dismissal click, including its release.
			if (!isOn && inputBlocker && consumedFrame != Time.frameCount
				&& (Mouse.current == null || !Mouse.current.leftButton.isPressed))
				inputBlocker.SetActive(false);
		}

		private void OnApplicationFocus(bool focused)
		{
			if (!focused) Exit();
		}

		private void OnDisable()
		{
			Exit();
			if (inputBlocker) inputBlocker.SetActive(false);
		}

		private void OnDestroy()
		{
			if (inputBlocker) Destroy(inputBlocker);
			foreach (var panel in menuPanels) if (panel) Destroy(panel);
			if (backItem) Destroy(backItem.gameObject);
			if (Instance == this) Instance = null;
		}

		public void Enter()
		{
			if (isOn) return;
			var items = GetCurrentMenuItems();
			if (items.Length == 0) return;
			verticalStyle = ArcadeComposeManager.Instance.ArcadePreference.VerticalContextMenu;
			if (verticalStyle) items = ArrangeVerticalItems(items);
			keepOpen = ArcadeComposeManager.Instance.ArcadePreference.VerticalContextMenu
				&& ArcadeComposeManager.Instance.ArcadePreference.KeepContextMenuOpen;
			openedChart = ArcGameplayManager.Instance.Chart;
			CreateInputBlocker();
			inputBlocker.SetActive(true);
			EventSystem.current.SetSelectedGameObject(null);
			isOn = true;
			Line.Enable = !verticalStyle;
			if (!verticalStyle) Line.AddJoint();
			ShowAt(InputExtension.scaledMousePosition, items);
		}

		private static bool PointerOverChart()
		{
			var target = PointerTarget();
			return target && target.GetComponentInParent<AdeGameplayContentInputHandler>();
		}

		private static GameObject PointerTarget()
		{
			// Raycast this position rather than using the previous frame's hover state.
			var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
			var hits = new List<RaycastResult>();
			EventSystem.current.RaycastAll(pointer, hits);
			return hits.Count > 0 ? hits[0].gameObject : null;
		}

		// Closing never confirms a highlighted item. Confirmation is an explicit action.
		public void Exit()
		{
			if (!isOn) return;
			consumedFrame = Time.frameCount;
			isOn = false;
			Line.Enable = false;
			foreach (var page in shownItems)
				foreach (var item in page.Items)
					item.Hide();
			shownItems.Clear();
			foreach (var parent in parentItems) parent.Expanded = false;
			parentItems.Clear();
			if (backItem) backItem.Hide();
			openedChart = null;
			foreach (var panel in menuPanels) if (panel) panel.SetActive(false);
		}

		public void ClickItem(MarkingMenuItem item)
		{
			if (!isOn || !keepOpen) return;
			if (verticalStyle)
			{
				int depth = VerticalPageDepth(item);
				if (depth < 0) return;
				if (item.KeepsMenuOpen)
				{
					CloseVerticalSubMenus(depth);
					item.OnConfirmed.Invoke();
					foreach (var page in shownItems)
						foreach (var shown in page.Items) shown.RefreshVerticalState();
					return;
				}
				if (item.HasVerticalSubMenu && !item.ClickConfirms)
				{
					// Hover usually opened it already; a click only makes sure it is open.
					if (depth >= parentItems.Count || parentItems[depth] != item)
					{
						CloseVerticalSubMenus(depth);
						ShowVerticalSubMenu(item);
					}
					return;
				}
				CloseVerticalSubMenus(depth);
			}
			else
			{
				if (item == backItem) { ReturnParentMenu(); return; }
				if (System.Array.IndexOf(shownItems.Peek().Items, item) < 0) return;
				if (item.HasSubMenu && item.SubItems.Length > 0)
				{
					parentItems.Add(item);
					ShowSubMenu(item.SubItems);
					ShowBackItem(item);
					return;
				}
			}
			// Arc/ArcTap parents select a creation mode. Defer that until a leaf is chosen,
			// so browsing a submenu and cancelling cannot change the current tool.
			var parents = parentItems.ToArray();
			Exit();
			foreach (var parent in parents) parent.OnConfirmed.Invoke();
			item.OnConfirmed.Invoke();
		}

		private void CreateInputBlocker()
		{
			if (inputBlocker) return;
			var root = ArcadeComposeManager.Instance.EditorCanvas.GetComponent<Canvas>();
			inputBlocker = new GameObject("ContextMenuInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(Button));
			inputBlocker.layer = root.gameObject.layer;
			var rect = (RectTransform)inputBlocker.transform;
			rect.SetParent(root.transform, false);
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = rect.offsetMax = Vector2.zero;
			var canvas = inputBlocker.GetComponent<Canvas>();
			canvas.overrideSorting = true;
			canvas.sortingLayerID = root.sortingLayerID;
			canvas.sortingOrder = MenuSortingOrder - 1;
			var image = inputBlocker.GetComponent<Image>();
			image.color = Color.clear;
			var button = inputBlocker.GetComponent<Button>();
			button.targetGraphic = image;
			button.transition = Selectable.Transition.None;
			button.navigation = new Navigation { mode = Navigation.Mode.None };
			button.onClick.AddListener(Exit);
		}

		private void ShowBackItem(MarkingMenuItem template)
		{
			if (!backItem)
			{
				backItem = Instantiate(template, template.transform.parent);
				backItem.name = "ContextMenuBack";
				backItem.StartupText = "返回";
				backItem.HasSubMenu = false;
				backItem.OnConfirmed = new UnityEvent();
				backItem.OnHangOver = new UnityEvent();
				backItem.SubItems = System.Array.Empty<MarkingMenuItem>();
				backItem.Text = backItem.StartupText;
			}
			backItem.Position = template.Position;
			backItem.SetIcon("back");
			backItem.Show(verticalStyle);
		}

		public void Check()
		{
			if (verticalStyle)
			{
				CheckVertical();
				return;
			}
			foreach (var item in shownItems.Peek().Items)
			{
				if (item.PointerHangOver)
				{
					if (item.HasSubMenu)
					{
						item.OnHangOver.Invoke();
						item.OnConfirmed.Invoke();
						ShowSubMenu(item.SubItems);
						return;
					}
				};
			}
		}
		private void CheckVertical()
		{
			int depth = shownItems.Count - 1;
			foreach (var page in shownItems)
			{
				foreach (var item in page.Items)
				{
					if (!item.PointerHangOver) continue;
					if (depth < parentItems.Count && parentItems[depth] == item) return;
					CloseVerticalSubMenus(depth);
					if (item.HasVerticalSubMenu)
					{
						// Click mode defers parent confirmation until a leaf is chosen (see ClickItem).
						if (!keepOpen)
						{
							item.OnHangOver.Invoke();
							item.OnConfirmed.Invoke();
						}
						ShowVerticalSubMenu(item);
					}
					return;
				}
				depth--;
			}
		}
		private int VerticalPageDepth(MarkingMenuItem item)
		{
			int depth = shownItems.Count - 1;
			foreach (var page in shownItems)
			{
				if (System.Array.IndexOf(page.Items, item) >= 0) return depth;
				depth--;
			}
			return -1;
		}
		private void ShowVerticalSubMenu(MarkingMenuItem parent)
		{
			parentItems.Add(parent);
			parent.Expanded = true;
			ShowVerticalPage(Vector2.zero, parent.VerticalSubItems ?? parent.SubItems, parent);
		}
		// Closes the cascaded submenus opened from the page at this depth.
		private void CloseVerticalSubMenus(int depth)
		{
			while (shownItems.Count > depth + 1)
			{
				foreach (var item in shownItems.Pop().Items) item.Hide();
				menuPanels[shownItems.Count].SetActive(false);
				var parent = parentItems[parentItems.Count - 1];
				parent.Expanded = false;
				parentItems.RemoveAt(parentItems.Count - 1);
			}
		}
		public void ShowSubMenu(MarkingMenuItem[] items, float from = 0, float to = 360)
		{
			if (!verticalStyle) Line.AddJoint();
			var parent = shownItems.Peek().Items;
			foreach (var item in parent) item.Hide();
			if (backItem) backItem.Hide();
			ShowAt(InputExtension.scaledMousePosition, items, from, to);
		}
		public void ReturnParentMenu()
		{
			if (!isOn || shownItems.Count <= 1) return;
			consumedFrame = Time.frameCount;
			if (verticalStyle)
			{
				CloseVerticalSubMenus(shownItems.Count - 2);
				return;
			}
			foreach (var item in shownItems.Pop().Items) item.Hide();
			var page = shownItems.Peek();
			for (int i = 0; i < page.Items.Length; i++)
			{
				page.Items[i].Show(verticalStyle);
				page.Items[i].Position = page.Layout[i].Position;
				page.Items[i].Degree = page.Layout[i].Degree;
			}
			if (!verticalStyle) Line.RemoveLastJoint();
			if (parentItems.Count > 0) parentItems.RemoveAt(parentItems.Count - 1);
			if (parentItems.Count > 0) ShowBackItem(parentItems[parentItems.Count - 1]);
			else if (backItem) backItem.Hide();
		}
	}
}

using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using UnityEngine.EventSystems;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Arcade.Compose.MarkingMenu
{
	public class MarkingMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
	{
		public CanvasGroup ItemCanvasGroup;
		public RectTransform ItemRectTransform;
		public Text ItemText;
		public Image Background;
		public Color InColor, OutColor;

		[Header("Settings")]
		public string StartupText;
		public UnityEvent OnConfirmed = new UnityEvent();

		[Header("Sub Menu")]
		public bool HasSubMenu;
		public float EnterTimeThreshold = 0.4f;
		// Vertical menus cascade on hover, so the delay only absorbs rows crossed in passing.
		private const float VerticalHoverDelay = 0.2f;
		public MarkingMenuItem[] SubItems;
		public UnityEvent OnHangOver = new UnityEvent();

		// Vertical-only runtime overrides; the ring menu keeps the serialized structure.
		internal MarkingMenuItem[] VerticalSubItems;
		internal bool VerticalLeaf;
		internal bool KeepsMenuOpen;
		internal bool ClickConfirms;
		internal int VerticalColumn;
		internal float VerticalWidth;
		internal string VerticalCaption;
		internal System.Func<string> IconKind;
		internal bool HasVerticalSubMenu => !VerticalLeaf && (VerticalSubItems ?? (HasSubMenu ? SubItems : null))?.Length > 0;

		public float Degree { get; set; }
		public string Text
		{
			get
			{
				return ItemText.text;
			}
			set
			{
				caption = value;
				ItemText.text = value + (HasSubMenu && !verticalStyle ? " ·" : "");
			}
		}
		public Vector2 Position
		{
			get
			{
				return ItemRectTransform.anchoredPosition;
			}
			set
			{
				ItemRectTransform.anchoredPosition = value;
			}
		}

		public bool PointerInItem { get; private set; }
		// Keeps a vertical row highlighted while its cascaded submenu is open.
		internal bool Expanded
		{
			get => expanded;
			set { expanded = value; ApplyTheme(); }
		}
		public bool PointerHangOver
		{
			get
			{
				return PointerInItem && pointerInItemTiming > (verticalStyle ? VerticalHoverDelay : EnterTimeThreshold);
			}
		}
		private float pointerInItemTiming;
		private bool active = false;
		private bool initialized;
		private bool verticalStyle;
		private bool expanded;
		private string caption;
		private Image separator;
		private ContextMenuIcon icon;
		private Text chevron;
		private MotionHandle fadeMotion;
		private int themeRevision = -1;

		private void Start()
		{
			InitializeText();
			ApplyTheme();
		}
		private void InitializeText()
		{
			if (initialized) return;
			Text = StartupText;
			initialized = true;
		}
		private void OnDestroy()
		{
			fadeMotion.TryCancel();
		}
		public void Show(bool vertical = false, int depth = 0)
		{
			gameObject.SetActive(true);
			verticalStyle = vertical;
			InitializeText();
			Text = caption;
			if (vertical && VerticalCaption != null) ItemText.text = VerticalCaption;
			if (vertical && IconKind != null) SetIcon(IconKind());
			var layout = GetComponent<VerticalLayoutGroup>();
			layout.childAlignment = vertical ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
			layout.childForceExpandWidth = vertical;
			var padding = layout.padding;
			padding.top = padding.bottom = vertical ? 4 : 12;
			padding.left = vertical ? 48 : 12;
			padding.right = vertical ? 28 : 12;
			if (icon) icon.gameObject.SetActive(vertical);
			bool showChevron = vertical && HasVerticalSubMenu;
			if (showChevron && !chevron)
			{
				var go = new GameObject("SubmenuArrow", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI), typeof(LayoutElement));
				go.layer = gameObject.layer;
				go.transform.SetParent(transform, false);
				go.GetComponent<LayoutElement>().ignoreLayout = true;
				chevron = go.GetComponent<Text>();
				AdeEditorText.Configure(chevron, AdeEditorText.CaptionSize);
				chevron.alignment = TMPro.TextAlignmentOptions.Center;
				chevron.raycastTarget = false;
				AdeUiIcons.SetText(chevron, AdeUiIcons.Expand);
				// 24px from the right edge, which also centres it in a 48px submenu button.
				var rect = chevron.rectTransform;
				rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
				rect.anchoredPosition = new Vector2(-24, 0);
				rect.sizeDelta = new Vector2(24, 24);
			}
			if (chevron) chevron.gameObject.SetActive(showChevron);
			if (separator) separator.gameObject.SetActive(false);
			var fitter = GetComponent<ContentSizeFitter>();
			fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			active = true;
			ItemCanvasGroup.blocksRaycasts = true;
			GetComponent<Canvas>().sortingOrder = AdeMarkingMenuManager.MenuSortingOrder + (vertical ? 1 + depth * 2 : 0);
			fadeMotion.TryComplete();
			ApplyTheme();
			LayoutRebuilder.ForceRebuildLayoutImmediate(ItemRectTransform);
			fadeMotion = LMotion.Create(ItemCanvasGroup.alpha, 1f, 0.15f)
				.WithEase(Ease.OutQuad).WithScheduler(MotionScheduler.Update)
				.BindToAlpha(ItemCanvasGroup);
		}
		internal void SetIcon(string kind)
		{
			if (!icon)
			{
				var go = new GameObject("ContextMenuIcon", typeof(RectTransform), typeof(ContextMenuIcon), typeof(LayoutElement));
				go.layer = gameObject.layer;
				go.transform.SetParent(transform, false);
				go.GetComponent<LayoutElement>().ignoreLayout = true;
				icon = go.GetComponent<ContextMenuIcon>();
				icon.raycastTarget = false;
				var rect = icon.rectTransform;
				rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
				rect.anchoredPosition = new Vector2(24, 0);
				rect.sizeDelta = new Vector2(24, 24);
			}
			icon.Kind = kind;
			icon.SetVerticesDirty();
			icon.gameObject.SetActive(verticalStyle);
		}
		internal void RefreshVerticalState()
		{
			if (IconKind != null) SetIcon(IconKind());
			ApplyTheme();
		}
		internal void SetRowSize(float width, float height, bool dividerAfter = true)
		{
			var fitter = GetComponent<ContentSizeFitter>();
			fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
			ItemRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
			ItemRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
			if (!separator)
			{
				var go = new GameObject("ContextMenuRowDivider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
				go.layer = gameObject.layer;
				go.transform.SetParent(transform, false);
				go.GetComponent<LayoutElement>().ignoreLayout = true;
				separator = go.GetComponent<Image>();
				separator.raycastTarget = false;
				var rect = separator.rectTransform;
				rect.anchorMin = Vector2.zero;
				rect.anchorMax = new Vector2(1, 0);
				rect.offsetMin = new Vector2(12, 0);
				rect.offsetMax = new Vector2(-12, 1);
			}
			separator.color = AdeUiTheme.Current.Border;
			separator.gameObject.SetActive(dividerAfter);
			LayoutRebuilder.ForceRebuildLayoutImmediate(ItemRectTransform);
		}
		public void Hide()
		{
			active = false;
			expanded = false;
			ItemCanvasGroup.blocksRaycasts = false;
			PointerInItem = false;
			pointerInItemTiming = 0;
			fadeMotion.TryComplete();
			if (verticalStyle)
			{
				ItemCanvasGroup.alpha = 0;
				gameObject.SetActive(false);
				return;
			}
			fadeMotion = LMotion.Create(ItemCanvasGroup.alpha, 0f, 0.15f)
				.WithEase(Ease.OutQuad).WithScheduler(MotionScheduler.Update)
				.WithOnComplete(() =>
				{
					ApplyTheme();
					if (!active)
					{
						gameObject.SetActive(false);
					}
				})
				.BindToAlpha(ItemCanvasGroup);
		}

		public void OnPointerEnter(PointerEventData data)
		{
			if (active)
			{
				PointerInItem = true;
				ApplyTheme();
				pointerInItemTiming = 0;
			}
		}
		public void OnPointerExit(PointerEventData data)
		{
			if (active)
			{
				PointerInItem = false;
				ApplyTheme();
				pointerInItemTiming = 0;
			}
		}
		public void OnPointerClick(PointerEventData data)
		{
			if (active && data.button == PointerEventData.InputButton.Left)
				AdeMarkingMenuManager.Instance.ClickItem(this);
		}
		private void ApplyTheme()
		{
			if (!AdeUiTheme.Instance)
			{
				Background.color = PointerInItem ? InColor : OutColor;
				return;
			}
			themeRevision = AdeUiTheme.Revision;
			Background.sprite = verticalStyle ? null : AdeUiTheme.ContextMenuSurface(PointerInItem);
			Background.type = verticalStyle ? Image.Type.Simple : Image.Type.Sliced;
			Background.color = verticalStyle ? (PointerInItem || expanded ? AdeUiTheme.Current.Selection : Color.clear) : Color.white;
			Background.rectTransform.sizeDelta = Vector2.zero;
			AdeEditorText.Configure(ItemText, verticalStyle ? 26 : AdeEditorText.BodySize);
			ItemText.alignment = verticalStyle ? TMPro.TextAlignmentOptions.Left : TMPro.TextAlignmentOptions.Center;
			ItemText.color = AdeUiTheme.Current.Text;
			if (icon) icon.color = AdeUiTheme.Current.Muted;
			if (chevron) chevron.color = AdeUiTheme.Current.Muted;
			var shadow = Background.GetComponent<Shadow>();
			if (shadow)
			{
				shadow.enabled = !verticalStyle;
				shadow.effectColor = new Color(0, 0, 0, AdeUiTheme.Dark ? .24f : .12f);
			}
		}

		private void Update()
		{
			if (themeRevision != AdeUiTheme.Revision) ApplyTheme();
			if ((verticalStyle || Mouse.current.rightButton.isPressed) && PointerInItem)
			{
				pointerInItemTiming += Time.deltaTime;
			}
		}
	}
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using LitMotion;

namespace Arcade.Compose
{
    // A mouse click should not leave a toolbar command in keyboard-selection state.
    public sealed class AdeToolbarPointerFocus : MonoBehaviour, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private bool hovering;
        private float enteredAt;
        private GameObject tooltip;
        private MotionHandle tooltipFade;

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovering = true;
            enteredAt = Time.unscaledTime;
        }
        public void OnPointerExit(PointerEventData eventData) => ClearTooltip();
        public void OnPointerDown(PointerEventData eventData) => ClearTooltip();
        private void OnDisable() => ClearTooltip();
        private void OnDestroy() => ClearTooltip();
        private void ClearTooltip()
        {
            tooltipFade.TryCancel();
            hovering = false;
            if (tooltip) { tooltip.SetActive(false); Destroy(tooltip); tooltip = null; }
        }
        private static string Caption(string name)
        {
            switch (name)
            {
                case "Save": return "保存谱面";
                case "ExportSongList": return "导出 songlist";
                case "FileWatch": return "谱面文件实时同步";
                case "SaveMode": return "保存模式";
                case "Folder": return "打开谱面文件夹";
                case "Auto": return "自动播放";
                case "CloudRealtime": return "云端实时同步";
                case "CloudSave": return "保存到云端";
                case "Camera": return "自由相机";
                case "Setting": return "设置";
                case "OBS": return "录制模式";
                case "Grid": return "网格设置";
                case "Feedback": return "应用反馈";
                case "Volume": return "音量设置";
                case "Hotkeys": return "热键设置";
                case "Timing": return "Timing列表";
                case "Skin": return "外观设置";
                case "Check": return "谱面检查";
                case "Scripts": return "外置脚本";
                case "Account": return "账号";
                default: return null;
            }
        }
        private void Update()
        {
            if (!hovering || tooltip || Time.unscaledTime - enteredAt < .5f) return;
            string caption = Caption(gameObject.name);
            var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (caption == null || !canvas || Mouse.current == null) return;
            tooltip = new GameObject("ToolbarTooltip", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
            tooltip.layer = gameObject.layer;
            tooltip.transform.SetParent(canvas.transform, false);
            var overlay = tooltip.GetComponent<Canvas>();
            overlay.overrideSorting = true; overlay.sortingOrder = 200;
            overlay.sortingLayerID = canvas.sortingLayerID;
            var group = tooltip.GetComponent<CanvasGroup>();
            group.alpha = 0;
            tooltipFade = LMotion.Create(0f, 1f, .14f).WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).Bind(alpha => group.alpha = alpha);
            group.blocksRaycasts = false; group.interactable = false;
            var image = tooltip.GetComponent<Image>();
            image.sprite = AdeUiTheme.PanelSurface; image.type = Image.Type.Sliced;
            image.color = Color.white; image.raycastTarget = false;
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = gameObject.layer; labelObject.transform.SetParent(tooltip.transform, false);
            var label = labelObject.GetComponent<TMP_Text>();
            AdeEditorText.Configure(label, 18);
            label.text = caption; label.color = AdeUiTheme.Current.Text; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 4); labelRect.offsetMax = new Vector2(-10, -4);
            var rect = (RectTransform)tooltip.transform;
            Vector2 size = new Vector2(label.GetPreferredValues(caption).x + 20, 34);
            rect.sizeDelta = size;
            var canvasRect = (RectTransform)canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Mouse.current.position.ReadValue(),
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
            point += new Vector2(point.x > canvasRect.rect.center.x ? -size.x / 2 - 16 : size.x / 2 + 16, -size.y / 2 - 12);
            point.x = Mathf.Clamp(point.x, canvasRect.rect.xMin + size.x / 2 + 4, canvasRect.rect.xMax - size.x / 2 - 4);
            point.y = Mathf.Clamp(point.y, canvasRect.rect.yMin + size.y / 2 + 4, canvasRect.rect.yMax - size.y / 2 - 4);
            rect.localPosition = point;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var events = EventSystem.current;
            if (eventData.button == PointerEventData.InputButton.Left && events
                && events.currentSelectedGameObject == gameObject)
                events.SetSelectedGameObject(null);
        }
    }
}

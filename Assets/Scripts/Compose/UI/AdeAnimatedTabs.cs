using System;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // One track, sliding selection and label-sized underline for every segmented selector.
    internal sealed class AdeAnimatedTabs : IDisposable
    {
        private readonly Button[] buttons;
        private readonly RectTransform highlight;
        private readonly Image image, track, underline, parentImage;
        private readonly TMP_Text[] labels;
        private readonly Vector4[] baseMargins;
        private readonly Rect[] bounds;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly RectTransform parent;
        private MotionHandle motion;
        private int selected = -1;
        private float position;
        // Upward text offset shared by every label, so text plus underline fits inside the selection.
        private float textLift;
        private const float Inset = 3, UnderlineGap = 1, UnderlineHeight = 3, UnderlineMinBottom = 2;
        // Shared metrics so every selector (skin, scripts, Timing/SC/Camera, Timing/properties) looks the same.
        // 48-high segments leave a 42-high selection after the inset, close to the 44-high action buttons.
        private const float LabelSize = 20, ButtonHeight = 48, ButtonSpacing = 8;

        public AdeAnimatedTabs(params Button[] buttons)
        {
            this.buttons = buttons;
            parent = (RectTransform)buttons[0].transform.parent;
            parentImage = parent.GetComponent<Image>();
            labels = new TMP_Text[buttons.Length];
            baseMargins = new Vector4[buttons.Length];
            bounds = new Rect[buttons.Length];
            var background = new GameObject("SelectionTrack", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            background.layer = buttons[0].gameObject.layer;
            background.transform.SetParent(parent, false);
            background.transform.SetAsFirstSibling();
            background.GetComponent<LayoutElement>().ignoreLayout = true;
            track = background.GetComponent<Image>();
            track.raycastTarget = false; track.type = Image.Type.Sliced;
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(.5f, .5f);
            var go = new GameObject("SlidingSelection", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.layer = buttons[0].gameObject.layer;
            go.transform.SetParent(buttons[0].transform.parent, false);
            go.transform.SetSiblingIndex(1);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            highlight = go.GetComponent<RectTransform>();
            highlight.anchorMin = highlight.anchorMax = new Vector2(.5f, .5f);
            image = go.GetComponent<Image>(); image.raycastTarget = false; image.type = Image.Type.Sliced;
            var line = new GameObject("SelectionUnderline", typeof(RectTransform), typeof(Image));
            line.layer = go.layer; line.transform.SetParent(highlight, false);
            underline = line.GetComponent<Image>(); underline.raycastTarget = false;
            // Positioned under the label's descender line each refresh, so it never depends on button height.
            underline.rectTransform.anchorMin = underline.rectTransform.anchorMax = new Vector2(.5f, .5f);
            underline.rectTransform.pivot = new Vector2(.5f, 1);
            var row = parent.GetComponent<HorizontalLayoutGroup>();
            if (row) row.spacing = ButtonSpacing;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].transition = Selectable.Transition.None;
                var size = buttons[i].GetComponent<LayoutElement>();
                if (size) size.minHeight = size.preferredHeight = ButtonHeight;
                labels[i] = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (!labels[i]) continue;
                labels[i].enableAutoSizing = false;
                labels[i].fontSize = LabelSize;
                labels[i].alignment = TextAlignmentOptions.Center;
                baseMargins[i] = labels[i].margin;
            }
        }

        public void Select(int index)
        {
            if (selected == index) { Refresh(); return; }
            motion.TryCancel();
            bool first = selected < 0;
            selected = index;
            if (first) { position = index; Refresh(); return; }
            float from = position;
            motion = LMotion.Create(0f, 1f, .26f)
                .WithEase(Ease.Linear).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(t =>
                {
                    float u = t - 1;
                    position = Mathf.LerpUnclamped(from, index, 1 + 1.65f * u * u * u + .65f * u * u);
                    Refresh();
                });
        }

        public void Refresh()
        {
            if (!highlight || selected < 0) return;
            if (parentImage) parentImage.color = Color.clear;
            track.sprite = AdeUiTheme.SegmentTrackSurface; track.color = Color.white;
            image.sprite = AdeUiTheme.RoundedSurface; image.color = AdeUiTheme.Current.Selection;
            image.pixelsPerUnitMultiplier = 1;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int i = 0; i < buttons.Length; i++)
            {
                ((RectTransform)buttons[i].transform).GetWorldCorners(corners);
                Vector2 lo = parent.InverseTransformPoint(corners[0]), hi = parent.InverseTransformPoint(corners[2]);
                bounds[i] = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
                min = Vector2.Min(min, lo); max = Vector2.Max(max, hi);
            }
            track.rectTransform.localPosition = (min + max) * .5f;
            track.rectTransform.sizeDelta = max - min;
            UpdateTextLift();
            int a = Mathf.Clamp(Mathf.FloorToInt(position), 0, buttons.Length - 2), b = a + 1;
            float t = position - a;
            highlight.localPosition = Vector2.LerpUnclamped(bounds[a].center, bounds[b].center, t);
            highlight.sizeDelta = Vector2.Max(Vector2.one, Vector2.LerpUnclamped(bounds[a].size, bounds[b].size, t) - Vector2.one * (Inset * 2));
            float widthA = labels[a] ? labels[a].GetPreferredValues(labels[a].text).x : 0;
            float widthB = labels[b] ? labels[b].GetPreferredValues(labels[b].text).x : 0;
            underline.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(Mathf.LerpUnclamped(widthA, widthB, t) * 1.25f,
                0, Mathf.Max(0, highlight.rect.width - 24)), 3);
            float bottom = -highlight.rect.height * .5f, lowest = bottom + UnderlineMinBottom + UnderlineHeight;
            float lineA = DescenderY(a), lineB = DescenderY(b);
            float lineY = float.IsNaN(lineA) || float.IsNaN(lineB) ? lowest
                : Mathf.LerpUnclamped(lineA, lineB, t) - UnderlineGap - highlight.localPosition.y;
            underline.rectTransform.anchoredPosition = new Vector2(0, Mathf.Max(lowest, lineY));
            underline.sprite = AdeUiTheme.PlainSurface;
            underline.color = AdeUiTheme.Dark ? Color.white : AdeUiTheme.Current.Text;
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.transition = Selectable.Transition.None;
                button.image.color = Color.clear;
                button.targetGraphic.CrossFadeColor(Color.white, 0, true, true);
                var label = labels[i];
                if (label) label.color = Color.Lerp(AdeUiTheme.Current.Muted, AdeUiTheme.Current.Text, Mathf.Clamp01(1 - Mathf.Abs(position - i)));
            }
        }

        // Font descender line of a label (bottom of g/p/y), in parent space; NaN when it has no laid-out text.
        private float DescenderY(int i)
        {
            var label = labels[i];
            if (!label) return float.NaN;
            if (label.textInfo == null || label.textInfo.lineCount == 0) label.ForceMeshUpdate(true);
            if (label.textInfo == null || label.textInfo.lineCount == 0) return float.NaN;
            Vector3 world = label.rectTransform.TransformPoint(new Vector3(0, label.textInfo.lineInfo[0].descender, 0));
            return parent.InverseTransformPoint(world).y;
        }

        // When a short selector leaves no room under descenders, raise all labels equally (via the TMP bottom
        // margin, which moves centred text up by half its value) so the underline never crosses the text.
        private void UpdateTextLift()
        {
            float need = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                float line = DescenderY(i);
                if (float.IsNaN(line)) continue;
                float lowestLine = bounds[i].yMin + Inset + UnderlineMinBottom + UnderlineHeight + UnderlineGap;
                need = Mathf.Max(need, lowestLine - (line - textLift));
            }
            need = Mathf.Min(Mathf.Ceil(need), 12);
            if (Mathf.Abs(need - textLift) < .5f) return;
            textLift = need;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!labels[i]) continue;
                var margin = baseMargins[i];
                margin.w += textLift * 2;
                labels[i].margin = margin;
                labels[i].ForceMeshUpdate(true);
            }
        }

        public void Dispose()
        {
            motion.TryCancel();
            for (int i = 0; i < labels.Length; i++) if (labels[i]) labels[i].margin = baseMargins[i];
            if (highlight) UnityEngine.Object.Destroy(highlight.gameObject);
            if (track) UnityEngine.Object.Destroy(track.gameObject);
        }
    }
}

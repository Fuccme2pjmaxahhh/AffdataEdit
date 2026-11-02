using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
    public sealed partial class AdeCloudManager
    {
        private const float ListWidth = 1120;
        private Text listSummary;
        private int listThemeRevision = -1;
        private readonly List<Image> listSurfaces = new List<Image>();
        private readonly List<Image> difficultySurfaces = new List<Image>();

        private void ConfigureListDialog()
        {
            AdeEditorText.Font.TryAddCharacters("\u2026");
            ((RectTransform)listDialog.View.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ListWidth);
            AdeUiKit.SetMaxHeight(listDialog, 720);
            var layout = listContent.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 20);
            layout.spacing = 16;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            listSummary = ListText(listContent, "Summary", "", 22, 36, true);
            listSummary.transform.SetAsFirstSibling();
            listStatus.fontSize = 22;
            listStatus.transform.parent.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset();
            var statusSize = listStatus.GetComponent<LayoutElement>();
            statusSize.minWidth = statusSize.preferredWidth = 0;
            statusSize.flexibleWidth = 1;
        }

        private void RebuildList()
        {
            foreach (Button button in openButtons)
            {
                if (!button) continue;
                button.transform.parent.gameObject.SetActive(false);
                Destroy(button.transform.parent.gameObject);
            }
            openButtons.Clear();
            listSurfaces.Clear();
            difficultySurfaces.Clear();
            coverQueue.Clear();
            listSummary.text = $"{AdeAccountManager.Username} · {charts.Count} 份谱面";
            foreach (CloudChart chart in charts) CreateChartRow(chart);
            listThemeRevision = -1;
            RefreshListAppearance();
            if (!coverLoading && coverQueue.Count > 0)
            {
                coverLoading = true;
                LoadListCovers().Forget();
            }
        }

        private void CreateChartRow(CloudChart chart)
        {
            var row = new GameObject($"Chart{chart.ArchiveId}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(listContent, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 24;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.GetComponent<Image>().raycastTarget = false;
            listSurfaces.Add(row.GetComponent<Image>());
            CreateListCover(row.transform, chart);

            var details = new GameObject("Details", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            details.transform.SetParent(row.transform, false);
            var detailsSize = details.GetComponent<LayoutElement>();
            detailsSize.minWidth = detailsSize.preferredWidth = 0;
            detailsSize.flexibleWidth = 1;
            var detailsLayout = details.GetComponent<VerticalLayoutGroup>();
            detailsLayout.spacing = 6;
            detailsLayout.childControlWidth = detailsLayout.childControlHeight = true;
            detailsLayout.childForceExpandWidth = true;
            detailsLayout.childForceExpandHeight = false;

            ListText(details.transform, "Metadata", $"#{chart.ArchiveId}  ·  {(chart.IsPrivate ? "私密谱面" : "公开谱面")}", 18, 26, true);
            var title = ListText(details.transform, "Title", chart.Title, 28, 40);
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.maxVisibleLines = 2;
            title.GetComponent<LayoutElement>().preferredHeight = Mathf.Clamp(title.GetPreferredValues(chart.Title, ListWidth - 380, 0).y, 40, 80);
            ListText(details.transform, "Artist", chart.Artist, 21, 30, true);
            CreateDifficultyLabels(details.transform, chart);

            var open = Instantiate(buttonRowTemplate.GetComponentInChildren<Button>(true), row.transform, false);
            open.name = "Open";
            var size = open.GetComponent<LayoutElement>() ?? open.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 120;
            size.minHeight = size.preferredHeight = 48;
            size.flexibleWidth = size.flexibleHeight = 0;
            var caption = open.GetComponentInChildren<Text>(true);
            caption.text = "打开";
            caption.fontSize = 24;
            caption.richText = false;
            open.interactable = !busy;
            AdeUiKit.SetOnClick(open, () => RequestOpenChart(chart));
            openButtons.Add(open);
        }

        private void CreateDifficultyLabels(Transform parent, CloudChart chart)
        {
            var row = new GameObject("Difficulties", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 32;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            foreach (int difficulty in chart.EnabledDiffs)
            {
                CloudDiff diff = chart.Diffs?.FirstOrDefault(item => item.Difficulty == difficulty);
                string name = string.IsNullOrWhiteSpace(diff?.DifficultyLabel) ? DifficultyNames[difficulty] : diff.DifficultyLabel;
                string caption = $"{name} {diff?.ChartDifficulty}{(chart.RankedDiffs.Contains(difficulty) ? " · Ranked" : "")}";
                var chip = new GameObject("Difficulty", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                chip.transform.SetParent(row.transform, false);
                var text = ListText(chip.transform, "DifficultyLabel", caption, 18, 32);
                text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(8, 0);
                text.rectTransform.offsetMax = new Vector2(-8, 0);
                chip.GetComponent<LayoutElement>().preferredWidth = Mathf.Clamp(text.GetPreferredValues(caption).x + 16, 84, 132);
                chip.GetComponent<LayoutElement>().preferredHeight = 32;
                chip.GetComponent<Image>().raycastTarget = false;
                difficultySurfaces.Add(chip.GetComponent<Image>());
            }
        }

        private Text ListText(Transform parent, string name, string caption, float fontSize, float height, bool muted = false)
        {
            var text = Instantiate(labelTemplate.GetComponent<Text>(), parent, false);
            text.name = name;
            AdeEditorText.Configure(text, fontSize);
            text.text = caption ?? "";
            text.color = new Color(AdeUiTheme.Current.Text.r, AdeUiTheme.Current.Text.g, AdeUiTheme.Current.Text.b, muted ? .65f : 1);
            var size = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 0;
            size.flexibleWidth = 1;
            size.preferredHeight = height;
            return text;
        }

        private void RefreshListAppearance()
        {
            if (listThemeRevision == AdeUiTheme.Revision) return;
            listThemeRevision = AdeUiTheme.Revision;
            foreach (var image in listSurfaces)
            {
                image.sprite = AdeUiTheme.ReadOnlySurface;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }
            foreach (var image in difficultySurfaces)
            {
                image.sprite = AdeUiTheme.RoundedSurface;
                image.type = Image.Type.Sliced;
                image.color = AdeUiTheme.Current.Input;
            }
            foreach (Button button in openButtons)
            {
                button.image.sprite = listDialog.CompleteButton.image.sprite;
                button.image.type = Image.Type.Sliced;
                button.image.color = Color.white;
                button.spriteState = listDialog.CompleteButton.spriteState;
                button.transition = Selectable.Transition.SpriteSwap;
                button.GetComponentInChildren<Text>(true).color = listDialog.ButtonText.color;
            }
        }
    }
}

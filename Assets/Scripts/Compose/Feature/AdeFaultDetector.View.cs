using System.Threading;
using Cysharp.Threading.Tasks;
using System.Globalization;
using System.Linq;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeFaultDetector
    {
        private void Render()
        {
            StopRendering();
            foreach (var row in rows) row.gameObject.SetActive(false);
            rendering = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RenderIssuesAsync(rendering).Forget();
        }

        private void StopRendering()
        {
            if (rendering == null) return;
            rendering.Cancel();
            rendering = null;
        }

        private async UniTask RenderIssuesAsync(CancellationTokenSource cancellation)
        {
            try
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (i >= rows.Count)
                    {
                        int index = i;
                        var row = Instantiate(issueTemplate, content, false);
                        row.name = "Issue";
                        row.onClick.AddListener(() => { if (index < issues.Count) Jump(issues[index]); });
                        rows.Add(row);
                    }
                    var item = issues[i];
                    rows[i].gameObject.SetActive(true);
                    rows[i].transform.Find("Reason").GetComponent<TMP_Text>().text = item.Reason;
                    var group = GroupOf(item.Event);
                    string location = item.Event.Timing.ToString(CultureInfo.InvariantCulture) + " ms · "
                        + (group == null ? "默认组" : "组 " + group.Id);
                    if (item.Related != null) location += " · 相关物件 " + item.Related.Timing.ToString(CultureInfo.InvariantCulture) + " ms";
                    rows[i].transform.Find("Location").GetComponent<TMP_Text>().text = location;
                    StyleRow(rows[i]);
                    if (i % 24 == 23) await UniTask.NextFrame(cancellation.Token);
                }
            }
            finally
            {
                if (rendering == cancellation) rendering = null;
                cancellation.Dispose();
            }
        }

        private void StyleRow(Button row)
        {
            row.image.sprite = AdeUiTheme.RoundedSurface;
            row.image.color = Color.white;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = AdeUiTheme.Current.Input;
            colors.highlightedColor = colors.selectedColor = AdeUiTheme.Current.Selection;
            colors.pressedColor = Color.Lerp(AdeUiTheme.Current.Selection, AdeUiTheme.Current.Text, .12f);
            row.colors = colors;
            row.transform.Find("Reason").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Text;
            row.transform.Find("Location").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Muted;
        }

        private ArcTimingGroup GroupOf(ArcEvent item) => item is IHasTimingGroup grouped ? grouped.TimingGroup
            : item is ArcTiming timing ? chart.TimingGroups.FirstOrDefault(g => g.Timings.Contains(timing)) : null;

        private void Jump(Issue issue)
        {
            if (chart != ArcGameplayManager.Instance.Chart) return;
            bool exists = issue.Event is ArcTap tap ? chart.Taps.Contains(tap)
                : issue.Event is ArcHold hold ? chart.Holds.Contains(hold)
                : issue.Event is ArcArc arc ? chart.Arcs.Contains(arc)
                : issue.Event is ArcArcTap at ? chart.Arcs.Contains(at.Arc) && at.Arc.ArcTaps.Contains(at)
                : issue.Event is ArcTiming timing && ArcTimingManager.Instance.GetTiming(GroupOf(timing)).Contains(timing);
            if (!exists) { Schedule(); return; }
            ArcadeComposeManager.Instance.SeekToChartTiming(issue.Event.Timing);
            AdeTimingEditor.Instance.SetCurrentTimingGroup(GroupOf(issue.Event));
            AdeSelectionManager.Instance.DeselectAllNotes();
            if (issue.Event is ArcNote note) AdeSelectionManager.Instance.SelectNote(note);
            else if (issue.Event is ArcTiming timingEvent) AdeChartToolsPanel.Instance.FocusTiming(timingEvent);
        }
    }
}

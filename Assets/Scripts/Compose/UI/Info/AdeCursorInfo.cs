using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Arcade.Compose.Editing;
using Arcade.Compose.Operation;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
	public class AdeCursorInfo : MonoBehaviour
	{
		private bool enableInfo;
		private int themeRevision = -1;

		public Text InfoText;
		public GameObject InfoGameObject;
		public bool EnableInfo
		{
			get
			{
				return enableInfo;
			}
			set
			{
				if (enableInfo != value)
				{
					InfoGameObject.SetActive(value);
					enableInfo = value;
				}
			}
		}

		private void ApplyTheme()
		{
			if (!AdeUiTheme.Instance || themeRevision == AdeUiTheme.Revision) return;
			var background = InfoGameObject.GetComponentInChildren<Image>(true);
			background.sprite = AdeUiTheme.PanelSurface;
			background.type = Image.Type.Sliced;
			background.color = Color.white;
			background.raycastTarget = false;
			background.rectTransform.sizeDelta = Vector2.zero;
			background.rectTransform.anchoredPosition = Vector2.zero;
			InfoGameObject.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
			AdeEditorText.Configure(InfoText, AdeEditorText.BodySize);
			InfoText.color = AdeUiTheme.Current.Text;
			themeRevision = AdeUiTheme.Revision;
		}

		void Update()
		{
			AdeCursorManager cursor = AdeCursorManager.Instance;
			EnableInfo = cursor.WallEnabled || cursor.TrackEnabled;
			string content = string.Empty;
			if (!EnableInfo) return;
			ApplyTheme();
			content += $"音乐时间: {(cursor.AttachedTiming + ArcGameplayManager.Instance.ChartAudioOffset).ToString(CultureInfo.InvariantCulture)}\n";
			content += $"谱面时间: {cursor.AttachedTiming.ToString(CultureInfo.InvariantCulture)}";
			if (cursor.WallEnabled)
			{
				Vector3 pos = cursor.AttachedWallPoint;
                float x = AdeDragEdit.Instance && AdeDragEdit.Instance.IsDraggingSlide ? .5f - pos.x / 8.5f : ArcAlgorithm.WorldXToArc(pos.x);
				content += $"\n坐标: ({x.ToString("f3", CultureInfo.InvariantCulture)},{ArcAlgorithm.WorldYToArc(pos.y).ToString("f3", CultureInfo.InvariantCulture)})";
			}
			if (AdeClickToCreate.Instance.Enable && AdeClickToCreate.Instance.Mode != ClickToCreateMode.Idle)
			{
				content += $"\n点立得: {AdeClickToCreate.Instance.Mode.ToString()}";
				if (AdeClickToCreate.Instance.Mode == ClickToCreateMode.Arc)
				{
					content += $"\n{AdeClickToCreate.Instance.CurrentArcColor}/{AdeClickToCreate.Instance.CurrentArcIsVoid}/{AdeClickToCreate.Instance.CurrentArcCurveType}";
				}
				if (AdeClickToCreate.Instance.Mode == ClickToCreateMode.ArcTap)
				{
					content += $"\n{AdeClickToCreate.Instance.CurrentArctapMode}";
				}
			}
			if (AdeSelectNoteOperation.Instance.RangeSelectPosition != null)
			{
				content += $"\n段落选择起点: {AdeSelectNoteOperation.Instance.RangeSelectPosition?.ToString(CultureInfo.InvariantCulture)}";
			}
			if (AdeSelectionManager.Instance.SelectedNotes.Count == 1 && AdeSelectionManager.Instance.SelectedNotes[0] is ArcArc arc)
			{
				if (arc.EndTiming - arc.Timing != 0)
				{
					float p = ((float)(cursor.AttachedTiming - arc.Timing)) / ((float)(arc.EndTiming - arc.Timing));
					if (p >= 0 && p <= 1)
					{
						float x = ArcAlgorithm.X(arc.XStart, arc.XEnd, p, arc.CurveType);
						float y = ArcAlgorithm.Y(arc.YStart, arc.YEnd, p, arc.CurveType);
						content += $"\nArc: {(p * 100).ToString("f2", CultureInfo.InvariantCulture)}%, {x.ToString("f3", CultureInfo.InvariantCulture)}, {y.ToString("f3", CultureInfo.InvariantCulture)}";
					}
				}
			}
			InfoText.text = content;
		}
	}
}

using System;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Arcade.Gameplay.Chart;
using Arcade.Compose.Command;

namespace Arcade.Compose.Editing
{
	public class AdeTimingItem : MonoBehaviour
	{
		public InputField ItemInputField;
		[System.NonSerialized]
		public ArcTiming TimingReference;
		public RectTransform RectTransform;
		public Button AddBtn, RemoveBtn;

		public string Text
		{
			get
			{
				return ItemInputField.text;
			}
			set
			{
				ItemInputField.text = value;
			}
		}
		public void Add()
		{
			AdeTimingEditor.Instance.AddTiming(TimingReference);
		}
		public void Delete()
		{
			AdeTimingEditor.Instance.RemoveTiming(TimingReference);
		}
		public void OnEndEdit()
		{
			if (TimingReference == null || Text == AdeTimingEditor.GetTimingString(TimingReference)) return;
			string[] parts = Text.Split(',');
			if (parts.Length != 3 || !ChartControlText.Integer(parts[0], out int time)
				|| !ChartControlText.Float(parts[1], out float bpm) || !ChartControlText.Float(parts[2], out float beats)
				|| beats < 0)
			{
				ItemInputField.SetTextWithoutNotify(AdeTimingEditor.GetTimingString(TimingReference));
				AdeToast.Instance?.Show("Timing 需要时间、BPM 和非负拍数，以逗号分隔");
				return;
			}
			if (time == TimingReference.Timing && bpm == TimingReference.Bpm && beats == TimingReference.BeatsPerLine) return;
			var next = (ArcTiming)TimingReference.Clone();
			next.Timing = time;
			next.Bpm = bpm;
			next.BeatsPerLine = beats;
			AdeCommandManager.Instance.Add(new EditTimingEvent(AdeTimingEditor.Instance.currentTimingGroup, TimingReference, next));
		}
	}
}

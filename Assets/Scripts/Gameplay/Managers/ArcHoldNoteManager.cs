using System.Collections.Generic;
using Affdata.Core.Geometry;
using UnityEngine;
using Arcade.Gameplay.Chart;

namespace Arcade.Gameplay
{
	public class ArcHoldNoteManager : MonoBehaviour
	{
		public static ArcHoldNoteManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		private void Awake()
		{
			Instance = this;
		}

		[HideInInspector]
		[System.NonSerialized]
		public List<ArcHold> Holds = new List<ArcHold>();
        internal readonly List<ArcHold> RenderingHolds = new List<ArcHold>();
        private readonly List<ArcHold> renderCandidates = new List<ArcHold>();
        private readonly ArcVisibleNoteIndex<ArcHold> renderIndex = new ArcVisibleNoteIndex<ArcHold>(n => n.EndTiming);
		[HideInInspector]
		public readonly float[] Lanes = { 10.625f, 6.375f, 2.125f, -2.125f, -6.375f, -10.625f };
		public GameObject HoldNotePrefab;
		public Transform NoteLayer;
		public Sprite DefaultSprite, HighlightSprite;
		public Material HoldNoteMatrial;

		public void Clean()
		{
			foreach (var t in Holds) t.Destroy();
			Holds.Clear();
            renderIndex.Clear(); renderCandidates.Clear(); RenderingHolds.Clear();
		}
		public void Load(List<ArcHold> holds)
		{
			Holds = holds;
			foreach (var t in Holds)
			{
				t.Instantiate();
			}
		}

		public void Add(ArcHold hold)
		{
			ArcScoreManager.Invalidate();
			hold.Instantiate();
			Holds.Add(hold);
		}
		public void Remove(ArcHold hold)
		{
			ArcScoreManager.Invalidate();
			hold.Destroy();
			Holds.Remove(hold);
		}

		private void Update()
		{
			if (Holds == null) return;
			if (ArcGameplayManager.Instance.Auto) JudgeHoldNotes();
			RenderHoldNotes();
		}

		private void RenderHoldNotes()
		{
            foreach (var note in RenderingHolds) note.Enable = false;
            RenderingHolds.Clear();
            renderIndex.Query(Holds, renderCandidates);
			ArcTimingManager timing = ArcTimingManager.Instance;
            Matrix4x4 parentMatrix = NoteLayer.localToWorldMatrix;
            Quaternion rotation = HoldNotePrefab.transform.localRotation;
            float widthScale = DefaultSprite ? NoteMeshDefinitions.HoldWidth / DefaultSprite.bounds.size.x : 1f;

			foreach (var t in renderCandidates)
			{
				int duration = t.EndTiming - t.Timing;
				if (!timing.ShouldTryRender(t.Timing, t.TimingGroup, duration, note: !t.IsEditing) || (t.Judged && !t.IsEditing) || t.GroupHide())
				{
					t.Enable = false;
					continue;
				}
				t.Position = timing.CalculatePositionByTiming(t.Timing, t.TimingGroup);
				float endPosition = timing.CalculatePositionByTiming(t.EndTiming, t.TimingGroup);
				if (!t.IsEditing && (t.Judging || (t.NoInput() && t.Timing < ArcGameplayManager.Instance.ChartTiming)))
				{
					t.Position = 0;
				}
				if (endPosition < t.Position)
				{
					var p = t.Position;
					t.Position = endPosition;
					endPosition = p;
				}
				if (t.Position > 100000 || endPosition < -100000)
				{
					t.Enable = false;
					continue;
				}
				if (endPosition > 100000)
				{
					endPosition = 100000;
				}
				if (t.Position < -100000)
				{
					t.Position = -100000;
				}
				t.Enable = true;
                RenderingHolds.Add(t);
				float pos = t.Position / 1000f;
				float length = (endPosition - t.Position) / 1000f;
                Vector3 position = new Vector3(t.WorldX, pos, 0);
                Vector3 scale = new Vector3(widthScale, length / 3.79f, 1);
                t.RenderMatrix = parentMatrix * Matrix4x4.TRS(position, rotation, new Vector3(-scale.x, scale.y, scale.z));

				float alpha = 1;
				if (t.Judging)
				{
					t.FlashCount = (t.FlashCount + 1) % 4;
					if (t.FlashCount == 0) alpha = 0.85f;
					t.Highlight = true;
				}
				else
				{
					if (t.Timing < ArcGameplayManager.Instance.ChartTiming)
					{
						if (!t.FadingHolds())
						{
							alpha = 0.5f;
						}
						else
						{
							int firstJudge = t.JudgeTimings.Count > 0 ? t.JudgeTimings[0] : t.Timing;
							if (firstJudge < ArcGameplayManager.Instance.ChartTiming)
							{
								alpha = 0.5f;
							}
						}
					}
					t.Highlight = false;
				}
				t.Alpha = alpha * 0.8627451f;
			}
		}
		private void JudgeHoldNotes()
		{
			ArcTimingManager timing = ArcTimingManager.Instance;
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			ArcEffectManager.Instance.ResetHoldNoteEffect();
			foreach (var t in Holds)
			{
                if (t.IsEditing)
                {
                    t.Judged = false;
                    t.Judging = false;
                    continue;
                }
				if (t.NoInput())
				{
					continue;
				}
				if (t.Judged) continue;
				if (currentTiming >= t.Timing && currentTiming <= t.EndTiming)
				{
					t.Judging = true;
					if (!t.AudioPlayed)
					{
						if (ArcGameplayManager.Instance.IsPlaying && t.ShouldPlayAudio) ArcEffectManager.Instance.PlayTapSound();
						t.AudioPlayed = true;
					}
					ArcEffectManager.Instance.SetHoldNoteEffect(t);
					ArcEffectManager.Instance.ShowLongNoteHitType(t, new Vector3(t.WorldX, 0, 0));
				}
				else if (currentTiming > t.EndTiming)
				{
					t.Judging = false;
					t.Judged = true;
					t.AudioPlayed = true;
				}
				else
				{
					t.ShouldPlayAudio = true;
				}
			}
		}
		public void SetHoldNoteSkin(Sprite normal, Sprite highlight)
		{
			HoldNoteMatrial.mainTexture = normal.texture;
			DefaultSprite = normal;
			HighlightSprite = highlight;
		}
	}
}

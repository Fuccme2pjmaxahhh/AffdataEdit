using System.Collections.Generic;
using UnityEngine;
using Arcade.Gameplay.Chart;
using System;

namespace Arcade.Gameplay
{
	public class ArcArcManager : MonoBehaviour
	{
		public static ArcArcManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

        // Visible Arcs borrow plain render state objects; no note owns a GameObject.
        private const int MaxIdleVisuals = 256;
        private readonly Stack<ArcArcVisual> idleVisuals = new Stack<ArcArcVisual>();
        private ArcArcRenderer arcSkin;
        internal ArcArcRenderer ArcSkin => arcSkin ? arcSkin : arcSkin = ArcNotePrefab.GetComponent<ArcArcRenderer>();
        internal ArcArcVisual RentVisual() => idleVisuals.Count > 0 ? idleVisuals.Pop() : new ArcArcVisual();
        internal void ReturnVisual(ArcArcVisual visual)
        {
            if (idleVisuals.Count < MaxIdleVisuals) idleVisuals.Push(visual);
        }
        // Sorting, material and rotation of the ArcTap shadow, read from the (never instantiated) prefab.
        internal SpriteRenderer ArcTapShadowStyle(bool sfx) => (sfx ? SfxArcTapPrefab : ArcTapPrefab).GetComponentInChildren<SpriteRenderer>();

		private void Awake()
		{
			Instance = this;
		}

		[HideInInspector]
		[System.NonSerialized]
		public List<ArcArc> Arcs = new List<ArcArc>();
        internal readonly List<ArcArc> RenderingArcs = new List<ArcArc>();
        internal readonly List<ArcArcTap> RenderingArcTaps = new List<ArcArcTap>();
        private readonly List<ArcArc> renderCandidates = new List<ArcArc>(), flaggedArcs = new List<ArcArc>();
        private readonly List<ArcArcTap> allArcTaps = new List<ArcArcTap>(), arcTapCandidates = new List<ArcArcTap>();
        private readonly ArcVisibleNoteIndex<ArcArc> renderIndex = new ArcVisibleNoteIndex<ArcArc>(n => Math.Max(n.EndTiming, n.RenderEndTiming));
        private readonly ArcVisibleNoteIndex<ArcArcTap> arcTapIndex = new ArcVisibleNoteIndex<ArcArcTap>(n => n.Timing);
        private long arcTapVersion = -1;
        private readonly ArcJudgementCursor<ArcArcTap> judgement = new ArcJudgementCursor<ArcArcTap>();
        private readonly List<ArcArcTap> crossedArcTaps = new List<ArcArcTap>();
        // Hit sounds run on their own cursor at SoundChartTiming (ahead of the drawn time by the visual delay).
        private readonly ArcJudgementCursor<ArcArcTap> soundJudgement = new ArcJudgementCursor<ArcArcTap>();
        private readonly List<ArcArcTap> soundCrossedArcTaps = new List<ArcArcTap>();
        internal void ResetJudgement() { judgement.Reset(); soundJudgement.Reset(); }
        private readonly HashSet<ArcArc> candidateArcs = new HashSet<ArcArc>();

		public GameObject ArcNotePrefab, ArcTapPrefab, SfxArcTapPrefab;
		public Transform ArcLayer;
		public Color ConnectionColor;
		public Sprite ArcTapShadowSkin;
		public Texture2D ArcTapSkin;
		public Material ArcTapMaterial;
		public Texture2D SfxArcTapNoteSkin;
		public Material SfxArcTapNoteMaterial;
		public Texture2D SfxArcTapCoreSkin;
		public Material SfxArcTapCoreMaterial;
		public Material ArcMaterial;

		public Color ArcRedLow;
		public Color ArcBlueLow;
		public Color ArcGreenLow;
		public Color ArcUnknownLow;
		public Color ArcRedHigh;
		public Color ArcBlueHigh;
		public Color ArcGreenHigh;
		public Color ArcUnknownHigh;
		public Color ArcVoid;
		public Color ArcDesignant;
		public Color ArcTapDesignant;
		public Color ShadowColor;
		public readonly float[] Lanes = { 10.625f, 6.375f, 2.125f, -2.125f, -6.375f, -10.625f };

		[HideInInspector]
		public float ArcJudgePosLeft, ArcJudgePosRight;

        internal void RecordJudgePosition(float worldX)
        {
            ArcJudgePosLeft = Mathf.Max(ArcJudgePosLeft, -worldX);
            ArcJudgePosRight = Mathf.Max(ArcJudgePosRight, worldX);
        }

		public void Clean()
		{
            RenderingArcs.Clear();
            RenderingArcTaps.Clear();
            renderIndex.Clear(); arcTapIndex.Clear(); allArcTaps.Clear(); candidateArcs.Clear();
            renderCandidates.Clear(); arcTapCandidates.Clear(); flaggedArcs.Clear(); arcTapVersion = -1; judgement.Clear(); crossedArcTaps.Clear(); soundJudgement.Clear(); soundCrossedArcTaps.Clear();
			foreach (var t in Arcs)
			{
				t.Destroy();
			};
			Arcs.Clear();
            idleVisuals.Clear();
		}
		public void Load(List<ArcArc> arcs)
		{
			Arcs = arcs;
            var taps = ArcTapNoteManager.Instance;
            try
            {
                taps.PrepareConnectionLookup();
                foreach (var t in Arcs) t.Instantiate();
            }
            finally
            {
                taps.ClearConnectionLookup();
            }
			CalculateArcRelationship();
		}

		public void CalculateArcRelationship()
		{
			CalculateArcGroups(Arcs);
			foreach (ArcArc arc in Arcs)
			{
				arc.CalculateJudgeTimings();
                if (arc.arcRenderer != null && arc.arcRenderer.BuiltRenderEndTiming != arc.RenderEndTiming)
                    arc.arcRenderer.Build();
			}
		}

        public void CalculateArcRelationship(ArcArc changed)
        {
            // A moved endpoint can split its old component or join another component.
            // Include both complete components before rebuilding so splits remain correct.
            var affected = new HashSet<ArcArc>();
            if (changed.ArcGroup != null) affected.UnionWith(changed.ArcGroup);
            bool present = Arcs.Contains(changed);
            if (present)
            {
                affected.Add(changed);
                foreach (var arc in Arcs)
                    if (Connected(changed, arc) || Connected(arc, changed))
                    {
                        if (arc.ArcGroup != null) affected.UnionWith(arc.ArcGroup);
                        else affected.Add(arc);
                    }
            }
            var ordered = new List<ArcArc>(affected.Count);
            foreach (var arc in Arcs) if (affected.Contains(arc)) ordered.Add(arc);
            CalculateArcGroups(ordered);
            foreach (var arc in ordered)
            {
                arc.CalculateJudgeTimings();
                if (arc.arcRenderer != null && arc.arcRenderer.BuiltRenderEndTiming != arc.RenderEndTiming)
                    arc.arcRenderer.Build();
            }
        }

        private static bool Connected(ArcArc a, ArcArc b) => a != b
            && !a.IsVariousSizedArctap && !b.IsVariousSizedArctap && a.IsVoid == b.IsVoid
            && Math.Abs((long)a.EndTiming - b.Timing) <= 9
            && Mathf.Abs(a.XEnd - b.XStart) < .1f && Mathf.Abs(a.YEnd - b.YStart) < .01f;

		internal static void CalculateArcGroups(List<ArcArc> arcs)
		{
			int count = arcs.Count;
			int[] byStartTiming = new int[count];
			// A root stores the negative group size; other entries store their parent index.
			int[] parents = new int[count];
			for (int i = 0; i < count; i++)
			{
				byStartTiming[i] = i;
				parents[i] = -1;
				arcs[i].RenderHead = true;
                arcs[i].RenderEndTiming = arcs[i].EndTiming;
			}
			Array.Sort(byStartTiming, (a, b) =>
			{
				int timingOrder = arcs[a].Timing.CompareTo(arcs[b].Timing);
				return timingOrder != 0 ? timingOrder : a.CompareTo(b);
			});

			for (int i = 0; i < count; i++)
			{
				ArcArc a = arcs[i];
				if (a.IsVariousSizedArctap) continue;
				long firstTiming = (long)a.EndTiming - 9;
				long lastTiming = (long)a.EndTiming + 9;
				int low = 0, high = count;
				while (low < high)
				{
					int middle = low + (high - low) / 2;
					if (arcs[byStartTiming[middle]].Timing < firstTiming) low = middle + 1;
					else high = middle;
				}

				for (int j = low; j < count; j++)
				{
					int nextIndex = byStartTiming[j];
					ArcArc b = arcs[nextIndex];
					if (b.Timing > lastTiming) break;
					if (a == b || b.IsVariousSizedArctap) continue;
					if (a.IsVoid == b.IsVoid && Mathf.Abs(a.XEnd - b.XStart) < 0.1f && Mathf.Abs(a.YEnd - b.YStart) < 0.01f)
					{
						b.RenderHead = false;
                        a.RenderEndTiming = Mathf.Max(a.RenderEndTiming, b.Timing);
						int rootA = FindArcGroupRoot(parents, i);
						int rootB = FindArcGroupRoot(parents, nextIndex);
						if (rootA == rootB) continue;
						if (parents[rootA] > parents[rootB])
						{
							int swap = rootA;
							rootA = rootB;
							rootB = swap;
						}
						parents[rootA] += parents[rootB];
						parents[rootB] = rootA;
					}
				}
			}

			// Walking the sorted indices builds every group in time order only once.
			var groups = new List<ArcArc>[count];
			foreach (int index in byStartTiming)
			{
				int root = FindArcGroupRoot(parents, index);
				if (groups[root] == null) groups[root] = new List<ArcArc>(-parents[root]);
				arcs[index].ArcGroup = groups[root];
				groups[root].Add(arcs[index]);
			}
		}

		private static int FindArcGroupRoot(int[] parents, int index)
		{
			int root = index;
			while (parents[root] >= 0) root = parents[root];
			while (index != root)
			{
				int next = parents[index];
				parents[index] = root;
				index = next;
			}
			return root;
		}

		public void Rebuild()
		{
			foreach (var t in Arcs) t.Rebuild();
			CalculateArcRelationship();
		}

		public void Add(ArcArc arc)
		{
			arc.Instantiate();
			Arcs.Add(arc);
			CalculateArcRelationship(arc);
			ArcScoreManager.Invalidate();
		}
		public void Remove(ArcArc arc)
		{
			Arcs.Remove(arc);
			CalculateArcRelationship(arc);
			arc.Destroy();
			ArcScoreManager.Invalidate();
		}

		private void Update()
		{
			if (Arcs == null) return;
			if (ArcGameplayManager.Instance.Auto) JudgeArcs();
			ArcJudgePosLeft = ArcJudgePosRight = 0;
			RenderArcs();
		}

		private void RenderArcs()
		{

            renderIndex.Query(Arcs, renderCandidates);
            candidateArcs.Clear();
            candidateArcs.UnionWith(renderCandidates);
            foreach (var arc in RenderingArcs) if (!candidateArcs.Contains(arc)) arc.Enable = false;
            foreach (var tap in RenderingArcTaps) tap.Enable = false;
            RenderingArcs.Clear();
            RenderingArcTaps.Clear();
			ArcTimingManager timingManager = ArcTimingManager.Instance;
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;

            RefreshArcTaps();
            arcTapIndex.Query(allArcTaps, arcTapCandidates);
            foreach (var tap in arcTapCandidates) RenderArcTap(tap);
            foreach (var t in renderCandidates)
            {
				int duration = t.EndTiming - t.Timing;
				if (!timingManager.ShouldTryRender(t.Timing, t.TimingGroup, duration, !t.IsVoid) || (t.Judged && !t.IsVoid) || t.GroupHide())
				{
					t.Enable = false;
					continue;
				}
				t.Position = timingManager.CalculatePositionByTiming(t.Timing, t.TimingGroup);
				t.EndPosition = timingManager.CalculatePositionByTiming(t.EndTiming, t.TimingGroup);
                // Trace geometry keeps the start timing's speed unless cross-timing is enabled.
                // Cull against that geometry, including the extension to its successor.
                if (t.IsVoid && !ArcArcRenderer.AllowCrossTiming)
                    t.EndPosition = t.Position + timingManager.CalculateVelocityAtTiming(t.Timing, t.TimingGroup)
                        * (Mathf.Max(t.EndTiming, t.RenderEndTiming) - t.Timing);
				if (Mathf.Min(t.Position, t.EndPosition) > 100000 || Mathf.Max(t.Position, t.EndPosition) < -100000)
				{
					t.Enable = false;
					continue;
				}
				t.Enable = true;
				t.arcRenderer.RootZ = -t.Position / 1000f;
				if (!t.IsVoid)
				{
					t.arcRenderer.EnableEffect = currentTiming > t.Timing && currentTiming <= t.EndTiming && !t.IsVoid && t.Judging;
                    if (!t.Flag) foreach (var a in t.ArcGroup)
					{
						if (!a.Flag)
						{
							a.Flag = true;
                            flaggedArcs.Add(a);
							float alpha = 1;

							if (a.Judging)
							{
								a.FlashCount = (a.FlashCount + 1) % 5;
								if (a.FlashCount == 0) alpha = 0.85f;
								a.RenderingHighlight = true;
							}
							else
							{
								if (t.ArcGroup.Count != 1 && currentTiming > t.Timing)
								{
									alpha = 0.65f;
								}
								a.RenderingHighlight = false;
							}
							a.RenderingAlpha = alpha * (225 / 255f);
						}
					}
				}
				else
				{
					t.arcRenderer.EnableEffect = false;
					bool customTrace = t.LineType == ArcLineType.TrueIsVoid && t.TimingGroup != null
                        && (t.TimingGroup.UseTraceColor || t.TimingGroup.TraceBodyGold);
                    t.arcRenderer.Highlight = !customTrace;
                    t.arcRenderer.Alpha = customTrace
                        ? 0.65f * ((t.TimingGroup.TraceBodyGold ? 188 : 125) / 255f)
                        : 0.85f * (125 / 255f);
				}
                if (!t.IsVoid)
                {
                    t.arcRenderer.Highlight = t.RenderingHighlight;
                    t.arcRenderer.Alpha = t.RenderingAlpha;
                }
				t.arcRenderer.UpdateArc();
                if (!t.IsVoid && t.Judging)
                    ArcEffectManager.Instance.ShowLongNoteHitType(t, t.arcRenderer.JudgePosition);
                RenderingArcs.Add(t);
			}
			foreach (var t in flaggedArcs) t.Flag = false;
            flaggedArcs.Clear();
		}

        private void RefreshArcTaps()
        {
            if (arcTapVersion != ArcEvent.RenderIndexVersion)
            {
                allArcTaps.Clear();
                foreach (var arc in Arcs)
                {
                    allArcTaps.AddRange(arc.ArcTaps);
                    if (arc.ConvertedVariousSizedArctap != null) allArcTaps.Add(arc.ConvertedVariousSizedArctap);
                }
                arcTapIndex.Clear();
                arcTapVersion = ArcEvent.RenderIndexVersion;
            }
        }

		private void RenderArcTap(ArcArcTap t)
		{
			ArcTimingManager timingManager = ArcTimingManager.Instance;

			if (!timingManager.ShouldTryRender(t.Timing, t.TimingGroup) || t.Judged || t.GroupHide())
			{
				t.Enable = false;
				return;
			}
			float pos = timingManager.CalculatePositionByTiming(t.Timing, t.TimingGroup) / 1000f;
			if (pos > -80 && pos <= 80)
			{
				t.Enable = true;
                t.Alpha = 240f / 255f;
				t.UpdatePosition();
			}
			else if (pos > 80 && pos <= 90)
			{
				t.Enable = true;
				t.Alpha = ArcadeAlphaAppearance.TapAlpha(pos) * (240f / 255f);
				t.UpdatePosition();
			}
			else
			{
				t.Enable = false;
			}
            if (t.Enable) RenderingArcTaps.Add(t);
		}

		private void JudgeArcs()
		{

			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
            RefreshArcTaps();
            int soundTiming = ArcGameplayManager.Instance.SoundChartTiming;
            soundJudgement.Advance(allArcTaps, soundTiming, soundCrossedArcTaps);
            foreach (var tap in soundCrossedArcTaps)
                if (!tap.Arc.NoInput() && !tap.Arc.Designant && soundTiming <= tap.Timing + 150 && ArcGameplayManager.Instance.IsPlaying)
                    ArcEffectManager.Instance.PlayArcTapSound(tap.Arc.Effect);
            judgement.Advance(allArcTaps, currentTiming, crossedArcTaps);
            foreach (var tap in crossedArcTaps)
                if (!tap.Arc.NoInput() && !tap.Arc.Designant) JudgeArcTap(tap);
			foreach (ArcArc arc in Arcs)
			{
				if (arc.NoInput() || arc.Designant)
				{
					continue;
				}
				// The sound follows the music's time (ahead of the drawn time by the visual delay).
				if (!arc.IsVoid && !arc.AudioPlayed && soundTiming > arc.Timing && soundTiming <= arc.EndTiming)
				{
					if (ArcGameplayManager.Instance.IsPlaying && arc.ShouldPlayAudio) ArcEffectManager.Instance.PlayArcSound();
					arc.AudioPlayed = true;
				}
				if (arc.Judged) continue;
				if (currentTiming > arc.EndTiming)
				{
					arc.Judged = true;
				}
				else if (currentTiming > arc.Timing && currentTiming <= arc.EndTiming)
				{
					foreach (var a in arc.ArcGroup) a.Judging = true;
				}
				else
				{
					arc.ShouldPlayAudio = true;
				}
			}
		}
		private void JudgeArcTap(ArcArcTap t)
		{
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			if (t.Judged) return;
			if (currentTiming > t.Timing && currentTiming <= t.Timing + 150)
			{
				t.Judged = true;
				if (ArcGameplayManager.Instance.IsPlaying) ArcEffectManager.Instance.PlayTapNoteEffectAt(new Vector2(t.LocalPosition.x, t.LocalPosition.y + 0.5f), true, t.Arc.Effect, false);
			}
			else if (currentTiming > t.Timing + 150)
			{
				t.Judged = true;
			}
		}

		public void SetArcTapShadowSkin(Sprite sprite)
		{
			ArcTapShadowSkin = sprite;
			ArcTapPrefab.GetComponentInChildren<SpriteRenderer>().sprite = sprite;
			SfxArcTapPrefab.GetComponentInChildren<SpriteRenderer>().sprite = sprite;
		}

		public void SetArcCapSkin(Sprite sprite)
		{
			ArcSkin.ArcCapRenderer.sprite = sprite;
		}
		public void SetHeightIndicatorSkin(Sprite sprite)
		{
			ArcSkin.HeightIndicatorRenderer.sprite = sprite;
			foreach (ArcArc arc in Arcs) arc.arcRenderer?.BuildHeightIndicator();
		}

		public void SetArcColors(
			Color arcRedLow, Color arcBlueLow, Color arcGreenLow, Color arcUnknownLow,
			Color arcRedHigh, Color arcBlueHigh, Color arcGreenHigh, Color arcUnknownHigh,
			Color arcVoid, Color arcDesignant, Color arcTapDesignant)
		{
			ArcRedLow = arcRedLow;
			ArcBlueLow = arcBlueLow;
			ArcGreenLow = arcGreenLow;
			ArcUnknownLow = arcUnknownLow;
			ArcRedHigh = arcRedHigh;
			ArcBlueHigh = arcBlueHigh;
			ArcGreenHigh = arcGreenHigh;
			ArcUnknownHigh = arcUnknownHigh;
			ArcVoid = arcVoid;
			ArcDesignant = arcDesignant;
			ArcTapDesignant = arcTapDesignant;
			foreach (ArcArc arc in Arcs)
			{
				arc.arcRenderer?.ReloadColor();
				foreach (ArcArcTap arctap in arc.ArcTaps)
				{
					arctap.UpdateColor();
				}
			}
		}

		public void SetArcBodySkin(Texture2D normal, Texture2D highlight)
		{
			ArcMaterial.mainTexture = normal;
			ArcSkin.HighlightTexture = highlight;
			ArcSkin.DefaultTexture = normal;
			ArcArcSegmentComponent prefabSegmentComponent = ArcSkin.SegmentPrefab.GetComponent<ArcArcSegmentComponent>();
			prefabSegmentComponent.HighlightTexture = highlight;
			prefabSegmentComponent.DefaultTexture = normal;
			prefabSegmentComponent.ReloadSkin();
		}

		public void SetParticleArcColor(Color particleArcStartColor, Color particleArcEndColor)
		{
			ArcSkin.JudgeEffect.StartColor = particleArcStartColor;
			ArcSkin.JudgeEffect.EndColor = particleArcEndColor;
		}

		public void SetArcTapSkin(Texture2D texture)
		{
			ArcTapSkin = texture;
			ArcTapMaterial.mainTexture = texture;
		}

		public void SetSfxArcTapSkin(Texture2D noteTexture, Texture2D coreTexture)
		{
			SfxArcTapNoteSkin = noteTexture;
			SfxArcTapNoteMaterial.mainTexture = noteTexture;
			SfxArcTapCoreSkin = coreTexture;
			SfxArcTapCoreMaterial.mainTexture = coreTexture;
		}

		internal void SetParticleArcTexture(Texture2D texture)
		{
			ArcSkin.JudgeEffect.Texture = texture;
		}
	}
}

using System.Collections.Generic;
using Affdata.Core.Chart;
using Affdata.Core.Geometry;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Gameplay
{
	// Render state of one visible Arc, without a GameObject, as in AffdataPlay. ArcNoteRenderer
	// draws its segments, head, height indicator and cap from these values, and the shared judge
	// effect follows JudgePosition. Skin assets come from the Arc prefab's ArcArcRenderer.
	internal sealed partial class ArcArcVisual
	{
		private static ArcArcRenderer Skin => ArcArcManager.Instance.ArcSkin;

		// Local z of the Arc's start under ArcLayer; ArcArcManager sets it every frame.
		internal float RootZ;
		internal Vector3 JudgePosition { get; private set; }
		internal Color HeadHighColor { get; private set; }
		internal Color HeadLowColor { get; private set; }
		internal Vector3 HeadOffset { get; private set; }

		private Vector3 heightIndicatorPosition, heightIndicatorScale = Vector3.one;
		private Color heightIndicatorColor = Color.white;
		private Vector3 arcCapPosition, arcCapScale = Vector3.one;
		private Color arcCapColor = Color.white;
		private int arcCapOrder;

		private bool enable;
		private bool selected;
		private bool headEnable;
		private bool heightIndicatorEnable;
		private HeightIndicatorClipState heightIndicatorClip;
		private bool arcCapEnable;
		private bool highlighted;
		private bool effect;
		private int effectColor;
		private ArcArc arc;
		private Color currentHighColor;
		private Color currentLowColor;
		private int segmentCount = 0;
		private readonly List<ArcSegmentData> segments = new List<ArcSegmentData>();
		private readonly List<double> segmentProgress = new List<double>();
		internal int BuiltRenderEndTiming { get; private set; }
		private int zeroLengthVoidArcDisappearTime = int.MaxValue;

		public ArcArc Arc => arc;
		public bool EnableHead => headEnable;
		public bool Selected
		{
			get => selected;
			set
			{
				if (selected == value) return;
				selected = value;
				foreach (var s in segments) s.Selected = value;
			}
		}
		public bool Highlight
		{
			get => highlighted;
			set
			{
				if (highlighted == value) return;
				highlighted = value;
				foreach (var s in segments) s.Highlight = value;
			}
		}
		public Color HighColor
		{
			get => currentHighColor;
			set
			{
				if (currentHighColor == value) return;
				currentHighColor = value;
				HeadHighColor = value;
				foreach (var s in segments) s.HighColor = SegmentColor(value);
			}
		}
		public Color LowColor
		{
			get => currentLowColor;
			set
			{
				if (currentLowColor == value) return;
				currentLowColor = value;
				HeadLowColor = value;
				foreach (var s in segments) s.LowColor = SegmentColor(value);
			}
		}
		public float Alpha
		{
			get => currentHighColor.a;
			set
			{
				if (currentHighColor.a != value)
				{
					currentHighColor.a = value;
					HeadHighColor = currentHighColor;
					foreach (var s in segments) s.Alpha = value;
				}
				if (currentLowColor.a != value)
				{
					currentLowColor.a = value;
					HeadLowColor = currentLowColor;
				}
			}
		}
		public bool Enable
		{
			get => enable;
			set
			{
				if (enable == value) return;
				enable = value;
				headEnable = value;
				heightIndicatorEnable = value;
				foreach (ArcSegmentData s in segments) s.Enable = value;
				arcCapEnable = value;
				if (!value) EnableEffect = false;
			}
		}
		public bool EnableEffect
		{
			get => effect;
			set
			{
				// Unregister the old color before this visual is rebound or edited.
				if (effect && (!value || arc == null || effectColor != arc.Color))
					ArcEffectManager.Instance.SetArcEffect(this, effectColor, false);
				effect = value && arc != null;
				if (effect)
				{
					effectColor = arc.Color;
					ArcEffectManager.Instance.SetArcEffect(this, effectColor, true);
				}
			}
		}

		private bool UsesGoldTrace => arc != null && arc.LineType == ArcLineType.TrueIsVoid
			&& arc.TimingGroup != null && arc.TimingGroup.TraceBodyGold;

		private Color SegmentColor(Color color) => UsesGoldTrace ? new Color(1, 1, 1, color.a) : color;

		private Color GetColor(bool high)
		{
			var manager = ArcArcManager.Instance;
			if (arc.LineType == ArcLineType.TrueIsVoid)
			{
				if (arc.TraceColor.HasValue) return arc.TraceColor.Value;
				if (arc.TimingGroup != null)
				{
					if (arc.TimingGroup.TraceBodyGold)
					{
						var color = Affdata.Core.Chart.ArcColors.GoldTrace;
						return new Color32(color.R, color.G, color.B, color.A);
					}
					if (arc.TimingGroup.UseTraceColor) return arc.TimingGroup.TraceColor;
				}
				return manager.ArcVoid;
			}
			if (arc.LineType == ArcLineType.Designant) return manager.ArcDesignant;
			if (arc.Color == ArcColors.BlueId) return high ? manager.ArcBlueHigh : manager.ArcBlueLow;
			if (arc.Color == ArcColors.RedId) return high ? manager.ArcRedHigh : manager.ArcRedLow;
			if (arc.Color == ArcColors.GreenId) return high ? manager.ArcGreenHigh : manager.ArcGreenLow;
			if (arc.Color == ArcColors.GrayId) return high ? manager.ArcUnknownHigh : manager.ArcUnknownLow;
			return ArcColors.Fallback;
		}

		public void ReloadColor()
		{
			float alpha = Alpha;
			if (arc != null)
			{
				HighColor = GetColor(true);
				LowColor = GetColor(false);
			}
			Alpha = alpha;
		}

		private void InstantiateSegment(int quantity)
		{
			while (segments.Count < quantity)
				segments.Add(new ArcSegmentData { Enable = Enable, Alpha = Alpha, Highlight = Highlight, Selected = Selected });
			if (segments.Count > quantity) segments.RemoveRange(quantity, segments.Count - quantity);
		}

		private Matrix4x4 Parent => ArcArcManager.Instance.ArcLayer.localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, RootZ));

		internal void Submit(ArcNoteRenderer renderer)
		{
			if (!enable) return;
			var skin = Skin;
			var parent = Parent;
			var gold = skin.TraceBodyGoldTexture;
			int noteKey = ArcRenderOrder.NoteKey(arc.TimingGroup?.Id ?? 0, arc.Timing, arc.EndTiming, arc.Color);
			for (int i = 0; i < segments.Count; i++)
				segments[i].Submit(renderer, parent, skin.DefaultTexture, skin.HighlightTexture, gold, arc.Color,
					ArcRenderOrder.SegmentKey(noteKey, i, arc.IsVoid), arc);
			renderer.SubmitHead(this, parent, highlighted ? skin.HighlightTexture : skin.DefaultTexture);
			var height = skin.HeightIndicatorRenderer;
			if (heightIndicatorEnable)
				renderer.SubmitSprite(height.sprite, parent * Matrix4x4.TRS(heightIndicatorPosition, Quaternion.identity, heightIndicatorScale),
					heightIndicatorColor, height, height.sortingOrder, 0, true);
			if (arcCapEnable)
			{
				// The cap is placed in world space at the judgement plane, under the Arc's root.
				Vector3 local = parent.inverse.MultiplyPoint3x4(arcCapPosition);
				renderer.SubmitSprite(skin.ArcCapRenderer.sprite, parent * Matrix4x4.TRS(local, Quaternion.identity, arcCapScale),
					arcCapColor, skin.ArcCapRenderer, arcCapOrder, 0);
			}
		}

		internal void Bind(ArcArc note)
		{
			arc = note;
			Build();
		}

		internal void Unbind()
		{
			Enable = false;
			EnableEffect = false;
			arc = null;
		}

		public void Build()
		{
			heightIndicatorClip = default;
			ResetTraceState();
			BuildHeightIndicator();
			BuildSegments();
		}
		public void BuildHeightIndicator()
		{
			if (arc.IsVoid || arc.IsVariousSizedArctap || arc.HiddenSegments)
			{
				heightIndicatorEnable = false;
				return;
			}
			SetHeightIndicatorPosition(new Vector3(ArcAlgorithm.ArcXToWorld(arc.XStart), ArcAlgorithm.ArcYToWorld(arc.YStart), 0));
			heightIndicatorColor = GetColor(true);
		}
		private void SetHeightIndicatorPosition(Vector3 point)
		{
			Sprite sprite = Skin.HeightIndicatorRenderer.sprite;
			if (!sprite) return;
			float height = point.y - ArcArcRenderer.OffsetNormal / 2 - NoteMeshDefinitions.HeightIndicatorFloor;
			float yScale = height / sprite.bounds.size.y;
			heightIndicatorPosition = new Vector3(point.x, NoteMeshDefinitions.HeightIndicatorFloor - sprite.bounds.min.y * yScale, point.z);
			heightIndicatorScale = new Vector3(NoteMeshDefinitions.HeightIndicatorWidth / sprite.bounds.size.x, yScale, 1);
		}

		public void BuildSegments()
		{
			if (arc == null) return;
			ArcTimingManager timingManager = ArcTimingManager.Instance;
			int duration = arc.EndTiming - arc.Timing;
			zeroLengthVoidArcDisappearTime = duration == 0 && (arc.IsVoid || arc.NoInput())
				? timingManager.CalculateZeroLengthVoidArcDisappearTime(arc.Timing, arc.TimingGroup) : int.MaxValue;

			BuiltRenderEndTiming = Mathf.Max(arc.EndTiming, arc.RenderEndTiming);
			int renderDuration = BuiltRenderEndTiming - arc.Timing;
			ArcSampling.FillProgress(segmentProgress, duration, renderDuration, arc.EffectiveSmoothness,
				arc.IsVoid && arc.CurveType == Arcade.Gameplay.Chart.ArcCurveType.S && !ArcArcRenderer.AllowCrossTiming);
			segmentCount = renderDuration == 0 && arc.XStart == arc.XEnd && arc.YStart == arc.YEnd
				? 0 : segmentProgress.Count - 1;
			InstantiateSegment(segmentCount);

			float startVelocity = timingManager.CalculateVelocityAtTiming(arc.Timing, arc.TimingGroup);
			float SegmentZ(float offset)
			{
				if (!ArcArcRenderer.AllowCrossTiming) return -offset * startVelocity / 1000f;
				float time = arc.Timing + offset;
				int whole = Mathf.FloorToInt(time);
				float position = timingManager.CalculatePositionByTimingAndStart(arc.Timing, whole, arc.TimingGroup)
					+ (time - whole) * timingManager.CalculateVelocityAtTiming(whole, arc.TimingGroup);
				return -position / 1000f;
			}

			float startHeight = 0;
			float endHeight = arc.YStart;
			Vector3 start = new Vector3();
			Vector3 end = new Vector3(ArcAlgorithm.ArcXToWorld(arc.XStart), ArcAlgorithm.ArcYToWorld(arc.YStart));
			for (int i = 0; i < segmentCount; ++i)
			{
				float progress = (float)segmentProgress[i + 1];
				float fromOffset = (float)(segmentProgress[i] * renderDuration);
				float endOffset = (float)(segmentProgress[i + 1] * renderDuration);
				bool last = i == segmentCount - 1;
				startHeight = endHeight;
				start = end;
				endHeight = last ? arc.YEnd : ArcAlgorithm.Y(arc.YStart, arc.YEnd, progress, arc.CurveType);
				float x = last ? arc.XEnd : ArcAlgorithm.X(arc.XStart, arc.XEnd, progress, arc.CurveType);
				end = new Vector3(ArcAlgorithm.ArcXToWorld(x), ArcAlgorithm.ArcYToWorld(endHeight), SegmentZ(endOffset));
				// Extend geometry to the successor while preserving zero-duration slam timing.
				segments[i].BuildSegment(start, end, arc.IsVoid ? ArcArcRenderer.OffsetVoid : ArcArcRenderer.OffsetNormal,
					arc.Timing + fromOffset, last ? arc.EndTiming : arc.Timing + endOffset, startHeight, endHeight);
			}
			HighColor = GetColor(true);
			LowColor = GetColor(false);
		}

		public void UpdateArc()
		{
			if (!enable) return;
            if (arc.IsVoid)
            {
                UpdateTrace();
                return;
            }
			UpdateHead();
			UpdateSegments();
			UpdateHeightIndicator();
			UpdateArcCap();
		}
		private void UpdateSegments()
		{
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			float z = RootZ;
			foreach (ArcSegmentData s in segments)
			{
				s.IsTrace = arc.IsVoid;
				s.UseGoldTrace = UsesGoldTrace;
				s.HighColor = SegmentColor(currentHighColor);
				s.LowColor = SegmentColor(currentLowColor);
				if (arc.IsVariousSizedArctap || arc.HiddenSegments)
				{
					s.Enable = false;
					continue;
				}
				if (s.ToTiming == s.FromTiming && currentTiming > zeroLengthVoidArcDisappearTime)
				{
					s.Enable = false;
					continue;
				}
				float pos = -(z + s.FromPos.z);
				float endPos = -(z + s.ToPos.z);
				float distanceAlpha = arc.IsVoid ? 1
                    : ArcadeAlphaAppearance.ArcDistanceAlpha(endPos, arc.Timing == arc.EndTiming);
				s.Alpha = currentHighColor.a * distanceAlpha;
				s.To = endPos > 100 && pos < 100 ? (100 - pos) / (endPos - pos) : 1;

				if ((s.ToTiming < currentTiming && s.ToTiming != s.FromTiming) || (Mathf.Max(pos, endPos) < 0 && s.ToTiming == s.FromTiming))
				{
					s.Enable = !(arc.Judging || arc.IsVoid || arc.NoInput());
					continue;
				}
				if (s.FromTiming < currentTiming && s.ToTiming >= currentTiming)
				{
					s.Enable = true;
					s.From = arc.Judging || arc.IsVoid || arc.NoInput() ? (z + s.FromPos.z) / (-s.ToPos.z + s.FromPos.z) : 0;
					continue;
				}
				if (pos > 90 && pos < 100 && !arc.IsVoid)
				{
					s.Enable = true;
					s.From = 0;
				}
				else if (Mathf.Min(pos, endPos) > 100 || Mathf.Max(pos, endPos) < -100)
				{
					s.Enable = false;
				}
				else
				{
					s.Enable = true;
					s.From = 0;
				}
			}
		}
		private void UpdateHead()
		{
			if (!arc.RenderHead || arc.IsVariousSizedArctap || arc.HiddenSegments)
			{
				headEnable = false;
				return;
			}
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			if (arc.Timing == arc.EndTiming && currentTiming > zeroLengthVoidArcDisappearTime)
			{
				headEnable = false;
				return;
			}
			if (arc.Position > 100000 || arc.Position < -100000)
			{
				headEnable = false;
				return;
			}
			headEnable = true;
			if (arc.Position > 90000 && arc.Position <= 100000)
			{
				HeadOffset = Vector3.zero;
				Color highC = currentHighColor;
				highC.a = currentHighColor.a * (100000 - arc.Position) / 100000;
				Color lowC = currentLowColor;
				lowC.a = currentLowColor.a * (100000 - arc.Position) / 100000;
				HeadHighColor = highC;
				HeadLowColor = lowC;
			}
			if ((arc.Timing < currentTiming && arc.Timing != arc.EndTiming) || (arc.Position < 0 && arc.Timing == arc.EndTiming))
			{
				HeadHighColor = currentHighColor;
				HeadLowColor = currentLowColor;
				if (arc.Judging || arc.IsVoid || arc.NoInput())
				{
					if (segmentCount >= 1)
					{
						ArcSegmentData s = segments[0];
						float duration = s.ToTiming - s.FromTiming;
						if (duration == 0 || s.ToTiming < currentTiming)
						{
							headEnable = false;
							return;
						}
						float t = (-arc.Position / 1000f) / (-s.ToPos.z);
						if (float.IsNaN(t)) t = ((float)(currentTiming - s.FromTiming)) / duration;
						if (t > 1)
						{
							headEnable = false;
							return;
						}
						if (t < 0) t = 0;
						HeadOffset = (s.ToPos - s.FromPos) * t;
					}
				}
				else HeadOffset = Vector3.zero;
			}
			else
			{
				HeadHighColor = currentHighColor;
				HeadLowColor = currentLowColor;
				HeadOffset = Vector3.zero;
			}
			if (!arc.IsVoid)
			{
				float distanceAlpha = ArcadeAlphaAppearance.ArcDistanceAlpha(-(RootZ + HeadOffset.z), arc.Timing == arc.EndTiming);
				Color high = currentHighColor, low = currentLowColor;
				high.a *= distanceAlpha; low.a *= distanceAlpha;
				HeadHighColor = high; HeadLowColor = low;
			}
		}
		private void UpdateHeightIndicator()
		{
			if (arc.IsVoid || (arc.YEnd == arc.YStart && !arc.RenderHead) || arc.IsVariousSizedArctap || arc.HiddenSegments || segments.Count == 0)
			{
				heightIndicatorEnable = false;
				return;
			}
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			float z = RootZ;
			Vector3 judgementPoint = segments[0].FromPos;
			foreach (ArcSegmentData segment in segments)
			{
				float fromZ = z + segment.FromPos.z;
				float toZ = z + segment.ToPos.z;
				float t = fromZ / (fromZ - toZ);
				if (float.IsNaN(t)) t = 0;
				judgementPoint = Vector3.Lerp(segment.FromPos, segment.ToPos, t);
				if (t >= 0 && t <= 1) break;
			}
			bool clipping = (arc.Judging || arc.NoInput()) && arc.Timing < currentTiming;
			float velocity = ArcTimingManager.Instance.CalculateVelocityAtTiming(arc.Timing, arc.TimingGroup);
			float firstEndZ = z + segments[0].ToPos.z;
			if (!heightIndicatorClip.TryGetPosition(currentTiming, currentTiming > arc.Timing, clipping, velocity,
				segments[0].FromPos + Vector3.forward * z, firstEndZ, judgementPoint, out Vector3 position))
			{
				heightIndicatorEnable = false;
				return;
			}
			position.z -= z;
			SetHeightIndicatorPosition(position);
			heightIndicatorEnable = true;
			heightIndicatorColor = GetColor(true);
			heightIndicatorColor.a = Alpha * ArcadeAlphaAppearance.ArcOpacity
                * ArcadeAlphaAppearance.ArcDistanceAlpha(-firstEndZ, arc.Timing == arc.EndTiming);
		}
		private void UpdateArcCap()
		{
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			if (arc.IsVariousSizedArctap)
			{
				arcCapEnable = false;
				return;
			}
			Sprite sprite = Skin.ArcCapRenderer.sprite;
			Vector3 arcCapScaleFactor = new Vector3(256 / sprite.rect.width, 256 / sprite.rect.height, 1);
			if (arc.Timing < currentTiming && arc.EndTiming >= currentTiming)
			{
				arcCapEnable = true;
				arcCapColor = new Color(1, 1, 1, arc.IsVoid ? 0.5f : 1f);
				arcCapScale = Vector3.Scale(new Vector3(arc.IsVoid ? 0.21f : 0.35f, arc.IsVoid ? 0.21f : 0.35f), arcCapScaleFactor);
				arcCapOrder = -1;
				foreach (var s in segments)
				{
					if (s.FromTiming < currentTiming && s.ToTiming >= currentTiming)
					{
						float t = (s.FromPos.z - arc.Position / 1000f) / (s.FromPos.z - s.ToPos.z);
						if (float.IsNaN(t)) t = ((float)(currentTiming - s.FromTiming)) / ((float)(s.ToTiming - s.FromTiming));
						arcCapPosition = new Vector3(s.FromPos.x + (s.ToPos.x - s.FromPos.x) * t, s.FromPos.y + (s.ToPos.y - s.FromPos.y) * t);
						JudgePosition = ArcEffectManager.Instance.EffectPlane.GetPositionOnPlane(arcCapPosition);
						if (!arc.IsVoid) ArcArcManager.Instance.RecordJudgePosition(arcCapPosition.x);
						break;
					}
				}
			}
			else if (arc.Timing >= currentTiming && arc.RenderHead && !arc.IsVoid)
			{
				float p = 1 - Mathf.Abs(arc.Position) / 100000;
				float scale = 0.35f + 0.5f * (1 - p);
				arcCapEnable = true;
				arcCapColor = new Color(1, 1, 1, p);
				arcCapScale = Vector3.Scale(new Vector3(scale, scale), arcCapScaleFactor);
				arcCapPosition = new Vector3(ArcAlgorithm.ArcXToWorld(arc.XStart), ArcAlgorithm.ArcYToWorld(arc.YStart));
				JudgePosition = arcCapPosition;
				//TODO: arccap position should be decided by z instead of time
				arcCapOrder = arc.Position < 0 ? -1 : 2;
			}
			else
			{
				arcCapEnable = false;
			}
		}
	}
}

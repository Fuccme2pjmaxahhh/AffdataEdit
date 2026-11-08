using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using InputField = TMPro.TMP_InputField;
using Arcade.Gameplay;
using System.Linq;
using Arcade.Compose.MarkingMenu;
using Newtonsoft.Json;
using Arcade.Compose.Editing;
using System.Globalization;

namespace Arcade.Compose
{
	[Serializable]
	public class CustomGrid
	{
		public string X = "-0.5>1.5/0.25";
		public string Y = "0>1/0.2;0.5";
	}
	public struct BeatlineProperty
	{
		public int Timing;
        public double? ExactTiming;
		public int Importance;
		public int TimingIndex;
		public bool IsMeasure;
	}

	public class AdeGridManager : AdeMarkingMenuItemProvider
	{
		public static AdeGridManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public GameObject BeatlinePrefab, VerticalPrefab;
		public Transform VerticalPanel;
		public float BeatlineDensity;
		public Color MeasureBeatlineColor;
		public Color[] BeatlineColors;
		public Color VerticalXColor, VerticalYColor;
		public InputField DensityInputField, VerticalXInputField, VerticalYInputField;
		public MarkingMenuItem[] Entry;


		private List<BeatlineProperty> beatlineTimings = new List<BeatlineProperty>();
		private List<int> beatTimings = new List<int>();
		private List<int> measureTimings = new List<int>();
		private List<float> verticalXPositions = new List<float>();
		public IReadOnlyList<float> VerticalXPositions => verticalXPositions;
		private List<float> verticalYPositions = new List<float>();

		private struct VisibleBeatline
		{
			public int Timing;
			public float Position, Width;
			public Color Color;
		}
		private readonly List<VisibleBeatline> visibleBeatlines = new List<VisibleBeatline>();
		private LineRenderer beatlineStyle, verticalStyle;

		private CustomGrid customGrid;

		private void Awake()
		{
			Instance = this;
			LoadCustomGrids();
		}
		private void Start()
		{
			beatlineStyle = BeatlinePrefab.GetComponent<LineRenderer>();
			verticalStyle = VerticalPrefab.GetComponent<LineRenderer>();
			ArcGameplayManager.Instance.OnChartLoad.AddListener(ReBuildBeatline);
		}
		private void Update()
		{
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ToggleGrid))
			{
				SwitchGridStatus();
			}
			if (!ArcGameplayManager.Instance.IsLoaded)
			{
				visibleBeatlines.Clear();
				return;
			}
			UpdateBeatline();
		}
		private void OnDestroy()
		{
			ArcGameplayManager.Instance.OnChartLoad.RemoveListener(ReBuildBeatline);
			SaveCustomGrids();
		}

		public bool Enable
		{
			get
			{
				return EnableBeatline & EnableVertical;
			}
			set
			{
				EnableBeatline = value;
				EnableVertical = value;
			}
		}
		public bool EnableBeatline { get; set; }
		public bool EnableVertical { get; set; }

		public override bool IsOnlyMarkingMenu
		{
			get
			{
				return false;
			}
		}
		public override MarkingMenuItem[] MarkingMenuItems
		{
			get
			{
				return Entry;
			}
		}

		private void CalculateBeatlineTimes()
		{
			beatlineTimings.Clear();
			visibleBeatlines.Clear();
			var Timings = ArcTimingManager.Instance.GetTiming(AdeTimingEditor.Instance.currentTimingGroup);

			for (int i = 0; i < Timings.Count; ++i)
			{
				int nextTiming = i + 1 >= Timings.Count ? ArcGameplayManager.Instance.Length : Timings[i + 1].Timing;
				float segment = Timings[i].Bpm == 0 ? (nextTiming - Timings[i].Timing) : (60000 / Mathf.Abs(Timings[i].Bpm) / BeatlineDensity);
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(nextTiming - Timings[i].Timing, 1);
				int primaryCount = 0;
				int beatCount = 0;
				while (true)
				{
					float j = Timings[i].Timing + primaryCount * segment;
					// Unlike timing line, we plus one here to avoid the 1ms time error
					if (j + 1 >= nextTiming)
						break;
					int importance = CalculateImportance(primaryCount, BeatlineDensity);
					bool isMeasure = importance == 0 && Timings[i].BeatsPerLine > 0
						&& beatCount % Timings[i].BeatsPerLine == 0;
					if (importance == 0) beatCount++;
					primaryCount++;
					if (beatlineTimings.Count > 0)
					{
						var last = beatlineTimings[beatlineTimings.Count - 1];
						if (last.Timing == j)
						{
							last.Importance = Mathf.Min(last.Importance, importance);
							last.IsMeasure |= isMeasure;
							beatlineTimings[beatlineTimings.Count - 1] = last;
							continue;
						}
					}
					beatlineTimings.Add(new BeatlineProperty() { Timing = Mathf.RoundToInt(j), ExactTiming = j, Importance = importance,
						TimingIndex = i, IsMeasure = isMeasure });
				}
			}
			if (Timings.Count > 0)
			{
				var firstTiming = Timings[0];
				int primaryCount = 1;
				int beatCount = 0;
				float segment = firstTiming.Bpm == 0 ? (firstTiming.Timing) : (60000 / Mathf.Abs(firstTiming.Bpm) / BeatlineDensity);
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(firstTiming.Timing, 1);
				while (true)
				{
					float j = firstTiming.Timing - primaryCount * segment;
					if (j < 0)
						break;
					int importance = CalculateImportance(primaryCount, BeatlineDensity);
					if (importance == 0) beatCount++;
					bool isMeasure = importance == 0 && firstTiming.BeatsPerLine > 0
						&& beatCount % firstTiming.BeatsPerLine == 0;
					primaryCount++;
					if (beatlineTimings.Count > 0)
					{
						var first = beatlineTimings[0];
						if (first.Timing == j)
						{
							first.Importance = Mathf.Min(first.Importance, importance);
							first.IsMeasure |= isMeasure;
							beatlineTimings[0] = first;
							continue;
						}
					}
					beatlineTimings.Insert(0, new BeatlineProperty() { Timing = Mathf.RoundToInt(j), ExactTiming = j, Importance = importance,
						TimingIndex = 0, IsMeasure = isMeasure });
				}
			}
		}

		private void CalculateBeatTimes()
		{
			beatTimings.Clear();
			var Timings = ArcTimingManager.Instance.GetTiming(AdeTimingEditor.Instance.currentTimingGroup);

			for (int i = 0; i < Timings.Count; ++i)
			{
				int nextTiming = i + 1 >= Timings.Count ? ArcGameplayManager.Instance.Length : Timings[i + 1].Timing;
				float segment = Timings[i].Bpm == 0 ? (nextTiming - Timings[i].Timing) : (60000 / Mathf.Abs(Timings[i].Bpm));
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(nextTiming - Timings[i].Timing, 1);
				int primaryCount = 0;
				while (true)
				{
					float j = Timings[i].Timing + primaryCount * segment;
					// Unlike timing line, we plus one here to avoid the 1ms time error
					if (j + 1 >= nextTiming)
						break;
					primaryCount++;
					if (beatTimings.Count > 0)
					{
						var last = beatTimings[beatTimings.Count - 1];
						if (last == j)
						{
							continue;
						}
					}
					beatTimings.Add(Mathf.RoundToInt(j));
				}
			}
			if (Timings.Count > 0)
			{
				var firstTiming = Timings[0];
				int primaryCount = 1;
				float segment = firstTiming.Bpm == 0 ? (firstTiming.Timing) : (60000 / Mathf.Abs(firstTiming.Bpm));
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(firstTiming.Timing, 1);
				while (true)
				{
					float j = firstTiming.Timing - primaryCount * segment;
					if (j < 0)
						break;
					primaryCount++;
					if (beatTimings.Count > 0)
					{
						var first = beatTimings[0];
						if (first == j)
						{
							continue;
						}
					}
					beatTimings.Insert(0, Mathf.RoundToInt(j));
				}
			}
		}

		private void CalculateMeasureTimes()
		{
			measureTimings.Clear();
			var Timings = ArcTimingManager.Instance.GetTiming(AdeTimingEditor.Instance.currentTimingGroup);

			for (int i = 0; i < Timings.Count; ++i)
			{
				int nextTiming = i + 1 >= Timings.Count ? ArcGameplayManager.Instance.Length : Timings[i + 1].Timing;
				float segment = Timings[i].Bpm == 0 ? (nextTiming - Timings[i].Timing) : (60000 / Mathf.Abs(Timings[i].Bpm) * Timings[i].BeatsPerLine);
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(nextTiming - Timings[i].Timing, 1);
				int primaryCount = 0;
				while (true)
				{
					float j = Timings[i].Timing + primaryCount * segment;
					// Unlike timing line, we plus one here to avoid the 1ms time error
					if (j + 1 >= nextTiming)
						break;
					primaryCount++;
					if (measureTimings.Count > 0)
					{
						var last = measureTimings[measureTimings.Count - 1];
						if (last == j)
						{
							continue;
						}
					}
					measureTimings.Add(Mathf.RoundToInt(j));
				}
			}
			if (Timings.Count > 0)
			{
				var firstTiming = Timings[0];
				int primaryCount = 1;
				float segment = firstTiming.Bpm == 0 ? (firstTiming.Timing) : (60000 / Mathf.Abs(firstTiming.Bpm) * firstTiming.BeatsPerLine);
				if (segment < 1 || float.IsNaN(segment)) segment = Math.Max(firstTiming.Timing, 1);
				while (true)
				{
					float j = firstTiming.Timing - primaryCount * segment;
					if (j < 0)
						break;
					primaryCount++;
					if (measureTimings.Count > 0)
					{
						var first = measureTimings[0];
						if (first == j)
						{
							continue;
						}
					}
					measureTimings.Insert(0, Mathf.RoundToInt(j));
				}
			}
		}

		private int CalculateImportance(int primaryCount, float BeatlineDensity)
		{

			if (Mathf.Approximately(primaryCount % BeatlineDensity, 0))
			{
				return 0;
			}
			if (Mathf.Approximately(primaryCount % (BeatlineDensity / 2), 0))
			{
				return 1;
			}
			if (Mathf.Approximately(primaryCount % (BeatlineDensity / 4), 0))
			{
				return 2;
			}
			return 3;
		}

		private void ParseVerticals()
		{
			verticalXPositions = Parse(customGrid.X);
			verticalYPositions = Parse(customGrid.Y);
		}
		private List<float> Parse(string str)
		{
			List<float> ret = new List<float>();
			try
			{
				string[] ss = str.Split(';');
				foreach (var s in ss)
				{
					if (s.Contains('>') && s.Contains('/'))
					{
						int v1 = s.IndexOf('>');
						int v2 = s.IndexOf('/');
						float start = float.Parse(s.Substring(0, v1), CultureInfo.InvariantCulture);
						float end = float.Parse(s.Substring(v1 + 1, v2 - v1 - 1), CultureInfo.InvariantCulture);
						float interval = float.Parse(s.Substring(v2 + 1, s.Length - v2 - 1), CultureInfo.InvariantCulture);
						for (float i = start; i <= end; i += interval)
						{
							ret.Add(i);
						}
					}
					else
					{
						ret.Add(float.Parse(s, CultureInfo.InvariantCulture));
					}
				}
			}
			catch (Exception)
			{

			}
			return ret;
		}

		private void UpdateBeatline()
		{

			var timingGroup = AdeTimingEditor.Instance.currentTimingGroup;
			visibleBeatlines.Clear();
			if (EnableBeatline)
			{
				int currentTimingIndex = Mathf.Max(0, ArcTimingManager.FindTimingIndex(
					ArcTimingManager.Instance.GetTiming(timingGroup), ArcGameplayManager.Instance.ChartTiming));
				foreach (var t in beatlineTimings)
				{
					if (!ArcTimingManager.Instance.ShouldTryRender(((int)t.Timing), timingGroup, 0, false))
					{
						continue;
					}
					float pos = ArcTimingManager.Instance.CalculatePositionByTiming((int)t.Timing, timingGroup);
					if (pos > 100000 || pos < -100000)
					{
						continue;
					}
					float z = pos / 1000f;
					Color color = t.IsMeasure ? MeasureBeatlineColor : BeatlineColors[t.Importance];
					if (t.TimingIndex != currentTimingIndex) color.a *= 0.5f;
					visibleBeatlines.Add(new VisibleBeatline { Timing = t.Timing, Position = -z,
						Color = color, Width = 0.2f - 0.021f * t.Importance });
				}
			}
		}
		internal void SubmitGrid(ArcNoteRenderer renderer, Camera camera)
		{
			if (!isActiveAndEnabled) return;
			float xEdgePos = 8.5f * (1 + ArcTimingManager.Instance.BeatlineEnwidenRatio * 0.5f);
			if (EnableBeatline && (camera.cullingMask & (1 << BeatlinePrefab.layer)) != 0)
			{
				Matrix4x4 matrix = transform.localToWorldMatrix * BeatlinePrefab.transform.localToWorldMatrix;
				foreach (var line in visibleBeatlines)
					renderer.SubmitLine(matrix.MultiplyPoint3x4(new Vector3(-xEdgePos, line.Position)),
						matrix.MultiplyPoint3x4(new Vector3(xEdgePos, line.Position)), line.Width, line.Color,
						beatlineStyle.sortingLayerName, beatlineStyle.sortingOrder);
			}
			float yEdgePos = 5.5f + ArcCameraManager.Instance.EnwidenRatio * 2.745f;
			if (EnableVertical && AdeCursorManager.Instance.WallPanelEnabled
				&& (camera.cullingMask & (1 << VerticalPrefab.layer)) != 0)
			{
				Matrix4x4 matrix = VerticalPanel.localToWorldMatrix * VerticalPrefab.transform.localToWorldMatrix;
				Vector3 normal = matrix.MultiplyVector(Vector3.forward);
				foreach (var t in verticalXPositions)
				{
					float x = ArcAlgorithm.ArcXToWorld(t);
					renderer.SubmitLine(matrix.MultiplyPoint3x4(new Vector3(x, 0)),
						matrix.MultiplyPoint3x4(new Vector3(x, yEdgePos)), verticalStyle.startWidth, VerticalXColor,
						verticalStyle.sortingLayerName, verticalStyle.sortingOrder, normal);
				}
				foreach (var t in verticalYPositions)
				{
					float y = ArcAlgorithm.ArcYToWorld(t);
					renderer.SubmitLine(matrix.MultiplyPoint3x4(new Vector3(-xEdgePos, y)),
						matrix.MultiplyPoint3x4(new Vector3(xEdgePos, y)), verticalStyle.startWidth, VerticalYColor,
						verticalStyle.sortingLayerName, verticalStyle.sortingOrder, normal);
				}
			}
		}

		public int AttachBeatlineTimingFromPos(float z)
		{
			var timingGroup = AdeTimingEditor.Instance.currentTimingGroup;
			int defaultTiming = ArcTimingManager.Instance.CalculateChartTimingByPosition(-z * 1000, timingGroup);
			return AttachBeatlineTimingFromPos(z, defaultTiming);
		}

		internal int AttachBeatlineTimingFromPos(float z, int defaultTiming)
		{
			if (!EnableBeatline) return defaultTiming;
			if (visibleBeatlines.Count == 0) return defaultTiming;
			int index = -1;
			float closest = 5;
			for (int i = 0; i < visibleBeatlines.Count; ++i)
			{
				float lz = visibleBeatlines[i].Position;
				float distance = Mathf.Abs(lz - z);
				if (distance < closest) { closest = distance; index = i; }
			}
			return index < 0 ? defaultTiming : visibleBeatlines[index].Timing;
		}
        internal int AttachDragTimingFromPos(float z, int fallback)
        {
            if (!EnableBeatline || visibleBeatlines.Count == 0) return fallback;
            float distance = float.PositiveInfinity;
            int timing = fallback;
            foreach (var line in visibleBeatlines)
            {
                float delta = Mathf.Abs(line.Position - z);
                if (delta >= distance) continue;
                distance = delta; timing = line.Timing;
            }
            return timing;
        }

		internal Vector2 AttachDragCoordinate(Vector2 coordinate)
		{
			if (EnableVertical)
			{
				coordinate.x = ClampToGrid(coordinate.x, verticalXPositions);
				coordinate.y = ClampToGrid(coordinate.y, verticalYPositions);
			}
			return new Vector2(AttachVerticalX(coordinate.x), AttachVerticalY(coordinate.y));
		}

		private static float ClampToGrid(float value, List<float> lines)
		{
			if (lines.Count == 0) return value;
			float min = lines[0], max = lines[0];
			foreach (float line in lines)
			{
				min = Mathf.Min(min, line);
				max = Mathf.Max(max, line);
			}
			return Mathf.Clamp(value, min, max);
		}

		public float AttachVerticalX(float x)
		{
			if (!EnableVertical || !AdeCursorManager.Instance.WallPanelEnabled || !ArcGameplayManager.Instance.IsLoaded) return x;
			if (verticalXPositions.Count == 0) return x;
			List<float> deltas = new List<float>();
			for (int i = 0; i < verticalXPositions.Count; ++i)
			{
				deltas.Add(Mathf.Abs(verticalXPositions[i] - x));
			}
			int index = deltas.IndexOf(deltas.Min());
			if (deltas[index] < 0.3f) return verticalXPositions[index];
			else return x;
		}
		public float AttachVerticalY(float y)
		{
			if (!EnableVertical || !AdeCursorManager.Instance.WallPanelEnabled || !ArcGameplayManager.Instance.IsLoaded) return y;
			if (verticalYPositions.Count == 0) return y;
			List<float> deltas = new List<float>();
			for (int i = 0; i < verticalYPositions.Count; ++i)
			{
				deltas.Add(Mathf.Abs(verticalYPositions[i] - y));
			}
			int index = deltas.IndexOf(deltas.Min());
			if (deltas[index] < 0.3f) return verticalYPositions[index];
			else return y;
		}
		public int AttachScroll(int t, float scroll)
		{
			if (beatlineTimings.Count == 0) return Mathf.RoundToInt(t + 50 * scroll);
			List<int> deltas = new List<int>();
			for (int i = 0; i < beatlineTimings.Count; ++i)
			{
				deltas.Add(Mathf.Abs(beatlineTimings[i].Timing - t));
			}
			int index = deltas.IndexOf(deltas.Min());
			if (deltas[index] <= 1f || ArcGameplayManager.Instance.IsPlaying)
			{
				index += (scroll > 0 ? 1 : -1);
			}
			else if (t > beatlineTimings[index].Timing)
			{
				index += (scroll > 0 ? 1 : 0);
			}
			else
			{
				index += (scroll > 0 ? 0 : -1);
			}
			if (index >= deltas.Count) return Mathf.Max(t, beatlineTimings[deltas.Count - 1].Timing);
			else if (index < 0) return Mathf.Min(t, beatlineTimings[0].Timing);
			return beatlineTimings[index].Timing;
		}
		public int AttachBeatScroll(int t, float scroll)
		{
			if (beatTimings.Count == 0) return Mathf.RoundToInt(t + 200 * scroll);
			List<int> deltas = new List<int>();
			for (int i = 0; i < beatTimings.Count; ++i)
			{
				deltas.Add(Mathf.Abs(beatTimings[i] - t));
			}
			int index = deltas.IndexOf(deltas.Min());
			if (deltas[index] <= 1f || ArcGameplayManager.Instance.IsPlaying)
			{
				index += (scroll > 0 ? 1 : -1);
			}
			else if (t > beatTimings[index])
			{
				index += (scroll > 0 ? 1 : 0);
			}
			else
			{
				index += (scroll > 0 ? 0 : -1);
			}
			if (index >= deltas.Count) return Mathf.Max(t, beatTimings[deltas.Count - 1]);
			else if (index < 0) return Mathf.Min(t, beatTimings[0]);
			return beatTimings[index];
		}
		public int AttachMeasureScroll(int t, float scroll)
		{
			if (measureTimings.Count == 0) return Mathf.RoundToInt(t + 800 * scroll);
			List<int> deltas = new List<int>();
			for (int i = 0; i < measureTimings.Count; ++i)
			{
				deltas.Add(Mathf.Abs(measureTimings[i] - t));
			}
			int index = deltas.IndexOf(deltas.Min());
			if (deltas[index] <= 1f || ArcGameplayManager.Instance.IsPlaying)
			{
				index += (scroll > 0 ? 1 : -1);
			}
			else if (t > measureTimings[index])
			{
				index += (scroll > 0 ? 1 : 0);
			}
			else
			{
				index += (scroll > 0 ? 0 : -1);
			}
			if (index >= deltas.Count) return Mathf.Max(t, measureTimings[deltas.Count - 1]);
			else if (index < 0) return Mathf.Min(t, measureTimings[0]);
			return measureTimings[index];
		}
		public int AttachTiming(int t)
		{
			if (beatlineTimings.Count == 0) return t;
			List<int> deltas = new List<int>();
			for (int i = 0; i < beatlineTimings.Count; ++i)
			{
				deltas.Add(Mathf.Abs(beatlineTimings[i].Timing - t));
			}
			int index = deltas.IndexOf(deltas.Min());
			return beatlineTimings[index].Timing;
		}

        internal int FixPasteTiming(int timing)
        {
            // AA checks the complete grid in order, independently of visibility or grid enablement.
            foreach (var line in beatlineTimings)
                if (Math.Abs((line.ExactTiming ?? line.Timing) - timing) < 2) return line.Timing;
            return timing;
        }

		public void ReBuildBeatline()
		{
			CalculateBeatlineTimes();
			CalculateBeatTimes();
			CalculateMeasureTimes();
		}
		private void LoadCustomGrids()
		{
			try
			{
				customGrid = JsonConvert.DeserializeObject<CustomGrid>(AdeSettings.GetString("AdeGridManager", ""));
				if (customGrid == null) customGrid = new CustomGrid();
			}
			catch (Exception)
			{
				customGrid = new CustomGrid();
			}
			finally
			{
				VerticalXInputField.text = customGrid.X;
				VerticalYInputField.text = customGrid.Y;
				ParseVerticals();
			}
		}
		private void SaveCustomGrids()
		{
			AdeSettings.SetString("AdeGridManager", JsonConvert.SerializeObject(customGrid));
		}

		public void SwitchGridStatus()
		{
			Enable = !Enable;
		}
		public void OnDensityChange()
		{
			float density;
			if (!float.TryParse(DensityInputField.text, NumberStyles.Float, CultureInfo.InvariantCulture, out density)) return;
			if (density <= 0 || density > 64) return;
			BeatlineDensity = density;
			CalculateBeatlineTimes();
		}
		public void OnVerticalXChange()
		{
			List<float> ret = Parse(VerticalXInputField.text);
			if (ret == null)
			{
				VerticalXInputField.text = customGrid.X;
				return;
			}
			verticalXPositions = ret;
			customGrid.X = VerticalXInputField.text;
			SaveCustomGrids();
		}
		public void OnVerticalYChange()
		{
			List<float> ret = Parse(VerticalYInputField.text);
			if (ret == null)
			{
				VerticalYInputField.text = customGrid.Y;
				return;
			}
			verticalYPositions = ret;
			customGrid.Y = VerticalYInputField.text;
			SaveCustomGrids();
		}
	}
}

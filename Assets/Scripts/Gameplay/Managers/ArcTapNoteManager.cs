using System.Collections.Generic;
using Affdata.Core.Geometry;
using UnityEngine;
using Arcade.Gameplay.Chart;

namespace Arcade.Gameplay
{
	public class ArcTapNoteManager : MonoBehaviour
	{
		public static ArcTapNoteManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		private void Awake()
		{
			Instance = this;
		}

		[HideInInspector]
		[System.NonSerialized]
		public List<ArcTap> Taps = new List<ArcTap>();
        internal readonly List<ArcTap> RenderingTaps = new List<ArcTap>();
        private readonly List<ArcTap> renderCandidates = new List<ArcTap>();
        private readonly ArcJudgementCursor<ArcTap> judgement = new ArcJudgementCursor<ArcTap>();
        private readonly List<ArcTap> crossedTaps = new List<ArcTap>();
        internal void ResetJudgement() => judgement.Reset();
        private readonly Dictionary<(ArcTimingGroup, int), (float position, float length)> framePositions
            = new Dictionary<(ArcTimingGroup, int), (float, float)>();
        private readonly ArcVisibleNoteIndex<ArcTap> renderIndex = new ArcVisibleNoteIndex<ArcTap>(n => n.Timing);
		[HideInInspector]
		public readonly float[] Lanes = { 10.625f, 6.375f, 2.125f, -2.125f, -6.375f, -10.625f };
		public GameObject TapNotePrefab;
        public Sprite DefaultSprite;
        internal float WidthScale => DefaultSprite ? NoteMeshDefinitions.TapWidth / DefaultSprite.bounds.size.x : 1f;
		public Transform NoteLayer;
		public Material ShaderdMaterial;

		public void Clean()
		{
			foreach (var t in Taps) t.Destroy();
			Taps.Clear();
            renderIndex.Clear(); renderCandidates.Clear(); RenderingTaps.Clear(); judgement.Clear(); crossedTaps.Clear(); framePositions.Clear();
		}
		public void Load(List<ArcTap> taps)
		{
			Taps = taps;
			foreach (var t in Taps)
			{
				t.Instantiate();
			}
		}

        // Only retained while loading Arcs; editing continues to use the live Tap list.
        private Dictionary<int, List<ArcTap>> connectionTapsByTiming;

        internal void PrepareConnectionLookup()
        {
            connectionTapsByTiming = new Dictionary<int, List<ArcTap>>();
            foreach (var tap in Taps)
            {
                if (!connectionTapsByTiming.TryGetValue(tap.Timing, out var bucket))
                {
                    bucket = new List<ArcTap>();
                    connectionTapsByTiming.Add(tap.Timing, bucket);
                }
                bucket.Add(tap);
            }
        }

        internal void ClearConnectionLookup() => connectionTapsByTiming = null;

        internal IEnumerable<ArcTap> GetConnectionCandidates(int timing)
        {
            if (connectionTapsByTiming == null) return Taps;
            return GetIndexedConnectionCandidates(timing);
        }

        private IEnumerable<ArcTap> GetIndexedConnectionCandidates(int timing)
        {
            // Use long bounds so the +/-1 ms window cannot wrap at int limits.
            for (long candidateTiming = (long)timing - 1; candidateTiming <= (long)timing + 1; candidateTiming++)
            {
                if (candidateTiming < int.MinValue || candidateTiming > int.MaxValue) continue;
                if (!connectionTapsByTiming.TryGetValue((int)candidateTiming, out var bucket)) continue;
                foreach (var tap in bucket) yield return tap;
            }
        }

		public void Add(ArcTap tap)
		{
			ArcScoreManager.Invalidate();
			tap.Instantiate();
			Taps.Add(tap);
			tap.SetupArcTapConnection();
		}
		public void Remove(ArcTap tap)
		{
			ArcScoreManager.Invalidate();
			tap.Destroy();
			Taps.Remove(tap);
		}

		private void Update()
		{
			if (Taps == null) return;
			if (ArcGameplayManager.Instance.Auto) JudgeTapNotes();
			RenderTapNotes();
		}

		private void RenderTapNotes()
		{

            foreach (var note in RenderingTaps) note.Enable = false;
            RenderingTaps.Clear();
            framePositions.Clear();
            renderIndex.Query(Taps, renderCandidates);
			ArcTimingManager timing = ArcTimingManager.Instance;
            Matrix4x4 parentMatrix = NoteLayer.localToWorldMatrix;
            Quaternion rotation = TapNotePrefab.transform.localRotation;
            float widthScale = WidthScale;
            float spriteHeight = DefaultSprite ? DefaultSprite.bounds.size.y : 1f;
            Matrix4x4 basis = parentMatrix * Matrix4x4.TRS(Vector3.zero, rotation, new Vector3(-widthScale, 1, 1));
            Vector4 lengthAxis = basis.GetColumn(1);

			foreach (var t in renderCandidates)
			{
				if (!timing.ShouldTryRender(t.Timing, t.TimingGroup) || t.Judged || t.GroupHide())
				{
					t.Enable = false;
					continue;
				}
                var key = (t.TimingGroup, t.Timing);
                if (!framePositions.TryGetValue(key, out var framePosition))
                {
                    float notePosition = timing.CalculatePositionByTiming(t.Timing, t.TimingGroup);
                    float noteLength = ArcCameraManager.Instance.EditorCamera ? 2f
                        : NoteMeshDefinitions.TapLength(notePosition / 1000f,
                            timing.CalculateVelocityAtTiming(t.Timing, t.TimingGroup)) / spriteHeight;
                    framePosition = (notePosition, noteLength);
                    framePositions.Add(key, framePosition);
                }
                t.Position = framePosition.position;
                if (t.Position > 100000 || t.Position < -100000)
                {
                    t.Enable = false;
                    continue;
                }
                t.Enable = true;
                RenderingTaps.Add(t);
                float pos = t.Position / 1000f;
                Vector3 position = new Vector3(t.WorldX, pos, 0);
                float lengthScale = framePosition.length;
                // Only translation and length vary; reuse the rotated/scaled parent basis.
                Matrix4x4 matrix = basis;
                matrix.SetColumn(1, lengthAxis * lengthScale);
                Vector3 world = parentMatrix.MultiplyPoint3x4(position);
                matrix.SetColumn(3, new Vector4(world.x, world.y, world.z, 1));
                t.RenderMatrix = matrix;
				t.Alpha = pos < 90 ? 1 : (100 - pos) / 10f;
			}
		}
		private void JudgeTapNotes()
		{

			ArcTimingManager timing = ArcTimingManager.Instance;
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
            judgement.Advance(Taps, currentTiming, crossedTaps);
			foreach (var t in crossedTaps)
			{
				if (t.NoInput())
				{
					continue;
				}
				if (t.Judged) continue;
				if (currentTiming > t.Timing && currentTiming <= t.Timing + 150)
				{
					t.Judged = true;
					if (ArcGameplayManager.Instance.IsPlaying) ArcEffectManager.Instance.PlayTapNoteEffectAt(new Vector2(t.WorldX, 0));
				}
				else if (currentTiming > t.Timing + 150)
				{
					t.Judged = true;
				}
			}
		}
		public void SetTapNoteSkin(Sprite sprite)
        {
            DefaultSprite = sprite;
            ShaderdMaterial.mainTexture = sprite.texture;
        }
		public void SetConnectionLineColor(Color color)
        {
            ArcArcManager.Instance.ConnectionColor = color;
        }
	}
}

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
        // Hit sounds run on their own cursor at SoundChartTiming (ahead of the drawn time by the visual delay).
        private readonly ArcJudgementCursor<ArcTap> soundJudgement = new ArcJudgementCursor<ArcTap>();
        private readonly List<ArcTap> soundCrossedTaps = new List<ArcTap>();
        internal void ResetJudgement() { judgement.Reset(); soundJudgement.Reset(); }
        private readonly Dictionary<(ArcTimingGroup, int), (float position, float length)> framePositions
            = new Dictionary<(ArcTimingGroup, int), (float, float)>();
        private readonly ArcVisibleNoteIndex<ArcTap> renderIndex = new ArcVisibleNoteIndex<ArcTap>(n => n.Timing);
		[HideInInspector]
		public readonly float[] Lanes = { 10.625f, 6.375f, 2.125f, -2.125f, -6.375f, -10.625f };
		public GameObject TapNotePrefab;
        public Sprite DefaultSprite;
        private Sprite mineLight, mineDark;
        internal Sprite GetSprite(ArcTap tap)
        {
            if (!tap.IsMine) return DefaultSprite;
            return ArcSkinManager.Instance.IsDarkSide
                ? (mineDark ? mineDark : mineDark = Resources.Load<Sprite>("AffdataEdit/MineTapDark"))
                : (mineLight ? mineLight : mineLight = Resources.Load<Sprite>("AffdataEdit/MineTapLight"));
        }
        internal float WidthScale => DefaultSprite ? NoteMeshDefinitions.TapWidth / DefaultSprite.bounds.size.x : 1f;
		public Transform NoteLayer;
		public Material ShaderdMaterial;

		public void Clean()
		{
			foreach (var t in Taps) t.Destroy();
			Taps.Clear();
            renderIndex.Clear(); renderCandidates.Clear(); RenderingTaps.Clear(); judgement.Clear(); crossedTaps.Clear(); soundJudgement.Clear(); soundCrossedTaps.Clear(); framePositions.Clear();
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
            int currentTiming = ArcGameplayManager.Instance.ChartTiming;
            bool editorCamera = ArcCameraManager.Instance.EditorCamera;
            var defaultTimings = timing.GetTiming(null);
            bool constantDefault = defaultTimings.Count == 1;
            float defaultBpm = constantDefault ? defaultTimings[0].Bpm : 0;
            float baseBpm = timing.BaseBpm, velocity = timing.SettingVelocity;
            float defaultVelocity = defaultBpm / baseBpm * velocity;
            bool resident = constantDefault && ArcNoteRenderer.Instance
                && ArcNoteRenderer.Instance.PrepareResidentTaps(Taps, new ArcNoteRenderer.TapProjection {
                    Layer = parentMatrix, Basis = basis, Timing = currentTiming, Bpm = defaultBpm,
                    BaseBpm = baseBpm, Velocity = velocity, SpriteHeight = spriteHeight, EditorCamera = editorCamera });

			foreach (var t in renderCandidates)
			{
				// The index has already checked this group's visible time range.
                if (unchecked(t.Timing + ArcTimingManager.LostDelay) < currentTiming || t.Judged || t.GroupHide())
				{
					t.Enable = false;
					continue;
				}
                if (resident && t.TimingGroup == null && !t.IsMine)
                {
                    t.Enable = true; t.GpuPresentationFrame = Time.frameCount;
                    RenderingTaps.Add(t);
                    continue;
                }
                (float position, float length) framePosition;
                if (constantDefault && t.TimingGroup == null)
                {
                    // Keep the integration order, including reversed and unchecked time differences.
                    bool reversed = currentTiming > t.Timing;
                    int start = reversed ? t.Timing : currentTiming;
                    int end = reversed ? currentTiming : t.Timing;
                    float constantPosition = 0;
                    constantPosition += unchecked(end - start) * defaultBpm / baseBpm * velocity;
                    if (reversed) constantPosition = -constantPosition;
                    framePosition = (constantPosition, editorCamera ? 2f : ArcadeAlphaAppearance.TapLength(constantPosition / 1000f, defaultVelocity) / spriteHeight);
                }
                else
                {
                    var key = (t.TimingGroup, t.Timing);
                    if (!framePositions.TryGetValue(key, out framePosition))
                    {
                        float notePosition = timing.CalculatePositionByTiming(t.Timing, t.TimingGroup);
                        float noteLength = editorCamera ? 2f
                            : ArcadeAlphaAppearance.TapLength(notePosition / 1000f,
                                timing.CalculateVelocityAtTiming(t.Timing, t.TimingGroup)) / spriteHeight;
                        framePosition = (notePosition, noteLength);
                        framePositions.Add(key, framePosition);
                    }
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
                float noteWidth = t.Width;
                if (t.IsMine)
                {
                    Sprite sprite = GetSprite(t);
                    noteWidth *= DefaultSprite.bounds.size.x / sprite.bounds.size.x;
                    lengthScale *= DefaultSprite.bounds.size.y / sprite.bounds.size.y;
                }
                // Apply each note's dimensions to the shared rotated/scaled parent basis.
                Matrix4x4 matrix = basis;
                matrix.SetColumn(0, matrix.GetColumn(0) * noteWidth);
                matrix.m01 = lengthAxis.x * lengthScale; matrix.m11 = lengthAxis.y * lengthScale;
                matrix.m21 = lengthAxis.z * lengthScale; matrix.m31 = lengthAxis.w * lengthScale;
                Vector3 world = parentMatrix.MultiplyPoint3x4(position);
                matrix.m03 = world.x; matrix.m13 = world.y; matrix.m23 = world.z; matrix.m33 = 1;
                t.RenderMatrix = matrix;
				t.Alpha = ArcadeAlphaAppearance.TapAlpha(pos);
			}
		}
        internal Matrix4x4 GetRenderMatrix(ArcTap tap)
        {
            if (tap.GpuPresentationFrame != Time.frameCount) return tap.RenderMatrix;
            var timing = ArcTimingManager.Instance;
            tap.Position = timing.CalculatePositionByTiming(tap.Timing, tap.TimingGroup);
            float pos = tap.Position / 1000f;
            float length = ArcCameraManager.Instance.EditorCamera ? 2f
                : ArcadeAlphaAppearance.TapLength(pos, timing.CalculateVelocityAtTiming(tap.Timing, tap.TimingGroup)) / DefaultSprite.bounds.size.y;
            tap.Alpha = ArcadeAlphaAppearance.TapAlpha(pos);
            tap.RenderMatrix = NoteLayer.localToWorldMatrix * Matrix4x4.TRS(new Vector3(tap.WorldX, pos, 0),
                TapNotePrefab.transform.localRotation, new Vector3(-WidthScale * tap.Width, length, 1));
            return tap.RenderMatrix;
        }
		private void JudgeTapNotes()
		{

			ArcTimingManager timing = ArcTimingManager.Instance;
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
            int soundTiming = ArcGameplayManager.Instance.SoundChartTiming;
            soundJudgement.Advance(Taps, soundTiming, soundCrossedTaps);
            foreach (var t in soundCrossedTaps)
                if (!t.NoInput() && soundTiming <= t.Timing + 150 && ArcGameplayManager.Instance.IsPlaying)
                    ArcEffectManager.Instance.PlayTapSound();
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
					if (ArcGameplayManager.Instance.IsPlaying) ArcEffectManager.Instance.PlayTapNoteEffectAt(new Vector2(t.WorldX, 0), playSound: false);
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

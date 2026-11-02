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
			ArcTimingManager timing = ArcTimingManager.Instance;
            Matrix4x4 parentMatrix = NoteLayer.localToWorldMatrix;
            Quaternion rotation = TapNotePrefab.transform.localRotation;
            float widthScale = WidthScale;
            float spriteHeight = DefaultSprite ? DefaultSprite.bounds.size.y : 1f;

			foreach (var t in Taps)
			{
				if (!timing.ShouldTryRender(t.Timing, t.TimingGroup) || t.Judged || t.GroupHide())
				{
					t.Enable = false;
					continue;
				}
				t.Position = timing.CalculatePositionByTiming(t.Timing, t.TimingGroup);
				if (t.Position > 100000 || t.Position < -100000)
				{
					t.Enable = false;
					continue;
				}
				t.Enable = true;
				float pos = t.Position / 1000f;
                Vector3 position = new Vector3(t.WorldX, pos, 0);
                float lengthScale = ArcCameraManager.Instance.EditorCamera
                    ? 2f
                    : NoteMeshDefinitions.TapLength(pos,
                        timing.CalculateVelocityAtTiming(t.Timing, t.TimingGroup)) / spriteHeight;
                Vector3 scale = new Vector3(widthScale, lengthScale, 1);
                // Skin sprites face left; flip their mesh without flipping the picking/connection transform.
                t.RenderMatrix = parentMatrix * Matrix4x4.TRS(position, rotation, new Vector3(-scale.x, scale.y, scale.z));
				t.Alpha = pos < 90 ? 1 : (100 - pos) / 10f;
			}
		}
		private void JudgeTapNotes()
		{
			ArcTimingManager timing = ArcTimingManager.Instance;
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			foreach (var t in Taps)
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

using Affdata.Core.Geometry;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Arcade.Gameplay;
using Arcade.Util.UnityExtension;
using Arcade.Gameplay.Chart;
using UnityEngine.Events;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Compose.MarkingMenu;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;

namespace Arcade.Compose
{
	public class AdeCursorManager : MonoBehaviour
	{
		public static AdeCursorManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public Camera GameplayCamera;
		public MeshCollider TrackCollider;
		public MeshCollider WallCollider;
		public GameObject WallPanel;
		public LineRenderer TrackX;
		public LineRenderer TrackY;
		public LineRenderer WallX;
		public LineRenderer WallY;
		public MeshRenderer WallRenderer;

		public Transform ArcTapCursor;
		public MeshRenderer ArcTapCursorRenderer;
		public Transform SfxArcTapCursor;
		public MeshRenderer SfxArcTapCursorRenderer;

		private bool trackEnabled, wallEnabled, wallPanelEnabled;
		private bool arcTapCursorEnable, arcTapCursorIsSfx;
		private RaycastHit trackHit, wallHit;

		public bool TrackEnabled
		{
			get
			{
				return trackEnabled;
			}
			private set
			{
				if (trackEnabled != value)
				{
					TrackX.enabled = value;
					TrackY.enabled = value;
					TrackX.positionCount = 0;
					TrackY.positionCount = 0;
					trackEnabled = value;
				}
			}
		}
		public bool WallEnabled
		{
			get
			{
				return wallEnabled;
			}
			private set
			{
				if (wallEnabled != value)
				{
					WallX.enabled = value;
					WallY.enabled = value;
					WallX.positionCount = 0;
					WallY.positionCount = 0;
					WallPanelEnabled = value;
					wallEnabled = value;
				}
			}
		}
		public bool WallPanelEnabled
		{
			get
			{
				return wallPanelEnabled;
			}
			private set
			{
				if (wallPanelEnabled != value)
				{
					WallRenderer.enabled = value;
					wallPanelEnabled = value;
				}
			}
		}
		public bool ArcTapCursorEnabled
		{
			get
			{
				return arcTapCursorEnable;
			}
			set
			{
				ArcTapCursorRenderer.enabled = (!arcTapCursorIsSfx) && value;
				SfxArcTapCursorRenderer.enabled = (arcTapCursorIsSfx) && value;
				arcTapCursorEnable = value;
			}
		}
		public bool ArcTapCursorIsSfx
		{
			get
			{
				return arcTapCursorIsSfx;
			}
			set
			{
				ArcTapCursorRenderer.enabled = (!value) && arcTapCursorEnable;
				SfxArcTapCursorRenderer.enabled = (value) && arcTapCursorEnable;
				arcTapCursorIsSfx = value;
			}
		}
		public Vector2 ArcTapCursorPosition
		{
			get
			{
                return (Vector2)ArcTapCursor.localPosition
                    - Vector2.up * (NoteMeshDefinitions.ArcTapCenter.y - NoteMeshDefinitions.ArcTapAnchorYOffset);
			}
			set
			{
                ArcTapCursor.localPosition = (Vector3)value
                    + Vector3.up * (NoteMeshDefinitions.ArcTapCenter.y - NoteMeshDefinitions.ArcTapAnchorYOffset);
				SfxArcTapCursor.localPosition = (Vector3)value
                    + Vector3.up * (NoteMeshDefinitions.SfxArcTapCenter.y - NoteMeshDefinitions.ArcTapAnchorYOffset);
			}
		}

		public bool VisibleWhenIdle = false;

		private int? overriddenCursorTiming = null;
		private Vector3? dragCursorPoint;
        private float? fixedCoordinatePlaneZ;

		internal bool BeginDragCursor(bool coordinate, bool sky)
		{
			if (currentSelectTaskType != null) return false;
			currentSelectTaskType = coordinate ? SelectTaskType.Coordinate : sky ? SelectTaskType.TimingForArc : SelectTaskType.Timing;
			return true;
		}

		internal void UpdateDragCursor(int timing, Vector3 point)
		{
			overriddenCursorTiming = timing;
			dragCursorPoint = point;
			UpdateTrackCursor();
			UpdateWallCursor();
		}

		internal void EndDragCursor()
		{
			dragCursorPoint = null;
			overriddenCursorTiming = null;
			currentSelectTaskType = null;
			UpdateTrackCursor();
			UpdateWallCursor();
		}

		private enum SelectTaskType
		{
			Timing,
			TimingForArc,
			Track,
			Coordinate,
		}
		private SelectTaskType? currentSelectTaskType = null;
		private bool shouldRenderTrack
		{
			get => (VisibleWhenIdle && currentSelectTaskType == null)
			|| currentSelectTaskType == SelectTaskType.Timing
			|| currentSelectTaskType == SelectTaskType.TimingForArc
			|| currentSelectTaskType == SelectTaskType.Track;
		}
		private bool shouldRenderWall
		{
			get => currentSelectTaskType == SelectTaskType.Coordinate;
		}
		private bool shouldRenderWallPanel
		{
			get => ArcTapCursorEnabled
			|| currentSelectTaskType == SelectTaskType.TimingForArc
			|| shouldRenderWall;
		}

		public bool IsTrackHit { get; private set; }
		public bool IsWallHit { get; private set; }
		public Vector3 TrackPoint
		{
			get
			{
				return trackHit.point;
			}
		}
		public Vector3 WallPoint
		{
			get
			{
				return wallHit.point;
			}
		}
		public Vector3 AttachedTrackPoint
		{
			get
			{
				float z;
				if (fixedCoordinatePlaneZ.HasValue) z = fixedCoordinatePlaneZ.Value;
                else if (overriddenCursorTiming != null)
				{
					z = ArcTimingManager.Instance.CalculatePositionByTiming(
						overriddenCursorTiming.Value,
						AdeTimingEditor.Instance.currentTimingGroup
					) / -1000f;
				}
				else
				{
					z = ArcTimingManager.Instance.CalculatePositionByTiming(
						AttachedTiming,
						AdeTimingEditor.Instance.currentTimingGroup
					) / -1000f;
				}
				return new Vector3(trackHit.point.x, trackHit.point.y, z);
			}
		}
		public Vector3 AttachedCoordinate
		{
			get
			{
				if (dragCursorPoint.HasValue) return new Vector2(ArcAlgorithm.WorldXToArc(dragCursorPoint.Value.x), ArcAlgorithm.WorldYToArc(dragCursorPoint.Value.y));
				return new Vector2(AdeGridManager.Instance.AttachVerticalX(ArcAlgorithm.WorldXToArc(WallPoint.x)),
				   AdeGridManager.Instance.AttachVerticalY(ArcAlgorithm.WorldYToArc(WallPoint.y)));
			}
		}
		public Vector3 AttachedWallPoint
		{
			get
			{
				if (dragCursorPoint.HasValue) return dragCursorPoint.Value;
				Vector2 coordinate = AttachedCoordinate;
				return new Vector3(ArcAlgorithm.ArcXToWorld(coordinate.x), ArcAlgorithm.ArcYToWorld(coordinate.y));
			}
		}

		public int AttachedTiming
		{
			get
			{
				if (!ArcGameplayManager.Instance.IsLoaded) return 0;
				if (overriddenCursorTiming != null)
				{
					return overriddenCursorTiming.Value;
				}
				return AdeGridManager.Instance.AttachBeatlineTimingFromPos(trackHit.point.z);
			}
		}

		private int PositionToTrack(float position)
		{
			return Mathf.Clamp((int)(position / -4.25f + 3), 0, 5);
		}

		public int AttachedTrack
		{
			get
			{
				if (!ArcGameplayManager.Instance.IsLoaded) return 0;
				Vector3 pos = AttachedTrackPoint;
				return PositionToTrack(pos.x);
			}
		}

		private void Awake()
		{
			Instance = this;
		}

		private void Update()
		{
			UpdateTrackCursor();
			UpdateWallCursor();
		}

		private void UpdateTrackCursor()
		{
			float xEdgePos = 8.5f * (1 + ArcTimingManager.Instance.BeatlineEnwidenRatio * 0.5f);
			if (dragCursorPoint.HasValue)
			{
				Vector3 point = dragCursorPoint.Value;
				TrackEnabled = shouldRenderTrack;
				if (TrackEnabled)
				{
					xEdgePos = Mathf.Max(xEdgePos, Mathf.Abs(point.x));
					TrackX.DrawLine(new Vector3(-xEdgePos, point.z), new Vector3(xEdgePos, point.z));
					TrackY.DrawLine(new Vector3(point.x, Mathf.Max(0, point.z)), new Vector3(point.x, Mathf.Min(-100, point.z)));
				}
				WallPanel.transform.localPosition = new Vector3(0, 0, point.z);
				return;
			}
			TrackCollider.gameObject.transform.localScale = new Vector3(xEdgePos * 2f, 100f, 1);
			Ray ray = GameplayCamera.MousePositionToRay();
			IsTrackHit = TrackCollider.Raycast(ray, out trackHit, 120);
			TrackEnabled = IsTrackHit && shouldRenderTrack;
			if (IsTrackHit || overriddenCursorTiming != null)
			{
				float z;
				if (fixedCoordinatePlaneZ.HasValue) z = fixedCoordinatePlaneZ.Value;
                else if (overriddenCursorTiming != null)
				{
					z = ArcTimingManager.Instance.CalculatePositionByTiming(
						overriddenCursorTiming.Value,
						AdeTimingEditor.Instance.currentTimingGroup
					) / -1000f;
				}
				else
				{
					z = ArcTimingManager.Instance.CalculatePositionByTiming(
						AttachedTiming,
						AdeTimingEditor.Instance.currentTimingGroup
					) / -1000f;
				}
				if (TrackEnabled)
				{
					TrackX.DrawLine(new Vector3(-xEdgePos, z), new Vector3(xEdgePos, z));
					TrackY.DrawLine(new Vector3(trackHit.point.x, 0), new Vector3(trackHit.point.x, -100));
				}
				WallPanel.transform.localPosition = new Vector3(0, 0, z);
			}
		}
		private void UpdateWallCursor()
		{
			float xEdgePos = 8.5f * (1 + ArcTimingManager.Instance.BeatlineEnwidenRatio * 0.5f);
			float yEdgePos = 5.5f + ArcCameraManager.Instance.EnwidenRatio * 2.745f;
			if (dragCursorPoint.HasValue)
			{
				Vector3 point = dragCursorPoint.Value;
				WallEnabled = shouldRenderWall;
				if (WallEnabled)
				{
					xEdgePos = Mathf.Max(xEdgePos, Mathf.Abs(point.x));
					WallX.DrawLine(new Vector3(-xEdgePos, point.y), new Vector3(xEdgePos, point.y));
					WallY.DrawLine(new Vector3(point.x, Mathf.Min(0, point.y)), new Vector3(point.x, Mathf.Max(yEdgePos, point.y)));
				}
				WallPanelEnabled = shouldRenderWallPanel;
				return;
			}
			WallCollider.gameObject.transform.localScale = new Vector3(xEdgePos * 2f, yEdgePos, 1);
			WallCollider.gameObject.transform.localPosition = new Vector3(0, yEdgePos / 2f, 0);
			Ray ray = GameplayCamera.MousePositionToRay();
			IsWallHit = WallCollider.Raycast(ray, out wallHit, 120);
			WallEnabled = IsWallHit && shouldRenderWall;
			if (WallEnabled)
			{
				WallX.DrawLine(new Vector3(-xEdgePos, AttachedWallPoint.y), new Vector3(xEdgePos, AttachedWallPoint.y));
				WallY.DrawLine(new Vector3(AttachedWallPoint.x, 0), new Vector3(AttachedWallPoint.x, yEdgePos));
			}
			WallPanelEnabled = shouldRenderWallPanel;
		}

		public async UniTask<int> SelectTiming(IProgress<int> progress, CancellationToken cancellationToken, bool timingForArc = false)
		{
			if (currentSelectTaskType != null)
			{
				throw new Exception("Cannot select two thing at the same time");
			}

			currentSelectTaskType = timingForArc ? SelectTaskType.TimingForArc : SelectTaskType.Timing;
			try
			{
				while (true)
				{
					await UniTask.NextFrame(cancellationToken);
					if (AdeGameplayContentInputHandler.InputActive && IsTrackHit)
					{
						progress.Report(AttachedTiming);
						if (Mouse.current.leftButton.wasPressedThisFrame)
						{
							return AttachedTiming;
						}
					}
				}
			}
			finally
			{
				currentSelectTaskType = null;
			}
		}

		public async UniTask<Rect> SelectRectangle(int timing, CancellationToken cancellationToken)
		{
			Vector2 first = await SelectCoordinate(timing, Progress.Create<Vector2>(_ => { }), cancellationToken);
			var outline = new GameObject("Selection rectangle");
			outline.layer = WallX.gameObject.layer;
			outline.transform.SetParent(WallX.transform.parent, false);
			outline.transform.localPosition = WallX.transform.localPosition;
			outline.transform.localRotation = WallX.transform.localRotation;
			outline.transform.localScale = WallX.transform.localScale;
			var line = outline.AddComponent<LineRenderer>();
			line.useWorldSpace = false; line.loop = true; line.positionCount = 4;
			line.sharedMaterial = WallX.sharedMaterial;
			line.widthMultiplier = WallX.widthMultiplier;
			line.startColor = line.endColor = Color.cyan;
			line.sortingLayerID = WallX.sortingLayerID;
			line.sortingOrder = WallX.sortingOrder + 1;
			void Draw(Vector2 second)
			{
				float left = ArcAlgorithm.ArcXToWorld(first.x), right = ArcAlgorithm.ArcXToWorld(second.x);
				float bottom = ArcAlgorithm.ArcYToWorld(first.y), top = ArcAlgorithm.ArcYToWorld(second.y);
				line.SetPositions(new[] { new Vector3(left, bottom), new Vector3(left, top), new Vector3(right, top), new Vector3(right, bottom) });
			}
			try
			{
				Draw(first);
				Vector2 second = await SelectCoordinate(timing, Progress.Create<Vector2>(Draw), cancellationToken);
				return Rect.MinMaxRect(Mathf.Min(first.x, second.x), Mathf.Min(first.y, second.y),
					Mathf.Max(first.x, second.x), Mathf.Max(first.y, second.y));
			}
			finally { Destroy(outline); }
		}

		public async UniTask<Vector2> SelectCoordinate(int overriddenCursorTiming, IProgress<Vector2> progress, CancellationToken cancellationToken, bool fixedWorldPlane = false)
		{
			if (currentSelectTaskType != null)
			{
				throw new Exception("Cannot select two thing at the same time");
			}

			currentSelectTaskType = SelectTaskType.Coordinate;
			this.overriddenCursorTiming = overriddenCursorTiming;
			try
			{
                if (fixedWorldPlane)
                    fixedCoordinatePlaneZ = ArcTimingManager.Instance.CalculatePositionByTiming(overriddenCursorTiming,
                        AdeTimingEditor.Instance.currentTimingGroup) / -1000f;
				while (true)
				{
					await UniTask.NextFrame(cancellationToken);
                    if (fixedWorldPlane) { UpdateTrackCursor(); UpdateWallCursor(); }
					if (AdeGameplayContentInputHandler.InputActive && IsWallHit)
					{
						progress.Report(AttachedCoordinate);
						if (Mouse.current.leftButton.wasPressedThisFrame)
						{
							return AttachedCoordinate;
						}
					}
				}
			}
			finally
			{
                fixedCoordinatePlaneZ = null;
				currentSelectTaskType = null;
				this.overriddenCursorTiming = null;
			}
		}

		public async UniTask<float> SelectFloatLane(IProgress<float> progress, CancellationToken cancellationToken)
		{
			if (currentSelectTaskType != null)
			{
				throw new Exception("Cannot select two thing at the same time");
			}

			currentSelectTaskType = SelectTaskType.Track;
			try
			{
				while (true)
				{
					await UniTask.NextFrame(cancellationToken);
					if (AdeGameplayContentInputHandler.InputActive && IsTrackHit)
					{
						progress.Report((8.5f - trackHit.point.x) / 17f);
						if (Mouse.current.leftButton.wasPressedThisFrame)
						{
							return (8.5f - trackHit.point.x) / 17f;
						}
					}
				}
			}
			finally
			{
				currentSelectTaskType = null;
			}
		}
		public async UniTask<int> SelectTrack(IProgress<int> progress, CancellationToken cancellationToken, bool timingForArc = false)
		{
			if (currentSelectTaskType != null)
			{
				throw new Exception("Cannot select two thing at the same time");
			}

			currentSelectTaskType = SelectTaskType.Track;
			try
			{
				while (true)
				{
					await UniTask.NextFrame(cancellationToken);
					if (AdeGameplayContentInputHandler.InputActive && IsTrackHit)
					{
						progress.Report(AttachedTrack);
						if (Mouse.current.leftButton.wasPressedThisFrame)
						{
							return AttachedTrack;
						}
					}
				}
			}
			finally
			{
				currentSelectTaskType = null;
			}
		}
	}
}

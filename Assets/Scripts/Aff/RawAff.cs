using System.Collections.Generic;
using UnityEngine;
using Arcade.Gameplay.Chart;

namespace Arcade.Aff
{
	public interface IRawAffValue { }

	public class RawAffWord : IRawAffValue
	{
		public string data;
	}
	public class RawAffFloat : IRawAffValue
	{
		public float data;
	}
	public class RawAffInt : IRawAffValue
	{
		public int data;
	}
	public interface IRawAffEvent { }
	public interface IRawAffItem : IRawAffEvent { }
	public interface IRawAffNestableItem : IRawAffItem { }
	public class RawAffTap : IRawAffNestableItem
	{
		public int Timing;
		public int Track;
		public float? FloatLane;
		public float Width = 1;
		public bool IsMine;
	}
	public class RawAffHold : IRawAffNestableItem
	{
		public int Timing;
		public int EndTiming;
		public int Track;
		public float? FloatLane;
		public float Width = 1;
	}
	public class RawAffTiming : IRawAffNestableItem
	{
		public int Timing;
		public float Bpm;
		public float BeatsPerLine;
	}
	public class RawAffArc : IRawAffNestableItem
	{
		public int Timing;
		public int EndTiming;
		public float XStart;
		public float XEnd;
		public ArcCurveType CurveType;
		public float YStart;
		public float YEnd;
		public int Color;
		public string Effect;
		public ArcLineType LineType;
		public Color32? TraceColor;
		public float? Smoothness;
		public List<RawAffArctap> ArcTaps;
	}
	public enum SlideHeight
	{
		Ground = 0,
		Sky = 1,
		HighSky = 2,
	}
	public class RawAffSlide : IRawAffNestableItem
	{
		public int Timing, EndTiming, LeftCurve, RightCurve;
		public SlideHeight Height = SlideHeight.Sky;
		// Compatibility for callers using the former two-plane model.
		public bool IsFloor { get => Height == SlideHeight.Ground; set => Height = value ? SlideHeight.Ground : SlideHeight.Sky; }
		private float startCenter, startWidth, endCenter, endWidth;
		public float StartCenter { get => startCenter; set => startCenter = RoundCoordinate(value); }
		public float StartWidth { get => startWidth; set => startWidth = RoundCoordinate(value); }
		public float EndCenter { get => endCenter; set => endCenter = RoundCoordinate(value); }
		public float EndWidth { get => endWidth; set => endWidth = RoundCoordinate(value); }
		internal static float RoundCoordinate(float value) => (float)System.Math.Round(value, 3, System.MidpointRounding.AwayFromZero);
	}
	public class RawAffCamera : IRawAffNestableItem
	{
		public int Timing;
		public float MoveX;
		public float MoveY;
		public float MoveZ;
		public float RotateX;
		public float RotateY;
		public float RotateZ;
		public CameraEaseType CameraType;
		public int Duration;
	}
	public class RawAffTimingGroup : IRawAffItem
	{
		public string Attributes;
		public List<IRawAffNestableItem> Items;
	}
	public class RawAffSceneControl : IRawAffNestableItem
	{
		public int Timing;
		public string Type;
		public List<IRawAffValue> Params;
	}
	public class RawAffArctap : IRawAffEvent
	{
		public int Timing;
		public float Width = 1;
		public bool IsMine;
	}
	public class RawAffChart
	{
		public int AudioOffset = 0;
		public float TimingPointDensityFactor = 1;
		public Dictionary<string, string> additionalMetadata = new Dictionary<string, string>();
		public List<IRawAffItem> items = new List<IRawAffItem>();
		public List<string> warning = new List<string>();
		public List<string> error = new List<string>();
		// Invalid events that retain their original data and do not prevent loading.
		public List<string> recoverableError = new List<string>();
	}
	public interface IIntoRawItem
	{
		IRawAffItem IntoRawItem();
	}
}

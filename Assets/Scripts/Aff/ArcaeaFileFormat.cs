using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Arcade.Gameplay.Chart;
using Affdata.Core.Colors;

namespace Arcade.Aff
{
	public static class ArcaeaFileFormat
	{
		public static RawAffChart ParseFromPath(string path)
		{
			RawAffChart chart = AffParser.Parse(File.ReadAllText(path));
			foreach (string warning in chart.warning)
			{
				Debug.LogWarning(warning);
			}
			foreach (string error in chart.error.Concat(chart.recoverableError))
			{
				Debug.LogError(error);
			}
			return chart;
		}

		public static void DumpToStream(Stream stream, RawAffChart chart)
		{
			TextWriter writer = new StreamWriter(stream);
			writer.WriteLine($"AudioOffset:{chart.AudioOffset.ToString(CultureInfo.InvariantCulture)}");
			if (chart.TimingPointDensityFactor != 1f)
			{
				writer.WriteLine($"TimingPointDensityFactor:{chart.TimingPointDensityFactor.ToString(CultureInfo.InvariantCulture)}");
			}
			foreach (var entry in chart.additionalMetadata)
			{
				writer.WriteLine($"{entry.Key}:{entry.Value}");
			}
			writer.WriteLine($"-");
			foreach (var item in chart.items)
			{
				writeItem(writer, item);
			}
			writer.Close();
		}
		// One item as written in an aff file, e.g. for quoting it in reports.
		public static string FormatItem(IRawAffItem item)
		{
			using (var writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				writeItem(writer, item);
				return writer.ToString().TrimEnd();
			}
		}
		static void writeItem(TextWriter writer, IRawAffItem item, string intent = "")
		{
			if (item is RawAffTiming timing)
			{
				writer.WriteLine($"{intent}timing({timing.Timing.ToString(CultureInfo.InvariantCulture)},{timing.Bpm.ToString("f3", CultureInfo.InvariantCulture)},{timing.BeatsPerLine.ToString("f2", CultureInfo.InvariantCulture)});");
			}
			else if (item is RawAffTap tap)
			{
				writer.WriteLine($"{intent}({tap.Timing.ToString(CultureInfo.InvariantCulture)},{LanePosition.Format(tap.Track, tap.FloatLane)}{FormatWidthAndMine(tap.Width, tap.IsMine)});");
			}
			else if (item is RawAffHold hold)
			{
				writer.WriteLine($"{intent}hold({hold.Timing.ToString(CultureInfo.InvariantCulture)},{hold.EndTiming.ToString(CultureInfo.InvariantCulture)},{LanePosition.Format(hold.Track, hold.FloatLane)}{FormatWidthAndMine(hold.Width, false)});");
			}
			else if (item is RawAffSlide slide)
			{
				writer.WriteLine(intent + string.Format(CultureInfo.InvariantCulture,
					"slide({0},{1},{2},{3},{4},{5},{6},{7},{8});", slide.Timing, slide.EndTiming,
					FormatSlideCoordinate(slide.StartCenter), FormatSlideCoordinate(slide.StartWidth),
					FormatSlideCoordinate(slide.EndCenter), FormatSlideCoordinate(slide.EndWidth), slide.LeftCurve, slide.RightCurve, (int)slide.Height));
			}
			else if (item is RawAffArc arc)
			{
				if (arc.ArcTaps.Count > 0 && arc.LineType == ArcLineType.FalseNotVoid)
				{
					arc.LineType = ArcLineType.TrueIsVoid;
				}
				writer.WriteLine($"{intent}arc({arc.Timing.ToString(CultureInfo.InvariantCulture)},{arc.EndTiming.ToString(CultureInfo.InvariantCulture)},{arc.XStart.ToString("f3", CultureInfo.InvariantCulture)},{arc.XEnd.ToString("f3", CultureInfo.InvariantCulture)}" +
					$",{ArcCurveTypeStrings[arc.CurveType]},{arc.YStart.ToString("f3", CultureInfo.InvariantCulture)},{arc.YEnd.ToString("f3", CultureInfo.InvariantCulture)},{arc.Color},{arc.Effect},{(arc.TraceColor.HasValue && arc.LineType == ArcLineType.TrueIsVoid ? FormatHexColor(arc.TraceColor.Value) : ArcLineTypeStrings[arc.LineType])}"
					 + (arc.Smoothness == null ? "" : $",{arc.Smoothness.Value.ToString("0.0######", CultureInfo.InvariantCulture)}") + ")" +
					(arc.ArcTaps.Count > 0 ? $"[{string.Join(",", arc.ArcTaps.Select(e => $"arctap({e.Timing.ToString(CultureInfo.InvariantCulture)}{FormatWidthAndMine(e.Width, e.IsMine)})"))}]" : "") +
					";");
			}
			else if (item is RawAffCamera cam)
			{
				writer.WriteLine($"{intent}camera({cam.Timing.ToString(CultureInfo.InvariantCulture)},{cam.MoveX.ToString("f2", CultureInfo.InvariantCulture)},{cam.MoveY.ToString("f2", CultureInfo.InvariantCulture)},{cam.MoveZ.ToString("f2", CultureInfo.InvariantCulture)}," +
					$"{cam.RotateX.ToString("f2", CultureInfo.InvariantCulture)},{cam.RotateY.ToString("f2", CultureInfo.InvariantCulture)},{cam.RotateZ.ToString("f2", CultureInfo.InvariantCulture)},{CameraEaseTypeStrings[cam.CameraType]},{cam.Duration.ToString(CultureInfo.InvariantCulture)});");
			}
			else if (item is RawAffSceneControl scenecontrol)
			{
				List<string> values = new List<string>();
				values.Add(scenecontrol.Timing.ToString(CultureInfo.InvariantCulture));
				values.Add(scenecontrol.Type);
				foreach (var @param in scenecontrol.Params)
				{
					if (@param is RawAffInt @int)
					{
						values.Add(@int.data.ToString(CultureInfo.InvariantCulture));
					}
					else if (@param is RawAffFloat @float)
					{
						values.Add(@float.data.ToString("f2", CultureInfo.InvariantCulture));
					}
					else if (@param is RawAffWord word)
					{
						values.Add(word.data);
					}
				}
				writer.WriteLine($"{intent}scenecontrol({string.Join(",", values)});");
			}
			else if (item is RawAffTimingGroup timinggroup)
			{
				writer.WriteLine($"{intent}timinggroup({timinggroup.Attributes}){{");
				foreach (var nestedItem in timinggroup.Items)
				{
					writeItem(writer, nestedItem, intent + "  ");
				}
				writer.WriteLine($"{intent}}};");
			}
		}
		// Optional width and mine arguments. A width always has a decimal point, so it cannot be
		// read back as the mine marker 4.
		private static string FormatWidthAndMine(float width, bool mine)
		{
			string text = width == 1 ? "" : "," + width.ToString("0.00", CultureInfo.InvariantCulture);
			return mine ? text + ",4" : text;
		}
		private static string FormatSlideCoordinate(float value)
		{
			return value.ToString("f3", CultureInfo.InvariantCulture);
		}
		public static string FormatHexColor(Color32 color)
		{
			return HexColor.Format(new Rgba32(color.r, color.g, color.b, color.a));
		}

		// #RGB, #RGBA, #RRGGBB or #RRGGBBAA.
		public static bool TryParseHexColor(string raw, out Color32 color)
		{
			bool success = HexColor.TryParse(raw, out Rgba32 parsed);
			color = new Color32(parsed.R, parsed.G, parsed.B, parsed.A);
			return success;
		}

		public static Dictionary<ArcCurveType, string> ArcCurveTypeStrings = new Dictionary<ArcCurveType, string>
		{
			[ArcCurveType.B] = "b",
			[ArcCurveType.S] = "s",
			[ArcCurveType.Si] = "si",
			[ArcCurveType.So] = "so",
			[ArcCurveType.SiSi] = "sisi",
			[ArcCurveType.SiSo] = "siso",
			[ArcCurveType.SoSi] = "sosi",
			[ArcCurveType.SoSo] = "soso",
		};
		public static Dictionary<ArcLineType, string> ArcLineTypeStrings = new Dictionary<ArcLineType, string>
		{
			[ArcLineType.TrueIsVoid] = "true",
			[ArcLineType.FalseNotVoid] = "false",
			[ArcLineType.Designant] = "designant",
		};
		public static Dictionary<CameraEaseType, string> CameraEaseTypeStrings = new Dictionary<CameraEaseType, string>
		{
			[CameraEaseType.L] = "l",
			[CameraEaseType.Reset] = "reset",
			[CameraEaseType.Qi] = "qi",
			[CameraEaseType.Qo] = "qo",
			[CameraEaseType.S] = "s",
		};
	}
}

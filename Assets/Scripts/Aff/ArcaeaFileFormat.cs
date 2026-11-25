using System;
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
		private const int SlideBufferLength = 256;

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

		public static void DumpToStream(Stream stream, RawAffChart chart, bool lowPrecision = false)
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
			var slideBuffer = new char[SlideBufferLength];
			foreach (var item in chart.items)
			{
				writeItem(writer, item, lowPrecision, slideBuffer);
			}
			writer.Close();
		}
        internal static string FormatDecimal(float value, int minimumDecimals = 2)
        {
            // AFF has no exponent syntax. Expand round-trip notation without losing small values.
            string number = value.ToString("R", CultureInfo.InvariantCulture);
            int exponentAt = number.IndexOf('E');
            if (exponentAt >= 0)
            {
                int exponent = int.Parse(number.Substring(exponentAt + 1), CultureInfo.InvariantCulture);
                string mantissa = number.Substring(0, exponentAt);
                string sign = mantissa.StartsWith("-", StringComparison.Ordinal) ? "-" : "";
                mantissa = mantissa.TrimStart('-');
                int point = (mantissa.Contains(".") ? mantissa.IndexOf('.') : mantissa.Length) + exponent;
                string digits = mantissa.Replace(".", "");
                number = sign + (point <= 0 ? "0." + new string('0', -point) + digits
                    : point >= digits.Length ? digits + new string('0', point - digits.Length) : digits.Insert(point, "."));
            }
            int dot = number.IndexOf('.');
            if (dot < 0) return number + "." + new string('0', minimumDecimals);
            return number.PadRight(dot + 1 + minimumDecimals, '0');
        }
		// One item as written in an aff file, e.g. for quoting it in reports.
		public static string FormatItem(IRawAffItem item, bool lowPrecision = false)
		{
			using (var writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				var slideBuffer = item is RawAffSlide || item is RawAffTimingGroup ? new char[SlideBufferLength] : null;
				writeItem(writer, item, lowPrecision, slideBuffer);
				return writer.ToString().TrimEnd();
			}
		}
		static void writeItem(TextWriter writer, IRawAffItem item, bool lowPrecision, char[] slideBuffer, string intent = "")
		{
			if (item is RawAffTiming timing)
			{
				writer.WriteLine($"{intent}timing({timing.Timing.ToString(CultureInfo.InvariantCulture)},{timing.Bpm.ToString("f3", CultureInfo.InvariantCulture)},{FormatDecimal(timing.BeatsPerLine)});");
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
                if (lowPrecision && !ArcSlide.ValidShape(
                    AffCoordinate.Round(slide.StartCenter, true), AffCoordinate.Round(slide.StartWidth, true),
                    AffCoordinate.Round(slide.EndCenter, true), AffCoordinate.Round(slide.EndWidth, true), slide.LeftCurve, slide.RightCurve))
                    throw new InvalidDataException($"Slide（{slide.Timing}）在两位小数下宽度归零或边界交叉，请关闭低精度或调整范围。");
				WriteSlide(writer, slide, lowPrecision, intent, slideBuffer);
			}
			else if (item is RawAffArc arc)
			{
				if (arc.ArcTaps.Count > 0 && arc.LineType == ArcLineType.FalseNotVoid)
				{
					arc.LineType = ArcLineType.TrueIsVoid;
				}
				writer.WriteLine($"{intent}arc({arc.Timing.ToString(CultureInfo.InvariantCulture)},{arc.EndTiming.ToString(CultureInfo.InvariantCulture)},{AffCoordinate.Format(arc.XStart, lowPrecision)},{AffCoordinate.Format(arc.XEnd, lowPrecision)}" +
					$",{ArcCurveTypeStrings[arc.CurveType]},{AffCoordinate.Format(arc.YStart, lowPrecision)},{AffCoordinate.Format(arc.YEnd, lowPrecision)},{arc.Color},{arc.Effect},{(arc.TraceColor.HasValue && arc.LineType == ArcLineType.TrueIsVoid ? FormatHexColor(arc.TraceColor.Value) : ArcLineTypeStrings[arc.LineType])}"
					 + (arc.Smoothness == null ? "" : $",{arc.Smoothness.Value.ToString("0.0######", CultureInfo.InvariantCulture)}") + ")" +
					(arc.ArcTaps.Count > 0 ? $"[{string.Join(",", arc.ArcTaps.Select(e => $"arctap({e.Timing.ToString(CultureInfo.InvariantCulture)}{FormatWidthAndMine(e.Width, e.IsMine)})"))}]" : "") +
					";");
			}
			else if (item is RawAffCamera cam)
			{
				writer.WriteLine($"{intent}camera({cam.Timing.ToString(CultureInfo.InvariantCulture)},{FormatDecimal(cam.MoveX)},{FormatDecimal(cam.MoveY)},{FormatDecimal(cam.MoveZ)}," +
					$"{FormatDecimal(cam.RotateX)},{FormatDecimal(cam.RotateY)},{FormatDecimal(cam.RotateZ)},{CameraEaseTypeStrings[cam.CameraType]},{cam.Duration.ToString(CultureInfo.InvariantCulture)});");
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
						values.Add(FormatDecimal(@float.data));
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
				string nestedIndent = intent + "  ";
				foreach (var nestedItem in timinggroup.Items)
				{
					writeItem(writer, nestedItem, lowPrecision, slideBuffer, nestedIndent);
				}
				writer.WriteLine($"{intent}}};");
			}
		}
		// A fixed-point float needs at most 44 characters (including sign and decimals).
		// The shared buffer fits four coordinates, five signed integers and separators.
		private static void WriteSlide(TextWriter writer, RawAffSlide slide, bool lowPrecision, string indent, char[] buffer)
		{
			int offset = 0;
			void Integer(int value)
			{
				if (!value.TryFormat(buffer.AsSpan(offset), out int length, default, CultureInfo.InvariantCulture))
					throw new InvalidDataException("Slide integer exceeds the AFF line buffer.");
				offset += length;
				buffer[offset++] = ',';
			}
			void Coordinate(float value)
			{
				if (!value.TryFormat(buffer.AsSpan(offset), out int length, lowPrecision ? "f2" : "f3", CultureInfo.InvariantCulture))
					throw new InvalidDataException("Slide coordinate exceeds the AFF line buffer.");
				offset += length;
				buffer[offset++] = ',';
			}
			Integer(slide.Timing); Integer(slide.EndTiming);
			Coordinate(slide.StartCenter); Coordinate(slide.StartWidth);
			Coordinate(slide.EndCenter); Coordinate(slide.EndWidth);
			Integer(slide.LeftCurve); Integer(slide.RightCurve); Integer((int)slide.Height);
			buffer[offset - 1] = ')';
			buffer[offset++] = ';';
			writer.Write(indent);
			writer.Write("slide(");
			writer.Write(buffer, 0, offset);
			writer.WriteLine();
		}

		// Optional width and mine arguments. A width always has a decimal point, so it cannot be
		// read back as the mine marker 4.
		private static string FormatWidthAndMine(float width, bool mine)
		{
			string text = width == 1 ? "" : "," + width.ToString("0.00", CultureInfo.InvariantCulture);
			return mine ? text + ",4" : text;
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

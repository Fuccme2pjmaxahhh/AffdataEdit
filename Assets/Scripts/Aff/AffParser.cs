using System;
using System.Collections.Generic;
using System.Globalization;
using Affdata.Core.Colors;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Aff
{
	// Hand-written AFF reader. It accepts every chart AffdataPlay's NoteParser accepts and reads
	// values the same way (names ignore case, numbers may be quoted, floats may omit the point),
	// while keeping Edit's recovery: an invalid event becomes a warning or a recoverable error and
	// the rest of the chart still loads. Only malformed structure fails the chart.
	public static class AffParser
	{
		public static RawAffChart Parse(string source)
		{
			var chart = new RawAffChart();
			var reader = new Reader(source ?? string.Empty, chart);
			reader.ReadHeader();
			reader.ReadBody();
			return chart;
		}

		private struct Arg
		{
			public int Start, Length, Line, Column;
		}

		private sealed class Reader
		{
			private readonly string source;
			private readonly RawAffChart chart;
			private readonly List<IRawAffItem> nonNestableItems = new List<IRawAffItem>();
			private readonly List<Arg> args = new List<Arg>();
			private readonly List<Arg> subArgs = new List<Arg>();
			private int index, line, column;
			private bool failed;

			public Reader(string source, RawAffChart chart)
			{
				this.source = source;
				this.chart = chart;
			}

			public void ReadHeader()
			{
				bool audioOffsetSet = false, densitySet = false;
				while (index < source.Length)
				{
					int start = index, number = line + 1;
					while (index < source.Length && source[index] != '\r' && source[index] != '\n') index++;
					string text = source.Substring(start, index - start).Trim();
					if (index < source.Length && source[index] == '\r') index++;
					if (index < source.Length && source[index] == '\n') index++;
					line++;
					if (text == "-") return;
					if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal)) continue;
					int separator = text.IndexOf(':');
					if (separator <= 0)
					{
						chart.warning.Add($"第 {Number(number)} 行：{text} 元信息格式错误，此行会被忽略");
						continue;
					}
					string key = text.Substring(0, separator).Trim();
					string value = text.Substring(separator + 1).Trim();
					if (Same(key, "AudioOffset"))
					{
						if (audioOffsetSet)
							chart.warning.Add($"第 {Number(number)} 行：AudioOffset 被重复设置为 {value}，此行会被忽略");
						else if (TryInt(Unquote(value.AsSpan()), out int offset))
						{
							chart.AudioOffset = offset;
							audioOffsetSet = true;
						}
						else chart.warning.Add($"第 {Number(number)} 行：AudioOffset 的值不是整数，此行会被忽略");
					}
					else if (Same(key, "TimingPointDensityFactor") || Same(key, "TimingPointsDensityFactor"))
					{
						if (densitySet)
							chart.warning.Add($"第 {Number(number)} 行：TimingPointDensityFactor 被重复设置为 {value}，此行会被忽略");
						// Play keeps any number, including 0 (one midpoint judgement per long note, as in Alpha).
						else if (!TryFloat(Unquote(value.AsSpan()), out float factor))
							chart.warning.Add($"第 {Number(number)} 行：TimingPointDensityFactor 的值不是浮点数，此行会被忽略");
						else
						{
							chart.TimingPointDensityFactor = factor;
							densitySet = true;
						}
					}
					else if (chart.additionalMetadata.ContainsKey(key))
						chart.warning.Add($"第 {Number(number)} 行：{key}被重复设置为{value}，此行会被忽略");
					else chart.additionalMetadata.Add(key, value);
				}
			}

			public void ReadBody()
			{
				var items = new List<IRawAffItem>();
				if (ReadItems(items, null)) chart.items.AddRange(items);
				chart.items.AddRange(nonNestableItems);
			}

			// Reads events until the end (top level) or the closing brace of a timinggroup.
			private bool ReadItems(List<IRawAffItem> top, List<IRawAffNestableItem> group)
			{
				while (true)
				{
					SkipTrivia();
					while (TryConsume(';')) SkipTrivia();
					if (index >= source.Length)
					{
						if (group == null) return true;
						return Fail("timinggroup 的事件块缺少右花括号");
					}
					if (source[index] == '}')
					{
						if (group == null) return Fail("多余的右花括号");
						Advance();
						return true;
					}

					int eventLine = line, eventColumn = column;
					string name = string.Empty;
					if (source[index] != '(' && !TryReadIdentifier(out name)) return Fail("无法识别的内容");
					if (!ReadArguments(args)) return false;

					IRawAffItem item;
					if (Same(name, "timinggroup"))
					{
						string attributes = Attributes(args);
						SkipTrivia();
						if (Peek == '[')
						{
							Warn(line, column, "timinggroup 事件不应包含子事件，这些子事件将被忽略");
							if (!ReadSubevents(null)) return false;
							SkipTrivia();
						}
						if (!TryConsume('{')) return Fail("timinggroup 缺少事件块");
						var nested = new List<IRawAffNestableItem>();
						if (!ReadItems(null, nested)) return false;
						item = new RawAffTimingGroup { Attributes = attributes, Items = nested };
					}
					else
					{
						item = ReadEvent(name, eventLine, eventColumn);
						if (failed) return false;
					}
					SkipTrivia();
					if (!TryConsume(';')) return Fail("事件后缺少分号");
					if (item == null) continue;
					if (group == null) top.Add(item);
					else if (item is IRawAffNestableItem nestable) group.Add(nestable);
					else
					{
						Warn(eventLine, eventColumn, "不可在 timinggroup 中嵌套使用的物件：timinggroup，此物件将被忽略");
						nonNestableItems.Add(item);
					}
				}
			}

			// Reads an event's optional subevents and block, then builds it from the arguments in args.
			private IRawAffItem ReadEvent(string name, int eventLine, int eventColumn)
			{
				string type = name.Length == 0 ? "tap" : name.ToLowerInvariant();
				bool isArc = type == "arc";
				List<RawAffArctap> arctaps = null;
				SkipTrivia();
				if (Peek == '[')
				{
					if (isArc)
					{
						arctaps = new List<RawAffArctap>();
						if (!ReadSubevents(arctaps)) return null;
					}
					else
					{
						Warn(line, column, $"{type} 事件不应包含子事件，这些子事件将被忽略");
						if (!ReadSubevents(null)) return null;
					}
					SkipTrivia();
				}
				if (Peek == '{')
				{
					Warn(line, column, $"{type} 事件不应包含事件块，此事件块将被忽略");
					Advance();
					var ignored = new List<IRawAffNestableItem>();
					if (!ReadItems(null, ignored)) return null;
				}
				switch (type)
				{
					case "tap": return Tap(eventLine, eventColumn);
					case "hold": return Hold(eventLine, eventColumn);
					case "timing": return Timing(eventLine, eventColumn);
					case "arc": return Arc(eventLine, eventColumn, arctaps ?? new List<RawAffArctap>());
					case "slide": return Slide(eventLine);
					case "camera": return Camera(eventLine, eventColumn);
					case "scenecontrol": return SceneControl(eventLine, eventColumn);
					case "arctap":
						Warn(eventLine, eventColumn, "不可作为物件使用的事件：arctap，该事件将被忽略");
						return null;
					default:
						Warn(eventLine, eventColumn, $"不支持的事件类型：{name}，该事件将被忽略");
						return null;
				}
			}

			private IRawAffItem Tap(int eventLine, int eventColumn)
			{
				if (!CountBetween("tap", 2, 4, eventLine, eventColumn)
					|| !Int(args[0], "tap", "时间", out int timing)) return null;
				if (!Lane(args[1], out int track, out float? lane))
				{
					WarnAt(args[1], "tap 事件的轨道参数超过范围，此 tap 将被忽略");
					return null;
				}
				if (!WidthAndMine(args, 2, out float width, out bool mine))
				{
					WarnAt(args[2], "tap 事件的宽度或地雷参数无效，此 tap 将被忽略");
					return null;
				}
				return new RawAffTap { Timing = timing, Track = track, FloatLane = lane, Width = width, IsMine = mine };
			}

			private IRawAffItem Hold(int eventLine, int eventColumn)
			{
				if (!CountBetween("hold", 3, 4, eventLine, eventColumn)
					|| !Int(args[0], "hold", "时间", out int timing)
					|| !Int(args[1], "hold", "结束时间", out int endTiming)) return null;
				if (endTiming < timing)
				{
					WarnAt(args[1], "hold 事件的结束时间早于开始时间，作为修复将会交换起止时间");
					(timing, endTiming) = (endTiming, timing);
				}
				if (!Lane(args[2], out int track, out float? lane))
				{
					WarnAt(args[2], "hold 事件的轨道参数超过范围，此 hold 将被忽略");
					return null;
				}
				float width = 1;
				if (args.Count == 4 && !Float(args[3], "hold", "宽度", out width)) return null;
				return new RawAffHold { Timing = timing, EndTiming = endTiming, Track = track, FloatLane = lane, Width = width };
			}

			private IRawAffItem Timing(int eventLine, int eventColumn)
			{
				if (!CountBetween("timing", 3, 3, eventLine, eventColumn)
					|| !Int(args[0], "timing", "时间", out int timing)
					|| !Float(args[1], "timing", "BPM", out float bpm)
					|| !Float(args[2], "timing", "单个小节拍数", out float beats)) return null;
				if (beats < 0)
				{
					WarnAt(args[2], "timing 事件的单个小节拍数小于 0，此 timing 将被忽略");
					return null;
				}
				return new RawAffTiming { Timing = timing, Bpm = bpm, BeatsPerLine = beats };
			}

			private IRawAffItem Arc(int eventLine, int eventColumn, List<RawAffArctap> arctaps)
			{
				if (!CountBetween("arc", 10, 11, eventLine, eventColumn)
					|| !Int(args[0], "arc", "时间", out int timing)
					|| !Int(args[1], "arc", "结束时间", out int endTiming)
					|| !Float(args[2], "arc", "起点横坐标", out float xStart)
					|| !Float(args[3], "arc", "终点横坐标", out float xEnd)
					|| !Float(args[5], "arc", "起点纵坐标", out float yStart)
					|| !Float(args[6], "arc", "终点纵坐标", out float yEnd)
					|| !Int(args[7], "arc", "颜色", out int color)) return null;
				if (!CurveTypes.TryGetValue(Word(args[4]).ToLowerInvariant(), out ArcCurveType curve))
				{
					WarnAt(args[4], "arc 事件的 arc 曲线类型 参数的值不是合法的几个值之一，此事件将被忽略");
					return null;
				}
				string trace = Word(args[9]);
				Color32? traceColor = null;
				if (TryHexColor(trace, out Color32 parsedColor))
				{
					traceColor = parsedColor;
					trace = "true";
				}
				if (!LineTypes.TryGetValue(trace.ToLowerInvariant(), out ArcLineType lineType))
				{
					// Play draws any other value as a solid arc.
					WarnAt(args[9], "arc 事件的 是否黑线 参数的值不是合法的几个值之一，将按实心 arc 读取");
					lineType = ArcLineType.FalseNotVoid;
				}
				float? smoothness = null;
				if (args.Count == 11)
				{
					if (TryFloat(Value(args[10]), out float value)) smoothness = value;
					else WarnAt(args[10], "arc 事件的 平滑度 参数的值类型错误，将使用默认平滑度");
				}
				int low = Math.Min(timing, endTiming), high = Math.Max(timing, endTiming);
				for (int i = 0; i < arctaps.Count; i++)
				{
					if (arctaps[i].Timing >= low && arctaps[i].Timing <= high) continue;
					chart.recoverableError.Add($"第 {Number(eventLine + 1)} 行第 {Number(eventColumn + 1)} 列，arctap 时间 {arctaps[i].Timing} 超出所属 arc 的时间范围 [{low}, {high}]，已保留原始事件，请修正时间");
				}
				return new RawAffArc
				{
					Timing = timing, EndTiming = endTiming, XStart = xStart, XEnd = xEnd, CurveType = curve,
					YStart = yStart, YEnd = yEnd, Color = color, Effect = Word(args[8]), LineType = lineType,
					TraceColor = traceColor, Smoothness = smoothness, ArcTaps = arctaps,
				};
			}

			private IRawAffItem Slide(int eventLine)
			{
				if (args.Count != 8 && args.Count != 9)
				{
					chart.error.Add($"第 {Number(eventLine + 1)} 行：slide 需要 8 个参数，以及可选的高度枚举（0 地面、1 天空、2 高空）。");
					return null;
				}
				SlideHeight height = SlideHeight.Sky;
				if (!TryInt(Value(args[0]), out int start) || !TryInt(Value(args[1]), out int end)
					|| !SlideCoordinate(args[2], out float startCenter) || !SlideCoordinate(args[3], out float startWidth)
					|| !SlideCoordinate(args[4], out float endCenter) || !SlideCoordinate(args[5], out float endWidth)
					|| !SlideCurve(args[6], out int left) || !SlideCurve(args[7], out int right)
					|| (args.Count == 9 && !SlideHeightValue(args[8], out height))
					|| (long)end - start < 2 || (long)end - start > int.MaxValue
					|| !ArcSlide.ValidShape(startCenter, startWidth, endCenter, endWidth, left, right))
				{
					chart.error.Add($"第 {Number(eventLine + 1)} 行：slide 参数无效；需要至少 2ms、三位小数下宽度大于 0 且边界不交叉、0/1/2/3（b）曲线及 0/1/2 高度。");
					return null;
				}
				return new RawAffSlide
				{
					Timing = start, EndTiming = end, StartCenter = startCenter, StartWidth = startWidth,
					EndCenter = endCenter, EndWidth = endWidth, LeftCurve = left, RightCurve = right, Height = height,
				};
			}

			private IRawAffItem Camera(int eventLine, int eventColumn)
			{
				if (!CountBetween("camera", 9, 9, eventLine, eventColumn)
					|| !Int(args[0], "camera", "时间", out int timing)
					|| !Float(args[1], "camera", "平移 X 轴", out float moveX)
					|| !Float(args[2], "camera", "平移 Y 轴", out float moveY)
					|| !Float(args[3], "camera", "平移 Z 轴", out float moveZ)
					|| !Float(args[4], "camera", "旋转 X 轴", out float rotateX)
					|| !Float(args[5], "camera", "旋转 Y 轴", out float rotateY)
					|| !Float(args[6], "camera", "旋转 Z 轴", out float rotateZ)
					|| !Int(args[8], "camera", "时长", out int duration)) return null;
				if (!CameraTypes.TryGetValue(Word(args[7]).ToLowerInvariant(), out CameraEaseType ease))
				{
					WarnAt(args[7], "camera 事件的 camera 类型 参数的值不是合法的几个值之一，此事件将被忽略");
					return null;
				}
				if (duration < 0)
				{
					WarnAt(args[8], "camera 事件的时长小于零，作为修复其时长见会被设为零");
					duration = 0;
				}
				return new RawAffCamera
				{
					Timing = timing, MoveX = moveX, MoveY = moveY, MoveZ = moveZ,
					RotateX = rotateX, RotateY = rotateY, RotateZ = rotateZ, CameraType = ease, Duration = duration,
				};
			}

			private IRawAffItem SceneControl(int eventLine, int eventColumn)
			{
				if (args.Count < 2)
				{
					Warn(eventLine, eventColumn, $"scenecontrol 事件的参数个数应当为至少 2 个而非 {args.Count} 个，此事件将被忽略");
					return null;
				}
				if (!Int(args[0], "scenecontrol", "时间", out int timing)) return null;
				var values = new List<IRawAffValue>(args.Count - 2);
				for (int i = 2; i < args.Count; i++)
				{
					var raw = Value(args[i]);
					if (TryInt(raw, out int integer)) values.Add(new RawAffInt { data = integer });
					else if (TryFloat(raw, out float number)) values.Add(new RawAffFloat { data = number });
					else values.Add(new RawAffWord { data = Word(args[i]) });
				}
				return new RawAffSceneControl { Timing = timing, Type = Word(args[1]), Params = values };
			}

			// Play reads every argument as underscore-separated attributes.
			private string Attributes(List<Arg> values)
			{
				if (values.Count == 0) return string.Empty;
				if (values.Count == 1) return Word(values[0]);
				var parts = new string[values.Count];
				for (int i = 0; i < values.Count; i++) parts[i] = Word(values[i]);
				return string.Join("_", parts);
			}

			// Reads "[event, ...]". Arctaps go to arctaps; anything else is reported and skipped.
			private bool ReadSubevents(List<RawAffArctap> arctaps)
			{
				Advance();
				while (true)
				{
					SkipTrivia();
					if (TryConsume(']')) return true;
					int eventLine = line, eventColumn = column;
					string name = string.Empty;
					if (Peek != '(' && !TryReadIdentifier(out name)) return Fail("无法识别的子事件");
					if (!ReadArguments(subArgs)) return false;
					if (arctaps != null)
					{
						if (!Same(name, "arctap"))
							Warn(eventLine, eventColumn, "arc 事件的子事件必须为 arctap 事件，此事件将被忽略");
						else if (subArgs.Count < 1 || subArgs.Count > 3)
							Warn(eventLine, eventColumn, $"arctap 事件的参数个数应当为 1 到 3 个而非 {subArgs.Count} 个，此事件将被忽略");
						else if (!TryInt(Value(subArgs[0]), out int timing))
							WarnAt(subArgs[0], "arctap 事件的 时间 参数的值类型错误，此事件将被忽略");
						else if (!WidthAndMine(subArgs, 1, out float width, out bool mine))
							WarnAt(subArgs[1], "arctap 事件的宽度或地雷参数无效，此事件将被忽略");
						else arctaps.Add(new RawAffArctap { Timing = timing, Width = width, IsMine = mine });
					}
					SkipTrivia();
					if (TryConsume(']')) return true;
					if (!TryConsume(',')) return Fail("子事件之间缺少逗号");
				}
			}

			// Splits "(a, b, ...)" at top-level commas; quotes and nested parentheses stay in one argument.
			private bool ReadArguments(List<Arg> target)
			{
				target.Clear();
				SkipTrivia();
				if (!TryConsume('(')) return Fail("缺少左括号");
				SkipTrivia();
				if (TryConsume(')')) return true;
				int start = index, argLine = line, argColumn = column, depth = 0;
				char quote = '\0';
				bool escaped = false;
				while (index < source.Length)
				{
					char current = source[index];
					if (quote != '\0')
					{
						Advance();
						if (escaped) escaped = false;
						else if (current == '\\') escaped = true;
						else if (current == quote) quote = '\0';
						continue;
					}
					if (current == '\'' || current == '"') quote = current;
					else if (current == '(') depth++;
					else if (current == ')' && depth > 0) depth--;
					else if (current == ')' || (current == ',' && depth == 0))
					{
						target.Add(new Arg { Start = start, Length = index - start, Line = argLine, Column = argColumn });
						Advance();
						if (current == ')') return true;
						SkipTrivia();
						start = index; argLine = line; argColumn = column;
						continue;
					}
					Advance();
				}
				return Fail("括号没有闭合");
			}

			private bool TryReadIdentifier(out string identifier)
			{
				int start = index;
				while (index < source.Length)
				{
					char value = source[index];
					if (value == '(' || value == ';' || value == '{' || value == '}' || value == '[' || value == ']'
						|| value == ',' || char.IsWhiteSpace(value)) break;
					Advance();
				}
				identifier = source.Substring(start, index - start);
				SkipTrivia();
				return identifier.Length > 0;
			}

			private char Peek => index < source.Length ? source[index] : '\0';

			private void Advance()
			{
				if (index >= source.Length) return;
				char value = source[index++];
				if (value == '\r')
				{
					if (index < source.Length && source[index] == '\n') index++;
					line++; column = 0;
				}
				else if (value == '\n') { line++; column = 0; }
				else column++;
			}

			private void SkipTrivia()
			{
				while (index < source.Length)
				{
					if (char.IsWhiteSpace(source[index])) Advance();
					else if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
						while (index < source.Length && source[index] != '\r' && source[index] != '\n') Advance();
					else break;
				}
			}

			private bool TryConsume(char value)
			{
				if (Peek != value) return false;
				Advance();
				return true;
			}

			private bool Fail(string reason)
			{
				chart.error.Add($"第 {Number(line + 1)} 行第 {Number(column + 1)} 列，谱面文件语法错误：{reason}");
				failed = true;
				return false;
			}

			private void Warn(int zeroLine, int zeroColumn, string text) =>
				chart.warning.Add($"第 {Number(zeroLine + 1)} 行第 {Number(zeroColumn + 1)} 列，{text}");

			private void WarnAt(Arg arg, string text) => Warn(arg.Line, arg.Column, text);

			private bool CountBetween(string type, int min, int max, int eventLine, int eventColumn)
			{
				if (args.Count >= min && args.Count <= max) return true;
				string expected = min == max ? $"{min}" : $"{min} 到 {max}";
				Warn(eventLine, eventColumn, $"{type} 事件的参数个数应当为 {expected} 个而非 {args.Count} 个，此事件将被忽略");
				return false;
			}

			private bool Int(Arg arg, string type, string field, out int value)
			{
				if (TryInt(Value(arg), out value)) return true;
				WarnAt(arg, $"{type} 事件的 {field} 参数的值类型错误，此事件将被忽略");
				return false;
			}

			private bool Float(Arg arg, string type, string field, out float value)
			{
				if (TryFloat(Value(arg), out value)) return true;
				WarnAt(arg, $"{type} 事件的 {field} 参数的值类型错误，此事件将被忽略");
				return false;
			}

			// Integer tracks keep their number; a decimal is a normalized lane position.
			private bool Lane(Arg arg, out int track, out float? lane)
			{
				lane = null;
				var raw = Value(arg);
				if (TryInt(raw, out track)) return true;
				if (!TryFloat(raw, out float value) || float.IsNaN(value) || float.IsInfinity(value)) return false;
				lane = value;
				track = Mathf.Clamp(Mathf.FloorToInt(value * 4) + 1, 0, 5);
				return true;
			}

			// Optional "width", "4" (mine) or "width, 4" after the required arguments, as in Play.
			private bool WidthAndMine(List<Arg> values, int start, out float width, out bool mine)
			{
				width = 1;
				mine = false;
				int count = values.Count - start;
				if (count == 0) return true;
				if (count == 1)
				{
					if (TryInt(Value(values[start]), out int color) && color == 4) return mine = true;
					return TryFloat(Value(values[start]), out width);
				}
				return count == 2 && TryFloat(Value(values[start]), out width)
					&& TryInt(Value(values[start + 1]), out int mineColor) && mineColor == 4 && (mine = true);
			}

			private bool SlideCoordinate(Arg arg, out float coordinate)
			{
				if (!TryFloat(Value(arg), out coordinate)) return false;
				coordinate = RawAffSlide.RoundCoordinate(coordinate);
				return true;
			}

			private bool SlideCurve(Arg arg, out int curve)
			{
				if (Same(Word(arg), "b")) { curve = 3; return true; }
				return TryInt(Value(arg), out curve) && curve >= 0 && curve <= 3;
			}

			private bool SlideHeightValue(Arg arg, out SlideHeight height)
			{
				height = SlideHeight.Sky;
				if (TryInt(Value(arg), out int number) && number >= 0 && number <= 2)
				{
					height = (SlideHeight)number;
					return true;
				}
				// 已废弃的 isfloor Slide 语法，预计两个月后（2026-12-18）移除。
				// 加载时自动迁移 true -> 0、false -> 1，保存时只输出高度枚举，旧谱仍可打开。
				if (!bool.TryParse(Word(arg), out bool isFloor)) return false;
				height = isFloor ? SlideHeight.Ground : SlideHeight.Sky;
				return true;
			}

			private ReadOnlySpan<char> Value(Arg arg) => Unquote(source.AsSpan(arg.Start, arg.Length));

			// A trimmed argument without its quotes, as a string.
			private string Word(Arg arg)
			{
				var raw = source.AsSpan(arg.Start, arg.Length).Trim();
				if (!IsQuoted(raw)) return raw.ToString();
				return raw.Slice(1, raw.Length - 2).ToString().Replace("\\\"", "\"").Replace("\\'", "'");
			}
		}

		private static ReadOnlySpan<char> Unquote(ReadOnlySpan<char> raw)
		{
			raw = raw.Trim();
			return IsQuoted(raw) ? raw.Slice(1, raw.Length - 2) : raw;
		}

		private static bool IsQuoted(ReadOnlySpan<char> raw) => raw.Length >= 2
			&& ((raw[0] == '"' && raw[raw.Length - 1] == '"') || (raw[0] == '\'' && raw[raw.Length - 1] == '\''));

		private static bool TryInt(ReadOnlySpan<char> raw, out int value) =>
			int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

		private static bool TryFloat(ReadOnlySpan<char> raw, out float value) =>
			float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

		private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

		private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static bool TryHexColor(string raw, out Color32 color)
		{
			bool success = HexColor.TryParse(raw, out Rgba32 parsed);
			color = new Color32(parsed.R, parsed.G, parsed.B, parsed.A);
			return success;
		}

		private static readonly Dictionary<string, ArcLineType> LineTypes = new Dictionary<string, ArcLineType>
		{
			["true"] = ArcLineType.TrueIsVoid,
			["false"] = ArcLineType.FalseNotVoid,
			["designant"] = ArcLineType.Designant,
		};

		private static readonly Dictionary<string, ArcCurveType> CurveTypes = new Dictionary<string, ArcCurveType>
		{
			["b"] = ArcCurveType.B,
			["s"] = ArcCurveType.S,
			["si"] = ArcCurveType.Si,
			["so"] = ArcCurveType.So,
			["sisi"] = ArcCurveType.SiSi,
			["soso"] = ArcCurveType.SoSo,
			["siso"] = ArcCurveType.SiSo,
			["sosi"] = ArcCurveType.SoSi,
		};

		private static readonly Dictionary<string, CameraEaseType> CameraTypes = new Dictionary<string, CameraEaseType>
		{
			["l"] = CameraEaseType.L,
			["s"] = CameraEaseType.S,
			["qi"] = CameraEaseType.Qi,
			["qo"] = CameraEaseType.Qo,
			["reset"] = CameraEaseType.Reset,
		};
	}
}

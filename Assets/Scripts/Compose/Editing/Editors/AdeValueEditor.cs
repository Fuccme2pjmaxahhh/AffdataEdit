using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Dropdown = TMPro.TMP_Dropdown;
using Arcade.Gameplay.Chart;
using Arcade.Compose.Command;
using Arcade.Gameplay;
using Cysharp.Threading.Tasks;
using System.Threading;
using Arcade.Util.UniTaskHelper;
using Arcade.Util.UI;
using System.Globalization;
using Affdata.Core.Chart;

namespace Arcade.Compose.Editing
{
	public class AdeValueEditor : AdeOperation, INoteSelectEvent
	{
		public static AdeValueEditor Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public RectTransform Panel;
		public RectTransform Timing, Track, EndTiming, StartPos, EndPos, CurveType, Color, IsVoid, SelectParent, SeparateArctap, TimingGroup;
		public RectTransform MoveTiming, MoveTrack, MoveEndTiming, MoveStartPos, MoveEndPos;

		public Image IsVoidIntermediate;
        private RectTransform smoothnessRow, designantRow;
        private InputField smoothnessInput;
        private Toggle designantToggle;
        private RectTransform tapWidthRow;
        private InputField tapWidthInput;
		public void OnNoteSelect(ArcNote note)
		{
			UpdateFields();
		}
		public void OnNoteDeselect(ArcNote note)
		{
			UpdateFields();
		}
		public void OnNoteDeselectAll()
		{
			UpdateFields();
		}
		private void OnCommandExecuted(ICommand command, bool undo)
		{
			UpdateFields();
		}

		private void Awake()
		{
			Instance = this;
		}
		private void Start()
		{
			curveTypeDropdownHelper = new DropdownHelper<ArcCurveType?>(CurveType.GetComponentInChildren<Dropdown>());
			colorDropdownHelper = new DropdownHelper<int?>(Color.GetComponentInChildren<Dropdown>());
			timingGroupDropdownHelper = new DropdownHelper<ArcTimingGroupOption?>(TimingGroup.GetComponentInChildren<Dropdown>());
			CreateArcExtensionRows();
			// Native caret/word selection, plus triple-click selection of a whole field.
			foreach (var input in Panel.GetComponentsInChildren<InputField>(true))
				AdeTextSelection.Configure(input);
			AdeUiTheme.Refresh();
            Track.GetComponentInChildren<InputField>(true).contentType = InputField.ContentType.Standard;
            Track.GetComponentInChildren<InputField>(true).characterLimit = 0;
            AdeSelectionManager.Instance.NoteEventListeners.Add(this);
			AdeCommandManager.Instance.onCommandExecuted += OnCommandExecuted;
		}

        private void CreateArcExtensionRows()
        {
            tapWidthRow = Instantiate(Timing, Timing.parent);
            tapWidthRow.name = "Tap Width";
            tapWidthRow.GetComponentsInChildren<Text>(true)[0].text = "宽度";
            foreach (var button in tapWidthRow.GetComponentsInChildren<Button>(true)) button.gameObject.SetActive(false);
            tapWidthInput = tapWidthRow.GetComponentInChildren<InputField>(true);
            tapWidthInput.contentType = InputField.ContentType.Standard;
            tapWidthInput.onEndEdit = new InputField.SubmitEvent();
            tapWidthInput.onValueChanged = new InputField.OnChangeEvent();
            tapWidthInput.onEndEdit.AddListener(_ =>
            {
                if (!ArcadeComposeManager.Instance.ArcadePreference.EnableTapWidthEditing) return;
                HandleValueChange(AdeTextSelection.NormalizeForCommit(tapWidthInput),
                    (string raw, ref float result) =>
                    {
                        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                            || float.IsNaN(result) || float.IsInfinity(result) || result <= 0)
                            return new ValueChangeErrorMessage { message = "宽度必须为大于 0 的有限数值（1 为普通大小）" };
                        return null;
                    }, (value, note) => note is ArcTap ? (ValueChangeErrorMessage?)null : new ValueChangeErrorMessage { message = "请选择地键" },
                    (value, note) => ((ArcTap)note).Width = value);
            });
            tapWidthRow.SetSiblingIndex(TimingGroup.GetSiblingIndex());
            tapWidthRow.gameObject.SetActive(false);
            smoothnessRow = Instantiate(EndTiming, EndTiming.parent);
            smoothnessRow.name = "Arc Smoothness";
            smoothnessRow.GetComponentsInChildren<Text>(true)[0].text = "平滑度";
            foreach (var button in smoothnessRow.GetComponentsInChildren<Button>(true)) button.gameObject.SetActive(false);
            smoothnessInput = smoothnessRow.GetComponentInChildren<InputField>(true);
            smoothnessInput.contentType = InputField.ContentType.DecimalNumber;
            smoothnessInput.onEndEdit = new InputField.SubmitEvent();
            smoothnessInput.onValueChanged = new InputField.OnChangeEvent();
            smoothnessInput.onEndEdit.AddListener(text => HandleValueChange(AdeTextSelection.NormalizeForCommit(smoothnessInput),
                (string raw, ref float result) =>
                {
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                        || float.IsNaN(result) || float.IsInfinity(result) || result < 1)
                        return new ValueChangeErrorMessage { message = "平滑度必须为不小于 1 的有限数值" };
                    return null;
                }, (value, note) => null, (value, note) => ((ArcArc)note).Smoothness = value));
            designantRow = Instantiate(IsVoid, IsVoid.parent);
            designantRow.name = "Arc Designant";
            designantRow.GetComponentsInChildren<Text>(true)[0].text = "Designant";
            foreach (var image in designantRow.GetComponentsInChildren<Image>(true))
                if (image.name == IsVoidIntermediate.name) image.enabled = false;
            designantToggle = designantRow.GetComponentInChildren<Toggle>(true);
            designantToggle.interactable = true;
            designantToggle.onValueChanged = new Toggle.ToggleEvent();
            designantToggle.onValueChanged.AddListener(value =>
            {
                var commands = new List<ICommand>();
                foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
                {
                    if (!(note is ArcArc arc)) continue;
                    var edited = (ArcArc)arc.Clone();
                    edited.LineType = value ? ArcLineType.Designant : arc.ArcTaps.Count > 0 ? ArcLineType.TrueIsVoid : ArcLineType.FalseNotVoid;
                    commands.Add(new EditArcEventCommand(arc, edited));
                }
                if (commands.Count > 0) AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "修改 Designant"));
            });
            smoothnessRow.SetSiblingIndex(TimingGroup.GetSiblingIndex());
            designantRow.SetSiblingIndex(TimingGroup.GetSiblingIndex());
            smoothnessRow.gameObject.SetActive(false);
            designantRow.gameObject.SetActive(false);
        }

		private void OnDestroy()
		{
			AdeSelectionManager.Instance.NoteEventListeners.Remove(this);
			AdeCommandManager.Instance.onCommandExecuted -= OnCommandExecuted;
		}

		public void UpdateFields()
		{
			List<ArcNote> selected = AdeSelectionManager.Instance.SelectedNotes;
            if (tapWidthRow)
                UpdateField(note => note is ArcTap && ArcadeComposeManager.Instance.ArcadePreference.EnableTapWidthEditing,
                    note => ((ArcTap)note).Width.ToString("f3", CultureInfo.InvariantCulture), "-",
                    active => tapWidthRow.gameObject.SetActive(active), text => tapWidthInput.SetTextWithoutNotify(text));
            if (smoothnessRow)
            {
                UpdateField(note => note is ArcArc, note => ((ArcArc)note).EffectiveSmoothness.ToString(CultureInfo.InvariantCulture), "-",
                    active => smoothnessRow.gameObject.SetActive(active), value => smoothnessInput.SetTextWithoutNotify(value));
                UpdateField<bool?>(note => note is ArcArc, note => ((ArcArc)note).Designant, null,
                    active => designantRow.gameObject.SetActive(active), value => designantToggle.SetIsOnWithoutNotify(value == true));
            }
            int count = selected.Count;
			if (count == 0)
			{
				Panel.gameObject.SetActive(false);
				return;
			}
			else
			{
				SelectParent.gameObject.SetActive(false);
				if (count == 1)
				{
					if (selected[0] is ArcArcTap)
					{
						SelectParent.gameObject.SetActive(true);
					}
				}
				MoveTiming.gameObject.SetActive(count == 1);
				MoveTrack.gameObject.SetActive(count == 1);
				MoveEndTiming.gameObject.SetActive(count == 1);
				MoveStartPos.gameObject.SetActive(count == 1);
				MoveEndPos.gameObject.SetActive(count == 1);
				SeparateArctap.gameObject.SetActive(GetSeparateArctapAvailable());

				UpdateField(
					(note) => true,
					(note) => note.Timing.ToString(CultureInfo.InvariantCulture),
					"-",
					(active) => Timing.gameObject.SetActive(active),
					(data) => Timing.GetComponentInChildren<InputField>().SetTextWithoutNotify(data)
				);
				UpdateField(
					(note) => note is ArcLongNote,
					(note) => (note as ArcLongNote).EndTiming.ToString(CultureInfo.InvariantCulture),
					"-",
					(active) => EndTiming.gameObject.SetActive(active),
					(data) => EndTiming.GetComponentInChildren<InputField>().SetTextWithoutNotify(data)
				);
				UpdateField(
					(note) => note is ArcTap || note is ArcHold,
					(note) => note is ArcTap ? LanePosition.Format(((ArcTap)note).Track, ((ArcTap)note).FloatLane) : LanePosition.Format(((ArcHold)note).Track, ((ArcHold)note).FloatLane),
					"-",
					(active) => Track.gameObject.SetActive(active),
					(data) => Track.GetComponentInChildren<InputField>().SetTextWithoutNotify(data)
				);
				UpdateField(
					(note) => note is ArcArc,
					(note) => $"{(note as ArcArc).XStart.ToString("f3", CultureInfo.InvariantCulture)},{(note as ArcArc).YStart.ToString("f3", CultureInfo.InvariantCulture)}",
					"-,-",
					(active) => StartPos.gameObject.SetActive(active),
					(data) => StartPos.GetComponentInChildren<InputField>().SetTextWithoutNotify(data)
				);
				UpdateField(
					(note) => note is ArcArc,
					(note) => $"{(note as ArcArc).XEnd.ToString("f3", CultureInfo.InvariantCulture)},{(note as ArcArc).YEnd.ToString("f3", CultureInfo.InvariantCulture)}",
					"-,-",
					(active) => EndPos.gameObject.SetActive(active),
					(data) => EndPos.GetComponentInChildren<InputField>().SetTextWithoutNotify(data)
				);
				UpdateField<ArcCurveType?>(
					(note) => note is ArcArc,
					(note) => (note as ArcArc).CurveType,
					null,
					(active) => CurveType.gameObject.SetActive(active),
					(data) => ApplyCurveTypeDropDown(data)
				);
				UpdateField<int?>(
					(note) => note is ArcArc,
					(note) => (note as ArcArc).Color,
					null,
					(active) => Color.gameObject.SetActive(active),
					(data) => ApplyColorDropDown(data)
				);
				UpdateField<bool?>(
					(note) => note is ArcArc,
					(note) => (note as ArcArc).IsVoid,
					null,
					(active) => IsVoid.gameObject.SetActive(active),
					(data) => ApplyIsVoid(data)
				);
				IsVoid.GetComponentInChildren<Toggle>().interactable = GetEditIsVoidAvailable();
				UpdateField<ArcTimingGroupOption?>(
					(note) => note is ISetableTimingGroup,
					(note) => new ArcTimingGroupOption { timingGroup = (note as IHasTimingGroup).TimingGroup },
					null,
					(active) => TimingGroup.gameObject.SetActive(active),
					(data) => ApplyTimingGroupDropdown(data)
				);
				Panel.gameObject.SetActive(true);
			}
		}

		delegate bool GetActive(ArcNote note);
		delegate TData GetValue<TData>(ArcNote note);
		delegate void ApplyActive(bool active);
		delegate void ApplyData<TData>(TData data);

		private void UpdateField<TData>(GetActive getActive, GetValue<TData> getValue, TData genericValue, ApplyActive applyActive, ApplyData<TData> applyData)
		{
			List<ArcNote> selected = AdeSelectionManager.Instance.SelectedNotes;
			bool active = true;
			TData data = default;
			bool first = true;
			foreach (var note in selected)
			{
				if (!getActive(note))
				{
					active = false;
					break;
				}
				TData newData = getValue(note);
				if (first)
				{
					data = newData;
					first = false;
				}
				else
				{
					if (!data.Equals(newData))
					{
						data = genericValue;
					}
				}
			}
			applyActive(active);
			if (active)
			{
				applyData(data);
			}
		}

		private DropdownHelper<ArcCurveType?> curveTypeDropdownHelper;

		private Dictionary<ValueTuple<ArcCurveType?>, string> curveTypeToDropDownLabel = new Dictionary<ValueTuple<ArcCurveType?>, string>{
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.B),"B"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.S),"S"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.Si),"Si"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.So),"So"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.SiSi),"SiSi"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.SiSo),"SiSo"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.SoSi),"SoSi"},
			{ValueTuple.Create<ArcCurveType?>(ArcCurveType.SoSo),"SoSo"},
			{ValueTuple.Create<ArcCurveType?>(null),"-"},
		};

		private readonly List<ArcCurveType?> defaultCurveTypeOptions = new List<ArcCurveType?>{
			ArcCurveType.B,
			ArcCurveType.S,
			ArcCurveType.Si,
			ArcCurveType.So,
			ArcCurveType.SiSi,
			ArcCurveType.SiSo,
			ArcCurveType.SoSi,
			ArcCurveType.SoSo,
		};
		private readonly List<ArcCurveType?> unknownCurveTypeOptions = new List<ArcCurveType?>{
			null,
			ArcCurveType.B,
			ArcCurveType.S,
			ArcCurveType.Si,
			ArcCurveType.So,
			ArcCurveType.SiSi,
			ArcCurveType.SiSo,
			ArcCurveType.SoSi,
			ArcCurveType.SoSo,
		};

		private void ApplyCurveTypeDropDown(ArcCurveType? data)
		{
			if (data == null)
			{
				curveTypeDropdownHelper.UpdateOptions(unknownCurveTypeOptions, (curveType, _) => curveTypeToDropDownLabel[ValueTuple.Create(curveType)]);
			}
			else
			{
				curveTypeDropdownHelper.UpdateOptions(defaultCurveTypeOptions, (curveType, _) => curveTypeToDropDownLabel[ValueTuple.Create(curveType)]);
			}
			curveTypeDropdownHelper.SetValueWithoutNotify(data);
		}

		private DropdownHelper<int?> colorDropdownHelper;

		private Dictionary<ValueTuple<int?>, string> colorToDropDownLabel = new Dictionary<ValueTuple<int?>, string>{
			{ValueTuple.Create<int?>(null),"-"},
			{ValueTuple.Create<int?>(ArcColors.BlueId),"蓝"},
			{ValueTuple.Create<int?>(ArcColors.RedId),"红"},
			{ValueTuple.Create<int?>(ArcColors.GreenId),"绿"},
			{ValueTuple.Create<int?>(ArcColors.GrayId),"灰"},
			{ValueTuple.Create<int?>(ArcColors.BlackId),"黑"},
		};

		private string GetColorOptionString(int? color)
		{
			if (colorToDropDownLabel.ContainsKey(ValueTuple.Create(color)))
			{
				return colorToDropDownLabel[ValueTuple.Create(color)];
			}
			else
			{
				return "其他";
			}
		}

		private void ApplyColorDropDown(int? data)
		{
			List<int?> options = new List<int?> { ArcColors.BlueId, ArcColors.RedId, ArcColors.GreenId, ArcColors.GrayId, ArcColors.BlackId };
			if (data == null)
			{
				options.Insert(0, null);
			}
			else if (!options.Contains(data))
			{
				options.Add(data);
			}
			colorDropdownHelper.UpdateOptions(options, (color, _) => GetColorOptionString(color));
			colorDropdownHelper.SetValueWithoutNotify(data);
		}

		private void ApplyIsVoid(bool? data)
		{
			IsVoidIntermediate.enabled = data == null;
			IsVoid.GetComponentInChildren<Toggle>().SetIsOnWithoutNotify(data == true);
		}

		private struct ArcTimingGroupOption
		{
			public ArcTimingGroup timingGroup;
		}
		private DropdownHelper<ArcTimingGroupOption?> timingGroupDropdownHelper;

		private string GetArcTimingGroupOptionString(ArcTimingGroupOption? color)
		{
			if (color == null)
			{
				return "-";
			}
			else if (color.Value.timingGroup == null)
			{
				return "默认";
			}
			else
			{
				return color.Value.timingGroup.Id.ToString(CultureInfo.InvariantCulture);
			}
		}
		private void ApplyTimingGroupDropdown(ArcTimingGroupOption? data)
		{
			List<ArcTimingGroupOption?> options = new List<ArcTimingGroupOption?>();
			if (data == null)
			{
				options.Add(null);
			}
			options.Add(new ArcTimingGroupOption { timingGroup = null });
			foreach (var tg in ArcTimingManager.Instance.timingGroups)
			{
				options.Add(new ArcTimingGroupOption { timingGroup = tg });
			}
			timingGroupDropdownHelper.UpdateOptions(options, (option, _) => GetArcTimingGroupOptionString(option));
			timingGroupDropdownHelper.SetValueWithoutNotify(data);
		}

		private struct ValueChangeErrorMessage
		{
			public string message;
		}
		private delegate ValueChangeErrorMessage? ParseValue<TRaw, TValue>(TRaw raw, ref TValue result);
		private delegate ValueChangeErrorMessage? ValidateValue<TValue>(TValue value, ArcNote note);
		private delegate void ApplyValue<TValue>(TValue value, ArcNote note);

		private void HandleValueChange<TRaw, TValue>(TRaw raw, ParseValue<TRaw, TValue> parseValue, ValidateValue<TValue> validateValue, ApplyValue<TValue> applyValue)
		{
			TValue result = default;
			var parseValueErrorMessage = parseValue(raw, ref result);
			if (parseValueErrorMessage != null)
			{
				AdeToast.Instance.Show(parseValueErrorMessage.Value.message);
				UpdateFields();
				return;
			}
			foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
			{
				var validateValueErrorMessage = validateValue(result, note);
				if (validateValueErrorMessage != null)
				{
					AdeToast.Instance.Show(validateValueErrorMessage.Value.message);
					UpdateFields();
					return;
				}
			}
			List<EditArcEventCommand> commands = new List<EditArcEventCommand>();
			foreach (var n in AdeSelectionManager.Instance.SelectedNotes)
			{
				var ne = n.Clone();
				applyValue(result, ne as ArcNote);
				commands.Add(new EditArcEventCommand(n, ne));
			}
			if (commands.Count == 1)
			{
				AdeCommandManager.Instance.Add(commands[0]);
			}
			else if (commands.Count > 1)
			{
				AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "批量修改 Note"));
			}
		}

		public void OnTiming(InputField inputField)
		{
			HandleValueChange(
				AdeTextSelection.NormalizeForCommit(inputField),
				(string raw, ref int result) =>
				{
					if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
					{
						return new ValueChangeErrorMessage { message = "时间数值格式错误" };
					}
					if (result < 0)
					{
						return new ValueChangeErrorMessage { message = "时间数值不能为负数" };
					}
					return null;
				},
				(value, note) =>
				{
					if (note is ArcSlide slide && ((long)slide.EndTiming - value < 2 || (long)slide.EndTiming - value > int.MaxValue))
						return new ValueChangeErrorMessage { message = "Slide 时长无效（至少 2ms）" };
					if (note is ArcHold hold)
					{
						if (hold.EndTiming <= value)
						{
							return new ValueChangeErrorMessage { message = "Hold 的时间不能晚于结束时间" };
						}
					}
					if (note is ArcArc arc)
					{
						if (arc.EndTiming < value || (arc.EndTiming <= value && arc.ArcTaps.Count > 0))
						{
							return new ValueChangeErrorMessage { message = "Arc 的时间不能晚于结束时间" };
						}
						foreach (var arctap in arc.ArcTaps)
						{
							if (arctap.Timing < value)
							{
								if (!AdeSelectionManager.Instance.SelectedNotes.Contains(arctap))
								{
									return new ValueChangeErrorMessage { message = "Arc 的时间不能晚于其上 Arctap 的时间" };
								}
							}
						}
					}
					if (note is ArcArcTap)
					{
						var arctap = note as ArcArcTap;
						if (arctap.Arc.Timing > value || arctap.Arc.EndTiming < value)
						{
							if (!AdeSelectionManager.Instance.SelectedNotes.Contains(arctap.Arc))
							{
								return new ValueChangeErrorMessage { message = "Arctap 的时间不能超过所在 Arc 的时间范围" };
							}
						}
					}
					return null;
				},
				(value, note) =>
				{
					note.Timing = value;
				}
			);
		}
        public void OnTrack(InputField inputField)
        {
            HandleValueChange(AdeTextSelection.NormalizeForCommit(inputField),
                (string raw, ref string result) =>
                {
                    int track; float? lane;
                    if (!LanePosition.TryParse(raw, out track, out lane))
                        return new ValueChangeErrorMessage { message = "轨道：整数 0～5，或 FloatLane 小数（0.5 为中央）" };
                    result = raw;
                    return null;
                },
                (value, note) => null,
                (value, note) =>
                {
                    int track; float? lane;
                    LanePosition.TryParse(value, out track, out lane);
                    if (note is ArcTap tap) { tap.Track = track; tap.FloatLane = lane; }
                    if (note is ArcHold hold) { hold.Track = track; hold.FloatLane = lane; }
                });
        }
		public void OnEndTiming(InputField inputField)
		{
			HandleValueChange(
				AdeTextSelection.NormalizeForCommit(inputField),
				(string raw, ref int result) =>
				{
					if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
					{
						return new ValueChangeErrorMessage { message = "时间数值格式错误" };
					}
					if (result < 0)
					{
						return new ValueChangeErrorMessage { message = "时间数值不能为负数" };
					}
					return null;
				},
				(value, note) =>
				{
					if (note is ArcSlide slide && ((long)value - slide.Timing < 2 || (long)value - slide.Timing > int.MaxValue))
						return new ValueChangeErrorMessage { message = "Slide 时长无效（至少 2ms）" };
					if (note is ArcHold)
					{
						var hold = note as ArcHold;
						if (hold.Timing >= value)
						{
							return new ValueChangeErrorMessage { message = "Hold 的结束时间不能早于时间" };
						}
					}
					if (note is ArcArc)
					{
						var arc = note as ArcArc;
						if (arc.Timing > value || (arc.Timing >= value && arc.ArcTaps.Count > 0))
						{
							return new ValueChangeErrorMessage { message = "Arc 的结束时间不能早于时间" };
						}
						foreach (var arctap in arc.ArcTaps)
						{
							if (arctap.Timing > value)
							{
								return new ValueChangeErrorMessage { message = "Arc 的结束时间不早于其上 Arctap 的时间" };
							}
						}
					}
					return null;
				},
				(value, note) =>
				{
					(note as ArcLongNote).EndTiming = value;
				}
			);
		}

		private ValueChangeErrorMessage? ParseCoord(string raw, ref Vector2 result)
		{
			string[] coords = raw.Split(',');
			float x, y;
			if (coords.Length != 2)
			{
				return new ValueChangeErrorMessage { message = "坐标位置格式错误" };
			}
			if (!float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
			{
				return new ValueChangeErrorMessage { message = "坐标位置格式错误" };
			}
			if (!float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
			{
				return new ValueChangeErrorMessage { message = "坐标位置格式错误" };
			}
			result = new Vector2(x, y);
			return null;
		}

		public void OnStartPos(InputField inputField)
		{
			HandleValueChange<string, Vector2>(
				AdeTextSelection.NormalizeForCommit(inputField),
				ParseCoord,
				(value, note) =>
				{
					return null;
				},
				(value, note) =>
				{
					(note as ArcArc).XStart = value.x;
					(note as ArcArc).YStart = value.y;
				}
			);
		}
		public void OnEndPos(InputField inputField)
		{
			HandleValueChange<string, Vector2>(
				AdeTextSelection.NormalizeForCommit(inputField),
				ParseCoord,
				(value, note) =>
				{
					return null;
				},
				(value, note) =>
				{
					(note as ArcArc).XEnd = value.x;
					(note as ArcArc).YEnd = value.y;
				}
			);
		}
		public void OnCurveType(Dropdown dropdown)
		{
			ArcCurveType? curveType = curveTypeDropdownHelper.QueryDataById(dropdown.value);
			if (curveType == null)
			{
				return;
			}
			HandleValueChange(
				curveType.Value,
				(ArcCurveType raw, ref ArcCurveType result) =>
				{
					result = raw;
					return null;
				},
				(value, note) =>
				{
					return null;
				},
				(value, note) =>
				{
					(note as ArcArc).CurveType = value;
				}
			);
		}
		public void OnColor(Dropdown dropdown)
		{
			int? color = colorDropdownHelper.QueryDataById(dropdown.value);
			if (color == null)
			{
				return;
			}
			HandleValueChange(
				color.Value,
				(int raw, ref int result) =>
				{
					result = raw;
					return null;
				},
				(value, note) =>
				{
					return null;
				},
				(value, note) =>
				{
					(note as ArcArc).Color = value;
				}
			);
		}
		public void OnIsVoid(Toggle toggle)
		{
			HandleValueChange(
				toggle.isOn,
				(bool raw, ref bool result) =>
				{
					result = raw;
					return null;
				},
				(value, note) =>
				{
					if (note is ArcArc arc)
					{
						if ((!value) && arc.ArcTaps.Count > 0)
						{
							return new ValueChangeErrorMessage { message = "Arc 含有 ArcTap 时必须是黑线" };
						}
					}
					return null;
				},
				(value, note) =>
				{
					(note as ArcArc).LineType = value ? ArcLineType.TrueIsVoid : ArcLineType.FalseNotVoid;
				}
			);
		}
		public void OnTimingGroup(Dropdown dropdown)
		{
			ArcTimingGroupOption? timingGroupOption = timingGroupDropdownHelper.QueryDataById(dropdown.value);
			if (timingGroupOption == null)
			{
				return;
			}
			HandleValueChange(
				timingGroupOption.Value,
				(ArcTimingGroupOption raw, ref ArcTimingGroupOption result) =>
				{
					result = raw;
					return null;
				},
				(value, note) =>
				{
					return null;
				},
				(value, note) =>
				{
					(note as ISetableTimingGroup).TimingGroup = value.timingGroup;
				}
			);
		}

		public void OnSelectParent()
		{
			List<ArcNote> selectedNotes = AdeSelectionManager.Instance.SelectedNotes;
			if (selectedNotes.Count == 1)
			{
				if (selectedNotes[0] is ArcArcTap arcTap)
				{
					ArcArc arc = arcTap.Arc;
					AdeSelectionManager.Instance.DeselectAllNotes();
					AdeSelectionManager.Instance.SelectNote(arc);
				}
			}
		}
		private bool GetEditIsVoidAvailable()
		{
			foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
			{
				if (note is ArcArc arc)
				{
					if (arc.LineType == ArcLineType.Designant)
					{
						return false;
					}
				}
			}
			return true;

		}
		private bool GetSeparateArctapAvailable()
		{
			bool allVoidArc = true;
			bool hasArctap = false;
			foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
			{
				if (note is ArcArc arc)
				{
					if (arc.IsVoid)
					{
						if (arc.ArcTaps.Count > 0 && arc.EndTiming - arc.Timing > 1)
						{
							hasArctap = true;
						}
						continue;
					}
				}
				allVoidArc = false;
			}
			return allVoidArc && hasArctap;
		}

		public void OnSeparateArctap()
		{
			List<ICommand> commands = new List<ICommand>();
			foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
			{
				if (note is ArcArc arc)
				{
					if (arc.EndTiming - arc.Timing <= 1)
					{
						continue;
					}
					foreach (var arcTap in arc.ArcTaps)
					{
						int timing = arcTap.Timing;
						float t = 1f * (timing - arc.Timing) / (arc.EndTiming - arc.Timing);
						float x = ArcAlgorithm.X(arc.XStart, arc.XEnd, t, arc.CurveType);
						float y = ArcAlgorithm.Y(arc.YStart, arc.YEnd, t, arc.CurveType);
						x = Mathf.RoundToInt(x * 1000) / 1000f;
						y = Mathf.RoundToInt(y * 1000) / 1000f;
						commands.Add(new RemoveArcTapCommand(arc, arcTap));
						ArcArc newArc = new ArcArc()
						{
							Timing = timing,
							EndTiming = timing + 1,
							Color = arc.Color,
							Effect = "none",
							LineType = arc.LineType,
							CurveType = ArcCurveType.S,
							TimingGroup = arc.TimingGroup,
							XStart = x,
							XEnd = x,
							YStart = y,
							YEnd = y,
						};
						commands.Add(new AddArcEventCommand(newArc));
						ArcArcTap newArcTap = new ArcArcTap() { Timing = timing };
						commands.Add(new AddArcTapCommand(newArc, newArcTap));

					}
				}
			}
			if (commands.Count > 0)
			{
				AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "分离 Arctap"));
			}
		}

		private bool IsValidTiming(ArcNote note, int timing)
		{
			if (note is ArcSlide slide) return (long)slide.EndTiming - timing >= 2 && (long)slide.EndTiming - timing <= int.MaxValue;
			if (note is ArcArc arc)
			{
				if (timing > arc.EndTiming)
				{
					return false;
				}
				foreach (ArcArcTap arctap in arc.ArcTaps)
				{
					if (timing > arctap.Timing)
					{
						return false;
					}
				}
			}
			else
			if (note is ArcHold hold)
			{
				if (timing >= hold.EndTiming)
				{
					return false;
				}
			}
			else
			if (note is ArcArcTap arcTap)
			{
				if (timing > arcTap.Arc.EndTiming || timing < arcTap.Arc.Timing)
				{
					return false;
				}
			}
			return true;
		}
		private async UniTask ReselectTiming(CancellationToken cancellationToken)
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			if (selected.Count != 1)
			{
				return;
			}
			ArcNote note = selected[0];
			ArcNote newNote = note.Clone() as ArcNote;
			AdeSelectionManager.Instance.DeselectAllNotes();

			EditArcEventCommand command = new EditArcEventCommand(note, newNote);

			AdeCommandManager.Instance.Prepare(command);
            var editingHold = note as ArcHold;
            bool wasEditing = editingHold != null && editingHold.IsEditing;
            if (editingHold != null) editingHold.IsEditing = true;
			try
			{
				while (true)
				{
					Action<int> updateTiming = (int timing) =>
					{
						if (IsValidTiming(note, timing))
						{
							(note as ArcArcTap)?.RemoveArcTapConnection();
							note.Timing = timing;
							newNote.Timing = timing;
							(note as ArcArcTap)?.Relocate();
							(note as ArcArc)?.Rebuild();
							(note as ArcTap)?.SetupArcTapConnection();
							(note as ArcArc)?.CalculateJudgeTimings();
							(note as ArcHold)?.CalculateJudgeTimings();
							(note as ArcSlide)?.Rebuild();
							if (note is ArcArc arc) ArcArcManager.Instance.CalculateArcRelationship(arc);
							ArcGameplayManager.Instance.ResetJudge();
						}
					};
					var newTiming = await AdeCursorManager.Instance.SelectTiming(Progress.Create(updateTiming), cancellationToken);
					updateTiming(newTiming);
					if (IsValidTiming(note, newTiming))
					{
						break;
					}
				}
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				AdeSelectionManager.Instance.SelectNote(note);
				throw ex;
			}
            finally
            {
                if (editingHold != null) editingHold.IsEditing = wasEditing;
            }
			AdeCommandManager.Instance.Commit();
			AdeSelectionManager.Instance.SelectNote(note);
		}

		private bool IsValidEndTiming(ArcNote note, int timing)
		{
			if (note is ArcSlide slide) return (long)timing - slide.Timing >= 2 && (long)timing - slide.Timing <= int.MaxValue;
			if (note is ArcArc arc)
			{
				if (timing < arc.Timing)
				{
					return false;
				}
				foreach (ArcArcTap arctap in arc.ArcTaps)
				{
					if (timing < arctap.Timing)
					{
						return false;
					}
				}
			}
			else
			if (note is ArcHold hold)
			{
				if (timing <= hold.Timing)
				{
					return false;
				}
			}
			return true;
		}

		private async UniTask ReselectEndTiming(CancellationToken cancellationToken)
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			if (selected.Count != 1)
			{
				return;
			}
			ArcNote note = selected[0];
			if (!(note is ArcArc || note is ArcHold || note is ArcSlide))
			{
				return;
			}
			ArcNote newNote = note.Clone() as ArcNote;
			AdeSelectionManager.Instance.DeselectAllNotes();

			EditArcEventCommand command = new EditArcEventCommand(note, newNote);

			AdeCommandManager.Instance.Prepare(command);
            var editingHold = note as ArcHold;
            bool wasEditing = editingHold != null && editingHold.IsEditing;
            if (editingHold != null) editingHold.IsEditing = true;
			try
			{
				while (true)
				{
					Action<int> updateEndTiming = (int endTiming) =>
					{
						if (IsValidEndTiming(note, endTiming))
						{
							((ArcLongNote)note).EndTiming = endTiming;
							((ArcLongNote)newNote).EndTiming = endTiming;
							(note as ArcArc)?.Rebuild();
							(note as ArcArc)?.CalculateJudgeTimings();
							(note as ArcHold)?.CalculateJudgeTimings();
							(note as ArcSlide)?.Rebuild();
							if (note is ArcArc arc) ArcArcManager.Instance.CalculateArcRelationship(arc);
							ArcGameplayManager.Instance.ResetJudge();
						}
					};
					var newEndTiming = await AdeCursorManager.Instance.SelectTiming(Progress.Create(updateEndTiming), cancellationToken);
					updateEndTiming(newEndTiming);
					if (IsValidEndTiming(note, newEndTiming))
					{
						break;
					}
				}
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				AdeSelectionManager.Instance.SelectNote(note);
				throw ex;
			}
            finally
            {
                if (editingHold != null) editingHold.IsEditing = wasEditing;
            }
			AdeCommandManager.Instance.Commit();
			AdeSelectionManager.Instance.SelectNote(note);
		}

		private async UniTask ReselectStartCoordinate(CancellationToken cancellationToken)
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			if (selected.Count == 1 && selected[0] is ArcSlide slide)
			{
				await ReselectSlideCenter(slide, false, cancellationToken);
				return;
			}
			if (selected.Count != 1)
			{
				return;
			}
			if (!(selected[0] is ArcArc))
			{
				return;
			}
			ArcArc note = selected[0] as ArcArc;
			ArcArc newNote = note.Clone() as ArcArc;
			AdeSelectionManager.Instance.DeselectAllNotes();

			EditArcEventCommand command = new EditArcEventCommand(note, newNote);

			AdeCommandManager.Instance.Prepare(command);
			try
			{
				Action<Vector2> updateStartCoordinate = (Vector2 coordinate) =>
				{
					note.XStart = coordinate.x;
					newNote.XStart = coordinate.x;
					note.YStart = coordinate.y;
					newNote.YStart = coordinate.y;
					note.Rebuild();
					note.CalculateJudgeTimings();
					ArcArcManager.Instance.CalculateArcRelationship(note);
				};
				var newCoordinate = await AdeCursorManager.Instance.SelectCoordinate(note.Timing, Progress.Create(updateStartCoordinate), cancellationToken);
				updateStartCoordinate(newCoordinate);
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				AdeSelectionManager.Instance.SelectNote(note);
				throw ex;
			}
			AdeCommandManager.Instance.Commit();
			AdeSelectionManager.Instance.SelectNote(note);
		}

		private async UniTask ReselectEndCoordinate(CancellationToken cancellationToken)
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			if (selected.Count == 1 && selected[0] is ArcSlide slide)
			{
				await ReselectSlideCenter(slide, true, cancellationToken);
				return;
			}
			if (selected.Count != 1)
			{
				return;
			}
			if (!(selected[0] is ArcArc))
			{
				return;
			}
			ArcArc note = selected[0] as ArcArc;
			ArcArc newNote = note.Clone() as ArcArc;
			AdeSelectionManager.Instance.DeselectAllNotes();

			EditArcEventCommand command = new EditArcEventCommand(note, newNote);

			AdeCommandManager.Instance.Prepare(command);
			try
			{
				Action<Vector2> updateEndCoordinate = (Vector2 coordinate) =>
				{
					note.XEnd = coordinate.x;
					newNote.XEnd = coordinate.x;
					note.YEnd = coordinate.y;
					newNote.YEnd = coordinate.y;
					note.Rebuild();
					note.CalculateJudgeTimings();
					ArcArcManager.Instance.CalculateArcRelationship(note);
				};
				var newCoordinate = await AdeCursorManager.Instance.SelectCoordinate(note.EndTiming, Progress.Create(updateEndCoordinate), cancellationToken);
				updateEndCoordinate(newCoordinate);
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				AdeSelectionManager.Instance.SelectNote(note);
				throw ex;
			}
			AdeCommandManager.Instance.Commit();
			AdeSelectionManager.Instance.SelectNote(note);
		}

		private async UniTask ReselectSlideCenter(ArcSlide note, bool end, CancellationToken cancellationToken)
		{
			var value = (ArcSlide)note.Clone();
			var previousGroup = AdeTimingEditor.Instance.currentTimingGroup;
			AdeSelectionManager.Instance.DeselectAllNotes();
			AdeTimingEditor.Instance.SetCurrentTimingGroup(note.TimingGroup);
			var command = new EditArcEventCommand(note, value);
			AdeCommandManager.Instance.Prepare(command);
			try
			{
				void UpdateCenter(Vector2 coordinate)
				{
					float center = .5f - ArcAlgorithm.ArcXToWorld(coordinate.x) / note.WorldWidth;
					float previous = end ? value.EndCenter : value.StartCenter;
					if (end) value.EndCenter = center; else value.StartCenter = center;
					if (!value.IsValid)
					{
						if (end) value.EndCenter = previous; else value.StartCenter = previous;
						return;
					}
					command.Do();
				}
				var coordinate = await AdeCursorManager.Instance.SelectCoordinate(end ? note.EndTiming : note.Timing,
					Progress.Create<Vector2>(UpdateCenter), cancellationToken);
				UpdateCenter(coordinate);
				AdeCommandManager.Instance.Commit();
			}
			catch (OperationCanceledException)
			{
				AdeCommandManager.Instance.Cancel();
				throw;
			}
			finally
			{
				AdeTimingEditor.Instance.SetCurrentTimingGroup(previousGroup);
				AdeSelectionManager.Instance.SelectNote(note);
			}
		}

		private async UniTask ReselectTrack(CancellationToken cancellationToken)
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			if (selected.Count != 1)
			{
				return;
			}
			ArcNote note = selected[0];
			if (!(note is ArcTap || note is ArcHold))
			{
				return;
			}
			ArcNote newNote = note.Clone() as ArcNote;
			AdeSelectionManager.Instance.DeselectAllNotes();

			EditArcEventCommand command = new EditArcEventCommand(note, newNote);

			AdeCommandManager.Instance.Prepare(command);
			try
			{
                bool isFloat = note is ArcTap ? ((ArcTap)note).FloatLane.HasValue : ((ArcHold)note).FloatLane.HasValue;
                if (isFloat)
                {
                    Action<float> updateLane = lane =>
                    {
                        if (note is ArcTap tap) { tap.FloatLane = lane; ((ArcTap)newNote).FloatLane = lane; tap.SetupArcTapConnection(); }
                        if (note is ArcHold hold) { hold.FloatLane = lane; ((ArcHold)newNote).FloatLane = lane; }
                    };
                    updateLane(await AdeCursorManager.Instance.SelectFloatLane(Progress.Create(updateLane), cancellationToken));
                }
                else

				while (true)
				{
					Action<int> updateTrack = (int track) =>
					{
						if (IsValidTiming(note, track))
						{
							if (note is ArcTap)
							{
								(note as ArcTap).Track = track;
								(newNote as ArcTap).Track = track;
							}
							else if (note is ArcHold)
							{
								(note as ArcHold).Track = track;
								(newNote as ArcHold).Track = track;
							}
							(note as ArcTap)?.SetupArcTapConnection();
							(note as ArcHold)?.CalculateJudgeTimings();
							(note as ArcSlide)?.Rebuild();
						}
					};
					var newTrack = await AdeCursorManager.Instance.SelectTrack(Progress.Create(updateTrack), cancellationToken);
					updateTrack(newTrack);
					if (IsValidTiming(note, newTrack))
					{
						break;
					}
				}
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				AdeSelectionManager.Instance.SelectNote(note);
				throw ex;
			}
			AdeCommandManager.Instance.Commit();
			AdeSelectionManager.Instance.SelectNote(note);
		}

		public void OnReselectTiming()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ReselectTiming(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public void OnReselectEndTiming()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ReselectEndTiming(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public void OnReselectStartCoordinate()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ReselectStartCoordinate(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public void OnReselectEndCoordinate()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ReselectEndCoordinate(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public void OnReselectTrack()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ReselectTrack(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public override AdeOperationResult TryExecuteOperation()
		{
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ReselectTiming))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ReselectTiming(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ReselectEndTiming))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ReselectEndTiming(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ReselectStartCoordinate))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ReselectStartCoordinate(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ReselectEndCoordinate))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ReselectEndCoordinate(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.ReselectTrack))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ReselectTrack(cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			return false;
		}
	}
}

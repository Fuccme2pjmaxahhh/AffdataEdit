using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using static UnityEngine.InputSystem.InputActionRebindingExtensions;
using System.Collections.Generic;

namespace Arcade.Compose
{

	public class AdeInputManager : MonoBehaviour
	{
		[System.NonSerialized]
		public AdeInputControl Controls;
		public AdeInputControl.ArcadeHotkeyActions Hotkeys { get => Controls.ArcadeHotkey; }
		public AdeInputControl.ArcadeInputActions Inputs { get => Controls.ArcadeInput; }
		public EventSystem EventSystem;
		public AdeHotkeyDialogContent Dialog;
		public AdeHotkeyPresets Presets { get; private set; }

		public static AdeInputManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public static bool UsesCommand => Application.platform == RuntimePlatform.OSXEditor
			|| Application.platform == RuntimePlatform.OSXPlayer;
		public static string PrimaryModifierLabel => UsesCommand ? "Cmd" : "Ctrl";
		private static string PrimaryModifierPath => UsesCommand ? "<Keyboard>/*Meta" : "<Keyboard>/ctrl";
		private static string AltModifierLabel => UsesCommand ? "Option" : "Alt";
		private static bool PrimaryModifierPressed => UsesCommand
			? Keyboard.current.leftMetaKey.isPressed || Keyboard.current.rightMetaKey.isPressed
			: Keyboard.current.ctrlKey.isPressed;

		private bool rebindFinishedThisFrame = false;
		private int textInputFrame = -2;

		private void Awake()
		{
			if (Controls == null)
			{
				Controls = new AdeInputControl();
				// Change defaults, not overrides: saved rebinds and reset keep their normal behavior.
				if (UsesCommand)
					foreach (var map in Controls.asset.actionMaps)
						for (int i = 0; i < map.bindings.Count; i++)
							if (map.bindings[i].path == "<Keyboard>/ctrl")
								map.ChangeBinding(i).WithPath(PrimaryModifierPath);
			}
			Instance = this;
			Presets = new AdeHotkeyPresets(Hotkeys.Get());
			gameObject.AddComponent<AdeEditorHotkeyCommands>();
		}
		public void OnEnable()
		{
			Presets.Load();
			Controls.Enable();
		}

		public void OnDisable()
		{
			Controls.Disable();
		}

		private void OnDestroy()
		{
			rebindingOperation?.Dispose();
			rebindingOperation = null;
			Controls?.Dispose();
			Controls = null;
		}

		public bool IsTextInputFocused => IsFocusingOnTextField();

		private bool IsFocusingOnTextField()
		{
			GameObject selected = EventSystem.currentSelectedGameObject;
			var tmpInput = selected ? selected.GetComponentInParent<InputField>() : null;
			var legacyInput = selected ? selected.GetComponentInParent<UnityEngine.UI.InputField>() : null;
			if ((tmpInput && tmpInput.isFocused) || (legacyInput && legacyInput.isFocused))
			{
				textInputFrame = Time.frameCount;
				return true;
			}
			// Submission can release TMP focus before playback reads the same Enter press.
			return Time.frameCount <= textInputFrame + 1;
		}

		public bool CheckHotkeyActionPressed(InputAction action)
		{
			if (MenuBlocksHotkey(action) || IsFocusingOnTextField() || rebindFinishedThisFrame || AdeCameraManager.ConsumesHotkey(action))
			{
				return false;
			}
			// Fixed Escape cancellation must survive shortcut conflict resolution:
			// the configurable Drag Edit Off composite also defaults to Escape.
			return action.WasPerformedThisFrame() || (action == Inputs.Cancel
				&& Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
		}

		public bool CheckHotkeyActionPressing(InputAction action)
		{
			if (MenuBlocksHotkey(action) || IsFocusingOnTextField() || rebindFinishedThisFrame || AdeCameraManager.ConsumesHotkey(action))
			{
				return false;
			}
			return action.inProgress || (action == Inputs.Cancel
				&& Keyboard.current != null && Keyboard.current.escapeKey.isPressed);
		}

		private bool MenuBlocksHotkey(InputAction action)
		{
			// Keep editing commands blocked by the menu, but allow playback while
			// holding right mouse. Compare actions so user-rebound keys work too.
			return AdeMarkingMenuManager.BlocksEditorInput
				&& action != Hotkeys.PlayWhenHolding && action != Hotkeys.PlayOrPause;
		}

		private AdeHotkeyRebindingButton rebindingButton = null;
		private RebindingOperation rebindingOperation = null;
        private bool cancelWithoutClearing;

		public AdeHotkeyRebindingButton CurrentRebindingButton
		{
			get => rebindingButton;
		}

		public void SetHotkeyRebindingButton(AdeHotkeyRebindingButton button)
		{
			// clean focus so will won't restart rebinding after complete
			EventSystem.SetSelectedGameObject(null);
			AdeHotkeyRebindingButton nextRebindingButton = button;
			InputAction action = null;
			if (button != null)
			{
				action = Hotkeys.Get().FindAction(button.HotkeyName);
				if (action == null)
				{
					Debug.LogError($"{button.HotkeyName} is not a known hotkey");
					nextRebindingButton = null;
				}
			};
			// cleanup old operation
			if (rebindingOperation != null)
			{
                cancelWithoutClearing = true;
                try { rebindingOperation.Cancel(); }
                finally { cancelWithoutClearing = false; }
			}
			// start new operation
			if (nextRebindingButton != null)
			{
				Hotkeys.Disable();
				rebindingOperation = action.PerformInteractiveRebinding()
					.WithExpectedControlType<ButtonControl>()
					.WithControlsHavingToMatchPath("<Keyboard>")
					.WithControlsExcluding("<Keyboard>/leftAltKey")
					.WithControlsExcluding("<Keyboard>/leftCtrlKey")
					.WithControlsExcluding("<Keyboard>/leftShiftKey")
					.WithControlsExcluding("<Keyboard>/rightAltKey")
					.WithControlsExcluding("<Keyboard>/rightCtrlKey")
					.WithControlsExcluding("<Keyboard>/rightShiftKey")
					.WithControlsExcluding("<Keyboard>/altKey")
					.WithControlsExcluding("<Keyboard>/ctrlKey")
					.WithControlsExcluding("<Keyboard>/shiftKey")
					.WithControlsExcluding("<Keyboard>/leftMeta")
					.WithControlsExcluding("<Keyboard>/rightMeta")
					.WithControlsExcluding("<Keyboard>/anyKey")
					.WithControlsExcluding("<Keyboard>/escapeKey")
					.WithControlsExcluding("<Keyboard>/leftAlt")
					.WithControlsExcluding("<Keyboard>/leftCtrl")
					.WithControlsExcluding("<Keyboard>/leftShift")
					.WithControlsExcluding("<Keyboard>/rightAlt")
					.WithControlsExcluding("<Keyboard>/rightCtrl")
					.WithControlsExcluding("<Keyboard>/rightShift")
					.WithControlsExcluding("<Keyboard>/alt")
					.WithControlsExcluding("<Keyboard>/ctrl")
					.WithControlsExcluding("<Keyboard>/shift")
					.WithControlsExcluding("<Keyboard>/any")
					.WithControlsExcluding("<Keyboard>/escape")
					.WithControlsExcluding("<Pointer>")
					.WithCancelingThrough(Keyboard.current.escapeKey)
					.WithMatchingEventsBeingSuppressed(true)
					.OnMatchWaitForAnother(float.PositiveInfinity)
					.OnPotentialMatch((operation) =>
					{
						if (operation.candidates.Count > 0)
						{
							operation.Complete();
						}
					})
					.OnApplyBinding((operation, bindingPath) =>
					{
						if (action.bindings.Count != 5)
						{
							Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
							return;
						}
						if (!action.bindings[0].isComposite)
						{
							Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
							return;
						}
						if (action.bindings[0].GetNameOfComposite() != "KeyWithModifiers")
						{
							Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
							return;
						}
						bool hasCtrl = PrimaryModifierPressed;
						bool hasAlt = Keyboard.current.altKey.isPressed;
						bool hasShift = Keyboard.current.shiftKey.isPressed;
						bool? needModifier1 = null;
						bool? needModifier2 = null;
						bool? needModifier3 = null;
						for (int i = 1; i < 5; i++)
						{
							InputBinding binding = action.bindings[i];
							if (binding.name == "key")
							{
								action.ApplyBindingOverride(i, bindingPath);
								continue;
							}
							bool? needModifier = null;
							if (binding.effectivePath == PrimaryModifierPath)
							{
								needModifier = hasCtrl;
							}
							else if (binding.effectivePath == "<Keyboard>/alt")
							{
								needModifier = hasAlt;
							}
							else if (binding.effectivePath == "<Keyboard>/shift")
							{
								needModifier = hasShift;
							}
							if (binding.name == "modifier1")
							{
								needModifier1 = needModifier;
							}
							else if (binding.name == "modifier2")
							{
								needModifier2 = needModifier;
							}
							else if (binding.name == "modifier3")
							{
								needModifier3 = needModifier;
							}
							else
							{
								Debug.LogError("The action for the rebinding button is not hotkey: unknown binding name");
								return;
							}
						}
						if (needModifier1 == null || needModifier2 == null || needModifier3 == null)
						{
							Debug.LogError("The action for the rebinding button is not hotkey: missing binding component");
							return;
						}
						List<string> param = new List<string>();
						param.Add($"needModifier1={needModifier1.Value.ToString().ToLower()}");
						param.Add($"needModifier2={needModifier2.Value.ToString().ToLower()}");
						param.Add($"needModifier3={needModifier3.Value.ToString().ToLower()}");
						InputBinding compositeBinding = action.bindings[0];
						// Note: LoadBindingOverridesFromJson will apply empty string
						// and SaveBindingOverridesAsJson will save null to empty string
						// so as a workaround here we set the overridePath to path
						compositeBinding.overridePath = compositeBinding.path;
						compositeBinding.overrideInteractions = $"HotKey({string.Join(",", param)})";
						action.ApplyBindingOverride(0, compositeBinding);
					})
					.OnComplete((operation) =>
					{
						CleanupCurrentOperation();
						Presets.CaptureEdit();
					})
					.OnCancel((operation) =>
					{
                        if (!cancelWithoutClearing)
                        {
                            new AdeHotkeyPresets.Binding("").Apply(action);
                            Presets.CaptureEdit();
                        }
						CleanupCurrentOperation();
					}).Start();
				rebindingButton = nextRebindingButton;
                AdeToast.Instance?.Show("请按新热键；按 Esc 退出绑定并设为未绑定");
			}
			UpdateTextForHotkeyButton(button);
		}

		private void CleanupCurrentOperation()
		{
			rebindingOperation.Dispose();
			rebindingOperation = null;
			AdeHotkeyRebindingButton lastButton = rebindingButton;
			rebindingButton = null;
			UpdateTextForHotkeyButton(lastButton);
			Hotkeys.Enable();
			rebindFinishedThisFrame = true;
		}

		public void UpdateTextForHotkeyButton(AdeHotkeyRebindingButton button)
		{
			if (button == null)
			{
				return;
			}
			if (button == rebindingButton)
			{
				string text = "<?>";
				if (Keyboard.current.shiftKey.isPressed)
				{
					text = "Shift+" + text;
				}
				if (Keyboard.current.altKey.isPressed)
				{
					text = AltModifierLabel + "+" + text;
				}
				if (PrimaryModifierPressed)
				{
					text = PrimaryModifierLabel + "+" + text;
				}
				button.Text.text = text;
				return;
			}
			InputAction action = Hotkeys.Get().FindAction(button.HotkeyName);
			if (action == null)
			{
				Debug.LogError($"{button.HotkeyName} is not a known hotkey");
				button.Text.text = "<N/A>";
				return;
			}
			if (action.bindings.Count != 5)
			{
				Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
				return;
			}
			if (!action.bindings[0].isComposite)
			{
				Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
				return;
			}
			if (action.bindings[0].GetNameOfComposite() != "KeyWithModifiers")
			{
				Debug.LogError("The action for the rebinding button is not hotkey: not a hotkey binding");
				return;
			}
			button.Text.text = "<Error>";
			bool needModifier1 = false;
			bool needModifier2 = false;
			bool needModifier3 = false;
			string interaction = action.bindings[0].effectiveInteractions;
			// Note: this depend on conventions on the effectivePath format
			if (interaction.Contains('('))
			{
				int start = interaction.IndexOf('(');
				int end = interaction.IndexOf(')');
				string param = interaction.Substring(start + 1, end - start - 1);
				foreach (string keyValue in param.Split(","))
				{
					int separator = keyValue.IndexOf('=');
					string paramKey = keyValue.Substring(0, separator);
					string paramValue = keyValue.Substring(separator + 1);
					if (paramKey == "needModifier1")
					{
						needModifier1 = paramValue == "true";
					}
					else if (paramKey == "needModifier2")
					{
						needModifier2 = paramValue == "true";
					}
					else if (paramKey == "needModifier3")
					{
						needModifier3 = paramValue == "true";
					}
				}
			}
			bool? hasCtrl = null;
			bool? hasAlt = null;
			bool? hasShift = null;
			string key = null;
			for (int i = 1; i < 5; i++)
			{
				InputBinding binding = action.bindings[i];
				if (binding.name == "key")
				{
					key = binding.ToDisplayString();
					continue;
				}
				bool? needModifier = null;
				if (binding.name == "modifier1")
				{
					needModifier = needModifier1;
				}
				else if (binding.name == "modifier2")
				{
					needModifier = needModifier2;
				}
				else if (binding.name == "modifier3")
				{
					needModifier = needModifier3;
				}
				if (needModifier == null)
				{
					Debug.LogError("The action for the rebinding button is not hotkey: unknown binding name");
					return;
				}
				if (binding.effectivePath == PrimaryModifierPath)
				{
					hasCtrl = needModifier;
				}
				else if (binding.effectivePath == "<Keyboard>/alt")
				{
					hasAlt = needModifier;
				}
				else if (binding.effectivePath == "<Keyboard>/shift")
				{
					hasShift = needModifier;
				}
			}
			if (hasCtrl == null || hasAlt == null || hasShift == null || key == null)
			{
				Debug.LogError("The action for the rebinding button is not hotkey: missing binding component");
				return;
			}
			if (string.IsNullOrEmpty(key)) { button.Text.text = "未绑定"; return; }
			string result = key;
			if (hasShift == true)
			{
				result = "Shift+" + result;
			}
			if (hasAlt == true)
			{
				result = AltModifierLabel + "+" + result;
			}
			if (hasCtrl == true)
			{
				result = PrimaryModifierLabel + "+" + result;
			}
			button.Text.text = result;
		}

		public void ResetHotkey()
		{
			SetHotkeyRebindingButton(null);
			Presets.Select("Legacy");
			foreach (var button in Dialog.RebindingButtons)
			{
				AdeInputManager.Instance.UpdateTextForHotkeyButton(button);
			}
		}

		private void Update()
		{
			if (rebindingButton != null)
			{
				UpdateTextForHotkeyButton(rebindingButton);
				if (Inputs.Cancel.IsPressed())
				{
					rebindingOperation.Cancel();
				}
			}
			else
			{
				rebindFinishedThisFrame = false;
			}
		}

	}

}

using Arcade.Gameplay;
using Arcade.Compose.Editing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Arcade.Compose
{
	public partial class AdeCameraManager
	{
		private float speed = 8, pressedTime;
		private static bool CanNavigate => FreeCameraEnabled && !AdeInputManager.Instance.IsTextInputFocused;
		public static bool FreeCameraPressing => FreeCameraEnabled && Mouse.current != null && Mouse.current.middleButton.isPressed
			&& (!AdeDragEdit.Instance || !AdeDragEdit.Instance.IsDragging);

		internal static bool ConsumesHotkey(InputAction action)
		{
			var hotkeys = AdeInputManager.Instance.Hotkeys;
			if (action == hotkeys.PlayWhenHolding || action == hotkeys.PlayOrPause) return false;
			if (FreeCameraPressing) return true;
			if (!CanNavigate || HasModifier()) return false;
			foreach (var control in action.controls)
				if (control is KeyControl key && key.isPressed
					&& (key.keyCode == Key.W || key.keyCode == Key.A || key.keyCode == Key.S || key.keyCode == Key.D)) return true;
			return false;
		}

		private static bool HasModifier()
		{
			var keyboard = Keyboard.current;
			return keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.leftAltKey.isPressed);
		}

		private void ResetFreeCamera()
		{
			var manager = ArcCameraManager.Instance;
			var camera = manager.GameplayCamera;
			camera.orthographic = false;
			camera.transform.position = new Vector3(0, 9, Mathf.Lerp(9, 8, manager.AspectBlend));
			camera.transform.LookAt(new Vector3(0, -5.5f, -20), Vector3.up);
		}

		private void Update()
		{
			if (!FreeCameraEnabled) return;
			var trans = ArcCameraManager.Instance.GameplayCamera.transform;
			if (FreeCameraPressing)
			{
				// User sensitivity: half the previous 0.4 mouse rotation multiplier.
				Rotate(trans, Mouse.current.delta.ReadValue() * .2f);
				float scroll = Mouse.current.scroll.ReadValue().y;
				if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
					&& (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer))
					scroll /= 120f;
				if (scroll != 0)
				{
					// AA's ScrollWheel axis has 0.1 sensitivity, then UpdateSpeed multiplies by 5.
					speed = Mathf.Clamp(speed + scroll * .5f, 1, 20);
					AdeToast.Instance.Show($"自由相机速度：{speed:F1}x");
				}
			}
			var gamepad = Gamepad.current;
			if (gamepad != null)
			{
				var rotation = gamepad.rightStick.ReadValue();
				Rotate(trans, new Vector2(-rotation.x, rotation.y) * 2.4f);
			}
			// AA gates translation on text editing and LeftAlt/LeftControl, not pointer position or playback.
			if (!CanNavigate || HasModifier()) return;
			var keyboard = Keyboard.current;
			var move = Vector2.zero;
			if (keyboard != null)
			{
				move.x = keyboard.aKey.isPressed ? -1 : keyboard.dKey.isPressed ? 1 : 0;
				move.y = keyboard.wKey.isPressed ? 1 : keyboard.sKey.isPressed ? -1 : 0;
			}
			float distance = (keyboard != null && keyboard.leftShiftKey.isPressed ? .5f : .1f)
				* (1 + pressedTime / 5) * speed / 8;
			trans.position += (trans.right * move.x + trans.forward * move.y) * distance;
			bool moving = move != Vector2.zero;
			var stick = gamepad != null ? gamepad.leftStick.ReadValue() : Vector2.zero;
			if (stick.sqrMagnitude > .1f)
			{
				if (stick.sqrMagnitude > .7f) distance = .5f * (1 + pressedTime / 5) * speed / 8;
				trans.position += (trans.right * stick.x + trans.forward * stick.y) * distance;
				moving = true;
			}
			pressedTime = moving ? pressedTime + Time.deltaTime : 0;
		}

		private static void Rotate(Transform trans, Vector2 delta)
		{
			if (delta == Vector2.zero) return;
			trans.Rotate(Vector3.right, -delta.y);
			var rotation = trans.localEulerAngles;
			rotation.y += delta.x;
			trans.localEulerAngles = rotation;
		}
	}
}

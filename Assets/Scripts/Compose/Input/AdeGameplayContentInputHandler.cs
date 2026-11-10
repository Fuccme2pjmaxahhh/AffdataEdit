using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Arcade.Compose
{
	public class AdeGameplayContentInputHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
	{
		public static AdeGameplayContentInputHandler Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		private bool active = false;

		public static bool InputActive => Instance != null && Instance.active && !AdeMarkingMenuManager.BlocksEditorInput;
		private void Awake()
		{
			Instance = this;
		}
		public void OnPointerEnter(PointerEventData data)
		{
			active = true;
		}
		public void OnPointerExit(PointerEventData data)
		{
			active = false;
		}

		public void OnPointerDown(PointerEventData data)
		{
			// The UI module deselects background fields on left click only. Returning to
			// chart editing with middle/right click must also release metadata focus.
			var events = EventSystem.current;
			var selected = events ? events.currentSelectedGameObject : null;
			if (selected && (selected.GetComponentInParent<TMPro.TMP_InputField>()
				|| selected.GetComponentInParent<UnityEngine.UI.InputField>()))
				events.SetSelectedGameObject(null, data);
		}
	}
}

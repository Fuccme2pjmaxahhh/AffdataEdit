using System;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Dropdown = TMPro.TMP_Dropdown;
using Arcade.Gameplay;
using LitMotion;
using LitMotion.Extensions;
using Newtonsoft.Json;
using Arcade.Compose.Dialog;
using Arcade.Compose.Editing;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using System.Globalization;
using Arcade.Compose.Command;
using UnityEngine.InputSystem;
using Arcade.Compose.Operation;
using Arcade.Compose.UI;

namespace Arcade.Compose
{
	[Serializable]
	public class ArcadePreference
	{
		public int AgreedUserAgreementVersion;
		public string ScreenResolution;
		public bool Fullscreen = false;
		public int TargetFrameRate = -1;
		public int Velocity = 165; // Displayed scroll speed 5.5, stored as speed * 30.
		public uint UndoBufferSize = 200;
		public bool PlaybackSync = false;
		public bool FixNotesTimeOnPaste = true;
        public bool EnableTapWidthEditing = false;
		public bool ShowSpectrum = false;
		public bool ShowJudgementDetails = false;
        public const int AllArcColors = 0b11111; // Bits follow the blue/red/green/gray/black color IDs.
        public int ArcColorCycleMask = AllArcColors;
        public int GetArcColorCycleMask()
        {
            int mask = ArcColorCycleMask & AllArcColors;
            return mask == 0 ? AllArcColors : mask;
        }
		public float SpectrumOpacity = 0.5f;
		// Spectrum frequency axis: false = linear (default), true = log (sliding bass lines show as curves).
		public bool SpectrumLogScale = false;
        // Missing in older preferences => false, preserving the existing dB display.
        public bool SpectrumLinearAmplitude = false;
		// User tuning of the spectrum look (Settings > 频谱); a missing field keeps these defaults.
		public SpectrumTuning SpectrumTuning = new SpectrumTuning();
		// Settings category shown when the settings window is opened from the toolbar (last one used).
		public int LastSettingsPage = 0;
		public bool KeepContextMenuOpen = true;
		public bool VerticalContextMenu = true;
		public ContextMenuCustomization ContextMenuItems = new ContextMenuCustomization();
		public bool Auto;
		public int GlobalAudioOffset = 0;
		// 画面延迟补偿 (ms): draws notes later than the music; hit sounds stay on the music.
		public int DisplayDelay = 0;
		// Also delay the picture by the output latency BASS measures at startup.
		public bool AutoOutputLatency = true;
		public Arcade.Gameplay.Chart.ChartSortMode ChartSortMode;
	}

	// Spectrum look parameters. Defaults are the built-in look; "恢复默认" resets to a new instance.
	[Serializable]
	public class SpectrumTuning
	{
		// dB mode.
		public float DbFloor = -60f, DbCeiling = -20f; // absolute part: these dB map to empty..full
		public float DbBandShare = 50f; // % of each band's own (relative) range, the rest absolute
		public float DbContrast = 1.5f; // S-curve exponent, 1 = none
		public float DbGate = -72f; // below this level cells fade out
		// Linear amplitude mode.
		public float LinearGain = 0.8f; // x raw FFT magnitude (Arcade Alpha uses 0.5 without a window)
		public float LinearContrast = 1f; // power curve, 1 = straight
		public float LinearBoost = 1.6f;
		// Log frequency axis.
		public float LogMinFrequency = 380f, LogMaxFrequency = 16000f;
	}

	public class ArcadeComposeManager : MonoBehaviour
	{
		public static ArcadeComposeManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		public const float ModeSwitchDuration = 0.3f;
		public const Ease ToEditorModeEase = Ease.OutCubic;
		public const Ease ToPlayerModeEase = Ease.InCubic;

		public bool IsEditorMode { get; set; } = true;
        public bool IsPlaybackPaused => !IsEditorMode && GameplayManager && !GameplayManager.IsPlaying;

		// AA keeps the projection aspect while reserving a uniform 5% editor border.
		public Rect EditorModeGameplayCameraRect => new Rect(.05f, .05f, .9f, .9f);
		public Camera GameplayCamera, EditorCamera;
		public ArcGameplayManager GameplayManager;
		[Header("Bar")]
		public RectTransform EditorCanvas;
		public RectTransform TopBar;
		public RectTransform BottomBar, LeftBar, RightBar, Bars;
		public RectTransform TopBarView, BottomBarView, LeftBarView, RightBarView;

		[Header("Pause")]
		public Button PauseButton;
		public Image PauseButtonImage;
		public Sprite PausePause, PausePlay, PausePausePressed, PausePlayPressed;
		[Header("Info")]
		public CanvasGroup InfoCanvasGroup;
		public Image TimingSliderHandle;
		public Sprite DefaultSliderSprite, GlowSliderSprite;
		[Header("Auto")]
		public Button AutoButton;
		[Header("Misc")]

		public UnityEvent OnPlay = new UnityEvent();
		public UnityEvent OnPause = new UnityEvent();
		public AdeNumberInputWithSlider VelocitySlider;

		public ArcadePreference ArcadePreference = new ArcadePreference();
		public Text Version;

		public Dropdown ResolutionDropdown;
		public Toggle FullscreenToggle;
		public Dropdown TargetFramerateDropdown;
		public InputField UndoBufferSizeInput;
		public Toggle PlaybackSyncToggle;
		public InputField GlobalAudioOffsetInput;

		private bool switchingMode = false;
		private readonly CompositeMotionHandle modeSwitchMotions = new CompositeMotionHandle(5);
		private int playShotTiming = 0;
		private bool previewing = false;
		private bool advancing = false;

		private void Awake()
		{
			Instance = this;
			Version.text = Application.version;
			CultureInfo.CurrentCulture = new CultureInfo("zh-Hans");
		}
		private void Start()
		{
			ArcGameplayManager.Instance.OnMusicFinished.AddListener(Pause);
			ResolutionDropdown.onValueChanged.AddListener((int value) =>
			{
				ArcadePreference.ScreenResolution = ResolutionDropdown.options[value].text;
				SetResolution(ArcadePreference.ScreenResolution, ArcadePreference.Fullscreen);
				SavePreferences();
			});
			FullscreenToggle.onValueChanged.AddListener((bool value) =>
			{
				ArcadePreference.Fullscreen = value;
				ResolutionDropdown.interactable = !value;
				SetResolution(ArcadePreference.ScreenResolution, ArcadePreference.Fullscreen);
				SavePreferences();
			});
			TargetFramerateDropdown.onValueChanged.AddListener((int value) =>
			{
				int fps;
				if (!int.TryParse(TargetFramerateDropdown.options[value].text, NumberStyles.Integer, CultureInfo.InvariantCulture, out fps))
				{
					fps = -1;
				}
				SetTargetFramerate(fps);
				ArcadePreference.TargetFrameRate = fps;
				SavePreferences();
			});
			UndoBufferSizeInput.onEndEdit.AddListener(SetUndoBufferSize);
			GlobalAudioOffsetInput.onEndEdit.AddListener(SetGlobalAudioOffset);
			PlaybackSyncToggle.onValueChanged.AddListener((bool value) =>
			{
				ArcadePreference.PlaybackSync = value;
				ArcGameplayManager.Instance.EnablePlaybackSync = value;
				SavePreferences();
			});
			VelocitySlider.onValueEdited += OnVelocityEdited;
			LoadPreferences();
			SavePreferences();
			Pause();
		}
		private enum ScrollingOperation
		{
			ScrollForward,
			ScrollBackward,
			ScrollToNextBeat,
			ScrollToPreviousBeat,
			ScrollToNextMeasure,
			ScrollToPreviousMeasure,

		}

		private ScrollingOperation? currentScrollingOperation = null;
		private bool isFirstScrollInterval = true;
		private float timeFromLastScroll = float.PositiveInfinity;

		private bool CheckScrollingOperation(ScrollingOperation? operation, float firstInterval, float thenInterval)
		{
			timeFromLastScroll += Time.deltaTime;
			if (operation != currentScrollingOperation)
			{
				currentScrollingOperation = operation;
				isFirstScrollInterval = true;
				timeFromLastScroll = operation == null ? float.PositiveInfinity : 0f;
				return true;
			}
			else
			{
				if (operation == null)
				{
					timeFromLastScroll = 0f;
					return true;
				}
				else if (isFirstScrollInterval && timeFromLastScroll > firstInterval)
				{
					isFirstScrollInterval = false;
					timeFromLastScroll = 0f;
					return true;
				}
				else if (!isFirstScrollInterval && timeFromLastScroll > thenInterval)
				{
					isFirstScrollInterval = false;
					timeFromLastScroll = 0f;
					return true;
				}
			}
			return false;
		}

		private void CheckScroll()
		{
			if (AdeCameraManager.FreeCameraPressing)
			{
				CheckScrollingOperation(null, 0f, 0f);
				return;
			}
			int timing = GameplayManager.ChartTiming;

			if (AdeInputManager.Instance.Hotkeys.ScrollToStart.WasPressedThisFrame())
			{
				CheckScrollingOperation(null, 0f, 0f);
				timing = GameplayManager.AllBeginChartTiming;
			}
			else if (AdeInputManager.Instance.Hotkeys.ScrollToEnd.WasPressedThisFrame())
			{
				CheckScrollingOperation(null, 0f, 0f);
				timing = GameplayManager.AllEndChartTiming;
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollToNextMeasure))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollToNextMeasure, 0.5f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachMeasureScroll(timing, 1);
				}
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollToPreviousMeasure))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollToPreviousBeat, 0.5f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachMeasureScroll(timing, -1);
				}
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollToNextBeat))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollToNextBeat, 0.5f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachBeatScroll(timing, 1);
				}
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollToPreviousBeat))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollToPreviousBeat, 0.5f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachBeatScroll(timing, -1);
				}
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollForward))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollForward, 0.05f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachScroll(timing, 1);
				}
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressing(AdeInputManager.Instance.Hotkeys.ScrollBackward))
			{
				if (CheckScrollingOperation(ScrollingOperation.ScrollBackward, 0.05f, 0.05f))
				{
					timing = AdeGridManager.Instance.AttachScroll(timing, -1);
				}
			}
			else if (Mouse.current.scroll.ReadValue().y != 0 && AdeGameplayContentInputHandler.InputActive)
			{
				CheckScrollingOperation(null, 0f, 0f);
				timing = AdeGridManager.Instance.AttachScroll(timing, Mouse.current.scroll.ReadValue().y);
			}
			else
			{
				CheckScrollingOperation(null, 0f, 0f);
				return;
			}

			if (timing > GameplayManager.AllEndChartTiming) timing = GameplayManager.AllEndChartTiming;
			if (timing < GameplayManager.AllBeginChartTiming) timing = GameplayManager.AllBeginChartTiming;

			if (GameplayManager.ChartTiming != Mathf.RoundToInt(timing))
			{
				GameplayManager.ChartTiming = Mathf.RoundToInt(timing);
				GameplayManager.ResetJudge();
			}
		}

		private void Update()
		{
			CheckScroll();
			if (AdeDragEdit.Instance && AdeDragEdit.Instance.IsDragging)
			{
				previewing = advancing = false;
				var input = AdeInputManager.Instance;
				if (!input.CheckHotkeyActionPressed(input.Hotkeys.PlayWhenHolding)
					&& !input.CheckHotkeyActionPressed(input.Hotkeys.PlayOrPause)) return;
				// Q changes playback independently; the held middle-button edit stays active.
			}
			if (IsEditorMode)
			{
				// Space previews with rollback; Shift+Space advances in place; Q toggles playback.
				var hotkeys = AdeInputManager.Instance.Hotkeys;
				if (AdeInputManager.Instance.CheckHotkeyActionPressed(hotkeys.PlayWhenHolding))
				{
					previewing = false;
					advancing = false;
					TogglePlayback();
				}
				else if (AdeInputManager.Instance.CheckHotkeyActionPressed(hotkeys.AdvanceWhenHolding))
				{
					previewing = false;
					advancing = true;
					if (!GameplayManager.IsPlaying) GameplayManager.Play();
				}
				else if (AdeInputManager.Instance.CheckHotkeyActionPressed(hotkeys.PreviewWhenHolding))
				{
					advancing = false;
					if (!GameplayManager.IsPlaying) GameplayManager.Play();
					playShotTiming = GameplayManager.AudioTiming;
					previewing = true;
				}
				else if (advancing && !AdeInputManager.Instance.CheckHotkeyActionPressing(hotkeys.AdvanceWhenHolding))
				{
					advancing = false;
					GameplayManager.Pause();
				}
				else if (previewing && !AdeInputManager.Instance.CheckHotkeyActionPressing(hotkeys.PreviewWhenHolding))
				{
					previewing = false;
					GameplayManager.Pause();
					GameplayManager.AudioTiming = playShotTiming;
					GameplayManager.ResetJudge();
				}
			}
			else
			{
				previewing = false;
				advancing = false;
			}
			if (!IsEditorMode && AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.PlayWhenHolding))
				TogglePlayback();
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.PlayOrPause)
				|| (!IsEditorMode && AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.PreviewWhenHolding)))
			{
				OnPauseClicked();
			}
		}
		private void OnEnable()
		{
			Application.logMessageReceived += OnLog;
		}
		private void OnDisable()
		{
			Application.logMessageReceived -= OnLog;
		}
		private void OnDestroy()
		{
			modeSwitchMotions.Cancel();
		}
		private void OnLog(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception) return;
			try
			{
				AdeBasicSingleDialogContent.Instance.Show(condition + "\n" + stackTrace, "异常", "确定", Copyable: true);
			}
			catch
			{

			}
		}

		public void SetResolution(string resolution, bool fullscreen)
		{
			if (fullscreen)
			{
				Debug.Log($"[fullscreen]{Screen.currentResolution.width.ToString(CultureInfo.InvariantCulture)}x{Screen.currentResolution.height.ToString(CultureInfo.InvariantCulture)}");
				Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
				return;
			}
			Vector2Int size = AdeScreenResolution.GetWindowedSize(resolution, Screen.mainWindowDisplayInfo.workArea, AdeScreenResolution.ScaleFactor);
			Debug.Log($"[window] requested={resolution}, applied={size.x}x{size.y}");
			Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
		}
		public void SetTargetFramerate(int fps)
		{
			Application.targetFrameRate = fps;
		}

		public void SeekToChartTiming(int timing)
		{
			previewing = advancing = false;
			if (!IsEditorMode) Pause();
			else GameplayManager.Pause();
			GameplayManager.ChartTiming = timing;
			GameplayManager.ResetJudge();
		}

		public void Play()
		{
			if (AdeProjectManager.Instance.CurrentProjectMetadata == null || !ArcGameplayManager.Instance.IsLoaded)
			{
				AdeToast.Instance.Show("请先加载谱面");
				return;
			}

			if (switchingMode) return;
			switchingMode = true;

			GameplayManager.Play();
			AnimateModeSwitch(false);

			PauseButtonImage.sprite = PausePause;
			PauseButton.spriteState = new SpriteState() { pressedSprite = PausePausePressed };
			InfoCanvasGroup.interactable = false;

			TimingSliderHandle.sprite = GlowSliderSprite;

			// Keep a prepared Hold/Arc edit alive while previewing; cursor pickers pause in player mode.
			if (!AdeOperationManager.Instance.HasOngoingOperation)
				AdeClickToCreate.Instance.Mode = ClickToCreateMode.Idle;

			IsEditorMode = false;
		}
		public void Pause()
		{
			if (switchingMode) return;
			switchingMode = true;

			GameplayManager.Pause();
			AnimateModeSwitch(true);

			PauseButtonImage.sprite = PausePlay;
			PauseButton.spriteState = new SpriteState() { pressedSprite = PausePlayPressed };
			InfoCanvasGroup.interactable = true;

			TimingSliderHandle.sprite = DefaultSliderSprite;

			IsEditorMode = true;
		}

		private void AnimateModeSwitch(bool editorMode)
		{
			modeSwitchMotions.Cancel();
			Ease ease = editorMode ? ToEditorModeEase : ToPlayerModeEase;
			LMotion.Create(TopBar.anchoredPosition.y, editorMode ? 0 : TopBar.sizeDelta.y, ModeSwitchDuration)
				.WithEase(ease).WithScheduler(MotionScheduler.Update)
				.BindToAnchoredPositionY(TopBar).AddTo(modeSwitchMotions);
			LMotion.Create(BottomBar.anchoredPosition.y, editorMode ? 0 : -BottomBar.sizeDelta.y, ModeSwitchDuration)
				.WithEase(ease).WithScheduler(MotionScheduler.Update)
				.BindToAnchoredPositionY(BottomBar).AddTo(modeSwitchMotions);
			LMotion.Create(LeftBar.anchoredPosition.x, editorMode ? 0 : -LeftBar.sizeDelta.x, ModeSwitchDuration)
				.WithEase(ease).WithScheduler(MotionScheduler.Update)
				.BindToAnchoredPositionX(LeftBar).AddTo(modeSwitchMotions);
			LMotion.Create(RightBar.anchoredPosition.x, editorMode ? 0 : RightBar.sizeDelta.x, ModeSwitchDuration)
				.WithEase(ease).WithScheduler(MotionScheduler.Update)
				.WithOnComplete(() => { switchingMode = false; })
				.BindToAnchoredPositionX(RightBar).AddTo(modeSwitchMotions);
			LMotion.Create(GameplayViewRect, editorMode ? EditorModeGameplayCameraRect : new Rect(0, 0, 1, 1), ModeSwitchDuration)
				.WithEase(ease).WithScheduler(MotionScheduler.Update)
				.Bind(this, (rect, compose) => { compose.GameplayViewRect = rect; }).AddTo(modeSwitchMotions);
            LMotion.Create(hudPlayerBlend, editorMode ? 0f : 1f, .25f)
                .WithEase(Ease.OutCubic).WithScheduler(MotionScheduler.Update)
                .Bind(this, (value, compose) => { compose.hudPlayerBlend = value; }).AddTo(modeSwitchMotions);
		}

		public void OnPauseClicked()
		{
            if (switchingMode) return;
            if (IsEditorMode)
            {
                GameplayManager.ResetJudge();
                Play();
            }
            else Pause();
		}

        // AA keeps switching play state independent from switching editor/player mode.
        public void TogglePlayback()
        {
            if (!GameplayManager.IsLoaded) return;
            if (GameplayManager.IsPlaying) GameplayManager.Pause(false);
            else GameplayManager.Play();
        }

        private float hudPlayerBlend;

        private void LateUpdate()
        {
            UpdateGameplayProjection();
            // Keep the editor tool layout intact; the player HUD uses AA's 1.5x
            // groups on a 1920x1080 Expand canvas, including ultrawide outputs.
            float playerScale = 1;
            if (EditorCanvas && GameplayCamera)
            {
                var canvas = EditorCanvas.GetComponent<Canvas>();
                var output = GameplayCamera.targetTexture;
                float width = output ? output.width : Screen.width;
                float height = output ? output.height : Screen.height;
                playerScale = 1.5f * Mathf.Min(width / 1920f, height / 1080f) / canvas.scaleFactor;
            }
            float scale = Mathf.Lerp(1, playerScale, hudPlayerBlend);
            if (PauseButtonImage)
            {
                var rect = PauseButtonImage.rectTransform;
                rect.localScale = new Vector3(scale, scale, 1);
                rect.sizeDelta = Vector2.Lerp(new Vector2(254, 100), new Vector2(237, 99), hudPlayerBlend);
                // AA's legacy pause: 170-237, then -50 when the HP sidebar is hidden.
                rect.anchoredPosition = new Vector2(Mathf.Lerp(-65, -117, hudPlayerBlend), -20) * scale;
            }
            if (InfoCanvasGroup)
            {
                var rect = (RectTransform)InfoCanvasGroup.transform;
                rect.localScale = new Vector3(scale, scale, 1);
                // AA places the legacy information artwork 30 units beyond the right edge.
                rect.anchoredPosition = new Vector2(Mathf.Lerp(0, 30, hudPlayerBlend), -20) * scale;
            }
        }
        public Rect GameplayViewRect { get; private set; } = new Rect(.05f, .05f, .9f, .9f);

        private void UpdateGameplayProjection()
        {
            if (!GameplayCamera) return;
            // Preserve AA's framing, while rendering the continuation behind AE's
            // narrow toolbars instead of physically cutting geometry at the 5% border.
            GameplayCamera.rect = new Rect(0, 0, 1, 1);
            GameplayCamera.ResetProjectionMatrix();
            var projection = GameplayCamera.projectionMatrix;
            Rect view = GameplayViewRect;
            projection.m00 *= view.width;
            projection.m11 *= view.height;
            projection.m02 = projection.m02 * view.width - (view.center.x * 2 - 1);
            projection.m12 = projection.m12 * view.height - (view.center.y * 2 - 1);
            GameplayCamera.projectionMatrix = projection;
        }

		public void OnAutoClicked()
		{
			ArcGameplayManager.Instance.Auto = !ArcGameplayManager.Instance.Auto;
			ArcGameplayManager.Instance.ResetJudge();
			UpdateAutoButton();
		}
		// On: a translucent accent tile like a VS Code selection; off: the plain tool tile.
		private void UpdateAutoButton()
		{
			AutoButton.image.color = ArcGameplayManager.Instance.Auto ? AdeUiTheme.ToolbarActiveColor : new Color(0.9f, 0.9f, 0.9f, 1);
			AdeUiTheme.Refresh();
		}
		public void OnShutdownClicked()
		{
			AdeProjectManager.Instance.SaveProject();
			Application.Quit();
		}

		public void SetUndoBufferSize(string x)
		{
			uint size = uint.Parse(x, CultureInfo.InvariantCulture);
			ArcadePreference.UndoBufferSize = size;
			SavePreferences();
			AdeCommandManager.Instance.SetBufferSize(size);
		}

		// Visual-only delay = 画面延迟补偿 + (if enabled) the device's measured output latency.
		public void ApplyDisplayDelay()
		{
			ArcGameplayManager.Instance.DisplayDelay = ArcadePreference.DisplayDelay
				+ (ArcadePreference.AutoOutputLatency ? Arcade.Audio.BassAudio.OutputLatencyMs : 0);
		}

		public void SetGlobalAudioOffset(string x)
		{
			int offset = int.Parse(x, CultureInfo.InvariantCulture);
			ArcadePreference.GlobalAudioOffset = offset;
			SavePreferences();
			ArcGameplayManager.Instance.GlobalAudioOffset = offset;
		}

		private void OnVelocityEdited(float value)
		{
			ArcTimingManager.Instance.SettingVelocity = Mathf.RoundToInt(value * 30);
		}


		public void LoadPreferences()
		{
			try
			{
				ArcadePreference = JsonConvert.DeserializeObject<ArcadePreference>(AdeSettings.GetString("ArcadeComposeManagerPreference", ""));
				if (ArcadePreference == null) ArcadePreference = new ArcadePreference();
			}
			catch (Exception)
			{
				ArcadePreference = new ArcadePreference();
			}
			finally
			{
				if (string.IsNullOrEmpty(ArcadePreference.ScreenResolution))
					ArcadePreference.ScreenResolution = AdeScreenResolution.GetDefaultResolution(
						Screen.mainWindowDisplayInfo.workArea, AdeScreenResolution.ScaleFactor);
				if (ArcadePreference.Velocity < 30)
				{
					ArcadePreference.Velocity = 30;
				}
				if (ArcadePreference.Velocity > 195)
				{
					ArcadePreference.Velocity = 195;
				}
				ArcTimingManager.Instance.SettingVelocity = ArcadePreference.Velocity;
				ArcGameplayManager.Instance.Auto = ArcadePreference.Auto;
				AdeProjectManager.Instance.SaveMode.text = ArcadePreference.ChartSortMode == Gameplay.Chart.ChartSortMode.Timing ? "按时间" : "按类别";
				UpdateAutoButton();
				if (ArcadePreference.AgreedUserAgreementVersion < ArcadeUserAgreementDialogContent.CurrentUserAgreementVersion)
				{
					ArcadeUserAgreementDialogContent.Instance.OpenDialog();
				}
				UndoBufferSizeInput.SetTextWithoutNotify($"{ArcadePreference.UndoBufferSize.ToString(CultureInfo.InvariantCulture)}");
				AdeCommandManager.Instance.bufferSize = ArcadePreference.UndoBufferSize;
				GlobalAudioOffsetInput.SetTextWithoutNotify($"{ArcadePreference.GlobalAudioOffset.ToString(CultureInfo.InvariantCulture)}");
				ArcGameplayManager.Instance.GlobalAudioOffset = ArcadePreference.GlobalAudioOffset;
				ApplyDisplayDelay();
				bool resolutionHit = false;
				for (int i = 0; i < ResolutionDropdown.options.Count; i++)
				{
					Dropdown.OptionData options = ResolutionDropdown.options[i];
					if (options.text == ArcadePreference.ScreenResolution)
					{
						ResolutionDropdown.SetValueWithoutNotify(i);
						resolutionHit = true;
					}
				}
				if (!resolutionHit)
				{
					ArcadePreference.ScreenResolution = ResolutionDropdown.options[ResolutionDropdown.value].text;
				}
				ResolutionDropdown.interactable = !ArcadePreference.Fullscreen;
				FullscreenToggle.SetIsOnWithoutNotify(ArcadePreference.Fullscreen);
				SetResolution(ArcadePreference.ScreenResolution, ArcadePreference.Fullscreen);
				bool targetFramerateHit = false;
				for (int i = 0; i < TargetFramerateDropdown.options.Count; i++)
				{
					Dropdown.OptionData options = TargetFramerateDropdown.options[i];
					if (options.text == ArcadePreference.TargetFrameRate.ToString(CultureInfo.InvariantCulture))
					{
						TargetFramerateDropdown.SetValueWithoutNotify(i);
						targetFramerateHit = true;
					}
				}
				if (!targetFramerateHit)
				{
					ArcadePreference.TargetFrameRate = -1;
				}
				SetTargetFramerate(ArcadePreference.TargetFrameRate);
				PlaybackSyncToggle.SetIsOnWithoutNotify(ArcadePreference.PlaybackSync);
				ArcGameplayManager.Instance.EnablePlaybackSync = ArcadePreference.PlaybackSync;
			}
		}
		public void SavePreferences()
		{
			ArcadePreference.Velocity = ArcTimingManager.Instance.SettingVelocity;
			ArcadePreference.Auto = ArcGameplayManager.Instance.Auto;
			AdeSettings.SetString("ArcadeComposeManagerPreference", JsonConvert.SerializeObject(ArcadePreference));
		}

		public void SetGlowSliderKnob(Sprite sprite)
		{
			if (TimingSliderHandle.sprite == GlowSliderSprite)
			{
				TimingSliderHandle.sprite = sprite;
			}
			GlowSliderSprite = sprite;
		}

		public void SetPauseSprite(Sprite sprite)
		{
			if (PauseButtonImage.sprite == PausePause)
			{
				PauseButtonImage.sprite = sprite;
			}
			PausePause = sprite;
		}
		public void SetPlaySprite(Sprite sprite)
		{
			if (PauseButtonImage.sprite == PausePlay)
			{
				PauseButtonImage.sprite = sprite;
			}
			PausePlay = sprite;
		}
		public void SetPausePressedSprite(Sprite sprite)
		{
			if (PauseButton.spriteState.pressedSprite == PausePausePressed)
			{
				PauseButton.spriteState = new SpriteState { pressedSprite = sprite };
			}
			PausePausePressed = sprite;
		}
		public void SetPlayPressedSprite(Sprite sprite)
		{
			if (PauseButton.spriteState.pressedSprite == PausePlayPressed)
			{
				PauseButton.spriteState = new SpriteState { pressedSprite = sprite };
			}
			PausePlayPressed = sprite;
		}
		private void OnApplicationQuit()
		{
			Debug.Log("saving main setting when exit...");
			SavePreferences();
			Debug.Log("saved main setting when exit");
		}
		public void OpenLogFile()
		{
			Util.Shell.FileBrowser.OpenExplorer(Application.consoleLogPath);
		}

		public void UpdateResolution()
		{
			if (ResolutionDropdown && ResolutionDropdown.captionText)
			{
				float scale = AdeScreenResolution.ScaleFactor;
				int width = Mathf.RoundToInt(Screen.width / scale);
				int height = Mathf.RoundToInt(Screen.height / scale);
				ResolutionDropdown.captionText.text = $"{width}x{height}";
			}
			var cameraManager = ArcCameraManager.Instance;
			if (GameplayCamera && cameraManager)
			{
				modeSwitchMotions.Complete();
				GameplayViewRect = IsEditorMode ? EditorModeGameplayCameraRect : new Rect(0, 0, 1, 1);
				cameraManager.ResetCamera();
			}
		}
	}
}

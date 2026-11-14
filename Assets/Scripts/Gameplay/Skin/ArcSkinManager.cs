using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using Arcade.Util.UnityExtension;
using Arcade.Compose;
using System.Collections.Generic;
using System;

namespace Arcade.Gameplay
{
	public class ArcSkinManager : MonoBehaviour
	{
		public static ArcSkinManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		[Header("Background")]
		public Image Background;
		public Image BackgroundDarken;

		[Header("Combo")]
		public Color[] ComboTextColors = new Color[3];
		[Header("InGame")]
		public Image SongInfo;
		public Image DifficultyPast;
		public Image DifficultyPresent;
		public Image DifficultyFuture;
		public Image DifficultyBeyond;
		public Image DifficultyEternal;

		[Header("Dialog")]
		public List<AdeSingleDialog> SingleDialogs;
		public List<AdeDualDialog> DualDialogs;

		[Header("Shutter")]
		public Image ShutterLeft;
		public Image ShutterRight;
		[Header("SkyInput")]
		public SpriteRenderer SkyInputLabel;
		public SpriteRenderer SkyInputLine;
		[Header("Track")]
		public SpriteRenderer[] TrackComponents;
		public SpriteRenderer[] TrackExtraComponents;
		public SpriteRenderer[] CriticalLines;
		public SpriteRenderer[] CriticalLineExtras;
		public SpriteRenderer[] TrackLaneDividers;
		public SpriteRenderer[] LaneHits;

		[Header("Misc")]
		public Text ComboText;

		public Image TutorialBanner;
		public Image Toast;

		private void Awake()
		{
			Instance = this;
			if (Background)
			{
				// Alpha's background has its own width-matched 1280 canvas and 4:3 image.
				var scaler = Background.GetComponentInParent<CanvasScaler>();
				if (scaler)
				{
					scaler.referenceResolution = new Vector2(1280, 720);
					scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
					scaler.matchWidthOrHeight = 0;
				}
				Background.rectTransform.sizeDelta = new Vector2(1280, 960);
				gameObject.AddComponent<ArcBackgroundEdgeFill>().Source = Background;
			}
		}

        private Vector2 backgroundDesignSize = new Vector2(-1, -1);
        private Rect backgroundView;
        private void LateUpdate()
        {
            if (!Background || !ArcCameraManager.Instance || !ArcCameraManager.Instance.GameplayCamera) return;
            Vector2 size = ArcCameraManager.Instance.DesignResolution;
            Rect view = ArcadeComposeManager.Instance ? ArcadeComposeManager.Instance.GameplayViewRect : new Rect(0, 0, 1, 1);
            if (size == backgroundDesignSize && view == backgroundView) return;
            backgroundView = view;
            backgroundDesignSize = size;
            Background.rectTransform.sizeDelta = new Vector2(1280 * view.width, 960 * view.height);
            Background.rectTransform.anchoredPosition = new Vector2(0, ArcadeAlphaAppearance.BackgroundOffset(size) * view.height);
        }

		public void SetSimpleSkin(AdeSkinHost.SkinDatas skinData)
		{
			SongInfo.sprite = skinData.SongInfo.value;
			AdeProjectManager.Instance.SetDefaultCover(skinData.UnknownCover.value);
			ArcadeComposeManager.Instance.SetGlowSliderKnob(skinData.ProgressGlow.value);

			DifficultyPast.sprite = skinData.DifficultyPast.value;
			DifficultyPresent.sprite = skinData.DifficultyPresent.value;
			DifficultyFuture.sprite = skinData.DifficultyFuture.value;
			DifficultyBeyond.sprite = skinData.DifficultyBeyond.value;
			DifficultyEternal.sprite = skinData.DifficultyEternal.value;

			ArcadeComposeManager.Instance.SetPauseSprite(skinData.Pause.value);
			ArcadeComposeManager.Instance.SetPausePressedSprite(skinData.PausePressed.value);
			ArcadeComposeManager.Instance.SetPlaySprite(skinData.Play.value);
			ArcadeComposeManager.Instance.SetPlayPressedSprite(skinData.PlayPressed.value);

			Toast.sprite = skinData.DialogBackground.value;
			foreach (AdeSingleDialog singleDialog in SingleDialogs)
			{
				singleDialog.SetDialogSkin(
					skinData.DialogTop.value,
					skinData.DialogBackground.value,
					skinData.ButtonSingle.value,
					skinData.ButtonSingleDisabled.value,
					skinData.ButtonSinglePressed.value
				);
			}
			foreach (AdeDualDialog dualDialog in DualDialogs)
			{
				dualDialog.SetDialogSkin(
					skinData.DialogTop.value,
					skinData.DialogBackground.value,
					skinData.ButtonDualLeft.value,
					skinData.ButtonDualLeftDisabled.value,
					skinData.ButtonDualLeftPressed.value,
					skinData.ButtonDualRight.value,
					skinData.ButtonDualRightDisabled.value,
					skinData.ButtonDualRightPressed.value
				);
			}

			ShutterLeft.sprite = skinData.ShutterLeft.value;
			ShutterRight.sprite = skinData.ShutterRight.value;
			AdeShutterManager.Instance.OpenAudio = skinData.ShutterOpen.value;
			AdeShutterManager.Instance.CloseAudio = skinData.ShutterClose.value;
			ArcEffectManager.Instance.TapAudio = skinData.TapSound.value;
			ArcEffectManager.Instance.ArcAudio = skinData.ArcSound.value;

			TutorialBanner.sprite = skinData.TutorialBanner.value;
			BackgroundDarken.sprite = skinData.BackgroundDarken.value;

			SkyInputLabel.sprite = skinData.SkyInputLabel.value;
			SkyInputLine.sprite = skinData.SkyInputLine.value;

			foreach (SpriteRenderer trackLaneDivider in TrackLaneDividers)
			{
				trackLaneDivider.sprite = skinData.TrackLaneDivider.value;
			}
			foreach (SpriteRenderer laneHit in LaneHits)
			{
				laneHit.sprite = skinData.LaneHit.value;
			}

			ArcEffectManager.Instance.SetSfxTapEffectTexture(skinData.ParticleSfxTap.value);
			ArcEffectManager.Instance.SetParticleArcTexture(skinData.ParticleArc.value);

			ArcArcManager.Instance.SetArcCapSkin(skinData.ArcCap.value);

			ArcArcManager.Instance.SetArcTapShadowSkin(skinData.ArcTapShadow.value);

			ArcArcManager.Instance.SetHeightIndicatorSkin(skinData.HeightIndicator.value);

			ArcArcManager.Instance.SetArcBodySkin(skinData.ArcBody.value, skinData.ArcBodyHighlight.value);

			AdeCursorManager.Instance.SfxArcTapCursorRenderer.GetComponent<MeshFilter>().mesh = ArcNoteMeshes.Sfx;
			AdeUiTheme.OnSkinApplied();
		}
		public void SetNoteSideSkin(AdeSkinHost.NoteSideData noteSideData)
		{
			ArcTapNoteManager.Instance.SetTapNoteSkin(noteSideData.TapNote.value);
			ArcHoldNoteManager.Instance.SetHoldNoteSkin(noteSideData.HoldNote.value, noteSideData.HoldNoteHighlight.value);
			ArcArcManager.Instance.SetArcTapSkin(noteSideData.ArcTap.value);
			ArcArcManager.Instance.SetSfxArcTapSkin(noteSideData.SfxArcTapNote.value, noteSideData.SfxArcTapCore.value);
			ArcTapNoteManager.Instance.SetConnectionLineColor(noteSideData.ConnectionLineColor);
			ArcArcManager.Instance.SetArcColors(
				noteSideData.ArcRedLow, noteSideData.ArcBlueLow, noteSideData.ArcGreenLow, noteSideData.ArcUnknownLow,
				noteSideData.ArcRedHigh, noteSideData.ArcBlueHigh, noteSideData.ArcGreenHigh, noteSideData.ArcUnknownHigh,
				noteSideData.ArcVoid, noteSideData.ArcDesignant, noteSideData.ArcTapDesignant);
		}
		private bool darkShadow;
		public bool IsDarkSide => darkShadow;
        public Color GetShadowTint(bool trace = false)
        {
            var color = darkShadow ? Color.white : Color.black;
            color.a = (darkShadow ? (trace ? 10f : 20f) : (trace ? 30f : 50f)) / 255f;
            return color;
        }

        public void SetThemeSideSkin(AdeSkinHost.ThemeSideData themeSideData, Side side = Side.Light)
		{
            darkShadow = side == Side.Conflict || (int)side == 4;
			foreach (SpriteRenderer trackComponent in TrackComponents)
			{
				trackComponent.sprite = themeSideData.Track.value;
			}
			foreach (SpriteRenderer trackExtraComponent in TrackExtraComponents)
			{
				trackExtraComponent.sprite = themeSideData.TrackExtra.value;
			}
			foreach (SpriteRenderer criticalLine in CriticalLines)
			{
				criticalLine.sprite = themeSideData.CriticalLine.value;
			}
			foreach (SpriteRenderer criticalLineExtra in CriticalLineExtras)
			{
				criticalLineExtra.sprite = themeSideData.CriticalLineExtra.value;
			}
			Color ComboTextColor = themeSideData.ComboTextColor;
			ComboText.color = ComboTextColor;
			ArcEffectManager.Instance.SetTapEffectTexture(themeSideData.ParticleNote.value);
			ArcEffectManager.Instance.SetTapEffectConflictBlend(side == Side.Conflict);
			ArcEffectManager.Instance.SetParticleArcColor(themeSideData.ParticleArcStartColor, themeSideData.ParticleArcEndColor);
            ArcLongNoteEffect.AppearanceScale = 1;
            Appearance.CaptureBase(this, themeSideData, side);
		}

        private ArcAppearanceRenderer appearance;
        private ArcAppearanceRenderer Appearance => appearance ? appearance : (appearance = gameObject.AddComponent<ArcAppearanceRenderer>());
        public void ApplyAppearance(AppearanceSelection selection, Side side) => Appearance.Apply(selection, side);
        public void ApplyPresetExtras(int note, string hitEffect, string particle, Side side) => Appearance.Extras(note, hitEffect, particle, side);

		public void SetBackground(Sprite background)
		{
			Background.sprite = background;
		}
	}
}


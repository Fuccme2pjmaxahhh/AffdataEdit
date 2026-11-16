using System;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public interface IAdeRightSideCard
    {
        void CloseImmediately();
    }

    internal static class AdeRightSideCards
    {
        private static IAdeRightSideCard active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => active = null;


        public static void Activate(IAdeRightSideCard card)
        {
            if (active != card) active?.CloseImmediately();
            active = card;
        }

        public static void Release(IAdeRightSideCard card)
        {
            if (active == card) active = null;
        }
    }

    public sealed class AdeRightSideCard : MonoBehaviour, IAdeRightSideCard
    {
        [SerializeField] private RectTransform view;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image background, closeIcon;
        public event Action Opened, Closed;
        public bool IsOpen { get; private set; }
        // Right toolbar button that opens this card; it stays highlighted while the card is open.
        public Button Entry { get; set; }
        private MotionHandle motion;
        private int themeRevision = -1;

        private void Start()
        {
            view.gameObject.SetActive(false);
            if (!Entry) Entry = FindSceneEntry();
        }

        private Button FindSceneEntry()
        {
            var bar = ArcadeComposeManager.Instance ? ArcadeComposeManager.Instance.RightBarView : null;
            if (!bar) return null;
            foreach (var button in bar.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentTarget(i) == this) return button;
            return null;
        }

        private void SetEntryPressed(bool pressed)
        {
            if (Entry) AdeUiTheme.SetToolbarToggleState(Entry.image, pressed);
        }

        private void LateUpdate()
        {
            if (themeRevision != AdeUiTheme.Revision)
            {
                themeRevision = AdeUiTheme.Revision;
                background.sprite = AdeUiTheme.PanelSurface;
                background.color = Color.white;
                closeIcon.color = AdeUiTheme.IconColor;
            }
            if (IsOpen && !ArcadeComposeManager.Instance.IsEditorMode) CloseImmediately();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            AdeRightSideCards.Activate(this);
            if (IsOpen) return;
            IsOpen = true;
            motion.TryCancel();
            float from = view.gameObject.activeSelf ? group.alpha : 0;
            view.gameObject.SetActive(true);
            AdeDialogManager.PoseSide(group, view, from);
            group.blocksRaycasts = true;
            motion = AdeDialogManager.AnimateSideView(group, view, from, 1, () => { });
            SetEntryPressed(true);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            group.blocksRaycasts = false;
            motion.TryCancel();
            motion = AdeDialogManager.AnimateSideView(group, view, group.alpha, 0, () =>
            {
                view.gameObject.SetActive(false);
                AdeRightSideCards.Release(this);
            });
            SetEntryPressed(false);
            Closed?.Invoke();
        }

        public void CloseImmediately()
        {
            motion.TryCancel();
            bool wasOpen = IsOpen;
            IsOpen = false;
            group.blocksRaycasts = false;
            view.gameObject.SetActive(false);
            AdeRightSideCards.Release(this);
            SetEntryPressed(false);
            if (wasOpen) Closed?.Invoke();
        }

        private void OnDestroy()
        {
            motion.TryCancel();
            AdeRightSideCards.Release(this);
        }
    }
}

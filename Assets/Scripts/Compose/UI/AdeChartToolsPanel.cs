using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.UI;
using LitMotion;

namespace Arcade.Compose
{
    public sealed partial class AdeChartToolsPanel : MonoBehaviour, IAdeRightSideCard
    {
        public enum Page { Groups, SceneControl, Camera }
        public static AdeChartToolsPanel Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Instance = null;
        private static readonly string[] Titles = { "时间组", "SC", "Camera" };
        [SerializeField] private RectTransform[] pages = new RectTransform[3];
        [SerializeField] private Button[] tabs = new Button[3];
        [SerializeField] private Button[] entries = new Button[3];
        private readonly List<ArcTimingGroup> groups = new List<ArcTimingGroup>();
        private readonly List<Button> groupRows = new List<Button>();
        private readonly List<TMP_Text> groupDetails = new List<TMP_Text>();
        private readonly List<Button> groupCopies = new List<Button>(), groupDeletes = new List<Button>();
        // The group list is virtualized: a few rows are rebound as it scrolls, between two spacers.
        private const int GroupRowCount = 6;
        private int groupFirst;
        private RectTransform groupTopSpacer, groupBottomSpacer;
        // Timing group dropdowns share one option list, rebuilt only when the groups change.
        private readonly List<ArcTimingGroup> optionGroups = new List<ArcTimingGroup>();
        private List<TMP_Dropdown.OptionData> groupOptions = new List<TMP_Dropdown.OptionData>();
        [SerializeField] private Button[] groupTabs = new Button[2];
        [SerializeField] private RectTransform[] groupSections = new RectTransform[2];
        private int activeGroupTab;
        private readonly List<TimingRow> timingRows = new List<TimingRow>();
        [SerializeField] private RectTransform panel, pageHost, emptyState, groupList, timingList, timingRowTemplate;
        [SerializeField] private ScrollRect groupScroll, timingScroll;
        [SerializeField] private TMP_Dropdown bottomGroup;
        [SerializeField] private TMP_Text context, status, groupCount, defaultGroupHint;
        [SerializeField] private Toggle noInput, fadingHolds, noClip;
        [SerializeField] private TMP_InputField angleX, angleY, attributes;
        [SerializeField] private CanvasGroup groupProperties;
        [SerializeField] private RectTransform deleteConfirmation;
        [SerializeField] private TMP_Text deletePrompt;
        private ArcTimingGroup pendingDelete;
        [SerializeField] private ControlPage[] controlPages = new ControlPage[2];
        private Page activePage;
        private ArcTimingGroup lastGroup;
        private ArcChart lastChart;
        private bool dirty = true, ready;
        private int themeRevision = -1;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private CanvasGroup panelGroup;
        private MotionHandle panelMotion;
        private bool expanded;
        private bool timingLayoutDirty = true;
        private bool revealCurrentGroup;
        private float timingViewportHeight;

        [Serializable]
        private sealed class ControlPage
        {
            public bool Camera;
            public RectTransform List;
            public ScrollRect Scroll;
            public readonly List<Button> Rows = new List<Button>();
            public readonly List<TMP_Text> Descriptions = new List<TMP_Text>();
            public readonly List<TMP_Text> Groups = new List<TMP_Text>();
            [NonSerialized] public readonly List<ArcEvent> Events = new List<ArcEvent>();
            [NonSerialized] public int EventsRevision = -1, EventsScope = -1, First = -1;
            [NonSerialized] public ArcChart EventsChart;
            [NonSerialized] public ArcTimingGroup EventsGroup;
            [NonSerialized] public RectTransform TopSpacer, BottomSpacer;
            [NonSerialized] public ArcEvent Selected;
            public TMP_Dropdown Scope, Group, Type;
            public TMP_InputField GroupInput;
            public TMP_InputField Time, Duration, CustomType, Parameters, Statement;
            public TMP_InputField[] Vectors;
            public TMP_Text Hint, Summary;
            public CanvasGroup Form;
            public Button Delete, Duplicate;
        }

        private void Start()
        {
            Instance = this;
            var timing = AdeTimingEditor.Instance;
            BindView();
            timing.View.SetActive(false);
            var timingEntry = ArcadeComposeManager.Instance.RightBarView.Find("Timing");
            if (timingEntry)
            {
                timingEntry.gameObject.SetActive(true);
                timingEntry.SetAsFirstSibling();
                AdeUiKit.SetOnClick(timingEntry.GetComponent<Button>(), () => Toggle(Page.Groups));
            }
            ArcGameplayManager.Instance.OnChartLoad.AddListener(OnChartLoad);
            AdeCommandManager.Instance.onCommandExecuted += OnCommand;
            ready = true;
            Refresh();
            panel.gameObject.SetActive(false);
            ApplyTheme();
        }

        private void OnDestroy()
        {
            AdeRightSideCards.Release(this);
            panelMotion.TryCancel();
            if (ArcGameplayManager.Instance) ArcGameplayManager.Instance.OnChartLoad.RemoveListener(OnChartLoad);
            if (AdeCommandManager.Instance) AdeCommandManager.Instance.onCommandExecuted -= OnCommand;
            if (Instance == this) Instance = null;
        }

        private void OnChartLoad()
        {
            foreach (var page in controlPages) page.Selected = null;
            dirty = true;
        }
        private void OnCommand(ICommand command, bool undo) => dirty = true;

        private void LateUpdate()
        {
            if (!ready) return;
            if (lastChart != ArcGameplayManager.Instance.Chart || lastGroup != AdeTimingEditor.Instance.currentTimingGroup) dirty = true;
            if (dirty) Refresh();
            if (revealCurrentGroup && expanded && activePage == Page.Groups)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(groupList);
                int index = Mathf.Max(0, groups.IndexOf(lastGroup));
                float viewport = groupScroll.viewport.rect.height;
                float y = Mathf.Clamp(index * (ControlHeight + 4) - viewport / 2 + ControlHeight / 2,
                    0, Mathf.Max(0, groupList.rect.height - viewport));
                groupList.anchoredPosition = new Vector2(groupList.anchoredPosition.x, y);
                groupFirst = -1; ScrollGroups(); revealCurrentGroup = false;
            }
            ResizeTimingList();
            if (themeRevision != AdeUiTheme.Revision) ApplyTheme();
            if (!ArcadeComposeManager.Instance.IsEditorMode && expanded) Collapse();
        }

        internal void RevealCurrentGroup() { revealCurrentGroup = true; dirty = true; }

        public void Toggle(Page page)
        {
            if (expanded && page == activePage) Collapse();
            else Show(page);
        }

        public void Show(Page page)
        {
            if (!ready) return;
            AdeRightSideCards.Activate(this);
            bool opening = !expanded;
            activePage = page;
            if (!panel.gameObject.activeSelf) AdeDialogManager.Pose(panelGroup, panel, 0);
            expanded = true;
            panelGroup.blocksRaycasts = true;
            panel.gameObject.SetActive(true);
            for (int i = 0; i < pages.Length; i++) pages[i].parent.parent.gameObject.SetActive(i == (int)page);
            ClearMessage();
            if (dirty) Refresh();
            else UpdateHighlights();
            if (opening)
            {
                panelMotion.TryCancel();
                panelMotion = AdeDialogManager.AnimateView(panelGroup, panel, panelGroup.alpha, 1, () => { });
            }
        }

        public void Collapse()
        {
            if (!expanded) return;
            expanded = false;
            panelGroup.blocksRaycasts = false;
            panelMotion.TryCancel();
            panelMotion = AdeDialogManager.AnimateView(panelGroup, panel, panelGroup.alpha, 0,
                () => { panel.gameObject.SetActive(false); AdeRightSideCards.Release(this); });
            UpdateHighlights();
        }

        public void CloseImmediately()
        {
            panelMotion.TryCancel();
            expanded = false;
            panelGroup.blocksRaycasts = false;
            panel.gameObject.SetActive(false);
            AdeRightSideCards.Release(this);
            UpdateHighlights();
        }

        public void FocusTiming(ArcTiming timing)
        {
            Show(Page.Groups);
            ShowGroupTab(0);
            Refresh();
            int index = ArcTimingManager.Instance.GetTiming(lastGroup).IndexOf(timing);
            if (index < 0) return;
            Canvas.ForceUpdateCanvases();
            timingScroll.verticalNormalizedPosition = timingRows.Count <= 1 ? 1 : 1f - index / (float)(timingRows.Count - 1);
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(timingRows[index].Time.gameObject);
            timingRows[index].Time.ActivateInputField();
        }

        private void Refresh()
        {
            dirty = false;
            lastChart = ArcGameplayManager.Instance.Chart;
            lastGroup = AdeTimingEditor.Instance.currentTimingGroup;
            groups.Clear(); groups.Add(null); groups.AddRange(ArcTimingManager.Instance.timingGroups);
            if (!groups.SequenceEqual(optionGroups))
            {
                groupOptions = groups.Select(g => new TMP_Dropdown.OptionData(GroupName(g))).ToList();
                optionGroups.Clear(); optionGroups.AddRange(groups);
            }
            SetGroupOptions(bottomGroup, groups.IndexOf(lastGroup));
            bottomGroup.interactable = lastChart != null;
            bool loaded = lastChart != null;
            pageHost.gameObject.SetActive(loaded);
            emptyState.gameObject.SetActive(!loaded);
            context.gameObject.SetActive(loaded);
            context.text = "当前时间组：" + GroupName(lastGroup);
            RefreshGroups();
            foreach (var page in controlPages)
            {
                SetGroupOptions(page.Group, page.Selected is IHasTimingGroup e ? groups.IndexOf(e.TimingGroup) : groups.IndexOf(lastGroup));
                RefreshControls(page);
            }
            UpdateHighlights();
        }

        private static string GroupName(ArcTimingGroup group) => group == null ? "默认组" : "组 " + group.Id;
        private void SelectGroup(int index)
        {
            if (index < 0 || index >= groups.Count || lastChart == null) return;
            AdeTimingEditor.Instance.SetCurrentTimingGroup(groups[index]);
            ClearMessage();
            dirty = true;
        }

        private void UpdateHighlights()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                Highlight(tabs[i], i == (int)activePage);
                Highlight(entries[i], expanded && i == (int)activePage);
            }
            for (int i = 0; i < groupTabs.Length; i++) Highlight(groupTabs[i], i == activeGroupTab);
        }

        private void ShowGroupTab(int index)
        {
            activeGroupTab = index;
            timingLayoutDirty = true;
            for (int i = 0; i < groupSections.Length; i++) groupSections[i].gameObject.SetActive(i == index);
            UpdateHighlights();
            ClearMessage();
        }

        private void Message(string text, bool error = false)
        {
            status.text = text;
            status.gameObject.SetActive(text.Length > 0);
            status.color = error ? new Color32(232, 113, 113, 255) : Foreground;
        }
        private void ClearMessage() => Message("");
    }
}

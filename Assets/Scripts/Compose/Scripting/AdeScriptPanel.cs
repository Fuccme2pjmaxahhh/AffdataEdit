using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Arcade.Gameplay;
using Cysharp.Threading.Tasks;
using TMPro;
using LitMotion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Arcade.Compose.Scripting
{
    // The "外置脚本" entry on the right bar and its side card. The card and rows are cloned from
    // the scene's problems card and chart tools panel, so they share their look and theming.
    public sealed class AdeScriptPanel : MonoBehaviour
    {
        private const string EditorSceneName = "ArcEditor";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == EditorSceneName) new GameObject(nameof(AdeScriptPanel)).AddComponent<AdeScriptPanel>();
        }

        private AdeRightSideCard card;
        private RectTransform content;
        private Button rowTemplate;
        private Transform textRowTemplate, timingRowTemplate, listRowTemplate, boolRowTemplate, actionRowTemplate;
        private TMP_Text sectionTemplate, status, toolCount;
        private bool showQuick = true;
        private float toolTextSize, timeButtonSpace;
        private AdeAnimatedTabs categorySelector;
        private readonly Dictionary<RectTransform, MotionHandle> formMotions = new Dictionary<RectTransform, MotionHandle>();
        private readonly HashSet<RectTransform> closingForms = new HashSet<RectTransform>();
        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<GameObject> built = new List<GameObject>();
        private readonly Dictionary<AdeScriptTool, RectTransform> forms = new Dictionary<AdeScriptTool, RectTransform>();
        private readonly List<Button> rows = new List<Button>();
        private int themeRevision = -1;
        private CancellationTokenSource running;

        private void Start() => InitializeAsync().Forget();

        private async UniTaskVoid InitializeAsync()
        {
            // Let the chart tools panel bind its rows first; their runtime icons are cloned too.
            await UniTask.NextFrame(destroyCancellationToken);
            if (!Build())
            {
                Debug.LogWarning("External script panel: the scene layout was not found.");
                return;
            }
            AdeScriptLibrary.Reported += ShowStatus;
            Reload();
        }

        private void LateUpdate()
        {
            categorySelector?.Refresh();
            if (themeRevision == AdeUiTheme.Revision) return;
            themeRevision = AdeUiTheme.Revision;
            foreach (var row in rows) if (row) StyleRow(row);
            StyleCategories();
            if (toolCount) toolCount.color = AdeUiTheme.Current.Muted;
        }

        // Same look as the problems card's issue rows.
        private static void StyleRow(Button row)
        {
            row.image.sprite = AdeUiTheme.RoundedSurface;
            row.image.type = Image.Type.Sliced;
            row.image.color = Color.white;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = AdeUiTheme.Current.Input;
            colors.highlightedColor = colors.selectedColor = AdeUiTheme.Current.Selection;
            colors.pressedColor = Color.Lerp(AdeUiTheme.Current.Selection, AdeUiTheme.Current.Text, .12f);
            colors.fadeDuration = 0;
            row.colors = colors;
            row.targetGraphic.CrossFadeColor(colors.normalColor, 0, true, true);
            row.transform.Find("Reason").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Text;
            row.transform.Find("Location").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Muted;
        }

        private void OnDestroy()
        {
            categorySelector?.Dispose();
            StopFormMotions();
            AdeScriptLibrary.Reported -= ShowStatus;
            running?.Cancel();
        }

        private bool Build()
        {
            var compose = ArcadeComposeManager.Instance;
            var right = compose ? compose.RightBarView.parent : null;
            var problems = right ? right.Find("ProblemsCard") : null;
            var check = compose ? compose.RightBarView.Find("Check") : null;
            var form = right ? right.Find("ChartTools/ChartToolsPanel/Pages/SC/Viewport/Content/参数") : null;
            var groupForm = right ? right.Find("ChartTools/ChartToolsPanel/Pages/时间组/Viewport/Content/属性分区/组属性") : null;
            if (!problems || !check || !form || !groupForm) return false;

            textRowTemplate = form.Find("自定义类型");
            timingRowTemplate = form.Find("事件时间");
            listRowTemplate = form.Find("SC类型");
            actionRowTemplate = form.Find("应用参数");
            boolRowTemplate = groupForm.Find("noinput");
            sectionTemplate = form.Find("SC 参数")?.GetComponent<TMP_Text>();
            if (!textRowTemplate || !timingRowTemplate || !listRowTemplate || !actionRowTemplate || !boolRowTemplate || !sectionTemplate) return false;

            toolTextSize = actionRowTemplate.GetComponentInChildren<Button>(true).GetComponentInChildren<TMP_Text>(true).fontSize;
            var timeButton = (RectTransform)timingRowTemplate.Find("当前");
            timeButtonSpace = Mathf.Max(LayoutUtility.GetMinWidth(timeButton), LayoutUtility.GetPreferredWidth(timeButton)) + 12;
            var cardObject = Instantiate(problems.gameObject, right, false);
            cardObject.name = "ScriptsCard";
            card = cardObject.GetComponent<AdeRightSideCard>();
            var view = cardObject.transform.Find("View");
            var viewRect = (RectTransform)view;
            viewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, viewRect.rect.width * 1.25f);
            var scroll = view.Find("Problems").GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            content = (RectTransform)view.Find("Problems/Viewport/Content");
            var contentFitter = content.GetComponent<ContentSizeFitter>();
            if (contentFitter) contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowTemplate = content.Find("IssueTemplate")?.GetComponent<Button>();
            status = view.Find("Label")?.GetComponent<TMP_Text>();
            var title = view.Find("Header/Label")?.GetComponent<TMP_Text>();
            if (!card || !rowTemplate || !status || !title) return false;
            // The copy keeps the source card's visibility; start closed like the other cards.
            view.gameObject.SetActive(false);
            title.text = "外置脚本";
            var countObject = new GameObject("ToolCount", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            countObject.layer = title.gameObject.layer;
            countObject.transform.SetParent(title.transform.parent, false);
            countObject.transform.SetSiblingIndex(title.transform.GetSiblingIndex() + 1);
            toolCount = countObject.GetComponent<TMP_Text>();
            AdeEditorText.Configure(toolCount, 16);
            toolCount.alignment = TextAlignmentOptions.MidlineRight;
            toolCount.raycastTarget = false;
            toolCount.textWrappingMode = TextWrappingModes.NoWrap;
            var countSize = countObject.GetComponent<LayoutElement>();
            countSize.minWidth = 0; countSize.preferredWidth = 160; countSize.flexibleWidth = 0;

            status.textWrappingMode = TextWrappingModes.Normal;
            status.alignment = TextAlignmentOptions.TopLeft;
            status.GetComponent<LayoutElement>().preferredHeight = -1;
            rowTemplate.gameObject.SetActive(false);
            foreach (Transform child in content)
                if (child != rowTemplate.transform) Destroy(child.gameObject);

            var entry = Instantiate(check.gameObject, check.parent, false);
            entry.name = "Scripts";
            entry.transform.SetSiblingIndex(check.GetSiblingIndex() + 1);
            var badge = entry.transform.Find("ProblemCount");
            if (badge) Destroy(badge.gameObject);
            var icon = entry.transform.Find("Image")?.GetComponent<Image>();
            if (icon) icon.sprite = Resources.Load<Sprite>("AffdataEdit/Icons/Script");
            var button = entry.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(card.Toggle);
            var controls = new GameObject("ScriptControls", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            controls.layer = view.gameObject.layer;
            controls.transform.SetParent(view, false);
            controls.transform.SetSiblingIndex(view.Find("Problems").GetSiblingIndex());
            var controlsSize = controls.GetComponent<LayoutElement>();
            controlsSize.minHeight = controlsSize.preferredHeight = 100;
            controlsSize.flexibleHeight = 0; controlsSize.flexibleWidth = 1;
            var controlsLayout = controls.GetComponent<VerticalLayoutGroup>();
            controlsLayout.spacing = 8;
            controlsLayout.childControlWidth = controlsLayout.childControlHeight = true;
            controlsLayout.childForceExpandWidth = true; controlsLayout.childForceExpandHeight = false;
            AddButtonPair("Actions", "重新加载", Reload, "打开文件夹",
                () => Application.OpenURL(new Uri(AdeScriptLibrary.Folder).AbsoluteUri), controls.transform);
            AddButtonPair("ToolCategory", "快速工具", () => SwitchCategory(true), "外置工具", () => SwitchCategory(false), controls.transform);
            return true;
        }

        // ---- List.

        private void Reload()
        {
            if (AdeScriptLibrary.Running) return;
            AdeScriptLibrary.Load();
            Rebuild();
            toolCount.text = AdeScriptLibrary.Tools.Count > 0 ? $"已加载 {AdeScriptLibrary.Tools.Count} 个工具" : "";
            toolCount.gameObject.SetActive(AdeScriptLibrary.Tools.Count > 0);
            if (AdeScriptLibrary.LoadErrors.Count > 0)
                ShowStatus("部分脚本加载失败：\n" + string.Join("\n", AdeScriptLibrary.LoadErrors), true);
            else ShowStatus(AdeScriptLibrary.Tools.Count == 0
                ? "没有找到脚本。把 Arcade Alpha 的 .lua 放进 Scripts/AA，Arcade-One 的放进 Scripts/A1，然后点“重新加载”。"
                : "", false);
        }

        private void Rebuild()
        {
            StopFormMotions();
            foreach (var item in built) if (item) { item.SetActive(false); Destroy(item); }
            built.Clear();
            forms.Clear();
            rows.Clear();

            StyleCategories();
            foreach (var tool in AdeScriptLibrary.Tools.Where(t => t.IsQuick == showQuick)) AddRow(tool);
            AdeUiTheme.Refresh();
        }

        private void SwitchCategory(bool quick)
        {
            if (showQuick == quick) return;
            showQuick = quick;
            Rebuild();

        }

        private void AddButtonPair(string name, string left, UnityEngine.Events.UnityAction leftAction,
            string right, UnityEngine.Events.UnityAction rightAction, Transform parent)
        {
            if (name == "ToolCategory") { BuildCategorySelector(parent); return; }
            var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            root.layer = content.gameObject.layer;
            root.transform.SetParent(parent, false);
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
            var rootSize = root.GetComponent<LayoutElement>();
            rootSize.minHeight = rootSize.preferredHeight = 44; rootSize.flexibleHeight = 0;
            var template = actionRowTemplate.GetComponentInChildren<Button>(true);
            for (int i = 0; i < 2; i++)
            {
                var button = Instantiate(template, root.transform, false);
                button.gameObject.SetActive(true);
                SetButton(button, i == 0 ? left : right, i == 0 ? leftAction : rightAction);
                var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = 0; size.flexibleWidth = 1;
            }
            StyleCategories();
        }

        private void BuildCategorySelector(Transform parent)
        {
            var root = new GameObject("ToolCategory", typeof(RectTransform), typeof(LayoutElement));
            root.layer = content.gameObject.layer; root.transform.SetParent(parent, false);
            var size = root.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 48; size.flexibleHeight = 0;
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "QuickTools" : "ExternalTools", typeof(RectTransform), typeof(Image), typeof(Button));
                go.layer = root.layer; go.transform.SetParent(root.transform, false);
                var rect = go.GetComponent<RectTransform>();
                SetCategoryBounds(rect, i);
                var button = go.GetComponent<Button>();
                button.targetGraphic = go.GetComponent<Image>();
                button.image.color = Color.clear; button.transition = Selectable.Transition.None;
                bool quick = i == 0;
                button.onClick.AddListener(() => SwitchCategory(quick));
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObject.layer = root.layer; labelObject.transform.SetParent(go.transform, false);
                var label = labelObject.GetComponent<TMP_Text>();
                AdeEditorText.Configure(label, toolTextSize);
                label.text = quick ? "快速工具" : "外置工具";
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                categoryButtons.Add(button);
            }
            categorySelector = new AdeAnimatedTabs(categoryButtons.ToArray());
            StyleCategories();
        }

        // Match the two action buttons above: half the width, separated by an 8-unit gap.
        internal static void SetCategoryBounds(RectTransform rect, float position)
        {
            rect.anchorMin = new Vector2(position * .5f, 0);
            rect.anchorMax = new Vector2((position + 1) * .5f, 1);
            rect.offsetMin = new Vector2(position * 4, 0);
            rect.offsetMax = new Vector2((position - 1) * 4, 0);
        }

        private void StyleCategories() => categorySelector?.Select(showQuick ? 0 : 1);

        private void AddRow(AdeScriptTool tool)
        {
            var row = Instantiate(rowTemplate, content, false);
            row.name = "Tool";
            row.gameObject.SetActive(false);
            built.Add(row.gameObject);
            rows.Add(row);
            StyleRow(row);
            row.transform.Find("Reason").GetComponent<TMP_Text>().text = tool.Name;
            row.transform.Find("Location").gameObject.SetActive(false);
            var label = row.transform.Find("Reason").GetComponent<TMP_Text>();
            AdeEditorText.Configure(label, toolTextSize);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var labelSize = label.GetComponent<LayoutElement>();
            labelSize.ignoreLayout = true;
            row.GetComponent<VerticalLayoutGroup>().enabled = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(12, 6);
            label.rectTransform.offsetMax = new Vector2(-12, -6);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var rowSize = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            rowSize.minHeight = rowSize.preferredHeight = 44;
            rowSize.flexibleHeight = 0;
            row.gameObject.SetActive(true);
            StyleRow(row);
            row.onClick.AddListener(() =>
            {
                if (tool.Kind == AdeScriptToolKind.Parameters) ToggleForm(tool, row.transform);
                else Execute(tool);
            });
        }

        // ---- Parameter form, shown under its tool row.

        private void ToggleForm(AdeScriptTool tool, Transform row)
        {
            if (forms.TryGetValue(tool, out var open))
            {
                bool closing = !closingForms.Remove(open);
                if (closing) closingForms.Add(open);
                AnimateForm(tool, open, closing);
                return;
            }
            var form = new GameObject("Form", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(CanvasGroup), typeof(RectMask2D)).GetComponent<RectTransform>();
            form.gameObject.layer = content.gameObject.layer;
            form.SetParent(content, false);
            form.SetSiblingIndex(row.GetSiblingIndex() + 1);
            var layout = form.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 4, 12);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            built.Add(form.gameObject);
            forms[tool] = form;

            foreach (var parameter in tool.Parameters) AddField(form, parameter);
            var run = Clone(actionRowTemplate, "Run", form).GetComponentInChildren<Button>(true);
            SetButton(run, "运行", () => Execute(tool));
            AdeUiTheme.Refresh();
            var formSize = form.GetComponent<LayoutElement>();
            formSize.layoutPriority = 2; formSize.minHeight = formSize.preferredHeight = 0; formSize.flexibleHeight = 0;
            form.GetComponent<CanvasGroup>().alpha = 0;
            AnimateForm(tool, form, false);
        }

        private void StopFormMotions()
        {
            foreach (var motion in formMotions.Values) motion.TryCancel();
            formMotions.Clear(); closingForms.Clear();
        }

        private void AnimateForm(AdeScriptTool tool, RectTransform form, bool closing)
        {
            if (formMotions.TryGetValue(form, out var motion)) motion.TryCancel();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var size = form.GetComponent<LayoutElement>();
            var group = form.GetComponent<CanvasGroup>();
            float from = size.preferredHeight;
            float target = closing ? 0 : form.GetComponent<VerticalLayoutGroup>().preferredHeight;
            float alpha = group.alpha;
            group.interactable = group.blocksRaycasts = !closing;
            formMotions[form] = LMotion.Create(0f, 1f, .2f).WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).WithOnComplete(() =>
                {
                    formMotions.Remove(form);
                    if (!closing) return;
                    closingForms.Remove(form); forms.Remove(tool); built.Remove(form.gameObject);
                    form.gameObject.SetActive(false); Destroy(form.gameObject);
                }).Bind(t =>
                {
                    size.preferredHeight = Mathf.Lerp(from, target, t);
                    group.alpha = Mathf.Lerp(alpha, closing ? 0 : 1, t);
                });
        }

        private void AddField(RectTransform form, AdeScriptParameter parameter)
        {
            string value = parameter.Value ?? parameter.Default;
            switch (parameter.Type)
            {
                case AdeScriptParameterType.Bool:
                {
                    var row = Clone(boolRowTemplate, "Bool", form);
                    var toggle = row.GetComponentInChildren<Toggle>(true);
                    toggle.onValueChanged = new Toggle.ToggleEvent();
                    toggle.SetIsOnWithoutNotify(value == "true");
                    toggle.onValueChanged.AddListener(on => parameter.Value = on ? "true" : "false");
                    foreach (var label in row.GetComponentsInChildren<TMP_Text>(true)) SetLabel(row, label, parameter.Name);
                    break;
                }
                case AdeScriptParameterType.List:
                {
                    var row = Clone(listRowTemplate, "List", form);
                    SetLabel(row, row.GetComponentInChildren<TMP_Text>(true), parameter.Name);
                    var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
                    dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
                    dropdown.ClearOptions();
                    dropdown.AddOptions(parameter.Options);
                    dropdown.SetValueWithoutNotify(Math.Max(0, parameter.Options.IndexOf(value)));
                    dropdown.RefreshShownValue();
                    dropdown.onValueChanged.AddListener(i => parameter.Value = parameter.Options[i]);
                    break;
                }
                default:
                {
                    bool timing = parameter.Type == AdeScriptParameterType.Timing;
                    var row = Clone(timing ? timingRowTemplate : textRowTemplate, "Field", form);
                    SetLabel(row, row.GetComponentInChildren<TMP_Text>(true), parameter.Name);
                    var input = row.GetComponentInChildren<TMP_InputField>(true);
                    input.onValueChanged = new TMP_InputField.OnChangeEvent();
                    input.onEndEdit = new TMP_InputField.SubmitEvent();
                    input.SetTextWithoutNotify(value);
                    input.onValueChanged.AddListener(text => parameter.Value = text);
                    var now = timing ? row.Find("当前")?.GetComponent<Button>() : null;
                    if (now)
                    {
                        now.onClick = new Button.ButtonClickedEvent();
                        now.onClick.AddListener(() => input.text = ArcGameplayManager.Instance.ChartTiming.ToString(CultureInfo.InvariantCulture));
                    }
                    break;
                }
            }
        }

        // ---- Running.

        private void Execute(AdeScriptTool tool)
        {
            if (AdeScriptLibrary.Running)
            {
                ShowStatus("已有工具正在执行。", true);
                return;
            }
            running?.Dispose();
            running = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            ShowStatus($"正在执行「{tool.Name}」…", false);
            ExecuteAsync(tool, running.Token).Forget();
        }

        private async UniTaskVoid ExecuteAsync(AdeScriptTool tool, CancellationToken cancellation)
        {
            try
            {
                await AdeScriptLibrary.Run(tool, cancellation);
            }
            catch (OperationCanceledException)
            {
                ShowStatus($"已取消「{tool.Name}」。", false);
            }
            catch (Exception ex)
            {
                // An error raised by the script itself is shown in the card, not as a program fault.
                bool scriptError = ex is MoonSharp.Interpreter.InterpreterException && ex.InnerException == null;
                if (!scriptError && !(ex is FormatException) && !(ex is InvalidOperationException)) Debug.LogException(ex);
                ShowStatus($"「{tool.Name}」执行失败，谱面未修改：\n{AdeScriptLibrary.Describe(ex)}", true);
            }
        }

        private void ShowStatus(string message, bool error)
        {
            if (!status) return;
            status.text = message;
            status.gameObject.SetActive(message.Length > 0);
            status.color = error ? new Color32(232, 113, 113, 255) : AdeUiTheme.Current.Muted;
            if (error && AdeToast.Instance) AdeToast.Instance.Show(message.Split('\n')[0]);
        }

        // ---- Helpers.

        private Transform Clone(Transform template, string name, Transform parent = null)
        {
            var copy = Instantiate(template.gameObject, parent ? parent : content, false);
            copy.name = name;
            copy.SetActive(true);
            if (!parent) built.Add(copy);
            return copy.transform;
        }

        // The template's label has a fixed width and one line; a long name wraps and the row grows.
        private void SetLabel(Transform row, TMP_Text label, string text)
        {
            AdeEditorText.Configure(label, toolTextSize);
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            if (label.TryGetComponent<LayoutElement>(out var labelLayout))
            {
                labelLayout.minWidth = labelLayout.preferredWidth = 0;
                labelLayout.flexibleWidth = 1; labelLayout.preferredHeight = -1;
            }
            if (row.TryGetComponent<LayoutElement>(out var rowLayout))
            {
                rowLayout.preferredHeight = -1; rowLayout.minWidth = rowLayout.preferredWidth = 0; rowLayout.flexibleWidth = 1;
            }
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout)
            {
                layout.childControlWidth = true; layout.childForceExpandWidth = false;
                layout.spacing = 12; layout.childAlignment = TextAnchor.MiddleLeft;
            }
            foreach (Transform child in row)
            {
                var input = child.GetComponent<TMP_InputField>();
                var dropdown = child.GetComponent<TMP_Dropdown>();
                if (!input && !dropdown) continue;
                var controlText = input ? input.textComponent : dropdown.captionText;
                controlText.fontSize = toolTextSize;
                float width = controlText.GetPreferredValues("0000000000").x + 24;
                if (layout && !row.Find("当前"))
                    layout.padding = new RectOffset(layout.padding.left, Mathf.RoundToInt(timeButtonSpace), layout.padding.top, layout.padding.bottom);
                var size = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = width; size.flexibleWidth = 0;
            }
        }

        private void SetButton(Button button, string text, UnityEngine.Events.UnityAction action)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            var colors = button.colors; colors.fadeDuration = 0; button.colors = colors;
            button.name = text;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label)
            {
                AdeEditorText.Configure(label, toolTextSize);
                label.text = text; label.alignment = TextAlignmentOptions.Center;
                foreach (var layout in button.GetComponents<LayoutGroup>()) layout.enabled = false;
                if (label.TryGetComponent<LayoutElement>(out var size)) size.ignoreLayout = true;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            }
        }
    }
}

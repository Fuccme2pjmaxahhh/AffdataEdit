using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Arcade.Gameplay;
using Cysharp.Threading.Tasks;
using TMPro;
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
        private TMP_Text sectionTemplate, status;
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
            if (themeRevision == AdeUiTheme.Revision) return;
            themeRevision = AdeUiTheme.Revision;
            foreach (var row in rows) if (row) StyleRow(row);
        }

        // Same look as the problems card's issue rows.
        private static void StyleRow(Button row)
        {
            row.image.sprite = AdeUiTheme.RoundedSurface;
            row.image.color = Color.white;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = AdeUiTheme.Current.Input;
            colors.highlightedColor = colors.selectedColor = AdeUiTheme.Current.Selection;
            colors.pressedColor = Color.Lerp(AdeUiTheme.Current.Selection, AdeUiTheme.Current.Text, .12f);
            row.colors = colors;
            row.transform.Find("Reason").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Text;
            row.transform.Find("Location").GetComponent<TMP_Text>().color = AdeUiTheme.Current.Muted;
        }

        private void OnDestroy()
        {
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

            var cardObject = Instantiate(problems.gameObject, right, false);
            cardObject.name = "ScriptsCard";
            card = cardObject.GetComponent<AdeRightSideCard>();
            var view = cardObject.transform.Find("View");
            content = (RectTransform)view.Find("Problems/Viewport/Content");
            rowTemplate = content.Find("IssueTemplate")?.GetComponent<Button>();
            status = view.Find("Label")?.GetComponent<TMP_Text>();
            var title = view.Find("Header/Label")?.GetComponent<TMP_Text>();
            if (!card || !rowTemplate || !status || !title) return false;
            // The copy keeps the source card's visibility; start closed like the other cards.
            view.gameObject.SetActive(false);
            title.text = "外置脚本";
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
            return true;
        }

        // ---- List.

        private void Reload()
        {
            if (AdeScriptLibrary.Running) return;
            AdeScriptLibrary.Load();
            Rebuild();
            if (AdeScriptLibrary.LoadErrors.Count > 0)
                ShowStatus("部分脚本加载失败：\n" + string.Join("\n", AdeScriptLibrary.LoadErrors), true);
            else ShowStatus(AdeScriptLibrary.Tools.Count == 0
                ? "没有找到脚本。把 Arcade Alpha 的 .lua 放进 Scripts/AA，Arcade-One 的放进 Scripts/A1，然后点“重新加载”。"
                : $"已加载 {AdeScriptLibrary.Tools.Count} 个工具。", false);
        }

        private void Rebuild()
        {
            foreach (var item in built) if (item) Destroy(item);
            built.Clear();
            forms.Clear();
            rows.Clear();

            var actions = Clone(actionRowTemplate, "Actions");
            var reload = actions.GetComponentInChildren<Button>(true);
            SetButton(reload, "重新加载", Reload);
            var open = Instantiate(reload.gameObject, reload.transform.parent, false).GetComponent<Button>();
            SetButton(open, "打开文件夹", () => Application.OpenURL(new Uri(AdeScriptLibrary.Folder).AbsoluteUri));

            foreach (var group in AdeScriptLibrary.Tools.GroupBy(t => t.Dialect))
            {
                var section = Clone(sectionTemplate.transform, "Section").GetComponent<TMP_Text>();
                section.text = group.Key == AdeScriptDialect.AA ? "Arcade Alpha 工具" : "Arcade-One 工具";
                foreach (var tool in group) AddRow(tool);
            }
            AdeUiTheme.Refresh();
        }

        private void AddRow(AdeScriptTool tool)
        {
            var row = Instantiate(rowTemplate, content, false);
            row.name = "Tool";
            row.gameObject.SetActive(true);
            built.Add(row.gameObject);
            rows.Add(row);
            StyleRow(row);
            row.transform.Find("Reason").GetComponent<TMP_Text>().text = tool.Name;
            string kind = tool.Kind == AdeScriptToolKind.Parameters ? "填写参数后运行"
                : tool.Kind == AdeScriptToolKind.Selection ? "作用于选中物件，点击即运行" : "点击后在轨道上依次选取";
            row.transform.Find("Location").GetComponent<TMP_Text>().text = kind + " · " + tool.Source;
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
                forms.Remove(tool);
                built.Remove(open.gameObject);
                Destroy(open.gameObject);
                return;
            }
            var form = new GameObject("Form", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
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
        private static void SetLabel(Transform row, TMP_Text label, string text)
        {
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            if (label.TryGetComponent<LayoutElement>(out var labelLayout)) labelLayout.preferredHeight = -1;
            if (row.TryGetComponent<LayoutElement>(out var rowLayout)) rowLayout.preferredHeight = -1;
        }

        private static void SetButton(Button button, string text, UnityEngine.Events.UnityAction action)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            button.name = text;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = text;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Cysharp.Threading.Tasks;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using UnityEngine;

namespace Arcade.Compose.Scripting
{
    internal enum AdeScriptDialect { AA, A1 }

    // Parameters: a form, then run. Selection: runs on the selected notes. Interactive: picks
    // times or positions on the track first (Alpha's NoteEditor.CreateEditing).
    internal enum AdeScriptToolKind { Parameters, Selection, Interactive }

    // Same order as Arcade Alpha's ArgumentType.
    internal enum AdeScriptParameterType { String, Float, Int, Bool, Timing, List }

    internal sealed class AdeScriptParameter
    {
        public string Name = "";
        public AdeScriptParameterType Type;
        public string Default = "";
        public List<string> Options;
        public string Value;
    }

    internal sealed class AdeScriptTool
    {
        public string Name, Source;
        public AdeScriptDialect Dialect;
        public AdeScriptToolKind Kind;
        public bool IsQuick => Dialect == AdeScriptDialect.AA && Kind != AdeScriptToolKind.Parameters;
        public Script Script;
        public DynValue Function;
        public Aa.AsyncEditing Editing;
        public readonly List<AdeScriptParameter> Parameters = new List<AdeScriptParameter>();
    }

    // Loads Lua tools from Scripts/AA (Arcade Alpha) and Scripts/A1 (Arcade-One) beside the app.
    // Each file runs in its own MoonSharp script with only that dialect's globals.
    internal static class AdeScriptLibrary
    {
        public static readonly List<AdeScriptTool> Tools = new List<AdeScriptTool>();
        public static readonly List<string> LoadErrors = new List<string>();
        public static event Action<string, bool> Reported;
        public static bool Running { get; private set; }

        private static Script loadingScript;
        private static string loadingSource;

        public static string Folder => Path.Combine(AdeStorage.DataFolder, "Scripts");
        public static string FolderOf(AdeScriptDialect dialect) => Path.Combine(Folder, dialect.ToString());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Tools.Clear();
            LoadErrors.Clear();
            Reported = null;
            Running = false;
        }

        public static void Load()
        {
            UserData.RegistrationPolicy = InteropRegistrationPolicy.Automatic;
            Tools.Clear();
            LoadErrors.Clear();
            foreach (AdeScriptDialect dialect in Enum.GetValues(typeof(AdeScriptDialect)))
            {
                string folder = FolderOf(dialect);
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    LoadErrors.Add($"{dialect}：无法创建脚本文件夹（{ex.Message}）");
                    continue;
                }
                var files = Directory.GetFiles(folder, "*.lua").OrderBy(f => f, StringComparer.Ordinal);
                foreach (string path in files)
                {
                    string file = Path.GetFileName(path);
                    // Alpha's generated type hints, not a tool.
                    if (Path.GetFileNameWithoutExtension(file).Equals("define", StringComparison.OrdinalIgnoreCase)) continue;
                    int before = Tools.Count;
                    try
                    {
                        if (dialect == AdeScriptDialect.AA) LoadAa(path, file);
                        else LoadA1(path, file);
                    }
                    catch (Exception ex)
                    {
                        Tools.RemoveRange(before, Tools.Count - before);
                        LoadErrors.Add($"{dialect}/{file}：{Describe(ex)}");
                    }
                }
            }
        }

        private static Script NewScript()
        {
            var script = new Script();
            script.Options.DebugPrint = text => Debug.Log("[Lua] " + text);
            return script;
        }

        private static void LoadAa(string path, string file)
        {
            var script = NewScript();
            Aa.AaGlobals.Assign(script.Globals);
            loadingScript = script;
            loadingSource = file;
            try
            {
                script.DoString(File.ReadAllText(path), null, file);
            }
            finally
            {
                loadingScript = null;
                loadingSource = null;
            }
        }

        public static void RegisterAa(string name, AdeScriptToolKind kind, Closure function, Aa.AsyncEditing editing, List<AdeScriptParameter> parameters)
        {
            if (loadingScript == null) throw new ScriptRuntimeException("只能在加载脚本时注册工具。");
            var tool = new AdeScriptTool
            {
                Name = string.IsNullOrEmpty(name) ? "未命名工具" : name,
                Source = loadingSource,
                Dialect = AdeScriptDialect.AA,
                Kind = kind,
                Script = loadingScript,
                Function = function == null ? null : DynValue.NewClosure(function),
                Editing = editing,
            };
            if (parameters != null) tool.Parameters.AddRange(parameters);
            Tools.Add(tool);
        }

        private static void LoadA1(string path, string file)
        {
            var script = NewScript();
            A1.A1Globals.Assign(script.Globals);
            script.DoString(File.ReadAllText(path), null, file);
            var providers = script.Globals.Get("ToolsProviders");
            if (providers.Type != DataType.Table) throw new ScriptRuntimeException("没有声明 ToolsProviders。");
            var names = script.Globals.Get("ToolsNames");
            for (int i = 1; i <= providers.Table.Length; i++)
            {
                string provider = providers.Table.Get(i).CastToString();
                var function = script.Globals.Get(provider ?? "");
                if (function.Type != DataType.Function) throw new ScriptRuntimeException($"ToolsProviders 中的 {provider} 不是函数。");
                string name = names.Type == DataType.Table ? names.Table.Get(i).CastToString() : null;
                var tool = new AdeScriptTool
                {
                    Name = string.IsNullOrEmpty(name) ? provider : name,
                    Source = file,
                    Dialect = AdeScriptDialect.A1,
                    Kind = AdeScriptToolKind.Parameters,
                    Script = script,
                    Function = function,
                };
                var parameters = script.Globals.Get(provider + "_Params");
                var values = script.Globals.Get(provider + "_ParamsValue");
                if (parameters.Type == DataType.Table)
                {
                    for (int j = 1; j <= parameters.Table.Length; j++)
                    {
                        tool.Parameters.Add(new AdeScriptParameter
                        {
                            Name = parameters.Table.Get(j).CastToString() ?? "",
                            Type = AdeScriptParameterType.String,
                            Default = values.Type == DataType.Table ? values.Table.Get(j).CastToString() ?? "" : "",
                        });
                    }
                }
                Tools.Add(tool);
            }
        }

        public static void Report(string message, bool error) => Reported?.Invoke(message ?? "", error);

        public static string Describe(Exception ex)
            => ex is InterpreterException lua ? lua.DecoratedMessage ?? lua.Message : ex.Message;

        // ---- Selection, ordered by time like Alpha's GetSelectedNotes.

        public static List<ArcEvent> SelectedEvents()
        {
            var notes = AdeSelectionManager.Instance.SelectedNotes;
            return notes.OfType<ArcTap>().Cast<ArcEvent>()
                .Concat(notes.OfType<ArcHold>())
                .Concat(notes.OfType<ArcArc>())
                .Concat(notes.OfType<ArcArcTap>())
                .OrderBy(e => e.Timing).ToList();
        }

        public static List<object> SelectedAaNotes()
        {
            AdeScriptSession.Require();
            return SelectedEvents().Select(Aa.AaConvert.Wrap).ToList();
        }

        // ---- Running.

        public static async UniTask Run(AdeScriptTool tool, CancellationToken cancellation)
        {
            if (Running) throw new InvalidOperationException("已有工具正在执行。");
            if (ArcGameplayManager.Instance.Chart == null) throw new InvalidOperationException("请先打开谱面。");
            Running = true;
            try
            {
                object[] args = tool.Kind == AdeScriptToolKind.Parameters ? Arguments(tool) : null;
                var picks = tool.Kind == AdeScriptToolKind.Interactive ? await Pick(tool.Editing, cancellation) : null;
                var session = AdeScriptSession.Begin(tool.Name);
                DynValue result;
                try
                {
                    var function = tool.Function;
                    if (tool.Kind == AdeScriptToolKind.Selection) args = new object[] { SelectedAaNotes() };
                    if (tool.Kind == AdeScriptToolKind.Interactive)
                    {
                        var list = new List<object> { SelectedAaNotes() };
                        list.AddRange(picks);
                        args = new object[] { list };
                        function = DynValue.NewClosure(tool.Editing.Function);
                    }
                    result = await Call(tool.Script, function, args, cancellation);
                }
                catch
                {
                    session.Discard();
                    throw;
                }
                bool changed = session.HasChanges;
                session.Commit();
                if (tool.Dialect == AdeScriptDialect.A1 && result.Type == DataType.String) Report(result.String, false);
                else Report(changed ? $"已执行「{tool.Name}」，可撤销。" : $"已执行「{tool.Name}」，谱面没有变化。", false);
            }
            finally
            {
                Running = false;
            }
        }

        private static object[] Arguments(AdeScriptTool tool)
        {
            var args = new object[tool.Parameters.Count];
            for (int i = 0; i < args.Length; i++)
            {
                var p = tool.Parameters[i];
                string text = (p.Value ?? p.Default).Trim();
                switch (p.Type)
                {
                    case AdeScriptParameterType.Float:
                        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f))
                            throw new FormatException($"「{p.Name}」需要填写数字。");
                        args[i] = f;
                        break;
                    case AdeScriptParameterType.Int:
                    case AdeScriptParameterType.Timing:
                        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                            throw new FormatException($"「{p.Name}」需要填写整数。");
                        args[i] = n;
                        break;
                    case AdeScriptParameterType.Bool:
                        args[i] = text == "true";
                        break;
                    default:
                        args[i] = p.Value ?? p.Default;
                        break;
                }
            }
            return args;
        }

        private static async UniTask<DynValue> Call(Script script, DynValue function, object[] args, CancellationToken cancellation)
        {
            var coroutine = script.CreateCoroutine(function).Coroutine;
            var result = coroutine.Resume(args ?? Array.Empty<object>());
            // A tool may yield; it continues on the next frame.
            while (coroutine.State == CoroutineState.Suspended)
            {
                await UniTask.NextFrame(cancellation);
                result = coroutine.Resume();
            }
            return result;
        }

        private static async UniTask<List<object>> Pick(Aa.AsyncEditing editing, CancellationToken cancellation)
        {
            var picks = new List<object>();
            if (editing.Commands.Count == 0) return picks;
            var operations = AdeOperationManager.Instance;
            if (operations.HasOngoingOperation) throw new InvalidOperationException("有正在进行的操作，请先完成或按 Esc 取消。");
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var task = PickAll(editing, picks, source.Token).Preserve();
            operations.TryExecuteOperation(() => new AdeOngoingOperation { task = task, cancellation = source, keepSelection = true });
            try
            {
                await task;
            }
            finally
            {
                source.Dispose();
            }
            return picks;
        }

        private static async UniTask PickAll(Aa.AsyncEditing editing, List<object> picks, CancellationToken cancellation)
        {
            int timing = ArcGameplayManager.Instance.ChartTiming;
            foreach (var (name, position) in editing.Commands)
            {
                Report($"请{(position ? "在判定面上点击选择位置" : "在轨道上点击选择时间")}：{name}（Esc 取消）", false);
                if (position)
                {
                    var coordinate = await AdeCursorManager.Instance.SelectCoordinate(timing, Progress.Create<Vector2>(_ => { }), cancellation);
                    picks.Add(new Aa.Vec2Lua(coordinate.x, coordinate.y));
                }
                else
                {
                    timing = await AdeCursorManager.Instance.SelectTiming(Progress.Create<int>(_ => { }), cancellation);
                    picks.Add(timing);
                }
                // The click that picked this value must not also pick the next one.
                await UniTask.NextFrame(cancellation);
            }
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Arcade.Aff;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Compose.Command
{
    // A null before/after means insertion/deletion. Keep the event identity across undo.
    public sealed class ChartControlCommand : ICommand, ICommandMemoryCost
    {
        private readonly ArcEvent target, before, after;
        long ICommandMemoryCost.EstimatedBytes => 128 + CommandMemory.Estimate(before) + CommandMemory.Estimate(after);
        private readonly int originalIndex;
        public string Name { get; }

        public ChartControlCommand(ArcEvent target, ArcEvent before, ArcEvent after)
        {
            this.target = target;
            this.before = before?.Clone();
            this.after = after?.Clone();
            originalIndex = target is ArcCamera camera ? ArcCameraManager.Instance.Cameras.IndexOf(camera)
                : ArcSceneControlManager.Instance.SceneControls.IndexOf((ArcSceneControl)target);
            Name = (before == null ? "添加 " : after == null ? "删除 " : "修改 ")
                + (target is ArcCamera ? "Camera" : "SceneControl");
        }

        public void Do() => Apply(after);
        public void Undo() => Apply(before);

        private void Apply(ArcEvent value)
        {
            if (value != null) target.Assign(value);
            if (target is ArcCamera camera)
            {
                var manager = ArcCameraManager.Instance;
                manager.Cameras.Remove(camera);
                if (value != null) manager.Cameras.Insert(originalIndex < 0 ? manager.Cameras.Count
                    : Math.Min(originalIndex, manager.Cameras.Count), camera);
                manager.Load(manager.Cameras);
                manager.ResetCamera();
            }
            else
            {
                var sc = (ArcSceneControl)target;
                var manager = ArcSceneControlManager.Instance;
                manager.SceneControls.Remove(sc);
                if (value != null) manager.SceneControls.Insert(originalIndex < 0 ? manager.SceneControls.Count
                    : Math.Min(originalIndex, manager.SceneControls.Count), sc);
                manager.Load(manager.SceneControls);
            }
        }
    }

    public sealed class EditTimingGroupAttributes : ICommand, ICommandMemoryCost
    {
        private readonly ArcTimingGroup group;
        private readonly string before, after;
        long ICommandMemoryCost.EstimatedBytes => 128 + 2L * ((before?.Length ?? 0) + (after?.Length ?? 0));
        public string Name => "修改时间组属性";
        public EditTimingGroupAttributes(ArcTimingGroup group, string attributes)
        { this.group = group; before = group.Attributes; after = attributes; }
        public void Do() => Apply(after);
        public void Undo() => Apply(before);
        private void Apply(string attributes)
        {
            group.ApplyAttributes(attributes);
            ArcGameplayManager.Instance.ResetJudge();
            ArcTimingManager.Instance.UpdateTimingGroup(group);
            ArcArcManager.Instance.CalculateArcRelationship();
        }
    }

    // Validate drafts without logging parser errors or changing the loaded chart.
    public static class ChartControlText
    {
        public static readonly string[] SceneTypes = { "trackhide", "trackshow", "trackdisplay", "hidegroup",
            "enwidencamera", "enwidenlanes", "arcahvdistort", "arcahvdebris", "redline" };
        public static readonly string[] CameraTypes = { "l", "qi", "qo", "reset", "s" };
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        public static string Number(float value)
        {
            // AFF has no exponent syntax. Expand round-trip notation without losing small values.
            string number = value.ToString("R", Culture);
            int exponentAt = number.IndexOf('E');
            if (exponentAt >= 0)
            {
                int exponent = int.Parse(number.Substring(exponentAt + 1), Culture);
                string mantissa = number.Substring(0, exponentAt);
                string sign = mantissa.StartsWith("-", StringComparison.Ordinal) ? "-" : "";
                mantissa = mantissa.TrimStart('-');
                int point = (mantissa.Contains(".") ? mantissa.IndexOf('.') : mantissa.Length) + exponent;
                string digits = mantissa.Replace(".", "");
                number = sign + (point <= 0 ? "0." + new string('0', -point) + digits
                    : point >= digits.Length ? digits + new string('0', point - digits.Length) : digits.Insert(point, "."));
            }
            return number.Contains(".") ? number : number + ".0";
        }
        public static bool Float(string text, out float value) => float.TryParse(text, NumberStyles.Float, Culture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Integer(string text, out int value) => int.TryParse(text, NumberStyles.Integer, Culture, out value);

        public static string Format(ArcEvent value)
        {
            if (value is ArcCamera c)
                return $"camera({c.Timing},{Number(c.Move.x)},{Number(c.Move.y)},{Number(c.Move.z)},"
                    + $"{Number(c.Rotate.x)},{Number(c.Rotate.y)},{Number(c.Rotate.z)},{CameraTypes[(int)c.CameraType]},{c.Duration});";
            var sc = (RawAffSceneControl)((ArcSceneControl)value).IntoRawItem();
            string args = string.Join(",", sc.Params.Select(x => x is RawAffInt i ? i.data.ToString(Culture)
                : x is RawAffFloat f ? Number(f.data) : ((RawAffWord)x).data));
            return $"scenecontrol({sc.Timing},{sc.Type}{(args.Length == 0 ? "" : "," + args)});";
        }

        public static bool TryParse(string text, bool camera, ArcTimingGroup group, out ArcEvent result, out string error)
        {
            result = null;
            error = camera ? "格式：camera(时间,位移X,Y,Z,旋转X,Y,Z,缓动,时长);" : "格式：scenecontrol(时间,类型,参数…);";
            var match = Regex.Match(text.Trim(), camera ? @"^camera\s*\(([^()]*)\)\s*;$" : @"^scenecontrol\s*\(([^()]*)\)\s*;$");
            if (!match.Success) return false;
            string[] parts = match.Groups[1].Value.Split(',').Select(x => x.Trim()).ToArray();
            if (parts.Length < 2 || !Integer(parts[0], out int time)) { error = "时间必须是有效的整数毫秒。"; return false; }
            if (camera)
            {
                if (parts.Length != 9) return false;
                var values = new float[6];
                for (int i = 0; i < 6; i++)
                    if (!Float(parts[i + 1], out values[i])) { error = "位移和旋转必须是有限数值。"; return false; }
                int ease = Array.IndexOf(CameraTypes, parts[7]);
                if (ease < 0) { error = "缓动可选 l、qi、qo、s、reset。"; return false; }
                if (!Integer(parts[8], out int duration) || duration < 0 || (long)time + duration > int.MaxValue)
                { error = "时长必须是非负整数，且结束时间不能超出范围。"; return false; }
                result = new ArcCamera { Timing = time, TimingGroup = group, Duration = duration, CameraType = (CameraEaseType)ease,
                    Move = new Vector3(values[0], values[1], values[2]), Rotate = new Vector3(values[3], values[4], values[5]) };
            }
            else
            {
                string type = parts[1];
                if (!Regex.IsMatch(type, @"^[a-zA-Z][a-zA-Z_0-9]*$")) { error = "SC 类型必须是以字母开头的标识符。"; return false; }
                var args = new List<IRawAffValue>();
                foreach (string p in parts.Skip(2))
                {
                    if (Integer(p, out int i)) args.Add(new RawAffInt { data = i });
                    else if (Float(p, out float f)) args.Add(new RawAffFloat { data = f });
                    else if (Regex.IsMatch(p, @"^[a-zA-Z][a-zA-Z_0-9]*$") && p != "NaN" && p != "Infinity") args.Add(new RawAffWord { data = p });
                    else { error = "SC 参数必须是有限数值或标识符。"; return false; }
                }
                if (Array.IndexOf(SceneTypes, type) >= 0)
                {
                    bool noArgs = type == "trackhide" || type == "trackshow";
                    if (noArgs && args.Count != 0) { error = type + " 不接受参数。"; return false; }
                    if (!noArgs)
                    {
                        if (args.Count != 2 || !Float(parts[2], out float duration) || duration < 0 || !(args[1] is RawAffInt))
                        { error = type + " 需要两个参数：非负时长、整数值。"; return false; }
                        args[0] = new RawAffFloat { data = duration };
                        int flag = ((RawAffInt)args[1]).data;
                        if ((type == "hidegroup" || type == "enwidencamera" || type == "enwidenlanes") && flag != 0 && flag != 1)
                        { error = "开关值只能为 0 或 1。"; return false; }
                    }
                }
                result = new ArcSceneControl(new RawAffSceneControl { Timing = time, Type = type, Params = args }, group);
            }
            error = "";
            return true;
        }

        public static bool ValidateAttributes(string text, out string error)
        {
            error = "属性仅支持字母、数字和下划线。";
            if (text.Length != 0 && !Regex.IsMatch(text, @"^[a-zA-Z][a-zA-Z_0-9]*$")) return false;
            foreach (var token in text.Split('_'))
            {
                if ((token.StartsWith("anglex", StringComparison.Ordinal) || token.StartsWith("angley", StringComparison.Ordinal))
                    && (!Integer(token.Substring(6), out int angle) || angle < 0))
                { error = "anglex / angley 后需填写非负整数（单位 0.1°）。"; return false; }
            }
            error = "";
            return true;
        }
    }
}

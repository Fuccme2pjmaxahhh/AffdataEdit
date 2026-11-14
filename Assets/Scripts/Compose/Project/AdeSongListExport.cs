using System;
using System.IO;
using System.Threading;
using Arcade.Compose.Dialog;
using Arcade.Gameplay;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arcade.Compose
{
    public sealed class AdeSongListExport : MonoBehaviour
    {
        private CancellationTokenSource selection;
        private AdeDualDialog overwriteDialog;
        private TMPro.TMP_Text overwriteHint;
        private string pendingFolder;
        private JObject pendingSong;

        private void Start()
        {
            AdeUiKit.CreateIconButton(ArcadeComposeManager.Instance.RightBarView, "ExportSongList", "ExportSongList", Begin);
            AdeUiTheme.Refresh();
        }

        public void Begin()
        {
            var project = AdeProjectManager.Instance;
            if (!ArcGameplayManager.Instance.IsLoaded || project.CurrentProjectMetadata == null || project.IsLoading)
            {
                AdeToast.Instance.Show("请先打开本地谱面");
                return;
            }
            if (!ArcadeComposeManager.Instance.IsEditorMode)
            {
                AdeToast.Instance.Show("请先切回编辑模式，再选择预览范围");
                return;
            }
            AdeOperationManager.Instance.TryExecuteOperation(() =>
            {
                selection?.Dispose();
                selection = new CancellationTokenSource();
                return new AdeOngoingOperation
                {
                    cancellation = selection, task = SelectPreview(selection.Token), keepSelection = true
                };
            });
        }

        private async UniTask SelectPreview(CancellationToken token)
        {
            var project = AdeProjectManager.Instance;
            string folder = project.CurrentProjectFolder;
            try
            {
                AdeToast.Instance.Show("导出 songlist：点击预览起点，再点击终点；Esc 取消");
                var progress = Cysharp.Threading.Tasks.Progress.Create<int>(_ => { });
                int start = await AdeCursorManager.Instance.SelectTiming(progress, token, timingForArc: true);
                AdeToast.Instance.Show("请选择预览终点；Esc 取消");
                int end = await AdeCursorManager.Instance.SelectTiming(progress, token, timingForArc: true);
                token.ThrowIfCancellationRequested();
                if (folder != project.CurrentProjectFolder) return;
                var skin = JsonConvert.DeserializeObject<SkinPreference>(AdeSettings.GetString("AdeSkinDialog", "{}")) ?? new SkinPreference();
                JObject song = AdeSongListExporter.Build(project.CurrentProjectMetadata, folder, start, end,
                    ArcGameplayManager.Instance.ChartAudioOffset, skin.SkinSide.Id(), skin.SelectedBackground);
                if ((long)song["songs"][0]["audioPreviewEnd"] > ArcGameplayManager.Instance.Length)
                    throw new ArgumentException("预览终点超过音频长度，请重新选择。");
                if (File.Exists(Path.Combine(folder, "songlist.json"))) ConfirmOverwrite(folder, song);
                else Write(folder, song, false);
            }
            catch (OperationCanceledException) { AdeToast.Instance.Show("已取消导出 songlist"); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is JsonException)
            { AdeBasicSingleDialogContent.Instance.Show(error.Message, "导出失败"); }
        }

        private void ConfirmOverwrite(string folder, JObject song)
        {
            if (!overwriteDialog)
            {
                overwriteDialog = AdeUiKit.CloneDialog(Feature.AdeObsManager.Instance.OBSDialog, "ExportSongListDialog");
                overwriteDialog.Title.text = "导出 songlist";
                overwriteDialog.LeftButtonText.text = "覆盖导出";
                overwriteDialog.RightButtonText.text = "取消";
                overwriteHint = AdeUiKit.CreateLabelRow(AdeUiKit.Content(overwriteDialog),
                    AdeBasicSingleDialogContent.Instance.Content.transform, "");
                AdeUiKit.SetOnClick(overwriteDialog.RightButton, () => overwriteDialog.Close());
                AdeUiKit.SetOnClick(overwriteDialog.LeftButton, () =>
                {
                    overwriteDialog.Close();
                    Write(pendingFolder, pendingSong, true);
                });
            }
            pendingFolder = folder;
            pendingSong = song;
            overwriteHint.text = "该文件已存在：\n" + Path.Combine(folder, "songlist.json")
                + "\n\n将替换整个文件；原文件保留为 songlist.json.bak。";
            overwriteHint.color = AdeUiTheme.Current.Text;
            overwriteDialog.Open();
        }

        private static void Write(string folder, JObject song, bool overwrite)
        {
            try
            {
                AdeSongListExporter.Write(folder, song, overwrite);
                AdeToast.Instance.Show("已导出 " + Path.Combine(folder, "songlist.json"));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { AdeBasicSingleDialogContent.Instance.Show(error.Message, "导出失败"); }
        }

        private void OnDestroy()
        {
            selection?.Cancel();
            selection?.Dispose();
        }
    }
}

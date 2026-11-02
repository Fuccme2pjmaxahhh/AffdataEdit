using Arcade.Util.UniTaskHelper;
using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Arcade.Compose.Feature;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using Debug = UnityEngine.Debug;

namespace Arcade.Compose
{
    // Built players installed by the updater check R2's latest.json when the editor scene
    // opens. Only a confirmed newer version blocks the editor; if the check cannot complete
    // (offline, timeout, bad manifest) the user may retry or continue with this version.
    // The updater beside AffdataEdit is kept current from its own manifest, so the updater
    // never has to replace itself.
    public sealed class AdeUpdateChecker : MonoBehaviour
    {
        private const string EditorSceneName = "ArcEditor";
        private static readonly Regex CommitPattern = new Regex("^[0-9a-fA-F]{40}$");
        private static readonly bool Mac = Application.platform == RuntimePlatform.OSXPlayer;
        private static readonly string UpdaterName = Mac ? "AffdataEdit-Updater" : "AffdataEdit-Updater.exe";
        // AffdataEdit_Data on Windows, AffdataEdit.app/Contents on macOS.
        private static readonly string Folder = Mac
            ? Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath))
            : Path.GetDirectoryName(Application.dataPath);

        private bool updateRequired, entered, refreshingUpdater;
        private AdeDualDialog dialog;
        private Text messageText;
        private GameObject blocker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name == EditorSceneName)
                new GameObject(nameof(AdeUpdateChecker)).AddComponent<AdeUpdateChecker>();
        }

        private void Start()
        {
            // Builds without the updater's files (development builds) are not checked.
            if (!File.Exists(Path.Combine(Folder, "build-version.txt")) || !File.Exists(Path.Combine(Folder, "updater-config.json")))
            {
                Destroy(gameObject);
                return;
            }
            AdeDualDialog source = AdeObsManager.Instance != null ? AdeObsManager.Instance.OBSDialog : null;
            Transform inputRow = source != null ? AdeUiKit.FindRow(source, "Address") : null;
            if (inputRow == null)
            {
                Debug.LogWarning("Update dialog templates not found");
                return;
            }
            dialog = AdeUiKit.CloneDialog(source, "UpdateDialog");
            dialog.Title.text = "检查更新";
            messageText = AdeUiKit.CreateLabelRow(AdeUiKit.Content(dialog), inputRow.GetComponentInChildren<Text>(true).transform, "");
            dialog.LeftButtonText.text = "重试";
            AdeUiKit.SetOnClick(dialog.LeftButton, () =>
            {
                if (updateRequired) LaunchUpdater().Forget();
                else CheckAtStartup().Forget();
            });
            AdeUiKit.SetOnClick(dialog.RightButton, () =>
            {
                if (updateRequired) Application.Quit();
                else dialog.Close();
            });
            // Closing the dialog (continue or the close button) enters the editor unless an
            // update is required.
            Transform close = dialog.View.transform.Find("ThemeClose");
            if (close) AdeUiKit.SetOnClick(close.GetComponent<Button>(), dialog.Close);
            dialog.OnClose += () =>
            {
                if (!updateRequired) Enter();
            };

            // Full-screen blocker beneath the dialog so the editor behind cannot be used.
            Transform layer = AdeDialogManager.Instance.Opening;
            blocker = new GameObject("UpdateBlocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(layer.parent, false);
            blocker.transform.SetSiblingIndex(layer.GetSiblingIndex());
            var rect = (RectTransform)blocker.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            AdeInputManager.Instance.Controls.Disable();
            dialog.Open();
            CheckAtStartup().Forget();
        }

        private void SetState(bool isChecking, string text)
        {
            messageText.text = text;
            bool failed = !isChecking && !updateRequired;
            dialog.LeftButton.gameObject.SetActive(failed || updateRequired);
            dialog.LeftButtonText.text = updateRequired ? "立即更新" : "重试";
            AdeUiKit.SetInteractable(dialog.RightButton, !isChecking);
            dialog.RightButtonText.text = updateRequired ? "退出" : "继续使用";
            // The dark theme's close button is only offered once the check has failed.
            Transform close = dialog.View.transform.Find("ThemeClose");
            if (close) close.gameObject.SetActive(failed);
        }

        private void Pass()
        {
            dialog.Close();
            Enter();
        }

        private void Enter()
        {
            if (entered) return;
            entered = true;
            AdeInputManager.Instance.Controls.Enable();
            Destroy(blocker);
            // Destroying the object would stop a background updater refresh.
            if (!refreshingUpdater) Destroy(gameObject);
        }

        private async UniTask CheckAtStartup()
        {
            SetState(true, "正在检查最新版本…");
            string versionPath = Path.Combine(Folder, "build-version.txt");
            string configPath = Path.Combine(Folder, "updater-config.json");
            if (!File.Exists(versionPath) || !File.Exists(configPath))
            {
                FailCheck();
                return;
            }

            string currentVersion;
            Uri manifestUri;
            try
            {
                currentVersion = File.ReadAllText(versionPath).Trim();
                string url = (string)JObject.Parse(File.ReadAllText(configPath))["manifestUrl"];
                if (!CommitPattern.IsMatch(currentVersion)
                    || !Uri.TryCreate(url, UriKind.Absolute, out manifestUri)
                    || manifestUri.Scheme != Uri.UriSchemeHttps)
                {
                    FailCheck();
                    return;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is JsonException || error is ArgumentException)
            {
                Debug.Log($"Update check skipped: {error.Message}");
                FailCheck();
                return;
            }

            string latestVersion;
            using (var request = UnityWebRequest.Get(manifestUri.AbsoluteUri))
            {
                request.timeout = 10;
                request.SetRequestHeader("Cache-Control", "no-cache");
                await request.SendWithResultAsync(destroyCancellationToken);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    FailCheck();
                    Debug.Log($"Update check unavailable: {request.error}");
                    return;
                }
                try
                {
                    JObject manifest = JObject.Parse(request.downloadHandler.text);
                    if ((int?)manifest["schema"] != 1) { FailCheck(); return; }
                    latestVersion = (string)manifest["version"];
                }
                catch (Exception error) when (error is JsonException || error is ArgumentException
                    || error is FormatException || error is InvalidCastException || error is OverflowException)
                {
                    FailCheck();
                    Debug.Log($"Update check skipped: {error.Message}");
                    return;
                }
            }
            if (!refreshingUpdater) RefreshUpdater(manifestUri).Forget();
            if (latestVersion == null || !CommitPattern.IsMatch(latestVersion))
            {
                FailCheck();
                return;
            }
            if (string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
            {
                Pass();
                return;
            }
            updateRequired = true;
            SetState(false, "当前版本不是最新版，请先更新。\n\n点击“立即更新”后 AffdataEdit 会退出，\n更新器完成更新后会重新启动它。");
        }

        // Replaces the updater beside AffdataEdit when its manifest lists a different file.
        // Failures only keep the current updater, which can still update AffdataEdit.
        private async UniTask RefreshUpdater(Uri manifestUri)
        {
            refreshingUpdater = true;
            try
            {
                var updaterUri = new Uri(manifestUri, $"../updater/{(Mac ? "macos" : "windows")}/latest.json");
                string target = Path.Combine(Folder, UpdaterName);
                string sha;
                long size;
                using (var request = UnityWebRequest.Get(updaterUri.AbsoluteUri))
                {
                    request.timeout = 10;
                    request.SetRequestHeader("Cache-Control", "no-cache");
                    await request.SendWithResultAsync(destroyCancellationToken);
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"Updater check unavailable: {request.error}");
                        return;
                    }
                    try
                    {
                        JToken file = JObject.Parse(request.downloadHandler.text)["files"]?[0];
                        sha = ((string)file?["sha256"])?.ToLowerInvariant();
                        size = (long?)file?["size"] ?? -1;
                        if ((string)file?["path"] != UpdaterName || sha == null || sha.Length != 64 || size < 0)
                            throw new FormatException("unexpected updater manifest");
                        if (File.Exists(target) && new FileInfo(target).Length == size && Hash(target) == sha)
                        {
                            return;
                        }
                    }
                    catch (Exception error) when (error is JsonException || error is ArgumentException || error is FormatException
                        || error is InvalidCastException || error is OverflowException || error is IOException || error is UnauthorizedAccessException)
                    {
                        Debug.Log($"Updater check skipped: {error.Message}");
                        return;
                    }
                }
                string download = target + ".download";
                using (var request = new UnityWebRequest(new Uri(updaterUri, "objects/" + sha).AbsoluteUri, "GET",
                    new DownloadHandlerFile(download) { removeFileOnAbort = true }, null))
                {
                    request.timeout = 120;
                    await request.SendWithResultAsync(destroyCancellationToken);
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                            throw new IOException(request.error);
                        if (new FileInfo(download).Length != size || Hash(download) != sha)
                            throw new IOException("updater hash mismatch");
                        if (Mac) Process.Start("/bin/chmod", $"755 \"{download}\"").WaitForExit();
                        // Replaced by renaming; on Windows a running updater cannot be deleted and is kept.
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(download, target);
                        Debug.Log("Updater refreshed");
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                        || error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
                    {
                        Debug.Log($"Updater refresh failed: {error.Message}");
                        try { File.Delete(download); } catch (Exception) { }
                    }
                }
            }
            finally { EndRefresh(); }
        }

        private void EndRefresh()
        {
            refreshingUpdater = false;
            if (this && entered) Destroy(gameObject);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        // Opens the updater (in Terminal on macOS) and quits so it can replace AffdataEdit.
        private async UniTask LaunchUpdater()
        {
            AdeUiKit.SetInteractable(dialog.LeftButton, false);
            while (refreshingUpdater) await UniTask.NextFrame(destroyCancellationToken);
            string updater = Path.Combine(Folder, UpdaterName);
            try
            {
                if (!File.Exists(updater)) throw new FileNotFoundException(updater);
                if (Mac) Process.Start("/usr/bin/open", $"-a Terminal \"{updater}\"");
                else Process.Start(new ProcessStartInfo(updater) { UseShellExecute = true, WorkingDirectory = Folder });
                Application.Quit();
            }
            catch (Exception error) when (error is IOException || error is InvalidOperationException
                || error is System.ComponentModel.Win32Exception)
            {
                Debug.Log($"Updater launch failed: {error.Message}");
                AdeUiKit.SetInteractable(dialog.LeftButton, true);
                messageText.text = $"无法启动更新器，请关闭 AffdataEdit 后手动运行\n{updater}";
            }
        }

        private void FailCheck()
        {
            SetState(false, "无法检查更新，可能是网络不可用或服务器暂时无响应。\n可以重试，或先继续使用当前版本。");
        }
    }
}

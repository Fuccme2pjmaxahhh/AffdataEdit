using Arcade.Util.UniTaskHelper;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Arcade.Compose.Dialog;
using Arcade.Gameplay;
using Arcade.Util.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
	// Opens AffdataNet cloud charts as local projects and pushes chart edits back to the cloud.
	public sealed partial class AdeCloudManager : MonoBehaviour
	{
		private const string ApiUrl = "https://api.affdata.net";
		private const string EditorSceneName = "ArcEditor";
		private const string ButtonTemplatePath = "ArcaeaEditor/EditorCanvas/Bars/Left/View/Top/Folder";
		private const string RealtimePrefsKey = "AdeCloudRealtime";
		private static readonly string[] DifficultyNames = { "Past", "Present", "Future", "Beyond", "Eternal" };

		private sealed class CloudSide
		{
			[JsonProperty("enabled")] public bool Enabled;
			[JsonProperty("mode")] public string Mode;
			[JsonProperty("name")] public string Name;
			[JsonProperty("trackSkin")] public string TrackSkin;
		}

		private sealed class CloudSkin
		{
			[JsonProperty("light")] public CloudSide Light;
			[JsonProperty("dark")] public CloudSide Dark;
		}

		private sealed class CloudDiff
		{
			[JsonProperty("difficulty")] public int Difficulty;
			[JsonProperty("difficultyLabel")] public string DifficultyLabel;
			[JsonProperty("chartDifficulty")] public string ChartDifficulty;
		}

		private sealed class CloudChart
		{
			[JsonProperty("archiveId")] public int ArchiveId;
			[JsonProperty("title")] public string Title;
			[JsonProperty("artist")] public string Artist;
			[JsonProperty("bpm")] public float Bpm;
			[JsonProperty("isPrivate")] public bool IsPrivate;
			[JsonProperty("updatedAt")] public string UpdatedAt;
			[JsonProperty("diffs")] public CloudDiff[] Diffs = new CloudDiff[0];
			[JsonProperty("enabledDiffs")] public int[] EnabledDiffs = new int[0];
			[JsonProperty("rankedDiffs")] public int[] RankedDiffs = new int[0];
			[JsonProperty("skin")] public CloudSkin Skin;
			[JsonProperty("sfxArctap")] public string[] SfxArctap = new string[0];
		}

		private sealed class AppliedSkin
		{
			public Side Side = Side.Light;
			public string BackgroundUrl;
			public string TrackUrl;
		}

		public static AdeCloudManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public static string CloudRoot => AdeStorage.CloudChartsFolder;

		private bool busy;
		private List<CloudChart> charts = new List<CloudChart>();
		private AdeDualDialog chooserDialog, updateDialog;
		private AdeSingleDialog listDialog;
		private Transform listContent;
		private Text listStatus;
		private Transform labelTemplate, buttonRowTemplate;
		private readonly List<Button> openButtons = new List<Button>();
		private Image realtimeIcon;
		private bool realtime;

		private string skinProjectFolder;
		private readonly List<UnityEngine.Object> skinObjects = new List<UnityEngine.Object>();
		private readonly Dictionary<int, CloudChart> openedCharts = new Dictionary<int, CloudChart>();

		private object syncedChart;
		private string syncedKey;
		private string syncedHash;
		private bool uploading;
		private float nextSyncTime;
		private bool realtimeErrorShown;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Bootstrap()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name == EditorSceneName && Instance == null)
				new GameObject(nameof(AdeCloudManager)).AddComponent<AdeCloudManager>();
		}

		private void Awake()
		{
			Instance = this;
			realtime = AdeSettings.GetInt(RealtimePrefsKey, 0) == 1;
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
			ReleaseListCovers();
			DestroySkinObjects();
		}

		private void Start()
		{
			GameObject template = GameObject.Find(ButtonTemplatePath);
			if (template == null)
			{
				Debug.LogWarning("Cloud button template not found");
				return;
			}
			AdeUiKit.CreateIconButton(template.transform.parent, "CloudSave", "CloudUpload", () => Upload(true).Forget());
			realtimeIcon = AdeUiKit.CreateIconButton(template.transform.parent, "CloudRealtime", "Realtime", ToggleRealtime);
			UpdateRealtimeLabel();
			BuildDialogs();
			AdeUiTheme.Refresh();
		}

		private void ToggleRealtime()
		{
			realtime = !realtime;
			AdeSettings.SetInt(RealtimePrefsKey, realtime ? 1 : 0);
			AdeSettings.Save();
			UpdateRealtimeLabel();
			realtimeErrorShown = false;
			AdeToast.Instance?.Show(realtime ? "已开启实时保存到云端" : "已关闭实时保存到云端");
		}

		private void UpdateRealtimeLabel()
		{
			AdeUiTheme.SetToolbarToggleState(realtimeIcon, realtime);
		}

		public void ShowOpenChooser()
		{
			if (chooserDialog == null)
			{
				AdeProjectManager.Instance.OpenLocalProject();
				return;
			}
			chooserDialog.SwitchOpenState();
		}

		// The chooser reuses the OBS dual dialog; the chart list reuses the common message dialog.
		private void BuildDialogs()
		{
			AdeDualDialog obs = Feature.AdeObsManager.Instance != null ? Feature.AdeObsManager.Instance.OBSDialog : null;
			AdeSingleDialog basic = AdeBasicSingleDialogContent.Instance != null ? AdeBasicSingleDialogContent.Instance.Dialog : null;
			Transform inputRow = obs != null ? AdeUiKit.FindRow(obs, "Address") : null;
			buttonRowTemplate = obs != null ? AdeUiKit.FindRow(obs, "Download") : null;
			if (inputRow == null || buttonRowTemplate == null || basic == null)
			{
				Debug.LogWarning("Cloud dialog templates not found");
				return;
			}
			labelTemplate = inputRow.GetComponentInChildren<Text>(true).transform;

			chooserDialog = AdeUiKit.CloneDialog(obs, "OpenChartDialog");
			chooserDialog.Title.text = "打开谱面";
			AdeUiKit.CreateLabelRow(AdeUiKit.Content(chooserDialog), labelTemplate, "从本地文件夹或 AffdataNet 云端打开谱面");
			chooserDialog.LeftButtonText.text = "本地谱面";
			chooserDialog.RightButtonText.text = "云端谱面";
			AdeUiKit.SetOnClick(chooserDialog.LeftButton, () =>
			{
				chooserDialog.Close();
				AdeProjectManager.Instance.OpenLocalProject();
			});
			AdeUiKit.SetOnClick(chooserDialog.RightButton, () =>
			{
				chooserDialog.Close();
				if (!AdeAccountManager.IsLoggedIn)
				{
					AdeToast.Instance?.Show("请先登录 AffdataNet");
					AdeAccountManager.Instance?.ShowLoginWindow();
					return;
				}
				listDialog.Title.text = "云端谱面";
				listDialog.Open();
				LoadList().Forget();
			});

			listDialog = AdeUiKit.CloneDialog(basic, "CloudChartsDialog");
			listContent = AdeUiKit.Content(listDialog);
			listStatus = AdeUiKit.CreateLabelRow(listContent, labelTemplate, "");
			ConfigureListDialog();
			listDialog.ButtonText.text = "关闭";
			AdeUiKit.SetOnClick(listDialog.CompleteButton, () =>
			{
				if (!busy) listDialog.Close();
			});
		}

		private void SetMessage(string text)
		{
			if (listStatus == null) return;
			listStatus.text = text;
			listStatus.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
		}

		private void SetBusy(bool value)
		{
			busy = value;
			foreach (Button button in openButtons)
			{
				if (button != null) button.interactable = !value;
			}
			if (listDialog != null) listDialog.CompleteButton.interactable = !value;
			AdeUiTheme.Refresh();
		}

		private void Update()
		{
			if (listDialog && listDialog.View.activeInHierarchy) RefreshListAppearance();
			AdeProjectManager project = AdeProjectManager.Instance;
			if (skinProjectFolder != null && (project == null || project.CurrentProjectFolder != skinProjectFolder))
			{
				skinProjectFolder = null;
				DestroySkinObjects();
				AdeSkinDialogContent.Instance?.ReapplySkin();
			}
			if (realtime && !busy && !uploading && Time.unscaledTime >= nextSyncTime)
			{
				nextSyncTime = Time.unscaledTime + 1f;
				Upload(false).Forget();
			}
		}

		private static bool TryGetCurrentArchive(out int archiveId)
		{
			archiveId = 0;
			string folder = AdeProjectManager.Instance?.CurrentProjectFolder;
			if (folder == null || AdeProjectManager.Instance.CurrentProjectMetadata == null) return false;
			string parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar));
			return string.Equals(parent, Path.GetFullPath(CloudRoot), StringComparison.Ordinal)
				&& int.TryParse(Path.GetFileName(folder), out archiveId);
		}

		private static string Sha256(byte[] data)
		{
			using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data));
		}

		private async UniTask Upload(bool manual)
		{
			ArcGameplayManager gameplay = ArcGameplayManager.Instance;
			AdeProjectManager project = AdeProjectManager.Instance;
			if (!TryGetCurrentArchive(out int archiveId) || gameplay?.Chart == null || project.IsLoading)
			{
				if (manual) AdeToast.Instance?.Show("当前打开的不是云端谱面");
				return;
			}
			if (!manual && gameplay.IsPlaying) return;
			if (!AdeAccountManager.IsLoggedIn)
			{
				if (manual) AdeAccountManager.Instance?.ShowLoginWindow();
				return;
			}

			int difficulty = project.CurrentDifficulty;
			string key = $"{archiveId}:{difficulty}";
			byte[] data;
			try
			{
                using (var stream = new MemoryStream())
                {
                    gameplay.Chart.Serialize(stream, ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode, ArcadeComposeManager.Instance.ArcadePreference.LowPrecisionArcAndSlide);
                    data = stream.ToArray();
                }
			}
            catch (InvalidDataException error)
            {
                if (manual) AdeToast.Instance?.Show(error.Message);
                return;
            }
			string hash = Sha256(data);
			// A newly loaded chart becomes the baseline so loading alone never uploads.
			if (!ReferenceEquals(syncedChart, gameplay.Chart) || syncedKey != key)
			{
				syncedChart = gameplay.Chart;
				syncedKey = key;
				syncedHash = hash;
				if (!manual) return;
			}
			if (!manual && hash == syncedHash) return;
			if (openedCharts.TryGetValue(archiveId, out CloudChart chart))
			{
				if (!chart.EnabledDiffs.Contains(difficulty))
				{
					if (manual) AdeToast.Instance?.Show($"云端谱面未启用 {DifficultyNames[difficulty]} 难度，无法保存");
					return;
				}
				if (chart.RankedDiffs.Contains(difficulty))
				{
					if (manual) AdeToast.Instance?.Show($"{DifficultyNames[difficulty]} 难度已 Ranked 冻结，无法保存到云端");
					return;
				}
			}

			uploading = true;
            try
            {
				try
				{
					File.WriteAllBytes(Path.Combine(project.CurrentProjectFolder, $"{difficulty}.aff"), data);
				}
				catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
				{
					Debug.LogWarning($"Cloud chart local write failed: {error.Message}");
				}
				using (var request = new UnityWebRequest($"{ApiUrl}/v2/game/editor/charts/{archiveId}/difficulties/{difficulty}", UnityWebRequest.kHttpVerbPUT))
				{
					request.uploadHandler = new UploadHandlerRaw(data);
					request.downloadHandler = new DownloadHandlerBuffer();
					request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
					request.SetRequestHeader("Authorization", "Bearer " + AdeAccountManager.Token);
					request.timeout = 30;
					await request.SendWithResultAsync(destroyCancellationToken);
					uploading = false;
					if (request.result == UnityWebRequest.Result.Success)
					{
						if (syncedKey == key) syncedHash = hash;
						realtimeErrorShown = false;
						if (manual) AdeToast.Instance?.Show($"已保存到云端：{DifficultyNames[difficulty]}");
					}
					else
					{
						nextSyncTime = Time.unscaledTime + 5f;
						if (manual || !realtimeErrorShown)
						{
							realtimeErrorShown = !manual;
							AdeToast.Instance?.Show("保存到云端失败：" + ErrorText(request));
						}
					}
				}
            }
            finally { uploading = false; }
		}

		private static string ErrorText(UnityWebRequest request)
		{
			if (request.responseCode == 401) return "登录已失效，请重新登录";
			try
			{
				string error = (string)JObject.Parse(request.downloadHandler?.text ?? "")["error"];
				if (!string.IsNullOrEmpty(error)) return error;
			}
			catch (JsonException)
			{
			}
			return request.error;
		}

		private static UnityWebRequest Get(string url)
		{
			var request = UnityWebRequest.Get(url.StartsWith("/") ? ApiUrl + url : url);
			request.SetRequestHeader("Authorization", "Bearer " + AdeAccountManager.Token);
			request.timeout = 120;
			return request;
		}

		private async UniTask LoadList()
		{
			SetBusy(true);
            try
            {
				SetMessage("正在获取云端谱面…");
				using (UnityWebRequest request = Get("/v2/game/editor/charts"))
				{
					await request.SendWithResultAsync(destroyCancellationToken);
					SetBusy(false);
					if (request.result != UnityWebRequest.Result.Success)
					{
						SetMessage("获取失败：" + ErrorText(request));
						return;
					}
					try
					{
						charts = JObject.Parse(request.downloadHandler.text)["items"]?.ToObject<List<CloudChart>>() ?? new List<CloudChart>();
						SetMessage(charts.Count == 0 ? "还没有上传过谱面" : "");
						RebuildList();
					}
					catch (JsonException error)
					{
						SetMessage("解析失败：" + error.Message);
					}
				}
            }
            finally { if (this) SetBusy(false); }
		}

		private async UniTask Download(string url, string path, bool required, Action<bool> done)
		{
			using (UnityWebRequest request = Get(url))
			{
				await request.SendWithResultAsync(destroyCancellationToken);
				string error = null;
				if (request.result == UnityWebRequest.Result.Success)
				{
					try
					{
						File.WriteAllBytes(path, request.downloadHandler.data);
						done(true);
						return;
					}
					catch (Exception writeError) when (writeError is IOException || writeError is UnauthorizedAccessException)
					{
						error = writeError.Message;
					}
				}
				if (required) SetMessage($"下载 {Path.GetFileName(path)} 失败：{error ?? ErrorText(request)}");
				done(false);
			}
		}

		private void RequestOpenChart(CloudChart chart)
		{
			if (busy) return;
			string folder = Path.Combine(CloudRoot, chart.ArchiveId.ToString());
			if (!Directory.Exists(folder))
			{
				DownloadChart(chart).Forget();
				return;
			}
			string versionFile = Path.Combine(folder, "Arcade", "RemoteVersion.txt");
			bool complete = File.Exists(Path.Combine(folder, "base.opus"))
				&& chart.EnabledDiffs.All(d => File.Exists(Path.Combine(folder, d + ".aff")));
			try
			{
				if (complete && File.Exists(versionFile) && File.ReadAllText(versionFile) == (chart.UpdatedAt ?? ""))
				{
					OpenDownloadedChart(chart, folder).Forget();
					return;
				}
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
			{
				SetMessage("无法读取云端谱面版本：" + error.Message);
				return;
			}
			if (updateDialog == null)
			{
				updateDialog = AdeUiKit.CloneDialog(Feature.AdeObsManager.Instance.OBSDialog, "CloudUpdateChoice");
				AdeUiKit.CreateLabelRow(AdeUiKit.Content(updateDialog), labelTemplate,
					"检测到已有本地副本。可以继续编辑本地版本，或下载远端版本。\n下载成功后会将原文件夹完整保留在 Charts/Cloud/.backups 中。\n请先保存当前尚未保存的修改。");
			}
			updateDialog.Title.text = "云端谱面版本";
			updateDialog.LeftButtonText.text = "打开本地副本";
			updateDialog.RightButtonText.text = "下载远端版本";
			AdeUiKit.SetOnClick(updateDialog.LeftButton, () =>
			{
				updateDialog.Close();
				OpenDownloadedChart(chart, folder).Forget();
			});
			AdeUiKit.SetOnClick(updateDialog.RightButton, () =>
			{
				updateDialog.Close();
				DownloadChart(chart).Forget();
			});
			updateDialog.Open();
		}

		private async UniTask OpenDownloadedChart(CloudChart chart, string folder)
		{
			SetBusy(true);
			try
			{
				openedCharts[chart.ArchiveId] = chart;
				while (AdeProjectManager.Instance.IsLoading) await UniTask.NextFrame(destroyCancellationToken);
				AdeProjectManager.Instance.OpenProjectFolder(folder);
				while (AdeProjectManager.Instance.IsLoading) await UniTask.NextFrame(destroyCancellationToken);
				string background = Path.Combine(folder, "Arcade", "CloudBackground.jpg");
				string track = Path.Combine(folder, "Arcade", "CloudTrack.png");
				ApplySkin(folder, ResolveSkin(chart).Side, File.Exists(background) ? background : null,
					File.Exists(track) ? track : null);
				listDialog.Close();
				AdeToast.Instance?.Show("已打开云端谱面的本地副本：" + chart.Title);
			}
			finally { if (this) SetBusy(false); }
		}

		private async UniTask DownloadChart(CloudChart chart)
		{
			SetBusy(true);
			string target = Path.Combine(CloudRoot, chart.ArchiveId.ToString());
			string staging = Path.Combine(CloudRoot, ".download-" + Guid.NewGuid().ToString("N"));
			string folder = staging;
			string arcadeFolder = Path.Combine(folder, "Arcade");
			try { Directory.CreateDirectory(arcadeFolder); }
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
			{
				SetBusy(false);
				SetMessage("无法创建下载目录：" + error.Message);
				return;
			}
			try
			{
				string files = $"/v2/game/editor/charts/{chart.ArchiveId}/files/";
				bool ok = true;

				SetMessage("正在下载音乐…");
				await Download(files + "base.opus", Path.Combine(folder, "base.opus"), true, result => ok = result);
				if (!ok) { SetBusy(false); return; }
				SetMessage("正在下载封面…");
				await Download(files + "base.jpg", Path.Combine(folder, "base.jpg"), false, _ => { });
				foreach (int difficulty in chart.EnabledDiffs)
				{
					SetMessage($"正在下载 {DifficultyNames[difficulty]} 谱面…");
					await Download(files + $"{difficulty}.aff", Path.Combine(folder, $"{difficulty}.aff"), true, result => ok = result);
					if (!ok) { SetBusy(false); return; }
				}
				foreach (string sfx in chart.SfxArctap ?? new string[0])
				{
					SetMessage($"正在下载音效 {sfx}…");
					await Download(files + Uri.EscapeDataString(sfx), Path.Combine(folder, Path.GetFileName(sfx)), false, _ => { });
				}

				AppliedSkin skin = ResolveSkin(chart);
				if (skin.TrackUrl == null && skin.BackgroundUrl != null && skin.BackgroundUrl.StartsWith("/v2/game/backgrounds/", StringComparison.Ordinal))
				{
					SetMessage("正在获取皮肤信息…");
					await ResolvePresetTrack(skin);
				}
				string backgroundPath = Path.Combine(arcadeFolder, "CloudBackground.jpg");
				string trackPath = Path.Combine(arcadeFolder, "CloudTrack.png");
				bool hasBackground = false, hasTrack = false;
				if (skin.BackgroundUrl != null)
				{
					SetMessage("正在下载背景…");
					await Download(skin.BackgroundUrl, backgroundPath, false, result => hasBackground = result);
				}
				if (skin.TrackUrl != null)
				{
					SetMessage("正在下载轨道…");
					await Download(skin.TrackUrl, trackPath, false, result => hasTrack = result);
				}
				try
				{
					WriteMetadata(folder, chart);
					File.WriteAllText(Path.Combine(arcadeFolder, "RemoteVersion.txt"), chart.UpdatedAt ?? "");
					AdeStorageMigration.InstallDownload(folder, target);
				}
				catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
				{
					SetMessage("无法保存下载的谱面：" + error.Message);
					return;
				}
				folder = target;
				arcadeFolder = Path.Combine(folder, "Arcade");
				backgroundPath = Path.Combine(arcadeFolder, "CloudBackground.jpg");
				trackPath = Path.Combine(arcadeFolder, "CloudTrack.png");
				openedCharts[chart.ArchiveId] = chart;
				while (AdeProjectManager.Instance.IsLoading) await UniTask.NextFrame(destroyCancellationToken);
				AdeProjectManager.Instance.OpenProjectFolder(folder);
				while (AdeProjectManager.Instance.IsLoading) await UniTask.NextFrame(destroyCancellationToken);
				ApplySkin(folder, skin.Side, hasBackground ? backgroundPath : null, hasTrack ? trackPath : null);
				SetBusy(false);
				SetMessage("");
				listDialog.Close();
				AdeToast.Instance?.Show($"已打开云端谱面：{chart.Title}");
			}
			finally
			{
				if (this) SetBusy(false);
				if (Directory.Exists(staging))
				{
					try { Directory.Delete(staging, true); }
					catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
					{
						Debug.LogError("无法清理未完成的谱面下载：" + error.Message);
					}
				}
			}
		}

		// Mirrors AffdataPlay: the light side when enabled, otherwise the dark (conflict) side.
		private static AppliedSkin ResolveSkin(CloudChart chart)
		{
			var skin = new AppliedSkin();
			CloudSide side = chart.Skin?.Light;
			string sideName = "light";
			if (side == null || !side.Enabled)
			{
				side = chart.Skin?.Dark;
				sideName = "dark";
			}
			if (side == null || !side.Enabled) return skin;
			skin.Side = sideName == "dark" ? Side.Conflict : Side.Light;
			if (string.Equals(side.Mode, "custom", StringComparison.OrdinalIgnoreCase))
			{
				skin.BackgroundUrl = $"/v2/game/editor/charts/{chart.ArchiveId}/files/bg_{sideName}.jpg";
				if (!string.IsNullOrWhiteSpace(side.TrackSkin)) skin.TrackUrl = $"/v2/game/track-skins/{side.TrackSkin.Trim()}.png";
			}
			else if (!string.IsNullOrWhiteSpace(side.Name))
			{
				skin.BackgroundUrl = "/v2/game/backgrounds/" + Uri.EscapeDataString(side.Name.Trim());
			}
			return skin;
		}

		private async UniTask ResolvePresetTrack(AppliedSkin skin)
		{
			string background = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(skin.BackgroundUrl.Substring("/v2/game/backgrounds/".Length)));
			using (UnityWebRequest request = Get("/v2/game/skin"))
			{
				await request.SendWithResultAsync(destroyCancellationToken);
				if (request.result != UnityWebRequest.Result.Success) return;
				try
				{
					string track = (string)JObject.Parse(request.downloadHandler.text)[background]?["track"];
					if (!string.IsNullOrWhiteSpace(track)) skin.TrackUrl = $"/v2/game/track-skins/{track.Trim()}.png";
				}
				catch (Exception error) when (error is JsonException || error is InvalidCastException)
				{
					Debug.LogWarning($"Cloud skin metadata ignored: {error.Message}");
				}
			}
		}

		private static void WriteMetadata(string folder, CloudChart chart)
		{
			string path = Path.Combine(folder, "Arcade", "Project.arcade");
			ArcadeProjectMetadata metadata = null;
			try
			{
				if (File.Exists(path)) metadata = JsonConvert.DeserializeObject<ArcadeProjectMetadata>(File.ReadAllText(path));
			}
			catch (JsonException)
			{
			}
			metadata = metadata ?? new ArcadeProjectMetadata();
			if (metadata.Difficulties == null || metadata.Difficulties.Length < AdeProjectManager.DIFFICULTY_COUNT)
			{
				var difficulties = new AdeChartDifficultyMetadata[AdeProjectManager.DIFFICULTY_COUNT];
				metadata.Difficulties?.CopyTo(difficulties, 0);
				metadata.Difficulties = difficulties;
			}
			metadata.Title = chart.Title;
			metadata.Artist = chart.Artist;
			metadata.BaseBpm = chart.Bpm;
			foreach (CloudDiff diff in chart.Diffs ?? new CloudDiff[0])
			{
				if (diff.Difficulty < 0 || diff.Difficulty >= AdeProjectManager.DIFFICULTY_COUNT) continue;
				var difficulty = metadata.Difficulties[diff.Difficulty] ?? new AdeChartDifficultyMetadata();
				difficulty.Rating = diff.ChartDifficulty;
				metadata.Difficulties[diff.Difficulty] = difficulty;
			}
			if (!chart.EnabledDiffs.Contains(metadata.LastWorkingDifficulty) && chart.EnabledDiffs.Length > 0)
				metadata.LastWorkingDifficulty = chart.EnabledDiffs.Contains(2) ? 2 : chart.EnabledDiffs[0];
			File.WriteAllText(path, JsonConvert.SerializeObject(metadata));
		}

		private void ApplySkin(string folder, Side side, string backgroundPath, string trackPath)
		{
			DestroySkinObjects();
			Sprite background = null, track = null;
			if (backgroundPath != null)
			{
				Texture2D texture = Loader.LoadTexture2D(backgroundPath);
				if (texture != null)
				{
					skinObjects.Add(texture);
					background = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
					skinObjects.Add(background);
				}
			}
			ArcSkinManager skinManager = ArcSkinManager.Instance;
			if (trackPath != null && skinManager.TrackComponents.Length > 0)
			{
				Texture2D texture = Loader.LoadTexture2D(trackPath);
				if (texture != null)
				{
					skinObjects.Add(texture);
					// Match the current track sprite's world size and pivot so the lane geometry is unchanged.
					Sprite current = skinManager.TrackComponents[0].sprite;
					float pixelsPerUnit = current != null ? texture.width / (current.rect.width / current.pixelsPerUnit) : 100f;
					Vector2 pivot = current != null ? new Vector2(current.pivot.x / current.rect.width, current.pivot.y / current.rect.height) : new Vector2(0.5f, 0.5f);
					track = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
					skinObjects.Add(track);
				}
			}
			AdeSkinDialogContent.Instance?.ApplyCloudSkin(side, background, track);
			skinProjectFolder = folder;
		}

		private void DestroySkinObjects()
		{
			foreach (UnityEngine.Object item in skinObjects)
			{
				if (item != null) Destroy(item);
			}
			skinObjects.Clear();
		}
	}
}

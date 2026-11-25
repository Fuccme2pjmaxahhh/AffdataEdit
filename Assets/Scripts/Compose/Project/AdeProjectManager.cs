using Cysharp.Threading.Tasks;
using Arcade.Audio;
using System;
using System.IO;
using System.Linq;
using Arcade.Compose.Dialog;
using Arcade.Compose.Feature;
using Arcade.Compose.UI;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Arcade.Compose.Command;
using Arcade.Util.Loader;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Arcade.Compose
{
	[Serializable]
	public class AdeChartDifficultyMetadata
	{
		public string Rating;
		public float BaseBpm;
	}
	[Serializable]
	public class ArcadeProjectMetadata
	{
		public string Title;
		public string Artist;
		public float BaseBpm;
		public AdeChartDifficultyMetadata[] Difficulties = new AdeChartDifficultyMetadata[AdeProjectManager.DIFFICULTY_COUNT];

		public int LastWorkingDifficulty = 2;
		public int LastWorkingTiming;

		internal void WriteTo(string path) => WriteJson(path, JsonConvert.SerializeObject(this));

		private static void WriteJson(string path, string json)
		{
			string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temporary, json);
				if (File.Exists(path)) File.Replace(temporary, path, null);
				else File.Move(temporary, path);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		internal void WriteWorkingPosition(string path, int difficulty, int timing)
		{
			// Song details are saved when edited; moving the cursor only owns these two fields.
			var snapshot = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : JObject.FromObject(this);
			snapshot[nameof(LastWorkingDifficulty)] = difficulty;
			snapshot[nameof(LastWorkingTiming)] = timing;
			WriteJson(path, snapshot.ToString(Formatting.None));
			LastWorkingDifficulty = difficulty;
			LastWorkingTiming = timing;
		}
	}

	public class AdeProjectManager : MonoBehaviour
	{
		public const int DIFFICULTY_COUNT = 5;
		public static AdeProjectManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public string CurrentProjectFolder { get; set; }
		public ArcadeProjectMetadata CurrentProjectMetadata { get; set; }
		public int CurrentDifficulty { get; set; } = 2;

		public Sprite DefaultCover;
		public Image CoverImage;
		public Image[] DifficultyImages;
        private Material difficultyNormal, difficultySelected, difficultyBeyondNormal;
        private Image difficultyCut;
        private Texture2D difficultyCutTexture;
        private Sprite difficultyCutSprite;
        // Main colours of the Past..Eternal difficulty artwork.
        private static readonly Color32[] DifficultyCutHues =
        {
            new Color32(6, 155, 186, 255), new Color32(166, 196, 124, 255), new Color32(144, 48, 116, 255),
            new Color32(98, 25, 43, 255), new Color32(102, 76, 128, 255),
        };
		public InputField Name, Composer, Diff, BaseBpm, AudioOffset;
		public Button CurrentTimingGroup;
		public Text OpenLabel;
		public Text SaveMode;

		public Image FileWatchEnableImage;

		public CanvasGroup TutorialCanvasGroup;
		public Text TutorialText;

		[System.NonSerialized] private BassClip chartAudio;
		private Texture2D Cover;
		private Sprite CoverSprite;
		private long coverSize, coverModifiedTicks;

		private bool loading;
		private FileSystemWatcher watcher = new FileSystemWatcher();
        private readonly object chartFileLock = new object();
        private string savedChartPath, pendingReloadPath;
        private byte[] savedChartBytes;
		private bool audioOverrided = false;

		public string ProjectArcadeFolder
		{
			get
			{
				return Path.Combine(CurrentProjectFolder, "Arcade");
			}
		}

		public string ProjectMetadataFilePath
		{
			get
			{
				return Path.Combine(ProjectArcadeFolder, "Project.arcade");
			}
		}
		public string ProjectAutosaveFolder
		{
			get
			{
				return Path.Combine(ProjectArcadeFolder, "Autosave");
			}
		}
		public string ProjectBackupFolder
		{
			get
			{
				return Path.Combine(ProjectArcadeFolder, "Backup");
			}
		}

		private void Awake()
		{
			Instance = this;
			ConfigureHudInputNavigation(Name, Composer, Diff);
			CreateDifficultyCut();
		}

        private void CreateDifficultyCut()
        {
            // AA marks the editable rating with a slanted, difficulty-coloured cut in the strip.
            // Its 30-unit slant matches the difficulty buttons' angle and is kept unstretched.
            const int width = 64, height = 60, slant = 29;
            difficultyCutTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (x + (sx + .5f) / 4 >= slant * (y + (sy + .5f) / 4) / height) covered++;
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(covered * 255 / 16));
                }
            difficultyCutTexture.SetPixels32(pixels);
            difficultyCutTexture.Apply(false, true);
            // 200 px per unit: the 60 px texture is the 30-unit strip height.
            difficultyCutSprite = Sprite.Create(difficultyCutTexture, new Rect(0, 0, width, height), Vector2.one, 200,
                0, SpriteMeshType.FullRect, new Vector4(slant + 1, 0, 0, 0));
            var cut = new GameObject("DifficultyCut", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)cut.transform;
            rect.SetParent(Diff.transform.parent, false);
            rect.SetSiblingIndex(Diff.transform.GetSiblingIndex());
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            // Spans the strip from just after the Eternal button to the panel edge.
            rect.sizeDelta = new Vector2(126, 30);
            rect.anchoredPosition = new Vector2(0, -91);
            difficultyCut = cut.GetComponent<Image>();
            difficultyCut.sprite = difficultyCutSprite;
            difficultyCut.type = Image.Type.Sliced;
            difficultyCut.raycastTarget = false;
            difficultyCut.enabled = false;
        }

        private static readonly string[] DifficultyNames = { "Past", "Present", "Future", "Beyond", "Eternal" };
        internal static string DefaultRating(int difficulty) =>
            difficulty >= 0 && difficulty < DifficultyNames.Length ? DifficultyNames[difficulty] + " ?" : "";

        private static Color DifficultyCutColor(Color hue)
        {
            // DifficultyPalette-style RGB scaling, darker so the white rating text stays readable.
            float luminance = hue.r * .2126f + hue.g * .7152f + hue.b * .0722f;
            float peak = Mathf.Max(hue.r, Mathf.Max(hue.g, hue.b));
            float gain = Mathf.Min(.13f / Mathf.Max(.0001f, luminance), .95f / Mathf.Max(.0001f, peak));
            return new Color(hue.r * gain, hue.g * gain, hue.b * gain, .92f);
        }

        internal static void ConfigureHudInputNavigation(params InputField[] fields)
        {
            // Chart arrow-key scrolling must not navigate into metadata and start typing there.
            foreach (var field in fields)
                if (field) field.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        private void RefreshDifficultyAppearance(int selected)
        {
            if (!difficultyNormal)
            {
                var shader = Resources.Load<Shader>("AffdataEdit/Theme/DifficultyPalette");
                difficultyNormal = new Material(shader); difficultyNormal.SetFloat("_Brightness", .29f);
                difficultySelected = new Material(shader); difficultySelected.SetFloat("_Brightness", .32f);
                difficultyBeyondNormal = new Material(shader); difficultyBeyondNormal.SetFloat("_Brightness", .18f);
            }
            // Keep the approved Past/Present/Beyond levels; lift Future and Eternal independently.
            float selectedBrightness = selected == 0 ? .53f : selected == 1 ? .78f : selected == 2 ? .42f : selected == 4 ? .53f : .32f;
            difficultySelected.SetFloat("_Brightness", selectedBrightness);
            for (int i = 0; i < DifficultyImages.Length; i++)
            {
                var image = DifficultyImages[i];
                image.material = i == selected ? difficultySelected : i == 3 ? difficultyBeyondNormal : difficultyNormal;
                image.color = new Color(1, 1, 1, i == selected ? 1 : .65f);
            }
            if (difficultyCut)
            {
                difficultyCut.enabled = selected >= 0 && selected < DifficultyCutHues.Length;
                if (difficultyCut.enabled) difficultyCut.color = DifficultyCutColor(DifficultyCutHues[selected]);
            }
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnWatcherChanged;
            watcher.Created -= OnWatcherChanged;
            watcher.Renamed -= OnWatcherChanged;
            watcher.Dispose();
            if (difficultyNormal) Destroy(difficultyNormal);
            if (difficultySelected) Destroy(difficultySelected);
            if (difficultyBeyondNormal) Destroy(difficultyBeyondNormal);
            if (difficultyCutSprite) Destroy(difficultyCutSprite);
            if (difficultyCutTexture) Destroy(difficultyCutTexture);
        }

		private void Start()
		{
			if (!Application.isEditor) Application.wantsToQuit += OnWantsToQuit;
            gameObject.AddComponent<AdeSongListExport>();
            RefreshDifficultyAppearance(-1);
			watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
			watcher.Changed += OnWatcherChanged;
			watcher.Created += OnWatcherChanged;
			watcher.Renamed += OnWatcherChanged;
			watcher.EnableRaisingEvents = false;
			AutosaveAsync().Forget();
		}
		private void Update()
		{
			FinishSaves(false);
            if (!loading)
            {
                string path = System.Threading.Interlocked.Exchange(ref pendingReloadPath, null);
                if (path != null && watcher.EnableRaisingEvents && CurrentProjectFolder != null
                    && string.Equals(path, Path.GetFullPath(Path.Combine(CurrentProjectFolder, $"{CurrentDifficulty}.aff")), StringComparison.OrdinalIgnoreCase))
                {
                    ReloadChart();
                }
            }
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Save))
			{
				SaveProject();
			}
			RefreshSaveState();
		}

		// Unsaved state: "●" at the start of the window title (still visible when the taskbar
		// truncates it, unlike Arcade-Alpha's trailing "*(Unsaved)") and a dot on the Save button.
		private bool HasUnsavedChart => CurrentProjectMetadata != null && ArcGameplayManager.Instance.Chart != null
			&& AdeCommandManager.Instance.HasUnsavedChanges;
		private bool? shownUnsaved;
		private bool titleUnsaved, titleSet;
		private string titleSong;
		private int titleDifficulty = int.MinValue;
		private void RefreshSaveState()
		{
			bool unsaved = HasUnsavedChart;
			if (shownUnsaved != unsaved && AdeToast.Instance && AdeToast.Instance.SetUnsaved(unsaved)) shownUnsaved = unsaved;
			bool hasChart = CurrentProjectMetadata != null && ArcGameplayManager.Instance.Chart != null;
			string song = hasChart ? CurrentProjectMetadata.Title : null;
			int difficulty = hasChart ? CurrentDifficulty : -1;
			if (titleSet && titleUnsaved == unsaved && titleSong == song && titleDifficulty == difficulty) return;
			string title = Application.productName;
			if (hasChart)
			{
				string name = string.IsNullOrWhiteSpace(song)
					? Path.GetFileName((CurrentProjectFolder ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : song;
				string diff = difficulty >= 0 && difficulty < DifficultyNames.Length
					? DifficultyNames[difficulty] : difficulty.ToString(CultureInfo.InvariantCulture);
				title = (unsaved ? "● " : "") + name + " [" + diff + "] — " + title;
			}
			titleSet = AdeWindowState.SetTitle(title);
			titleUnsaved = unsaved;
			titleSong = song;
			titleDifficulty = difficulty;
		}

		private AdeDualDialog unsavedDialog;
		private Text unsavedHint;
		private async UniTask<bool> ConfirmTransitionAsync(string action)
		{
			if (!HasUnsavedChart) return true;
			if (!unsavedDialog)
			{
				unsavedDialog = AdeUiKit.CloneDialog(AdeObsManager.Instance.OBSDialog, "UnsavedChartDialog");
				unsavedDialog.Title.text = "谱面未保存";
				unsavedHint = AdeUiKit.CreateLabelRow(AdeUiKit.Content(unsavedDialog),
					AdeBasicSingleDialogContent.Instance.Content.transform, "");
			}
			bool externalReload = action == null;
			unsavedDialog.LeftButtonText.text = externalReload ? "重新加载" : "保存并继续";
			unsavedDialog.RightButtonText.text = externalReload ? "保留我的修改" : "不保存";
			unsavedHint.text = externalReload
				? "谱面文件已在外部被修改，但这里还有未保存的修改。\n重新加载会放弃这里的修改；保留则下次保存时覆盖外部修改。"
				: $"当前谱面有未保存的修改。\n{action}前要先保存吗？点右上角 × 取消。";
			unsavedHint.color = AdeUiTheme.Current.Text;

			var closed = new UniTaskCompletionSource();
			bool? left = null;
			void Choose(bool value) { left = value; unsavedDialog.Close(); }
			void OnClosed() => closed.TrySetResult();
			AdeUiKit.SetOnClick(unsavedDialog.LeftButton, () => Choose(true));
			AdeUiKit.SetOnClick(unsavedDialog.RightButton, () => Choose(false));
			unsavedDialog.OnClose += OnClosed;
			try
			{
				unsavedDialog.Open();
				await closed.Task.AttachExternalCancellation(destroyCancellationToken);
				if (externalReload) return left == true;
				return left.HasValue && (!left.Value || TrySaveProject());
			}
			finally { unsavedDialog.OnClose -= OnClosed; }
		}

		public void CleanProject()
		{
			FinishSaves(true);
			AdeOperationManager.Instance.CancelOngoingOperation();
			ArcGameplayManager.Instance.Clean();
			SlowAudioCache.BeginSong(null);
			if (chartAudio != null)
			{
				Destroy(chartAudio);
				chartAudio = null;
			}
			if (Cover != null)
			{
				Destroy(Cover);
				Cover = null;
			}
			if (CoverSprite != null)
			{
				Destroy(CoverSprite);
				CoverSprite = null;
			}
			RefreshDifficultyAppearance(-1);
			CoverImage.sprite = DefaultCover;
			Name.text = "";
			Composer.text = "";
			Diff.text = "";
			Name.interactable = false;
			Composer.interactable = false;
			Diff.interactable = false;
			AdeTimingSlider.Instance.Enable = false;
			OpenLabel.gameObject.SetActive(true);
			BaseBpm.interactable = false;
			AudioOffset.interactable = false;
			CurrentTimingGroup.interactable = false;
			watcher.EnableRaisingEvents = false;
			AdeUiTheme.SetToolbarToggleState(FileWatchEnableImage, false);
			ArcEffectManager.Instance.CleanSpecialEffectAudios();
		}
		public bool IsLoading => loading;
		internal string LoadedProjectFolder { get; private set; }

		public void OpenProject()
		{
			if (loading)
			{
				return;
			}
			if (AdeCloudManager.Instance != null)
			{
				AdeCloudManager.Instance.ShowOpenChooser();
				return;
			}
			OpenLocalProject();
		}
		public void OpenLocalProject()
		{
			if (loading)
			{
				return;
			}
			try
			{
				string folder = Util.Shell.FileBrowser.OpenFolderDialog("选择您的 Arcaea 自制谱文件夹 (包含 0/1/2.aff, base.mp3/ogg/wav, base.jpg)");
				if (folder == null) return;
				OpenProjectFolderAsync(folder).Forget();
			}
			catch (Exception Ex)
			{
				AdeBasicSingleDialogContent.Instance.Show(Ex.Message, "读取错误");
				Debug.Log(Ex);
			}
		}
		public UniTask<bool> OpenProjectFolderAsync(string folder)
			=> RunLoadingAsync("打开其他谱面", () => LoadProject(folder, null, true));

		internal UniTask<bool> OpenCloudProjectAsync(string folder, string staging)
			=> RunLoadingAsync("打开其他谱面", () => LoadProject(folder, staging, false));

		private sealed class ProjectSave
		{
			public Action Write;
			public Task Worker;
			public AdeCommandManager.SavePoint Point;
			public int Difficulty, Timing;
		}
		private ProjectSave activeSave, queuedSave;

		public void SaveProject() => QueueSave();
		private bool QueueSave()
		{
			var chart = ArcGameplayManager.Instance.Chart;
			if (CurrentProjectMetadata == null || CurrentProjectFolder == null || chart == null) return false;
			try
			{
				var preference = ArcadeComposeManager.Instance.ArcadePreference;
				var serialize = chart.CaptureSerializer(preference.ChartSortMode, preference.LowPrecisionArcAndSlide);
				int difficulty = CurrentDifficulty;
				string path = Path.GetFullPath(Path.Combine(CurrentProjectFolder, $"{difficulty}.aff"));
				string backupFolder = ProjectBackupFolder;
				var request = new ProjectSave
				{
					Point = AdeCommandManager.Instance.CaptureSavePoint(),
					Difficulty = difficulty, Timing = ArcGameplayManager.Instance.AudioTiming,
					Write = () => WriteSavedChart(serialize, path, backupFolder, difficulty)
				};
				if (activeSave == null) StartSave(request);
				else
				{
					if (queuedSave != null) AdeCommandManager.Instance.FinishSave(queuedSave.Point, false);
					queuedSave = request;
				}
				return true;
			}
			catch (Exception ex)
			{
				ShowSaveError(ex);
				return false;
			}
		}

		private void StartSave(ProjectSave request)
		{
			request.Worker = Task.Run(request.Write);
			request.Write = null;
			activeSave = request;
		}

		private void WriteSavedChart(Action<Stream> serialize, string path, string backupFolder, int difficulty)
		{
			byte[] bytes;
			using (var buffer = new MemoryStream())
			{
				serialize(buffer);
				bytes = buffer.ToArray();
			}
			WriteChartBytes(bytes, path, backupFolder, difficulty);
		}

		internal void WriteCloudCache(byte[] bytes)
		{
			FinishSaves(true);
			string path = Path.GetFullPath(Path.Combine(CurrentProjectFolder, $"{CurrentDifficulty}.aff"));
			WriteChartBytes(bytes, path, null, CurrentDifficulty);
		}

		private void WriteChartBytes(byte[] bytes, string path, string backupFolder, int difficulty)
		{
			string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllBytes(temporary, bytes);
				lock (chartFileLock)
				{
					if (File.Exists(path))
					{
						string backup = backupFolder == null ? null : NewSnapshotPath(backupFolder, difficulty);
						try { File.Replace(temporary, path, backup); }
						catch (IOException) when (!File.Exists(path) && File.Exists(backup))
						{
							File.Copy(backup, path);
							throw;
						}
					}
					else File.Move(temporary, path);
					savedChartPath = path;
					savedChartBytes = bytes;
					System.Threading.Interlocked.Exchange(ref pendingReloadPath, null);
				}
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		// Resolve work in request order. Only explicit transitions and shutdown need to wait.
		private bool FinishSaves(bool wait)
		{
			bool saved = false;
			while (activeSave != null && (wait || activeSave.Worker.IsCompleted))
			{
				var request = activeSave;
				saved = false;
				try
				{
					request.Worker.GetAwaiter().GetResult();
					CurrentProjectMetadata.WriteWorkingPosition(ProjectMetadataFilePath, request.Difficulty, request.Timing);
					saved = true;
					if (queuedSave == null) AdeToast.Instance.ShowSaved("谱面已保存");
				}
				catch (Exception ex) { ShowSaveError(ex); }
				finally { AdeCommandManager.Instance.FinishSave(request.Point, saved); }
				activeSave = null;
				if (queuedSave != null)
				{
					StartSave(queuedSave);
					queuedSave = null;
				}
			}
			return saved;
		}
		private bool TrySaveProject() => QueueSave() && FinishSaves(true);

		private static void ShowSaveError(Exception ex)
			=> AdeBasicSingleDialogContent.Instance.Show(ex.Message + "\n" + ex, "保存错误");

		private static string NewSnapshotPath(string folder, int difficulty)
		{
			Directory.CreateDirectory(folder);
			// Repeated saves within one second intentionally replace that snapshot.
			return Path.Combine(folder, $"{difficulty}_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.aff");
		}

		private bool autosaving;
		private async UniTask AutosaveAsync()
		{
			while (true)
			{
				await UniTask.WaitForSeconds(30f, cancellationToken: destroyCancellationToken);
				if (CurrentProjectMetadata == null || CurrentProjectFolder == null) continue;
				if (ArcGameplayManager.Instance.Chart == null) continue;
				if (loading || ArcGameplayManager.Instance.IsPlaying) continue;
				autosaving = true;
				try
				{
					var preference = ArcadeComposeManager.Instance.ArcadePreference;
					var serialize = ArcGameplayManager.Instance.Chart.CaptureSerializer(preference.ChartSortMode, preference.LowPrecisionArcAndSlide);
					string folder = ProjectAutosaveFolder;
					int difficulty = CurrentDifficulty;
					await Task.Run(() =>
					{
						string path = NewSnapshotPath(folder, difficulty);
						string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
						try
						{
							using (var output = File.Create(temporary)) serialize(output);
							if (File.Exists(path)) File.Replace(temporary, path, null);
							else File.Move(temporary, path);
						}
						finally { if (File.Exists(temporary)) File.Delete(temporary); }
					});
				}
				catch (Exception Ex)
				{
					AdeBasicSingleDialogContent.Instance.Show(Ex.Message + "\n" + Ex.ToString(), "自动保存错误");
				}
				finally { autosaving = false; }
			}
		}

		private async UniTask<bool> RunLoadingAsync(string action, Func<bool> load)
		{
			if (loading) return false;
			loading = true;
			try
			{
				FinishSaves(true);
				if (!await ConfirmTransitionAsync(action)) return false;
				await AdeShutterManager.Instance.CloseAsync(destroyCancellationToken);
				try
				{
					if (autosaving) await UniTask.WaitUntil(() => !autosaving, cancellationToken: destroyCancellationToken);
					FinishSaves(true);
					return load();
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
				{
					Debug.Log(ex);
					AdeBasicSingleDialogContent.Instance.Show(ex.Message, "加载失败");
					return false;
				}
				finally { await AdeShutterManager.Instance.OpenAsync(destroyCancellationToken); }
			}
			finally { loading = false; }
		}

		public void ReloadChart()
		{
			if (CurrentProjectMetadata != null)
				RunLoadingAsync(null, () => LoadDifficulty(CurrentProjectFolder, CurrentDifficulty, false, null,
					ArcGameplayManager.Instance.AudioTiming)).Forget();
		}
		public void SwitchDifficulty(int difficulty)
		{
			if (CurrentProjectMetadata != null)
				RunLoadingAsync("切换难度", () => LoadDifficulty(difficulty)).Forget();
		}

		public void SetDefaultCover(Sprite cover)
		{
			if (CoverImage.sprite == DefaultCover)
			{
				CoverImage.sprite = cover;
			}
			DefaultCover = cover;
		}
		private bool LoadDifficulty(int difficulty)
			=> LoadDifficulty(CurrentProjectFolder, difficulty, true, null);

		private bool LoadDifficulty(string source, int difficulty, bool createMissingChart, Action<BassClip> beginProject, int? resumeTiming = null)
		{
			if (difficulty < 0 || difficulty >= DIFFICULTY_COUNT)
				throw new InvalidDataException($"无效的谱面难度：{difficulty}");
			BassClip audio = FindAudio(source, difficulty, beginProject == null, out bool useDifficultyAudio);
			if (!audio)
			{
				const string message = "无法加载音频文件，请确认音频文件存在且格式正确后重新打开谱面文件夹";
				if (ArcGameplayManager.Instance.IsLoaded) AdeBasicSingleDialogContent.Instance.Show(message, "加载失败");
				else SetTutorialMessage(message);
				return false;
			}
			try
			{
				var chart = ReadChart(source, difficulty, createMissingChart);
				if (chart == null)
				{
					if (!ArcGameplayManager.Instance.IsLoaded) SetTutorialMessage("无法加载谱面文件，请修正谱面文件格式后重新打开谱面文件夹");
					return false;
				}
				beginProject?.Invoke(audio);
				if (!ApplyChart(difficulty, chart, audio, useDifficultyAudio, resumeTiming ?? CurrentProjectMetadata.LastWorkingTiming)) return false;
			}
			finally { if (audio != chartAudio) Destroy(audio); }
			LoadCover(difficulty);
			LoadSpecialEffectAudio();
			LoadedProjectFolder = CurrentProjectFolder;
			SetTutorialMessage(null);
			return true;
		}

		private bool LoadProject(string folder, string staging, bool createMissingChart)
		{
			string source = staging ?? folder;
			ArcadeProjectMetadata metadata;
			try
			{
				metadata = ReadMetadata(source);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException)
			{
				AdeBasicSingleDialogContent.Instance.Show(ex.Message, "元信息读取错误");
				return false;
			}

			// With no open chart, a local folder can still be edited or switched to another difficulty.
			if (createMissingChart && !ArcGameplayManager.Instance.IsLoaded)
			{
				BeginProject(folder, metadata);
				return LoadDifficulty(metadata.LastWorkingDifficulty);
			}
			return LoadDifficulty(source, metadata.LastWorkingDifficulty, createMissingChart, audio =>
			{
				if (staging != null)
				{
					InstallCloudDownload(staging, folder);
					audio.SetMovedSourceFolder(folder);
				}
				BeginProject(folder, metadata);
			});
		}

		private void InstallCloudDownload(string staging, string folder)
		{
			var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			if (CurrentProjectFolder == null || !string.Equals(Path.GetFullPath(CurrentProjectFolder).TrimEnd(Path.DirectorySeparatorChar),
				Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), comparison))
			{
				AdeStorageMigration.InstallDownload(staging, folder);
				return;
			}

			// Windows cannot move the active directory while the watcher or audio readers hold it.
			bool watching = watcher.EnableRaisingEvents;
			var gameplay = ArcGameplayManager.Instance;
			int timing = gameplay.AudioTiming;
			bool playing = gameplay.IsPlaying;
			watcher.EnableRaisingEvents = false;
			gameplay.Pause(false);
			FindAnyObjectByType<AdeAudioSpectrum>()?.Release();
			ArcAudioManager.Instance.ReleaseVoice();
			SlowAudioCache.Shutdown();
			try { AdeStorageMigration.InstallDownload(staging, folder); }
			catch
			{
				watcher.EnableRaisingEvents = watching;
				if (chartAudio)
				{
					ArcAudioManager.Instance.Load(chartAudio);
					gameplay.AudioTiming = timing;
					if (playing) gameplay.Play();
				}
				throw;
			}
		}

		public static float ResolveBaseBpm(float configuredBpm, IEnumerable<ArcTiming> timings)
		{
			if (configuredBpm > 0 && float.IsFinite(configuredBpm)) return configuredBpm;
			// Match Alpha when the project has no explicit speed reference.
			return timings?.Where(t => t.Bpm > 0 && float.IsFinite(t.Bpm)).OrderBy(t => t.Timing)
				.FirstOrDefault()?.Bpm ?? 100;
		}

		private static ArcadeProjectMetadata ReadMetadata(string folder)
		{
			if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"找不到谱面文件夹：{folder}");
			string arcade = Path.Combine(folder, "Arcade");
			Directory.CreateDirectory(Path.Combine(arcade, "Autosave"));
			Directory.CreateDirectory(Path.Combine(arcade, "Backup"));
			string path = Path.Combine(arcade, "Project.arcade");
			ArcadeProjectMetadata metadata;
			if (File.Exists(path))
			{
				metadata = JsonConvert.DeserializeObject<ArcadeProjectMetadata>(File.ReadAllText(path))
					?? throw new InvalidDataException("工程元信息为空");
			}
			else
			{
				metadata = new ArcadeProjectMetadata();
				metadata.WriteTo(path);
			}
			if ((metadata.Difficulties?.Length ?? 0) < DIFFICULTY_COUNT)
			{
				Array.Resize(ref metadata.Difficulties, DIFFICULTY_COUNT);
			}

			return metadata;
		}

		private void BeginProject(string folder, ArcadeProjectMetadata metadata)
		{
			CleanProject();
			CurrentProjectFolder = folder;
			CurrentProjectMetadata = metadata;
			Name.text = CurrentProjectMetadata.Title;
			Composer.text = CurrentProjectMetadata.Artist;
			Diff.text = "";
			Name.interactable = true;
			Composer.interactable = true;
			Diff.interactable = true;
			// Visibility is project state, not a color that theme refreshes can overwrite.
			OpenLabel.gameObject.SetActive(false);
		}

		private void LoadCover(int difficulty)
		{
			string[] files = new string[]{
				Path.Combine(CurrentProjectFolder, $"1080_{difficulty}.jpg"),
				Path.Combine(CurrentProjectFolder, $"{difficulty}.jpg"),
				Path.Combine(CurrentProjectFolder, $"1080_{difficulty}_256.jpg"),
				Path.Combine(CurrentProjectFolder, $"{difficulty}_256.jpg"),
				Path.Combine(CurrentProjectFolder, "1080_base.jpg"),
				Path.Combine(CurrentProjectFolder, "base.jpg"),
				Path.Combine(CurrentProjectFolder, "1080_base_256.jpg"),
				Path.Combine(CurrentProjectFolder, "base_256.jpg"),
			};
			foreach (string coverPath in files)
			{
				var info = new FileInfo(coverPath);
				if (!info.Exists) continue;
				if (Cover && CoverSprite && CoverSprite.name == coverPath && coverSize == info.Length
					&& coverModifiedTicks == info.LastWriteTimeUtc.Ticks) return;
				Texture2D texture = Loader.LoadTexture2D(coverPath);
				if (texture != null)
				{
					Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
					sprite.name = coverPath;
					if (CoverSprite) Destroy(CoverSprite);
					if (Cover) Destroy(Cover);
					Cover = texture;
					CoverSprite = sprite;
					coverSize = info.Length;
					coverModifiedTicks = info.LastWriteTimeUtc.Ticks;
					CoverImage.sprite = CoverSprite;
					return;
				}
			}
			if (CoverSprite) Destroy(CoverSprite);
			if (Cover) Destroy(Cover);
			CoverSprite = null;
			Cover = null;
			CoverImage.sprite = DefaultCover;
		}
		struct AudioSpec
		{
			public string path;
			public bool overrided;
		};
		private BassClip FindAudio(string folder, int difficulty, bool reuseAudio, out bool useDifficultyAudio)
		{

			AudioSpec[] files = new AudioSpec[]{
				new AudioSpec{path=Path.Combine(folder, $"{difficulty}.ogg"),overrided=true},
				new AudioSpec{path=Path.Combine(folder, $"{difficulty}.mp3"),overrided=true},
				new AudioSpec{path=Path.Combine(folder, $"{difficulty}.wav"),overrided=true},
				new AudioSpec{path=Path.Combine(folder, $"{difficulty}.opus"),overrided=true},
				new AudioSpec{path=Path.Combine(folder, "base.ogg"),overrided=false},
				new AudioSpec{path=Path.Combine(folder, "base.mp3"),overrided=false},
				new AudioSpec{path=Path.Combine(folder, "base.wav"),overrided=false},
				new AudioSpec{path=Path.Combine(folder, "base.opus"),overrided=false},
			};
			foreach (AudioSpec audioPath in files)
			{
				BassClip clip = reuseAudio && chartAudio && chartAudio.MatchesFile(audioPath.path)
					? chartAudio : BassClip.Load(audioPath.path, Path.Combine(folder, "Arcade", "Backup"));
				if (clip)
				{
					useDifficultyAudio = audioPath.overrided;
					return clip;
				}
			}
			useDifficultyAudio = false;
			return null;
		}

		private static ArcChart ReadChart(string folder, int difficulty, bool createMissingChart)
		{
			string chartPath = Path.Combine(folder, $"{difficulty}.aff");
			if (!File.Exists(chartPath))
			{
				if (!createMissingChart) throw new FileNotFoundException($"找不到谱面文件：{chartPath}", chartPath);
				File.WriteAllText(chartPath, "AudioOffset:0\n-\ntiming(0,100.00,4.00);");
			}
			Aff.RawAffChart raw = null;
			try
			{
				raw = Aff.ArcaeaFileFormat.ParseFromPath(chartPath);
			}
			catch (Exception Ex)
			{
				Debug.LogWarning(Ex);
				AdeBasicSingleDialogContent.Instance.Show(Ex.Message, "谱面读取错误");
			}
			Gameplay.Chart.ArcChart chart = null;
			if (raw != null)
			{
				if (raw.error.Count > 0)
				{
					AdeBasicSingleDialogContent.Instance.Show($"格式问题：\n{(raw.error.Count > 256 ? "* 谱面错误太多，仅显示前 256 条" : "")}{string.Join("\n", raw.error.Take(256).Select(s => $"- {s}"))}", "谱面解析失败");
				}
				else
				{
					if (raw.recoverableError.Count > 0)
					{
						var issues = raw.recoverableError.Select(s => $"错误：{s}")
							.Concat(raw.warning.Select(s => $"警告：{s}")).ToList();
						AdeBasicSingleDialogContent.Instance.Show($"谱面包含可恢复错误，相关事件已保留，谱面将继续加载。请修正以下问题：\n{(issues.Count > 256 ? "* 问题太多，仅显示前 256 条\n" : "")}{string.Join("\n", issues.Take(256).Select(s => $"- {s}"))}", "谱面存在错误（已保留事件）");
					}
					else if (raw.warning.Count > 0)
					{
						AdeBasicSingleDialogContent.Instance.Show($"Arcade-Plus 检测到谱面存在问题，并对谱面进行了自动修复\n谱面在存在的问题：\n{(raw.warning.Count > 256 ? "* 谱面错误太多，仅显示前 256 条" : "")}{string.Join("\n", raw.warning.Take(256).Select(s => $"- {s}"))}", "谱面解析出现问题");
					}
					chart = new Gameplay.Chart.ArcChart(raw);
				}
			}

			return chart;
		}

		private bool ApplyChart(int difficulty, ArcChart chart, BassClip audio, bool useDifficultyAudio, int resumeTiming)
		{
			lock (chartFileLock)
			{
				savedChartPath = null;
				savedChartBytes = null;
			}
			AdeOperationManager.Instance.CancelOngoingOperation();
			AdeSelectionManager.Instance.DeselectAllNotes();
			ArcadeComposeManager.Instance.Pause();
			AdeObsManager.Instance.ForceStopRecording();
			AdeCommandManager.Instance.Clear();

			float rawBaseBpm = useDifficultyAudio ?
				CurrentProjectMetadata.Difficulties[difficulty] == null ? 0 : CurrentProjectMetadata.Difficulties[difficulty].BaseBpm :
				CurrentProjectMetadata.BaseBpm;
			ArcTimingManager.Instance.BaseBpm = ResolveBaseBpm(rawBaseBpm, chart?.Timings);
			BaseBpm.interactable = true;
			BaseBpm.text = ArcTimingManager.Instance.BaseBpm.ToString(CultureInfo.InvariantCulture);

			if (!ArcGameplayManager.Instance.Load(chart, audio)) return false;
			if (chartAudio && chartAudio != audio) Destroy(chartAudio);
			chartAudio = audio;
			audioOverrided = useDifficultyAudio;
			AdeTimingSlider.Instance.Enable = true;
			AdeTimingSlider.Instance.Length = (int)(audio.length * 1000);
			CurrentDifficulty = difficulty;

			string rating = CurrentProjectMetadata.Difficulties[CurrentDifficulty]?.Rating;
			// Unrated charts start from "<difficulty> ?", like "Future ?".
			Diff.text = string.IsNullOrWhiteSpace(rating) ? DefaultRating(CurrentDifficulty) : rating;
			RefreshDifficultyAppearance(difficulty);

			AudioOffset.interactable = true;
			CurrentTimingGroup.interactable = true;
			AudioOffset.text = ArcGameplayManager.Instance.ChartAudioOffset.ToString(CultureInfo.InvariantCulture);

			bool watching = watcher.EnableRaisingEvents;
			watcher.EnableRaisingEvents = false;
			watcher.Path = CurrentProjectFolder;
			watcher.Filter = $"{difficulty}.aff";
			watcher.EnableRaisingEvents = watching;
			ArcGameplayManager.Instance.AudioTiming = Math.Min(resumeTiming, ArcGameplayManager.Instance.Length);
			return true;
		}

		private void LoadSpecialEffectAudio()
		{
			var effects = new HashSet<string>();
			foreach (var arc in ArcGameplayManager.Instance.Chart.Arcs)
			{
				effects.Add(arc.Effect);
			}
			ArcEffectManager.Instance.RetainSpecialEffectAudios(effects);
			foreach (var effect in effects)
			{
				if (effect.EndsWith("_wav"))
				{
					string effectAudioFilename = effect.Substring(0, effect.Length - 4);
					string effectAudioPath = Path.Combine(CurrentProjectFolder, $"{effectAudioFilename}.wav");
					string opusPath = Path.Combine(CurrentProjectFolder, $"{effect}.opus");
					string sourcePath = File.Exists(effectAudioPath) ? effectAudioPath : opusPath;
					if (ArcEffectManager.Instance.SpecialEffectAudios.TryGetValue(effect, out BassClip existing)
						&& existing && existing.MatchesFile(sourcePath)) continue;
					BassClip clip = Loader.LoadAudioFile(sourcePath);
					if (clip != null)
					{
						ArcEffectManager.Instance.AddSpecialEffectAudio(effect, clip);
					}
					else
					{
						Debug.LogWarning($"SpecialEffectAudio for effect '{effect}' load fail");
					}
				}
			}
		}

		private void SetTutorialMessage(string message)
		{
			if (message == null)
			{
				TutorialCanvasGroup.alpha = 0;
				return;
			}
			TutorialCanvasGroup.alpha = 1;
			TutorialText.text = message;
		}

		public void OnComposerEdited()
		{
			if (CurrentProjectMetadata == null) return;
			CurrentProjectMetadata.Artist = Composer.text;
			CurrentProjectMetadata.WriteTo(ProjectMetadataFilePath);
		}
		public void OnNameEdited()
		{
			if (CurrentProjectMetadata == null) return;
			CurrentProjectMetadata.Title = Name.text;
			CurrentProjectMetadata.WriteTo(ProjectMetadataFilePath);
		}
		public void OnDiffEdited()
		{
			if (CurrentProjectMetadata == null) return;
			if (CurrentDifficulty < 0 || CurrentDifficulty > 4) return;
			if (CurrentProjectMetadata.Difficulties[CurrentDifficulty] == null)
			{
				CurrentProjectMetadata.Difficulties[CurrentDifficulty] = new AdeChartDifficultyMetadata();
			}
			CurrentProjectMetadata.Difficulties[CurrentDifficulty].Rating = Diff.text;
			CurrentProjectMetadata.WriteTo(ProjectMetadataFilePath);
		}
		public void OnBaseBpmEdited()
		{
			if (CurrentProjectMetadata == null) return;
			if (!ChartControlText.Float(BaseBpm.text, out float value))
			{
				BaseBpm.SetTextWithoutNotify(ArcTimingManager.Instance.BaseBpm.ToString(CultureInfo.InvariantCulture));
				return;
			}
			if (value <= 0) value = 100;
			if (audioOverrided)
			{
				var difficulty = CurrentProjectMetadata.Difficulties[CurrentDifficulty] ??= new AdeChartDifficultyMetadata();
				difficulty.BaseBpm = value;
			}
			else CurrentProjectMetadata.BaseBpm = value;
			ArcTimingManager.Instance.BaseBpm = value;
			CurrentProjectMetadata.WriteTo(ProjectMetadataFilePath);
			ArcArcManager.Instance.Rebuild();
			BaseBpm.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
		}
		public void OnAudioOffsetEdited()
		{
			int value;
			bool result = int.TryParse(AudioOffset.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
			if (result)
			{
				// The offset is written into the .aff but is not an undoable command.
				if (ArcGameplayManager.Instance.ChartAudioOffset != value) AdeCommandManager.Instance.MarkChanged();
				ArcGameplayManager.Instance.ChartAudioOffset = value;
			}
			AudioOffset.SetTextWithoutNotify(ArcGameplayManager.Instance.ChartAudioOffset.ToString(CultureInfo.InvariantCulture));
		}
		public void OnFileWatchClicked()
		{
			if (CurrentProjectMetadata != null && CurrentDifficulty != -1)
			{
				watcher.EnableRaisingEvents = !watcher.EnableRaisingEvents;
				AdeUiTheme.SetToolbarToggleState(FileWatchEnableImage, watcher.EnableRaisingEvents);
			}
		}

		private void OnWatcherChanged(object sender, FileSystemEventArgs e)
		{
            // A rename also matches the filter when the old chart moves away.
            if (e is RenamedEventArgs && !string.Equals(e.Name, watcher.Filter, StringComparison.OrdinalIgnoreCase)) return;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    lock (chartFileLock)
                    using (var file = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (string.Equals(e.FullPath, savedChartPath, StringComparison.OrdinalIgnoreCase)
                            && MatchesSavedChart(file)) return;
                        System.Threading.Interlocked.Exchange(ref pendingReloadPath, e.FullPath);
                    }
                    return;
                }
                catch (IOException)
                {
                    if (wait.ElapsedMilliseconds >= 3000) return;
                    System.Threading.Thread.Sleep(25);
                }
            }
		}

        private bool MatchesSavedChart(Stream file)
        {
            if (file.Length != savedChartBytes.Length) return false;
            var buffer = new byte[8192];
            int offset = 0, count;
            while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (!buffer.AsSpan(0, count).SequenceEqual(savedChartBytes.AsSpan(offset, count))) return false;
                offset += count;
            }
            return offset == savedChartBytes.Length;
        }

		public void OnSaveModeClicked()
		{
			ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode = ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode == ChartSortMode.Timing ? ChartSortMode.Type : ChartSortMode.Timing;
			SaveMode.text = ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode == ChartSortMode.Timing ? "按时间" : "按类型";
		}
		public void OnOpenFolder()
		{
			if (CurrentProjectMetadata == null || string.IsNullOrWhiteSpace(CurrentProjectFolder)) return;
			Util.Shell.FileBrowser.OpenExplorer(CurrentProjectFolder);
		}

		private bool SaveForQuit()
		{
			FinishSaves(true);
			var gameplay = ArcGameplayManager.Instance;
			if (CurrentProjectMetadata == null || !gameplay || gameplay.Chart == null) return true;
			if (HasUnsavedChart) return TrySaveProject();
			if (CurrentProjectMetadata.LastWorkingDifficulty == CurrentDifficulty &&
				CurrentProjectMetadata.LastWorkingTiming == gameplay.AudioTiming) return true;
			try
			{
				CurrentProjectMetadata.WriteWorkingPosition(ProjectMetadataFilePath, CurrentDifficulty, gameplay.AudioTiming);
				return true;
			}
			catch (Exception ex) { ShowSaveError(ex); return false; }
		}

		private bool quitSaveCompleted;
		private bool OnWantsToQuit() => quitSaveCompleted = SaveForQuit();

		public void SaveAndQuit()
		{
			// The standalone player fires wantsToQuit for both the toolbar and the window close button.
			if (!Application.isEditor || SaveForQuit()) Application.Quit();
		}

		public void OnApplicationQuit()
		{
			if (!quitSaveCompleted) SaveForQuit();
		}
	}
}

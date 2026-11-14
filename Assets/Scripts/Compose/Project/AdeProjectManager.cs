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
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Arcade.Compose.Command;
using Arcade.Util.Loader;
using System.Collections.Generic;
using System.Globalization;

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
		private bool shouldReload = false;
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
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnWatcherChanged;
            watcher.Dispose();
            if (difficultyNormal) Destroy(difficultyNormal);
            if (difficultySelected) Destroy(difficultySelected);
            if (difficultyBeyondNormal) Destroy(difficultyBeyondNormal);
            if (difficultyCutSprite) Destroy(difficultyCutSprite);
            if (difficultyCutTexture) Destroy(difficultyCutTexture);
        }

		private void Start()
		{
            gameObject.AddComponent<AdeSongListExport>();
            RefreshDifficultyAppearance(-1);
			watcher.NotifyFilter = NotifyFilters.LastWrite;
			watcher.Changed += OnWatcherChanged;
			watcher.EnableRaisingEvents = false;
			AutosaveAsync().Forget();
		}
		private void Update()
		{
			if (shouldReload)
			{
				ReloadChart();
				shouldReload = false;
			}
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Save))
			{
				SaveProject();
			}
		}

		private void CreateArcadeDirectories(string folder)
		{
			string[] directories = new string[] {
					ProjectArcadeFolder,
					ProjectAutosaveFolder,
				 	ProjectBackupFolder,
				};
			foreach (var s in directories) if (!Directory.Exists(s)) Directory.CreateDirectory(s);
		}

		public void CleanProject()
		{
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
				RunLoadingAsync(() => LoadProject(folder), false).Forget();
			}
			catch (Exception Ex)
			{
				AdeBasicSingleDialogContent.Instance.Show(Ex.Message, "读取错误");
				Debug.Log(Ex);
				CurrentProjectMetadata = null;
				CurrentProjectFolder = null;
			}
		}
		public bool OpenProjectFolder(string folder)
		{
			if (loading)
			{
				return false;
			}
			RunLoadingAsync(() => LoadProject(folder), false).Forget();
			return true;
		}
		public void SaveProject()
		{
			if (CurrentProjectMetadata == null || CurrentProjectFolder == null) return;
			if (ArcGameplayManager.Instance.Chart == null) return;
			try
			{
				// Finish serialization before touching the existing chart file.
				using (var snapshot = new MemoryStream())
				{
					ArcGameplayManager.Instance.Chart.Serialize(snapshot, ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode, ArcadeComposeManager.Instance.ArcadePreference.LowPrecisionArcAndSlide);
					string path = Path.Combine(CurrentProjectFolder, $"{CurrentDifficulty}.aff");
					if (File.Exists(path)) File.Copy(path, NewSnapshotPath(ProjectBackupFolder, CurrentDifficulty), true);
					CurrentProjectMetadata.LastWorkingDifficulty = CurrentDifficulty;
					CurrentProjectMetadata.LastWorkingTiming = ArcGameplayManager.Instance.AudioTiming;
					File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
					File.WriteAllBytes(path, snapshot.ToArray());
				}
				AdeToast.Instance.Show("谱面已保存");
			}
			catch (Exception Ex)
			{
				AdeBasicSingleDialogContent.Instance.Show(Ex.Message + "\n" + Ex.ToString(), "保存错误");
			}
		}

		private static string NewSnapshotPath(string folder, int difficulty)
		{
			Directory.CreateDirectory(folder);
			// Repeated saves within one second intentionally replace that snapshot.
			return Path.Combine(folder, $"{difficulty}_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.aff");
		}

		private async UniTask AutosaveAsync()
		{
			while (true)
			{
				await UniTask.WaitForSeconds(30f, cancellationToken: destroyCancellationToken);
				if (CurrentProjectMetadata == null || CurrentProjectFolder == null) continue;
				if (ArcGameplayManager.Instance.Chart == null) continue;
				if (ArcGameplayManager.Instance.IsPlaying) continue;
				try
				{
					using (var snapshot = new MemoryStream())
					{
						ArcGameplayManager.Instance.Chart.Serialize(snapshot, ArcadeComposeManager.Instance.ArcadePreference.ChartSortMode, ArcadeComposeManager.Instance.ArcadePreference.LowPrecisionArcAndSlide);
						// The AFF writer closes its stream; ToArray remains valid after Close.
						byte[] bytes = snapshot.ToArray();
						using (var fs = new FileStream(NewSnapshotPath(ProjectAutosaveFolder, CurrentDifficulty), FileMode.Create, FileAccess.Write, FileShare.None))
							fs.Write(bytes, 0, bytes.Length);
					}
				}
				catch (Exception Ex)
				{
					AdeBasicSingleDialogContent.Instance.Show(Ex.Message + "\n" + Ex.ToString(), "自动保存错误");
				}
			}
		}

        private async UniTask RunLoadingAsync(Action load, bool checkChart)
        {
            loading = true;
            try
            {
                await AdeShutterManager.Instance.CloseAsync(destroyCancellationToken);
                load();
                if (checkChart && !ArcGameplayManager.Instance.IsLoaded)
                    SetTutorialMessage("无法加载谱面文件，请修正谱面文件格式或删除谱面文件后重新打开谱面文件夹");
                await AdeShutterManager.Instance.OpenAsync(destroyCancellationToken);
            }
            finally { loading = false; }
        }
		public void ReloadChart()
		{
			if (loading)
			{
				return;
			}
			if (CurrentProjectMetadata == null)
			{
				return;
			}
			RunLoadingAsync(() => LoadChart(CurrentDifficulty), true).Forget();
		}
		public void SwitchDifficulty(int difficulty)
		{
			if (loading)
			{
				return;
			}
			if (CurrentProjectMetadata == null)
			{
				return;
			}
			RunLoadingAsync(() => LoadDifficulty(difficulty), true).Forget();
		}

		public void SetDefaultCover(Sprite cover)
		{
			if (CoverImage.sprite == DefaultCover)
			{
				CoverImage.sprite = cover;
			}
			DefaultCover = cover;
		}
		private void LoadDifficulty(int difficulty)
		{
			LoadCover(difficulty);
			LoadAudio(difficulty);
			if (chartAudio == null)
			{
				SetTutorialMessage("无法加载音频文件，请确认音频文件存在且格式正确后重新打开谱面文件夹");
				return;
			}
			LoadChart(difficulty);
			if (!ArcGameplayManager.Instance.IsLoaded)
			{
				SetTutorialMessage("无法加载谱面文件，请修正谱面文件格式或删除谱面文件后重新打开谱面文件夹");
				return;
			}
			LoadSpecialEffectAudio();
			SetTutorialMessage(null);
		}

		private void LoadProject(string folder)
		{
			CleanProject();
			watcher.EnableRaisingEvents = false;
			AdeUiTheme.SetToolbarToggleState(FileWatchEnableImage, false);
			CurrentProjectFolder = folder;
			CreateArcadeDirectories(folder);
			try
			{
				LoadMetadata();
			}
			catch (Exception Ex)
			{
				AdeBasicSingleDialogContent.Instance.Show(Ex.Message, "元信息读取错误");
				SetTutorialMessage("无法加载工程元信息，请删除谱面文件夹下的 Arcade 文件夹后重新打开谱面文件夹");
				return;
			}
			LoadDifficulty(CurrentProjectMetadata.LastWorkingDifficulty);
		}

		public static float ResolveBaseBpm(float configuredBpm, IEnumerable<ArcTiming> timings)
		{
			if (configuredBpm != 0) return configuredBpm;
			// Match Alpha when the project has no explicit speed reference.
			return timings?.Where(t => t.Bpm > 0).OrderBy(t => t.Timing)
				.FirstOrDefault()?.Bpm ?? 100;
		}

		private void LoadMetadata()
		{
			try
			{
				CurrentProjectMetadata = JsonConvert.DeserializeObject<ArcadeProjectMetadata>(File.ReadAllText(ProjectMetadataFilePath));
			}
			catch
			{
				CurrentProjectMetadata = new ArcadeProjectMetadata();
				File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
			}
			if (CurrentProjectMetadata.Difficulties.Length < DIFFICULTY_COUNT)
			{
				var Difficulties = new AdeChartDifficultyMetadata[DIFFICULTY_COUNT];
				CurrentProjectMetadata.Difficulties.CopyTo(Difficulties, 0);
				CurrentProjectMetadata.Difficulties = Difficulties;
			}

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
		private void LoadAudio(int difficulty)
		{

			AudioSpec[] files = new AudioSpec[]{
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, $"{difficulty}.ogg"),overrided=true},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, $"{difficulty}.mp3"),overrided=true},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, $"{difficulty}.wav"),overrided=true},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, $"{difficulty}.opus"),overrided=true},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, "base.ogg"),overrided=false},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, "base.mp3"),overrided=false},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, "base.wav"),overrided=false},
				new AudioSpec{path=Path.Combine(CurrentProjectFolder, "base.opus"),overrided=false},
			};
			foreach (AudioSpec audioPath in files)
			{
				if (chartAudio && chartAudio.MatchesFile(audioPath.path))
				{
					audioOverrided = audioPath.overrided;
					return;
				}
				BassClip clip = Loader.LoadAudioFile(audioPath.path);
				if (clip != null)
				{
					ArcAudioManager.Instance.ReleaseVoice();
					if (chartAudio) Destroy(chartAudio);
					chartAudio = clip;
					AdeTimingSlider.Instance.Enable = true;
					AdeTimingSlider.Instance.Length = (int)(chartAudio.length * 1000);
					audioOverrided = audioPath.overrided;
					return;
				}
			}
			ArcAudioManager.Instance.ReleaseVoice();
			if (chartAudio) Destroy(chartAudio);
			chartAudio = null;
		}

		private void LoadChart(int difficulty)
		{
			AdeOperationManager.Instance.CancelOngoingOperation();
			AdeSelectionManager.Instance.DeselectAllNotes();
			if (CurrentProjectMetadata == null || CurrentProjectFolder == null || chartAudio == null)
			{
				return;
			}
			string chartPath = Path.Combine(CurrentProjectFolder, $"{difficulty}.aff");
			if (!File.Exists(chartPath))
			{
				File.WriteAllText(chartPath, "AudioOffset:0\n-\ntiming(0,100.00,4.00);");
			}
			ArcadeComposeManager.Instance.Pause();
			AdeObsManager.Instance.ForceStopRecording();
			AdeCommandManager.Instance.Clear();
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

			float rawBaseBpm = audioOverrided ?
				CurrentProjectMetadata.Difficulties[difficulty] == null ? 0 : CurrentProjectMetadata.Difficulties[difficulty].BaseBpm :
				CurrentProjectMetadata.BaseBpm;
			ArcTimingManager.Instance.BaseBpm = ResolveBaseBpm(rawBaseBpm, chart?.Timings);
			BaseBpm.interactable = true;
			BaseBpm.text = ArcTimingManager.Instance.BaseBpm.ToString(CultureInfo.InvariantCulture);

			ArcGameplayManager.Instance.Load(chart, chartAudio);
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
			ArcGameplayManager.Instance.AudioTiming = CurrentProjectMetadata.LastWorkingTiming;
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
			File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
		}
		public void OnNameEdited()
		{
			if (CurrentProjectMetadata == null) return;
			CurrentProjectMetadata.Title = Name.text;
			File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
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
			File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
		}
		public void OnBaseBpmEdited()
		{
			float value;
			bool result = float.TryParse(BaseBpm.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
			if (result)
			{
				if (value <= 0) value = 100;
				if (CurrentProjectMetadata != null)
				{
					if (audioOverrided)
					{
						if (CurrentProjectMetadata.Difficulties[CurrentDifficulty] == null)
						{
							CurrentProjectMetadata.Difficulties[CurrentDifficulty] = new AdeChartDifficultyMetadata();
						}
						CurrentProjectMetadata.Difficulties[CurrentDifficulty].BaseBpm = value;
					}
					else
					{
						CurrentProjectMetadata.BaseBpm = value;
					}
				};
				ArcTimingManager.Instance.BaseBpm = value;
				File.WriteAllText(ProjectMetadataFilePath, JsonConvert.SerializeObject(CurrentProjectMetadata));
				ArcArcManager.Instance.Rebuild();
				BaseBpm.text = value.ToString(CultureInfo.InvariantCulture);
			}
		}
		public void OnAudioOffsetEdited()
		{
			int value;
			bool result = int.TryParse(AudioOffset.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
			if (result)
			{
				ArcGameplayManager.Instance.ChartAudioOffset = value;
				AudioOffset.text = value.ToString(CultureInfo.InvariantCulture);
			}
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
			DateTime begin = DateTime.Now;
			FileStream fs = null;

		retry:
			try
			{
				fs = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None);
			}
			catch (IOException)
			{
				if (DateTime.Now - begin < TimeSpan.FromSeconds(3))
					goto retry;
				else return;
			}

			fs?.Close();

			shouldReload = true;
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

		public void OnApplicationQuit()
		{
			Debug.Log("saving project when exit...");
			SaveProject();
			Debug.Log("saved project when exit");
		}
	}
}

using Cysharp.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // A tiny first scene can draw before the editor scene is deserialized. Nothing here
    // survives the handoff: no render texture, particle system, timer or image cache.
    public sealed class AdeStartupLoader : MonoBehaviour
    {
        private const string EditorScene = "ArcEditor";
        private const float SweepPeriod = 1.3f;
        private TextMeshProUGUI status, percentage, promptTitle, promptBody, primaryLabel, secondaryLabel;
        private Image progress, sweep;
        private CanvasGroup surface;
        private RectTransform content, canvasRoot, rail, primaryRect, secondaryRect;
        private Fader track, loadInfo, sweepFader, prompt;
        private Texture2D fillTexture, outlineTexture;
        private Sprite fillSprite, outlineSprite;
        private int shownPercent = -1;
        private int previousFrameRate;
        private float displayedFill, barTarget, sweepTime;
        private bool fadeNextStatus;
        private string swapTarget;

        // One fading group. Only transitions run coroutines; hidden groups are deactivated.
        private sealed class Fader
        {
            public CanvasGroup Group;
            public RectTransform Rect;
            public Vector2 Home;
            public Coroutine Running;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
            BuildSurface();
            surface.alpha = 0;
        }

        private IEnumerator Start()
        {
            // Restore the actual OS window before presenting text or the logo. The
            // main scene must not resize this window again during its activation.
            yield return AdeWindowState.RestoreStartup();
            surface.alpha = 1;
            // Present the small logo and rail before loading the editor font/atlas.
            yield return null;
            var font = Resources.LoadAsync<TMP_FontAsset>("AffdataEdit/Fonts/EditorUI SDF");
            yield return font;
            BuildLabels();
            if (AdeUpdateChecker.ShouldCheck)
            {
                var checker = new GameObject(nameof(AdeUpdateChecker)).AddComponent<AdeUpdateChecker>();
                var eventObject = new GameObject("StartupInput", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventObject.transform.SetParent(transform, false);
                eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                BuildPrompt(checker);
                // While checking, the rail stays in place with a moving highlight; the
                // percentage and detail line appear only once real loading starts.
                rail.gameObject.AddComponent<RectMask2D>();
                SetHidden(loadInfo);
                sweepFader.Group.gameObject.SetActive(true);
                status.text = "正在检查最新版本…";
                yield return checker.RunAtStartup(ShowCheckState).ToCoroutine();
                prompt.Group.interactable = prompt.Group.blocksRaycasts = false;
                // Disable before scene activation so there is never more than one active EventSystem.
                eventObject.SetActive(false);
                Destroy(eventObject);
                if (prompt.Group.gameObject.activeSelf)
                {
                    // Continued after a failed check: bring the loading row back in.
                    status.text = "正在准备编辑器";
                    Run(prompt, false, 6);
                    Run(track, true, -8);
                }
                else fadeNextStatus = true;
                Run(sweepFader, false, 0);
                Run(loadInfo, true, 0);
                Destroy(prompt.Group.gameObject, .25f);
            }
            // Present a complete frame before asking Unity to load the large scene.
            SetProgress(0, "正在准备编辑器");
            yield return null;
            if (!Application.CanStreamedLevelBeLoaded(EditorScene))
            {
                swapTarget = null;
                status.text = "无法载入编辑器，请检查安装文件";
                Application.targetFrameRate = previousFrameRate;
                Debug.LogError("The editor scene is missing from the player build.");
                yield break;
            }

            AsyncOperation loading = SceneManager.LoadSceneAsync(EditorScene, LoadSceneMode.Single);
            loading.allowSceneActivation = false;
            while (loading.progress < .9f)
            {
                // Unity reserves the final 10% of its progress for scene activation.
                SetProgress(loading.progress / .9f, "正在载入编辑器资源");
                yield return null;
            }
            yield return AdeWindowState.WaitForStableSize();
            // Loading is complete; let the bar finish filling before the editor opens.
            SetProgress(1, "正在载入编辑器资源");
            while (displayedFill < 1) yield return null;
            // Scene activation sets up the whole editor in one blocking step (about a second)
            // and nothing can draw meanwhile, so it happens on a full bar labelled as opening.
            // Finish crossfading the label first so it never freezes half-faded.
            while (swapTarget != null) yield return null;
            swapTarget = "正在打开编辑器…";
            yield return SwapStatus(.3f, .08f, .12f);
            yield return null;
            Application.targetFrameRate = previousFrameRate;
            loading.allowSceneActivation = true;
            yield return loading;
            // sceneLoaded precedes Start; give scene scripts and their next-frame UI
            // construction a chance to finish before exposing the editor.
            yield return null;
            yield return null;

            // The editor's first frames can be long. Wait (at most 0.5 s) for a normal frame,
            // then cap each fade step so one slow frame cannot finish the 0.16 s fade at once.
            for (float settle = 0; settle < .5f && Time.unscaledDeltaTime > 1f / 20; settle += Time.unscaledDeltaTime)
                yield return null;
            float elapsed = 0;
            while (elapsed < .16f)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30);
                surface.alpha = 1 - Mathf.Clamp01(elapsed / .16f);
                yield return null;
            }
            Destroy(gameObject);
        }

        private void ShowCheckState(bool checking, bool required, string message)
        {
            if (checking)
            {
                status.text = message;
                if (prompt.Group.gameObject.activeSelf)
                {
                    Run(prompt, false, 6);
                    Run(track, true, -8);
                }
                return;
            }
            // First line is the heading, the rest is the explanation.
            int split = message.IndexOf('\n');
            promptTitle.text = split < 0 ? message : message.Substring(0, split);
            promptBody.text = split < 0 ? "" : message.Substring(split + 1);
            float bodyHeight = Mathf.Max(52, promptBody.GetPreferredValues(promptBody.text, 440, 0).y);
            promptBody.rectTransform.sizeDelta = new Vector2(440, bodyHeight);
            primaryRect.anchoredPosition = new Vector2(0, -(40 + bodyHeight + 28));
            secondaryRect.anchoredPosition = new Vector2(226, -(40 + bodyHeight + 28));
            primaryLabel.text = required ? "立即更新" : "重试";
            secondaryLabel.text = required ? "退出" : "继续使用";
            Run(track, false, 6);
            Run(prompt, true, -8);
        }

        private void LateUpdate()
        {
            if (sweep && sweep.gameObject.activeInHierarchy)
            {
                sweepTime = (sweepTime + Time.unscaledDeltaTime) % SweepPeriod;
                float s = sweepTime / SweepPeriod;
                sweep.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-140, 450, s * s * (3 - 2 * s)), 0);
            }
            if (!progress || surface.alpha <= 0 || displayedFill >= barTarget) return;
            // Chase the target smoothly instead of restarting a tween on every change. A
            // loading stall counts as one frame at most, so the bar cannot leap afterwards;
            // the minimum speed fills an empty bar in about 0.35 s and keeps the end from crawling.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30);
            float step = Mathf.Max((barTarget - displayedFill) * (1 - Mathf.Exp(-12 * dt)), 1.2f * dt);
            displayedFill = Mathf.Min(barTarget, displayedFill + step);
            progress.rectTransform.anchorMax = new Vector2(displayedFill, 1);
            ShowPercent(Mathf.FloorToInt(displayedFill * 100 + .001f));
        }

        private void ShowPercent(int value)
        {
            if (value == shownPercent) return;
            shownPercent = value;
            percentage.text = value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        private void SetProgress(float value, string message)
        {
            // The bar only moves forward.
            barTarget = Mathf.Max(barTarget, Mathf.Clamp01(value));
            if (shownPercent < 0) ShowPercent(0);
            if (swapTarget != null) swapTarget = message;
            else if (status.text == message) return;
            else if (fadeNextStatus)
            {
                fadeNextStatus = false;
                swapTarget = message;
                StartCoroutine(SwapStatus(0, .12f, .16f));
            }
            else status.text = message;
        }

        // Changes the status line in place: fades down to `dip`, swaps the text, fades back up.
        // A full fade (dip 0) suits the checking -> loading change; a shallow, quick dip keeps
        // the final "opening" label from drawing attention to itself.
        private IEnumerator SwapStatus(float dip, float fadeOut, float fadeIn)
        {
            var group = status.GetComponent<CanvasGroup>();
            for (float elapsed = 0; elapsed < fadeOut; elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30))
            {
                group.alpha = Mathf.Lerp(1, dip, elapsed / fadeOut);
                yield return null;
            }
            if (swapTarget != null) status.text = swapTarget;
            swapTarget = null;
            for (float elapsed = 0; elapsed < fadeIn; elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30))
            {
                float t = elapsed / fadeIn;
                group.alpha = Mathf.Lerp(dip, 1, 1 - (1 - t) * (1 - t));
                yield return null;
            }
            group.alpha = 1;
        }

        private void Run(Fader fader, bool show, float offset)
        {
            if (fader.Running != null) StopCoroutine(fader.Running);
            fader.Running = StartCoroutine(Fade(fader, show, offset));
        }

        // 0.20 s ease-out in, 0.14 s ease-in out, matching the editor's dialog motion.
        private IEnumerator Fade(Fader fader, bool show, float offset)
        {
            if (show) fader.Group.gameObject.SetActive(true);
            fader.Group.interactable = fader.Group.blocksRaycasts = false;
            float from = fader.Group.alpha, duration = show ? .2f : .14f, elapsed = 0;
            do
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30);
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = show ? 1 - (1 - t) * (1 - t) * (1 - t) : t * t;
                fader.Group.alpha = Mathf.Lerp(from, show ? 1 : 0, eased);
                if (offset != 0)
                    fader.Rect.anchoredPosition = fader.Home + new Vector2(0, show ? offset * (1 - eased) : offset * eased);
                yield return null;
            } while (elapsed < duration);
            if (show) fader.Group.interactable = fader.Group.blocksRaycasts = true;
            else fader.Group.gameObject.SetActive(false);
            fader.Running = null;
        }

        private static void SetHidden(Fader fader)
        {
            fader.Group.alpha = 0;
            fader.Group.gameObject.SetActive(false);
        }

        private static Fader MakeGroup(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);
            return new Fader { Group = rect.GetComponent<CanvasGroup>(), Rect = rect, Home = rect.anchoredPosition };
        }

        private void BuildPrompt(AdeUpdateChecker checker)
        {
            var palette = AdeUiTheme.Current;
            fillSprite = RoundedSprite(false, out fillTexture);
            outlineSprite = RoundedSprite(true, out outlineTexture);
            prompt = MakeGroup("Prompt", content, 0, 118, 440, 200);
            SetHidden(prompt);
            promptTitle = Label("Title", prompt.Rect, "", 22, palette.Text);
            Place(promptTitle.rectTransform, 0, 0, 440, 32);
            promptBody = Label("Body", prompt.Rect, "", 15, palette.Muted);
            Place(promptBody.rectTransform, 0, 40, 440, 52);
            promptBody.textWrappingMode = TextWrappingModes.Normal;
            promptBody.alignment = TextAlignmentOptions.TopLeft;
            promptBody.lineSpacing = 12;
            primaryRect = PromptButton("UpdateAction", 0, fillSprite, palette.Accent, palette.AccentText,
                checker.RetryOrUpdate, out primaryLabel);
            secondaryRect = PromptButton("ContinueAction", 226, outlineSprite, palette.Border, palette.Text,
                checker.ContinueOrQuit, out secondaryLabel);
        }

        private RectTransform PromptButton(string name, float x, Sprite sprite, Color surfaceColor, Color textColor,
            UnityEngine.Events.UnityAction action, out TextMeshProUGUI label)
        {
            var image = Rectangle(name, prompt.Rect, surfaceColor);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Place(image.rectTransform, x, 120, 214, 44);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(.9f, .9f, .9f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(action);
            label = Label("Label", image.transform, "", 16, textColor);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            return image.rectTransform;
        }

        // A 40 px nine-slice rounded rectangle (radius 8), filled or 1 px outline, tinted by Image.color.
        private static Sprite RoundedSprite(bool outline, out Texture2D texture)
        {
            const int scale = 2;
            const float radius = 8;
            int size = Mathf.CeilToInt((radius * 2 + 4) * scale);
            float half = size / (2f * scale);
            var pixels = new Color32[size * size];
            Color border = Color.white, fill = outline ? Color.clear : Color.white;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 q = new Vector2(Mathf.Abs((x + .5f) / scale - half), Mathf.Abs((y + .5f) / scale - half))
                        - Vector2.one * (half - radius);
                    float distance = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude
                        + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    Color color = Color.Lerp(border, fill, Mathf.Clamp01(.5f - (distance + 1) * scale));
                    color.a *= Mathf.Clamp01(.5f - distance * scale);
                    pixels[y * size + x] = color;
                }
            }
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float slice = (radius + 1) * scale;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100 * scale, 0,
                SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        }

        private void OnDestroy()
        {
            if (fillSprite) Destroy(fillSprite);
            if (outlineSprite) Destroy(outlineSprite);
            if (fillTexture) Destroy(fillTexture);
            if (outlineTexture) Destroy(outlineTexture);
        }

        private void BuildSurface()
        {
            var palette = AdeUiTheme.Current;
            var cameraObject = new GameObject("StartupCamera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = palette.Chrome;
            camera.cullingMask = 0;
            camera.depth = -100;
            camera.allowHDR = camera.allowMSAA = false;

            var canvasObject = new GameObject("StartupSurface", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            canvasRoot = (RectTransform)canvasObject.transform;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            // Fit the whole design with one scale. Averaging the two axes makes the
            // content grow relative to the shorter edge in unusually wide/tall windows.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            surface = canvasObject.GetComponent<CanvasGroup>();
            var background = Rectangle("Background", canvasObject.transform, palette.Chrome);
            Stretch(background.rectTransform);
            background.raycastTarget = true;

            // A faint outline of the application's star mark with a soft glow, built once from static triangles.
            var motif = new GameObject("StarMark", typeof(RectTransform), typeof(CanvasRenderer));
            motif.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)motif.transform);
            var mark = motif.AddComponent<StartupMark>();
            mark.color = Color.Lerp(palette.Chrome, palette.Muted, .08f);
            mark.raycastTarget = false;

            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(canvasObject.transform, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, .5f);
            content.sizeDelta = new Vector2(440, 236);
            // Sits slightly above centre so the taller update prompt is optically centred.
            content.anchoredPosition = new Vector2(0, 42);

            // This is the application's actual icon, imported as a small, non-readable
            // startup copy. Keep its original artwork and color in both UI themes.
            var logo = new GameObject("Mark", typeof(RectTransform), typeof(RawImage));
            logo.transform.SetParent(content, false);
            Place((RectTransform)logo.transform, 0, -1, 50, 50);
            var logoGraphic = logo.GetComponent<RawImage>();
            logoGraphic.texture = Resources.Load<Texture2D>("AffdataEdit/ApplicationIcon");
            logoGraphic.raycastTarget = false;
            track = MakeGroup("Track", content, 0, 0, 440, 236);
            var railImage = Rectangle("ProgressTrack", track.Rect, palette.Border);
            rail = railImage.rectTransform;
            Place(rail, 0, 184, 440, 3);
            progress = Rectangle("Progress", rail, palette.Accent);
            Stretch(progress.rectTransform);
            progress.rectTransform.anchorMax = new Vector2(0, 1);
            sweep = Rectangle("Sweep", rail, palette.Accent);
            sweep.rectTransform.anchorMin = Vector2.zero;
            sweep.rectTransform.anchorMax = new Vector2(0, 1);
            sweep.rectTransform.pivot = new Vector2(0, .5f);
            sweep.rectTransform.sizeDelta = new Vector2(132, 0);
            sweep.rectTransform.anchoredPosition = new Vector2(-140, 0);
            var sweepGroup = sweep.gameObject.AddComponent<CanvasGroup>();
            sweepFader = new Fader { Group = sweepGroup, Rect = sweep.rectTransform };
            sweep.gameObject.SetActive(false);
        }

        private void BuildLabels()
        {
            var palette = AdeUiTheme.Current;
            var title = Label("Title", content, "AffdataEdit", 37, palette.Text);
            Place(title.rectTransform, 69, 1, 371, 48);
            title.characterSpacing = -.8f;
            var subtitle = Label("Subtitle", content, "CHART EDITOR", 12, palette.Muted);
            Place(subtitle.rectTransform, 71, 49, 320, 20);
            subtitle.characterSpacing = 4;

            status = Label("Status", track.Rect, "", 19, palette.Text);
            status.gameObject.AddComponent<CanvasGroup>();
            Place(status.rectTransform, 0, 139, 340, 30);
            loadInfo = MakeGroup("LoadInfo", track.Rect, 0, 0, 440, 236);
            percentage = Label("Percentage", loadInfo.Rect, "", 19, palette.Muted);
            percentage.alignment = TextAlignmentOptions.Right;
            Place(percentage.rectTransform, 340, 139, 100, 30);
            var detail = Label("Detail", loadInfo.Rect, "资源  /  界面  /  工作区", 12, palette.Muted);
            Place(detail.rectTransform, 0, 203, 440, 22);
            detail.characterSpacing = 1;

            var version = Label("Version", canvasRoot, "AFFDATAEDIT   /   " + Application.version,
                11, palette.Muted);
            version.characterSpacing = 1;
            version.alignment = TextAlignmentOptions.Center;
            version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(.5f, 0);
            version.rectTransform.pivot = new Vector2(.5f, 0);
            version.rectTransform.sizeDelta = new Vector2(500, 24);
            version.rectTransform.anchoredPosition = new Vector2(0, 28);
        }

        private static Image Rectangle(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(string name, Transform parent, string value, float size, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            AdeEditorText.Configure(label, size);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = color;
            label.text = value;
            return label;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        [RequireComponent(typeof(CanvasRenderer))]
        private sealed class StartupMark : MaskableGraphic
        {
            // The application's four-point star in 1280x720 design pixels, cropped by the
            // bottom-right corner: the star with its centre diamond, two evenly spaced
            // outward (mitred) echoes, and a soft glow in the logo's blue behind them.
            private static readonly Vector2 Centre = new Vector2(1120, 600);
            private static readonly Vector2[] Star =
            {
                new Vector2(1120, 150), new Vector2(1194, 526), new Vector2(1570, 600), new Vector2(1194, 674),
                new Vector2(1120, 1050), new Vector2(1046, 674), new Vector2(670, 600), new Vector2(1046, 526),
            };
            private static readonly Vector2[] Diamond =
            {
                new Vector2(1120, 526), new Vector2(1194, 600), new Vector2(1120, 674), new Vector2(1046, 600),
            };
            private static readonly Vector2[] Echo1 =
            {
                new Vector2(1120, -26.1f), new Vector2(1017, 497), new Vector2(493.9f, 600), new Vector2(1017, 703),
                new Vector2(1120, 1226.1f), new Vector2(1223, 703), new Vector2(1746.1f, 600), new Vector2(1223, 497),
            };
            private static readonly Vector2[] Echo2 =
            {
                new Vector2(1120, -202.1f), new Vector2(988.1f, 468.1f), new Vector2(317.9f, 600), new Vector2(988.1f, 731.9f),
                new Vector2(1120, 1402.1f), new Vector2(1251.9f, 731.9f), new Vector2(1922.1f, 600), new Vector2(1251.9f, 468.1f),
            };
            private static readonly Color Glow = new Color(74 / 255f, 163 / 255f, 240 / 255f);

            protected override void OnPopulateMesh(VertexHelper mesh)
            {
                mesh.Clear();
                Rect r = rectTransform.rect;
                if (r.width <= 0 || r.height <= 0) return;
                // Keep the illustration's design aspect when the window changes. Fill
                // the background by cropping its edges, rather than stretching each axis.
                float fit = Mathf.Max(r.width / 1280, r.height / 720);
                Vector2 size = new Vector2(1280, 720) * fit;
                r = new Rect(r.center - size * .5f, size);
                AddGlow(mesh, r);
                Color line = color;
                DrawOutline(mesh, r, Echo2, Faded(line, .32f));
                DrawOutline(mesh, r, Echo1, Faded(line, .6f));
                DrawOutline(mesh, r, Star, line);
                DrawOutline(mesh, r, Diamond, line);
            }

            private static Color Faded(Color value, float alpha)
            {
                value.a *= alpha;
                return value;
            }

            private static Vector2 Point(Rect r, Vector2 design) =>
                r.min + Vector2.Scale(new Vector2(design.x / 1280, 1 - design.y / 720), r.size);

            // Radial falloff from vertex colours alone, no texture. Vertex colours only interpolate
            // linearly across each triangle, so the disc is dense (24 rings x 96 slices) and follows
            // one smooth curve, 13% * (1 - t)^2.2, with no stop where the slope changes.
            private static void AddGlow(VertexHelper mesh, Rect r)
            {
                const int Segments = 96, Rings = 24;
                const float Radius = 560;
                float scale = r.width / 1280;
                Vector2 centre = Point(r, Centre);
                int first = mesh.currentVertCount;
                mesh.AddVert(centre, Faded(Glow, .13f), Vector2.zero);
                for (int ring = 1; ring <= Rings; ring++)
                {
                    float t = ring / (float)Rings;
                    Color tint = Faded(Glow, .13f * Mathf.Pow(1 - t, 2.2f));
                    for (int i = 0; i < Segments; i++)
                    {
                        float angle = i * Mathf.PI * 2 / Segments;
                        mesh.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Radius * scale * t),
                            tint, Vector2.zero);
                    }
                }
                for (int i = 0; i < Segments; i++)
                    mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % Segments);
                for (int ring = 1; ring < Rings; ring++)
                {
                    int inner = first + 1 + (ring - 1) * Segments, outer = inner + Segments;
                    for (int i = 0; i < Segments; i++)
                    {
                        int next = (i + 1) % Segments;
                        mesh.AddTriangle(inner + i, outer + i, outer + next);
                        mesh.AddTriangle(inner + i, outer + next, inner + next);
                    }
                }
            }

            private static void DrawOutline(VertexHelper mesh, Rect r, Vector2[] points, Color32 color)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Length];
                    AddLine(mesh, r, new Vector2(a.x / 1280, 1 - a.y / 720), new Vector2(b.x / 1280, 1 - b.y / 720),
                        1.5f / 1280, color);
                }
            }
        }

        private static void AddLine(VertexHelper mesh, Rect r, Vector2 start, Vector2 end, float width, Color32 color)
        {
            Vector2 a = r.min + Vector2.Scale(start, r.size), b = r.min + Vector2.Scale(end, r.size);
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * r.width * .5f);
            int index = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero);
            mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}

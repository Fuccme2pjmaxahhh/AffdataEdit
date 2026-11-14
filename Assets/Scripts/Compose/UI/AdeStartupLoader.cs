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
        private TextMeshProUGUI status, percentage;
        private Image progress;
        private CanvasGroup surface;
        private RectTransform content, canvasRoot;
        private int displayedProgress = -1;
        private int previousFrameRate;
        private const float FillDuration = .2f;
        private float displayedFill, fillStart, fillTarget, fillElapsed;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 30;
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
            // Present a complete frame before asking Unity to load the large scene.
            SetProgress(0, "正在准备编辑器");
            yield return null;
            if (!Application.CanStreamedLevelBeLoaded(EditorScene))
            {
                status.text = "无法载入编辑器，请检查安装文件";
                Application.targetFrameRate = previousFrameRate;
                Debug.LogError("The editor scene is missing from the player build.");
                yield break;
            }

            AsyncOperation loading = SceneManager.LoadSceneAsync(EditorScene, LoadSceneMode.Single);
            loading.allowSceneActivation = false;
            while (loading.progress < .9f)
            {
                // Unity reserves the final 10% for scene activation. This is its actual
                // reported progress, with no clock-based increment or fabricated ETA.
                SetProgress(Mathf.FloorToInt(loading.progress * 100), "正在载入编辑器资源");
                yield return null;
            }
            SetProgress(90, "正在初始化工作区");
            yield return null;
            yield return AdeWindowState.WaitForStableSize();
            while (displayedFill < fillTarget) yield return null;
            Application.targetFrameRate = previousFrameRate;
            loading.allowSceneActivation = true;
            yield return loading;
            // sceneLoaded precedes Start; give scene scripts and their next-frame UI
            // construction a chance to finish before exposing the editor.
            yield return null;
            yield return null;
            SetProgress(100, "准备就绪");
            while (displayedFill < fillTarget) yield return null;

            float elapsed = 0;
            while (elapsed < .16f)
            {
                elapsed += Time.unscaledDeltaTime;
                surface.alpha = 1 - Mathf.Clamp01(elapsed / .16f);
                yield return null;
            }
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (!progress || surface.alpha <= 0 || displayedFill >= fillTarget) return;
            // A loading stall may span the whole tween. Spend at most one loading
            // frame per rendered update so the next frame cannot jump to the end.
            fillElapsed = Mathf.Min(FillDuration, fillElapsed + Mathf.Min(Time.unscaledDeltaTime, 1f / 30));
            float t = fillElapsed / FillDuration;
            displayedFill = fillElapsed >= FillDuration
                ? fillTarget : Mathf.LerpUnclamped(fillStart, fillTarget, t * t * (3 - 2 * t));
            progress.rectTransform.anchorMax = new Vector2(displayedFill, 1);
        }

        private void SetProgress(int value, string message)
        {
            if (displayedProgress != value)
            {
                displayedProgress = value;
                percentage.text = value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
                fillStart = displayedFill;
                fillTarget = value / 100f;
                fillElapsed = 0;
            }
            if (status.text != message) status.text = message;
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

            // A quiet chart-like line drawing, built from a few static triangles.
            var motif = new GameObject("ChartLines", typeof(RectTransform), typeof(CanvasRenderer));
            motif.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)motif.transform);
            var lines = motif.AddComponent<StartupLines>();
            lines.color = Color.Lerp(palette.Chrome, palette.Muted, .11f);
            lines.raycastTarget = false;

            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(canvasObject.transform, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, .5f);
            content.sizeDelta = new Vector2(440, 236);
            content.anchoredPosition = new Vector2(0, 12);

            // This is the application's actual icon, imported as a small, non-readable
            // startup copy. Keep its original artwork and color in both UI themes.
            var mark = new GameObject("Mark", typeof(RectTransform), typeof(RawImage));
            mark.transform.SetParent(content, false);
            Place((RectTransform)mark.transform, 0, -1, 50, 50);
            var markGraphic = mark.GetComponent<RawImage>();
            markGraphic.texture = Resources.Load<Texture2D>("AffdataEdit/ApplicationIcon");
            markGraphic.raycastTarget = false;
            var rail = Rectangle("ProgressTrack", content, palette.Border);
            Place(rail.rectTransform, 0, 184, 440, 3);
            progress = Rectangle("Progress", rail.transform, palette.Accent);
            Stretch(progress.rectTransform);
            progress.rectTransform.anchorMax = new Vector2(0, 1);
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

            status = Label("Status", content, "", 19, palette.Text);
            Place(status.rectTransform, 0, 139, 340, 30);
            percentage = Label("Percentage", content, "", 19, palette.Muted);
            percentage.alignment = TextAlignmentOptions.Right;
            Place(percentage.rectTransform, 340, 139, 100, 30);
            var detail = Label("Detail", content, "资源  /  界面  /  工作区", 12, palette.Muted);
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
        private sealed class StartupLines : MaskableGraphic
        {
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
                for (int lane = 0; lane < 5; lane++)
                    AddLine(mesh, r, new Vector2(.68f + lane * .055f, 0), new Vector2(.72f + lane * .008f, 1),
                        1 / r.width, color);
                Vector2 previous = new Vector2(.57f, -.03f);
                for (int i = 1; i <= 28; i++)
                {
                    float t = i / 28f;
                    var next = new Vector2(.57f + .39f * Mathf.Sin(t * 1.9f), t);
                    AddLine(mesh, r, previous, next, 2 / r.width, color);
                    previous = next;
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

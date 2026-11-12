using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Arcade.Compose;
using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using Arcade.Compose.Dialog;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

public class MemoryBackgroundCacheTests
{
    private static readonly Type CacheType = typeof(AdeSkinHost).Assembly.GetType("Arcade.Compose.AdeBackgroundImageCache", true);
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private object cache;
    private string folder;
    private readonly List<Sprite> sprites = new List<Sprite>();

    [SetUp]
    public void SetUp()
    {
        cache = Activator.CreateInstance(CacheType, Members, null, new object[] { null }, null);
        folder = Path.Combine(Path.GetTempPath(), "AffdataBackgroundTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown()
    {
        ((IDisposable)cache).Dispose();
        foreach (var sprite in sprites)
        {
            if (!sprite) continue;
            UnityEngine.Object.DestroyImmediate(sprite.texture);
            UnityEngine.Object.DestroyImmediate(sprite);
        }
        sprites.Clear();
        Directory.Delete(folder, true);
    }

    [Test]
    public void SceneBackgroundWidgetRequestsAndDisplaysCachedPreview()
    {
        var scene = EditorSceneManager.OpenScene("Assets/_Scenes/ArcEditor.unity", OpenSceneMode.Additive);
        var hostObject = new GameObject("Background widget host");
        hostObject.SetActive(false);
        var instance = typeof(AdeSkinHost).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = instance.GetValue(null);
        try
        {
            AdeSkinDialogContent dialog = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                dialog = root.GetComponentInChildren<AdeSkinDialogContent>(true);
                if (dialog) break;
            }
            Assert.That(dialog, Is.Not.Null);
            var container = dialog.ExternalBackgroundContainer;
            for (Transform node = container; node; node = node.parent) node.gameObject.SetActive(true);
            var option = UnityEngine.Object.Instantiate(dialog.BackgroundOptionPrefab, container).GetComponent<AdeSkinBackgroundOption>();
            string path = Path.Combine(folder, "widget.jpg");
            var sprite = Add("previews", path, Time.frameCount);
            var host = hostObject.AddComponent<AdeSkinHost>();
            host.ExternalBackgrounds = new Dictionary<string, AdeSkinHost.Labelled<Sprite>>
            {
                { "widget", new AdeSkinHost.Labelled<Sprite> { label = path } }
            };
            typeof(AdeSkinHost).GetField("backgroundImages", Members).SetValue(host, cache);
            instance.SetValue(null, host);
            option.Initialize("widget", true, null);
            for (int i = 0; i < 3; i++) Canvas.ForceUpdateCanvases();
            var viewport = (RectTransform)typeof(AdeSkinBackgroundOption).GetField("viewport", Members).GetValue(option);
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            TestContext.WriteLine("Viewport " + viewport.name + " rect=" + viewport.rect + " world=" + string.Join(", ", corners));
            option.Preview.rectTransform.GetWorldCorners(corners);
            TestContext.WriteLine("Preview rect=" + option.Preview.rectTransform.rect + " world=" + string.Join(", ", corners));
            typeof(AdeSkinBackgroundOption).GetMethod("LateUpdate", Members).Invoke(option, null);
            Assert.That(option.Preview.sprite, Is.SameAs(sprite), "A visible scene widget must reach the cache and assign its returned image.");
            Assert.That(option.Preview.enabled, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(hostObject);
            instance.SetValue(null, previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void BackgroundWidgetsInitializedOnHiddenCustomPageKeepTheirScrollViewport()
    {
        var scene = EditorSceneManager.OpenScene("Assets/_Scenes/ArcEditor.unity", OpenSceneMode.Additive);
        var hostObject = new GameObject("Hidden custom background host");
        hostObject.SetActive(false);
        var instance = typeof(AdeSkinHost).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = instance.GetValue(null);
        try
        {
            AdeSkinDialogContent dialog = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                dialog = root.GetComponentInChildren<AdeSkinDialogContent>(true);
                if (dialog) break;
            }
            Assert.That(dialog, Is.Not.Null);
            var customPage = (GameObject)typeof(AdeSkinDialogContent).GetField("customPage", Members).GetValue(dialog);
            customPage.SetActive(false);
            var host = hostObject.AddComponent<AdeSkinHost>();
            host.ExternalBackgrounds = new Dictionary<string, AdeSkinHost.Labelled<Sprite>>();
            for (int i = 0; i < 158; i++)
                host.ExternalBackgrounds.Add("Background " + i, new AdeSkinHost.Labelled<Sprite> { label = Path.Combine(folder, i + ".jpg") });
            typeof(AdeSkinHost).GetField("backgroundImages", Members).SetValue(host, cache);
            instance.SetValue(null, host);
            typeof(AdeSkinDialogContent).GetMethod("LoadExternalBackgroundOptions", Members).Invoke(dialog, null);
            var options = (Dictionary<string, AdeSkinBackgroundOption>)typeof(AdeSkinDialogContent).GetField("ExternalBackgroundButtons", Members).GetValue(dialog);
            var scroll = dialog.ExternalBackgroundContainer.GetComponentInParent<ScrollRect>(true);
            Assert.That(options.Count, Is.EqualTo(158));
            foreach (var option in options.Values)
            {
                var viewport = (RectTransform)typeof(AdeSkinBackgroundOption).GetField("viewport", Members).GetValue(option);
                Assert.That(viewport == scroll.viewport, Is.True,
                    "Initialization on hidden Custom resolved " + (viewport ? viewport.name : "no viewport") + " instead of its scroll mask.");
            }
            for (Transform node = dialog.ExternalBackgroundContainer; node; node = node.parent) node.gameObject.SetActive(true);
            for (int i = 0; i < 3; i++) Canvas.ForceUpdateCanvases();
            AdeSkinBackgroundOption firstVisible = null;
            foreach (var option in options.Values)
                if ((bool)typeof(AdeSkinBackgroundOption).GetMethod("IsVisible", Members).Invoke(option, null)) { firstVisible = option; break; }
            Assert.That(firstVisible, Is.Not.Null);
            string path = host.ExternalBackgrounds[firstVisible.BackgroundName].label;
            var sprite = Add("previews", path, Time.frameCount);
            typeof(AdeSkinBackgroundOption).GetMethod("LateUpdate", Members).Invoke(firstVisible, null);
            Assert.That(firstVisible.Preview.sprite, Is.SameAs(sprite));
            Assert.That(firstVisible.Preview.enabled, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(hostObject);
            instance.SetValue(null, previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void CatalogingManyImagesDoesNotDecodeOrAllocateTextures()
    {
        for (int i = 0; i < 158; i++) File.WriteAllBytes(Path.Combine(folder, i + ".jpg"), new byte[] { 0xff, 0xd8 });
        File.WriteAllText(Path.Combine(folder, "ignored.txt"), "not a background");
        int before = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        var result = (Dictionary<string, AdeSkinHost.Labelled<Sprite>>)typeof(AdeSkinHost)
            .GetMethod("DiscoverBackgrounds", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { folder });
        Assert.That(result.Count, Is.EqualTo(158));
        foreach (var item in result.Values)
        {
            Assert.That(item.value, Is.Null);
            Assert.That(File.Exists(item.label), Is.True);
        }
        Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(before));
    }

    [Test]
    public void PlayingChartDoesNotStarveFirstVisiblePreviewDownload()
    {
        var instance = typeof(ArcGameplayManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = instance.GetValue(null);
        var go = new GameObject("Preview playback policy test");
        go.SetActive(false); // No scene managers or rendering are initialized for this scheduling test.
        var ownerObject = new GameObject("Preview cache owner");
        IEnumerator loader = null;
        try
        {
            var gameplay = go.AddComponent<ArcGameplayManager>();
            gameplay.IsPlaying = true;
            instance.SetValue(null, gameplay);
            var host = ownerObject.AddComponent<AdeSkinHost>();
            host.enabled = false;
            ((IDisposable)cache).Dispose();
            cache = Activator.CreateInstance(CacheType, Members, null, new object[] { host }, null);
            string path = Path.Combine(folder, "visible.png");
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGOssJFjYGBgYgADAApcANaqWE86AAAAAElFTkSuQmCC"));
            QueuePreview(path, Time.frameCount);
            loader = (IEnumerator)Call("LoadPreviews");
            Assert.That(loader.MoveNext(), Is.True); // Initial layout-settling frame.
            Assert.That(loader.MoveNext(), Is.True);
            Assert.That(CacheType.GetProperty("ActiveOriginalJobs", Members).GetValue(cache), Is.EqualTo(1),
                "Playback must start the image request rather than wait indefinitely for pause.");
        }
        finally
        {
            ((IDisposable)cache).Dispose(); // Aborts the request without decoding/rendering the test image.
            (loader as IDisposable)?.Dispose();
            instance.SetValue(null, previous);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(ownerObject);
        }
    }

    [Test]
    public void RapidScrollPrioritizesFreshVisibleRowsAndDropsOffscreenRequests()
    {
        QueuePreview("offscreen", Time.frameCount - 10);
        QueuePreview("previous-row", Time.frameCount - 1);
        QueuePreview("just-revealed", Time.frameCount);
        Assert.That(Call("NextVisiblePreview"), Is.EqualTo("just-revealed"));
        Assert.That(((HashSet<string>)Field("queued")).Contains("offscreen"), Is.False);
        Assert.That(((Dictionary<string, int>)Field("visible")).ContainsKey("offscreen"), Is.False);
        Assert.That(Call("NextVisiblePreview"), Is.EqualTo("previous-row"));
    }

    [Test]
    public void VisibleRequestsPrecedePrefetchAndNearestNeighborsComeFirst()
    {
        QueueNearby("far", Time.frameCount, 100);
        QueueNearby("near", Time.frameCount, 10);
        QueuePreview("visible", Time.frameCount - 1);
        Assert.That(Call("NextVisiblePreview"), Is.EqualTo("visible"));
        Assert.That(Call("NextVisiblePreview"), Is.EqualTo("near"));
        Assert.That(Call("NextVisiblePreview"), Is.EqualTo("far"));
    }

    [Test]
    public void PrefetchAdmissionReservesCapacityForVisibleRowsAndClosestNeighbors()
    {
        for (int i = 0; i < 18; i++) ((Dictionary<string, int>)Field("visible"))["visible " + i] = Time.frameCount;
        for (int i = 0; i < 40; i++) QueueNearby("nearby " + i, Time.frameCount, i);
        for (int i = 0; i < 40; i++)
            Assert.That(Call("CanPrefetch", "nearby " + i), Is.EqualTo(i < 30));
    }

    [Test]
    public void PrefetchHitsDoNotRenewRecencyAndVisiblePreviewsSurviveEviction()
    {
        string path = "visible";
        var displayed = Add("previews", path, -100);
        var prefetched = Add("previews", "prefetched", -99);
        Assert.That(Call("Preview", "prefetched", true, 10f), Is.SameAs(prefetched));
        var entry = ((IDictionary)Field("previews"))["prefetched"];
        Assert.That(entry.GetType().GetField("Used").GetValue(entry), Is.EqualTo(-99));
        ((Dictionary<string, int>)Field("visible"))[path] = Time.frameCount;
        for (int i = 2; i < 49; i++) Add("previews", "old " + i, i);
        Call("TrimPreviews");
        Assert.That(((IDictionary)Field("previews")).Count, Is.EqualTo(48));
        Assert.That((bool)displayed, Is.True);
    }

    [Test]
    public void InvalidationRetainsOriginalLeaseUntilReadbackCleanup()
    {
        var jobType = CacheType.GetNestedType("PreviewJob", BindingFlags.NonPublic);
        var job = Activator.CreateInstance(jobType, true);
        jobType.GetField("Resizing").SetValue(job, true);
        ((IList)Field("jobs")).Add(job);
        Call("AcquireOriginal", job);
        Call("Invalidate");
        Assert.That(CacheType.GetProperty("ActiveOriginalJobs", Members).GetValue(cache), Is.EqualTo(1));
        Assert.That(CacheType.GetProperty("ActivePreviewJobs", Members).GetValue(cache), Is.EqualTo(1));
        jobType.GetField("Resizing").SetValue(job, false);
        Call("ReleaseOriginal", job);
        Call("ReleaseOriginal", job);
        Call("Invalidate");
        Assert.That(CacheType.GetProperty("ActiveOriginalJobs", Members).GetValue(cache), Is.Zero);
        Assert.That(CacheType.GetProperty("ActivePreviewJobs", Members).GetValue(cache), Is.Zero);
    }

    [Test]
    public void ResizeSetupFailureReleasesSourceAndOriginalLeaseExactlyOnce()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Temporary render targets require a graphics device.");
        var jobType = CacheType.GetNestedType("PreviewJob", BindingFlags.NonPublic);
        var job = Activator.CreateInstance(jobType, true);
        Call("AcquireOriginal", job);
        int completed = 0;
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        const string path = "setup failure";
        LogAssert.Expect(LogType.Warning, "Background preview could not be read; a later request will retry: setup failure (injected draw failure)");
        Call("ResizePreview", source, path, null, (Action)(() => { completed++; Call("ReleaseOriginal", job); }),
            (Action<RenderTexture>)(_ => throw new InvalidOperationException("injected draw failure")));
        Assert.That(completed, Is.EqualTo(1));
        Assert.That((bool)source, Is.False);
        Assert.That(CacheType.GetProperty("ActiveOriginalJobs", Members).GetValue(cache), Is.Zero);
        Assert.That(Call("CanRequestPreview", path, Time.realtimeSinceStartup), Is.EqualTo(false));
    }

    [Test]
    public void SlowDiskDoesNotQueueMoreThanTwoEncodedPreviews()
    {
        var blocker = new TaskCompletionSource<bool>();
        SetField("diskWork", blocker.Task);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            for (int i = 0; i < 4; i++) Call("SavePreview", texture, Path.Combine(folder, i + ".png"));
            Assert.That(Field("pendingDiskWrites"), Is.EqualTo(2));
            blocker.SetResult(true);
            Assert.That(((Task)Field("diskWork")).Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Field("pendingDiskWrites"), Is.Zero);
            Assert.That(Directory.GetFiles(folder, "*.png").Length, Is.EqualTo(2));
        }
        finally
        {
            blocker.TrySetResult(true);
            ((Task)Field("diskWork")).Wait(TimeSpan.FromSeconds(5));
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void TransientPreviewFailureRetriesWithBoundedBackoff()
    {
        string path = "temporarily unavailable";
        Call("RecordPreviewFailure", path);
        Assert.That(Call("CanRequestPreview", path, Time.realtimeSinceStartup), Is.EqualTo(false));
        Assert.That(Call("CanRequestPreview", path, Time.realtimeSinceStartup + 31), Is.EqualTo(true));
        for (int i = 0; i < 8; i++) Call("RecordPreviewFailure", path);
        Assert.That(Call("CanRequestPreview", path, Time.realtimeSinceStartup + 31), Is.EqualTo(true),
            "A temporarily failed image must never be permanently blacklisted.");
    }

    [Test]
    public void DestroyedPreviewIsRemovedInsteadOfRemainingAnEmptyCacheHit()
    {
        string path = "released preview";
        var sprite = Add("previews", path, 0);
        UnityEngine.Object.DestroyImmediate(sprite.texture);
        UnityEngine.Object.DestroyImmediate(sprite);
        Call("RecordPreviewFailure", path); // Prevent starting a real coroutine in this ownership-only test.
        Assert.That(Call("Preview", path), Is.Null);
        Assert.That(((IDictionary)Field("previews")).Contains(path), Is.False);
    }

    [Test]
    public void FullImageEvictionIsBoundedAndPreservesTheDisplayedImage()
    {
        var oldest = Add("full", "oldest", 0);
        var retained = Add("full", "retained", 1);
        var displayed = Add("full", "displayed", 2);
        var oldTexture = oldest.texture;
        CacheType.GetMethod("Trim", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Field("full"), 2, displayed });
        Assert.That(((IDictionary)Field("full")).Count, Is.EqualTo(2));
        Assert.That(!oldest && !oldTexture, Is.True, "Eviction releases both native objects.");
        Assert.That(retained && displayed, Is.True);
    }

    [Test]
    public void PreviewEvictionReleasesNativeTexturesAndSprites()
    {
        var first = Add("previews", "first", 0);
        var second = Add("previews", "second", 1);
        for (int i = 2; i < 50; i++) Add("previews", i.ToString(), i);
        CacheType.GetMethod("Trim", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Field("previews"), 48, null });
        Assert.That(((IDictionary)Field("previews")).Count, Is.EqualTo(48));
        Assert.That(!first && !second, Is.True);
    }

    [Test]
    public void ReloadKeepsCurrentImageUntilReplacementAndRejectsOldSelection()
    {
        var displayed = Add("full", "displayed", 0);
        var unused = Add("full", "unused", 1);
        SetField("active", displayed);
        int oldVersion = (int)Field("selectionVersion");
        Call("Invalidate");
        Assert.That(displayed, Is.Not.Null);
        Assert.That((bool)displayed, Is.True, "Reload must not blank the displayed image while its replacement decodes.");
        Assert.That((bool)unused, Is.False);
        Assert.That(((IDictionary)Field("full")).Count, Is.Zero);
        Assert.That(Call("IsCurrentSelection", oldVersion), Is.EqualTo(false));
        Call("Invalidate"); // Repeated catalog/skin reloads must keep the same retained image safe.
        Assert.That((bool)displayed, Is.True);
        var replacement = Add("full", "replacement", 3);
        Sprite applied = null;
        Call("Select", "replacement", (Action<Sprite>)(value => applied = value));
        Assert.That(applied, Is.SameAs(replacement));
        Assert.That((bool)displayed, Is.False);
        Assert.That(Field("active"), Is.SameAs(replacement));
    }

    [Test]
    public void ReturningToBuiltInBackgroundReleasesRetiredImageAndCancelsSelection()
    {
        var displayed = Add("full", "displayed", 0);
        SetField("active", displayed);
        Call("Invalidate");
        int version = (int)Field("selectionVersion");
        Call("UseSharedBackground");
        Assert.That((bool)displayed, Is.False);
        Assert.That(Call("IsCurrentSelection", version), Is.EqualTo(false));
        Assert.That(Field("active"), Is.Null);
    }

    [Test]
    public void CloudBackgroundReleasesRetiredImageAndClearsSelectedImagePin()
    {
        var displayed = Add("full", "displayed", 0);
        SetField("active", displayed);
        Call("Invalidate");
        int version = (int)Field("selectionVersion");
        var go = new GameObject("Background ownership test");
        try
        {
            var host = go.AddComponent<AdeSkinHost>();
            var field = typeof(AdeSkinHost).GetField("backgroundImages", Members);
            (field.GetValue(host) as IDisposable)?.Dispose();
            field.SetValue(host, cache);
            typeof(AdeSkinHost).GetMethod("CancelBackgroundSelection", Members).Invoke(host, null);
            Assert.That((bool)displayed, Is.False);
            Assert.That(Field("active"), Is.Null);
            Assert.That(Field("retired"), Is.Null);
            Assert.That(Call("IsCurrentSelection", version), Is.EqualTo(false));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private Sprite Add(string dictionary, string key, int used)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
        sprites.Add(sprite);
        var entryType = CacheType.GetNestedType("Entry", BindingFlags.NonPublic);
        var entry = Activator.CreateInstance(entryType, true);
        entryType.GetField("Sprite").SetValue(entry, sprite);
        entryType.GetField("Used").SetValue(entry, used);
        ((IDictionary)Field(dictionary)).Add(key, entry);
        return sprite;
    }

    private void QueuePreview(string path, int visibleFrame)
    {
        ((Queue<string>)Field("queue")).Enqueue(path);
        ((HashSet<string>)Field("queued")).Add(path);
        ((Dictionary<string, int>)Field("visible"))[path] = visibleFrame;
    }

    private void QueueNearby(string path, int frame, float distance)
    {
        var type = CacheType.GetNestedType("Nearby", BindingFlags.NonPublic);
        var demand = Activator.CreateInstance(type, true);
        type.GetField("Frame").SetValue(demand, frame);
        type.GetField("Distance").SetValue(demand, distance);
        ((IDictionary)Field("nearby"))[path] = demand;
        ((Queue<string>)Field("queue")).Enqueue(path);
        ((HashSet<string>)Field("queued")).Add(path);
    }

    private object Field(string name) => CacheType.GetField(name, Members).GetValue(cache);
    private void SetField(string name, object value) => CacheType.GetField(name, Members).SetValue(cache, value);
    private object Call(string name, params object[] arguments)
    {
        var method = CacheType.GetMethod(name, Members);
        var parameters = method.GetParameters();
        int supplied = arguments.Length;
        if (supplied < parameters.Length)
        {
            Array.Resize(ref arguments, parameters.Length);
            for (int i = supplied; i < parameters.Length; i++) arguments[i] = parameters[i].DefaultValue;
        }
        return method.Invoke(cache, arguments);
    }
}

public class BackgroundPreviewWidgetIntegrationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [UnityTest]
    public IEnumerator CustomSceneCatalogRendersLoadedPreviewsWhilePausedAndPlaying()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Final UI pixels require a graphics device.");
        yield return new EnterPlayMode();
        Scene scene = default;
        RenderTexture output = null;
        GameObject fixtureTheme = null;
        string folder = Path.Combine(Path.GetTempPath(), "AffdataCustomWidgetTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> freezeStartup = (loaded, mode) =>
        {
            if (loaded.name != "ArcEditor") return;
            scene = loaded;
            // Keep this an image-browser fixture: real UI callbacks run, while unrelated editor,
            // chart, account and audio managers never reach Start/Update.
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour.GetType().Assembly != typeof(AdeSkinHost).Assembly) continue;
                behaviour.enabled = false;
                if (behaviour is AdeSoundDialogContent sound)
                    typeof(AdeSoundDialogContent).GetField("preferences", Members).SetValue(sound, new SoundPreferences());
            }
        };
        SceneManager.sceneLoaded += freezeStartup;
        try
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Scenes/ArcEditor.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            SceneManager.sceneLoaded -= freezeStartup;
            AdeSkinDialogContent dialog = null;
            AdeSkinHost host = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!dialog) dialog = root.GetComponentInChildren<AdeSkinDialogContent>(true);
                if (!host) host = root.GetComponentInChildren<AdeSkinHost>(true);
            }
            Assert.That(dialog && host, Is.True);
            if (!AdeUiTheme.Instance)
            {
                fixtureTheme = new GameObject("Background preview theme");
                fixtureTheme.AddComponent<AdeUiTheme>().enabled = false;
            }
            var customPage = (GameObject)typeof(AdeSkinDialogContent).GetField("customPage", Members).GetValue(dialog);
            customPage.SetActive(false);
            host.skinData = new AdeSkinHost.SkinDatas
            {
                ThemeDatas = new Dictionary<string, AdeSkinHost.WithSideData<AdeSkinHost.ThemeSideData>>(),
                NoteDatas = new Dictionary<string, AdeSkinHost.WithSideData<AdeSkinHost.NoteSideData>>(),
                BackgroundDatas = new Dictionary<string, AdeSkinHost.BackgroundData>
                {
                    { "DefaultLight", new AdeSkinHost.BackgroundData { background = new AdeSkinHost.Labelled<Sprite> { value = host.rawDefaultData.BackgroundLight } } },
                    { "DefaultConflict", new AdeSkinHost.BackgroundData { background = new AdeSkinHost.Labelled<Sprite> { value = host.rawDefaultData.BackgroundConflict } } }
                }
            };
            var source = new Texture2D(1920, 1440, TextureFormat.RGB24, false);
            var pixels = new Color32[1920 * 1440];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(220, 35, 20, 255);
            source.SetPixels32(pixels);
            source.Apply();
            byte[] jpeg = source.EncodeToJPG();
            UnityEngine.Object.Destroy(source);
            host.ExternalBackgrounds = new Dictionary<string, AdeSkinHost.Labelled<Sprite>>();
            for (int i = 0; i < 158; i++)
            {
                string name = "Background " + i;
                string path = Path.Combine(folder, i + ".jpg");
                File.WriteAllBytes(path, jpeg);
                host.ExternalBackgrounds.Add(name, new AdeSkinHost.Labelled<Sprite> { label = path });
            }
            typeof(AdeSkinDialogContent).GetMethod("LoadSkinOptions", Members).Invoke(dialog, null);
            typeof(AdeSkinDialogContent).GetMethod("LoadExternalBackgroundOptions", Members).Invoke(dialog, null);
            typeof(AdeSkinDialogContent).GetField("preference", Members).SetValue(dialog, new SkinPreference { Customize = true });
            typeof(AdeSkinDialogContent).GetMethod("UpdateTabs", Members).Invoke(dialog, null);
            for (Transform node = dialog.ExternalBackgroundContainer; node; node = node.parent)
            {
                node.gameObject.SetActive(true);
                if (node.TryGetComponent<CanvasGroup>(out var group)) group.alpha = 1;
            }
            var sideCard = (AdeRightSideCard)typeof(AdeSkinDialogContent).GetField("sideCard", Members).GetValue(dialog);
            var view = (RectTransform)sideCard.transform.Find("View");
            view.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 760);
            typeof(AdeSkinDialogContent).GetField("skinView", Members).SetValue(dialog, view);
            typeof(AdeSkinDialogContent).GetMethod("LateUpdate", Members).Invoke(dialog, null);
            var canvas = dialog.ExternalBackgroundContainer.GetComponentInParent<Canvas>(true);
            var camera = canvas.worldCamera;
            output = new RenderTexture(1440, 1080, 24);
            camera.targetTexture = output;
            var scroll = dialog.ExternalBackgroundContainer.GetComponentInParent<ScrollRect>(true);
            var options = (Dictionary<string, AdeSkinBackgroundOption>)typeof(AdeSkinDialogContent).GetField("ExternalBackgroundButtons", Members).GetValue(dialog);
            var imageCache = typeof(AdeSkinHost).GetField("backgroundImages", Members).GetValue(host);
            Assert.That(options.Count, Is.EqualTo(158));
            foreach (var option in options.Values) option.enabled = true;
            foreach (bool playing in new[] { false, true })
            {
                ArcGameplayManager.Instance.IsPlaying = playing;
                imageCache.GetType().GetMethod("Invalidate", Members).Invoke(imageCache, null);
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = playing ? .25f : .75f;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var visibleRows = VisibleOptions(options, scroll.viewport);
                Assert.That(visibleRows.Count, Is.GreaterThanOrEqualTo(6), "Benchmark a viewport containing several rows.");
                var visible = visibleRows[0];
                float started = Time.realtimeSinceStartup, firstReady = -1;
                float deadline = started + 20;
                while (!AllReady(visibleRows) && Time.realtimeSinceStartup < deadline)
                {
                    if (firstReady < 0)
                        foreach (var option in visibleRows) if (option.Preview.sprite) { firstReady = Time.realtimeSinceStartup; break; }
                    yield return null;
                }
                string state = playing ? "playing" : "paused";
                Assert.That(AllReady(visibleRows), Is.True, state + ": all visible images must finish loading.");
                TestContext.WriteLine(state + " cold visible=" + visibleRows.Count + " firstMs=" +
                    ((firstReady < 0 ? Time.realtimeSinceStartup : firstReady) - started) * 1000 + " allMs=" +
                    (Time.realtimeSinceStartup - started) * 1000);
                TestContext.WriteLine(state + " image=" + visible.BackgroundName + " enabled=" + visible.Preview.enabled +
                    " color=" + visible.Preview.color + " renderer=" + visible.Preview.canvasRenderer.GetColor() +
                    " alpha=" + visible.Preview.canvasRenderer.GetInheritedAlpha() + " culled=" + visible.Preview.canvasRenderer.cull +
                    " viewport=" + ((RectTransform)typeof(AdeSkinBackgroundOption).GetField("viewport", Members).GetValue(visible)).name +
                    " previews=" + imageCache.GetType().GetProperty("PreviewCount", Members).GetValue(imageCache));
                Assert.That(visible.Preview.enabled && visible.Preview.sprite, Is.True, state + ": actual Custom widget must receive an image.");
                Assert.That(visible.Preview.sprite.texture.width, Is.EqualTo(256));
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Vector2 samplePosition = RectTransformUtility.WorldToScreenPoint(camera, visible.Preview.rectTransform.TransformPoint(visible.Preview.rectTransform.rect.center));
                var previous = RenderTexture.active;
                var sample = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = output;
                    sample.ReadPixels(new Rect(Mathf.Floor(samplePosition.x), Mathf.Floor(samplePosition.y), 1, 1), 0, 0, false);
                    Color pixel = sample.GetPixel(0, 0);
                    TestContext.WriteLine(state + " final UI pixel=" + pixel + " at=" + samplePosition);
                    Assert.That(pixel.r, Is.GreaterThan(pixel.g * 2), state + ": final UI must show the source image rather than a gray surface.");
                    Assert.That(pixel.r, Is.GreaterThan(.4f));
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.Destroy(sample);
                }
                Assert.That((int)imageCache.GetType().GetProperty("PreviewCount", Members).GetValue(imageCache), Is.LessThanOrEqualTo(48));

                // Let the bounded nearby band finish, then reveal one already-prefetched row.
                deadline = Time.realtimeSinceStartup + 20;
                do
                {
                    yield return null;
                } while (!CacheIdle(imageCache) && Time.realtimeSinceStartup < deadline);
                Assert.That(CacheIdle(imageCache), Is.True, "A stationary viewport must finish prefetch rather than thrash the 48-image cache.");
                int originals = CacheMetric(imageCache, "OriginalLoadCount"), disks = CacheMetric(imageCache, "DiskLoadCount");
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(CacheMetric(imageCache, "OriginalLoadCount"), Is.EqualTo(originals));
                float scrollRange = scroll.content.rect.height - scroll.viewport.rect.height;
                scroll.verticalNormalizedPosition -= 160 / scrollRange;
                Canvas.ForceUpdateCanvases();
                started = Time.realtimeSinceStartup;
                var revealed = VisibleOptions(options, scroll.viewport);
                foreach (var option in revealed) typeof(AdeSkinBackgroundOption).GetMethod("LateUpdate", Members).Invoke(option, null);
                Assert.That(AllReady(revealed), Is.True, "A prefetched row must supply every Sprite in its next LateUpdate.");
                Assert.That(CacheMetric(imageCache, "OriginalLoadCount"), Is.EqualTo(originals));
                Assert.That(CacheMetric(imageCache, "DiskLoadCount"), Is.EqualTo(disks));
                TestContext.WriteLine(state + " prefetched scroll visible=" + revealed.Count + " readyMs=" +
                    (Time.realtimeSinceStartup - started) * 1000 + " newDownloads=0");

                deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; } while (!CacheIdle(imageCache) && Time.realtimeSinceStartup < deadline);
                var diskWork = (Task)imageCache.GetType().GetField("diskWork", Members).GetValue(imageCache);
                while (!diskWork.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(diskWork.IsCompleted, Is.True);
                visibleRows = VisibleOptions(options, scroll.viewport);
                // Evict RAM only, preserving the just-generated small disk files.
                foreach (var option in options.Values) { option.Preview.sprite = null; option.Preview.enabled = false; }
                imageCache.GetType().GetMethod("Invalidate", Members).Invoke(imageCache, null);
                originals = CacheMetric(imageCache, "OriginalLoadCount");
                disks = CacheMetric(imageCache, "DiskLoadCount");
                started = Time.realtimeSinceStartup;
                deadline = started + 20;
                while (!AllReady(visibleRows) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(AllReady(visibleRows), Is.True);
                Assert.That(CacheMetric(imageCache, "OriginalLoadCount"), Is.EqualTo(originals), "Warm scrolling should decode the small disk previews only.");
                TestContext.WriteLine(state + " warm disk visible=" + visibleRows.Count + " allMs=" +
                    (Time.realtimeSinceStartup - started) * 1000 + " diskLoads=" + (CacheMetric(imageCache, "DiskLoadCount") - disks) +
                    " peakJobs=" + CacheMetric(imageCache, "PeakPreviewJobs") + " peakOriginals=" + CacheMetric(imageCache, "PeakOriginalJobs") +
                    " resident=" + CacheMetric(imageCache, "PreviewCount"));
                Assert.That(CacheMetric(imageCache, "PeakPreviewJobs"), Is.LessThanOrEqualTo(2));
                Assert.That(CacheMetric(imageCache, "PeakOriginalJobs"), Is.LessThanOrEqualTo(1));
                Assert.That(CacheMetric(imageCache, "PreviewCount"), Is.LessThanOrEqualTo(48));
            }
        }
        finally
        {
            SceneManager.sceneLoaded -= freezeStartup;
            if (scene.IsValid())
                foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            if (fixtureTheme) UnityEngine.Object.DestroyImmediate(fixtureTheme);
            if (output) UnityEngine.Object.DestroyImmediate(output);
            Directory.Delete(folder, true);
        }
        yield return new ExitPlayMode();
    }

    private static int CacheMetric(object cache, string name)
        => (int)cache.GetType().GetProperty(name, Members).GetValue(cache);

    private static bool CacheIdle(object cache)
        => CacheMetric(cache, "ActivePreviewJobs") == 0
            && ((Queue<string>)cache.GetType().GetField("queue", Members).GetValue(cache)).Count == 0;

    private static bool AllReady(List<AdeSkinBackgroundOption> options)
    {
        foreach (var option in options) if (!option.Preview.enabled || !option.Preview.sprite) return false;
        return true;
    }

    private static List<AdeSkinBackgroundOption> VisibleOptions(Dictionary<string, AdeSkinBackgroundOption> options, RectTransform viewport)
    {
        var result = new List<AdeSkinBackgroundOption>();
        foreach (var option in options.Values)
        {
            var rect = option.Preview.rectTransform;
            Vector3 center = viewport.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            if (viewport.rect.Contains(center)) result.Add(option);
        }
        return result;
    }

    [UnityTest]
    public IEnumerator VisiblePrefabLoadsAndDisplaysImageWhilePausedAndPlaying()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Image decode, resize and rendered pixels require a graphics device.");
        yield return new EnterPlayMode();
        var root = new GameObject("Background preview integration");
        string folder = Path.Combine(Path.GetTempPath(), "AffdataWidgetTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var gameplayInstance = typeof(ArcGameplayManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousGameplay = gameplayInstance.GetValue(null);
        RenderTexture output = null;
        try
        {
            var host = Child("Host", root.transform).AddComponent<AdeSkinHost>();
            host.enabled = false; // The fixture supplies its catalog; do not initialize the application skin.
            var gameplayObject = Child("Playback state", root.transform);
            gameplayObject.SetActive(false);
            var gameplay = gameplayObject.AddComponent<ArcGameplayManager>();
            gameplayInstance.SetValue(null, gameplay);
            var camera = Child("Camera", root.transform).AddComponent<Camera>();
            output = new RenderTexture(512, 512, 24);
            camera.targetTexture = output;
            var canvas = Child("Canvas", root.transform, typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100;
            var scroll = Child("Scroll", canvas.transform, typeof(RectTransform), typeof(ScrollRect)).GetComponent<ScrollRect>();
            ((RectTransform)scroll.transform).sizeDelta = new Vector2(400, 400);
            scroll.viewport = (RectTransform)Child("Viewport", scroll.transform, typeof(RectTransform), typeof(RectMask2D)).transform;
            scroll.viewport.sizeDelta = new Vector2(400, 400);
            scroll.content = (RectTransform)Child("Content", scroll.viewport, typeof(RectTransform)).transform;
            scroll.content.sizeDelta = new Vector2(400, 400);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Editor/BackgroundOption.prefab");
            Assert.That(prefab, Is.Not.Null);
            foreach (bool playing in new[] { false, true })
            {
                gameplay.IsPlaying = playing;
                string name = playing ? "playing" : "paused";
                string path = Path.Combine(folder, name + ".jpg");
                var image = new Texture2D(320, 240, TextureFormat.RGB24, false);
                var pixels = new Color32[320 * 240];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(220, 35, 20, 255);
                image.SetPixels32(pixels);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToJPG());
                UnityEngine.Object.Destroy(image);
                host.ExternalBackgrounds = new Dictionary<string, AdeSkinHost.Labelled<Sprite>>
                {
                    { name, new AdeSkinHost.Labelled<Sprite> { label = path } }
                };
                var option = UnityEngine.Object.Instantiate(prefab, scroll.content).GetComponent<AdeSkinBackgroundOption>();
                option.Initialize(name, true, null);
                Canvas.ForceUpdateCanvases();
                float deadline = Time.realtimeSinceStartup + 10;
                while (!option.Preview.sprite && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(option.Preview.enabled && option.Preview.sprite, Is.True,
                    name + ": the real LateUpdate must request, receive and display the decoded preview.");
                Assert.That(option.Preview.sprite.texture.width, Is.EqualTo(256));
                Assert.That(option.Preview.sprite.texture.height, Is.EqualTo(192));
                var sample = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var readback = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(option.Preview.sprite.texture, sample);
                    RenderTexture.active = sample;
                    readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, false);
                    Color pixel = readback.GetPixel(8, 8);
                    Assert.That(pixel.r, Is.GreaterThan(pixel.g * 2), name + ": loaded texture must contain the source image, not a gray placeholder.");
                    Assert.That(pixel.r, Is.GreaterThan(.4f));
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(sample);
                    UnityEngine.Object.Destroy(readback);
                }
                UnityEngine.Object.Destroy(option.gameObject);
                yield return null;
            }
        }
        finally
        {
            gameplayInstance.SetValue(null, previousGameplay);
            UnityEngine.Object.DestroyImmediate(root);
            if (output) UnityEngine.Object.DestroyImmediate(output);
            Directory.Delete(folder, true);
        }
        yield return new ExitPlayMode();
    }

    private static GameObject Child(string name, Transform parent, params Type[] components)
    {
        var child = new GameObject(name, components);
        child.transform.SetParent(parent, false);
        return child;
    }
}

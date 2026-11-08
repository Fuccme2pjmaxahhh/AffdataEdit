using System.Reflection;
using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class ArcadeAlphaAppearanceTests
{
    // Fixed values taken from the inspected AA 1.12.2 renderer, not AE's former formulas.
    [TestCase(80, 1)]
    [TestCase(85, .5f)]
    [TestCase(90, 0)]
    [TestCase(95, 0)]
    public void TapFadeUsesAaDistance(float distance, float alpha)
        => Assert.That(ArcadeAlphaAppearance.TapAlpha(distance), Is.EqualTo(alpha).Within(.000001f));

    [Test]
    public void TapLengthRetainsSubCentimetrePrecision()
    {
        Assert.That(ArcadeAlphaAppearance.TapLength(1, 165), Is.EqualTo(2.051f).Within(.000001f));
        Assert.That(ArcadeAlphaAppearance.TapLength(-100, 165), Is.Zero);
    }

    [TestCase(84, false, 1)]
    [TestCase(90, false, 150f / 255f)]
    [TestCase(100, false, 75f / 255f)]
    [TestCase(85, true, 150f / 255f)]
    public void ArcFadeUsesAaSegmentOpacity(float distance, bool zeroDuration, float alpha)
        => Assert.That(ArcadeAlphaAppearance.ArcDistanceAlpha(distance, zeroDuration), Is.EqualTo(alpha).Within(.000001f));

    [Test]
    public void SameSideArcsUseExtentInsteadOfSumAndRespectEnwiden()
    {
        var go = new GameObject("AA tilt test");
        try
        {
            var manager = go.AddComponent<ArcArcManager>();
            var record = typeof(ArcArcManager).GetMethod("RecordJudgePosition", BindingFlags.Instance | BindingFlags.NonPublic);
            record.Invoke(manager, new object[] { 1f });
            record.Invoke(manager, new object[] { 2f });
            record.Invoke(manager, new object[] { 1f });
            Assert.That(manager.ArcJudgePosRight, Is.EqualTo(2));
            Assert.That(ArcadeAlphaAppearance.TiltTarget(manager.ArcJudgePosLeft, manager.ArcJudgePosRight, 1),
                Is.EqualTo(-.0235294118f).Within(.000001f));
            Assert.That(ArcadeAlphaAppearance.TiltTarget(0, 3, 1.5f),
                Is.EqualTo(-.0235294118f).Within(.000001f));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void FollowAndRecenterUseAaFrameResponse()
    {
        Assert.That(ArcadeAlphaAppearance.StepTilt(0, .04f, 1f / 60), Is.EqualTo(.0064f).Within(.000001f));
        Assert.That(ArcadeAlphaAppearance.StepTilt(0, -.04f, 1f / 60), Is.EqualTo(-.0032f).Within(.000001f));
        Assert.That(ArcadeAlphaAppearance.StepTilt(.04f, 0, 1f / 60), Is.EqualTo(.0368f).Within(.000001f));
        Assert.That(ArcadeAlphaAppearance.StepTilt(.00005f, 0, 1f / 60), Is.Zero);
    }

    [TestCase(1280, 720, 50)]
    [TestCase(1920, 1200, 55)]
    [TestCase(1024, 768, 65)]
    [TestCase(2048, 1184, 51.25f)]
    public void ResetCameraEventUsesExactAaLookAt(int width, int height, float fov)
    {
        var go = new GameObject("AA camera test");
        var label = new GameObject("Sky label");
        var texture = new RenderTexture(width, height, 0);
        try
        {
            var camera = go.AddComponent<Camera>();
            camera.targetTexture = texture;
            var manager = go.AddComponent<ArcCameraManager>();
            manager.GameplayCamera = camera;
            manager.SkyInputLabel = label.transform;
            manager.ResetCamera();
            Assert.That(camera.fieldOfView, Is.EqualTo(fov).Within(.0001f));
            Vector3 expected = Matrix4x4.LookAt(camera.transform.position, new Vector3(0, -5.5f, -20), Vector3.up).rotation.eulerAngles;
            Assert.That(Quaternion.Angle(Quaternion.Euler(manager.ResetRotation), Quaternion.Euler(expected)), Is.LessThan(.001f));
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(label); Object.DestroyImmediate(texture); }
    }

    [Test]
    public void EditorViewportUsesAaUniformBorderAndWholeOutputDesignSize()
    {
        var go = new GameObject("AA viewport test", typeof(Camera));
        var texture = new RenderTexture(1920, 1200, 0);
        try
        {
            var camera = go.GetComponent<Camera>();
            camera.targetTexture = texture;
            var manager = go.AddComponent<ArcCameraManager>();
            manager.GameplayCamera = camera;
            var compose = go.AddComponent<Arcade.Compose.ArcadeComposeManager>();
            Assert.That(compose.EditorModeGameplayCameraRect, Is.EqualTo(new Rect(.05f, .05f, .9f, .9f)));
            camera.rect = new Rect(.04f, .05f, .92f, .90f);
            Assert.That(manager.DesignResolution, Is.EqualTo(new Vector2(1280, 800)));
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(texture); }
    }

    [TestCase(1280, 720, 0)]
    [TestCase(1280, 960, 0)]
    [TestCase(1560, 720, -50)]
    public void BackgroundShiftUsesAaCanvasUnits(float width, float height, float offset)
        => Assert.That(ArcadeAlphaAppearance.BackgroundOffset(new Vector2(width, height)), Is.EqualTo(offset).Within(.0001f));

    [Test]
    public void BackgroundCanvasUpdatesAndResetsAfterAspectChange()
    {
        var root = new GameObject("AA background test", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var image = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var cameraObject = new GameObject("Camera", typeof(Camera));
        var wide = new RenderTexture(2560, 1080, 0);
        var normal = new RenderTexture(1920, 1080, 0);
        try
        {
            image.transform.SetParent(root.transform, false);
            var skin = root.AddComponent<ArcSkinManager>();
            skin.Background = image.GetComponent<Image>();
            typeof(ArcSkinManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skin, null);
            Assert.That(root.GetComponent<CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1280, 720)));
            Assert.That(skin.Background.rectTransform.sizeDelta, Is.EqualTo(new Vector2(1280, 960)));
            var camera = cameraObject.GetComponent<Camera>();
            var manager = cameraObject.AddComponent<ArcCameraManager>();
            manager.GameplayCamera = camera;
            typeof(ArcCameraManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            var update = typeof(ArcSkinManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            camera.targetTexture = wide;
            update.Invoke(skin, null);
            Assert.That(skin.Background.rectTransform.anchoredPosition.y, Is.EqualTo(-54.700855f).Within(.0001f));
            camera.targetTexture = normal;
            update.Invoke(skin, null);
            Assert.That(skin.Background.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
        }
        finally
        {
            Object.DestroyImmediate(image); Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(wide); Object.DestroyImmediate(normal);
        }
    }
}

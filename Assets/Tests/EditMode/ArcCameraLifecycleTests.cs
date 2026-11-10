using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;

public class ArcCameraLifecycleTests
{
    [TestCase(1920, 1080)]
    [TestCase(1200, 900)]
    public void BackgroundKeepsScreenBoundsAcrossPerspectiveAndBothOverheadModes(int width, int height)
    {
        var cameraObject = new GameObject("Background projection camera", typeof(Camera));
        var parent = new GameObject("Background parent");
        var background = new GameObject("Background canvas", typeof(RectTransform), typeof(Canvas));
        var output = new RenderTexture(width, height, 0);
        try
        {
            background.transform.SetParent(parent.transform, false);
            parent.transform.localScale = Vector3.one * 2;
            var canvas = background.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1000;
            var camera = cameraObject.GetComponent<Camera>();
            camera.targetTexture = output; camera.farClipPlane = 10000;
            canvas.worldCamera = camera;
            var fit = typeof(ArcSkinManager).Assembly.GetType("Arcade.Gameplay.ArcBackgroundEdgeFill")
                .GetMethod("FitCanvas", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var corners = new Vector3[4];
            var expected = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            for (int mode = 0; mode < 4; mode++)
            {
                camera.orthographic = mode == 1 || mode == 2;
                camera.orthographicSize = mode == 1 ? 15 : 17;
                camera.fieldOfView = 50;
                camera.transform.SetPositionAndRotation(new Vector3(-3, 9, -28),
                    mode == 1 ? Quaternion.Euler(90, 180, 0) : mode == 2 ? Quaternion.Euler(90, 270, 0) : Quaternion.Euler(25, 180, 0));
                camera.ResetProjectionMatrix();
                var projection = camera.projectionMatrix;
                projection.m00 *= .9f; projection.m11 *= .9f;
                camera.projectionMatrix = projection;
                float scale = Mathf.Min(width / 1280f, height / 720f);
                fit.Invoke(null, new object[] { canvas, camera, new Vector2(width / scale, height / scale) });
                ((RectTransform)background.transform).GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(corners[i]);
                    Assert.That(Vector2.Distance(viewport, expected[i]), Is.LessThan(.0001f), $"Mode {mode}, corner {i}");
                    Assert.That(viewport.z, Is.EqualTo(1000).Within(.01f));
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(background); Object.DestroyImmediate(parent);
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(output);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResetCameraToleratesDestroyedSceneDependencies(bool destroyLabel)
    {
        var host = new GameObject("Camera lifecycle test");
        host.SetActive(false);
        var cameraObject = new GameObject("Gameplay camera");
        var labelObject = new GameObject("Sky input label");
        try
        {
            var manager = host.AddComponent<ArcCameraManager>();
            manager.GameplayCamera = cameraObject.AddComponent<Camera>();
            manager.SkyInputLabel = labelObject.transform;
            Object.DestroyImmediate(destroyLabel ? labelObject : cameraObject);
            Assert.DoesNotThrow(manager.ResetCamera);
        }
        finally
        {
            if (cameraObject) Object.DestroyImmediate(cameraObject);
            if (labelObject) Object.DestroyImmediate(labelObject);
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ResetCameraStillRestoresLiveCameraAndLabel()
    {
        var host = new GameObject("Camera reset test");
        host.SetActive(false);
        var cameraObject = new GameObject("Gameplay camera");
        var labelObject = new GameObject("Sky input label");
        try
        {
            var manager = host.AddComponent<ArcCameraManager>();
            manager.GameplayCamera = cameraObject.AddComponent<Camera>();
            manager.GameplayCamera.aspect = 16f / 9f;
            manager.SkyInputLabel = labelObject.transform;
            manager.IsReset = false;
            cameraObject.transform.position = Vector3.one * 100;
            labelObject.transform.localPosition = Vector3.one * 100;
            manager.ResetCamera();
            Assert.That(manager.IsReset, Is.True);
            Assert.That(cameraObject.transform.position.y, Is.EqualTo(9));
            Assert.That(labelObject.transform.localPosition.y, Is.EqualTo(0.13f));
            Assert.That(manager.GameplayCamera.nearClipPlane, Is.EqualTo(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(labelObject);
            Object.DestroyImmediate(host);
        }
    }
}

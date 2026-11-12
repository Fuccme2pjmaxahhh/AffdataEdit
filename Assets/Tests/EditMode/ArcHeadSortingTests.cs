using System;
using System.Collections;
using System.Reflection;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class ArcHeadSortingTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private GameObject owner;
    private ArcNoteRenderer renderer;
    private object visual;

    [SetUp]
    public void SetUp()
    {
        owner = new GameObject("Arc head sorting regression");
        owner.SetActive(false);
        renderer = owner.AddComponent<ArcNoteRenderer>();
        Type visualType = typeof(ArcNoteRenderer).Assembly.GetType("Arcade.Gameplay.ArcArcVisual", true);
        visual = Activator.CreateInstance(visualType, true);
        Set(visual, "arc", new ArcArc
        {
            Timing = 1000, EndTiming = 2000, XStart = .5f, YStart = .5f,
            LineType = ArcLineType.FalseNotVoid
        });
        Set(visual, "headEnable", true);
    }

    [TearDown]
    public void TearDown()
    {
        // EditMode owns the native resources synchronously; the player releases them on destruction.
        typeof(ArcNoteRenderer).GetMethod("ReleaseChartMemory", InstanceMembers, null, Type.EmptyTypes, null)
            .Invoke(renderer, null);
        var materials = (IDictionary)Get(renderer, "materials");
        foreach (Material material in materials.Values) Object.DestroyImmediate(material);
        materials.Clear();
        Type meshes = typeof(ArcNoteRenderer).Assembly.GetType("Arcade.Gameplay.ArcNoteMeshes", true);
        foreach (FieldInfo field in meshes.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            if (field.FieldType == typeof(Mesh))
            {
                var mesh = (Mesh)field.GetValue(null);
                if (mesh) Object.DestroyImmediate(mesh);
            }
        var sprites = (IDictionary)meshes.GetField("sprites", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (Mesh mesh in sprites.Values) if (mesh) Object.DestroyImmediate(mesh);
        sprites.Clear();
        Object.DestroyImmediate(owner);
    }

    [TestCase(-.021f, false, -2)]
    [TestCase(.021f, false, 6)]
    [TestCase(-.021f, true, 6)]
    [TestCase(.021f, true, -2)]
    [TestCase(-.004f, false, 2)]
    [TestCase(.004f, false, 2)]
    public void HeadUsesWorldDepthAndTruncatesTowardZero(float worldZ, bool fromBack, int expectedOrder)
    {
        Set(renderer, "viewOnBack", fromBack);
        SubmitHead(Matrix4x4.Translate(new Vector3(0, 0, worldZ)));
        Assert.That(OnlyOrder(), Is.EqualTo(expectedOrder));
    }

    [Test]
    public void HeadSortIncludesClippedOffsetAndParentTransform()
    {
        // A quarter turn maps a clipped head's local X movement into world Z.
        Matrix4x4 parent = Matrix4x4.identity;
        parent.m00 = parent.m22 = 0;
        parent.m02 = -1;
        parent.m20 = 1;
        parent.m23 = -3;
        visual.GetType().GetProperty("HeadOffset", InstanceMembers)
            .SetValue(visual, new Vector3(1.25f, 0, 0));
        SubmitHead(parent);
        Assert.That(OnlyOrder(), Is.EqualTo(-348));
    }

    [Test]
    public void HeadsWithinTheSameLayerSortByIncreasingWorldDepth()
    {
        SubmitHead(Matrix4x4.Translate(new Vector3(0, 0, -.121f)));
        SubmitHead(Matrix4x4.Translate(new Vector3(0, 0, -.101f)));
        var items = (IList)Get(renderer, "items");
        Assert.That(Get(items[0], "LayerValue"), Is.EqualTo(Get(items[1], "LayerValue")));
        Assert.That((int)Get(items[0], "Order"), Is.LessThan((int)Get(items[1], "Order")));
    }

    [Test]
    public void FinalIndirectDrawKeepsNearerArcHeadVisibleWhenCameraDepthDisagrees()
    {
        Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
        Assert.That(SystemInfo.supportsInstancing, Is.True);
        var shader = Resources.Load<Shader>("NoteIndirect");
        Assert.That(shader && shader.isSupported, Is.True);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Arc.prefab");
        var skin = prefab.GetComponent<ArcArcRenderer>();
        Assert.That(skin.DefaultTexture, Is.Not.Null);
        visual.GetType().GetProperty("HeadHighColor", InstanceMembers).SetValue(visual, Color.red);
        visual.GetType().GetProperty("HeadLowColor", InstanceMembers).SetValue(visual, Color.red);
        object farVisual = Activator.CreateInstance(visual.GetType(), true);
        Set(farVisual, "arc", new ArcArc
        {
            Timing = 1000, EndTiming = 2000, XStart = .5f, YStart = .5f,
            LineType = ArcLineType.FalseNotVoid
        });
        Set(farVisual, "headEnable", true);
        farVisual.GetType().GetProperty("HeadHighColor", InstanceMembers).SetValue(farVisual, Color.green);
        farVisual.GetType().GetProperty("HeadLowColor", InstanceMembers).SetValue(farVisual, Color.green);

        var camera = owner.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = .8f;
        camera.aspect = 1;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 20;
        Vector3 hit = new Vector3(ArcAlgorithm.ArcXToWorld(.5f), ArcAlgorithm.ArcYToWorld(.5f), 0);
        camera.transform.position = hit + new Vector3(0, 3, 5);
        camera.transform.LookAt(hit);
        Set(renderer, "cameraPosition", camera.transform.position);
        Set(renderer, "cameraForward", camera.transform.forward);
        Set(renderer, "orthographic", true);
        GeometryUtility.CalculateFrustumPlanes(camera, (Plane[])Get(renderer, "frustum"));

        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        try
        {
            Color32[] near = RenderHeads(camera, target, skin, farVisual, false, false);
            Color32[] corrected = RenderHeads(camera, target, skin, farVisual, true, false);
            Color32[] former = RenderHeads(camera, target, skin, farVisual, true, true);
            long nearRed = 0, correctedRed = 0, formerRed = 0;
            for (int i = 0; i < near.Length; i++)
                if (near[i].r > 25)
                {
                    nearRed += near[i].r;
                    correctedRed += corrected[i].r;
                    formerRed += former[i].r;
                }
            TestContext.WriteLine($"Head overlap red sums: isolated={nearRed}, corrected={correctedRed}, former={formerRed}.");
            Assert.That(nearRed, Is.GreaterThan(1000), "The actual nearer Arc head must draw visible pixels.");
            Assert.That(correctedRed, Is.GreaterThanOrEqualTo(nearRed * .98), "The head nearer in world Z must retain its visible red pixels.");
            Assert.That(formerRed, Is.LessThan(correctedRed - 100), "Restoring the old constant head order must reproduce coverage by the other head.");
        }
        finally { target.Release(); Object.DestroyImmediate(target); }
    }

    private Color32[] RenderHeads(Camera camera, RenderTexture target, ArcArcRenderer skin, object farVisual, bool includeFar, bool formerOrder)
    {
        var items = (IList)Get(renderer, "items");
        items.Clear();
        ((IList)Get(renderer, "commands")).Clear();
        Set(renderer, "visibleItemCount", 0);
        // The red head is nearer in world Z but its lower Y puts it farther along the camera's axis.
        // Both heads retain a substantial overlap in the image.
        Matrix4x4 nearParent = Matrix4x4.Translate(new Vector3(0, -.03f, -.101f));
        Matrix4x4 farParent = Matrix4x4.Translate(new Vector3(0, .03f, -.121f));
        typeof(ArcNoteRenderer).GetMethod("SubmitHead", InstanceMembers, null,
            new[] { visual.GetType(), typeof(Matrix4x4), typeof(Texture) }, null).Invoke(renderer,
            new[] { visual, (object)nearParent, skin.DefaultTexture });
        if (includeFar)
        {
            typeof(ArcNoteRenderer).GetMethod("SubmitHead", InstanceMembers, null,
                new[] { visual.GetType(), typeof(Matrix4x4), typeof(Texture) }, null).Invoke(renderer,
                new[] { farVisual, (object)farParent, skin.DefaultTexture });
            if (formerOrder)
            {
                // Restore only the former keys; keep geometry, material and GPU data identical.
                var keys = (Array)Get(renderer, "sortKeys");
                for (int i = 0; i < items.Count; i++)
                {
                    object item = items[i];
                    Set(item, "Order", 1);
                    items[i] = item;
                    object key = keys.GetValue(i);
                    Set(key, "Order", 1);
                    keys.SetValue(key, i);
                }
            }
        }
        typeof(ArcNoteRenderer).GetMethod("Upload", InstanceMembers).Invoke(renderer, null);
        Assert.That(renderer.VisibleInstanceCount, Is.EqualTo(includeFar ? 2 : 1));
        Assert.That(renderer.DrawCount, Is.EqualTo(1), "Both heads should retain the existing single indirect batch.");
        var sortedKeys = (Array)Get(renderer, "sortKeys");
        Assert.That(Get(sortedKeys.GetValue(0), "Index"), Is.EqualTo(includeFar && !formerOrder ? 1 : 0),
            "World-Z order and the former camera-distance order must disagree for this fixture.");
        var draws = (IList)Get(renderer, "draws");
        var arguments = (GraphicsBuffer)Get(renderer, "commandBuffer");
        var command = new CommandBuffer { name = "Arc head overlap regression" };
        try
        {
            command.SetRenderTarget(target);
            command.ClearRenderTarget(true, true, Color.black);
            command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            for (int i = 0; i < renderer.DrawCount; i++)
            {
                object draw = draws[i], item = Get(draw, "Item");
                command.DrawMeshInstancedIndirect((Mesh)Get(item, "Mesh"), (int)Get(item, "Submesh"),
                    (Material)Get(draw, "Material"), 0, arguments, i * GraphicsBuffer.IndirectDrawIndexedArgs.size,
                    (MaterialPropertyBlock)Get(draw, "Properties"));
            }
            Graphics.ExecuteCommandBuffer(command);
        }
        finally { command.Release(); }
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }

    private void SubmitHead(Matrix4x4 parent)
        => typeof(ArcNoteRenderer).GetMethod("SubmitHead", InstanceMembers, null,
            new[] { visual.GetType(), typeof(Matrix4x4), typeof(Texture) }, null)
            .Invoke(renderer, new[] { visual, (object)parent, Texture2D.whiteTexture });

    private int OnlyOrder()
    {
        var items = (IList)Get(renderer, "items");
        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(Get(items[0], "DepthWrite"), Is.EqualTo(0));
        Assert.That(Get(items[0], "DepthTest"), Is.EqualTo(4));
        return (int)Get(items[0], "Order");
    }

    private static object Get(object target, string name)
        => target.GetType().GetField(name, InstanceMembers).GetValue(target);

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, InstanceMembers).SetValue(target, value);
}

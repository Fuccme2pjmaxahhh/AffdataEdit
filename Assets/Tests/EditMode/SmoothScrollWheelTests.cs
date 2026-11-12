using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Compose;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class SmoothScrollWheelTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private EventSystem system;
    private static readonly FieldInfo InputParent = typeof(TMP_InputField).GetField("m_IScrollHandlerParent", Private);

    public void SetUp()
    {
        root = new GameObject("Scroll test", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        system = new GameObject("Scroll events", typeof(EventSystem)).GetComponent<EventSystem>();
        system.transform.SetParent(root.transform);
    }

    public void TearDown() { Object.DestroyImmediate(root); }

    [UnityTest]
    public IEnumerator RuntimeWheelRoutingAndLifecycleRegressions()
    {
        // These are runtime MonoBehaviours, not ExecuteAlways editor components. Enter once so
        // Awake/enable/disable/destruction and active-template cloning use Unity's real callbacks.
        yield return new EnterPlayMode();
        var failures = new List<string>();
        var cases = new Action[]
        {
            SingleLineInputUsesOneSmoothStepInsteadOfZeroOrDoubleNativeSteps,
            NestedInputsReparentAndDisabledInnerViewUseNearestEnabledScrollOnly,
            DisableAndUninstallRestoreNativeInputForwardingAndSensitivity,
            FastWheelClampsThenImmediatelyAllowsReverseAndReusesEventObject,
            ActiveDropdownTemplateClonePreservesOriginalNonzeroSensitivity,
            RecycledInputRemovesStrongBindingAndRebindsAfterEnable,
            MultilineTextKeepsNativeTextScrollHandler
        };
        foreach (var test in cases)
        {
            SetUp();
            try { test(); }
            catch (Exception error) { failures.Add(test.Method.Name + ": " + error); }
            finally { TearDown(); }
        }
        yield return new ExitPlayMode();
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    private ScrollRect MakeScroll(string name, Transform parent)
    {
        var view = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
        view.transform.SetParent(parent, false);
        var rect = (RectTransform)view.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(200, 200);
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        var body = (RectTransform)content.transform;
        body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one;
        body.pivot = new Vector2(.5f, 1); body.sizeDelta = new Vector2(0, 1200);
        var scroll = view.GetComponent<ScrollRect>();
        scroll.content = body; scroll.viewport = rect;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.inertia = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20;
        Canvas.ForceUpdateCanvases();
        return scroll;
    }

    private TMP_InputField MakeInput(ScrollRect scroll)
    {
        var go = new GameObject("Numeric input", typeof(RectTransform), typeof(TMP_InputField));
        go.transform.SetParent(scroll.content, false);
        var input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        // This is the exact native TMP cache used on enabled, fully constructed single-line fields.
        InputParent.SetValue(input, scroll);
        return input;
    }

    private static void Prepare(GameObject target)
    {
        typeof(AdeSmoothScroll).Assembly.GetType("Arcade.Compose.AdeSmoothScrollInstaller")
            .GetMethod("PrepareWheelTarget", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { target });
    }

    private void Wheel(GameObject target, float y = -1)
    {
        Prepare(target);
        ExecuteEvents.ExecuteHierarchy(target, new PointerEventData(system) { scrollDelta = new Vector2(0, y) }, ExecuteEvents.scrollHandler);
    }

    private static void Advance(AdeSmoothScroll smooth, float seconds)
        => typeof(AdeSmoothScroll).GetMethod("Advance", Private).Invoke(smooth, new object[] { seconds });

    public void SingleLineInputUsesOneSmoothStepInsteadOfZeroOrDoubleNativeSteps()
    {
        var scroll = MakeScroll("Timing", root.transform);
        var smooth = scroll.gameObject.AddComponent<AdeSmoothScroll>();
        var input = MakeInput(scroll);
        Wheel(input.gameObject);
        Assert.AreSame(smooth, InputParent.GetValue(input));
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(.001));
        Advance(smooth, .02f);
        Assert.That(scroll.content.anchoredPosition.y, Is.InRange(.01f, 19.9f));
        Advance(smooth, 1);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
    }

    public void NestedInputsReparentAndDisabledInnerViewUseNearestEnabledScrollOnly()
    {
        var outer = MakeScroll("Outer", root.transform);
        var inner = MakeScroll("Inner", outer.content);
        var input = MakeInput(inner);
        Wheel(input.gameObject);
        Advance(inner.GetComponent<AdeSmoothScroll>(), 1);
        Assert.That(inner.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
        Assert.That(outer.content.anchoredPosition.y, Is.EqualTo(0).Within(.001));
        input.transform.SetParent(outer.content, false);
        Wheel(input.gameObject);
        Advance(outer.GetComponent<AdeSmoothScroll>(), 1);
        Assert.That(outer.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
        input.transform.SetParent(inner.content, false);
        inner.enabled = false;
        Wheel(input.gameObject);
        Advance(outer.GetComponent<AdeSmoothScroll>(), 1);
        Assert.That(outer.content.anchoredPosition.y, Is.EqualTo(40).Within(.05));
        Assert.That(inner.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
    }

    public void DisableAndUninstallRestoreNativeInputForwardingAndSensitivity()
    {
        var scroll = MakeScroll("View", root.transform);
        var input = MakeInput(scroll);
        Wheel(input.gameObject);
        var smooth = scroll.GetComponent<AdeSmoothScroll>();
        smooth.enabled = false;
        Assert.AreEqual(20, scroll.scrollSensitivity);
        Wheel(input.gameObject);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
        smooth.enabled = true;
        Assert.AreEqual(0, scroll.scrollSensitivity);
        Object.DestroyImmediate(smooth);
        Assert.AreSame(scroll, InputParent.GetValue(input));
        Assert.AreEqual(20, scroll.scrollSensitivity);
        ExecuteEvents.ExecuteHierarchy(input.gameObject, new PointerEventData(system) { scrollDelta = Vector2.down }, ExecuteEvents.scrollHandler);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(40).Within(.05));
    }

    public void FastWheelClampsThenImmediatelyAllowsReverseAndReusesEventObject()
    {
        var scroll = MakeScroll("View", root.transform);
        Wheel(scroll.gameObject, -1000000);
        var smooth = scroll.GetComponent<AdeSmoothScroll>();
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(.001), "Native stepping must not bypass smoothing.");
        Advance(smooth, 1);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(1000).Within(.05));
        Advance(smooth, 1);
        var eventObject = typeof(AdeSmoothScroll).GetField("wheel", Private).GetValue(smooth);
        Assert.IsNotNull(eventObject);
        Wheel(scroll.gameObject, 1);
        Advance(smooth, 1);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(980).Within(.05));
        Assert.AreSame(eventObject, typeof(AdeSmoothScroll).GetField("wheel", Private).GetValue(smooth));
    }

    public void ActiveDropdownTemplateClonePreservesOriginalNonzeroSensitivity()
    {
        var template = MakeScroll("Dropdown template", root.transform);
        template.gameObject.AddComponent<AdeSmoothScroll>();
        Assert.AreEqual(0, template.scrollSensitivity);
        var clone = Object.Instantiate(template.gameObject, root.transform).GetComponent<ScrollRect>();
        Wheel(clone.gameObject);
        Advance(clone.GetComponent<AdeSmoothScroll>(), 1);
        Assert.That(clone.content.anchoredPosition.y, Is.EqualTo(20).Within(.05));
        clone.GetComponent<AdeSmoothScroll>().enabled = false;
        Assert.AreEqual(20, clone.scrollSensitivity);
    }

    public void RecycledInputRemovesStrongBindingAndRebindsAfterEnable()
    {
        var scroll = MakeScroll("View", root.transform);
        var input = MakeInput(scroll);
        Wheel(input.gameObject);
        var smooth = scroll.GetComponent<AdeSmoothScroll>();
        var bindings = (System.Collections.IDictionary)typeof(AdeSmoothScroll).GetField("inputs", Private).GetValue(smooth);
        Assert.AreEqual(1, bindings.Count);
        input.gameObject.SetActive(false);
        Assert.AreEqual(0, bindings.Count);
        Assert.AreSame(scroll, InputParent.GetValue(input));
        input.gameObject.SetActive(true);
        Wheel(input.gameObject);
        Assert.AreEqual(1, bindings.Count);
        Object.DestroyImmediate(input.gameObject);
        Assert.AreEqual(0, bindings.Count);
    }

    public void MultilineTextKeepsNativeTextScrollHandler()
    {
        var scroll = MakeScroll("View", root.transform);
        var input = MakeInput(scroll);
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        Prepare(input.gameObject);
        Assert.AreSame(scroll, InputParent.GetValue(input));
    }
}

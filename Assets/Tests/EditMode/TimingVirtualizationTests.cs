using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class TimingVirtualizationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static void Call(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
    private RectTransform Rect(string name, Transform parent, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
    private TMP_Text Label(Transform parent)
    {
        var rect = Rect("Label", parent, 100, 44);
        return rect.gameObject.AddComponent<TextMeshProUGUI>();
    }
    private Button Button(Transform parent)
    {
        var rect = Rect("Virtual row", parent, 400, 44);
        return rect.gameObject.AddComponent<Button>();
    }
    [TearDown] public void TearDown() { if (root) Object.DestroyImmediate(root); }

    [Test]
    public void ThreeThousandGroupsRemainCoveredAfterFastJumpsReverseAndViewportResizes()
    {
        root = new GameObject("Timing virtualization regression", typeof(RectTransform)); root.SetActive(false);
        var panel = root.AddComponent<AdeChartToolsPanel>();
        var scrollRect = Rect("Scroll", root.transform, 400, 188).gameObject.AddComponent<ScrollRect>();
        var viewport = Rect("Viewport", scrollRect.transform, 400, 188);
        var content = Rect("Content", viewport, 400, 0);
        content.gameObject.AddComponent<VerticalLayoutGroup>();
        content.gameObject.AddComponent<ContentSizeFitter>();
        scrollRect.viewport = viewport; scrollRect.content = content;
        Set(panel, "groupList", content); Set(panel, "groupScroll", scrollRect);
        Set(panel, "ready", true); Set(panel, "expanded", true);
        var groups = (List<ArcTimingGroup>)Get(panel, "groups"); groups.Add(null);
        for (int i = 1; i < 3007; i++) groups.Add(new ArcTimingGroup { Id = i });
        var rows = (List<Button>)Get(panel, "groupRows");
        var details = (List<TMP_Text>)Get(panel, "groupDetails");
        var copies = (List<Button>)Get(panel, "groupCopies");
        var deletes = (List<Button>)Get(panel, "groupDeletes");
        for (int i = 0; i < 24; i++)
        {
            var row = Button(content); Label(row.transform); rows.Add(row);
            details.Add(Label(row.transform)); copies.Add(Button(row.transform)); deletes.Add(Button(row.transform));
        }
        var input = Rect("Uncommitted edit", root.transform, 180, 44).gameObject.AddComponent<TMP_InputField>();
        input.SetTextWithoutNotify("noinput_anglex123"); Set(panel, "attributes", input);

        foreach (float height in new[] { 188f, 720f, 320f, 188f })
        {
            viewport.sizeDelta = new Vector2(400, height);
            float fullHeight = 3007 * 48 - 4, end = fullHeight - height;
            foreach (float offset in new[] { 0, 1500 * 48f, end, 5 * 48f, end / 2, end, 0 })
            {
                content.anchoredPosition = new Vector2(0, offset);
                // The pre-render callback also catches motion applied after ScrollRect.LateUpdate.
                Call(panel, "SynchronizeVirtualRows");
                int first = (int)Get(panel, "groupFirst"), slots = (int)Get(panel, "groupSlots");
                Assert.That(first, Is.LessThanOrEqualTo(Mathf.FloorToInt(offset / 48)));
                Assert.That(first + slots, Is.GreaterThanOrEqualTo(Mathf.Min(3007, Mathf.CeilToInt((offset + height) / 48))));
                Assert.That(slots, Is.LessThanOrEqualTo(Mathf.CeilToInt(height / 48) + 5));
                Assert.That(content.rect.height, Is.EqualTo(fullHeight).Within(.01));
                for (int i = 0; i < slots; i++)
                {
                    Assert.That(rows[i].gameObject.activeSelf, Is.True);
                    Assert.That(((RectTransform)rows[i].transform).anchoredPosition.y, Is.EqualTo(-(first + i) * 48));
                    string expected = first + i == 0 ? "默认组" : "组 " + (first + i);
                    Assert.That(rows[i].GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo(expected));
                }
                Assert.That(rows.Count, Is.EqualTo(24)); // no data-sized UI growth during any jump
            }
        }
        Assert.That(content.GetComponent<VerticalLayoutGroup>().enabled, Is.False);
        Assert.That(input.text, Is.EqualTo("noinput_anglex123"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MinimumPhysicalThumbRetainsFullNativeDragRange(bool vertical)
    {
        root = new GameObject("Scrollbar thumb regression", typeof(RectTransform));
        var events = root.AddComponent<EventSystem>();
        var owner = Rect("Scroll", root.transform, 300, 200).gameObject.AddComponent<ScrollRect>();
        owner.horizontal = !vertical; owner.vertical = vertical;
        var viewport = Rect("Viewport", owner.transform, 300, 200);
        var content = Rect("Content", viewport, vertical ? 300 : 144332, vertical ? 144332 : 200);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 1);
        owner.viewport = viewport; owner.content = content;
        var track = Rect("Bar", owner.transform, vertical ? 8 : 300, vertical ? 200 : 8);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;
        var handle = Rect("Handle", track, 0, 0);
        handle.gameObject.AddComponent<Image>(); bar.handleRect = handle;
        if (vertical) owner.verticalScrollbar = bar; else owner.horizontalScrollbar = bar;
        bar.size = .001f;
        AdeScrollbarThumb.Ensure(bar);
        var helper = bar.GetComponent<AdeScrollbarThumb>();
        Call(helper, "Apply");
        Assert.That((vertical ? handle.rect.height : handle.rect.width), Is.GreaterThanOrEqualTo(19.9f));
        int axis = vertical ? 1 : 0;
        var data = new PointerEventData(events) { button = PointerEventData.InputButton.Left };
        bar.value = .5f;
        Vector2 press = RectTransformUtility.WorldToScreenPoint(null, handle.TransformPoint(handle.rect.center));
        data.position = press; data.pointerPressRaycast = new RaycastResult { screenPosition = press };
        bar.OnBeginDrag(data);
        foreach (float value in new[] { 0, .5f, 1f, 0 })
        {
            float length = track.rect.size[axis], thumbLength = handle.rect.size[axis];
            Vector2 local = track.rect.center;
            local[axis] = track.rect.min[axis] + thumbLength / 2 + value * (length - thumbLength);
            data.position = RectTransformUtility.WorldToScreenPoint(null, track.TransformPoint(local));
            bar.OnDrag(data);
            Assert.That(bar.value, Is.EqualTo(value).Within(.001));
            Assert.That(vertical ? owner.verticalNormalizedPosition : owner.horizontalNormalizedPosition,
                Is.EqualTo(value).Within(.001));
        }
        track.sizeDelta *= 2;
        Call(helper, "Apply");
        Assert.That((vertical ? handle.rect.height : handle.rect.width), Is.EqualTo(20).Within(.1));
    }
}

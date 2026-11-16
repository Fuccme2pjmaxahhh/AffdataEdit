using System;
using System.Reflection;
using Arcade.Compose;
using NUnit.Framework;
using UnityEngine;

public class StartupUpdateGateTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private AdeUpdateChecker checker;
    [SetUp] public void SetUp()
    {
        root = new GameObject("Startup update gate"); root.SetActive(false);
        checker = root.AddComponent<AdeUpdateChecker>();
    }
    [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);
    private void State(bool busy) => typeof(AdeUpdateChecker).GetMethod("SetState", Hidden).Invoke(checker, new object[] { busy, "status" });
    private bool Entered => (bool)typeof(AdeUpdateChecker).GetField("entered", Hidden).GetValue(checker);

    [Test]
    public void StartupCannotContinueWhileCheckingButCanContinueAfterFailure()
    {
        State(true); checker.ContinueOrQuit();
        Assert.IsFalse(Entered);
        State(false); checker.ContinueOrQuit();
        Assert.IsTrue(Entered);
    }

    [TestCase(false)] [TestCase(true)]
    public void StartupReceivesStateWithoutEditorScene(bool required)
    {
        typeof(AdeUpdateChecker).GetField("updateRequired", Hidden).SetValue(checker, required);
        bool called = false;
        Action<bool, bool, string> show = (busy, update, message) =>
        {
            called = true; Assert.IsFalse(busy); Assert.AreEqual(required, update); Assert.AreEqual("status", message);
        };
        typeof(AdeUpdateChecker).GetField("showState", Hidden).SetValue(checker, show);
        State(false);
        Assert.IsTrue(called); Assert.IsFalse(Entered);
    }
}

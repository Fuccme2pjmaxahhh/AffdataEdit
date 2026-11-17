using System;
using System.Collections;
using System.Reflection;
using Arcade.Compose;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

public class StartupPromptLifecycleTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator ContinuingWaitsForPromptFadeDuringSlowFrames()
    {
        yield return new EnterPlayMode();
        int previousFrameRate = Application.targetFrameRate;
        var root = new GameObject("Startup prompt lifecycle test");
        try
        {
            var loader = root.AddComponent<AdeStartupLoader>();
            loader.enabled = false;
            loader.StopAllCoroutines();
            var type = typeof(AdeStartupLoader);
            type.GetMethod("BuildLabels", Hidden).Invoke(loader, null);
            var checker = root.AddComponent<AdeUpdateChecker>();
            type.GetMethod("BuildPrompt", Hidden).Invoke(loader, new object[] { checker });
            var prompt = type.GetField("prompt", Hidden).GetValue(loader);
            var group = (CanvasGroup)prompt.GetType().GetField("Group").GetValue(prompt);
            var running = prompt.GetType().GetField("Running");
            group.gameObject.SetActive(true);
            group.alpha = 1;
            type.GetMethod("Run", Hidden).Invoke(loader, new[] { prompt, (object)false, 6f });
            loader.StartCoroutine((IEnumerator)type.GetMethod("RemovePromptAfterFade", Hidden).Invoke(loader, null));
            Assert.IsTrue(group, "The prompt must remain alive while it is fading.");
            Assert.IsNotNull(running.GetValue(prompt));
            for (int frame = 0; frame < 8 && group; frame++)
            {
                // Reproduce loading stalls longer than the former 0.25-second destruction delay.
                System.Threading.Thread.Sleep(350);
                yield return null;
            }
            Assert.IsFalse(group, "The completed transition must release its prompt.");
            Assert.IsNull(running.GetValue(prompt), "Destruction must follow animation completion.");
        }
        finally
        {
            Application.targetFrameRate = previousFrameRate;
            UnityEngine.Object.Destroy(root);
        }
        yield return new ExitPlayMode();
    }
}

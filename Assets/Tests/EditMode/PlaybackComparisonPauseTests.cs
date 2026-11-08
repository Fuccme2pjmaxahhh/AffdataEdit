using System.Collections.Generic;
using System.Reflection;
using Arcade.Compose;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class PlaybackComparisonPauseTests
{
    [Test]
    public void PlayStateToggleKeepsPlayerModeAndModeButtonExits()
    {
        var root = new GameObject("Pause comparison test", typeof(RectTransform));
        try
        {
            var audio = root.AddComponent<ArcAudioManager>();
            typeof(ArcAudioManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audio, null);
            var game = root.AddComponent<ArcGameplayManager>();
            var tap = new ArcTap { Judged = true };
            game.Chart = new ArcChart(new Arcade.Aff.RawAffChart()) { Taps = new List<ArcTap> { tap } };
            var compose = root.AddComponent<ArcadeComposeManager>();
            var camera = root.AddComponent<Camera>();
            compose.GameplayManager = game;
            compose.GameplayCamera = camera;
            compose.IsEditorMode = false;
            camera.rect = new Rect(0, 0, 1, 1);
            game.IsPlaying = true;
            typeof(ArcGameplayManager).GetField("audioTiming", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, 13.945f);
            var switching = typeof(ArcadeComposeManager).GetField("switchingMode", BindingFlags.Instance | BindingFlags.NonPublic);
            switching.SetValue(compose, true);

            compose.TogglePlayback();
            Assert.That(game.IsPlaying, Is.False);
            Assert.That(compose.IsPlaybackPaused, Is.True);
            Assert.That(compose.IsEditorMode, Is.False);
            Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(game.AudioTiming, Is.EqualTo(13945));
            Assert.That(tap.Judged, Is.True, "Pausing must not reset judged notes");
            Assert.That(switching.GetValue(compose), Is.True, "Q must pause during the transition without cancelling it");

            compose.TogglePlayback();
            Assert.That(game.IsPlaying, Is.True);
            Assert.That(compose.IsEditorMode, Is.False);
            Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(game.AudioTiming, Is.EqualTo(13945));
            Assert.That(tap.Judged, Is.True);
            compose.TogglePlayback();
            Assert.That(game.IsPlaying, Is.False);

            var rect = root.GetComponent<RectTransform>();
            compose.TopBar = compose.BottomBar = compose.LeftBar = compose.RightBar = rect;
            compose.PauseButtonImage = root.AddComponent<Image>();
            compose.PauseButton = root.AddComponent<Button>();
            compose.InfoCanvasGroup = root.AddComponent<CanvasGroup>();
            compose.TimingSliderHandle = compose.PauseButtonImage;
            switching.SetValue(compose, false);
            compose.OnPauseClicked();
            Assert.That(compose.IsEditorMode, Is.True);
            Assert.That(game.IsPlaying, Is.False);
            Assert.That(game.AudioTiming, Is.EqualTo(13945));
            // EditMode destruction does not dispatch MonoBehaviour.OnDestroy;
            // cancel the player's scheduled view transition before tearing down its camera.
            typeof(ArcadeComposeManager).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(compose, null);
        }
        finally { Object.DestroyImmediate(root); }
    }
}

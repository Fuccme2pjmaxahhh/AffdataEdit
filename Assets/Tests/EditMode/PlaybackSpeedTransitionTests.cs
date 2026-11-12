using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Arcade.Audio;
using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class PlaybackSpeedTransitionTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private GameObject root;
    private ArcGameplayManager game, previousGame;
    private ArcAudioManager audio, previousAudio;
    private BassClip clip;
    private string temp;

    [SetUp]
    public void SetUp()
    {
        previousGame = ArcGameplayManager.Instance;
        previousAudio = ArcAudioManager.Instance;
        root = new GameObject("Playback transition checks");
        game = root.AddComponent<ArcGameplayManager>();
        audio = root.AddComponent<ArcAudioManager>();
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, game);
        typeof(ArcAudioManager).GetProperty("Instance").SetValue(null, audio);
        game.DisplayDelay = 160;
        game.GlobalAudioOffset = 35;
        game.ChartAudioOffset = 80;
        temp = Path.Combine(Path.GetTempPath(), "Affdata-rate-" + Guid.NewGuid().ToString("N") + ".flac");
    }

    [TearDown]
    public void TearDown()
    {
        audio.ReleaseVoice();
        if (clip) Object.DestroyImmediate(clip);
        Object.DestroyImmediate(root);
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, previousGame);
        typeof(ArcAudioManager).GetProperty("Instance").SetValue(null, previousAudio);
        if (File.Exists(temp)) File.Delete(temp);
    }

    private void SetRate(float value) => typeof(ArcAudioManager).GetProperty("PlayBackSpeed").SetValue(audio, value);
    private void RateWithoutAudio(float value) => typeof(ArcAudioManager).GetField("playBackSpeed", Private).SetValue(audio, value);
    private void SetGame(string field, object value) => typeof(ArcGameplayManager).GetField(field, Private).SetValue(game, value);
    private float Capture(float previous, double now) => (float)typeof(ArcGameplayManager).GetMethod("CapturePlaybackTransition", Private)
        .Invoke(game, new object[] { previous, now });
    private void Complete(float source, double now) => typeof(ArcGameplayManager).GetMethod("CompletePlaybackTransition", Private)
        .Invoke(game, new object[] { source, now });
    private void Advance(double now, float speed) => typeof(ArcGameplayManager).GetMethod("AdvancePlaybackClock", Private)
        .Invoke(game, new object[] { now, speed });
    private int Handle => VoiceHandle(typeof(ArcAudioManager).GetProperty("ActiveVoice", Private).GetValue(audio));
    private static int VoiceHandle(object voice) => (int)voice.GetType().GetProperty("Handle", Private).GetValue(voice);

    [TestCase(1f, .75f)]
    [TestCase(.75f, .5f)]
    [TestCase(.5f, .25f)]
    [TestCase(.25f, 1f)]
    public void PlayingRateChangesKeepSoundPhaseAndExcludePreparationTime(float oldRate, float newRate)
    {
        RateWithoutAudio(oldRate);
        SetGame("audioTiming", 2f);
        SetGame("lastDspTime", 100d);
        game.IsPlaying = true;
        int before = game.SoundChartTiming;
        uint seek = game.SeekVersion;
        float source = Capture(oldRate, 100.04);
        RateWithoutAudio(newRate);
        Complete(source, 102); // Simulate a slow seek/buffer refill while the old voice is paused.
        Assert.That(game.SoundChartTiming, Is.EqualTo(before + oldRate * 40).Within(1));
        Advance(102.04, newRate);
        Assert.That(game.SoundChartTiming, Is.EqualTo(before + oldRate * 40 + newRate * 40).Within(1));
        Assert.AreEqual(seek, game.SeekVersion, "A rate change is not an explicit seek and must not replay sound cursors.");
        Assert.AreEqual(160, game.DisplayDelay);
        Assert.AreEqual(35, game.GlobalAudioOffset);
        Assert.AreEqual(80, game.ChartAudioOffset);
    }

    [Test]
    public void PausedRateChangeKeepsTheDisplayedEditingPosition()
    {
        SetGame("audioTiming", 2f);
        SetGame("lastDspTime", 100d);
        game.IsPlaying = false;
        int before = game.ChartTiming;
        float source = Capture(1, 100.04);
        RateWithoutAudio(.25f);
        Complete(source, 102);
        Assert.AreEqual(before, game.ChartTiming);
        Assert.IsFalse(game.IsPlaying);
    }

    private void LoadSilentTransport()
    {
        clip = BassClip.CreateSilence("rate test embedded audio", 48000 * 12, 2, 48000);
        audio.Load(clip);
        audio.Timing = 1;
    }

    [Test]
    public void FallbackStreamChangesRateWithoutLosingPlayingOrPausedPosition()
    {
        LoadSilentTransport();
        typeof(ArcAudioManager).GetField("liveDisabled", Private).SetValue(audio, true);
        audio.Play(); // Silence: exercises the actual device playback position without test sounds.
        foreach (float rate in new[] { .75f, .5f, .25f, 1f })
        {
            float before = audio.Timing;
            uint version = audio.VoiceVersion;
            SetRate(rate);
            Assert.IsNull(typeof(ArcAudioManager).GetField("live", Private).GetValue(audio));
            Assert.That(audio.Timing, Is.EqualTo(before).Within(.025));
            Assert.AreEqual(version + 1, audio.VoiceVersion);
            Assert.AreEqual(1, Native.IsActive(Handle));
            float start = audio.Timing;
            double clock = BassAudio.Clock;
            Thread.Sleep(100);
            double expected = (BassAudio.Clock - clock) * rate;
            Assert.That(audio.Timing - start, Is.InRange(expected * .2, expected * 1.8));
        }
        audio.Pause();
        foreach (float rate in new[] { .75f, .5f, .25f, 1f })
        {
            float before = audio.Timing;
            SetRate(rate);
            Assert.IsNull(typeof(ArcAudioManager).GetField("live", Private).GetValue(audio));
            Assert.That(audio.Timing, Is.EqualTo(before).Within(.001));
            Assert.That(Native.IsActive(Handle), Is.Not.EqualTo(1));
            Assert.That(Native.IsActive(Handle), Is.Not.EqualTo(2));
        }
    }

    [Test]
    public void PreRollDeadlineUsesRemainingChartTimeAtTheNewRate()
    {
        LoadSilentTransport();
        game.IsPlaying = true;
        SetGame("audioTiming", -3f - (game.GlobalAudioOffset + game.DisplayDelayChart) / 1000f);
        SetGame("lastDspTime", BassAudio.Clock);
        audio.PlayDelayed(3);
        SetRate(.5f);
        float source = Capture(.5f, BassAudio.Clock);
        double deadline = (double)typeof(ArcAudioManager).GetField("scheduledStart", Private).GetValue(audio);
        Assert.That(deadline - BassAudio.Clock, Is.EqualTo(-source / .5f).Within(.03));
        Assert.That(source, Is.LessThan(-2.9f), "A pending countdown must not jump to the stopped native stream's zero position.");
        Assert.That(Native.IsActive(Handle), Is.Not.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ReadyRenderedVoiceKeepsSourcePositionAndPlayingState(bool playing)
    {
        LoadSilentTransport();
        SetRate(.5f);
        EncodeSilence();
        if (playing) audio.Play();
        float before = audio.Timing;
        uint version = audio.VoiceVersion;
        typeof(ArcAudioManager).GetMethod("SwitchVoice", Private).Invoke(audio, new object[] { temp, .5f });
        Assert.That(audio.Timing, Is.EqualTo(before).Within(.05));
        Assert.AreEqual(version + 1, audio.VoiceVersion);
        object rendered = typeof(ArcAudioManager).GetField("renderedVoice", Private).GetValue(audio);
        Assert.NotNull(rendered);
        Assert.That(audio.Timing, Is.EqualTo((double)rendered.GetType().GetProperty("Position", Private).GetValue(rendered) * .5).Within(.005));
        Assert.AreEqual(playing, Native.IsActive(Handle) == 1);
    }

    private void EncodeSilence()
    {
        Type voiceType = typeof(BassClip).Assembly.GetType("Arcade.Audio.BassVoice", true);
        var decoder = (IDisposable)Activator.CreateInstance(voiceType, Private, null, new object[] { clip, false }, null);
        int encoder = 0;
        try
        {
            Native.EncoderVersion();
            encoder = Native.StartFlac(VoiceHandle(decoder), null, 0x80000004, temp);
            Assert.AreNotEqual(0, encoder);
            var buffer = new float[8192];
            while (Native.Read(VoiceHandle(decoder), buffer, buffer.Length * sizeof(float)) > 0) { }
            Assert.IsTrue(Native.StopEncoder(encoder));
            encoder = 0;
        }
        finally { if (encoder != 0) Native.StopEncoder(encoder); decoder.Dispose(); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ClearingPlayingOrPausedCacheReleasesFileAndKeepsPosition(bool playing)
    {
        var cache = typeof(BassClip).Assembly.GetType("Arcade.Audio.SlowAudioCache", true);
        var folderField = cache.GetField("folder", BindingFlags.Static | BindingFlags.NonPublic);
        object previousFolder = folderField.GetValue(null);
        string cacheFolder = Path.Combine(Path.GetTempPath(), "Affdata-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheFolder);
        temp = Path.Combine(cacheFolder, "song.flac");
        try
        {
            folderField.SetValue(null, cacheFolder);
            LoadSilentTransport();
            SetRate(.5f);
            EncodeSilence();
            typeof(ArcAudioManager).GetMethod("SwitchVoice", Private).Invoke(audio, new object[] { temp, .5f });
            if (playing) audio.Play();
            float before = audio.Timing;
            Assert.AreEqual(1, ArcAudioManager.ClearSlowAudioCache());
            Assert.IsFalse(File.Exists(temp), "The native cached voice must release its file before deletion.");
            Assert.IsNull(typeof(ArcAudioManager).GetField("renderedVoice", Private).GetValue(audio));
            Assert.IsNotNull(typeof(ArcAudioManager).GetField("live", Private).GetValue(audio));
            Assert.That(audio.Timing, Is.EqualTo(before).Within(.05));
            Assert.AreEqual(.5f, audio.PlayBackSpeed);
            Assert.AreEqual(playing, Native.IsActive(Handle) == 1);
            typeof(ArcAudioManager).GetMethod("Update", Private).Invoke(audio, null);
            Assert.IsFalse(File.Exists(temp), "Clearing must not immediately regenerate the cache.");
        }
        finally
        {
            audio.ReleaseVoice();
            folderField.SetValue(null, previousFolder);
            Directory.Delete(cacheFolder, true);
        }
    }

    private static class Native
    {
        [DllImport("bass", EntryPoint = "BASS_ChannelIsActive")] internal static extern int IsActive(int handle);
        [DllImport("bass", EntryPoint = "BASS_ChannelGetData")] internal static extern int Read(int handle, [Out] float[] samples, int bytes);
        [DllImport("bassenc", EntryPoint = "BASS_Encode_GetVersion")] internal static extern uint EncoderVersion();
        [DllImport("bassenc", EntryPoint = "BASS_Encode_Stop")] internal static extern bool StopEncoder(int handle);
        [DllImport("bassenc_flac", EntryPoint = "BASS_Encode_FLAC_StartFile", CharSet = CharSet.Unicode)] internal static extern int StartFlac(int handle, string options, uint flags, string path);
    }
}

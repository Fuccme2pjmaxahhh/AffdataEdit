using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Arcade.Audio;
using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class LiveSlowPlaybackTests
{
    private const int Frequency = 48000;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type LiveType = typeof(BassClip).Assembly.GetType("Arcade.Audio.LiveSlowStream", true);
    private static readonly Type VoiceType = typeof(BassClip).Assembly.GetType("Arcade.Audio.BassVoice", true);
    private static readonly Type CacheType = typeof(BassClip).Assembly.GetType("Arcade.Audio.SlowAudioCache", true);
    private readonly List<IDisposable> streams = new List<IDisposable>();
    private readonly List<BassClip> clips = new List<BassClip>();
    private string folder, wavePath, flacPath;
    private GameObject root;
    private ArcGameplayManager game, previousGame;
    private ArcAudioManager audio, previousAudio;
    private BassAudio previousBass;
    private bool previousCachePlaying;
    private float previousCacheSpeed;

    [SetUp]
    public void SetUp()
    {
        previousGame = ArcGameplayManager.Instance;
        previousAudio = ArcAudioManager.Instance;
        previousBass = (BassAudio)typeof(BassAudio).GetField("instance", Static).GetValue(null);
        previousCachePlaying = (bool)CacheType.GetField("playbackActive", Static).GetValue(null);
        previousCacheSpeed = (float)CacheType.GetField("playbackSpeed", Static).GetValue(null);
        root = new GameObject("Live slow playback checks");
        game = root.AddComponent<ArcGameplayManager>();
        audio = root.AddComponent<ArcAudioManager>();
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, game);
        typeof(ArcAudioManager).GetProperty("Instance").SetValue(null, audio);
        folder = Path.Combine(Path.GetTempPath(), "Affdata-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        wavePath = Path.Combine(folder, "非零 双声道.wav");
        flacPath = Path.Combine(folder, "ready.flac");
        WriteTone();
    }

    [TearDown]
    public void TearDown()
    {
        audio.ReleaseVoice();
        foreach (var stream in streams) stream.Dispose();
        streams.Clear();
        foreach (var clip in clips) if (clip) Object.DestroyImmediate(clip);
        clips.Clear();
        Object.DestroyImmediate(root);
        if (!previousBass)
        {
            var created = (BassAudio)typeof(BassAudio).GetField("instance", Static).GetValue(null);
            if (created) Object.DestroyImmediate(created.gameObject);
        }
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, previousGame);
        typeof(ArcAudioManager).GetProperty("Instance").SetValue(null, previousAudio);
        CacheType.GetField("playbackActive", Static).SetValue(null, previousCachePlaying);
        CacheType.GetField("playbackSpeed", Static).SetValue(null, previousCacheSpeed);
        Directory.Delete(folder, true);
    }

    private void WriteTone()
    {
        int frames = Frequency * 4;
        using (var writer = new BinaryWriter(File.Create(wavePath)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(frames * 4 + 36);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(Frequency);
            writer.Write(Frequency * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 4);
            for (int i = 0; i < frames; i++)
            {
                writer.Write((short)(7000 * Math.Sin(2 * Math.PI * 440 * i / Frequency)));
                writer.Write((short)(5000 * Math.Cos(2 * Math.PI * 660 * i / Frequency)));
            }
        }
    }

    private static object Get(object value, string property) => value.GetType().GetProperty(property, Instance).GetValue(value);
    private static object Field(object value, string field) => value.GetType().GetField(field, Instance).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Instance).SetValue(value, data);
    private static object Call(object value, string method, params object[] arguments)
        => value.GetType().GetMethod(method, Instance).Invoke(value, arguments);
    private static int Handle(object voice) => (int)Get(voice, "Handle");
    private object Live => Field(audio, "live");
    private object Active => Get(audio, "ActiveVoice");
    private void Rate(float speed) => typeof(ArcAudioManager).GetProperty("PlayBackSpeed").SetValue(audio, speed);

    private object CreateLive(float speed, double start)
    {
        var clip = BassClip.Load(wavePath);
        clips.Add(clip);
        object live = LiveType.GetMethod("TryCreate", Static).Invoke(null, new object[] { clip, speed, start });
        Assert.IsNotNull(live, "The installed native plugin must provide live pitch-preserving playback.");
        streams.Add((IDisposable)live);
        return live;
    }

    [TestCase(.75f)]
    [TestCase(.5f)]
    [TestCase(.25f)]
    public void NonzeroSourcePrimesAtRequestedStartAndReleasesAllResources(float speed)
    {
        object live = CreateLive(speed, 1.2);
        object voice = Get(live, "Voice");
        int handle = Handle(voice);
        Assert.IsTrue(Native.Attribute(handle, 2, 0)); // Never output test tones.
        Call(voice, "PreparePlayback");
        var samples = new float[4096];
        int read = Native.Read(handle, samples, samples.Length * sizeof(float));
        Assert.Greater(read, 0);
        double energy = 0;
        for (int i = 0; i < read / sizeof(float); i++) energy += samples[i] * samples[i];
        Assert.Greater(energy, .0001, "The new path must produce actual nonzero audio, not just a moving silent clock.");
        Assert.That((double)Get(live, "Start"), Is.EqualTo(1.2).Within(1d / Frequency));
        double mapped = (double)Get(live, "Start") + (double)Get(voice, "Position") * speed;
        Assert.That((double)Get(live, "Timing"), Is.EqualTo(mapped).Within(.0001));
        var worker = (Thread)Field(live, "worker");
        ((IDisposable)live).Dispose();
        Assert.IsFalse(worker.IsAlive);
        Assert.IsNull(Get(live, "Voice"));
        Assert.AreEqual(0, Field(live, "source"));
        Assert.AreEqual(IntPtr.Zero, Field(live, "stretch"));
        Assert.IsFalse(((GCHandle)Field(live, "memory")).IsAllocated);
        ((IDisposable)live).Dispose();
        using (var file = File.Open(wavePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.IsTrue(file.CanWrite);
    }

    [Test]
    public void LiveDevicePositionAdvancesAtTheRequestedSongRate()
    {
        object live = CreateLive(.5f, 1);
        object voice = Get(live, "Voice");
        int handle = Handle(voice);
        Assert.IsTrue(Native.Attribute(handle, 2, 0));
        Call(voice, "PreparePlayback");
        Assert.IsTrue(Native.Play(handle, false));
        Thread.Sleep(100); // Let the output device pass its initial start latency.
        double before = (double)Get(live, "Timing"), clock = BassAudio.Clock;
        Thread.Sleep(100);
        double expected = (BassAudio.Clock - clock) * .5;
        Assert.That((double)Get(live, "Timing") - before, Is.InRange(expected * .4, expected * 1.6));
        Assert.IsFalse((bool)Get(live, "Failed"));
    }

    [Test]
    public void SongEndPrimesWithoutLoopingOrLeakingAWorker()
    {
        object live = CreateLive(.25f, 4);
        object voice = Get(live, "Voice");
        Assert.IsTrue(Native.Attribute(Handle(voice), 2, 0));
        Assert.DoesNotThrow(() => Call(voice, "PreparePlayback"));
        Assert.IsTrue((bool)Field(live, "ended"));
        Assert.That((double)Get(live, "Timing"), Is.EqualTo(4).Within(1d / Frequency));
        var worker = (Thread)Field(live, "worker");
        ((IDisposable)live).Dispose();
        Assert.IsFalse(worker.IsAlive);
        Assert.AreEqual(0, Field(live, "source"));
    }

    private BassClip InitializeSilentManager()
    {
        // Install a fileless silent voice directly: no speculative whole-song cache tasks are queued.
        var clip = BassClip.CreateSilence("live-state-fixture", Frequency * 8, 2, Frequency);
        clips.Add(clip);
        object voice = Activator.CreateInstance(VoiceType, Instance, null, new object[] { clip, true }, null);
        Set(audio, "voice", voice);
        typeof(ArcAudioManager).GetProperty("Clip").SetValue(audio, clip);
        audio.Timing = .5f;
        return clip;
    }

    [Test]
    public void PendingLiveSeekRestartsOnceAndStopPlayReturnsToZero()
    {
        InitializeSilentManager();
        Rate(.5f);
        Assert.IsNotNull(Live);
        audio.Play();
        game.IsPlaying = true;
        Set(game, "audioTiming", .5f);
        Set(game, "lastDspTime", BassAudio.Clock);
        object old = Live;
        var oldWorker = (Thread)Field(old, "worker");
        uint version = audio.VoiceVersion;
        game.ChartTiming = 1500;
        Call(audio, "Update");
        Assert.IsTrue((bool)Field(audio, "liveResume"));
        Assert.That(Native.IsActive(Handle(Active)), Is.Not.EqualTo(1));
        Set(audio, "liveSeekClock", BassAudio.Clock - .06);
        Call(audio, "Update");
        Assert.AreNotSame(old, Live);
        Assert.IsFalse(oldWorker.IsAlive);
        Assert.AreEqual(version + 1, audio.VoiceVersion);
        Assert.IsFalse((bool)Field(audio, "liveSeekPending"));
        Assert.IsFalse((bool)Field(audio, "liveResume"));
        Assert.AreEqual(1, Native.IsActive(Handle(Active)));
        Assert.That(audio.Timing, Is.EqualTo(game.SoundChartTiming / 1000d).Within(.03));
        game.IsPlaying = false;
        audio.Stop();
        Assert.AreEqual(0, audio.Timing);
        Assert.IsFalse((bool)Field(audio, "liveResume"));
        audio.Play();
        Assert.AreEqual(1, Native.IsActive(Handle(Active)));
        Assert.That(audio.Timing, Is.InRange(0, .03));
        Assert.IsNull(Field(audio, "pendingRender"));
    }

    [Test]
    public void CompletedCacheWaitsForPauseBeforeReplacingLivePlayback()
    {
        InitializeSilentManager();
        Rate(.5f);
        var renderedClip = BassClip.CreateSilence("live-cache-fixture", Frequency * 16, 2, Frequency);
        clips.Add(renderedClip);
        EncodeFlac(renderedClip);
        audio.Play();
        object live = Live;
        Assert.IsNotNull(live);
        Set(audio, "pendingRender", Task.FromResult(flacPath));
        Set(audio, "pendingSpeed", .5f);
        Call(audio, "Update");
        Assert.AreSame(live, Live, "A completed cache must not interrupt an already-playing live stream.");
        Assert.IsNotNull(Field(audio, "pendingRender"));
        audio.Pause();
        float before = audio.Timing;
        Call(audio, "Update");
        Assert.IsNull(Live);
        Assert.IsNotNull(Field(audio, "renderedVoice"));
        Assert.IsNull(Field(audio, "pendingRender"));
        Assert.That(audio.Timing, Is.EqualTo(before).Within(.03));
        Assert.That(Native.IsActive(Handle(Active)), Is.Not.EqualTo(1));
        audio.Play();
        Assert.AreEqual(1, Native.IsActive(Handle(Active)));
    }

    [Test]
    public void DisposeHandlesAWorkerWhichNeverStarted()
    {
        object live = Activator.CreateInstance(LiveType, Instance, null, new object[] { .5f }, null);
        streams.Add((IDisposable)live);
        Set(live, "worker", new Thread(() => { }));
        Assert.DoesNotThrow(() => ((IDisposable)live).Dispose());
        Assert.IsNull(Field(live, "worker"));
    }

    private void EncodeFlac(BassClip clip)
    {
        var decoder = (IDisposable)Activator.CreateInstance(VoiceType, Instance, null, new object[] { clip, false }, null);
        int encoder = 0;
        try
        {
            Native.EncoderVersion();
            encoder = Native.StartFlac(Handle(decoder), null, 0x80000004, flacPath);
            Assert.AreNotEqual(0, encoder);
            var buffer = new float[8192];
            while (Native.Read(Handle(decoder), buffer, buffer.Length * sizeof(float)) > 0) { }
            Assert.IsTrue(Native.StopEncoder(encoder));
            encoder = 0;
        }
        finally { if (encoder != 0) Native.StopEncoder(encoder); decoder.Dispose(); }
    }

    private static class Native
    {
        [DllImport("bass", EntryPoint = "BASS_ChannelIsActive")] internal static extern int IsActive(int handle);
        [DllImport("bass", EntryPoint = "BASS_ChannelSetAttribute")] internal static extern bool Attribute(int handle, uint kind, float value);
        [DllImport("bass", EntryPoint = "BASS_ChannelPlay")] internal static extern bool Play(int handle, bool restart);
        [DllImport("bass", EntryPoint = "BASS_ChannelGetData")] internal static extern int Read(int handle, [Out] float[] samples, int bytes);
        [DllImport("bassenc", EntryPoint = "BASS_Encode_GetVersion")] internal static extern uint EncoderVersion();
        [DllImport("bassenc", EntryPoint = "BASS_Encode_Stop")] internal static extern bool StopEncoder(int handle);
        [DllImport("bassenc_flac", EntryPoint = "BASS_Encode_FLAC_StartFile", CharSet = CharSet.Unicode)] internal static extern int StartFlac(int handle, string options, uint flags, string path);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Arcade.Audio;
using NUnit.Framework;
using UnityEngine;

public class MemoryAudioLifecycleTests
{
    private const int Frames = 12345, Frequency = 44100;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<BassClip> clips = new List<BassClip>();
    private string folder, path;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "Affdata-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        path = Path.Combine(folder, "路径 音频.wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(Frames * 4 + 36);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(Frequency);
            writer.Write(Frequency * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(Frames * 4);
            for (int i = 0; i < Frames; i++) { writer.Write(Left(i)); writer.Write((short)-12000); }
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var clip in clips) if (clip) UnityEngine.Object.DestroyImmediate(clip);
        clips.Clear();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private BassClip Load()
    {
        var clip = BassClip.Load(path);
        clips.Add(clip);
        return clip;
    }

    private static short Left(int frame) => (short)((frame % 101 - 50) * 200);
    private static object Field(BassClip clip, string name) => typeof(BassClip).GetField(name, PrivateInstance).GetValue(clip);

    private static void AssertSpectrum(BassClip clip, int frames, Func<int, float> expectedSample)
    {
        var type = typeof(BassClip).GetNestedType("SpectrumReader", BindingFlags.NonPublic);
        using (var reader = (IDisposable)Activator.CreateInstance(type, PrivateInstance, null, new object[] { clip }, null))
        {
            Assert.AreEqual(Frequency, type.GetField("SampleRate", PrivateInstance).GetValue(reader));
            Assert.AreEqual(frames, type.GetField("FrameCount", PrivateInstance).GetValue(reader));
            var read = type.GetMethod("Read", PrivateInstance);
            // Split in the middle of a decoder block and exercise a short final upload.
            var samples = new float[4500];
            for (int offset = 0; offset < frames; offset += samples.Length)
            {
                int count = Math.Min(samples.Length, frames - offset);
                Assert.AreEqual(count, read.Invoke(reader, new object[] { samples, count, CancellationToken.None }));
                for (int i = 0; i < count; i++)
                    Assert.That(samples[i], Is.EqualTo(expectedSample(offset + i)).Within(0.000001));
            }
        }
    }

    [Test]
    public void MusicLengthAndChunkedSpectrumKeepFileBytesUnloaded()
    {
        var clip = Load();
        Assert.That(clip.length, Is.EqualTo(Frames / (double)Frequency).Within(0.0001));
        Assert.That(Field(clip, "externalData"), Is.Null);
        AssertSpectrum(clip, Frames, i => Left(i) / 32768f);
        Assert.That(Field(clip, "externalData"), Is.Null);
    }

    [Test]
    public void DisposingFileVoiceReleasesNativeHandleAndFileLock()
    {
        var clip = Load();
        Type voiceType = typeof(BassClip).Assembly.GetType("Arcade.Audio.BassVoice", true);
        var voice = (IDisposable)Activator.CreateInstance(voiceType, PrivateInstance, null, new object[] { clip, false }, null);
        try
        {
            Assert.IsFalse(((GCHandle)voiceType.GetField("memory", PrivateInstance).GetValue(voice)).IsAllocated);
            Assert.That((int)voiceType.GetProperty("Handle", PrivateInstance).GetValue(voice), Is.Not.Zero);
        }
        finally { voice.Dispose(); }
        Assert.AreEqual(0, voiceType.GetProperty("Handle", PrivateInstance).GetValue(voice));
        voice.Dispose(); // Repeated cleanup must be safe.
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.IsTrue(stream.CanWrite);
    }

    [Test]
    public void FileReuseDetectsModifiedAudioInsteadOfKeepingStaleSource()
    {
        var clip = Load();
        var matches = typeof(BassClip).GetMethod("MatchesFile", PrivateInstance);
        Assert.AreEqual(true, matches.Invoke(clip, new object[] { path }));
        Assert.AreEqual(false, matches.Invoke(clip, new object[] { Path.Combine(folder, "other.wav") }));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
        Assert.AreEqual(false, matches.Invoke(clip, new object[] { path }));
    }

    [Test]
    public void EmbeddedSilenceStillSupportsChunkedDecode()
    {
        var clip = BassClip.CreateSilence("embedded silence", 5000, 2, Frequency);
        clips.Add(clip);
        AssertSpectrum(clip, 5000, i => 0);
    }

    [Test]
    public void ClearingCacheCancelsQueuedRendersAndPreservesOtherFiles()
    {
        var clip = Load();
        var cache = typeof(BassClip).Assembly.GetType("Arcade.Audio.SlowAudioCache", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var folderField = cache.GetField("folder", flags);
        object previousFolder = folderField.GetValue(null);
        string cacheFolder = Path.Combine(folder, "SlowAudio");
        Directory.CreateDirectory(cacheFolder);
        Directory.CreateDirectory(Path.Combine(cacheFolder, "keep"));
        File.WriteAllText(Path.Combine(cacheFolder, "song.flac"), "cached");
        File.WriteAllText(Path.Combine(cacheFolder, "song.flac.part"), "unfinished");
        File.WriteAllText(Path.Combine(cacheFolder, "settings.json"), "keep");
        File.WriteAllText(Path.Combine(cacheFolder, "keep", "song.flac"), "keep");
        try
        {
            folderField.SetValue(null, cacheFolder);
            cache.GetMethod("BeginSong", flags).Invoke(null, new object[] { clip });
            cache.GetMethod("SetPlaybackState", flags).Invoke(null, new object[] { true, 1f });
            var render = (System.Threading.Tasks.Task<string>)cache.GetMethod("Request", flags)
                .Invoke(null, new object[] { clip, .5f });
            Assert.AreEqual(2, cache.GetMethod("Clear", flags).Invoke(null, null));
            Assert.IsTrue(render.IsCompleted);
            Assert.IsNull(render.Result);
            Assert.IsFalse(File.Exists(Path.Combine(cacheFolder, "song.flac")));
            Assert.IsFalse(File.Exists(Path.Combine(cacheFolder, "song.flac.part")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheFolder, "settings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheFolder, "keep", "song.flac")));
            Assert.AreEqual(0, cache.GetMethod("Clear", flags).Invoke(null, null));
        }
        finally
        {
            cache.GetMethod("Shutdown", flags).Invoke(null, null);
            folderField.SetValue(null, previousFolder);
        }
    }

    [Test]
    public void ShutdownCancelsQueuedSlowRendersBeforeDeviceRelease()
    {
        var clip = Load();
        var cache = typeof(BassClip).Assembly.GetType("Arcade.Audio.SlowAudioCache", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        cache.GetMethod("BeginSong", flags).Invoke(null, new object[] { clip });
        cache.GetMethod("SetPlaybackState", flags).Invoke(null, new object[] { true, 1f });
        var request = cache.GetMethod("Request", flags);
        var renders = new List<System.Threading.Tasks.Task<string>>();
        try
        {
            foreach (float speed in new[] { .75f, .5f, .25f })
                renders.Add((System.Threading.Tasks.Task<string>)request.Invoke(null, new object[] { clip, speed }));
        }
        finally { cache.GetMethod("Shutdown", flags).Invoke(null, null); }
        foreach (var render in renders)
        {
            Assert.IsTrue(render.IsCompleted, "Shutdown must wait for every owned render task.");
            Assert.IsNull(render.Result, "Cancelled work must not touch BASS after shutdown.");
        }
    }
}

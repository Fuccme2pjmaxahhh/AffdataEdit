using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

    [Test]
    public void MusicLengthAndChunkedSpectrumKeepFileBytesUnloaded()
    {
        var clip = Load();
        Assert.That(clip.length, Is.EqualTo(Frames / (double)Frequency).Within(0.0001));
        Assert.That(Field(clip, "externalData"), Is.Null);
        int offset = 0, calls = 0;
        float[] firstBuffer = null;
        Action<int, int> begin = (rate, count) => { Assert.AreEqual(Frequency, rate); Assert.AreEqual(Frames, count); };
        Action<float[], int, int> write = (buffer, destination, count) =>
        {
            Assert.AreEqual(offset, destination);
            if (firstBuffer == null) firstBuffer = buffer;
            else Assert.AreSame(firstBuffer, buffer, "Spectrum chunks must reuse the bounded buffer.");
            for (int i = 0; i < count; i++) Assert.That(buffer[i], Is.EqualTo(Left(offset + i) / 32768f).Within(0.000001));
            offset += count;
            calls++;
        };
        int actual = (int)typeof(BassClip).GetMethod("ReadSpectrumSamples", PrivateInstance).Invoke(clip, new object[] { begin, write });
        Assert.AreEqual(Frames, actual);
        Assert.AreEqual(Frames, offset);
        Assert.Greater(calls, 1);
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
        int written = 0;
        Action<int, int> begin = (rate, count) => { Assert.AreEqual(Frequency, rate); Assert.AreEqual(5000, count); };
        Action<float[], int, int> write = (buffer, destination, count) =>
        {
            Assert.AreEqual(written, destination);
            for (int i = 0; i < count; i++) Assert.AreEqual(0, buffer[i]);
            written += count;
        };
        Assert.AreEqual(5000, typeof(BassClip).GetMethod("ReadSpectrumSamples", PrivateInstance).Invoke(clip, new object[] { begin, write }));
        Assert.AreEqual(5000, written);
    }
}

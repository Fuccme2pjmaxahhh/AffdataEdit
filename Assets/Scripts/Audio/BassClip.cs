using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Arcade.Audio
{
    public sealed class BassClip : ScriptableObject
    {
        private string filePath;
        private byte[] externalData;
        private float? duration;
        private long fileSize, modifiedTicks;
        // Null when playback, spectrum and slow rendering should use Bytes.
        internal string FilePath { get; private set; }
        internal bool IsOpusFile { get; private set; }
        internal byte[] Bytes
        {
            get
            {
                if (externalData == null)
                {
                    byte[] data = File.ReadAllBytes(filePath);
                    externalData = IsOpusFile ? DecodeOpus(data) : data;
                }
                return externalData;
            }
        }
        internal bool MatchesFile(string path)
        {
            if (filePath == null || !string.Equals(filePath, Path.GetFullPath(path), System.StringComparison.OrdinalIgnoreCase)) return false;
            var info = new FileInfo(path);
            return info.Exists && info.Length == fileSize && info.LastWriteTimeUtc.Ticks == modifiedTicks;
        }
        // A validated cloud download is moved as a whole before playback opens a new voice.
        internal void SetMovedSourceFolder(string folder)
        {
            filePath = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(filePath)));
            if (FilePath != null) FilePath = filePath;
            name = filePath;
        }
        public float length
        {
            get
            {
                if (!duration.HasValue)
                    using (var voice = new BassVoice(this, false)) duration = (float)voice.Length;
                return duration.Value;
            }
        }
        public static BassClip Load(string path) => Load(path, null);

        internal static BassClip Load(string path, string projectBackupFolder)
        {
            if (!File.Exists(path)) return null;
            var clip = CreateInstance<BassClip>();
            clip.name = path;
            clip.filePath = Path.GetFullPath(path);
            try
            {
                var info = new FileInfo(path);
                clip.fileSize = info.Length;
                clip.modifiedTicks = info.LastWriteTimeUtc.Ticks;
                var header = new byte[36];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    stream.Read(header, 0, header.Length);
                    if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
                    {
                        stream.Position = 0;
                        clip.externalData = OggAudioTrack.ExtractTheoraVorbis(stream, out bool onlyVideoRemoved);
                        if (onlyVideoRemoved && projectBackupFolder != null)
                        {
                            try { clip.ReplaceWithAudioTrack(info, projectBackupFolder); }
                            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException ||
                                e is System.NotSupportedException)
                            {
                                Debug.Log($"未能原地清理「{info.Name}」，本次使用内存音轨。\n{e}");
                            }
                        }
                    }
                }
                clip.FilePath = clip.externalData == null ? clip.filePath : null;
                clip.IsOpusFile = clip.FilePath != null && IsOpus(header);
                _ = clip.length;
                return clip;
            }
            catch (System.Exception e) when (e is IOException || e is InvalidDataException ||
                e is System.UnauthorizedAccessException || e is System.InvalidOperationException)
            {
                Destroy(clip);
                throw new InvalidDataException($"无法读取音频「{Path.GetFileName(path)}」\n{e.Message}", e);
            }
            catch { Destroy(clip); throw; }
        }

        private void ReplaceWithAudioTrack(FileInfo original, string backupFolder)
        {
            if ((original.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
            {
                Debug.Log($"「{original.Name}」为只读文件或链接，本次使用内存音轨。");
                return;
            }

            string id = System.Guid.NewGuid().ToString("N");
            string temporary = Path.Combine(original.DirectoryName, $".affdata-{id}.tmp");
            string backup = Path.Combine(backupFolder, $"{Path.GetFileNameWithoutExtension(filePath)}-{id}{original.Extension}");
            var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (output)
                {
                    output.Write(externalData, 0, externalData.Length);
                    output.Flush(true);
                }
                FilePath = temporary;
                using (var voice = new BassVoice(this, false))
                {
                    var buffer = new byte[65536];
                    int read = BassNative.BASS_ChannelGetData(voice.Handle, buffer, buffer.Length);
                    BassNative.Check(read > 0, "validate extracted audio");
                    do { read = BassNative.BASS_ChannelGetData(voice.Handle, buffer, buffer.Length); } while (read > 0);
                    BassNative.Check(read < 0 && BassNative.BASS_ErrorGetCode() == 45, "finish validating extracted audio");
                    duration = (float)voice.Length;
                }
                Directory.CreateDirectory(backupFolder);
                if (!MatchesFile(filePath)) throw new IOException("读取期间源文件已更改。");
                try { File.Replace(temporary, filePath, backup); }
                catch (IOException) when (!File.Exists(filePath) && File.Exists(backup))
                {
                    // Windows can move the original to the backup before a replacement fails.
                    File.Move(backup, filePath);
                    throw;
                }
                original.Refresh();
                fileSize = original.Length;
                modifiedTicks = original.LastWriteTimeUtc.Ticks;
                externalData = null;
                Debug.Log($"已清理「{original.Name}」的视频轨，原文件已备份至 {backup}");
            }
            finally
            {
                FilePath = null;
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        public static BassClip CreateSilence(string name, int frames, int channels, int frequency)
        {
            var clip = CreateInstance<BassClip>();
            clip.name = name;
            clip.externalData = Wav(new byte[checked(frames * channels * 2)], channels, frequency);
            return clip;
        }
        // Stream the first channel in bounded blocks, independently of playback and tempo.
        internal sealed class SpectrumReader : System.IDisposable
        {
            private readonly BassVoice voice;
            private readonly int channels;
            private readonly float[] interleaved;
            internal readonly int SampleRate, FrameCount;

            internal SpectrumReader(BassClip source)
            {
                voice = new BassVoice(source, false);
                try
                {
                    BassNative.Check(BassNative.BASS_ChannelGetInfo(voice.Handle, out var info), "spectrum info");
                    long bytes = BassNative.BASS_ChannelGetLength(voice.Handle, 0);
                    BassNative.Check(bytes >= 0, "spectrum length");
                    channels = info.Channels;
                    SampleRate = info.Frequency;
                    FrameCount = checked((int)(bytes / sizeof(float) / channels));
                    interleaved = new float[4096 * channels];
                }
                catch { voice.Dispose(); throw; }
            }

            internal int Read(float[] samples, int count, CancellationToken cancellation)
            {
                int written = 0;
                while (written < count)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int frames = System.Math.Min(4096, count - written);
                    int read = BassNative.BASS_ChannelGetData(voice.Handle, interleaved, frames * channels * sizeof(float));
                    if (read < 0 && BassNative.BASS_ErrorGetCode() == 45) break; // BASS_ERROR_ENDED
                    BassNative.Check(read > 0, "spectrum decode");
                    frames = read / sizeof(float) / channels;
                    BassNative.Check(frames > 0, "spectrum frames");
                    for (int i = 0; i < frames; i++) samples[written + i] = interleaved[i * channels];
                    written += frames;
                }
                return written;
            }

            public void Dispose() => voice.Dispose();
        }
        private static byte[] Wav(byte[] pcm, int channels, int frequency)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(pcm.Length + 36);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(frequency);
                writer.Write(frequency * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
                return stream.ToArray();
            }
        }
        // Ogg pages whose first packet is an OpusHead identification header.
        private static bool IsOpus(byte[] data)
        {
            return data.Length >= 36 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S'
                && System.Text.Encoding.ASCII.GetString(data, 28, 8) == "OpusHead";
        }
        // BASS core cannot decode Opus, so bassopus decodes it once into 16-bit PCM WAV;
        // streams and samples then use the WAV bytes unchanged.
        private static byte[] DecodeOpus(byte[] data)
        {
            BassAudio.EnsureInitialized();
            var memory = GCHandle.Alloc(data, GCHandleType.Pinned);
            int handle = 0;
            try
            {
                handle = BassNative.BASS_OPUS_StreamCreateFile(true, memory.AddrOfPinnedObject(), 0, data.LongLength, BassNative.Decode);
                BassNative.Check(handle != 0, "opus decode");
                BassNative.Check(BassNative.BASS_ChannelGetInfo(handle, out BassNative.ChannelInfo info), "opus info");
                using (var pcm = new MemoryStream())
                {
                    var buffer = new byte[65536];
                    int read;
                    while ((read = BassNative.BASS_ChannelGetData(handle, buffer, buffer.Length)) > 0) pcm.Write(buffer, 0, read);
                    return Wav(pcm.ToArray(), info.Channels, info.Frequency);
                }
            }
            finally
            {
                if (handle != 0) BassNative.BASS_StreamFree(handle);
                memory.Free();
            }
        }
        private void OnDestroy() { BassAudio.ReleaseSample(this); externalData = null; }
    }
}

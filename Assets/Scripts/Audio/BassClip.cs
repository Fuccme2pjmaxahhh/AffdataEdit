using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Arcade.Audio
{
    public sealed class BassClip : ScriptableObject
    {
        private string filePath;
        private byte[] externalData;
        private float? duration;
        private long fileSize, modifiedTicks;
        internal string FilePath => filePath;
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
        public float length
        {
            get
            {
                if (!duration.HasValue)
                    using (var voice = new BassVoice(this, false)) duration = (float)voice.Length;
                return duration.Value;
            }
        }
        public static BassClip Load(string path)
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
                // Music stays file-backed, including Opus; only short sample playback asks for Bytes.
                var header = new byte[36];
                using (var stream = File.OpenRead(path)) stream.Read(header, 0, header.Length);
                clip.IsOpusFile = IsOpus(header);
                _ = clip.length;
                return clip;
            }
            catch { Destroy(clip); throw; }
        }
        public static BassClip CreateSilence(string name, int frames, int channels, int frequency)
        {
            var clip = CreateInstance<BassClip>();
            clip.name = name;
            clip.externalData = Wav(new byte[checked(frames * channels * 2)], channels, frequency);
            return clip;
        }
        // A separate decode stream keeps spectrum reads independent of playback and tempo.
        internal int ReadSpectrumSamples(System.Action<int, int> begin, System.Action<float[], int, int> write)
        {
            using (var voice = new BassVoice(this, false))
            {
                BassNative.Check(BassNative.BASS_ChannelGetInfo(voice.Handle, out BassNative.ChannelInfo info), "spectrum info");
                long bytes = BassNative.BASS_ChannelGetLength(voice.Handle, 0);
                BassNative.Check(bytes >= 0, "spectrum length");
                int expected = checked((int)(bytes / sizeof(float) / info.Channels));
                begin(info.Frequency, expected);
                var buffer = new float[4096 * info.Channels];
                var mono = new float[4096];
                int offset = 0;
                while (offset < expected)
                {
                    int count = System.Math.Min(buffer.Length, (expected - offset) * info.Channels);
                    int read = BassNative.BASS_ChannelGetData(voice.Handle, buffer, count * sizeof(float));
                    if (read < 0 && BassNative.BASS_ErrorGetCode() == 45) break; // BASS_ERROR_ENDED
                    BassNative.Check(read > 0, "spectrum decode");
                    int frames = read / sizeof(float) / info.Channels;
                    BassNative.Check(frames > 0, "spectrum frames");
                    // Alpha analyzes the first channel without averaging stereo channels.
                    for (int i = 0; i < frames; i++) mono[i] = buffer[i * info.Channels];
                    write(mono, offset, frames);
                    offset += frames;
                }
                return offset;
            }
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

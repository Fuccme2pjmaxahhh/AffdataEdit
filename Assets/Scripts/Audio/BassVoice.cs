using System;
using System.Runtime.InteropServices;

namespace Arcade.Audio
{
    internal sealed class BassVoice : IDisposable
    {
        private const uint AttribFrequency = 1; // BASS_ATTRIB_FREQ
        private GCHandle memory;
        private readonly float baseFrequency;
        internal int Handle { get; private set; }
        internal double Length => BassNative.BASS_ChannelBytes2Seconds(Handle, BassNative.BASS_ChannelGetLength(Handle, 0));
        internal double Position
        {
            get => BassNative.BASS_ChannelBytes2Seconds(Handle, BassNative.BASS_ChannelGetPosition(Handle, 0));
            set => BassNative.Check(BassNative.BASS_ChannelSetPosition(Handle,
                BassNative.BASS_ChannelSeconds2Bytes(Handle, Math.Max(0, Math.Min(value, Length))), 0), "seek");
        }
        // playback: a playable music stream; otherwise a decode stream for length and spectrum reads.
        internal BassVoice(BassClip clip, bool playback)
        {
            BassAudio.EnsureInitialized();
            uint flags = playback ? BassNative.Float : BassNative.Decode | BassNative.Float;
            if (clip.FilePath != null)
            {
                Handle = clip.IsOpusFile
                    ? BassNative.BASS_OPUS_StreamCreateFile(false, clip.FilePath, 0, 0, flags | BassNative.Unicode)
                    : BassNative.BASS_StreamCreateFile(false, clip.FilePath, 0, 0, flags | BassNative.Unicode);
            }
            else
            {
                var bytes = clip.Bytes;
                memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                Handle = BassNative.BASS_StreamCreateFile(true, memory.AddrOfPinnedObject(), 0, bytes.LongLength, flags);
            }
            if (Handle == 0) { if (memory.IsAllocated) memory.Free(); BassNative.Check(false, "decode"); }
            if (playback && BassNative.BASS_ChannelGetInfo(Handle, out BassNative.ChannelInfo info)) baseFrequency = info.Frequency;
        }
        private BassVoice(int handle) { Handle = handle; }
        // Takes ownership of an existing playable stream (for example a push stream).
        internal static BassVoice FromHandle(int handle) => new BassVoice(handle);
        // A pre-rendered slow-playback file, streamed from disk at its own rate.
        internal static BassVoice OpenRendered(string path)
        {
            BassAudio.EnsureInitialized();
            int handle = BassNative.BASS_FLAC_StreamCreateFile(false, path, 0, 0, BassNative.Float | BassNative.Unicode);
            BassNative.Check(handle != 0, "open rendered audio");
            return new BassVoice(handle);
        }
        // Fallback while no pitch-preserving render is ready: resample like a slowed record (lower pitch).
        internal void SetSpeed(float speed)
        {
            if (baseFrequency > 0)
                BassNative.Check(BassNative.BASS_ChannelSetAttribute(Handle, AttribFrequency, baseFrequency * speed), "playback speed");
        }
        // Prime decoded data before restarting the transport clock. Otherwise the first update
        // after a seek can spend time filling the stream while the chart clock is already moving.
        internal void PreparePlayback()
        {
            if (BassNative.BASS_ChannelUpdate(Handle, 100)) return;
            int error = BassNative.BASS_ErrorGetCode();
            // The automatic update thread may already be filling it; an end-position seek has
            // no samples left to prime. Neither condition is a failed stream.
            if (error != 14 && error != 45) BassNative.Check(false, "prepare music buffer");
        }
        public void Dispose()
        {
            if (Handle != 0) { BassNative.BASS_StreamFree(Handle); Handle = 0; }
            if (memory.IsAllocated) memory.Free();
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace Arcade.Audio
{
    // BASS 2.4 ABI; constants/signatures follow AffdataPlay's ManagedBass bindings.
    internal static class BassNative
    {
        internal const uint Decode = 0x200000, Float = 0x100, FreeSource = 0x10000, Unicode = 0x80000000;
        [DllImport("bass")] internal static extern bool BASS_Init(int device, uint frequency, uint flags, IntPtr window, IntPtr clsid);
        [DllImport("bass")] internal static extern bool BASS_Free();
        // Device info; latency is only measured when BASS_Init gets DeviceLatency.
        internal const uint DeviceLatency = 0x100;
        [StructLayout(LayoutKind.Sequential)]
        internal struct Info
        {
            public uint flags, hwsize, hwfree, freesam, free3d, minrate, maxrate;
            public int eax;
            public uint minbuf, dsver, latency, initflags, speakers, freq;
        }
        [DllImport("bass")] internal static extern bool BASS_GetInfo(out Info info);
        [DllImport("bass")] internal static extern int BASS_ErrorGetCode();
        [DllImport("bass")] internal static extern bool BASS_SetConfig(uint option, uint value);
        [DllImport("bass")] internal static extern int BASS_StreamCreateFile(bool memory, IntPtr data, long offset, long length, uint flags);
        [DllImport("bass", CharSet = CharSet.Unicode)] internal static extern int BASS_StreamCreateFile(bool memory, string file, long offset, long length, uint flags);
        [DllImport("bass")] internal static extern bool BASS_StreamFree(int handle);
        [DllImport("bass")] internal static extern int BASS_StreamCreate(uint frequency, uint channels, uint flags, IntPtr proc, IntPtr user);
        [DllImport("bass")] internal static extern int BASS_StreamPutData(int handle, float[] buffer, int length);
        [DllImport("bass")] internal static extern long BASS_ChannelGetLength(int handle, uint mode);
        [DllImport("bass")] internal static extern long BASS_ChannelGetPosition(int handle, uint mode);
        [DllImport("bass")] internal static extern double BASS_ChannelBytes2Seconds(int handle, long bytes);
        [DllImport("bass")] internal static extern long BASS_ChannelSeconds2Bytes(int handle, double seconds);
        [DllImport("bass")] internal static extern bool BASS_ChannelSetPosition(int handle, long bytes, uint mode);
        [DllImport("bass")] internal static extern bool BASS_ChannelSetAttribute(int handle, uint attribute, float value);
        [DllImport("bass")] internal static extern bool BASS_ChannelPlay(int handle, bool restart);
        [DllImport("bass")] internal static extern bool BASS_ChannelPause(int handle);
        [DllImport("bass")] internal static extern bool BASS_ChannelUpdate(int handle, uint length);
        [DllImport("bass")] internal static extern bool BASS_ChannelStop(int handle);
        [DllImport("bass")] internal static extern int BASS_ChannelIsActive(int handle);
        [DllImport("bass")] internal static extern int BASS_SampleLoad(bool memory, IntPtr data, long offset, uint length, uint max, uint flags);
        [DllImport("bass")] internal static extern int BASS_SampleGetChannel(int sample, bool onlyNew);
        [DllImport("bass")] internal static extern bool BASS_SampleFree(int sample);
        [DllImport("bass")] internal static extern bool BASS_ChannelGetInfo(int handle, out ChannelInfo info);
        [DllImport("bass")] internal static extern int BASS_ChannelGetData(int handle, byte[] buffer, int length);
        [DllImport("bass")] internal static extern int BASS_ChannelGetData(int handle, [Out] float[] buffer, int length);
        [DllImport("bass_fx")] internal static extern int BASS_FX_TempoCreate(int source, uint flags);
        [DllImport("bassopus")] internal static extern int BASS_OPUS_StreamCreateFile(bool memory, IntPtr data, long offset, long length, uint flags);
        [DllImport("bassopus", CharSet = CharSet.Unicode)] internal static extern int BASS_OPUS_StreamCreateFile(bool memory, string file, long offset, long length, uint flags);
        // Rendered slow-playback cache (SlowAudioCache): FLAC playback and encoding add-ons.
        [DllImport("bassflac", CharSet = CharSet.Unicode)] internal static extern int BASS_FLAC_StreamCreateFile(bool memory, string file, long offset, long length, uint flags);
        [DllImport("bassenc")] internal static extern uint BASS_Encode_GetVersion();
        [DllImport("bassenc")] internal static extern bool BASS_Encode_Stop(int handle);
        [DllImport("bassenc_flac", CharSet = CharSet.Unicode)] internal static extern int BASS_Encode_FLAC_StartFile(int handle, string options, uint flags, string file);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ChannelInfo
        {
            public int Frequency, Channels, Flags, ChannelType, OriginalResolution, Plugin, Sample;
            public IntPtr FileName;
        }

        internal static void Check(bool success, string operation)
        {
            if (!success) throw new InvalidOperationException($"BASS {operation}: error {BASS_ErrorGetCode()}");
        }
    }
}

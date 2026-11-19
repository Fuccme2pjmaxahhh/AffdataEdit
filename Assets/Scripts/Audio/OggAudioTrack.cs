using System;
using System.Collections.Generic;
using System.IO;

namespace Arcade.Audio
{
    internal static class OggAudioTrack
    {
        // All track identification pages precede the data pages, in either track order.
        internal static byte[] ExtractTheoraVorbis(Stream input, out bool onlyVideoRemoved)
        {
            onlyVideoRemoved = false;
            var header = new byte[27];
            if (input.Length < header.Length) return null;
            ReadFully(input, header, header.Length);
            if (!IsOggPage(header) || (header[5] & 2) == 0) return null;

            var segments = new byte[255];
            var payload = Array.Empty<byte>();
            using var audio = new MemoryStream();
            int? selected = null;
            var videoStreams = new HashSet<int>();
            bool safeToReplace = true;
            bool firstLink = true;
            bool inHeaders = true;
            bool audioEnded = false;
            while (true)
            {
                bool bos = (header[5] & 2) != 0;
                if (!bos && inHeaders)
                {
                    if (firstLink && (videoStreams.Count == 0 || !selected.HasValue)) return null;
                    if (!selected.HasValue) throw new InvalidDataException("后续 Ogg 分段中缺少 Vorbis 音轨。");
                }
                if (bos && !inHeaders)
                {
                    // A chained link has fresh stream serials.
                    if (!audioEnded) throw new InvalidDataException("Ogg 音轨不完整。");
                    selected = null;
                    videoStreams.Clear();
                    firstLink = false;
                    audioEnded = false;
                }
                inHeaders = bos;

                int count = header[26];
                ReadFully(input, segments, count);
                int size = 0;
                for (int i = 0; i < count; i++) size += segments[i];
                int serial = BitConverter.ToInt32(header, 14);
                if (bos || selected == serial)
                {
                    if (payload.Length < size) payload = new byte[size];
                    ReadFully(input, payload, size);
                }
                else
                {
                    if (size > input.Length - input.Position) throw new EndOfStreamException("Ogg 文件不完整。");
                    input.Position += size;
                }
                if (bos)
                {
                    var packet = payload.AsSpan(0, count == 0 ? 0 : segments[0]);
                    if (selected == serial || videoStreams.Contains(serial)) safeToReplace = false;
                    if (packet.StartsWith(TheoraHeader)) videoStreams.Add(serial);
                    else if (!selected.HasValue && packet.StartsWith(VorbisHeader)) selected = serial;
                    else safeToReplace = false;
                }
                if (serial == selected)
                {
                    audioEnded = (header[5] & 4) != 0;
                    audio.Write(header, 0, header.Length);
                    audio.Write(segments, 0, count);
                    audio.Write(payload, 0, size);
                }
                else if (!videoStreams.Contains(serial)) safeToReplace = false;

                if (input.Position == input.Length) break;
                ReadFully(input, header, header.Length);
                if (!IsOggPage(header)) throw new InvalidDataException("Ogg 页头无效。");
            }
            if ((firstLink && videoStreams.Count == 0) || audio.Length == 0) return null;
            if (!audioEnded) throw new InvalidDataException("Ogg 音轨不完整。");
            onlyVideoRemoved = safeToReplace;
            return audio.ToArray();
        }

        private static readonly byte[] VorbisHeader = System.Text.Encoding.ASCII.GetBytes("\u0001vorbis");
        private static readonly byte[] TheoraHeader = { 0x80, (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a' };
        private static bool IsOggPage(byte[] header) =>
            header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S' && header[4] == 0;

        private static void ReadFully(Stream input, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Ogg 文件不完整。");
                offset += read;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Arcade.Audio
{
    // Pitch-preserving slow playback (Arcade Alpha style): each song is rendered once per speed by
    // the AffStretch native plugin (resample + Signalsmith pitch shift, processing delay trimmed),
    // cached as FLAC and streamed from disk, so playback needs no delay compensation.
    internal static class SlowAudioCache
    {
        private const long CacheLimit = 1L << 30;
        private const int RenderVersion = 1; // change when the render output changes
        private const int Chunk = 16384;
        private const uint EncodeFloatTo16Bit = 4, EncodeDither = 0x8000, ErrorEnded = 45; // BASS_ERROR_ENDED
        private static readonly IntPtr StreamProcPush = new IntPtr(-1);
        private static readonly Dictionary<string, Task<string>> renders = new Dictionary<string, Task<string>>();
        private static Task queue = Task.CompletedTask;
        private static string folder, currentSong;
        private static int generation;
        private static volatile bool unavailable;
        private static volatile string runningPath;
        private static float runningFraction;
        private static volatile bool playbackActive;
        private static float playbackSpeed = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            renders.Clear();
            queue = Task.CompletedTask;
            folder = currentSong = null;
            runningPath = null;
            Interlocked.Increment(ref generation);
            unavailable = false;
            playbackActive = false;
            playbackSpeed = 1;
        }

        // Next to AffdataEdit.exe so it survives rebuilds and stays out of chart folders.
        private static string Folder => folder ?? (folder = Path.GetFullPath(Application.isEditor
            ? Path.Combine(Application.temporaryCachePath, "SlowAudio")
            : Path.Combine(Path.GetDirectoryName(Application.dataPath), "Cache", "SlowAudio")));
        internal static string CacheFolder => Folder;

        // Keyed on the song file's path, size and modified time, so a replaced song renders again.
        private static string PathFor(BassClip clip, float speed)
        {
            try
            {
                var info = new FileInfo(clip.name);
                if (!info.Exists) return null;
                string id = $"{RenderVersion}|{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                using (var sha = SHA1.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").Substring(0, 20);
                    return Path.Combine(Folder, $"{hash.ToLowerInvariant()}_{Mathf.RoundToInt(speed * 100)}.flac");
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is NotSupportedException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal static string TryGet(BassClip clip, float speed)
        {
            string path = clip ? PathFor(clip, speed) : null;
            if (path == null || !File.Exists(path)) return null;
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return path;
        }

        // Starts (or joins) a background render; the task yields the cached file, or null on failure.
        internal static Task<string> Request(BassClip clip, float speed)
        {
            if (unavailable || !clip) return null;
            string path = PathFor(clip, speed);
            if (path == null) return null;
            if (renders.TryGetValue(path, out var running) && !running.IsCompleted) return running;
            string sourcePath = clip.FilePath;
            bool opus = clip.IsOpusFile;
            byte[] bytes = sourcePath == null ? clip.Bytes : null;
            int owner = Volatile.Read(ref generation);
            var task = queue.ContinueWith(_ => RenderSafely(sourcePath, opus, bytes, speed, path, owner), TaskScheduler.Default);
            queue = task;
            renders[path] = task;
            return task;
        }

        internal static string CachePath(BassClip clip, float speed) => clip ? PathFor(clip, speed) : null;

        // 0..1 for the render currently writing `path`; 0 while it is still queued.
        internal static float ProgressOf(string path) => path == runningPath ? Volatile.Read(ref runningFraction) : 0;

        // Speculative cache work yields while normal-speed playback is active. A requested slow
        // speed may still finish its serial queue so it can replace the temporary resampled voice.
        internal static void SetPlaybackState(bool playing, float speed)
        {
            Volatile.Write(ref playbackSpeed, speed);
            playbackActive = playing;
        }

        private static void WaitForRender(int owner)
        {
            while (true)
            {
                if (owner != Volatile.Read(ref generation)) throw new OperationCanceledException();
                if (!playbackActive || Volatile.Read(ref playbackSpeed) < 1) return;
                Thread.Sleep(100);
            }
        }

        // Called on every chart load. Reloading the same song file keeps its renders running;
        // a different (or changed) song stops the old song's queued and running renders early.
        internal static void BeginSong(BassClip clip)
        {
            string song = CachePath(clip, 1);
            if (song != null && song == currentSong) return;
            currentSong = song;
            Interlocked.Increment(ref generation);
            renders.Clear();
        }

        private static string RenderSafely(string sourcePath, bool opus, byte[] bytes, float speed, string path, int owner)
        {
            Thread thread = Thread.CurrentThread;
            System.Threading.ThreadPriority priority = thread.Priority;
            try
            {
                thread.Priority = System.Threading.ThreadPriority.BelowNormal;
                WaitForRender(owner);
                return Render(sourcePath, opus, bytes, speed, path, owner);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                if (e is DllNotFoundException || e is EntryPointNotFoundException) unavailable = true;
                Debug.LogWarning($"Slow playback render failed, keeping pitch-lowered playback: {e.Message}");
                return null;
            }
            finally { thread.Priority = priority; }
        }

        private static string Render(string sourcePath, bool opus, byte[] bytes, float speed, string path, int owner)
        {
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".part";
            GCHandle memory = default;
            int source = 0, push = 0, encoder = 0;
            IntPtr stretch = IntPtr.Zero;
            bool complete = false;
            Volatile.Write(ref runningFraction, 0);
            runningPath = path;
            try
            {
                if (sourcePath != null)
                {
                    uint flags = BassNative.Decode | BassNative.Float | BassNative.Unicode;
                    source = opus ? BassNative.BASS_OPUS_StreamCreateFile(false, sourcePath, 0, 0, flags)
                        : BassNative.BASS_StreamCreateFile(false, sourcePath, 0, 0, flags);
                }
                else
                {
                    memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    source = BassNative.BASS_StreamCreateFile(true, memory.AddrOfPinnedObject(), 0, bytes.LongLength, BassNative.Decode | BassNative.Float);
                }
                BassNative.Check(source != 0, "open song for slow render");
                BassNative.Check(BassNative.BASS_ChannelGetInfo(source, out BassNative.ChannelInfo info), "slow render info");
                int channels = info.Channels;
                double totalBytes = Math.Max(1, BassNative.BASS_ChannelGetLength(source, 0));
                long decodedBytes = 0;
                stretch = AffStretchNative.AffStretch_Create(channels, info.Frequency, speed);
                if (stretch == IntPtr.Zero) throw new InvalidOperationException("AffStretch rejected the audio format");
                push = BassNative.BASS_StreamCreate((uint)info.Frequency, (uint)channels, BassNative.Decode | BassNative.Float, StreamProcPush, IntPtr.Zero);
                BassNative.Check(push != 0, "slow render stream");
                BassNative.BASS_Encode_GetVersion(); // load bassenc before its FLAC add-on
                encoder = BassNative.BASS_Encode_FLAC_StartFile(push, null, EncodeFloatTo16Bit | EncodeDither | BassNative.Unicode, temp);
                BassNative.Check(encoder != 0, "start FLAC encoder");

                var input = new float[Chunk * channels];
                int capacity = (int)(Chunk / speed) + 8192;
                var output = new float[capacity * channels];
                var scratch = new float[capacity * channels];
                while (true)
                {
                    WaitForRender(owner);
                    int read = BassNative.BASS_ChannelGetData(source, input, input.Length * sizeof(float));
                    if (read < 0 && BassNative.BASS_ErrorGetCode() == ErrorEnded) break;
                    BassNative.Check(read >= 0, "decode song for slow render");
                    if (read == 0) break;
                    int frames = read / sizeof(float) / channels;
                    decodedBytes += read;
                    Volatile.Write(ref runningFraction, (float)Math.Min(1, decodedBytes / totalBytes));
                    Encode(push, output, scratch, channels, AffStretchNative.AffStretch_Process(stretch, input, frames, output, capacity));
                }
                int tail;
                while (true)
                {
                    WaitForRender(owner);
                    tail = AffStretchNative.AffStretch_Finish(stretch, output, capacity);
                    if (tail <= 0) break;
                    Encode(push, output, scratch, channels, tail);
                }
                if (tail < 0) throw new InvalidOperationException("AffStretch failed to finish");
                BassNative.Check(BassNative.BASS_Encode_Stop(encoder), "finish FLAC file");
                encoder = 0;
                complete = true;
            }
            finally
            {
                runningPath = null;
                if (encoder != 0) BassNative.BASS_Encode_Stop(encoder);
                if (push != 0) BassNative.BASS_StreamFree(push);
                if (source != 0) BassNative.BASS_StreamFree(source);
                if (stretch != IntPtr.Zero) AffStretchNative.AffStretch_Destroy(stretch);
                if (memory.IsAllocated) memory.Free();
                if (!complete)
                {
                    try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            Prune(path);
            return path;
        }

        // Rendered samples pass through a push stream so BASSenc converts them (16-bit, dithered) and encodes.
        private static void Encode(int push, float[] samples, float[] scratch, int channels, int frames)
        {
            if (frames < 0) throw new InvalidOperationException("AffStretch render failed");
            int bytes = frames * channels * sizeof(float);
            if (bytes == 0) return;
            BassNative.Check(BassNative.BASS_StreamPutData(push, samples, bytes) >= 0, "queue rendered audio");
            while (bytes > 0)
            {
                int read = BassNative.BASS_ChannelGetData(push, scratch, bytes);
                BassNative.Check(read > 0, "encode rendered audio");
                bytes -= read;
            }
        }

        // Keeps the cache under 1 GB by deleting the least recently used renders.
        private static void Prune(string keep)
        {
            var files = new DirectoryInfo(Path.GetDirectoryName(keep)).GetFiles("*.flac");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
                if (total <= CacheLimit || string.Equals(file.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    // Plays a slow speed whose cached render is not ready yet. AffStretch renders from the
    // playhead into a push stream, staying a few seconds ahead of playback on a worker thread.
    // It starts a short pre-roll before the requested time and drops that output, so the first
    // heard sample is already settled, and output frame n is song time Start + n * speed: the
    // same timing as the cached file, with no delay compensation.
    internal sealed class LiveSlowStream : IDisposable
    {
        private const double PreRollSeconds = .3, PrimeSeconds = .25, AheadSeconds = 6;
        private const int Chunk = 4096;
        private const uint StreamEnd = 0x80000000, ErrorEnded = 45; // BASS_STREAMPROC_END, BASS_ERROR_ENDED
        private static readonly IntPtr StreamProcPush = new IntPtr(-1);
        private static volatile bool unavailable;
        private readonly float speed;
        private int channels, frequency, capacity, aheadBytes, source;
        private float[] input, output;
        private GCHandle memory;
        private IntPtr stretch;
        private long skip;
        private bool ended;
        private Thread worker;
        private volatile bool cancel, failed;
        internal BassVoice Voice { get; private set; }
        internal double Start { get; private set; }
        internal bool Failed => failed;
        // Song time in seconds at the current playback position.
        internal double Timing => Start + Voice.Position * speed;

        private LiveSlowStream(float speed) { this.speed = speed; }

        // Opens and primes a stream at `start` (song seconds); null if AffStretch is unavailable.
        // Priming renders the pre-roll and the first quarter second here, so it can play at once.
        internal static LiveSlowStream TryCreate(BassClip clip, float speed, double start)
        {
            if (unavailable || !clip || !(speed > 0 && speed < 1)) return null;
            var stream = new LiveSlowStream(speed);
            try
            {
                stream.Open(clip, Math.Max(0, start));
                stream.Prime();
                return stream;
            }
            catch (Exception e)
            {
                stream.Dispose();
                if (e is DllNotFoundException || e is EntryPointNotFoundException) unavailable = true;
                else if (!(e is InvalidOperationException)) throw;
                Debug.LogWarning($"Live slow playback unavailable, keeping pitch-lowered playback: {e.Message}");
                return null;
            }
        }

        private void Open(BassClip clip, double start)
        {
            BassAudio.EnsureInitialized();
            uint flags = BassNative.Decode | BassNative.Float;
            if (clip.FilePath != null)
            {
                source = clip.IsOpusFile
                    ? BassNative.BASS_OPUS_StreamCreateFile(false, clip.FilePath, 0, 0, flags | BassNative.Unicode)
                    : BassNative.BASS_StreamCreateFile(false, clip.FilePath, 0, 0, flags | BassNative.Unicode);
            }
            else
            {
                var bytes = clip.Bytes;
                memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                source = BassNative.BASS_StreamCreateFile(true, memory.AddrOfPinnedObject(), 0, bytes.LongLength, flags);
            }
            BassNative.Check(source != 0, "open song for live slow playback");
            BassNative.Check(BassNative.BASS_ChannelGetInfo(source, out BassNative.ChannelInfo info), "live slow playback info");
            channels = info.Channels;
            frequency = info.Frequency;
            int frameBytes = channels * sizeof(float);
            long frames = Math.Max(0, BassNative.BASS_ChannelGetLength(source, 0) / frameBytes);
            long first = Math.Min((long)Math.Round(start * info.Frequency), frames);
            long preRoll = Math.Min(first, (long)Math.Round(PreRollSeconds * info.Frequency));
            long from = first - preRoll;
            if (from > 0) BassNative.Check(BassNative.BASS_ChannelSetPosition(source, from * frameBytes, 0), "seek live slow playback");
            skip = (long)Math.Round(preRoll / (double)speed);
            Start = (from + skip * (double)speed) / info.Frequency;
            stretch = AffStretchNative.AffStretch_Create(channels, info.Frequency, speed);
            if (stretch == IntPtr.Zero) throw new InvalidOperationException("AffStretch rejected the audio format");
            int push = BassNative.BASS_StreamCreate((uint)info.Frequency, (uint)channels, BassNative.Float, StreamProcPush, IntPtr.Zero);
            BassNative.Check(push != 0, "live slow playback stream");
            Voice = BassVoice.FromHandle(push);
            input = new float[Chunk * channels];
            capacity = (int)(Chunk / speed) + 4096;
            output = new float[capacity * channels];
            aheadBytes = (int)(AheadSeconds * info.Frequency) * frameBytes;
        }

        private void Prime()
        {
            long needed = (long)(PrimeSeconds * frequency);
            while (needed > 0 && !ended) needed -= Step();
            worker = new Thread(Run) { IsBackground = true, Name = "AffStretch live", Priority = System.Threading.ThreadPriority.AboveNormal };
            worker.Start();
        }

        private void Run()
        {
            try
            {
                while (!cancel && !ended)
                {
                    int queued = BassNative.BASS_StreamPutData(Voice.Handle, null, 0);
                    BassNative.Check(queued >= 0, "live slow playback queue");
                    if (queued >= aheadBytes) Thread.Sleep(15);
                    else Step();
                }
            }
            catch (Exception e) when (e is InvalidOperationException)
            {
                failed = true;
                Debug.LogWarning($"Live slow playback stopped, keeping pitch-lowered playback: {e.Message}");
            }
        }

        // Renders one input chunk (or part of the tail) into the push stream; returns frames queued.
        private int Step()
        {
            if (ended) return 0;
            int read = BassNative.BASS_ChannelGetData(source, input, input.Length * sizeof(float));
            int frames;
            if (read > 0) frames = AffStretchNative.AffStretch_Process(stretch, input, read / sizeof(float) / channels, output, capacity);
            else
            {
                BassNative.Check(read == 0 || BassNative.BASS_ErrorGetCode() == ErrorEnded, "decode song for live slow playback");
                frames = AffStretchNative.AffStretch_Finish(stretch, output, capacity);
                if (frames == 0)
                {
                    ended = true;
                    BassNative.Check(BassNative.BASS_StreamPutData(Voice.Handle, null, unchecked((int)StreamEnd)) >= 0, "end live slow playback");
                    return 0;
                }
            }
            if (frames < 0) throw new InvalidOperationException("AffStretch render failed");
            int dropped = (int)Math.Min(skip, frames);
            skip -= dropped;
            int count = frames - dropped;
            if (count == 0) return 0;
            if (dropped > 0) Buffer.BlockCopy(output, dropped * channels * sizeof(float), output, 0, count * channels * sizeof(float));
            BassNative.Check(BassNative.BASS_StreamPutData(Voice.Handle, output, count * channels * sizeof(float)) >= 0, "queue live slow playback");
            return count;
        }

        public void Dispose()
        {
            cancel = true;
            // Thread.Start can fail before the worker is running. Joining an unstarted thread
            // would throw here and skip disposal of the native streams, stretch handle and pin.
            if (worker != null && worker.IsAlive && worker != Thread.CurrentThread) worker.Join();
            worker = null;
            Voice?.Dispose();
            Voice = null;
            if (source != 0) { BassNative.BASS_StreamFree(source); source = 0; }
            if (stretch != IntPtr.Zero) { AffStretchNative.AffStretch_Destroy(stretch); stretch = IntPtr.Zero; }
            if (memory.IsAllocated) memory.Free();
        }
    }

    internal static class AffStretchNative
    {
        [DllImport("AffStretch")] internal static extern IntPtr AffStretch_Create(int channels, int sampleRate, float speed);
        [DllImport("AffStretch")] internal static extern int AffStretch_Process(IntPtr handle, float[] input, int frames, [Out] float[] output, int capacity);
        [DllImport("AffStretch")] internal static extern int AffStretch_Finish(IntPtr handle, [Out] float[] output, int capacity);
        [DllImport("AffStretch")] internal static extern void AffStretch_Destroy(IntPtr handle);
    }
}

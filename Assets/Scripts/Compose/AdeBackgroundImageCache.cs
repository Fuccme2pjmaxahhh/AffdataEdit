using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Arcade.Compose
{
    // The browser owns paths, not full-resolution images. Small disk previews can overlap one
    // source-image decode; all Unity object work stays on the main thread.
    internal sealed class AdeBackgroundImageCache : IDisposable
    {
        private sealed class Entry { public Sprite Sprite; public int Used; }
        private sealed class Failure { public int Attempts; public float RetryAt; }
        private sealed class Nearby { public int Frame; public float Distance; }
        private sealed class Location { public string Disk; public bool SourceOnly; }
        private sealed class PreviewJob
        {
            public string Path;
            public int Version;
            public Coroutine Worker;
            public UnityWebRequest Download;
            public bool Original, Resizing;
        }
        private const int PreviewLimit = 48, FullLimit = 2, PreviewSize = 256;
        private const int PreviewJobs = 2, OriginalJobs = 1, DiskWrites = 2;
        private readonly MonoBehaviour owner;
        private readonly Dictionary<string, Entry> previews = new Dictionary<string, Entry>();
        private readonly Dictionary<string, Entry> full = new Dictionary<string, Entry>();
        private readonly Dictionary<string, int> visible = new Dictionary<string, int>();
        private readonly Dictionary<string, Nearby> nearby = new Dictionary<string, Nearby>();
        private readonly Dictionary<string, Location> locations = new Dictionary<string, Location>();
        private readonly Queue<string> queue = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>();
        private readonly List<PreviewJob> jobs = new List<PreviewJob>(PreviewJobs);
        private readonly HashSet<string> deferred = new HashSet<string>();
        private readonly Dictionary<string, Failure> failed = new Dictionary<string, Failure>();
        private readonly string diskFolder;
        private Task diskWork = Task.CompletedTask;
        private Coroutine worker, selection;
        private UnityWebRequest fullDownload;
        private int selectionVersion, generation;
        private int originalJobs, pendingDiskWrites;
        private Sprite active, retired;
        private bool disposed;

        internal int PreviewCount => previews.Count;
        internal int FullCount => full.Count;
        internal int ActivePreviewJobs => jobs.Count;
        internal int ActiveOriginalJobs => originalJobs;
        internal int PeakPreviewJobs { get; private set; }
        internal int PeakOriginalJobs { get; private set; }
        internal int OriginalLoadCount { get; private set; }
        internal int DiskLoadCount { get; private set; }

        internal AdeBackgroundImageCache(MonoBehaviour owner)
        {
            this.owner = owner;
            diskFolder = Path.Combine(Application.temporaryCachePath, "BackgroundPreviews-v1");
        }

        internal Sprite Preview(string path, bool prefetch = false, float distance = 0)
        {
            if (disposed || string.IsNullOrEmpty(path)) return null;
            if (prefetch)
            {
                if (!nearby.TryGetValue(path, out var demand)) nearby[path] = demand = new Nearby();
                demand.Frame = Time.frameCount; demand.Distance = distance;
            }
            else
            {
                visible[path] = Time.frameCount;
                nearby.Remove(path);
            }
            if (previews.TryGetValue(path, out var entry))
            {
                if (entry.Sprite && entry.Sprite.texture)
                {
                    if (!prefetch) entry.Used = Time.frameCount;
                    return entry.Sprite;
                }
                // A resource purge or device recovery must not leave a permanent empty cache hit.
                DestroySprite(entry.Sprite);
                previews.Remove(path);
            }
            if (prefetch && !CanPrefetch(path)) return null;
            if (!CanRequestPreview(path, Time.realtimeSinceStartup)) return null;
            if (queued.Add(path)) queue.Enqueue(path);
            if (worker == null) worker = owner.StartCoroutine(LoadPreviews());
            return null;
        }

        internal void Select(string path, Action<Sprite> apply)
        {
            CancelSelection();
            if (disposed) return;
            if (full.TryGetValue(path, out var entry))
            {
                if (entry.Sprite && entry.Sprite.texture)
                {
                    entry.Used = Time.frameCount;
                    Apply(entry.Sprite, apply);
                    return;
                }
                DestroySprite(entry.Sprite);
                full.Remove(path);
            }
            selection = owner.StartCoroutine(LoadSelected(path, selectionVersion, apply));
        }

        internal void CancelSelection()
        {
            selectionVersion++;
            if (fullDownload != null) { fullDownload.Abort(); fullDownload.Dispose(); fullDownload = null; }
            if (selection != null) { owner.StopCoroutine(selection); selection = null; }
        }

        internal void UseSharedBackground()
        {
            CancelSelection();
            active = null;
            if (retired) { DestroySprite(retired); retired = null; }
        }

        private IEnumerator LoadSelected(string path, int version, Action<Sprite> apply)
        {
            var request = UnityWebRequestTexture.GetTexture(new Uri(Path.GetFullPath(path)).AbsoluteUri, true);
            fullDownload = request;
            yield return request.SendWebRequest();
            fullDownload = null;
            if (!IsCurrentSelection(version)) { request.Dispose(); yield break; }
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Cannot load background: " + path + " (" + request.error + ")");
                request.Dispose(); selection = null; yield break;
            }
            var texture = DownloadHandlerTexture.GetContent(request);
            request.Dispose();
            texture.name = path; texture.wrapMode = TextureWrapMode.Clamp;
            var sprite = MakeSprite(texture, path);
            full[path] = new Entry { Sprite = sprite, Used = Time.frameCount };
            Apply(sprite, apply);
            Trim(full, FullLimit, active);
            selection = null;
        }

        private bool IsCurrentSelection(int version) => !disposed && version == selectionVersion;

        private void Apply(Sprite sprite, Action<Sprite> apply)
        {
            active = sprite;
            apply(sprite);
            if (retired) { DestroySprite(retired); retired = null; }
        }

        private IEnumerator LoadPreviews()
        {
            // Let layout and clipping settle before checking which rows are actually visible.
            yield return null;
            while (!disposed && (queue.Count > 0 || jobs.Count > 0))
            {
                deferred.Clear();
                // Bound metadata work as well as image work when a cold source is already decoding.
                int attempts = 4;
                while (jobs.Count < PreviewJobs && attempts-- > 0)
                {
                    string path = NextPreview(deferred, originalJobs < OriginalJobs);
                    if (path == null) break;
                    if (!locations.TryGetValue(path, out var location))
                        locations[path] = location = new Location { Disk = PreviewPath(path) };
                    bool cached = !location.SourceOnly && location.Disk != null && File.Exists(location.Disk);
                    if (!cached && originalJobs >= OriginalJobs)
                    {
                        queue.Enqueue(path); deferred.Add(path); continue;
                    }
                    var job = new PreviewJob { Path = path, Version = generation };
                    jobs.Add(job);
                    PeakPreviewJobs = Mathf.Max(PeakPreviewJobs, jobs.Count);
                    if (!cached) AcquireOriginal(job);
                    job.Worker = owner.StartCoroutine(LoadPreview(job, location, cached));
                }
                // Ready disk requests may complete together, but at most two image uploads and
                // one source resize can be in progress. There is no artificial delay per thumbnail.
                yield return null;
            }
            worker = null;
        }

        private IEnumerator LoadPreview(PreviewJob job, Location location, bool cached)
        {
            bool retrySource = false;
            try
            {
                if (cached) DiskLoadCount++; else OriginalLoadCount++;
                job.Download = UnityWebRequestTexture.GetTexture(new Uri(Path.GetFullPath(cached ? location.Disk : job.Path)).AbsoluteUri, true);
                yield return job.Download.SendWebRequest();
                if (disposed || job.Version != generation) yield break;
                if (job.Download.result != UnityWebRequest.Result.Success)
                {
                    // A missing/corrupt disk preview must reacquire the single original-image
                    // slot through the scheduler, rather than decode two full images at once.
                    if (cached) { location.SourceOnly = true; retrySource = true; }
                    else RecordPreviewFailure(job.Path);
                    yield break;
                }
                var source = DownloadHandlerTexture.GetContent(job.Download);
                job.Download.Dispose(); job.Download = null;
                if (cached) StorePreview(job.Path, source);
                else
                {
                    bool completed = false;
                    job.Resizing = true;
                    ResizePreview(source, job.Path, location.Disk, () =>
                    {
                        job.Resizing = false;
                        ReleaseOriginal(job);
                        jobs.Remove(job);
                        completed = true;
                    });
                    while (!completed && !disposed && job.Version == generation) yield return null;
                }
            }
            finally
            {
                job.Download?.Dispose(); job.Download = null;
                if (!job.Resizing) { ReleaseOriginal(job); jobs.Remove(job); }
                if (job.Version == generation)
                {
                    if (retrySource && HasDemand(job.Path)) queue.Enqueue(job.Path);
                    else { queued.Remove(job.Path); locations.Remove(job.Path); }
                }
            }
        }

        private void AcquireOriginal(PreviewJob job)
        {
            job.Original = true;
            originalJobs++;
            PeakOriginalJobs = Mathf.Max(PeakOriginalJobs, originalJobs);
        }

        private void ReleaseOriginal(PreviewJob job)
        {
            if (!job.Original) return;
            job.Original = false;
            originalJobs--;
        }

        private bool HasDemand(string path)
            => IsVisibleRequest(path) || (nearby.TryGetValue(path, out var demand) && Time.frameCount - demand.Frame <= 2 && CanPrefetch(path));

        private bool IsVisibleRequest(string path)
            => visible.TryGetValue(path, out int frame) && Time.frameCount - frame <= 2;

        private bool CanPrefetch(string path)
        {
            if (!nearby.TryGetValue(path, out var demand)) return false;
            int ahead = 0;
            foreach (var pair in visible)
                if (Time.frameCount - pair.Value <= 2 && pair.Key != path) ahead++;
            foreach (var pair in nearby)
                if (pair.Key != path && Time.frameCount - pair.Value.Frame <= 2 && !IsVisibleRequest(pair.Key)
                    && (pair.Value.Distance < demand.Distance || (pair.Value.Distance == demand.Distance && string.CompareOrdinal(pair.Key, path) < 0))) ahead++;
            return ahead < PreviewLimit;
        }

        private string NextVisiblePreview() => NextPreview(null, true);

        private string NextPreview(HashSet<string> skip, bool allowOriginal)
        {
            string selected = null;
            int newest = int.MinValue, count = queue.Count;
            bool selectedVisible = false;
            float closest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                string path = queue.Dequeue();
                if (!HasDemand(path))
                {
                    queued.Remove(path); locations.Remove(path); visible.Remove(path); nearby.Remove(path); continue;
                }
                queue.Enqueue(path);
                if (skip != null && skip.Contains(path)) continue;
                if (!allowOriginal && locations.TryGetValue(path, out var location)
                    && (location.SourceOnly || location.Disk == null || !File.Exists(location.Disk))) continue;
                bool onScreen = IsVisibleRequest(path);
                int frame = onScreen ? visible[path] : nearby[path].Frame;
                float distance = onScreen ? 0 : nearby[path].Distance;
                if (selected == null || (onScreen && !selectedVisible) || (onScreen == selectedVisible
                    && (distance < closest || (distance == closest && frame > newest))))
                {
                    newest = frame; closest = distance; selectedVisible = onScreen; selected = path;
                }
            }
            if (selected == null) return null;
            // Keep other visible requests in stable order, but prioritize freshly revealed rows.
            count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                string path = queue.Dequeue();
                if (path != selected) queue.Enqueue(path);
            }
            return selected;
        }

        private bool CanRequestPreview(string path, float now)
            => !failed.TryGetValue(path, out var failure) || now >= failure.RetryAt;

        private void RecordPreviewFailure(string path)
        {
            if (!failed.TryGetValue(path, out var failure)) failed[path] = failure = new Failure();
            failure.Attempts++;
            failure.RetryAt = Time.realtimeSinceStartup + Mathf.Min(30, Mathf.Pow(2, Mathf.Min(5, failure.Attempts)));
        }

        private void ResizePreview(Texture2D source, string path, string disk, Action completed, Action<RenderTexture> draw = null)
        {
            int version = generation;
            RenderTexture target = null;
            bool transferred = false, cleaned = false;
            Action cleanup = () =>
            {
                if (cleaned) return;
                cleaned = true;
                try
                {
                    if (target) RenderTexture.ReleaseTemporary(target);
                    if (source)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(source);
                        else UnityEngine.Object.DestroyImmediate(source);
                    }
                }
                finally { completed(); }
            };
            try
            {
                float scale = Mathf.Min(1, PreviewSize / (float)Mathf.Max(source.width, source.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                if (draw == null) Graphics.Blit(source, target); else draw(target);
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    // Ownership transfers only after request registration succeeds. Until then,
                    // setup failure must release the source and its original-image slot too.
                    AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, readback =>
                    {
                        Texture2D thumbnail = null;
                        try
                        {
                            if (!disposed && version == generation)
                            {
                                if (readback.hasError)
                                    ReadPreviewPixels(target, width, height, path, disk);
                                else
                                {
                                    thumbnail = new Texture2D(width, height, TextureFormat.RGBA32, false);
                                    thumbnail.LoadRawTextureData(readback.GetData<byte>());
                                    SavePreview(thumbnail, disk);
                                    thumbnail.Apply(false, true);
                                    StorePreview(path, thumbnail);
                                    thumbnail = null;
                                }
                            }
                        }
                        catch (UnityException error) { ReportPreviewFailure(path, error); }
                        catch (InvalidOperationException error) { ReportPreviewFailure(path, error); }
                        finally
                        {
                            if (thumbnail) UnityEngine.Object.Destroy(thumbnail);
                            cleanup();
                        }
                    });
                    transferred = true;
                }
                else ReadPreviewPixels(target, width, height, path, disk);
            }
            catch (UnityException error) { ReportPreviewFailure(path, error); }
            catch (InvalidOperationException error) { ReportPreviewFailure(path, error); }
            finally { if (!transferred) cleanup(); }
        }

        private void ReadPreviewPixels(RenderTexture target, int width, int height, string path, string disk)
        {
            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                SavePreview(texture, disk);
                texture.Apply(false, true);
                StorePreview(path, texture);
                texture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture) UnityEngine.Object.Destroy(texture);
            }
        }

        private void ReportPreviewFailure(string path, Exception error)
        {
            if (disposed) return;
            RecordPreviewFailure(path);
            if (failed[path].Attempts == 1)
                Debug.LogWarning("Background preview could not be read; a later request will retry: " + path + " (" + error.Message + ")");
        }

        private string PreviewPath(string path)
        {
            try
            {
                var info = new FileInfo(path);
                string key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                using (var hash = SHA256.Create())
                    return Path.Combine(diskFolder, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "") + ".png");
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private void SavePreview(Texture2D texture, string path)
        {
            if (path == null || Volatile.Read(ref pendingDiskWrites) >= DiskWrites) return;
            byte[] png = texture.EncodeToPNG();
            Interlocked.Increment(ref pendingDiskWrites);
            // Slow storage must not build an unbounded queue of encoded PNG byte arrays.
            diskWork = diskWork.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(diskFolder);
                    File.WriteAllBytes(path, png);
                    var files = new DirectoryInfo(diskFolder).GetFiles("*.png");
                    Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    long bytes = 0;
                    for (int i = 0; i < files.Length; i++)
                    {
                        bytes += files[i].Length;
                        if (i >= 256 || bytes > 64L * 1024 * 1024) files[i].Delete();
                    }
                }
                catch (IOException) { /* A read-only/full cache never prevents displaying a background. */ }
                catch (UnauthorizedAccessException) { }
                finally { Interlocked.Decrement(ref pendingDiskWrites); }
            }, TaskScheduler.Default);
        }

        private void StorePreview(string path, Texture2D texture)
        {
            if (disposed) { UnityEngine.Object.Destroy(texture); return; }
            texture.name = "Background preview: " + path;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (previews.TryGetValue(path, out var previous)) DestroySprite(previous.Sprite);
            previews[path] = new Entry { Sprite = MakeSprite(texture, path), Used = Time.frameCount };
            failed.Remove(path);
            TrimPreviews();
        }

        private void TrimPreviews()
        {
            while (previews.Count > PreviewLimit)
            {
                string oldest = null;
                float farthest = -1;
                int frame = int.MaxValue;
                foreach (var pair in previews)
                {
                    if (IsVisibleRequest(pair.Key)) continue;
                    float distance = nearby.TryGetValue(pair.Key, out var demand) && Time.frameCount - demand.Frame <= 2
                        ? demand.Distance : float.MaxValue;
                    if (distance > farthest || (distance == farthest && pair.Value.Used < frame))
                    {
                        oldest = pair.Key; farthest = distance; frame = pair.Value.Used;
                    }
                }
                // An unusually large viewport can itself contain more than the fixed cache limit.
                if (oldest == null) { Trim(previews, PreviewLimit, null); break; }
                DestroySprite(previews[oldest].Sprite);
                previews.Remove(oldest);
            }
        }

        private static Sprite MakeSprite(Texture2D texture, string name)
        {
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        private static void Trim(Dictionary<string, Entry> entries, int limit, Sprite keep)
        {
            while (entries.Count > limit)
            {
                string oldest = null;
                int frame = int.MaxValue;
                foreach (var pair in entries)
                    if (pair.Value.Sprite != keep && pair.Value.Used < frame) { frame = pair.Value.Used; oldest = pair.Key; }
                if (oldest == null) break;
                DestroySprite(entries[oldest].Sprite);
                entries.Remove(oldest);
            }
        }

        private static void DestroySprite(Sprite sprite)
        {
            if (!sprite) return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        // Keep the displayed image alive until its replacement has finished decoding.
        internal void Invalidate()
        {
            generation++;
            CancelSelection();
            if (worker != null) { owner.StopCoroutine(worker); worker = null; }
            foreach (var job in jobs.ToArray())
            {
                if (job.Download != null) { job.Download.Abort(); job.Download.Dispose(); job.Download = null; }
                if (job.Worker != null) owner.StopCoroutine(job.Worker);
                // GPU readback callbacks retain their source lease until their own cleanup runs.
                if (!job.Resizing) { ReleaseOriginal(job); jobs.Remove(job); }
            }
            foreach (var entry in previews.Values) DestroySprite(entry.Sprite);
            previews.Clear(); queue.Clear(); queued.Clear(); visible.Clear(); nearby.Clear(); locations.Clear(); failed.Clear();
            foreach (var entry in full.Values)
            {
                if (entry.Sprite == active) { if (retired && retired != active) DestroySprite(retired); retired = active; }
                else DestroySprite(entry.Sprite);
            }
            full.Clear(); active = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Invalidate();
            if (retired) { DestroySprite(retired); retired = null; }
        }
    }
}

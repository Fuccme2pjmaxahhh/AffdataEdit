using System.Collections.Generic;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Gameplay
{
    // Alpha's Pure sprite, screen offsets and fade/move durations, pooled under the editor canvas.
    [DefaultExecutionOrder(31000)]
    internal sealed class ArcHitTypeEffects : MonoBehaviour
    {
        // All current hit types are Pure, so the half-unit position identifies a frame's quota.
        // Fixed storage also bounds work when a chart requests many distinct hit positions.
        private sealed class PureHitLimiter
        {
            private const int Capacity = 1024, MaxHitsPerPosition = 10;
            private readonly long[] keys = new long[Capacity];
            private readonly int[] frames = new int[Capacity];
            private readonly byte[] counts = new byte[Capacity];
            private int lastFrame = -1;
            private bool full;

            public PureHitLimiter()
            {
                for (int i = 0; i < Capacity; i++) frames[i] = -1;
            }

            public bool Allow(int frame, float x, float y)
            {
                if (frame != lastFrame)
                {
                    lastFrame = frame;
                    full = false;
                }
                if (full) return false;
                int cellX = Mathf.FloorToInt(x * 2f + .5f);
                int cellY = Mathf.FloorToInt(y * 2f + .5f);
                long key = ((long)cellX << 32) | (uint)cellY;
                int hash = unchecked((cellX * 73856093) ^ (cellY * 19349663));
                int slot = (hash & int.MaxValue) % Capacity;
                for (int probe = 0; probe < Capacity; probe++)
                {
                    if (frames[slot] != frame)
                    {
                        frames[slot] = frame;
                        keys[slot] = key;
                        counts[slot] = 1;
                        return true;
                    }
                    if (keys[slot] == key)
                    {
                        if (counts[slot] >= MaxHitsPerPosition) return false;
                        counts[slot]++;
                        return true;
                    }
                    slot = (slot + 1) % Capacity;
                }
                full = true;
                return false;
            }
        }

        private struct Hit
        {
            public Image Image;
            public Vector2 Position;
            public float Age, FadeDuration, Scale;
        }

        private readonly List<Hit> active = new List<Hit>();
        private readonly Stack<Image> idle = new Stack<Image>();
        private readonly PureHitLimiter hitLimiter = new PureHitLimiter();
        private const int MaxIdleImages = 64;
        private RectTransform root;
        private Sprite pure;
        private ArcChart previousChart;
        private uint previousSeekVersion;
        private int previousTiming;

        private static bool CanShow => ArcadeComposeManager.Instance
            && ArcadeComposeManager.Instance.ArcadePreference.ShowJudgementDetails
            && ArcGameplayManager.Instance.Auto;

        private static bool CanSpawn => CanShow && ArcGameplayManager.Instance.IsPlaying;

        private bool Continuous => previousChart == ArcGameplayManager.Instance.Chart
            && previousSeekVersion == ArcGameplayManager.Instance.SeekVersion
            && ArcGameplayManager.Instance.ChartTiming >= previousTiming;

        internal void ShowLongNote(ArcLongNote note, Vector3 position)
        {
            if (!CanSpawn || !Continuous) return;
            int index = note.JudgeTimings.BinarySearch(previousTiming);
            if (index < 0) index = ~index;
            else while (index < note.JudgeTimings.Count && note.JudgeTimings[index] <= previousTiming) index++;
            if (index < note.JudgeTimings.Count && note.JudgeTimings[index] <= ArcGameplayManager.Instance.ChartTiming)
                ShowPure(position, true);
        }

        internal void ShowSlide(ArcSlide slide)
        {
            if (!CanSpawn || !Continuous) return;
            var game = ArcGameplayManager.Instance;
            var root = slide.GroupRoot ?? slide;
            double bpm = ArcTimingManager.Instance.CalculateBpmByTiming(root.Timing, root.TimingGroup);
            if (slide.CountJudgements(game.ChartTiming, bpm, game.TimingPointDensityFactor)
                <= slide.CountJudgements(previousTiming, bpm, game.TimingPointDensityFactor)) return;
            float progress = Mathf.Clamp01((game.ChartTiming - (float)slide.Timing) / (slide.EndTiming - (float)slide.Timing));
            Vector2 range = slide.Range(progress);
            ShowPure(new Vector3(slide.WorldX((range.x + range.y) * .5f), 0, 0), true);
        }

        internal void ShowPure(Vector3 position, bool longNote = false)
        {
            if (!CanSpawn || !hitLimiter.Allow(Time.frameCount, position.x, position.y)) return;
            var camera = ArcCameraManager.Instance.GameplayCamera;
            Vector3 viewport = camera.WorldToViewportPoint(position);
            if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return;
            if (!root)
            {
                pure = Resources.Load<Sprite>("AffdataEdit/HitPure");
                root = new GameObject("Hit types", typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(ArcadeComposeManager.Instance.EditorCanvas, false);
                root.SetAsFirstSibling();
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = root.offsetMax = Vector2.zero;
            }
            var canvas = root.GetComponentInParent<Canvas>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, camera.WorldToScreenPoint(position),
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out Vector2 point);
            // AA's 1.5x at 1920x1080 equals 1x at our 1280x720 design resolution.
            // Convert the gameplay viewport's pixels to canvas units, excluding editor bars.
            float scale = camera.pixelHeight * ArcadeComposeManager.Instance.GameplayViewRect.height / (canvas.scaleFactor * ArcCameraManager.Instance.DesignResolution.y);
            point.y += (longNote ? 110 : 80) * scale;
            Image image = idle.Count > 0 ? idle.Pop() : CreateImage();
            image.rectTransform.sizeDelta = pure.rect.size * scale;
            image.rectTransform.anchoredPosition = point;
            image.color = Color.white;
            image.gameObject.SetActive(true);
            active.Add(new Hit { Image = image, Position = point, FadeDuration = longNote ? .3f : .5f, Scale = scale });
        }

        private Image CreateImage()
        {
            var image = new GameObject("Pure", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.sprite = pure;
            image.raycastTarget = false;
            return image;
        }

        private void LateUpdate()
        {
            var game = ArcGameplayManager.Instance;
            if (!CanShow || !Continuous) Clear();
            else for (int i = active.Count - 1; i >= 0; i--)
            {
                Hit hit = active[i];
                hit.Age += Time.deltaTime;
                if (hit.Age >= (hit.FadeDuration < .4f ? .4f : hit.FadeDuration))
                {
                    ReturnImage(hit.Image);
                    active.RemoveAt(i);
                    continue;
                }
                hit.Image.color = new Color(1, 1, 1, Mathf.Max(0, 1 - hit.Age / hit.FadeDuration));
                float movement = 1 - Mathf.Pow(1 - Mathf.Clamp01(hit.Age / .4f), 3);
                hit.Image.rectTransform.anchoredPosition = hit.Position + Vector2.up * (30 * hit.Scale * movement);
                active[i] = hit;
            }
            previousChart = game.Chart;
            previousSeekVersion = game.SeekVersion;
            previousTiming = game.ChartTiming;
        }

        private void Clear()
        {
            foreach (var hit in active)
            {
                ReturnImage(hit.Image);
            }
            active.Clear();
        }
        private void ReturnImage(Image image)
        {
            image.gameObject.SetActive(false);
            if (idle.Count < MaxIdleImages || ArcGameplayManager.Instance && ArcGameplayManager.Instance.IsPlaying)
                idle.Push(image);
            else Destroy(image.gameObject);
        }
        internal void ReleaseChartMemory()
        {
            Clear(); active.TrimExcess(); previousChart = null;
            while (idle.Count > 8) Destroy(idle.Pop().gameObject);
        }

        private void OnDisable() => Clear();
        private void OnDestroy() { if (root) Destroy(root.gameObject); }
    }
}

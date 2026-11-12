using System.Collections.Generic;
using System.Threading.Tasks;
using Arcade.Audio;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Gameplay
{
    public class ArcAudioManager : MonoBehaviour
    {
        public static ArcAudioManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Instance = null;
        public Text PlayBackSpeedButtonText;
        public BassClip Clip { get; private set; }
        private BassVoice voice, renderedVoice;
        private float renderedSpeed;
        private Task<string> pendingRender;
        private float pendingSpeed;
        // Until the cached render of the current slow speed exists, AffStretch renders live from
        // the playhead (same sound and timing). A seek marks it stale; it restarts on play, or
        // shortly after the last seek while playing. Pitch-lowered `voice` remains the fallback.
        private const double LiveSeekSettle = .05;
        private LiveSlowStream live;
        private bool liveSeekPending, liveResume, liveDisabled;
        private float liveSeekTiming;
        private double liveSeekClock;
        // Every slow speed is pre-rendered on chart load (one at a time); a thin bar under the
        // speed button shows the overall progress while any is missing.
        private static readonly float[] SlowSpeeds = { .75f, .5f, .25f };
        private readonly List<KeyValuePair<string, Task<string>>> preRenders = new List<KeyValuePair<string, Task<string>>>();
        private GameObject progressBar;
        private RectTransform progressFill;
        private Image progressTrack, progressFillImage;
        private double scheduledStart = -1;
        private float playBackSpeed = 1;
        // Changes when playback moves between the original and a rendered stream (sync recalibrates).
        public uint VoiceVersion { get; private set; }
        private BassVoice ActiveVoice => renderedVoice ?? live?.Voice ?? voice;
        private bool WantsLive => voice != null && renderedVoice == null && playBackSpeed < 1 && !liveDisabled;
        private bool LivePlaying
        {
            get
            {
                int state = live == null ? 0 : BassNative.BASS_ChannelIsActive(live.Voice.Handle);
                return state == 1 || state == 2; // BASS_ACTIVE_PLAYING / STALLED
            }
        }
        private void Awake() { Instance = this; }
        // Song time in seconds; a rendered file runs 1/speed times longer than the song.
        public float Timing
        {
            get => renderedVoice != null ? (float)(renderedVoice.Position * renderedSpeed)
                : live != null ? (liveSeekPending ? liveSeekTiming : (float)live.Timing)
                : voice == null ? 0 : (float)voice.Position;
            set
            {
                if (renderedVoice != null) renderedVoice.Position = value / renderedSpeed;
                else if (voice != null)
                {
                    voice.Position = value; // kept in place as the fallback
                    if (live != null) { liveSeekTiming = Mathf.Max(0, value); liveSeekPending = true; liveSeekClock = BassAudio.Clock; }
                }
            }
        }
        public float PlayBackSpeed
        {
            get => playBackSpeed;
            private set
            {
                float previousSpeed = playBackSpeed;
                playBackSpeed = value;
                ApplySpeed(previousSpeed);
            }
        }
        // Below 100%: play the cached pitch-preserving render, or start one in the background and
        // render live from the playhead until it is ready.
        private void ApplySpeed(float previousSpeed)
        {
            if (voice == null || (renderedVoice != null && renderedSpeed == playBackSpeed)) return;
            string rendered = playBackSpeed < 1 ? SlowAudioCache.TryGet(Clip, playBackSpeed) : null;
            SwitchVoice(rendered, previousSpeed);
            pendingRender = rendered == null && playBackSpeed < 1 ? SlowAudioCache.Request(Clip, playBackSpeed) : null;
            pendingSpeed = playBackSpeed;
        }
        // Moves playback between the original stream and a rendered file at the same song position.
        private void SwitchVoice(string renderedPath, float previousSpeed)
        {
            if (renderedVoice == null && renderedPath == null && previousSpeed == playBackSpeed && live == null)
            {
                voice.SetSpeed(playBackSpeed);
                // Chart load at a slow speed with no cached render: prepare live playback at the playhead.
                if (WantsLive) { live = LiveSlowStream.TryCreate(Clip, playBackSpeed, Timing); ApplyVolume(); }
                return;
            }
            // Open the new file before pausing the old stream. Its format/seek-table setup can
            // take time, and that time still belongs to playback at the previous speed.
            BassVoice replacement = null;
            if (renderedPath != null)
                try { replacement = BassVoice.OpenRendered(renderedPath); }
                catch (System.Exception e) when (e is System.InvalidOperationException || e is System.DllNotFoundException || e is System.EntryPointNotFoundException) { Debug.LogWarning($"Slow playback file unusable, keeping pitch-lowered playback: {e.Message}"); }
            var game = ArcGameplayManager.Instance;
            float timing = game && game.IsPlaying
                ? game.CapturePlaybackTransition(previousSpeed, BassAudio.Clock) : Timing;
            int state = BassNative.BASS_ChannelIsActive(ActiveVoice.Handle);
            bool playing = state == 1 || state == 2 || liveResume; // BASS_ACTIVE_PLAYING / STALLED, or paused for a seek
            liveResume = false;
            if (playing) BassNative.BASS_ChannelPause(ActiveVoice.Handle);
            renderedVoice?.Dispose();
            renderedVoice = replacement;
            if (replacement != null) renderedSpeed = playBackSpeed;
            live?.Dispose();
            live = null;
            liveSeekPending = false;
            voice.SetSpeed(renderedVoice != null ? 1 : playBackSpeed);
            // Seeking after the frequency change also flushes data queued at the previous rate.
            Timing = timing;
            if (WantsLive) live = LiveSlowStream.TryCreate(Clip, playBackSpeed, timing);
            ActiveVoice.PreparePlayback();
            ApplyVolume();
            VoiceVersion++;
            if (playing) BassNative.Check(BassNative.BASS_ChannelPlay(ActiveVoice.Handle, false), "play music");
            double resumedAt = BassAudio.Clock;
            if (game) game.CompletePlaybackTransition(timing, resumedAt);
            // A countdown is measured in chart seconds, not a fixed wall-clock deadline at the old rate.
            if (scheduledStart >= 0) scheduledStart = resumedAt + System.Math.Max(0, -timing) / playBackSpeed;
        }
        public void Load(BassClip clip)
        {
            ReleaseVoice();
            liveDisabled = false;
            SlowAudioCache.BeginSong(clip);
            Clip = clip;
            voice = new BassVoice(clip, true);
            PlayBackSpeed = playBackSpeed; // the current slow speed, if any, is queued first
            ApplyVolume();
            PreRenderSlowSpeeds();
        }
        // Queues each slow speed whose render is missing (or outdated: the cache key includes the
        // song file's size and modified time). Renders already running for this song are joined.
        private void PreRenderSlowSpeeds()
        {
            foreach (float speed in SlowSpeeds)
            {
                if (SlowAudioCache.TryGet(Clip, speed) != null) continue;
                string path = SlowAudioCache.CachePath(Clip, speed);
                var task = path == null ? null : SlowAudioCache.Request(Clip, speed);
                if (task != null) preRenders.Add(new KeyValuePair<string, Task<string>>(path, task));
            }
        }
        private void UpdateRenderProgress()
        {
            float done = 0;
            bool busy = false;
            foreach (var render in preRenders)
            {
                if (render.Value.IsCompleted) done += 1;
                else { busy = true; done += SlowAudioCache.ProgressOf(render.Key); }
            }
            if (!busy) preRenders.Clear();
            if (busy) ShowRenderProgress(done / preRenders.Count);
            else if (progressBar) progressBar.SetActive(false);
        }
        private void ShowRenderProgress(float fraction)
        {
            if (!progressBar)
            {
                var button = PlayBackSpeedButtonText ? PlayBackSpeedButtonText.transform.parent as RectTransform : null;
                if (!button) return;
                progressBar = new GameObject("SlowRenderProgress", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                progressBar.GetComponent<LayoutElement>().ignoreLayout = true;
                var bar = (RectTransform)progressBar.transform;
                bar.SetParent(button, false);
                bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(.5f, 0);
                bar.offsetMin = new Vector2(8, 1.5f); bar.offsetMax = new Vector2(-8, 3.5f); // 2 units tall along the bottom edge, clear of the text and rounded corners
                progressTrack = progressBar.GetComponent<Image>();
                progressTrack.raycastTarget = false;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                progressFill = (RectTransform)fill.transform;
                progressFill.SetParent(bar, false);
                progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0, 1);
                progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
                progressFillImage = fill.GetComponent<Image>();
                progressFillImage.raycastTarget = false;
            }
            var theme = Arcade.Compose.AdeUiTheme.Current;
            progressTrack.color = theme.Border;
            progressFillImage.color = theme.Accent;
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
            progressBar.SetActive(true);
        }
        public void ApplyVolume()
        {
            if (voice != null) BassNative.Check(BassNative.BASS_ChannelSetAttribute(voice.Handle, 2, BassAudio.MusicVolume), "music volume");
            if (renderedVoice != null) BassNative.Check(BassNative.BASS_ChannelSetAttribute(renderedVoice.Handle, 2, BassAudio.MusicVolume), "music volume");
            if (live != null) BassNative.Check(BassNative.BASS_ChannelSetAttribute(live.Voice.Handle, 2, BassAudio.MusicVolume), "music volume");
        }
        // Restarts live rendering at a pending seek position (a few milliseconds of rendering).
        private void EnsureLive()
        {
            if (live == null || !liveSeekPending) return;
            live.Dispose();
            live = LiveSlowStream.TryCreate(Clip, playBackSpeed, liveSeekTiming);
            liveSeekPending = false;
            ApplyVolume();
            VoiceVersion++;
        }
        // Resumes after seeks during live playback. The chart clock holds still while the new
        // position renders, as in SwitchVoice, so the restart does not leave a lasting offset.
        private void RestartLivePlayback()
        {
            var game = ArcGameplayManager.Instance;
            float timing = game && game.IsPlaying ? game.CapturePlaybackTransition(playBackSpeed, BassAudio.Clock) : liveSeekTiming;
            liveResume = false;
            Timing = timing;
            EnsureLive();
            ActiveVoice.PreparePlayback();
            BassNative.Check(BassNative.BASS_ChannelPlay(ActiveVoice.Handle, false), "play music");
            if (game) game.CompletePlaybackTransition(timing, BassAudio.Clock);
        }
        public void Play()
        {
            scheduledStart = -1;
            SlowAudioCache.SetPlaybackState(true, playBackSpeed);
            liveResume = false;
            EnsureLive();
            if (voice != null) BassNative.Check(BassNative.BASS_ChannelPlay(ActiveVoice.Handle, false), "play music");
        }
        // Live rendering is prepared now, so the scheduled start does not wait for it.
        public void PlayDelayed(float seconds) { EnsureLive(); scheduledStart = BassAudio.Clock + Mathf.Max(0, seconds); }
        public void Pause()
        {
            scheduledStart = -1;
            liveResume = false;
            SlowAudioCache.SetPlaybackState(false, playBackSpeed);
            if (voice != null && BassNative.BASS_ChannelIsActive(ActiveVoice.Handle) != 0)
                BassNative.Check(BassNative.BASS_ChannelPause(ActiveVoice.Handle), "pause music");
        }
        public void Stop()
        {
            scheduledStart = -1;
            liveResume = false;
            SlowAudioCache.SetPlaybackState(false, playBackSpeed);
            if (voice != null) { BassNative.BASS_ChannelStop(ActiveVoice.Handle); Timing = 0; }
        }
        private void Update()
        {
            int state = ActiveVoice == null ? 0 : BassNative.BASS_ChannelIsActive(ActiveVoice.Handle);
            SlowAudioCache.SetPlaybackState(state == 1 || state == 2 || scheduledStart >= 0, playBackSpeed);
            if (live != null && live.Failed)
            {
                liveDisabled = true;
                SwitchVoice(null, playBackSpeed);
            }
            else
            {
                // Seeks while playing (scrolling, the timing slider) pause the live stream; it
                // restarts at the latest position once the seeks pause, not once per frame.
                if (liveSeekPending && LivePlaying) { BassNative.BASS_ChannelPause(live.Voice.Handle); liveResume = true; }
                if (liveResume && BassAudio.Clock - liveSeekClock >= LiveSeekSettle) RestartLivePlayback();
            }
            // Live playback sounds the same as the cached file, so the switch waits for a pause.
            if (pendingRender != null && pendingRender.IsCompleted && !LivePlaying)
            {
                var render = pendingRender;
                pendingRender = null;
                if (voice != null && pendingSpeed == playBackSpeed && render.Status == TaskStatus.RanToCompletion && render.Result != null)
                    SwitchVoice(render.Result, playBackSpeed);
            }
            UpdateRenderProgress();
            if (scheduledStart >= 0 && BassAudio.Clock >= scheduledStart) Play();
        }
        public static int ClearSlowAudioCache()
        {
            var audio = Instance;
            if (audio)
            {
                audio.pendingRender = null;
                audio.preRenders.Clear();
                // Release the cached file before deleting it; live playback keeps the same song time.
                if (audio.renderedVoice != null) audio.SwitchVoice(null, audio.playBackSpeed);
                if (audio.progressBar) audio.progressBar.SetActive(false);
            }
            return SlowAudioCache.Clear();
        }

        public void ReleaseVoice()
        {
            scheduledStart = -1;
            SlowAudioCache.SetPlaybackState(false, playBackSpeed);
            pendingRender = null;
            preRenders.Clear();
            live?.Dispose(); live = null; liveSeekPending = liveResume = false;
            renderedVoice?.Dispose(); renderedVoice = null;
            voice?.Dispose(); voice = null; Clip = null;
        }
        private void OnDestroy() { ReleaseVoice(); if (Instance == this) Instance = null; }
		public void NextPlaybackSpeed()
		{
			if (PlayBackSpeedButtonText.text == "100%")
			{
				PlayBackSpeed = 0.75f;
				PlayBackSpeedButtonText.text = "75%";
			}
			else if (PlayBackSpeedButtonText.text == "75%")
			{
				PlayBackSpeed = 0.5f;
				PlayBackSpeedButtonText.text = "50%";
			}
			else if (PlayBackSpeedButtonText.text == "50%")
			{
				PlayBackSpeed = 0.25f;
				PlayBackSpeedButtonText.text = "25%";
			}
			else
			{
				PlayBackSpeed = 1f;
				PlayBackSpeedButtonText.text = "100%";
			}
		}
	}
}

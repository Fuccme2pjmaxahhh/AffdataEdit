using Arcade.Audio;
using UnityEngine;
using Arcade.Gameplay.Chart;
using UnityEngine.Events;
using Arcade.Gameplay.Events;

namespace Arcade.Gameplay
{
	namespace Events
	{
		public class OnMusicFinishedEvent : UnityEvent
		{

		}
	}

	public class ArcGameplayManager : MonoBehaviour
	{
		public uint SelectionLayerMask;

		public static ArcGameplayManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public ShaderVariantCollection Shaders;
		private void Awake()
		{
			Instance = this;
			gameObject.AddComponent<ArcSlideManager>();
            gameObject.AddComponent<ArcNoteRenderer>();
		}
		private void Start()
		{
			Shaders.WarmUp();
		}

		public bool Auto { get; set; }
		public bool IsPlaying { get; set; }
		// Explicit seeks invalidate click cycling; normal playback advances without changing this.
		public uint SeekVersion { get; private set; }
		private float audioTiming;
		public int AudioTiming
		{
			get
			{
				return Mathf.RoundToInt(audioTiming * 1000);
			}
			set
			{
				SeekVersion++;
				audioTiming = value / 1000f;
				ArcAudioManager.Instance.Timing = Mathf.Max(0, audioTiming);
			}
		}
		private float AudioTimingWithoutGlobalOffset
		{
			get
			{
				return audioTiming + (GlobalAudioOffset + DisplayDelayChart) / 1000f;
			}
			set
			{
				audioTiming = value - (GlobalAudioOffset + DisplayDelayChart) / 1000f;
			}
		}
		public int ChartAudioOffset { get; set; }
		public int GlobalAudioOffset { get; set; }
		// Visual-only delay in real ms (画面延迟补偿 + measured output latency): notes are drawn this much
		// later than the music, while hit sounds keep firing on the music's time (SoundChartTiming).
		public int DisplayDelay { get; set; }
		// In chart time: the timeline runs at the playback speed.
		public int DisplayDelayChart => Mathf.RoundToInt(DisplayDelay * (ArcAudioManager.Instance ? ArcAudioManager.Instance.PlayBackSpeed : 1));
		// Chart time to trigger hit sounds at: the drawn time plus the visual delay, so sounds stay on the music.
		public int SoundChartTiming => ChartTiming + DisplayDelayChart;
		public int ChartTiming
		{
			get
			{
				return AudioTiming - ChartAudioOffset;
			}
			set
			{
				AudioTiming = value + ChartAudioOffset;
			}
		}
		public int Length { get; private set; }

		public int AllBeginChartTiming
		{
			get
			{
				return -ChartAudioOffset;
			}
		}
		public int AllEndChartTiming
		{
			get
			{
				return Length - ChartAudioOffset;
			}
		}
		public bool IsLoaded
		{
			get
			{
				return Chart != null;
			}
		}

		public UnityEvent OnChartLoad = new UnityEvent();
		[System.NonSerialized]
		public OnMusicFinishedEvent OnMusicFinished = new OnMusicFinishedEvent();
		public ArcChart Chart { get; set; }

		public float TimingPointDensityFactor
		{
			get
			{
				return Chart.TimingPointDensityFactor;
			}
		}

		public bool EnablePlaybackSync = false;

		private double lastDspTime = 0;
		private double deltaDspTime = 0;

		// Playback sync corrects drift only. The music's reported position (from the BASS_FX tempo
		// stream, used even at 100%) trails what is heard, so the gap between the clock and that
		// position is measured once after playback starts and kept, instead of snapping to it.
		private const int SyncCalibrationSamples = 12;
		// SyncRate 20/s removes about 95% of any drift within 150 ms.
		private const float SyncDeadZone = .003f, SyncResnap = .25f, SyncRate = 20f;
		private float syncBaseline, syncSum, lastSyncPosition = -1, syncSpeed;
		private int syncSamples;
		private uint syncSeekVersion, syncVoiceVersion;
		private bool syncWasPlaying;

		private void ResetPlaybackSync()
		{
			syncSamples = 0;
			syncSum = 0;
			lastSyncPosition = -1;
		}

		private void Update()
		{
			double now = BassAudio.Clock;
			AdvancePlaybackClock(now, IsPlaying ? ArcAudioManager.Instance.PlayBackSpeed : 1);
			if (IsPlaying)
			{
				float playBackSpeed = ArcAudioManager.Instance.PlayBackSpeed;
				uint voiceVersion = ArcAudioManager.Instance.VoiceVersion;
				if (!syncWasPlaying || syncSeekVersion != SeekVersion || syncSpeed != playBackSpeed || syncVoiceVersion != voiceVersion) ResetPlaybackSync();
				syncSeekVersion = SeekVersion;
				syncVoiceVersion = voiceVersion;
				syncSpeed = playBackSpeed;
				if (EnablePlaybackSync && AudioTimingWithoutGlobalOffset <= Length) SyncToMusic();
			}
			syncWasPlaying = IsPlaying;
			if (AudioTiming > Length)
			{
				OnMusicFinished.Invoke();
				Stop();
			}
		}

        private void AdvancePlaybackClock(double now, float speed)
        {
            deltaDspTime = System.Math.Max(0, now - lastDspTime);
            lastDspTime = now;
            if (IsPlaying) audioTiming += (float)deltaDspTime * speed;
        }

        // Preserve the music/sound clock across rate changes. DisplayDelayChart changes with
        // speed, so preserving only the drawn time would jump hit sounds backwards/forwards.
        internal float CapturePlaybackTransition(float previousSpeed, double now)
        {
            AdvancePlaybackClock(now, previousSpeed);
            return audioTiming + (GlobalAudioOffset + Mathf.RoundToInt(DisplayDelay * previousSpeed)) / 1000f;
        }

        internal void CompletePlaybackTransition(float transportTiming, double now)
        {
            if (IsPlaying) AudioTimingWithoutGlobalOffset = transportTiming;
            // Buffer preparation and seeking happened while music was paused. Do not advance
            // the chart through that wall time, or every switch accumulates a phase error.
            lastDspTime = now;
            ResetPlaybackSync();
        }
		private void SyncToMusic()
		{
			float position = ArcAudioManager.Instance.Timing;
			// Wait for the music to start; sample only fresh positions, since BASS updates them in steps.
			if (position <= 0 || deltaDspTime <= 0 || position == lastSyncPosition) return;
			lastSyncPosition = position;
			float gap = AudioTimingWithoutGlobalOffset - position;
			if (syncSamples < SyncCalibrationSamples)
			{
				syncSum += gap;
				if (++syncSamples == SyncCalibrationSamples) syncBaseline = syncSum / SyncCalibrationSamples;
				return;
			}
			float drift = gap - syncBaseline;
			// A large jump means the music stalled or skipped: follow it at once, keeping the baseline.
			if (Mathf.Abs(drift) > SyncResnap) AudioTimingWithoutGlobalOffset = position + syncBaseline;
			else if (Mathf.Abs(drift) > SyncDeadZone)
				AudioTimingWithoutGlobalOffset -= drift * Mathf.Min(1, (float)deltaDspTime * SyncRate);
		}

		public bool Load(ArcChart chart, BassClip audio)
		{
			if (audio == null || chart == null) return false;

			Clean();
			Chart = chart;
			Length = (int)(audio.length * 1000);
			ChartAudioOffset = chart.AudioOffset;

			ArcCameraManager.Instance.ResetCamera();
			ArcAudioManager.Instance.Load(audio);
			ArcTimingManager.Instance.Load(chart.Timings, chart.TimingGroups);
			ArcTapNoteManager.Instance.Load(chart.Taps);
			ArcHoldNoteManager.Instance.Load(chart.Holds);
			ArcArcManager.Instance.Load(chart.Arcs);
			ArcSlideManager.Instance.Load(chart.Slides);
			ArcCameraManager.Instance.Load(chart.Cameras);
			ArcSceneControlManager.Instance.Load(chart.SceneControl);

			OnChartLoad.Invoke();
			return true;
		}
		public void Clean()
		{
            ArcNoteRenderer.Instance?.ReleaseChartMemory();
            ArcAudioManager.Instance.ReleaseVoice();
			AudioTiming = 0;
			ArcTimingManager.Instance.Clean();
			ArcTapNoteManager.Instance.Clean();
			ArcHoldNoteManager.Instance.Clean();
			ArcArcManager.Instance.Clean();
			ArcSlideManager.Instance.Clean();
			ArcCameraManager.Instance.Clean();
			ArcSceneControlManager.Instance.Clean();
            ArcEffectManager.Instance?.ReleaseChartMemory();
			Chart = null;
			Length = 0;
		}

		public void ResetJudge()
		{
            if (ArcTapNoteManager.Instance) ArcTapNoteManager.Instance.ResetJudgement();
            if (ArcArcManager.Instance) ArcArcManager.Instance.ResetJudgement();
            if (ArcEffectManager.Instance) ArcEffectManager.Instance.ResetHitSoundDeduplication();
            ArcLongNoteEffect.ResetAll();
			if (ArcSlideManager.Instance) ArcSlideManager.Instance.ResetFeedback();
			if (Chart != null)
			{
				foreach (var t in Chart.Arcs)
				{
					foreach (var a in t.ArcTaps) { a.Judged = false; }
					t.Judged = false; t.Judging = false; t.AudioPlayed = false;
					if (t.ConvertedVariousSizedArctap != null)
					{
						t.ConvertedVariousSizedArctap.Judged = false;
					}
				};
				foreach (var t in Chart.Holds) { t.Judged = false; t.Judging = false; t.AudioPlayed = false; };
				foreach (var t in Chart.Taps) { t.Judged = false; };
			}
		}
		public void PlayDelayed()
		{
			SeekVersion++;
			AudioTimingWithoutGlobalOffset = -3f;
			ResetJudge();
			ArcAudioManager.Instance.Stop();
			ArcAudioManager.Instance.Timing = 0;
			ArcAudioManager.Instance.PlayDelayed(3 / ArcAudioManager.Instance.PlayBackSpeed);
			lastDspTime = BassAudio.Clock;
			IsPlaying = true;
		}

		public void Play()
		{
			if (AudioTimingWithoutGlobalOffset < 0)
			{
				ArcAudioManager.Instance.Stop();
				ArcAudioManager.Instance.Timing = 0;
				ArcAudioManager.Instance.PlayDelayed(-AudioTimingWithoutGlobalOffset / ArcAudioManager.Instance.PlayBackSpeed);
			}
			else
			{
				ArcAudioManager.Instance.Timing = AudioTimingWithoutGlobalOffset;
				if (AudioTimingWithoutGlobalOffset <= Length)
				{
					ArcAudioManager.Instance.Play();
				}
			}
			// Seeking and starting the stream can prepare buffers synchronously.
			lastDspTime = BassAudio.Clock;
			IsPlaying = true;
		}
		public void Pause()
		{
            Pause(true);
		}
        public void Pause(bool resetJudge)
		{
			ArcAudioManager.Instance.Pause();
			IsPlaying = false;
			if (resetJudge) ResetJudge();
		}
		public void Stop()
		{
			SeekVersion++;
			audioTiming = 0;
			ArcAudioManager.Instance.Pause();
			ArcAudioManager.Instance.Timing = 0;
			IsPlaying = false;
		}


	}
}


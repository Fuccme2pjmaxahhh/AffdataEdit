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
				return audioTiming + GlobalAudioOffset / 1000f;
			}
			set
			{
				audioTiming = value - GlobalAudioOffset / 1000f;
			}
		}
		public int ChartAudioOffset { get; set; }
		public int GlobalAudioOffset { get; set; }
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

		private void Update()
		{
			double now = BassAudio.Clock;
			deltaDspTime = now - lastDspTime;
			lastDspTime = now;
			if (IsPlaying)
			{
				float playBackSpeed = ArcAudioManager.Instance.PlayBackSpeed;
				// Count only time after playback began, not the previous editor frame.
				AudioTimingWithoutGlobalOffset += (float)deltaDspTime * playBackSpeed;
				if (EnablePlaybackSync && AudioTimingWithoutGlobalOffset <= Length)
				{
					float t = ArcAudioManager.Instance.Timing;
					if (deltaDspTime > 0f && (AudioTimingWithoutGlobalOffset >= 0 || ArcAudioManager.Instance.Timing > 0))
					{
						float delta = AudioTimingWithoutGlobalOffset - t;
                        const float maxDelta = .04f;
						if (Mathf.Abs(delta) > maxDelta)
						{
							AudioTimingWithoutGlobalOffset = t;
						}
					}
				}
			}
			if (AudioTiming > Length)
			{
				OnMusicFinished.Invoke();
				Stop();
			}
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
            ArcAudioManager.Instance.ReleaseVoice();
			AudioTiming = 0;
			ArcTimingManager.Instance.Clean();
			ArcTapNoteManager.Instance.Clean();
			ArcHoldNoteManager.Instance.Clean();
			ArcArcManager.Instance.Clean();
			ArcSlideManager.Instance.Clean();
			ArcCameraManager.Instance.Clean();
			ArcSceneControlManager.Instance.Clean();
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


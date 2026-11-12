using Arcade.Audio;
using System;
using System.Collections.Generic;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using Arcade.Util.Pooling;
using UnityEngine;

namespace Arcade.Gameplay
{
	public class ArcEffectManager : MonoBehaviour
	{
		public static ArcEffectManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public ArcEffectPlane EffectPlane;
		private void Awake()
		{
			Instance = this;
		}
		private void Start()
		{
			hitTypes = gameObject.AddComponent<ArcHitTypeEffects>();
			tapNoteEffectPool = new GameObjectPool<ArcTapNoteEffectComponent>(TapNoteJudgeEffect, EffectPlane.transform, 1);
			sfxTapNoteEffectPool = new GameObjectPool<ArcTapNoteEffectComponent>(SfxTapNoteJudgeEffect, EffectPlane.transform, 1);
			for (int i = 0; i < 6; ++i)
			{
				HoldNoteEffectPosition[i] = HoldNoteEffects[i].transform.position;
                HoldNoteEffects[i].ResetState();
			};
		}

		public GameObject TapNoteJudgeEffect;
		public GameObject SfxTapNoteJudgeEffect;
		public GameObject[] LaneHits = new GameObject[6];
		public ArcLongNoteEffect[] HoldNoteEffects = new ArcLongNoteEffect[6];
		public Vector3[] HoldNoteEffectPosition = new Vector3[6];
		public Transform EffectLayer;
		[System.NonSerialized] public BassClip TapAudio, ArcAudio;

		[System.NonSerialized]
		public Dictionary<string, BassClip> SpecialEffectAudios = new Dictionary<string, BassClip>();

		// Long-note particles follow Alpha's ArcLongNoteParticleManager: every hold binds its
		// own pooled particle (GetHoldParticle(ArcHold)), and arcs share one particle per
		// color (GetArcParticle(color)). HoldNoteEffects stay as skin/position templates.
		private sealed class HoldEffect
		{
			public ArcLongNoteEffect Effect;
			public bool Requested;
		}
		private readonly Dictionary<ArcHold, HoldEffect> holdEffects = new Dictionary<ArcHold, HoldEffect>();
		private readonly Stack<ArcLongNoteEffect> freeHoldEffects = new Stack<ArcLongNoteEffect>();
		private readonly List<ArcHold> releasedHolds = new List<ArcHold>();

		private sealed class ArcEffect
		{
			public ArcLongNoteEffect Effect;
			public readonly List<ArcArcVisual> Owners = new List<ArcArcVisual>();
		}
		private readonly Dictionary<int, ArcEffect> arcEffects = new Dictionary<int, ArcEffect>();

		private ArcLongNoteEffect CreateLongNoteAnchor(string name, ArcLongNoteEffect template)
		{
			var go = new GameObject(name, typeof(ArcLongNoteEffect));
			go.layer = template.gameObject.layer;
			go.transform.SetParent(HoldNoteEffects[0].transform.parent, false);
			var effect = go.GetComponent<ArcLongNoteEffect>();
			effect.enabled = false;
			CopyLongNoteSkin(template, effect);
			return effect;
		}

		private static void CopyLongNoteSkin(ArcLongNoteEffect from, ArcLongNoteEffect to)
		{
			to.StartColor = from.StartColor;
			to.EndColor = from.EndColor;
			to.Texture = from.Texture;
		}

		public void SetHoldNoteEffect(ArcHold note)
		{
			if (!holdEffects.TryGetValue(note, out var state) || !state.Effect)
			{
				ArcLongNoteEffect effect = null;
				while (freeHoldEffects.Count > 0 && !effect) effect = freeHoldEffects.Pop();
				state = new HoldEffect { Effect = effect ? effect : CreateLongNoteAnchor("Hold hit", HoldNoteEffects[0]) };
				holdEffects[note] = state;
			}
			state.Requested = true;
			state.Effect.transform.position = note.FloatLane.HasValue
				? EffectPlane.GetPositionOnPlane(new Vector2(note.WorldX, 0))
				: EffectPlane.GetPositionOnPlane(HoldNoteEffectPosition[note.Track]);
		}

		public void RemoveFloatHoldNoteEffect(ArcHold note)
		{
			if (!holdEffects.TryGetValue(note, out var state)) return;
			holdEffects.Remove(note);
			ReleaseHoldEffect(state.Effect);
		}

		private void ReleaseHoldEffect(ArcLongNoteEffect effect)
		{
			if (!effect) return;
			effect.ResetState();
			if (freeHoldEffects.Count < 32 || ArcGameplayManager.Instance && ArcGameplayManager.Instance.IsPlaying)
                freeHoldEffects.Push(effect);
			else Destroy(effect.gameObject);
		}

        internal void ReleaseChartMemory()
        {
            ArcLongNoteEffect.ResetAll();
            foreach (var state in holdEffects.Values) ReleaseHoldEffect(state.Effect);
            holdEffects.Clear(); releasedHolds.Clear();
            while (freeHoldEffects.Count > 8)
            {
                var effect = freeHoldEffects.Pop();
                if (effect) Destroy(effect.gameObject);
            }
            foreach (var state in arcEffects.Values)
            {
                state.Owners.Clear();
                if (state.Effect) { state.Effect.ResetState(); Destroy(state.Effect.gameObject); }
            }
            arcEffects.Clear();
            if (hitTypes) hitTypes.ReleaseChartMemory();
            tapNoteEffectPool?.TrimAvailable(1);
            sfxTapNoteEffectPool?.TrimAvailable(1);
            ArcLongNoteParticlePool.TrimIdle(8);
        }

		// Arc renderers report their judging state; the color's particle follows the most
		// recent judging arc of that color and keeps emitting while any of them judges.
		internal void SetArcEffect(ArcArcVisual renderer, int color, bool enable)
		{
			if (!arcEffects.TryGetValue(color, out var state))
			{
				state = new ArcEffect();
				arcEffects.Add(color, state);
			}
			var skin = ArcArcManager.Instance.ArcSkin.JudgeEffect;
			bool registered = state.Owners.Contains(renderer);
            if (registered == enable && (!enable || (state.Effect && state.Effect.Active))) return;
			if (enable && !registered) state.Owners.Add(renderer);
			else if (!enable) state.Owners.Remove(renderer);
			if (!state.Effect) state.Effect = CreateLongNoteAnchor($"Arc hit {color}", skin);
			ArcArcVisual current = state.Owners.Count > 0 ? state.Owners[state.Owners.Count - 1] : null;
			var effect = state.Effect;
			if (current != null)
			{
				// Keep the shared anchor outside pooled Arc objects; follow the judge point in LateUpdate.
				CopyLongNoteSkin(skin, state.Effect);
				effect.FollowTarget = current;
				effect.RefreshSkin();
				effect.SetEmission(true);
			}
			else
			{
				effect.FollowTarget = null;
				effect.SetEmission(false);
			}
		}

        // Alpha deduplicates the two standard hit sounds independently within 10 ms.
        private const double HitSoundDeduplicationSeconds = 0.010;
        private double lastTapSoundTime = double.NegativeInfinity;
        private double lastArcSoundTime = double.NegativeInfinity;

        public void ResetHitSoundDeduplication()
        {
            lastTapSoundTime = lastArcSoundTime = double.NegativeInfinity;
        }

        private void PlayHitSound(BassClip clip, ref double lastPlayedTime)
        {
            if (!clip) return;
            double now = Time.unscaledTimeAsDouble;
            if (now - lastPlayedTime <= HitSoundDeduplicationSeconds) return;
            BassAudio.PlayOneShot(clip);
            lastPlayedTime = now;
        }

		private GameObjectPool<ArcTapNoteEffectComponent> tapNoteEffectPool;
		private GameObjectPool<ArcTapNoteEffectComponent> sfxTapNoteEffectPool;
        private ArcTapNoteEffectComponent tapEmitter, sfxEmitter;
        private ArcHitTypeEffects hitTypes;

        public void ShowLongNoteHitType(ArcLongNote note, Vector3 position) => hitTypes.ShowLongNote(note, position);
        public void ShowSlideHitType(ArcSlide slide) => hitTypes.ShowSlide(slide);

		void Update()
		{
			releasedHolds.Clear();
			foreach (var pair in holdEffects)
			{
				var effect = pair.Value.Effect;
				effect.SetEmission(pair.Value.Requested);
				// Return the anchor once its 200 ms stop delay has finished.
				if (!pair.Value.Requested && !effect.Active) releasedHolds.Add(pair.Key);
			}
			foreach (ArcHold hold in releasedHolds)
			{
				ReleaseHoldEffect(holdEffects[hold].Effect);
				holdEffects.Remove(hold);
			}
		}

		public void AddSpecialEffectAudio(string effect, BassClip audio)
		{
			if (SpecialEffectAudios.TryGetValue(effect, out BassClip previous) && previous != audio) Destroy(previous);
			SpecialEffectAudios[effect] = audio;
		}

		public void RetainSpecialEffectAudios(ISet<string> used)
		{
			var unused = new List<string>();
			foreach (var effect in SpecialEffectAudios.Keys)
				if (!used.Contains(effect)) unused.Add(effect);
			foreach (string effect in unused)
			{
				Destroy(SpecialEffectAudios[effect]);
				SpecialEffectAudios.Remove(effect);
			}
		}

		public void CleanSpecialEffectAudios()
		{
			foreach (var audio in SpecialEffectAudios.Values)
			{
				Destroy(audio);
			}
			SpecialEffectAudios.Clear();
		}

		// playSound false: the caller plays the hit sound itself on the music's time (see SoundChartTiming).
		public void PlayTapNoteEffectAt(Vector2 pos, bool isArc = false, string arcTapEffect = "none", bool playSound = true)
		{
            hitTypes.ShowPure(pos);
            Vector3 world = EffectPlane.GetPositionOnPlane(pos);
            if (arcTapEffect.EndsWith("_wav"))
            {
                if (!sfxEmitter) sfxEmitter = sfxTapNoteEffectPool.Get(effect => effect.PlayAt(world));
                else sfxEmitter.PlayAt(world);
            }
            else
            {
                bool combine = !isArc;
                if (!tapEmitter) tapEmitter = tapNoteEffectPool.Get(effect => effect.PlayAt(world, combine));
                else tapEmitter.PlayAt(world, combine);
            }
			if (!playSound) return;
			if (isArc) PlayArcTapSound(arcTapEffect);
			else PlayTapSound();
		}
		public void PlayArcTapSound(string arcTapEffect)
		{
			if (SpecialEffectAudios.ContainsKey(arcTapEffect))
			{
				BassAudio.PlayOneShot(SpecialEffectAudios[arcTapEffect], true);
			}
			else
			{
				PlayArcSound();
			}
		}
		public void PlayTapSound()
		{
			PlayHitSound(TapAudio, ref lastTapSoundTime);
		}
		public void PlayArcSound()
		{
			PlayHitSound(ArcAudio, ref lastArcSoundTime);
		}
		public void ResetHoldNoteEffect()
		{
			foreach (var state in holdEffects.Values) state.Requested = false;
		}

		public void SetParticleArcColor(Color particleArcStartColor, Color particleArcEndColor)
		{

			foreach (ArcLongNoteEffect holdEffect in HoldNoteEffects)
			{
				holdEffect.StartColor = particleArcStartColor;
				holdEffect.EndColor = particleArcEndColor;
                holdEffect.RefreshSkin();
			}
			foreach (ArcLongNoteEffect effect in LongNoteAnchors())
			{
				effect.StartColor = particleArcStartColor;
				effect.EndColor = particleArcEndColor;
				effect.RefreshSkin();
			}
            ArcArcManager.Instance.SetParticleArcColor(particleArcStartColor, particleArcEndColor);
		}

		private IEnumerable<ArcLongNoteEffect> LongNoteAnchors()
		{
			foreach (var state in holdEffects.Values) if (state.Effect) yield return state.Effect;
			foreach (ArcLongNoteEffect effect in freeHoldEffects) if (effect) yield return effect;
			foreach (var state in arcEffects.Values) if (state.Effect) yield return state.Effect;
		}

		internal void SetTapEffectConflictBlend(bool value)
		{
			tapNoteEffectPool.Modify(effect => effect.SetConflictBlend(value));
			sfxTapNoteEffectPool.Modify(effect => effect.SetConflictBlend(value));
		}

		internal void SetTapEffectTexture(Texture2D particleTap)
		{
			tapNoteEffectPool.Modify(effect =>
			{
				effect.SetTexture(particleTap);
			});
		}

		internal void SetSfxTapEffectTexture(Texture2D particleSfxTap)
		{
			sfxTapNoteEffectPool.Modify(effect =>
			{
				effect.SetTexture(particleSfxTap);
			});
		}

		internal void SetParticleArcTexture(Texture2D texture)
		{

			foreach (ArcLongNoteEffect holdEffect in HoldNoteEffects)
			{
				holdEffect.Texture = texture;
                holdEffect.RefreshSkin();
			}
			foreach (ArcLongNoteEffect effect in LongNoteAnchors())
			{
				effect.Texture = texture;
				effect.RefreshSkin();
			}
            ArcArcManager.Instance.SetParticleArcTexture(texture);
		}
	}
}

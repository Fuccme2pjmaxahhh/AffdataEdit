using Arcade.Audio;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using LitMotion;

namespace Arcade.Compose
{
	public class AdeShutterManager : MonoBehaviour
	{
		public const float OpenDuration = 0.3f;
		public const float CloseDuration = 0.75f;
		public static AdeShutterManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		private void Awake()
		{
			if (Instance != null)
			{
				Destroy(gameObject);
				return;
			}
			Instance = this;
			DontDestroyOnLoad(gameObject);
		}

		public RectTransform Left, Right;
		[System.NonSerialized] public BassClip CloseAudio, OpenAudio;
		private MotionHandle shutterMotion;

		private void OnDestroy()
		{
			shutterMotion.TryCancel();
		}

		public void Open()
		{
			AnimateShutter(true);
			BassAudio.PlayOneShot(OpenAudio, true);
		}
		public void Close()
		{
			AnimateShutter(false);
			BassAudio.PlayOneShot(CloseAudio, true);
		}
		private void AnimateShutter(bool open)
		{
			shutterMotion.TryCancel();
			float leftStart = Left.pivot.x;
			float rightStart = Right.pivot.x;
			shutterMotion = LMotion.Create(0f, 1f, open ? OpenDuration : CloseDuration)
				.WithEase(open ? Ease.Linear : Ease.OutCubic)
				.WithScheduler(MotionScheduler.Update)
				.Bind(progress =>
				{
					if (open) progress = Mathf.Pow(progress, 1.5f);
					Vector2 leftPivot = Left.pivot;
					Vector2 rightPivot = Right.pivot;
					leftPivot.x = Mathf.LerpUnclamped(leftStart, open ? 1 : 0, progress);
					rightPivot.x = Mathf.LerpUnclamped(rightStart, open ? 0 : 1, progress);
					Left.pivot = leftPivot;
					Right.pivot = rightPivot;
				});
		}
		public async UniTask OpenAsync(CancellationToken cancellationToken = default)
		{
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken, cancellationToken);
			Open();
			await UniTask.WaitForSeconds(OpenDuration, cancellationToken: lifetime.Token);
		}
		public async UniTask CloseAsync(CancellationToken cancellationToken = default)
		{
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken, cancellationToken);
			Close();
			await UniTask.WaitForSeconds(CloseDuration, cancellationToken: lifetime.Token);
		}
	}
}

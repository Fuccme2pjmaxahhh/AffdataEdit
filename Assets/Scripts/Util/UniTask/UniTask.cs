using System.Threading;
using UnityEngine.Networking;
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Arcade.Util.UniTaskHelper
{
	public static class UniTaskHelper
	{
        // Preserve callers' request.result handling for HTTP/transport failures.
        // Cancellation aborts the request; it never becomes a successful result.
        public static async UniTask SendWithResultAsync(this UnityWebRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = request.SendWebRequest();
            using (cancellationToken.Register(request.Abort))
                await UniTask.WaitUntil(() => operation.isDone, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

		public static async UniTask WithExceptionLogger(this UniTask task)
		{
			try
			{
				await task;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Debug.LogException(ex);
			}
		}
	}
}
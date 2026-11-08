using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Arcade.Compose.MarkingMenu;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Arcade.Compose
{
	public struct AdeOngoingOperation
	{
		public UniTask task;
		public CancellationTokenSource cancellation;
		public System.Action cancel;
		public bool keepSelection;
	}
	public struct AdeOperationResult
	{
		public bool operationExecuted;
		public AdeOngoingOperation? startedOperation;
		public static implicit operator AdeOperationResult(bool operationExecuted) => new AdeOperationResult { operationExecuted = operationExecuted, startedOperation = null };
		public static AdeOperationResult FromOngoingOperation(AdeOngoingOperation startedOperation) => new AdeOperationResult { operationExecuted = true, startedOperation = startedOperation };
	}
	public abstract class AdeOperation : AdeMarkingMenuItemProvider
	{
		public abstract AdeOperationResult TryExecuteOperation();
		public virtual void Reset() { }
	}
	public class AdeOperationManager : AdeMarkingMenuItemProvider
	{
		public static AdeOperationManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		private AdeOngoingOperation? ongoingOperation = null;
		public bool HasOngoingOperation { get => ongoingOperation != null; }
		public AdeOperation[] operations;
		private Editing.AdeDragEdit dragEdit;


		public MarkingMenuItem CancelMenuEntry;
		public override bool IsOnlyMarkingMenu => HasOngoingOperation;
		public override MarkingMenuItem[] MarkingMenuItems
		{
			get
			{
				if (HasOngoingOperation)
				{
					List<MarkingMenuItem> items = new List<MarkingMenuItem>
					{
						CancelMenuEntry
					};
					items.AddRange(AdeGridManager.Instance.MarkingMenuItems);
					return items.ToArray();
				};
				return null;
			}
		}
		private void Awake()
		{
			Instance = this;
			dragEdit = gameObject.AddComponent<Editing.AdeDragEdit>();
		}
		void Update()
		{
			if (ongoingOperation != null)
			{
				if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Inputs.Cancel))
				{
					CancelOngoingOperation();
				}
				if (ongoingOperation?.task.Status != UniTaskStatus.Pending)
				{
					ongoingOperation = null;
				}
				return;
			}
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Inputs.Cancel)
				&& Operation.AdeClickToCreate.Instance.Enable
				&& Operation.AdeClickToCreate.Instance.Mode != Operation.ClickToCreateMode.Idle)
			{
				// On-Arc ArcTap placement is instantaneous, so it has no ongoing task
				// for CancelOngoingOperation to cancel. Escape must also exit that mode.
				Operation.AdeClickToCreate.Instance.Mode = Operation.ClickToCreateMode.Idle;
				foreach (var operation in operations) operation.Reset();
				return;
			}
			var drag = dragEdit.TryExecuteOperation();
			if (drag.operationExecuted)
			{
				SetOngoingOperation(drag.startedOperation);
				return;
			}
			foreach (var operation in operations)
			{
				var result = operation.TryExecuteOperation();
				if (result.operationExecuted)
				{
					SetOngoingOperation(result.startedOperation);
					return;
				}
			}
		}

		private void SetOngoingOperation(AdeOngoingOperation? ongoingOperation)
		{
			if (ongoingOperation != null)
			{
				foreach (var operation in operations)
				{
					operation.Reset();
				}
				if (!ongoingOperation.Value.keepSelection) AdeSelectionManager.Instance.DeselectAllNotes();
			}
			this.ongoingOperation = ongoingOperation;
		}

		public delegate AdeOngoingOperation? OperationExecutor();
		public void TryExecuteOperation(OperationExecutor executor)
		{
			if (ongoingOperation != null)
			{
				AdeToast.Instance.Show("有正在进行的操作，不能执行其他操作");
			}
			else
			{
				var ongoingOperation = executor();
				this.ongoingOperation = ongoingOperation;
			}
		}
		public void CancelOngoingOperation()
		{
			if (ongoingOperation != null)
			{
				ongoingOperation.Value.cancel?.Invoke();
				if (ongoingOperation.Value.task.Status == UniTaskStatus.Pending)
				{
					ongoingOperation.Value.cancellation.Cancel();
				}
				ongoingOperation = null;
			}
		}
	}
}

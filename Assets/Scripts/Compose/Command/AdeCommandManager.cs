using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose.Command
{

	public class AdeCommandManager : MonoBehaviour
	{
		public delegate void OnCommandExecuted(ICommand command, bool undo);
		public OnCommandExecuted onCommandExecuted;
		public static AdeCommandManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		private void Awake()
		{
			Instance = this;
		}
		private bool undoClickable, redoClickable;
		private bool UndoClickable
		{
			get
			{
				return undoClickable;
			}
			set
			{
				if (undoClickable != value)
				{
					UndoButton.interactable = value;
					undoClickable = value;
				}
			}
		}
		private bool RedoClickable
		{
			get
			{
				return redoClickable;
			}
			set
			{
				if (redoClickable != value)
				{
					RedoButton.interactable = value;
					redoClickable = value;
				}
			}
		}
		public Button UndoButton, RedoButton;

		public uint bufferSize = 200;

		private LinkedList<ICommand> undo = new LinkedList<ICommand>();
		private LinkedList<ICommand> redo = new LinkedList<ICommand>();
		private ICommand preparing = null;
        internal const long HistoryMemoryLimitBytes = 128L * 1024 * 1024;
        private readonly Dictionary<ICommand, (long Cost, int References)> memoryCosts = new Dictionary<ICommand, (long, int)>();
        internal long HistoryMemoryBytes { get; private set; }
        // Edit position for the unsaved-changes indicator. A saved position of -1 can no longer be reached.
        private long editPosition, savedPosition;
        public bool HasUnsavedChanges => preparing != null || editPosition != savedPosition;
        internal sealed class SavePoint { internal long Position; }
        private readonly List<SavePoint> pendingSaves = new List<SavePoint>();
        internal SavePoint CaptureSavePoint()
        {
            var point = new SavePoint { Position = preparing == null ? editPosition : -1 };
            pendingSaves.Add(point);
            return point;
        }
        internal void FinishSave(SavePoint point, bool saved)
        {
            if (pendingSaves.Remove(point) && saved) savedPosition = point.Position;
        }
        public void MarkSaved() => savedPosition = editPosition;
        public void MarkChanged()
        {
            savedPosition = -1;
            foreach (var point in pendingSaves) point.Position = -1;
        }
        private void AdvanceEdit()
        {
            // A new edit after undoing past the saved state discards that state from redo.
            if (editPosition < savedPosition) savedPosition = -1;
            foreach (var point in pendingSaves)
                if (editPosition < point.Position) point.Position = -1;
            editPosition++;
        }

		private void Update()
		{
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Undo))
			{
				Undo();
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Redo))
			{
				Redo();
			}
			UndoClickable = undo.Count != 0;
			RedoClickable = redo.Count != 0;
		}

		public void Add(ICommand command)
		{
			if (preparing != null)
			{
				throw new Exception("有正在进行的命令，暂时不能执行新命令");
			}
			command.Do();
			Debug.Log($"Add: {command.Name}");
			undo.AddLast(command);
            AdvanceEdit();
            Remember(command);
            ClearRedo();
            TrimHistory();
			onCommandExecuted?.Invoke(command, false);
		}
		public void Undo()
		{
			if (AdeOperationManager.Instance.HasOngoingOperation)
			{
				AdeOperationManager.Instance.CancelOngoingOperation();
				return;
			}
			if (preparing != null)
			{
				AdeToast.Instance.Show("有正在进行的命令，暂时不能撤销");
				return;
			}
			if (undo.Count == 0) return;
			ICommand cmd = undo.Last.Value;
			cmd.Undo();
			undo.RemoveLast();
			editPosition--;
			Debug.Log($"Undo: {cmd.Name}");
			redo.AddLast(cmd);
			onCommandExecuted?.Invoke(cmd, true);
		}
		public void Redo()
		{
			if (preparing != null)
			{
				AdeToast.Instance.Show("有正在进行的命令，暂时不能重做");
				return;
			}
			if (redo.Count == 0) return;
			ICommand cmd = redo.Last.Value;
			cmd.Do();
			redo.RemoveLast();
			editPosition++;
			Debug.Log($"Redo: {cmd.Name}");
			undo.AddLast(cmd);
			onCommandExecuted?.Invoke(cmd, false);
		}
		public void Clear()
		{
			Cancel();
			undo.Clear();
			redo.Clear();
            memoryCosts.Clear(); HistoryMemoryBytes = 0;
            pendingSaves.Clear();
            editPosition = savedPosition = 0;
		}

		public void SetBufferSize(uint size)
		{
			bufferSize = size;
            TrimHistory();
		}
		public void Prepare(ICommand command)
		{
			if (preparing != null)
			{
				throw new Exception("有正在进行的命令，暂时不能准备新命令");
			}
			preparing = command;
			preparing.Do();
		}
		public void Commit()
		{
			if (preparing != null)
			{
				Debug.Log($"Add: {preparing.Name}");
				undo.AddLast(preparing);
                AdvanceEdit();
                Remember(preparing);
                ClearRedo();
                TrimHistory();
				var committed = preparing;
				preparing = null;
				onCommandExecuted?.Invoke(committed, false);
			}
		}
		public void Cancel()
		{
			if (preparing != null)
			{
				preparing.Undo();
				preparing = null;
			}
		}
        private void Remember(ICommand command)
        {
            long cost = Math.Max(0, CommandMemory.Estimate(command));
            if (memoryCosts.TryGetValue(command, out var entry))
            {
                cost = entry.Cost;
                memoryCosts[command] = (entry.Cost, entry.References + 1);
            }
            else memoryCosts.Add(command, (cost, 1));
            HistoryMemoryBytes += cost;
        }
        private void Forget(ICommand command)
        {
            if (!memoryCosts.TryGetValue(command, out var entry)) return;
            if (entry.References > 1) memoryCosts[command] = (entry.Cost, entry.References - 1);
            else memoryCosts.Remove(command);
            HistoryMemoryBytes -= entry.Cost;
        }
        private void ClearRedo()
        {
            foreach (var command in redo) Forget(command);
            redo.Clear();
        }
        private void TrimHistory()
        {
            // Preserve the newest operation even if a single large import exceeds the budget,
            // so every committed edit remains undoable. A user-selected depth of zero still wins.
            while (undo.Count + redo.Count > bufferSize
                || HistoryMemoryBytes > HistoryMemoryLimitBytes && undo.Count + redo.Count > 1)
            {
                var list = redo.Count > 0 ? redo : undo;
                Forget(list.First.Value);
                list.RemoveFirst();
            }
        }
	}
}


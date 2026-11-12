using System.Linq;
using Arcade.Gameplay.Chart;

namespace Arcade.Compose.Command
{
	public interface ICommand
	{
		string Name
		{
			get;
		}
		void Do();
		void Undo();
	}
    // Conservative retained-data accounting, not a heap profiler. It deliberately excludes
    // live renderer/Unity assets shared with the chart and counts owned snapshots/arrays.
    internal interface ICommandMemoryCost { long EstimatedBytes { get; } }
    internal static class CommandMemory
    {
        internal static long Estimate(ICommand command) => command is ICommandMemoryCost cost ? cost.EstimatedBytes : 1024;
        internal static long Estimate(ArcEvent note)
        {
            if (note == null) return 0;
            long bytes = 512;
            if (note is ArcLongNote hold) bytes += 4L * hold.JudgeTimings.Capacity;
            if (note is ArcArc arc)
                bytes += 8L * arc.ArcTaps.Capacity + arc.ArcTaps.Sum(tap => Estimate(tap)) + 2L * (arc.Effect?.Length ?? 0);
            if (note is ArcSceneControl control)
                bytes += 128L * (control.RawParams?.Count ?? 0) + 2L * (control.RawType?.Length ?? 0);
            return bytes;
        }
    }
	public class BatchCommand : ICommand, ICommandMemoryCost
	{
		public string Name { get; private set; }
		private readonly ICommand[] commands = null;
        long ICommandMemoryCost.EstimatedBytes => 64 + 8L * commands.Length + commands.Sum(CommandMemory.Estimate);
		public BatchCommand(ICommand[] commands, string description)
		{
			this.commands = commands;
			Name = description;
		}
		public void Do()
		{
			foreach (var c in commands)
			{
				c.Do();
			}
		}
		public void Undo()
		{
			foreach (var c in commands.Reverse())
			{
				c.Undo();
			}
		}
	}
}

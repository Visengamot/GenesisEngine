namespace Genesis.Engine.Core;

public enum WorldCommandKind { Tool, PopulationLimit }
public sealed record WorldCommand(long Tick, long Sequence, string Source, WorldCommandKind Kind,
    GodTool Tool = GodTool.Inspect, Vec2 Position = default, float Radius = 1, float Strength = 0, int Limit = 1000);
public sealed record CommandResult(long Tick, long Sequence, bool Accepted, string Reason);

public sealed partial class Simulation
{
    private const int MaximumPendingCommands = 4096;
    private readonly List<WorldCommand> pendingCommands = new();
    private readonly List<CommandResult> commandResults = new();
    private long nextCommandSequence;
    public IReadOnlyList<WorldCommand> PendingCommands => pendingCommands.AsReadOnly();
    /// <summary>Bounded acknowledgement history, not the authoritative replay input log.</summary>
    public IReadOnlyList<CommandResult> CommandResults => commandResults.AsReadOnly();

    /// <summary>Queues an immutable command for a future tick. Returned sequence defines equal-tick order.
    /// Callers retaining a replay log must store the returned command, including its assigned sequence.</summary>
    public WorldCommand QueueCommand(WorldCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (pendingCommands.Count >= MaximumPendingCommands) throw new InvalidOperationException("Command queue is full.");
        if (command.Tick <= Tick) throw new ArgumentOutOfRangeException(nameof(command), "Command must target a future tick.");
        ValidateCommand(command);
        if (nextCommandSequence == long.MaxValue) throw new InvalidOperationException("Command sequence exhausted.");
        var queued = command with { Sequence = nextCommandSequence++ };
        pendingCommands.Add(queued);
        pendingCommands.Sort(CompareCommands);
        return queued;
    }

    private static int CompareCommands(WorldCommand a, WorldCommand b)
    { int tick = a.Tick.CompareTo(b.Tick); return tick != 0 ? tick : a.Sequence.CompareTo(b.Sequence); }

    private static void ValidateCommand(WorldCommand c)
    {
        if (!Enum.IsDefined(c.Kind) || string.IsNullOrWhiteSpace(c.Source) || c.Source.Length > 64)
            throw new ArgumentException("Invalid command kind or source.");
        if (c.Kind == WorldCommandKind.PopulationLimit)
        {
            if (c.Limit < MinimumPopulationLimit || c.Limit > AbsoluteMaximumPopulation)
                throw new ArgumentOutOfRangeException(nameof(c.Limit));
        }
        else
        {
            if (!Enum.IsDefined(c.Tool)) throw new ArgumentException("Invalid tool.");
            Guard.Position(c.Position, nameof(c.Position));
            Guard.Finite(c.Radius, nameof(c.Radius)); Guard.Finite(c.Strength, nameof(c.Strength));
            if (c.Radius <= 0 || c.Strength < 0) throw new ArgumentException("Invalid brush parameters.");
        }
    }

    private bool ExecuteCommands()
    {
        int consumed = 0;
        foreach (var c in pendingCommands)
        {
            if (c.Tick > Tick) break;
            bool accepted = true; string reason = "ok";
            if (c.Kind == WorldCommandKind.PopulationLimit) SetPopulationLimit(c.Limit);
            else if (c.Tool == GodTool.People && Agents.Count >= PopulationLimit)
            { accepted = false; reason = "population-limit"; }
            else if (c.Tool == GodTool.Predator && predators.Count >= 64)
            { accepted = false; reason = "predator-limit"; }
            else if ((c.Tool is GodTool.People or GodTool.Predator) && c.Position != Clamp(c.Position))
            { accepted = false; reason = "outside-map"; }
            else ApplyTool(c.Tool, c.Position, c.Radius, c.Strength);
            if (commandResults.Count == 256) commandResults.RemoveAt(0);
            commandResults.Add(new(Tick, c.Sequence, accepted, reason));
            consumed++;
        }
        if (consumed > 0) pendingCommands.RemoveRange(0, consumed);
        return consumed > 0;
    }

    private void RestoreCommands(WorldSave save)
    {
        if (save.Version < 7) return;
        if (save.PendingCommands is null || save.PendingCommands.Length > MaximumPendingCommands || save.NextCommandSequence < 0)
            throw new ArgumentException("Invalid command queue.");
        var ids = new HashSet<long>();
        WorldCommand? previous = null;
        foreach (var c in save.PendingCommands)
        {
            if (c is null || c.Tick <= Tick || c.Sequence < 0 || c.Sequence >= save.NextCommandSequence || !ids.Add(c.Sequence))
                throw new ArgumentException("Invalid command schedule.");
            ValidateCommand(c);
            if (previous is not null && CompareCommands(previous, c) >= 0) throw new ArgumentException("Unordered command queue.");
            pendingCommands.Add(c); previous = c;
        }
        nextCommandSequence = save.NextCommandSequence;
        if (save.CommandResults is null || save.CommandResults.Length > 256) throw new ArgumentException("Invalid command results.");
        long lastSequence = -1; long lastTick = -1;
        foreach (var r in save.CommandResults)
        {
            if (r is null || r.Tick < 0 || r.Tick > Tick || r.Sequence < 0 || r.Sequence >= nextCommandSequence ||
                string.IsNullOrEmpty(r.Reason) || r.Reason.Length > 64 || r.Tick < lastTick ||
                (r.Tick == lastTick && r.Sequence <= lastSequence) || !ids.Add(r.Sequence))
                throw new ArgumentException("Invalid command acknowledgement.");
            commandResults.Add(r); lastTick = r.Tick; lastSequence = r.Sequence;
        }
    }
}

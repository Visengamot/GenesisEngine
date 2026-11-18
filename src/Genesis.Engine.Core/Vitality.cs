namespace Genesis.Engine.Core;

public enum DamageKind { Fire, Climate, Predator, Conflict }
public sealed record DamageEvent(long Tick, int TargetId, int? SourceAgentId, DamageKind Kind, float Amount);

public sealed partial class Simulation
{
    private readonly List<DamageEvent> damageHistory = new();
    /// <summary>Last 256 actual health losses in phase/iteration order, not an unbounded event log.</summary>
    public IReadOnlyList<DamageEvent> DamageHistory => damageHistory.AsReadOnly();

    private float Damage(Agent target, float amount, DamageKind kind, int? sourceAgentId = null)
    {
        if (amount <= 0 || target.Health <= 0) return 0;
        float applied = Math.Min(target.Health, amount);
        target.Health = Math.Max(0, target.Health - applied);
        if (damageHistory.Count >= 256) damageHistory.RemoveAt(0);
        damageHistory.Add(new(Tick, target.Id, sourceAgentId, kind, applied));
        if (target.Health <= 0)
            target.LethalCause = kind switch
            {
                DamageKind.Fire => DeathCause.Fire,
                DamageKind.Climate => DeathCause.Climate,
                DamageKind.Predator => DeathCause.Predator,
                _ => DeathCause.Other
            };
        return applied;
    }

    private void RemoveDeadAgents()
    {
        HashSet<int>? dead = null;
        Agents.RemoveAll(agent =>
        {
            if (agent.Health > 0 && agent.Energy > 0 && agent.Age < MaximumAge) return false;
            (dead ??= new HashSet<int>()).Add(agent.Id);
            var m = Mortality;
            // Age has priority, then recorded lethal health damage, then depleted food energy.
            var cause = agent.Health <= 0 ? agent.LethalCause : DeathCause.EnergyDepletion;
            m = agent.Age >= MaximumAge ? m with { OldAge = m.OldAge + 1 }
                : cause switch
                {
                    DeathCause.Fire => m with { Fire = m.Fire + 1 },
                    DeathCause.Predator => m with { Predators = m.Predators + 1 },
                    DeathCause.Climate => m with { Climate = m.Climate + 1 },
                    DeathCause.EnergyDepletion => m with { EnergyDepletion = m.EnergyDepletion + 1 },
                    _ => m with { Other = m.Other + 1 }
                };
            Mortality = m with { LifetimeDeaths = m.LifetimeDeaths + 1 };
            return true;
        });
        if (dead is null) return;
        foreach (var tribe in tribes) tribe.MemberIds.RemoveAll(dead.Contains);
        var extinct = tribes.Where(t => t.MemberIds.Count == 0).Select(t => t.Id).ToHashSet();
        tribes.RemoveAll(t => extinct.Contains(t.Id));
        structures.RemoveAll(s => extinct.Contains(s.TribeId));
        relations.RemoveAll(r => extinct.Contains(r.FirstTribeId) || extinct.Contains(r.SecondTribeId));
    }
}

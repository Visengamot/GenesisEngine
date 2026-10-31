namespace Genesis.Engine.Core;

/// <summary>Observable stages of a tribe's learned and funded inventions.</summary>
public enum CivilizationPhase { Early, Settled, Skilled, Advanced }

/// <summary>Specializations in food, crafted tools, and defense.</summary>
public enum CivilizationBranch { Food, Craft, Defense }

/// <summary>Progress of one invention, including prerequisites that still lock it.</summary>
public sealed record CivilizationResearch(CivilizationMilestone Milestone, CivilizationBranch Branch,
    CivilizationMilestone? Prerequisite, bool Unlocked, bool Available, float Progress);

/// <summary>Permanent inventions learned through harvesting experience and paid investment.</summary>
public enum CivilizationMilestone { Farming, Weapons, Toolmaking, Medicine, Fireproofing, Complete }

internal static class CivilizationProgression
{
    // Explicit bounded counters permit stable, finite save/load and avoid unbounded research growth.
    internal const float MaximumKnowledge = 10_000;

    // Research is cumulative practice with real harvests; inventions additionally consume stored
    // material. Costs and knowledge are intentionally independent: traded goods are not practice.
    internal static (float Wood, float Stone, float Food, float WoodExperience, float StoneExperience, float FoodExperience) Cost(CivilizationMilestone milestone) => milestone switch
    {
        CivilizationMilestone.Farming => (.5f, 0, .1f, .35f, 0, .25f),
        CivilizationMilestone.Weapons => (.3f, .5f, 0, .6f, .4f, 0),
        CivilizationMilestone.Toolmaking => (.4f, .6f, 0, 1.4f, 1f, 0),
        CivilizationMilestone.Medicine => (.3f, 0, .6f, .8f, 0, 1f),
        CivilizationMilestone.Fireproofing => (.2f, .8f, 0, 2.2f, 1.8f, 0),
        _ => (0, 0, 0, 0, 0, 0)
    };

    internal static IReadOnlyList<CivilizationResearch> Tree(Tribe t)
    {
        CivilizationMilestone[] order = [CivilizationMilestone.Farming, CivilizationMilestone.Weapons,
            CivilizationMilestone.Toolmaking, CivilizationMilestone.Medicine, CivilizationMilestone.Fireproofing];
        return Array.AsReadOnly(order.Select(m => new CivilizationResearch(m, Branch(m), Prerequisite(m),
            Unlocked(t, m), Available(t).Contains(m), Progress(t, m))).ToArray());
    }

    private static CivilizationBranch Branch(CivilizationMilestone milestone) => milestone switch
    {
        CivilizationMilestone.Farming or CivilizationMilestone.Medicine => CivilizationBranch.Food,
        CivilizationMilestone.Weapons or CivilizationMilestone.Fireproofing => CivilizationBranch.Defense,
        _ => CivilizationBranch.Craft
    };

    private static CivilizationMilestone? Prerequisite(CivilizationMilestone milestone) => milestone switch
    {
        CivilizationMilestone.Toolmaking => CivilizationMilestone.Weapons,
        CivilizationMilestone.Medicine => CivilizationMilestone.Farming,
        CivilizationMilestone.Fireproofing => CivilizationMilestone.Toolmaking,
        _ => null
    };

    private static bool Unlocked(Tribe t, CivilizationMilestone milestone) => milestone switch
    {
        CivilizationMilestone.Farming => t.HasFarming,
        CivilizationMilestone.Weapons => t.HasWeapons,
        CivilizationMilestone.Toolmaking => t.HasToolmaking,
        CivilizationMilestone.Medicine => t.HasMedicine,
        CivilizationMilestone.Fireproofing => t.HasFireproofing,
        _ => false
    };

    internal static IEnumerable<CivilizationMilestone> Available(Tribe t)
    {
        if (!t.HasFarming) yield return CivilizationMilestone.Farming;
        if (!t.HasWeapons) yield return CivilizationMilestone.Weapons;
        if (t.HasWeapons && !t.HasToolmaking) yield return CivilizationMilestone.Toolmaking;
        if (t.HasFarming && !t.HasMedicine) yield return CivilizationMilestone.Medicine;
        if (t.HasToolmaking && !t.HasFireproofing) yield return CivilizationMilestone.Fireproofing;
    }

    internal static CivilizationMilestone NextMilestone(Tribe t)
    {
        // Most actionable opportunity first; stable enum order breaks ties deterministically.
        var candidates = Available(t).ToArray();
        return candidates.Length == 0 ? CivilizationMilestone.Complete : candidates.MaxBy(m => Progress(t, m));
    }

    internal static float Progress(Tribe t, CivilizationMilestone milestone)
    {
        if (milestone == CivilizationMilestone.Complete) return 1;
        var c = Cost(milestone);
        if (!Available(t).Contains(milestone)) return 0;
        float fraction = 1;
        if (c.Wood > 0) fraction = Math.Min(fraction, t.Wood / c.Wood);
        if (c.Stone > 0) fraction = Math.Min(fraction, t.Stone / c.Stone);
        if (c.Food > 0) fraction = Math.Min(fraction, t.Food / c.Food);
        if (c.WoodExperience > 0) fraction = Math.Min(fraction, t.WoodKnowledge / c.WoodExperience);
        if (c.StoneExperience > 0) fraction = Math.Min(fraction, t.StoneKnowledge / c.StoneExperience);
        if (c.FoodExperience > 0) fraction = Math.Min(fraction, t.FoodKnowledge / c.FoodExperience);
        return Math.Clamp(fraction, 0, 1);
    }

    internal static string Unlock(Tribe t, CivilizationMilestone milestone)
    {
        var c = Cost(milestone);
        t.Wood -= c.Wood; t.Stone -= c.Stone; t.Food -= c.Food;
        switch (milestone)
        {
            case CivilizationMilestone.Farming: t.HasFarming = true; return "земледелие";
            case CivilizationMilestone.Weapons: t.HasWeapons = true; return "оружие";
            case CivilizationMilestone.Toolmaking: t.HasToolmaking = true; return "изготовление орудий";
            case CivilizationMilestone.Medicine: t.HasMedicine = true; return "лечение";
            case CivilizationMilestone.Fireproofing: t.HasFireproofing = true; return "противопожарные просеки";
            default: throw new ArgumentOutOfRangeException(nameof(milestone));
        }
    }
}

public sealed partial class Simulation
{
    private void AdvanceCivilization(Tribe tribe)
    {
        // A single paid discovery per decision: no instant cascade when a rich old settlement loads.
        var milestone = tribe.NextMilestone;
        if (milestone == CivilizationMilestone.Complete || tribe.MilestoneProgress < 1) return;
        string name = CivilizationProgression.Unlock(tribe, milestone);
        RecordEvent($"Племя {tribe.Id} освоило {name}.");
    }
}

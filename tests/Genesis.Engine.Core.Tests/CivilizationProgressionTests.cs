using System.Text.Json;
using Genesis.Engine.Core;
using Xunit;

namespace Genesis.Engine.Core.Tests;

public class CivilizationProgressionTests
{
    private static WorldSave Settlement(bool resources)
    {
        var sim = new Simulation(24, 16); sim.Agents.Clear();
        for (int i = 0; i < 6; i++)
            sim.Agents.Add(new Agent(100 + i, new Vec2(7 + i % 3, 7 + i / 3),
                new Genome(.1f, .25f, 2, .1f, .1f, 1, 0)));
        var data = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
        Array.Fill(data.Terrain, .1f);
        Array.Fill(data.Food, 0);
        Array.Fill(data.Environment, new EnvironmentCell(0, 22, 0, 0, 0, 0));
        data.Tribes = [new TribeSave { Id = 0, Center = new(8, 7), Members = [100, 101, 102, 103, 104, 105] }];
        if (resources)
        {
            for (int y = 7; y <= 8; y++)
                for (int x = 7; x <= 9; x++)
                {
                    int cell = y * data.Width + x;
                    data.Food[cell] = 1;
                    data.Environment[cell] = new EnvironmentCell(1, 22, 1, 1, 1, 0);
                }
        }
        return data;
    }

    private static Simulation Load(WorldSave data) => Simulation.LoadJson(JsonSerializer.Serialize(data));

    private static void StepAtSettlement(Simulation sim)
    {
        for (int i = 0; i < 6; i++)
        {
            var a = sim.Agents.Single(a => a.Id == 100 + i);
            a.Position = new(7 + i % 3, 7 + i / 3);
            a.Energy = 2;
        }
        sim.Step(1);
    }

    [Fact]
    public void AgeAndStockAloneDoNotDiscoverAnythingWithoutActualHarvest()
    {
        var data = Settlement(false);
        data.Tribes[0].Wood = 8; data.Tribes[0].Stone = 8; data.Tribes[0].Food = 8;
        data.Tribes[0].Age = 500;
        var sim = Load(data);
        for (int i = 0; i < 40; i++) StepAtSettlement(sim);
        var tribe = Assert.Single(sim.Tribes);
        Assert.Equal(CivilizationPhase.Early, tribe.Phase);
        Assert.False(tribe.HasFarming || tribe.HasWeapons || tribe.HasToolmaking || tribe.HasMedicine || tribe.HasFireproofing);
        // World ecology can regenerate tiny harvests even from initially empty cells;
        // that real gathering remains below the threshold for a discovery.
        Assert.InRange(tribe.WoodKnowledge, 0, .35f);
        Assert.Equal(0, tribe.StoneKnowledge);
        Assert.True(tribe.FoodKnowledge > 0); // Regenerated food is genuine experience.
        Assert.InRange(tribe.MilestoneProgress, 0, .999f);
    }

    [Fact]
    public void RealHarvestAdvancesThroughMultipleStagesWithoutInstantUnlock()
    {
        var sim = Load(Settlement(true));
        int earliestSettled = -1, earliestSkilled = -1, earliestAdvanced = -1;
        int previousUnlocked = 0;
        for (int i = 0; i < 180; i++)
        {
            StepAtSettlement(sim);
            var tribe = Assert.Single(sim.Tribes);
            int unlocked = new[] { tribe.HasFarming, tribe.HasWeapons, tribe.HasToolmaking, tribe.HasMedicine, tribe.HasFireproofing }.Count(x => x);
            Assert.InRange(unlocked - previousUnlocked, 0, 1);
            previousUnlocked = unlocked;
            Assert.InRange(tribe.MilestoneProgress, 0, 1);
            if (tribe.Phase == CivilizationPhase.Settled && earliestSettled < 0) earliestSettled = i;
            if (tribe.Phase == CivilizationPhase.Skilled && earliestSkilled < 0) earliestSkilled = i;
            if (tribe.Phase == CivilizationPhase.Advanced) { earliestAdvanced = i; break; }
        }
        Assert.True(earliestSettled >= 0, "Harvest should unlock the first settlement inventions.");
        Assert.True(earliestSkilled > earliestSettled, "Training and prerequisites must require further decisions.");
        Assert.True(earliestAdvanced > earliestSkilled, "Advanced society must follow substantial resource activity.");
        Assert.True(earliestAdvanced >= 30, "A tribe must not unlock the whole tree instantly.");
        Assert.Equal(CivilizationMilestone.Complete, sim.Tribes[0].NextMilestone);
        Assert.Equal(1, sim.Tribes[0].MilestoneProgress);
    }

    [Fact]
    public void EachTribeHasIndependentKnowledgeAndCannotResearchByTradeOrAge()
    {
        var data = Settlement(true);
        data.Agents[3].Position = new(19, 7);
        data.Agents[4].Position = new(20, 7);
        data.Agents[5].Position = new(19, 8);
        data.Tribes[0].Members = [100, 101, 102];
        data.Tribes = [data.Tribes[0], new TribeSave { Id = 1, Center = new(19, 7), Members = [103, 104, 105], Age = 500, Wood = 10, Stone = 10, Food = 10 }];
        var sim = Load(data);
        for (int i = 0; i < 55; i++)
        {
            foreach (var a in sim.Agents)
            {
                if (a.Id >= 103) a.Position = new(a.Id == 104 ? 20 : 19, a.Id == 105 ? 8 : 7);
                else a.Position = new(7 + (a.Id - 100) % 3, 7);
                a.Energy = 2;
            }
            sim.Step(1);
        }
        Assert.Equal(2, sim.Tribes.Count);
        var active = sim.Tribes.Single(t => t.Id == 0);
        var inactive = sim.Tribes.Single(t => t.Id == 1);
        Assert.True(active.WoodKnowledge > 0);
        Assert.True(active.StoneKnowledge > 0);
        Assert.InRange(inactive.WoodKnowledge, 0, .35f); // Naturally regrown biomass is still real harvest.
        Assert.Equal(0, inactive.StoneKnowledge);
        Assert.InRange(inactive.FoodKnowledge, 0, .25f);
        Assert.Equal(CivilizationPhase.Early, inactive.Phase);
    }

    [Fact]
    public void LocalResourcesShapeDistinctResearchBranches()
    {
        var data = Settlement(false);
        data.Tribes[0].Members = [100, 101, 102];
        data.Tribes = [data.Tribes[0], new TribeSave { Id = 1, Center = new(19, 7), Members = [103, 104, 105] }];
        for (int i = 3; i < 6; i++) data.Agents[i].Position = new(18 + (i - 3), 7);
        // Food land has wood but no stone; craft land has stone but sterile dry soil.
        for (int y = 5; y <= 9; y++)
            for (int x = 4; x <= 12; x++)
            {
                int first = y * data.Width + x;
                data.Food[first] = 1;
                data.Environment[first] = new(1, 22, 1, 1, 0, 0);
            }
        for (int y = 5; y <= 9; y++)
            for (int x = 15; x <= 23; x++)
                data.Environment[y * data.Width + x] = new(0, 60, 0, 1, 1, 0);
        var sim = Load(data);
        for (int tick = 0; tick < 25; tick++)
        {
            for (int i = 0; i < 6; i++)
            {
                var a = sim.Agents.Single(a => a.Id == 100 + i);
                a.Position = new(i < 3 ? 7 + i : 18 + i - 3, 7);
                a.Energy = 2;
            }
            sim.Step(1);
        }
        var growers = sim.Tribes.Single(t => t.Id == 0);
        var makers = sim.Tribes.Single(t => t.Id == 1);
        Assert.True(growers.HasFarming);
        Assert.False(growers.HasWeapons);
        Assert.False(makers.HasFarming);
        Assert.True(makers.HasWeapons);
        Assert.Contains(growers.Research, r => r.Branch == CivilizationBranch.Food && r.Milestone == CivilizationMilestone.Medicine && r.Prerequisite == CivilizationMilestone.Farming);
        Assert.Contains(makers.Research, r => r.Branch == CivilizationBranch.Craft && r.Milestone == CivilizationMilestone.Toolmaking && r.Prerequisite == CivilizationMilestone.Weapons);
        Assert.Equal(5, growers.Research.Count);
    }

    [Fact]
    public void ExtinctTribeReleasesStructuresBeforeSurvivorBuilds()
    {
        var data = Settlement(false);
        data.Agents[5].Traits[5] = 0; // A living but non-social former member.
        data.Tribes[0].Members = [100, 101, 102, 103, 104];
        data.Tribes[0].HasFarming = true;
        data.Tribes[0].Wood = 2; data.Tribes[0].Stone = 2;
        data.Tribes = [data.Tribes[0], new TribeSave { Id = 1, Center = new(17, 7), Members = [105] }];
        data.Structures = [new Structure(StructureKind.Hut, new(7, 7), 0),
            .. Enumerable.Range(0, 127).Select(_ => new Structure(StructureKind.Road, new(17, 7), 1))];
        var sim = Load(data);
        sim.Step(1);
        Assert.Single(sim.Tribes);
        Assert.All(sim.Structures, s => Assert.Equal(0, s.TribeId));
        Assert.Contains(sim.Structures, s => s.Kind == StructureKind.Farm);
    }
}

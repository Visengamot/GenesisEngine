using System.Text.Json;
using Genesis.Engine.Core;
using Xunit;

namespace Genesis.Engine.Core.Tests;

public class CivilizationSystemsTests
{
    private static WorldSave Fixture(bool road = true)
    {
        var sim = new Simulation(24, 10); sim.Agents.Clear();
        for (int i = 0; i < 6; i++)
            sim.Agents.Add(new(100 + i, new(i < 3 ? 3 : 17, 5), new Genome(.1f, .25f, 2, .1f, .1f, 1, 0)));
        var d = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
        Array.Fill(d.Terrain, .1f);
        Array.Fill(d.Food, 0);
        Array.Fill(d.Environment, new EnvironmentCell(0, 22, 0, 0, 0, 0));
        d.Tribes = [new() { Id = 0, Center = new(3, 5), Members = [100,101,102] },
            new() { Id = 1, Center = new(17, 5), Members = [103,104,105] }];
        var structures = new List<Structure> { new(StructureKind.Hut, new(3, 1), 0), new(StructureKind.Hut, new(17, 1), 1) };
        if (road) for (int x = 4; x < 17; x++) structures.Add(new(StructureKind.Road, new(x, 1), 0));
        d.Structures = structures.ToArray();
        return d;
    }
    private static Simulation Load(WorldSave d) => Simulation.LoadJson(JsonSerializer.Serialize(d));

    [Theory]
    [InlineData("food")]
    [InlineData("wood")]
    [InlineData("stone")]
    public void ConnectedAlliesTransferConservedBoundedResources(string resource)
    {
        var d = Fixture();
        if (resource == "food") d.Tribes[0].Food = 2;
        if (resource == "wood") d.Tribes[0].Wood = 2;
        if (resource == "stone") d.Tribes[0].Stone = 2;
        var sim = Load(d); sim.Step(1);
        float Amount(Tribe t) => resource == "food" ? t.Food : resource == "wood" ? t.Wood : t.Stone;
        Assert.InRange(Amount(sim.Tribes[1]), .099f, .101f);
        Assert.InRange(Amount(sim.Tribes[0]), 1.899f, 1.901f);
        Assert.InRange(sim.Tribes.Sum(Amount), 1.999f, 2.001f);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("fire")]
    [InlineData("water")]
    [InlineData("war")]
    public void BrokenUnsafeOrHostileRoutesDoNotTransfer(string obstruction)
    {
        var d = Fixture(); d.Tribes[0].Food = 2;
        if (obstruction == "gap") d.Structures = d.Structures.Where(s => s.Position.X != 10).ToArray();
        if (obstruction == "fire") d.Environment[24 + 10] = new(0, 22, 1, 1, 0, 1);
        if (obstruction == "water") d.Terrain[24 + 10] = -1;
        if (obstruction == "war") foreach (var a in d.Agents) a.Traits[4] = 1;
        var sim = Load(d); sim.Step(1);
        Assert.Equal(0, sim.Tribes[1].Food);
        Assert.Equal(2, sim.Tribes[0].Food);
    }

    [Fact]
    public void TradeRoadConstructionPaysForOnlyOneSegmentPerDecision()
    {
        var d = Fixture(false);
        d.Tribes[0].HasWeapons = true; d.Tribes[0].HasFarming = true;
        d.Tribes[0].Wood = .2f; d.Tribes[0].Stone = .2f; d.Tribes[0].Food = 2;
        var sim = Load(d); sim.Step(1);
        Assert.Single(sim.Structures.Where(s => s.Kind == StructureKind.Road));
        Assert.Equal(.18f, sim.Tribes[0].Wood, 4);
        Assert.Equal(.16f, sim.Tribes[0].Stone, 4);
        Assert.Equal(0, sim.Tribes[1].Food); // No delivery before the route is complete.
    }

    [Fact]
    public void TradeNeverOvershootsCapacityOrEqualization()
    {
        var d = Fixture(); d.Tribes[0].Food = 100; d.Tribes[1].Food = 99.95f;
        var sim = Load(d); sim.Step(1);
        Assert.Equal(sim.Tribes[0].Food, sim.Tribes[1].Food, 4);
        Assert.All(sim.Tribes, t => Assert.InRange(t.Food, 0, 100));
        Assert.Equal(199.95f, sim.Tribes.Sum(t => t.Food), 3);
    }

    [Fact]
    public void DiplomacyJournalRecordsTransitionsRatherThanEveryDecision()
    {
        var sim = Load(Fixture()); sim.Step(1); sim.Step(1);
        Assert.Single(sim.Events.Where(e => e.Contains("союз")));
        foreach (var a in sim.Agents) a.Genome = new(.1f, .25f, 2, .1f, 1, 1, 0);
        sim.Step(1); sim.Step(1);
        Assert.Single(sim.Events.Where(e => e.Contains("война")));
        Assert.Equal(TribalRelation.War, Assert.Single(sim.Relations).Relation);
    }

    [Fact]
    public void RepeatedDiplomacyTransitionsKeepJournalBounded()
    {
        var sim = Load(Fixture());
        for (int tick = 0; tick < 75; tick++)
        {
            foreach (var a in sim.Agents)
            {
                a.Position = new(a.Id < 103 ? 3 : 17, 5); a.Energy = 1;
                a.Genome = new(.1f, .25f, 2, .1f, tick % 2, 1, 0);
            }
            sim.Step(1);
        }
        Assert.Equal(64, sim.Events.Count);
        Assert.All(sim.Events, e => Assert.StartsWith("Тик ", e));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HuntersRequireContactAndPayRecurringMaterialAndStaminaCosts(bool supplied, bool contact)
    {
        var d = Fixture(false); d.Tribes[0].HasWeapons = true;
        d.Tribes[0].Wood = supplied ? .2f : 0; d.Tribes[0].Stone = supplied ? .2f : 0;
        d.Predators = [new() { Position = new(contact ? 3 : 9, 5), Energy = 1 }];
        var armed = Load(d);
        d.Tribes[0].HasWeapons = false;
        var control = Load(d);
        armed.Step(1); control.Step(1);
        bool attacks = supplied && contact;
        Assert.Equal(attacks ? .3f : 0, control.Predators[0].Energy - armed.Predators[0].Energy, 4);
        Assert.Equal(attacks ? .01f : 0, control.Tribes[0].Wood - armed.Tribes[0].Wood, 4);
        Assert.Equal(attacks ? .02f : 0, control.Tribes[0].Stone - armed.Tribes[0].Stone, 4);
        Assert.Equal(attacks ? .05f : 0, control.Agents.Sum(a => a.Energy) - armed.Agents.Sum(a => a.Energy), 4);
    }
}

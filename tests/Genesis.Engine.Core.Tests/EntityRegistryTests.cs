using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class EntityRegistryTests
{
    [Fact]
    public void RecyclesSlotsWithNewGenerationsAndRejectsForeignHandles()
    {
        var registry = new EntityRegistry();
        var old = registry.Create();
        var survivor = registry.Create();
        Assert.Equal(2, registry.Count);
        Assert.True(registry.Destroy(old));
        Assert.False(registry.IsAlive(old));
        Assert.False(registry.Destroy(old));
        var replacement = registry.Create();
        Assert.Equal(old.Index, replacement.Index);
        Assert.NotEqual(old.Generation, replacement.Generation);
        Assert.True(registry.IsAlive(survivor));
        Assert.False(registry.IsAlive(default));
        var foreign = new EntityRegistry();
        Assert.False(foreign.IsAlive(replacement));
        Assert.False(foreign.Destroy(replacement));
        Assert.Throws<InvalidOperationException>(() => foreign.Add(replacement, new PositionComponent(default)));
        Assert.Throws<InvalidOperationException>(() => registry.Add(old, new PositionComponent(default)));
    }

    [Fact]
    public void ComponentTypesAndEntitiesRemainIsolatedOnReuse()
    {
        var registry = new EntityRegistry();
        var first = registry.Create();
        var second = registry.Create();
        var genome = new Genome(1, 1, 2, 1, 0, 0, 0);
        registry.Add(first, new PositionComponent(new Vec2(1, 2)));
        registry.Add(first, new VitalityComponent(10, 20));
        registry.Add(first, new GenomeComponent(genome));
        registry.Add(second, new PositionComponent(new Vec2(3, 4)));
        Assert.False(registry.TryGet<VitalityComponent>(second, out _));
        Assert.Equal(genome, registry.Get<GenomeComponent>(first).Value);
        Assert.Throws<InvalidOperationException>(() => registry.Add(first, new PositionComponent(default)));
        registry.Get<PositionComponent>(first) = new(new Vec2(7, 8));
        Assert.Equal(new Vec2(7, 8), registry.Get<PositionComponent>(first).Value);
        Assert.Equal(new Vec2(3, 4), registry.Get<PositionComponent>(second).Value);
        Assert.True(registry.Destroy(first));
        var recycled = registry.Create();
        Assert.False(registry.TryGet<PositionComponent>(recycled, out _));
        Assert.False(registry.TryGet<GenomeComponent>(recycled, out _));
        Assert.False(registry.TryGet<VitalityComponent>(first, out _));
        Assert.False(registry.TryGet<PositionComponent>(first, out var absent));
        Assert.Equal(default, absent);
        Assert.Throws<InvalidOperationException>(() => registry.Get<PositionComponent>(first));
        Assert.Equal(new Vec2(3, 4), registry.Get<PositionComponent>(second).Value);
    }

    [Fact]
    public void RemoveSwapsDenseComponentsAndPreservesOtherValues()
    {
        var registry = new EntityRegistry();
        var entities = Enumerable.Range(0, 12).Select(_ => registry.Create()).ToArray();
        for (int i = 0; i < entities.Length; i++)
            registry.Add(entities[i], new VitalityComponent(i, 100 + i));
        Assert.True(registry.Remove<VitalityComponent>(entities[3]));
        Assert.False(registry.Remove<VitalityComponent>(entities[3]));
        Assert.False(registry.TryGet<VitalityComponent>(entities[3], out _));
        for (int i = 0; i < entities.Length; i++)
            if (i != 3) Assert.Equal(i, registry.Get<VitalityComponent>(entities[i]).Energy);
        registry.Add(entities[3], new VitalityComponent(77, 88));
        Assert.Equal(77, registry.Get<VitalityComponent>(entities[3]).Energy);
        Assert.False(registry.Remove<PositionComponent>(entities[3]));
    }

    [Fact]
    public void BrainIsOptionalAndReferencePreserved()
    {
        var registry = new EntityRegistry();
        var entity = registry.Create();
        var network = new NeuralNetwork(2, 1);
        registry.Add(entity, new BrainComponent(network));
        Assert.Same(network, registry.Get<BrainComponent>(entity).Network);
        Assert.True(registry.Remove<BrainComponent>(entity));
        Assert.False(registry.TryGet<BrainComponent>(entity, out _));
    }

    [Fact]
    public void QueryIsDeterministicInLiveDenseEntityOrderAndDoesNotSnapshot()
    {
        var registry = new EntityRegistry();
        var a = registry.Create(); var b = registry.Create(); var c = registry.Create(); var d = registry.Create();
        foreach (var (entity, value) in new[] { (a, 1f), (b, 2f), (c, 3f), (d, 4f) })
            registry.Add(entity, new PositionComponent(new Vec2(value, 0)));
        registry.Add(a, new VitalityComponent(1, 1));
        registry.Add(c, new VitalityComponent(3, 3));
        registry.Add(d, new VitalityComponent(4, 4));
        Assert.Equal(new[] { a, c, d }, registry.Query<PositionComponent, VitalityComponent>().SelectForTest());
        Assert.True(registry.Destroy(b)); // d swaps into b's entity slot
        Assert.Equal(new[] { a, d, c }, registry.Query<PositionComponent, VitalityComponent>().SelectForTest());
        registry.Get<PositionComponent>(c) = new(new Vec2(99, 0));
        foreach (var item in registry.Query<PositionComponent, VitalityComponent>())
            if (item.Entity == c) Assert.Equal(99, item.First.Value.X);
        Assert.Empty(registry.Query<PositionComponent, GenomeComponent>().SelectForTest());
        Assert.Throws<ArgumentException>(() => registry.Query<PositionComponent, PositionComponent>());
    }

    [Fact]
    public void QueryRejectsStructuralMutationButAllowsComponentValueUpdates()
    {
        var registry = new EntityRegistry();
        var entity = registry.Create();
        registry.Add(entity, new PositionComponent(default));
        registry.Add(entity, new VitalityComponent(1, 1));
        var iterator = registry.Query<PositionComponent, VitalityComponent>().GetEnumerator();
        Assert.True(iterator.MoveNext());
        registry.Get<PositionComponent>(entity) = new(new Vec2(5, 6));
        Assert.False(iterator.MoveNext());
        iterator = registry.Query<PositionComponent, VitalityComponent>().GetEnumerator();
        registry.Remove<VitalityComponent>(entity);
        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
        iterator = registry.Query<PositionComponent, VitalityComponent>().GetEnumerator();
        registry.Create();
        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }

    [Fact]
    public void RepeatedMixedMutationsPreserveSparseLookupAndGenerationSafety()
    {
        var registry = new EntityRegistry();
        var handles = new List<EntityHandle>();
        for (int round = 0; round < 100; round++)
        {
            var entity = registry.Create();
            handles.Add(entity);
            registry.Add(entity, new PositionComponent(new Vec2(round, -round)));
            if (round % 3 == 0) registry.Add(entity, new VitalityComponent(round, 1));
            if (round % 2 == 0) Assert.True(registry.Destroy(entity));
        }
        foreach (var (handle, round) in handles.Select((handle, index) => (handle, index)))
        {
            Assert.Equal(round % 2 != 0, registry.IsAlive(handle));
            if (round % 2 != 0) Assert.Equal(round, registry.Get<PositionComponent>(handle).Value.X);
            else Assert.False(registry.TryGet<PositionComponent>(handle, out _));
        }
        Assert.Equal(50, registry.Count);
        int matching = 0;
        foreach (var item in registry.Query<PositionComponent, VitalityComponent>())
        {
            Assert.Equal(item.First.Value.X, item.Second.Energy);
            matching++;
        }
        Assert.Equal(17, matching);
    }
}

internal static class EntityRegistryTestExtensions
{
    public static EntityHandle[] SelectForTest<T1, T2>(this EntityRegistry.QueryEnumerable<T1, T2> query)
        where T1 : struct where T2 : struct
    {
        var result = new List<EntityHandle>();
        foreach (var item in query) result.Add(item.Entity);
        return result.ToArray();
    }
}

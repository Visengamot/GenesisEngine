using System.Threading;

namespace Genesis.Engine.Core;

/// <summary>A registry-local entity handle. Slot reuse always changes the generation.</summary>
public readonly record struct EntityHandle(int Index, uint Generation, long RegistryId);

public readonly record struct PositionComponent(Vec2 Value);
public readonly record struct VitalityComponent(float Energy, float Health);
public readonly record struct GenomeComponent(Genome Value);
/// <summary>Optional reference to a brain; the network is not copied when this component is read.</summary>
public readonly record struct BrainComponent(NeuralNetwork Network);

/// <summary>
/// Independent, archetype-lite component storage; it is not connected to Simulation or AgentStore.
/// Confine a registry to one thread. Queries traverse live entities in dense order, allocate no
/// snapshots, and fail fast on structural edits (create/destroy/add/remove). Component values may
/// be edited by ref during traversal; do not retain refs across structural edits.
/// </summary>
public sealed class EntityRegistry
{
    private static long nextRegistryId;
    private readonly long registryId = Interlocked.Increment(ref nextRegistryId);
    private readonly List<Slot> slots = new();
    private readonly List<int> denseEntities = new();
    private readonly Stack<int> free = new();
    private readonly Dictionary<Type, IComponentPool> pools = new();
    private int version;

    private struct Slot
    {
        public uint Generation;
        public int DenseIndex;
        public bool Alive;
    }

    public int Count => denseEntities.Count;

    public EntityHandle Create()
    {
        int index;
        if (free.Count != 0) index = free.Pop();
        else
        {
            index = slots.Count;
            slots.Add(new Slot { Generation = 1 });
        }
        var slot = slots[index];
        slot.Alive = true;
        slot.DenseIndex = denseEntities.Count;
        slots[index] = slot;
        denseEntities.Add(index);
        version++;
        return new EntityHandle(index, slot.Generation, registryId);
    }

    public bool IsAlive(EntityHandle entity) => entity.RegistryId == registryId &&
        (uint)entity.Index < (uint)slots.Count && slots[entity.Index].Alive &&
        slots[entity.Index].Generation == entity.Generation;

    /// <returns>False for a stale, foreign, or already destroyed handle.</returns>
    public bool Destroy(EntityHandle entity)
    {
        if (!IsAlive(entity)) return false;
        foreach (var pool in pools.Values) pool.Remove(entity.Index);
        var slot = slots[entity.Index];
        int lastIndex = denseEntities[^1];
        denseEntities[slot.DenseIndex] = lastIndex;
        denseEntities.RemoveAt(denseEntities.Count - 1);
        if (lastIndex != entity.Index)
        {
            var moved = slots[lastIndex];
            moved.DenseIndex = slot.DenseIndex;
            slots[lastIndex] = moved;
        }
        slot.Alive = false;
        // Never wrap the generation: retire exhausted slots rather than resurrecting an old handle.
        if (slot.Generation != uint.MaxValue)
        {
            slot.Generation++;
            free.Push(entity.Index);
        }
        slots[entity.Index] = slot;
        version++;
        return true;
    }

    /// <summary>Add exactly once per entity and component type. Use Get&lt;T&gt; by ref to update.</summary>
    public void Add<T>(EntityHandle entity, T component) where T : struct
    {
        RequireAlive(entity);
        var pool = GetOrCreatePool<T>();
        pool.Add(entity.Index, component);
        version++;
    }

    /// <summary>Returns a direct ref into dense storage, invalidated by structural edits.</summary>
    public ref T Get<T>(EntityHandle entity) where T : struct
    {
        RequireAlive(entity);
        if (FindPool<T>() is not { } pool || !pool.TryIndex(entity.Index, out int index))
            throw new InvalidOperationException($"Entity does not have {typeof(T).Name}.");
        return ref pool.At(index);
    }

    public bool TryGet<T>(EntityHandle entity, out T component) where T : struct
    {
        if (IsAlive(entity) && FindPool<T>() is { } pool && pool.TryIndex(entity.Index, out int index))
        {
            component = pool.At(index);
            return true;
        }
        component = default;
        return false;
    }

    public bool Remove<T>(EntityHandle entity) where T : struct
    {
        if (!IsAlive(entity) || FindPool<T>() is not { } pool || !pool.Remove(entity.Index)) return false;
        version++;
        return true;
    }

    /// <summary>Zero-allocation foreach over entities with both components; dense entity order.</summary>
    public QueryEnumerable<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct
    {
        if (typeof(T1) == typeof(T2)) throw new ArgumentException("Query component types must be distinct.");
        return new QueryEnumerable<T1, T2>(this);
    }

    private void RequireAlive(EntityHandle entity)
    {
        if (!IsAlive(entity)) throw new InvalidOperationException("Entity handle is stale or belongs to another registry.");
    }

    private ComponentPool<T>? FindPool<T>() where T : struct =>
        pools.TryGetValue(typeof(T), out var pool) ? (ComponentPool<T>)pool : null;

    private ComponentPool<T> GetOrCreatePool<T>() where T : struct
    {
        if (FindPool<T>() is { } pool) return pool;
        pool = new ComponentPool<T>();
        pools.Add(typeof(T), pool);
        return pool;
    }

    public readonly struct QueryEnumerable<T1, T2> where T1 : struct where T2 : struct
    {
        private readonly EntityRegistry registry;
        internal QueryEnumerable(EntityRegistry registry) => this.registry = registry;
        public Enumerator GetEnumerator() => new(registry);

        public struct Enumerator
        {
            private readonly EntityRegistry registry;
            private readonly ComponentPool<T1>? first;
            private readonly ComponentPool<T2>? second;
            private readonly int expectedVersion;
            private int cursor;
            public (EntityHandle Entity, T1 First, T2 Second) Current { get; private set; }
            internal Enumerator(EntityRegistry registry)
            {
                this.registry = registry; first = registry.FindPool<T1>(); second = registry.FindPool<T2>();
                expectedVersion = registry.version;
                cursor = 0;
                Current = default;
            }
            public bool MoveNext()
            {
                if (registry.version != expectedVersion)
                    throw new InvalidOperationException("Registry structure changed during query iteration.");
                if (first is null || second is null) return false;
                while (cursor < registry.denseEntities.Count)
                {
                    int index = registry.denseEntities[cursor++];
                    if (!first.TryIndex(index, out int a) || !second.TryIndex(index, out int b)) continue;
                    var slot = registry.slots[index];
                    Current = (new EntityHandle(index, slot.Generation, registry.registryId), first.At(a), second.At(b));
                    return true;
                }
                return false;
            }
        }
    }

    private interface IComponentPool { bool Remove(int entityIndex); }

    private sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private int[] sparse = Array.Empty<int>(); // dense index + 1; zero denotes absence
        private int[] entities = Array.Empty<int>();
        private T[] components = Array.Empty<T>();
        private int count;

        public bool TryIndex(int entityIndex, out int denseIndex)
        {
            denseIndex = (uint)entityIndex < (uint)sparse.Length ? sparse[entityIndex] - 1 : -1;
            return denseIndex >= 0;
        }
        public ref T At(int denseIndex) => ref components[denseIndex];

        public void Add(int entityIndex, T component)
        {
            if (TryIndex(entityIndex, out _)) throw new InvalidOperationException($"Entity already has {typeof(T).Name}.");
            if (entityIndex >= sparse.Length) Array.Resize(ref sparse, Math.Max(entityIndex + 1, Math.Max(4, sparse.Length * 2)));
            if (count == components.Length)
            {
                int capacity = Math.Max(4, count * 2);
                Array.Resize(ref components, capacity);
                Array.Resize(ref entities, capacity);
            }
            sparse[entityIndex] = count + 1;
            entities[count] = entityIndex;
            components[count++] = component;
        }
        public bool Remove(int entityIndex)
        {
            if (!TryIndex(entityIndex, out int target)) return false;
            int last = --count;
            if (target != last)
            {
                components[target] = components[last];
                int movedEntity = entities[last];
                entities[target] = movedEntity;
                sparse[movedEntity] = target + 1;
            }
            components[last] = default; // release references held by removed structs
            entities[last] = default;
            sparse[entityIndex] = 0;
            return true;
        }
    }
}

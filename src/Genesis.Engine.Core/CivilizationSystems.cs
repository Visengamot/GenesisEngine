namespace Genesis.Engine.Core;

public sealed partial class Simulation
{
    // One treatment per decision, only after environmental exposure, spending stored food.
    // The medicine cannot cure starvation or create food; it replaces some exposure damage.
    private void TreatExposure(Tribe tribe, Agent[] members, float elapsed)
    {
        if (!tribe.HasMedicine || tribe.Food < .04f) return;
        foreach (var member in members.OrderBy(a => a.Id))
        {
            var cell = Environment.Get((int)member.Position.X, (int)member.Position.Y);
            if (cell.Fire < .05f && Math.Abs(cell.Temperature - 22) <= 37) continue;
            if (member.Energy <= 0 || member.Energy >= 3) continue;
            float treatment = Math.Min(.03f * Math.Min(elapsed, 1), 3 - member.Energy);
            if (treatment <= 0) continue;
            tribe.Food -= .04f;
            member.Energy += treatment;
            return;
        }
    }

    // Maintain at most one paid tile of a firebreak beside a hut each decision.
    // Clearing fuel does not extinguish existing fire and the removed biomass is not harvested.
    private void MaintainFirebreak(Tribe tribe, float elapsed)
    {
        if (!tribe.HasFireproofing || tribe.Stone < .02f || elapsed <= 0) return;
        var hut = structures.FirstOrDefault(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Hut);
        if (hut is null) return;
        int hx = (int)hut.Position.X, hy = (int)hut.Position.Y;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = hx + dx, y = hy + dy;
                if (x < 0 || x >= Environment.Width || y < 0 || y >= Environment.Height) continue;
                var cell = Environment.Get(x, y);
                if (cell.Fire >= .05f || (cell.Wood <= .01f && cell.Vegetation <= .01f)) continue;
                tribe.Stone -= .02f;
                Environment.Set(x, y, cell with { Wood = Math.Max(0, cell.Wood - .25f), Vegetation = Math.Max(0, cell.Vegetation - .25f) });
                return;
            }
    }

    // Rule-based contact defense: one living hunter attacks once per decision, not a center aura.
    // Technology is permanent; each strike consumes replacement wood/stone and hunter stamina.
    private void HuntPredators(Tribe tribe, Agent[] members, float elapsed)
    {
        if (!tribe.HasWeapons) return;
        float effort = Math.Min(elapsed, 1);
        float woodCost = .01f * effort, stoneCost = (tribe.HasToolmaking ? .01f : .02f) * effort;
        if (tribe.Wood < woodCost || tribe.Stone < stoneCost) return;
        foreach (var hunter in members.OrderBy(a => a.Id))
        {
            if (hunter.Energy <= .05f * effort) continue;
            var prey = predators.Where(p => p.Energy > 0 && DistanceSquared(p.Position, hunter.Position) <= 2.25f)
                .MinBy(p => DistanceSquared(p.Position, hunter.Position));
            if (prey is null) continue;
            tribe.Wood -= woodCost; tribe.Stone -= stoneCost;
            hunter.Energy -= .05f * effort;
            prey.Energy = Math.Max(0, prey.Energy - .3f * effort);
            if (prey.Energy == 0)
            {
                predators.Remove(prey);
                RecordEvent($"Охотник племени {tribe.Id} победил хищника.");
            }
            return;
        }
    }

    private void JournalRelation(Tribe a, Tribe b, TribalRelation relation, TribeRelation[] previous)
    {
        var old = previous.FirstOrDefault(r => (r.FirstTribeId == a.Id && r.SecondTribeId == b.Id) ||
            (r.FirstTribeId == b.Id && r.SecondTribeId == a.Id))?.Relation ?? TribalRelation.Neutral;
        if (old == relation) return;
        string state = relation == TribalRelation.Alliance ? "союз" : relation == TribalRelation.War ? "война" : "нейтралитет";
        RecordEvent($"Племена {a.Id} и {b.Id}: {state}.");
    }

    // Abstract shipments, not simulated caravans: only an unbroken four-neighbor road between huts
    // carries conserved goods. Fire, deep water and cliffs close a route immediately.
    private void TradeAlongRoad(Tribe a, Tribe b, float elapsed)
    {
        var start = structures.FirstOrDefault(s => s.TribeId == a.Id && s.Kind == StructureKind.Hut);
        var end = structures.FirstOrDefault(s => s.TribeId == b.Id && s.Kind == StructureKind.Hut);
        if (start is null || end is null) return;
        bool Safe(Vec2 p) => Terrain.Get((int)p.X, (int)p.Y) >= 0 && Terrain.Get((int)p.X, (int)p.Y) < 1.5f &&
            Environment.Get((int)p.X, (int)p.Y).Fire < .05f;
        var nodes = structures.Where(s => (s.TribeId == a.Id || s.TribeId == b.Id) &&
            (s.Kind == StructureKind.Road || s == start || s == end) && Safe(s.Position))
            .Select(s => s.Position).ToHashSet();
        var visited = new HashSet<Vec2>();
        var queue = new Queue<Vec2>();
        if (nodes.Contains(start.Position)) { queue.Enqueue(start.Position); visited.Add(start.Position); }
        while (queue.TryDequeue(out var point))
        {
            foreach (var delta in new Vec2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
            {
                var next = point + delta;
                if (nodes.Contains(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        if (!visited.Contains(end.Position))
        {
            // At most one paid segment per pair per decision, subject to the global 128-structure cap.
            if (!a.HasFarming || !a.HasWeapons || a.Wood < .02f || a.Stone < .04f || structures.Count >= 128 || structures.Count(s => s.Kind == StructureKind.Road) >= 64) return;
            var path = GridPathfinder.Find(Terrain, start.Position, end.Position,
                (x, y) => !Safe(new(x, y)) || structures.Any(s => s.Position == new Vec2(x, y) &&
                    s.Position != start.Position && s.Position != end.Position &&
                    (s.Kind != StructureKind.Road || (s.TribeId != a.Id && s.TribeId != b.Id))));
            foreach (var point in path.Skip(1).SkipLast(1))
            {
                if (nodes.Contains(point)) continue;
                a.Wood -= .02f; a.Stone -= .04f;
                structures.Add(new(StructureKind.Road, point, a.Id));
                break;
            }
            return;
        }
        float budget = .1f * Math.Min(elapsed, 1);
        (a.Food, b.Food) = Transfer(a.Food, b.Food, ref budget);
        (a.Wood, b.Wood) = Transfer(a.Wood, b.Wood, ref budget);
        (a.Stone, b.Stone) = Transfer(a.Stone, b.Stone, ref budget);
    }

    private static (float, float) Transfer(float first, float second, ref float budget)
    {
        float amount = Math.Min(Math.Abs(first - second) * .5f, budget);
        budget -= amount;
        float signed = first >= second ? amount : -amount;
        return (first - signed, second + signed);
    }
}

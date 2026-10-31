using System.Numerics;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

internal static class WorldRenderer
{
    // Only the presentation is cached; simulation fields continue updating each tick.
    internal const int TerrainRefreshTicks = 6;
    static RenderTexture2D terrainTexture;
    static Simulation? cachedWorld;
    static long cachedTick = -1;
    static Vector2 cachedOrigin;
    static float cachedCell;
    static Rectangle cachedViewport;
    static bool terrainDirty = true;

    public static void InvalidateTerrain() => terrainDirty = true;

    public static void Unload()
    {
        if (terrainTexture.Id != 0) Raylib.UnloadRenderTexture(terrainTexture);
        terrainTexture = default;
        cachedWorld = null;
        terrainDirty = true;
        AgentSpriteAtlas.Unload();
    }

    public static void Draw(Simulation world, Vector2 origin, float cell, int selectedId, Rectangle viewport)
    {
        if (cell <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return;
        int left = Math.Clamp((int)MathF.Floor((viewport.X - origin.X) / cell) - 2, 0, world.Terrain.Width);
        int top = Math.Clamp((int)MathF.Floor((viewport.Y - origin.Y) / cell) - 2, 0, world.Terrain.Height);
        int right = Math.Clamp((int)MathF.Ceiling((viewport.X + viewport.Width - origin.X) / cell) + 2, 0, world.Terrain.Width);
        int bottom = Math.Clamp((int)MathF.Ceiling((viewport.Y + viewport.Height - origin.Y) / cell) + 2, 0, world.Terrain.Height);
        int width = (int)MathF.Ceiling(viewport.Width), height = (int)MathF.Ceiling(viewport.Height);
        bool refresh = terrainDirty || !ReferenceEquals(cachedWorld, world) || world.Tick < cachedTick
            || world.Tick - cachedTick >= TerrainRefreshTicks || cachedOrigin != origin || cachedCell != cell
            || !cachedViewport.Equals(viewport);
        // Render targets have their own pixel coordinates: never inherit the window scissor.
        Raylib.EndScissorMode();
        AgentSpriteAtlas.EnsureLoaded();
        if (terrainTexture.Id == 0 || terrainTexture.Texture.Width != width || terrainTexture.Texture.Height != height)
        {
            if (terrainTexture.Id != 0) Raylib.UnloadRenderTexture(terrainTexture);
            terrainTexture = Raylib.LoadRenderTexture(width, height);
            refresh = true;
        }
        if (refresh)
        {
            Raylib.BeginTextureMode(terrainTexture);
            Raylib.ClearBackground(Color.Blank);
            DrawTerrain(world, origin - new Vector2(viewport.X, viewport.Y), cell, left, top, right, bottom);
            Raylib.EndTextureMode();
            cachedWorld = world; cachedTick = world.Tick; cachedOrigin = origin; cachedCell = cell;
            cachedViewport = viewport; terrainDirty = false;
        }
        Raylib.BeginScissorMode((int)viewport.X, (int)viewport.Y, (int)viewport.Width, (int)viewport.Height);
        Raylib.DrawTextureRec(terrainTexture.Texture, new Rectangle(0, 0, width, -height), new Vector2(viewport.X, viewport.Y), Color.White);
        // Waves and fire are deliberately live, including while terrain is reused.
        float time = world.Tick / 30f;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                float px = origin.X + x * cell, py = origin.Y + y * cell;
                if (world.Terrain.Get(x, y) < -.15f && Hash(x, y) % 5 == 0)
                {
                    float wave = MathF.Sin(time * 1.3f + x * .7f + y) * .12f;
                    Rect(px + cell * .2f, py + cell * (.5f + wave), cell * .55f, Math.Max(1, cell * .08f), new Color(83, 139, 157, 180));
                }
                if (world.Environment.Get(x, y).Fire > .05f)
                {
                    Raylib.DrawCircleV(new(px + cell * .5f, py + cell * .5f), cell * .5f, new Color(222, 78, 30, 210));
                    Raylib.DrawCircleV(new(px + cell * .45f, py + cell * (.4f + MathF.Sin(time * 9 + x) * .1f)), cell * .25f, new Color(255, 207, 71, 255));
                }
            }
        DrawActors(world, origin, cell, selectedId, viewport);
    }

    static void DrawTerrain(Simulation world, Vector2 origin, float cell, int left, int top, int right, int bottom)
    {
        var time = world.Tick / 30f;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                float h = world.Terrain.Get(x, y);
                var environment = world.Environment.Get(x, y);
                int noise = (int)(Hash(x, y) % 17) - 8;
                bool water = h < -.15f;
                bool cold = environment.Temperature < 0;
                bool desert = environment.Moisture < .2f;
                var color = water ? new Color(31 + noise, 80 + noise, 108 + noise, 255)
                    : cold ? new Color(180 + noise, 202 + noise, 203 + noise, 255)
                    : h > .65f ? new Color(111 + noise, 119 + noise, 113 + noise, 255)
                    : desert ? new Color(166 + noise, 145 + noise, 91 + noise, 255)
                    : new Color(65 + noise, 108 + noise, 68 + noise, 255);
                float px = origin.X + x * cell, py = origin.Y + y * cell;
                Rect(px, py, cell + .5f, cell + .5f, color);
                if (water)
                {
                    if (world.Terrain.Get(Math.Min(x + 1, world.Terrain.Width - 1), y) >= -.15f)
                        Rect(px + cell * .8f, py, cell * .2f, cell, new Color(115, 155, 134, 180));
                }
                else
                {
                    if (h > .65f && Hash(x, y) % 3 == 0)
                    {
                        Raylib.DrawTriangle(new(px + cell * .15f, py + cell * .85f), new(px + cell * .85f, py + cell * .85f), new(px + cell * .5f, py + cell * .15f), new Color(150, 155, 144, 255));
                        Rect(px + cell * .42f, py + cell * .18f, cell * .2f, cell * .18f, new Color(207, 211, 197, 255));
                    }
                    else if (environment.Vegetation > .4f && Hash(x, y) % 3 == 0)
                    {
                        Rect(px + cell * .45f, py + cell * .45f, cell * .18f, cell * .6f, new Color(77, 58, 38, 255));
                        Raylib.DrawCircleV(new(px + cell * .57f, py + cell * .52f), cell * .44f, new Color(31, 65, 45, 220));
                        Raylib.DrawCircleV(new(px + cell * .4f, py + cell * .35f), cell * .34f, new Color(43 + noise, 91 + noise, 50, 255));
                        Rect(px + cell * .26f, py + cell * .2f, cell * .22f, cell * .16f, new Color(91, 126, 64, 255));
                    }
                    else if (world.Food.Get(x, y) > .65f && Hash(x, y) % 4 == 0)
                    {
                        Rect(px + cell * .3f, py + cell * .6f, cell * .15f, cell * .25f, new Color(128, 155, 65, 255));
                        Rect(px + cell * .55f, py + cell * .35f, cell * .15f, cell * .38f, new Color(146, 167, 72, 255));
                    }
                    if (environment.Stone > .3f && Hash(x, y) % 4 == 1)
                    {
                        Rect(px + cell * .3f, py + cell * .6f, cell * .5f, cell * .3f, new Color(72, 80, 80, 255));
                        Rect(px + cell * .25f, py + cell * .5f, cell * .45f, cell * .25f, new Color(157, 158, 140, 255));
                    }
                }
            }
    }

    static void DrawActors(Simulation world, Vector2 origin, float cell, int selectedId, Rectangle viewport)
    {
        var time = world.Tick / 30f;
        foreach (var structure in world.Structures)
        {
            float px = origin.X + structure.Position.X * cell, py = origin.Y + structure.Position.Y * cell;
            if (!Visible(px, py, cell * 2, viewport)) continue;
            var kind = structure.Kind.ToString();
            if (kind == "Road") { Rect(px, py, cell, cell, new Color(153, 131, 89, 220)); continue; }
            if (kind == "Farm")
            {
                Rect(px - cell * .4f, py - cell * .4f, cell * 1.8f, cell * 1.8f, new Color(102, 74, 43, 255));
                for (int row = 0; row < 4; row++) Rect(px - cell * .2f, py + row * cell * .35f, cell * 1.4f, cell * .12f, new Color(175, 168, 73, 255));
                continue;
            }
            Rect(px - cell * .3f, py, cell * 1.6f, cell * 1.2f, new Color(148, 105, 61, 255));
            Raylib.DrawTriangle(new(px - cell * .6f, py + cell * .3f), new(px + cell * 1.6f, py + cell * .3f), new(px + cell * .5f, py - cell * .8f), new Color(104, 64, 43, 255));
            Rect(px + cell * .35f, py + cell * .6f, cell * .35f, cell * .6f, new Color(52, 40, 32, 255));
        }
        foreach (var agent in world.Agents)
        {
            float px = origin.X + (agent.Position.X + .5f) * cell, py = origin.Y + (agent.Position.Y + .5f) * cell;
            float size = Math.Clamp(agent.Genome.Size, .7f, 1.5f) * Math.Max(cell, 5);
            var shirt = new Color((int)(90 + agent.Genome.Aggression * 140), (int)(100 + agent.Genome.Sociality * 120), 170, 255);
            if (!Visible(px, py, size, viewport)) continue;
            float walk = MathF.Sin(time * 9 + agent.Id) * size * .1f;
            AgentSpriteAtlas.Draw(px, py, size, walk, shirt);
            if (agent.Id == selectedId) Raylib.DrawCircleLines((int)px, (int)py, size * .85f, new Color(247, 231, 153, 255));
        }
        foreach (var predator in world.Predators)
        {
            float px = origin.X + (predator.Position.X + .5f) * cell, py = origin.Y + (predator.Position.Y + .5f) * cell;
            if (!Visible(px, py, cell, viewport)) continue;
            Rect(px - cell * .5f, py - cell * .2f, cell, cell * .45f, new Color(88, 49, 45, 255));
            Rect(px + cell * .25f, py - cell * .45f, cell * .4f, cell * .4f, new Color(139, 69, 48, 255));
            Rect(px + cell * .5f, py - cell * .35f, Math.Max(1, cell * .12f), Math.Max(1, cell * .12f), new Color(255, 200, 80, 255));
        }
    }
    public static void DrawTerritories(Simulation world, Vector2 origin, float cell)
    {
        foreach (var relation in world.Relations)
        {
            var first = world.Tribes.FirstOrDefault(t => t.Id == relation.FirstTribeId);
            var second = world.Tribes.FirstOrDefault(t => t.Id == relation.SecondTribeId);
            if (first is null || second is null || relation.Relation == TribalRelation.Neutral) continue;
            var color = relation.Relation == TribalRelation.War ? new Color(235, 92, 76, 170) : new Color(109, 210, 197, 160);
            Raylib.DrawLineEx(origin + new Vector2(first.Center.X, first.Center.Y) * cell,
                origin + new Vector2(second.Center.X, second.Center.Y) * cell, 2, color);
        }
        foreach (var tribe in world.Tribes)
        {
            var center = origin + new Vector2(tribe.Center.X, tribe.Center.Y) * cell;
            var color = new Color(100 + (tribe.Id * 47 % 140), 140 + (tribe.Id * 31 % 100), 120 + (tribe.Id * 19 % 120), 180);
            // Influence radius is informational, not exclusive ownership of terrain.
            Raylib.DrawCircleLines((int)center.X, (int)center.Y, 10 * cell, color);
            Game.Text($"Племя {tribe.Id} · {tribe.MemberIds.Count}", center.X + 6, center.Y - 26, 14, Color.White);
            Game.Text($"Еда {tribe.Food:0.0}  Дерево {tribe.Wood:0.0}  Камень {tribe.Stone:0.0}", center.X + 6, center.Y - 8, 11, Color.White);
        }
    }

    static bool Visible(float x, float y, float radius, Rectangle viewport) =>
        x + radius >= viewport.X && y + radius >= viewport.Y
        && x - radius <= viewport.X + viewport.Width && y - radius <= viewport.Y + viewport.Height;

    static uint Hash(int x, int y) => unchecked((uint)(x * 374761393 + y * 668265263) ^ ((uint)(x * 1274126177) >> 13));
    static void Rect(float x, float y, float w, float h, Color color) => Raylib.DrawRectangleRec(new Rectangle(x, y, w, h), color);
}

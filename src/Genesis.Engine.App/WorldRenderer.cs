using System.Numerics;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

internal static class WorldRenderer
{
    public static void Draw(Simulation world, Vector2 origin, float cell, int selectedId)
    {
        var time = world.Tick / 30f;
        for (int y = 0; y < world.Terrain.Height; y++)
            for (int x = 0; x < world.Terrain.Width; x++)
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
                    if ((Hash(x, y) % 5) == 0)
                    {
                        float wave = MathF.Sin(time * 1.3f + x * .7f + y) * .12f;
                        Rect(px + cell * .2f, py + cell * (.5f + wave), cell * .55f, Math.Max(1, cell * .08f), new Color(83, 139, 157, 180));
                    }
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
                if (environment.Fire > .05f)
                {
                    Raylib.DrawCircleV(new(px + cell * .5f, py + cell * .5f), cell * .5f, new Color(222, 78, 30, 210));
                    Raylib.DrawCircleV(new(px + cell * .45f, py + cell * (.4f + MathF.Sin(time * 9 + x) * .1f)), cell * .25f, new Color(255, 207, 71, 255));
                }
            }
        foreach (var structure in world.Structures)
        {
            float px = origin.X + structure.Position.X * cell, py = origin.Y + structure.Position.Y * cell;
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
            Raylib.DrawEllipse((int)px, (int)(py + size * .3f), size * .4f, size * .16f, new Color(10, 25, 22, 120));
            float walk = MathF.Sin(time * 9 + agent.Id) * size * .1f;
            Rect(px - size * .22f, py + size * .15f + walk, size * .14f, size * .3f, new Color(41, 49, 53, 255));
            Rect(px + size * .07f, py + size * .15f - walk, size * .14f, size * .3f, new Color(41, 49, 53, 255));
            Rect(px - size * .28f, py - size * .2f, size * .56f, size * .45f, shirt);
            Rect(px - size * .2f, py - size * .55f, size * .4f, size * .35f, new Color(226, 186, 139, 255));
            Rect(px - size * .22f, py - size * .59f, size * .44f, size * .12f, new Color(69, 53, 40, 255));
            if (agent.Id == selectedId) Raylib.DrawCircleLines((int)px, (int)py, size * .85f, new Color(247, 231, 153, 255));
        }
        foreach (var predator in world.Predators)
        {
            float px = origin.X + (predator.Position.X + .5f) * cell, py = origin.Y + (predator.Position.Y + .5f) * cell;
            Rect(px - cell * .5f, py - cell * .2f, cell, cell * .45f, new Color(88, 49, 45, 255));
            Rect(px + cell * .25f, py - cell * .45f, cell * .4f, cell * .4f, new Color(139, 69, 48, 255));
            Rect(px + cell * .5f, py - cell * .35f, Math.Max(1, cell * .12f), Math.Max(1, cell * .12f), new Color(255, 200, 80, 255));
        }
    }
    static uint Hash(int x, int y) => unchecked((uint)(x * 374761393 + y * 668265263) ^ ((uint)(x * 1274126177) >> 13));
    static void Rect(float x, float y, float w, float h, Color color) => Raylib.DrawRectangleRec(new Rectangle(x, y, w, h), color);
}

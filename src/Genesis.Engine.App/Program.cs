using Genesis.Engine.Core;
using Raylib_cs;

const int cell = 8;
const int header = 84;
const float step = 1f / 30;
var simulation = new Simulation();
var paused = false;
var accumulator = 0f;
var radius = 6f;
var speed = 1;
var smoke = args.Contains("--smoke");
var frames = 0;
Raylib.InitWindow(simulation.Terrain.Width * cell, simulation.Terrain.Height * cell + header, "Genesis Engine | Biome.AI");
Raylib.SetTargetFPS(60);
try
{
    while (!Raylib.WindowShouldClose())
    {
        var elapsed = Math.Clamp(Raylib.GetFrameTime(), 0, .1f);
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) paused = !paused;
        if (Raylib.IsKeyPressed(KeyboardKey.R)) { simulation = new Simulation(); accumulator = 0; }
        if (Raylib.IsKeyPressed(KeyboardKey.One)) speed = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) speed = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) speed = 4;
        radius = Math.Clamp(radius + Raylib.GetMouseWheelMove(), 1, 20);
        var mouse = Raylib.GetMousePosition();
        var point = new Vec2(mouse.X / cell, (mouse.Y - header) / cell);
        if (mouse.Y >= header && mouse.X >= 0 && mouse.X < simulation.Terrain.Width * cell)
        {
            var direction = Raylib.IsMouseButtonDown(MouseButton.Left) ? 1 : Raylib.IsMouseButtonDown(MouseButton.Right) ? -1 : 0;
            if (direction != 0) new TerrainBrush(radius, direction * elapsed * .6f).Apply(simulation.Terrain, point);
        }
        if (!paused)
        {
            accumulator += elapsed * speed;
            while (accumulator >= step) { simulation.Step(step); accumulator -= step; }
        }
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(12, 18, 27, 255));
        for (var y = 0; y < simulation.Terrain.Height; y++)
            for (var x = 0; x < simulation.Terrain.Width; x++)
            {
                var height = simulation.Terrain.Get(x, y);
                var shade = Math.Clamp((int)(height * 50), -25, 60);
                var color = height < -.15f ? new Color(23, 65, 104, 255)
                    : height > .65f ? new Color(120 + shade, 133 + shade, 145 + shade, 255)
                    : new Color(34 + shade, 78 + shade, 65 + shade, 255);
                Raylib.DrawRectangle(x * cell, y * cell + header, cell, cell, color);
                if (simulation.Food.Get(x, y) > .7f && height >= -.15f && height < .65f)
                    Raylib.DrawRectangle(x * cell + 3, y * cell + header + 3, 2, 2, new Color(92, 142, 77, 255));
            }
        foreach (var agent in simulation.Agents)
        {
            var color = new Color((int)(90 + agent.Genome.Aggression * 150), (int)(120 + agent.Genome.Sociality * 120), 190, 255);
            Raylib.DrawCircle((int)(agent.Position.X * cell + 4), (int)(agent.Position.Y * cell + header + 4), Math.Clamp(agent.Genome.Size * 2, 1, 4), color);
        }
        if (mouse.Y >= header) Raylib.DrawCircleLines((int)mouse.X, (int)mouse.Y, radius * cell, new Color(130, 230, 210, 180));
        Raylib.DrawText("GENESIS ENGINE / BIOME.AI", 16, 12, 22, new Color(130, 235, 203, 255));
        Raylib.DrawText($"Population {simulation.Agents.Count}   Tick {simulation.Tick}   Speed {speed}x   {(paused ? "PAUSED" : "LIVE")}", 500, 16, 18, Color.White);
        Raylib.DrawText("LMB raise | RMB lower | Wheel brush | Space pause | 1/2/3 speed | R reset | Esc exit", 16, 49, 18, Color.LightGray);
        Raylib.EndDrawing();
        if (smoke && ++frames >= 120) break;
    }
}
finally { Raylib.CloseWindow(); }

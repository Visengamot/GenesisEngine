using System.Numerics;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

internal static class Game
{
    const int Sidebar = 300, Top = 74, Bottom = 42;
    static Font font;
    static bool benchmarkMode;
    static readonly Color Ink = new(222, 231, 225, 255);
    static readonly Color Muted = new(144, 162, 160, 255);
    static readonly (GodTool Tool, string Name, string Hint)[] Tools =
    [
        (GodTool.Inspect,"Наблюдать", "Выберите жителя, чтобы увидеть его гены"),
        (GodTool.Raise,"Горы", "Поднимает землю и создаёт препятствия"),
        (GodTool.Lower,"Низины", "Опускает поверхность мира"),
        (GodTool.Water,"Вода", "Увлажняет землю и создаёт водоёмы"),
        (GodTool.Dry,"Пустыня", "Высушивает выбранную область"),
        (GodTool.Heat,"Тепло", "Повышает температуру области"),
        (GodTool.Cool,"Холод", "Понижает температуру области"),
        (GodTool.Forest,"Лес", "Высаживает деревья и добавляет древесину"),
        (GodTool.Food,"Пища", "Размещает съедобные ресурсы"),
        (GodTool.Stone,"Камень", "Размещает строительный ресурс"),
        (GodTool.Fire,"Пожар", "Поджигает растительность"),
        (GodTool.Predator,"Хищник", "Одно нажатие — один хищник"),
        (GodTool.People,"Житель", "Одно нажатие — новый житель"),
        (GodTool.Erase,"Очистить", "Удаляет объекты выбранной области")
    ];

    public static int Run(string[] args)
    {
        benchmarkMode = args.Contains("--benchmark-render");
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1480, 940, "Genesis Engine — Живой мир");
        Raylib.SetWindowMinSize(1100, 900);
        Raylib.SetTargetFPS(benchmarkMode ? 0 : 60);
        var fontPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(fontPath)) { Raylib.CloseWindow(); Console.Error.WriteLine("Не найден системный шрифт Segoe UI: " + fontPath); return 1; }
        var codepoints = Enumerable.Range(32, 95).Concat(Enumerable.Range(0x400, 256)).Concat(new[] { 0x2014, 0x2013, 0x00D7, 0x2116 }).ToArray();
        font = Raylib.LoadFontEx(fontPath, 32, codepoints, codepoints.Length);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        var benchmark = benchmarkMode ? new RenderBenchmark() : null;
        var simulation = benchmark?.CreateSimulation() ?? new Simulation();
        var tool = GodTool.Inspect;
        var radius = 4f;
        var paused = false;
        var speed = 1;
        var accumulator = 0f;
        var zoom = 1f;
        var pan = Vector2.Zero;
        var selectedId = -1;
        var showTerritories = false;
        var notice = "Мир развивается сам. Вы меняете условия жизни.";
        var smoke = !benchmarkMode && args.Contains("--smoke");
        var frames = 0;
        var savePath = Path.Combine(AppContext.BaseDirectory, "Saves", "world.json");
        try
        {
            while (!Raylib.WindowShouldClose())
            {
                benchmark?.BeginFrame(simulation);
                int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
                var mapArea = new Rectangle(Sidebar, Top, width - Sidebar, height - Top - Bottom);
                var mouse = Raylib.GetMousePosition();
                var elapsed = Math.Clamp(Raylib.GetFrameTime(), 0, .1f);
                bool onMap = Raylib.CheckCollisionPointRec(mouse, mapArea);
                float baseCell = Math.Min(mapArea.Width / simulation.Terrain.Width, mapArea.Height / simulation.Terrain.Height);
                float cell = baseCell * zoom;
                var origin = new Vector2(mapArea.X + (mapArea.Width - simulation.Terrain.Width * cell) / 2,
                    mapArea.Y + (mapArea.Height - simulation.Terrain.Height * cell) / 2) + pan;
                if (!benchmarkMode)
                {
                    float wheel = Raylib.GetMouseWheelMove();
                    if (onMap && Raylib.IsKeyDown(KeyboardKey.LeftControl)) zoom = Math.Clamp(zoom + wheel * .15f, .7f, 4);
                    else radius = Math.Clamp(radius + wheel, 1, 20);
                    if (onMap && Raylib.IsMouseButtonDown(MouseButton.Middle)) pan += Raylib.GetMouseDelta();
                    if (Raylib.IsKeyPressed(KeyboardKey.Space)) paused = !paused;
                    if (Raylib.IsKeyPressed(KeyboardKey.T)) showTerritories = !showTerritories;
                    if (Raylib.IsKeyPressed(KeyboardKey.One)) speed = 1;
                    if (Raylib.IsKeyPressed(KeyboardKey.Two)) speed = 2;
                    if (Raylib.IsKeyPressed(KeyboardKey.Three)) speed = 4;
                    if (Raylib.IsKeyPressed(KeyboardKey.Home)) { pan = Vector2.Zero; zoom = 1; }
                    if (Raylib.IsKeyPressed(KeyboardKey.R)) { simulation = new Simulation(seed: simulation.Seed + 1); selectedId = -1; accumulator = 0; notice = "Создан новый мир"; }
                    if (Raylib.IsKeyPressed(KeyboardKey.F5))
                    {
                        try { WorldFile.Save(simulation, savePath); notice = "Мир сохранён: " + savePath; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notice = "Не удалось сохранить: " + e.Message; }
                    }
                    if (Raylib.IsKeyPressed(KeyboardKey.F9))
                    {
                        try { simulation = WorldFile.Load(savePath); selectedId = -1; accumulator = 0; notice = "Мир загружен"; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException) { notice = "Не удалось загрузить: " + e.Message; }
                    }
                }
                var point = new Vec2((mouse.X - origin.X) / cell, (mouse.Y - origin.Y) / cell);
                bool inside = onMap && point.X >= 0 && point.Y >= 0 && point.X < simulation.Terrain.Width && point.Y < simulation.Terrain.Height;
                if (inside && !benchmarkMode)
                {
                    if (tool == GodTool.Inspect && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        var nearest = simulation.Agents.OrderBy(a => Distance(a.Position, point)).FirstOrDefault();
                        selectedId = nearest != null && Distance(nearest.Position, point) < 5 ? nearest.Id : -1;
                    }
                    else if (tool != GodTool.Inspect)
                    {
                        bool single = tool is GodTool.People or GodTool.Predator;
                        if (single ? Raylib.IsMouseButtonPressed(MouseButton.Left) : Raylib.IsMouseButtonDown(MouseButton.Left))
                        {
                            simulation.ApplyTool(tool, point, radius, single ? 1 : elapsed * 2);
                            WorldRenderer.InvalidateTerrain();
                        }
                    }
                }
                benchmark?.BeginStep();
                if (benchmarkMode) simulation.Step(1f / 30);
                else if (!paused)
                {
                    accumulator += elapsed * speed;
                    while (accumulator >= 1f / 30) { simulation.Step(1f / 30); accumulator -= 1f / 30; }
                }
                benchmark?.EndStep();
                benchmark?.BeginDraw();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(16, 23, 28, 255));
                Raylib.BeginScissorMode((int)mapArea.X, (int)mapArea.Y, (int)mapArea.Width, (int)mapArea.Height);
                WorldRenderer.Draw(simulation, origin, cell, selectedId, mapArea);
                if (showTerritories) WorldRenderer.DrawTerritories(simulation, origin, cell);
                if (inside && tool != GodTool.Inspect)
                {
                    Raylib.DrawCircleLines((int)mouse.X, (int)mouse.Y, radius * cell, new Color(220, 240, 200, 220));
                    Raylib.DrawCircle((int)mouse.X, (int)mouse.Y, 2, Ink);
                }
                Raylib.EndScissorMode();
                Raylib.DrawRectangle(0, 0, width, Top, new Color(23, 32, 38, 255));
                Text("GENESIS", 22, 12, 25, new Color(147, 223, 177, 255));
                Text("ЛАБОРАТОРИЯ ЖИВОГО МИРА", 23, 43, 12, Muted);
                Text($"Жители  {simulation.Agents.Count}/{Simulation.MaximumPopulation}     Племена  {simulation.Tribes.Count}     Поколение  {(simulation.Agents.Count == 0 ? 0 : simulation.Agents.Max(a => a.Generation))}", 322, 14, 19, Ink);
                Text($"Время  {simulation.Tick / 30f:0} с    •    {speed}×    •    Видов {simulation.SpeciesCount}    •    {(paused ? "Пауза" : "Жизнь продолжается")}", 322, 42, 16, Muted);
                if (Button(new(width - 244, 20, 102, 34), paused ? "Продолжить" : "Пауза", paused)) paused = !paused;
                if (Button(new(width - 130, 20, 108, 34), $"Скорость {speed}×", false)) speed = speed == 4 ? 1 : speed * 2;
                Raylib.DrawRectangle(0, Top, Sidebar, height - Top, new Color(25, 35, 40, 255));
                Text("ИНСТРУМЕНТЫ ТВОРЦА", 20, 94, 16, Ink);
                for (int i = 0; i < Tools.Length; i++)
                {
                    var entry = Tools[i];
                    var rect = new Rectangle(16 + i % 2 * 138, 125 + i / 2 * 42, 130, 34);
                    if (Button(rect, entry.Name, entry.Tool == tool)) tool = entry.Tool;
                }
                var chosen = Tools.First(t => t.Tool == tool);
                Text(chosen.Name, 20, 435, 21, new Color(147, 223, 177, 255));
                Wrap(chosen.Hint, 20, 464, 260, 15, Muted);
                Text($"Радиус кисти: {radius:0}", 20, 510, 16, Ink);
                Text("Колесо — кисть • Ctrl+колесо — масштаб", 20, 538, 12, Muted);
                Text("Средняя кнопка — карта • T — племена", 20, 557, 12, Muted);
                var selected = simulation.Agents.FirstOrDefault(a => a.Id == selectedId);
                if (selected != null)
                {
                    Text($"Житель №{selected.Id} · Вид {selected.SpeciesId}", 20, 595, 17, Ink);
                    Text($"Возраст {selected.Age:0} с   Поколение {selected.Generation}", 20, 625, 14, Muted);
                    Text($"Энергия {selected.Energy:0.00} / 3", 20, 648, 15, Ink);
                    Text($"Скорость {selected.Genome.Speed:0.00}   Зрение {selected.Genome.Vision:0.0}", 20, 674, 14, Muted);
                    Text($"Общительность {selected.Genome.Sociality:0.00}", 20, 698, 14, Muted);
                    Text($"Агрессия {selected.Genome.Aggression:0.00}", 20, 722, 14, Muted);
                    Text($"Мозг: {selected.Brain.HiddenNodeCount} узлов / {selected.Brain.ConnectionCount} связей", 20, 746, 13, Muted);
                }
                else
                {
                    Text("ЛЕТОПИСЬ МИРА", 20, 594, 16, Ink);
                    int eventY = 622;
                    foreach (var entry in simulation.Events.TakeLast(3))
                    {
                        Wrap(entry, 20, eventY, 260, 13, Muted);
                        eventY += 44;
                    }
                    if (simulation.Events.Count == 0) Wrap("Выберите «Наблюдать» и нажмите на жителя. Цвет одежды отражает его гены.", 20, 622, 260, 16, Muted);
                }
                Text("F5 — сохранить   F9 — загрузить", 20, height - 103, 14, Ink);
                Text("R — новый мир   Home — вид целиком", 20, height - 80, 13, Muted);
                Raylib.DrawRectangle(Sidebar, height - Bottom, width - Sidebar, Bottom, new Color(22, 31, 36, 255));
                Text(notice.Length > 110 ? notice[..110] + "..." : notice, Sidebar + 16, height - 29, 14, Muted);
                benchmark?.BeginPresent();
                Raylib.EndDrawing();
                if (benchmark != null && benchmark.EndFrame(simulation))
                {
                    if (benchmark.IsComplete) break;
                    simulation = benchmark.CreateSimulation();
                }
                if (smoke && ++frames >= 120)
                {
                    Raylib.TakeScreenshot("genesis-preview.png");
                    break;
                }
            }
            benchmark?.Save();
            return benchmark != null && !benchmark.IsComplete ? 2 : 0;
        }
        finally { WorldRenderer.Unload(); Raylib.UnloadFont(font); Raylib.CloseWindow(); }
    }
    static float Distance(Vec2 a, Vec2 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    internal static void Text(string text, float x, float y, float size, Color color) => Raylib.DrawTextEx(font, text, new Vector2(x, y), size, .3f, color);
    static bool Button(Rectangle rect, string text, bool selected)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        Raylib.DrawRectangleRounded(rect, .16f, 4, selected ? new Color(55, 105, 83, 255) : hover ? new Color(56, 73, 79, 255) : new Color(36, 49, 55, 255));
        Text(text, rect.X + 10, rect.Y + 8, 15, selected ? Color.White : Ink);
        return !benchmarkMode && hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
    static void Wrap(string text, float x, float y, float width, float size, Color color)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (Raylib.MeasureTextEx(font, line + word, size, .3f).X > width) { Text(line, x, y, size, color); y += size + 5; line = ""; }
            line += word + " ";
        }
        Text(line, x, y, size, color);
    }
}

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
        bool naturalMode = args.Contains("--benchmark-natural");
        benchmarkMode = args.Contains("--benchmark-render") || naturalMode;
        // Natural prewarm is headless; no window or renderer resources are needed yet.
        var natural = naturalMode ? new NaturalPopulationBenchmark() : null;
        var naturalSimulation = natural?.CreateSimulation();
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1480, 940, "Genesis Engine — Живой мир");
        Raylib.SetWindowMinSize(1100, 900);
        Raylib.SetTargetFPS(benchmarkMode ? 0 : 60);
        var fontPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(fontPath)) { Raylib.CloseWindow(); Console.Error.WriteLine("Не найден системный шрифт Segoe UI: " + fontPath); return 1; }
        var codepoints = Enumerable.Range(32, 95).Concat(Enumerable.Range(0x400, 256)).Concat(new[] { 0x2014, 0x2013, 0x00D7, 0x2116 }).ToArray();
        font = Raylib.LoadFontEx(fontPath, 32, codepoints, codepoints.Length);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        var benchmark = !naturalMode && benchmarkMode ? new RenderBenchmark() : null;
        var simulation = naturalSimulation ?? benchmark?.CreateSimulation() ?? new Simulation();
        var smokeAgentPanel = !benchmarkMode && args.Contains("--smoke-agent-panel");
        var tool = GodTool.Inspect;
        var radius = 4f;
        var paused = smokeAgentPanel;
        var speed = 1;
        var accumulator = 0f;
        var zoom = 1f;
        var pan = Vector2.Zero;
        var mapDrag = new MapInteractionController();
        var selectedId = smokeAgentPanel ? simulation.Agents[0].Id : -1;
        var showTerritories = false;
        var showCivilizationPanel = false;
        var selectedTribeId = -1;
        var editingLimit = false;
        var limitInput = "";
        var notice = "Мир развивается сам. Вы меняете условия жизни.";
        var smoke = !benchmarkMode && (args.Contains("--smoke") || smokeAgentPanel);
        var frames = 0;
        var savePath = Path.Combine(AppContext.BaseDirectory, "Saves", "world.json");
        try
        {
            while (!Raylib.WindowShouldClose())
            {
                benchmark?.BeginFrame(simulation);
                natural?.BeginFrame(simulation);
                int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
                var mapArea = new Rectangle(Sidebar, Top, width - Sidebar, height - Top - Bottom);
                var mouse = Raylib.GetMousePosition();
                var elapsed = Math.Clamp(Raylib.GetFrameTime(), 0, .1f);
                bool onMap = Raylib.CheckCollisionPointRec(mouse, mapArea)
                    && (selectedId < 0 || !Raylib.CheckCollisionPointRec(mouse, AgentDetailsPanel.Bounds(width, height, Sidebar, Top)));
                float baseCell = Math.Min(mapArea.Width / simulation.Terrain.Width, mapArea.Height / simulation.Terrain.Height);
                float cell = baseCell * zoom;
                Vector2 MapOrigin() => new(mapArea.X + (mapArea.Width - simulation.Terrain.Width * cell) / 2 + pan.X,
                    mapArea.Y + (mapArea.Height - simulation.Terrain.Height * cell) / 2 + pan.Y);
                if (!benchmarkMode)
                {
                    float wheel = Raylib.GetMouseWheelMove();
                    if (onMap && Raylib.IsKeyDown(KeyboardKey.LeftControl)) zoom = Math.Clamp(zoom + wheel * .15f, .7f, 4);
                    else radius = Math.Clamp(radius + wheel, 1, 20);
                    if (onMap && Raylib.IsMouseButtonDown(MouseButton.Middle)) pan += Raylib.GetMouseDelta();
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.Space)) paused = !paused;
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.T)) showTerritories = !showTerritories;
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.C)) showCivilizationPanel = !showCivilizationPanel;
                    if (editingLimit)
                    {
                        int key;
                        while ((key = Raylib.GetCharPressed()) != 0)
                            if (key >= '0' && key <= '9' && limitInput.Length < 5) limitInput += (char)key;
                        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && limitInput.Length > 0) limitInput = limitInput[..^1];
                        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { editingLimit = false; limitInput = ""; }
                        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                        {
                            if (int.TryParse(limitInput, out int requested) && requested >= Simulation.MinimumPopulationLimit && requested <= Simulation.AbsoluteMaximumPopulation)
                            { simulation.SetPopulationLimit(requested); notice = $"Лимит жителей: {requested}"; editingLimit = false; }
                            else notice = $"Допустимый лимит: {Simulation.MinimumPopulationLimit}–{Simulation.AbsoluteMaximumPopulation}";
                        }
                    }
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.One)) speed = 1;
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.Two)) speed = 2;
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.Three)) speed = 4;
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.Home)) { pan = Vector2.Zero; zoom = 1; }
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.R)) { int chosenLimit = simulation.PopulationLimit; simulation = new Simulation(seed: simulation.Seed + 1); simulation.SetPopulationLimit(chosenLimit); selectedId = -1; selectedTribeId = -1; accumulator = 0; mapDrag.Reset(); notice = "Создан новый мир"; }
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.F5))
                    {
                        try { WorldFile.Save(simulation, savePath); notice = "Мир сохранён: " + savePath; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notice = "Не удалось сохранить: " + e.Message; }
                    }
                    if (!editingLimit && Raylib.IsKeyPressed(KeyboardKey.F9))
                    {
                        try { simulation = WorldFile.Load(savePath); selectedId = -1; selectedTribeId = -1; accumulator = 0; mapDrag.Reset(); notice = "Мир загружен"; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException) { notice = "Не удалось загрузить: " + e.Message; }
                    }
                }
                bool brush = tool is not (GodTool.Inspect or GodTool.People or GodTool.Predator);
                var gesture = benchmarkMode ? default : mapDrag.Update(mouse,
                    Raylib.IsMouseButtonPressed(MouseButton.Left), Raylib.IsMouseButtonDown(MouseButton.Left),
                    Raylib.IsMouseButtonReleased(MouseButton.Left), onMap,
                    brush && (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)));
                pan += gesture.PanDelta;
                var origin = MapOrigin();
                var point = new Vec2((mouse.X - origin.X) / cell, (mouse.Y - origin.Y) / cell);
                bool inside = onMap && point.X >= 0 && point.Y >= 0 && point.X < simulation.Terrain.Width && point.Y < simulation.Terrain.Height;
                if (inside && !benchmarkMode)
                {
                    if (gesture.Click && tool == GodTool.Inspect)
                    {
                        var nearest = simulation.Agents.OrderBy(a => Distance(a.Position, point)).FirstOrDefault();
                        selectedId = nearest != null && Distance(nearest.Position, point) < 5 ? nearest.Id : -1;
                    }
                    else if (gesture.Click || (gesture.Paint && brush))
                    {
                        simulation.ApplyTool(tool, point, radius,
                            tool is GodTool.People or GodTool.Predator ? 1 : gesture.Paint ? elapsed * 2 : .15f);
                        WorldRenderer.InvalidateTerrain();
                    }
                }
                benchmark?.BeginStep();
                natural?.BeginStep();
                if (benchmarkMode) simulation.Step(1f / 30);
                else if (!paused)
                {
                    accumulator += elapsed * speed;
                    while (accumulator >= 1f / 30) { simulation.Step(1f / 30); accumulator -= 1f / 30; }
                }
                benchmark?.EndStep();
                natural?.EndStep();
                benchmark?.BeginDraw();
                natural?.BeginDraw();
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
                Text($"Жители  {simulation.Agents.Count}/{simulation.PopulationLimit}     Племена  {simulation.Tribes.Count}     Поколение  {(simulation.Agents.Count == 0 ? 0 : simulation.Agents.Max(a => a.Generation))}", 322, 14, 19, Ink);
                Text($"Время  {simulation.Tick / 30f:0} с    •    {speed}×    •    Видов {simulation.SpeciesCount}    •    {(paused ? "Пауза" : "Жизнь продолжается")}", 322, 42, 16, Muted);
                if (Button(new Rectangle(width - 244, 20, 102, 34), paused ? "Продолжить" : "Пауза", paused)) paused = !paused;
                if (Button(new(width - 130, 20, 108, 34), $"Скорость {speed}×", false)) speed = speed == 4 ? 1 : speed * 2;
                Raylib.DrawRectangle(0, Top, Sidebar, height - Top, new Color(25, 35, 40, 255));
                Raylib.BeginScissorMode(0, Top, Sidebar, height - Top - Bottom);
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
                Text($"Радиус кисти: {radius:0}", 20, 502, 16, Ink);
                var limitButton = new Rectangle(16, 525, 268, 34);
                if (Button(limitButton, editingLimit ? $"Лимит: {limitInput}_ (Enter)" : $"Лимит жителей: {simulation.PopulationLimit} — изменить", editingLimit) && !editingLimit)
                { editingLimit = true; limitInput = ""; }
                Text("ЛКМ+движение — карта • клик — действие", 20, 568, 12, Muted);
                Text("Shift+ЛКМ — кисть • Ctrl+колесо — масштаб", 20, 585, 12, Muted);
                var civilizationButton = new Rectangle(16, 602, 268, 30);
                if (Button(civilizationButton, showCivilizationPanel ? "Цивилизации: скрыть" : "Цивилизации: открыть", showCivilizationPanel))
                    showCivilizationPanel = !showCivilizationPanel;
                var selected = simulation.Agents.FirstOrDefault(a => a.Id == selectedId);
                if (selected is null) selectedId = -1;
                if (showCivilizationPanel)
                {
                    var tribes = simulation.Tribes.OrderBy(t => t.Id).ToArray();
                    var selectedTribe = tribes.FirstOrDefault(t => t.Id == selectedTribeId) ?? tribes.FirstOrDefault();
                    if (selectedTribe is null) Text("Племена пока не возникли", 20, 645, 14, Muted);
                    else
                    {
                        selectedTribeId = selectedTribe.Id;
                        if (Button(new Rectangle(16, 639, 268, 29), $"Племя {selectedTribe.Id} • {PhaseName(selectedTribe.Phase)}  ›", false))
                            selectedTribeId = tribes[(Array.IndexOf(tribes, selectedTribe) + 1) % tribes.Length].Id;
                        Text($"Жители {selectedTribe.MemberIds.Count} Еда {selectedTribe.Food:0.0} Дерево {selectedTribe.Wood:0.0}", 20, 671, 11, Muted);
                        Text($"Камень {selectedTribe.Stone:0.0} · опыт: еда {selectedTribe.FoodKnowledge:0.0} лес {selectedTribe.WoodKnowledge:0.0} камень {selectedTribe.StoneKnowledge:0.0}", 20, 686, 9, Muted);
                        Text("Ветки: ✓ освоено / % изучено / — закрыто", 20, 700, 10, Ink);
                        int techY = 715;
                        foreach (var research in selectedTribe.Research)
                        {
                            string state = research.Unlocked ? "✓" : research.Available ? $"{research.Progress * 100:0}%" : "—";
                            Text($"{BranchName(research.Branch)}: {TechName(research.Milestone)} {state}", 20, techY, 11, research.Unlocked ? Ink : Muted);
                            techY += 15;
                        }
                    }
                }
                else if (selected != null)
                    Text($"Выбран житель №{selected.Id}. Подробности — справа.", 20, 651, 12, Muted);
                else
                {
                    Text("ЛЕТОПИСЬ МИРА", 20, 610, 16, Ink);
                    int eventY = 633;
                    foreach (var entry in simulation.Events.TakeLast(3))
                    {
                        Wrap(entry, 20, eventY, 260, 13, Muted);
                        eventY += 44;
                    }
                    if (simulation.Events.Count == 0) Wrap("Выберите «Наблюдать» и нажмите на жителя. Цвет одежды отражает его гены.", 20, 633, 260, 16, Muted);
                }
                var mortality = simulation.Mortality;
                int mortalityY = height - 135;
                Text($"Умерли {mortality.LifetimeDeaths} · возраст {mortality.OldAge} · голод {mortality.EnergyDepletion}", 20, mortalityY, 11, Ink);
                Text($"Огонь {mortality.Fire} · хищники {mortality.Predators} · климат {mortality.Climate}", 20, mortalityY + 18, 11, Muted);
                Text($"Прочие причины: {mortality.Other}", 20, mortalityY + 35, 11, Muted);
                Text("F5 — сохранить   F9 — загрузить", 20, height - 90, 13, Ink);
                Text("R — новый мир   Home — вид целиком", 20, height - 70, 12, Muted);
                Raylib.EndScissorMode();
                if (selected != null && AgentDetailsPanel.Draw(selected, width, height, Sidebar, Top, font))
                    selectedId = -1;
                Raylib.DrawRectangle(Sidebar, height - Bottom, width - Sidebar, Bottom, new Color(22, 31, 36, 255));
                Text(notice.Length > 110 ? notice[..110] + "..." : notice, Sidebar + 16, height - 29, 14, Muted);
                benchmark?.BeginPresent();
                natural?.BeginPresent();
                Raylib.EndDrawing();
                if (natural != null && natural.EndFrame(simulation)) break;
                if (benchmark != null && benchmark.EndFrame(simulation))
                {
                    if (benchmark.IsComplete) break;
                    simulation = benchmark.CreateSimulation();
                }
                if (smoke && ++frames >= 120)
                {
                    Raylib.TakeScreenshot(smokeAgentPanel ? "genesis-agent-panel.png" : "genesis-preview.png");
                    break;
                }
            }
            benchmark?.Save();
            natural?.Save(simulation);
            return (benchmark != null && !benchmark.IsComplete) || (natural != null && !natural.IsComplete) ? 2 : 0;
        }
        finally { WorldRenderer.Unload(); Raylib.UnloadFont(font); Raylib.CloseWindow(); }
    }
    static string PhaseName(CivilizationPhase phase) => phase switch
    {
        CivilizationPhase.Early => "Первые поселения", CivilizationPhase.Settled => "Поселение",
        CivilizationPhase.Skilled => "Ремёсла", _ => "Развитое общество"
    };
    static string BranchName(CivilizationBranch branch) => branch switch
    {
        CivilizationBranch.Food => "Питание", CivilizationBranch.Craft => "Ремесло", _ => "Защита"
    };
    static string TechName(CivilizationMilestone milestone) => milestone switch
    {
        CivilizationMilestone.Farming => "Земледелие", CivilizationMilestone.Weapons => "Оружие",
        CivilizationMilestone.Toolmaking => "Орудия", CivilizationMilestone.Medicine => "Лечение",
        CivilizationMilestone.Fireproofing => "Просеки", _ => "Освоено"
    };
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

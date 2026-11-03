using System.Numerics;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

/// <summary>Standalone agent card; its fixed rows keep traits away from the narrow sidebar.</summary>
internal static class AgentDetailsPanel
{
    private static readonly Color Background = new(28, 42, 48, 246);
    private static readonly Color Accent = new(137, 221, 173, 255);
    private static readonly Color Ink = new(224, 235, 229, 255);
    private static readonly Color Secondary = new(153, 176, 173, 255);
    private const int CardWidth = 350;
    private const int CardHeight = 424;
    private const int Gap = 12;

    internal static Rectangle Bounds(int screenWidth, int screenHeight, int sidebar, int top)
    {
        int width = Math.Min(CardWidth, Math.Max(100, screenWidth - sidebar - 2 * Gap));
        int height = Math.Min(CardHeight, Math.Max(100, screenHeight - top - 2 * Gap - 42));
        int left = Math.Max(sidebar + Gap, screenWidth - width - 16);
        int upper = Math.Max(top + Gap, Math.Min(screenHeight - height - 58, 96));
        return new Rectangle(left, upper, width, height);
    }

    /// <returns>True when the close button was clicked.</returns>
    internal static bool Draw(Agent agent, int screenWidth, int screenHeight, int sidebar, int top, Font font)
    {
        var rect = Bounds(screenWidth, screenHeight, sidebar, top);
        Raylib.DrawRectangleRounded(rect, .06f, 8, Background);
        Raylib.BeginScissorMode((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        Raylib.DrawRectangleLinesEx(rect, 2, new Color(79, 126, 110, 255));
        float x = rect.X + 20, y = rect.Y + 16;
        void Line(string text, int row, float size = 16, bool accent = false)
            => Raylib.DrawTextEx(font, text, new Vector2(x, y + row * 36), size, .4f, accent ? Accent : Ink);

        Line($"ЖИТЕЛЬ №{agent.Id}", 0, 22, true);
        Line($"Вид {agent.SpeciesId}  ·  Поколение {agent.Generation}", 1, 15);
        Raylib.DrawLine((int)x, (int)(y + 75), (int)((rect.X + rect.Width) - 20), (int)(y + 75), Secondary);
        Line($"Возраст: {agent.Age:0.0} с", 3);
        Line($"Энергия: {agent.Energy:0.00} из 3", 4);
        Line($"Скорость: {agent.Genome.Speed:0.00}", 5);
        Line($"Размер: {agent.Genome.Size:0.00}  ·  Зрение: {agent.Genome.Vision:0.0}", 6, 15);
        Line($"Метаболизм: {agent.Genome.Metabolism:0.00}", 7);
        Line($"Общительность: {agent.Genome.Sociality:0.00}", 8);
        Line($"Агрессия: {agent.Genome.Aggression:0.00}", 9);
        Line($"Любознательность: {agent.Genome.Curiosity:0.00}", 10);
        Raylib.DrawTextEx(font, $"Мозг: {agent.Brain.HiddenNodeCount} узлов · {agent.Brain.ConnectionCount} связей",
            new Vector2(x, (rect.Y + rect.Height) - 27), 13, .3f, Secondary);
        var close = new Rectangle((rect.X + rect.Width) - 38, rect.Y + 12, 28, 28);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), close);
        Raylib.DrawRectangleRounded(close, .2f, 4, hover ? new Color(101, 80, 81, 255) : new Color(58, 76, 81, 255));
        Raylib.DrawTextEx(font, "×", new Vector2(close.X + 6, close.Y + 1), 23, .2f, Ink);
        Raylib.EndScissorMode();
        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
}

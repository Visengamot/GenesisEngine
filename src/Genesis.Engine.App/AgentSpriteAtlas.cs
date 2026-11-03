using System.Numerics;
using Raylib_cs;

namespace Genesis.Engine.App;

/// <summary>Procedural high-resolution atlas; preserves continuous gene tint and walking offsets.</summary>
internal static class AgentSpriteAtlas
{
    const int Tile = 128;
    static RenderTexture2D atlas;

    internal static void EnsureLoaded()
    {
        if (atlas.Id != 0) return;
        atlas = Raylib.LoadRenderTexture(Tile * 3, Tile);
        Raylib.BeginTextureMode(atlas);
        Raylib.ClearBackground(Color.Blank);
        // Opaque white silhouette, tinted at draw time to avoid double alpha blending.
        Raylib.DrawEllipse(Tile / 2, Tile / 2, Tile * .4f, Tile * .16f, Color.White);
        Raylib.DrawRectangleRec(new Rectangle(Tile + Tile * .3f, Tile * .45f, Tile * .4f, Tile * .35f), new Color(226, 186, 139, 255));
        Raylib.DrawRectangleRec(new Rectangle(Tile + Tile * .28f, Tile * .41f, Tile * .44f, Tile * .12f), new Color(69, 53, 40, 255));
        // A white texel region also backs all rectangles, keeping agent draws in one texture batch.
        Raylib.DrawRectangle(Tile * 3 - 8, 0, 8, 8, Color.White);
        Raylib.EndTextureMode();
        Raylib.SetTextureFilter(atlas.Texture, TextureFilter.Point);
    }

    internal static void Draw(float x, float y, float size, float walk, Color shirt)
    {
        Raylib.DrawTexturePro(atlas.Texture, new Rectangle(0, 0, Tile, -Tile),
            new Rectangle(x - size * .5f, y - size * .2f, size, size), Vector2.Zero, 0, new Color(10, 25, 22, 120));
        Quad(x - size * .22f, y + size * .15f + walk, size * .14f, size * .3f, new Color(41, 49, 53, 255));
        Quad(x + size * .07f, y + size * .15f - walk, size * .14f, size * .3f, new Color(41, 49, 53, 255));
        Quad(x - size * .28f, y - size * .2f, size * .56f, size * .45f, shirt);
        Raylib.DrawTexturePro(atlas.Texture, new Rectangle(Tile, 0, Tile, -Tile),
            new Rectangle(x - size * .5f, y - size, size, size), Vector2.Zero, 0, Color.White);
    }

    static void Quad(float x, float y, float width, float height, Color color) =>
        Raylib.DrawTexturePro(atlas.Texture, new Rectangle(Tile * 3 - 6, Tile - 6, 2, 2),
            new Rectangle(x, y, width, height), Vector2.Zero, 0, color);

    internal static void Unload()
    {
        if (atlas.Id != 0) Raylib.UnloadRenderTexture(atlas);
        atlas = default;
    }
}

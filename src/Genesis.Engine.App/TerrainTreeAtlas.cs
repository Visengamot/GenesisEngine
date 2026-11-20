using System.Numerics;
using Raylib_cs;

namespace Genesis.Engine.App;

/// <summary>Precomposes the four tree primitives into a straight-alpha texture, once per window.</summary>
internal static class TerrainTreeAtlas
{
    private const int Tile = 72, Scale = 64, Shades = 17;
    private static Texture2D texture;
    internal static bool IsLoaded => texture.Id != 0;

    internal static bool EnsureLoaded()
    {
        if (texture.Id != 0) return true;
        var image = Raylib.GenImageColor(Tile * Shades, Tile, Color.Blank);
        try
        {
            for (int shade = 0; shade < Shades; shade++)
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                float u = (x + .5f) / Scale, v = (y + .5f) / Scale;
                var color = Color.Blank;
                if (u >= .45f && u < .63f && v >= .45f && v < 1.05f) color = new(77, 58, 38, 255);
                if (InsideCircle(u, v, .57f, .52f, .44f))
                    color = color.A == 0 ? new(31, 65, 45, 220) : new(37, 64, 44, 255);
                int noise = shade - 8;
                if (InsideCircle(u, v, .4f, .35f, .34f)) color = new(43 + noise, 91 + noise, 50, 255);
                if (u >= .26f && u < .48f && v >= .2f && v < .36f) color = new(91, 126, 64, 255);
                if (color.A != 0) Raylib.ImageDrawPixel(ref image, shade * Tile + x, y, color);
            }
            // The last tile corner is unused by all tree shapes; use it for solid terrain primitives.
            Raylib.ImageDrawPixel(ref image, Tile * Shades - 2, Tile - 2, Color.White);
            texture = Raylib.LoadTextureFromImage(image);
            if (texture.Id == 0) return false;
            Raylib.SetTextureFilter(texture, TextureFilter.Point);
            return true;
        }
        finally { Raylib.UnloadImage(image); }
    }

    internal static void UseForShapes() => Raylib.SetShapesTexture(texture, new(Tile * Shades - 2, Tile - 2, 1, 1));

    internal static void Draw(float x, float y, float cell, int noise)
    {
        // Exclude the reserved bottom row/column; neither is reached by the tree silhouette.
        float extent = cell * (Tile - 3) / Scale;
        Raylib.DrawTexturePro(texture, new((noise + 8) * Tile, 0, Tile - 3, Tile - 3),
            new(x, y, extent, extent), Vector2.Zero, 0, Color.White);
    }

    internal static void Unload()
    {
        if (texture.Id != 0) Raylib.UnloadTexture(texture);
        texture = default;
    }

    private static bool InsideCircle(float x, float y, float cx, float cy, float radius) =>
        (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
}

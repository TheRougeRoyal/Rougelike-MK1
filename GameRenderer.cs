using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Roguelike;

/// <summary>
/// Draws dungeon tiles, the player, and the text-free HUD.
/// </summary>
public sealed class GameRenderer
{
    private readonly Texture2D pixel;
    private readonly SpriteBatch spriteBatch;
    private readonly int tileSize;
    private readonly int hudHeight;

    /// <summary>
    /// Initializes a renderer using a single white pixel texture.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device used to create the texture.</param>
    /// <param name="spriteBatch">The sprite batch used for drawing.</param>
    /// <param name="tileSize">The size of a tile in pixels.</param>
    /// <param name="hudHeight">The height of the HUD strip in pixels.</param>
    public GameRenderer(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, int tileSize, int hudHeight)
    {
        this.spriteBatch = spriteBatch;
        this.tileSize = tileSize;
        this.hudHeight = hudHeight;
        pixel = new Texture2D(graphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Draws one complete frame.
    /// </summary>
    /// <param name="dungeon">The current dungeon.</param>
    /// <param name="player">The player actor.</param>
    /// <param name="depth">The current dungeon depth.</param>
    public void Draw(Dungeon dungeon, PlayerActor player, int depth)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        for (int y = 0; y < dungeon.Height; y++)
        {
            for (int x = 0; x < dungeon.Width; x++)
            {
                Point position = new(x, y);
                if (!dungeon.IsExplored(position))
                {
                    continue;
                }

                bool lit = dungeon.IsVisible(position);
                Color tileColor = GetTileColor(dungeon[position], lit);
                DrawRectangle(new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize), tileColor);
            }
        }

        if (dungeon.IsVisible(player.Position))
        {
            Rectangle playerRect = new(
                player.Position.X * tileSize + 3,
                player.Position.Y * tileSize + 3,
                tileSize - 6,
                tileSize - 6);
            DrawRectangle(playerRect, Color.Gold);
        }

        int hudTop = dungeon.Height * tileSize;
        DrawRectangle(new Rectangle(0, hudTop, dungeon.Width * tileSize, hudHeight), new Color(12, 12, 18));
        for (int index = 0; index < depth; index++)
        {
            DrawRectangle(new Rectangle(8 + index * 14, hudTop + 13, 8, 14), Color.CornflowerBlue);
        }

        spriteBatch.End();
    }

    private void DrawRectangle(Rectangle destination, Color color)
    {
        spriteBatch.Draw(pixel, destination, color);
    }

    private static Color GetTileColor(TileType tile, bool lit)
    {
        if (lit)
        {
            return tile switch
            {
                TileType.Wall => new Color(80, 90, 110),
                TileType.Stairs => new Color(190, 150, 45),
                _ => new Color(155, 155, 165)
            };
        }

        return tile switch
        {
            TileType.Wall => new Color(28, 32, 42),
            TileType.Stairs => new Color(70, 55, 22),
            _ => new Color(52, 52, 60)
        };
    }
}

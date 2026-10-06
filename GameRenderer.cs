using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Roguelike;

/// <summary>
/// Draws dungeon tiles, actors, feedback bars, and the HUD.
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
        Draw(dungeon, player, Array.Empty<MonsterActor>(), depth, 1, 1, string.Empty, Color.White, null);
    }

    /// <summary>Draws a complete game state.</summary>
    public void Draw(
        Dungeon dungeon, PlayerActor player, IReadOnlyList<MonsterActor> monsters,
        int depth, int level, int experience, string message)
    {
        Draw(dungeon, player, monsters, depth, level, experience, message, Color.White, null);
    }

    /// <summary>Draws a complete state with feedback tint.</summary>
    public void Draw(
        Dungeon dungeon, PlayerActor player, IReadOnlyList<MonsterActor> monsters,
        int depth, int level, int experience, string message, Color feedbackTint,
        Actor? feedbackActor = null)
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
            DrawRectangle(playerRect, feedbackActor == player ? Color.White : Color.Gold);
        }

        foreach (MonsterActor monster in monsters)
        {
            if (!monster.IsAlive || !dungeon.IsVisible(monster.Position)) continue;
            Rectangle monsterRect = new(
                monster.Position.X * tileSize + 4,
                monster.Position.Y * tileSize + 4,
                tileSize - 8,
                tileSize - 8);
            DrawRectangle(monsterRect, feedbackActor == monster ? Color.White : monster.Definition.Color);
            if (monster.Hp < monster.MaxHp)
            {
                DrawRectangle(new Rectangle(monsterRect.X, monsterRect.Y - 2,
                    Math.Max(1, monsterRect.Width * monster.Hp / monster.MaxHp), 2), Color.Red);
            }
        }

        int hudTop = dungeon.Height * tileSize;
        DrawRectangle(new Rectangle(0, hudTop, dungeon.Width * tileSize, hudHeight),
            new Color((byte)(12 + feedbackTint.R / 8), (byte)(12 + feedbackTint.G / 8), (byte)(18 + feedbackTint.B / 8)));
        DrawRectangle(new Rectangle(8, hudTop + 8, 120, 8), Color.DarkRed);
        DrawRectangle(new Rectangle(8, hudTop + 8,
            Math.Max(0, 120 * player.Hp / Math.Max(1, player.MaxHp)), 8), Color.Red);
        DrawRectangle(new Rectangle(140, hudTop + 8, 120, 8), Color.DarkBlue);
        DrawRectangle(new Rectangle(140, hudTop + 8,
            Math.Max(0, 120 * experience / Math.Max(1, player.ExperienceToNextLevel)), 8), Color.CornflowerBlue);
        for (int index = 0; index < Math.Min(level, 30); index++)
            DrawRectangle(new Rectangle(8 + index * 8, hudTop + 22, 5, 8), Color.Gold);

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

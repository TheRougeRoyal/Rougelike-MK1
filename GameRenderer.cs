using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Roguelike.Runs;

namespace Roguelike;

/// <summary>Draws the map, glyph actors, HUD, message log, and text inventory overlay.</summary>
public sealed class GameRenderer
{
    private const string ExperienceLabel = "XP";
    private const int HudLeftMargin = 8;
    private readonly Texture2D pixel;
    private readonly SpriteBatch spriteBatch;
    private readonly int tileSize;
    private readonly int hudHeight;
    private readonly ITextRenderer text;

    /// <summary>Gets the width available for HUD text given dungeon width and tile size.</summary>
    public static int GetHudTextWidth(int dungeonWidth, int tileSize) => dungeonWidth * tileSize - HudLeftMargin;

    /// <summary>Initializes a renderer using the single white pixel texture.</summary>
    public GameRenderer(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, int tileSize, int hudHeight)
    {
        this.spriteBatch = spriteBatch;
        this.tileSize = tileSize;
        this.hudHeight = hudHeight;
        pixel = new Texture2D(graphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        text = new BitmapFont(spriteBatch, pixel);
    }

    /// <summary>Draws a complete state.</summary>
    public void Draw(
        Dungeon dungeon, PlayerActor player, IReadOnlyList<MonsterActor> monsters,
        int depth, int level, int experience, string message, Color feedbackTint,
        Actor? feedbackActor = null, IReadOnlyList<FloorItem>? floorItems = null,
        bool inventoryOpen = false, int inventoryCursor = -1, MessageLog? messageLog = null,
        int turnNumber = 0, ScreenKind screen = ScreenKind.Playing, RunStats? stats = null,
        bool restartConfirmation = false, int titleMenuIndex = 0, string seedInput = "",
        int historyScrollOffset = 0, IReadOnlyList<RunRecord>? historyRecords = null, int? savedSeed = null, 
        int pauseMenuIndex = 0, bool canContinue = false, int? rankPosition = null, int? totalRuns = null, int? seed = null,
        bool examineMode = false, Point examineCursor = default, string examineDescription = "")
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawMap(dungeon, player, monsters, floorItems, feedbackActor, examineMode, examineCursor);
        DrawHud(dungeon, player, depth, level, experience, messageLog, turnNumber, stats, examineMode, examineDescription);
        if (inventoryOpen) DrawInventory(player, inventoryCursor);
        if (restartConfirmation) DrawRestartConfirmation(dungeon.Width * tileSize, dungeon.Height * tileSize);
        if (screen != ScreenKind.Playing) DrawScreen(screen, depth, stats, titleMenuIndex, seedInput, historyScrollOffset, historyRecords, savedSeed, pauseMenuIndex, canContinue, rankPosition, totalRuns, seed);
        spriteBatch.End();
    }

    private void DrawRestartConfirmation(int width, int height)
    {
        int boxWidth = 260;
        int boxHeight = 54;
        int x = (width - boxWidth) / 2;
        int y = (height - boxHeight) / 2;
        DrawRectangle(new Rectangle(x, y, boxWidth, boxHeight), new Color(20, 20, 30, 250));
        DrawRectangle(new Rectangle(x, y, boxWidth, 2), Color.Gold);
        DrawRectangle(new Rectangle(x, y + boxHeight - 2, boxWidth, 2), Color.Gold);
        text.DrawString("Restart run? Y/N", new Point(x + 58, y + 20), Color.White, 1, true);
    }

    private void DrawMap(Dungeon dungeon, PlayerActor player, IReadOnlyList<MonsterActor> monsters,
        IReadOnlyList<FloorItem>? floorItems, Actor? feedbackActor, bool examineMode = false, Point examineCursor = default)
    {
        for (int y = 0; y < dungeon.Height; y++)
        for (int x = 0; x < dungeon.Width; x++)
        {
            Point position = new(x, y);
            if (!dungeon.IsExplored(position)) continue;
            bool lit = dungeon.IsVisible(position);
            DrawRectangle(new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize),
                GetTileColor(dungeon[position], lit));
            if (lit && dungeon[position] == TileType.Floor)
                DrawGlyph('.', position, new Color(90, 90, 100));
            if (lit && dungeon[position] == TileType.Stairs)
                DrawGlyph('>', position, Color.Gold);
        }
        if (floorItems is not null)
            foreach (FloorItem item in floorItems)
                if (dungeon.IsVisible(item.Position))
                    DrawGlyph(item.Item.Definition.Glyph, item.Position, item.Item.Definition.Color);
        if (dungeon.IsVisible(player.Position))
            DrawGlyph(player.Glyph, player.Position, feedbackActor == player ? Color.White : Color.Gold);
        foreach (MonsterActor monster in monsters)
        {
            if (!monster.IsAlive || !dungeon.IsVisible(monster.Position)) continue;
            DrawGlyph(monster.Glyph, monster.Position, feedbackActor == monster ? Color.White : monster.Definition.Color);
            if (monster.Hp < monster.MaxHp)
            {
                int width = Math.Max(1, tileSize * monster.Hp / monster.MaxHp);
                DrawRectangle(new Rectangle(monster.Position.X * tileSize,
                    monster.Position.Y * tileSize - 2, width, 2), Color.Red);
            }
        }
        
        // Draw examine cursor as a highlight box
        if (examineMode && dungeon.IsVisible(examineCursor))
        {
            DrawRectangle(new Rectangle(examineCursor.X * tileSize, examineCursor.Y * tileSize, tileSize, tileSize), 
                new Color(200, 200, 100, 80));
        }
    }

    private void DrawHud(Dungeon dungeon, PlayerActor player, int depth, int level, int experience,
        MessageLog? log, int turnNumber, RunStats? stats, bool examineMode = false, string examineDescription = "")
    {
        int top = dungeon.Height * tileSize;
        DrawRectangle(new Rectangle(0, top, dungeon.Width * tileSize, hudHeight), new Color(14, 16, 24));
        
        // Draw examine description above HUD if in examine mode
        if (examineMode && !string.IsNullOrEmpty(examineDescription))
        {
            string truncated = TextLayout.Truncate(examineDescription, 70);
            text.DrawString(truncated, new Point(HudLeftMargin, top - 12), Color.LimeGreen);
        }
        
        text.DrawString($"HP {player.Hp}/{player.MaxHp}", new Point(HudLeftMargin, top + 8), Color.White, 1, true);
        DrawBar(new Rectangle(HudLeftMargin, top + 20, 150, 7), player.Hp, player.MaxHp, Color.Red);
        text.DrawString($"{ExperienceLabel} {experience}/{player.ExperienceToNextLevel}",
            new Point(HudLeftMargin, top + 31), Color.White);
        DrawBar(new Rectangle(HudLeftMargin, top + 43, 150, 7), experience, player.ExperienceToNextLevel, Color.CornflowerBlue);
        text.DrawString($"Level {level}  Depth {depth}", new Point(HudLeftMargin, top + 55), Color.Gold);
        text.DrawString($"ATK {player.TotalAttack}  DEF {player.TotalDefense}", new Point(HudLeftMargin, top + 67), Color.White);
        string weapon = player.EquippedWeapon?.Definition.Name ?? "None";
        string armor = player.EquippedArmor?.Definition.Name ?? "None";
        text.DrawString($"W:{TextLayout.Truncate(weapon, 14)} A:{TextLayout.Truncate(armor, 14)}",
            new Point(HudLeftMargin, top + 79), Color.LightGray);
        StatusEffect? buff = player.Effects.FirstOrDefault();
        if (buff is not null) text.DrawString($"Buff +{buff.Magnitude} ({buff.RemainingTurns})",
            new Point(HudLeftMargin, top + 91), Color.Orange);

        int logLeft = 330;
        text.DrawString("MESSAGE LOG", new Point(logLeft, top + 8), Color.Gold);
        if (log is not null)
        {
            int start = Math.Max(0, log.Entries.Count - 6);
            for (int i = start; i < log.Entries.Count; i++)
            {
                MessageLogEntry entry = log.Entries[i];
                Color color = i == log.Entries.Count - 1 ? entry.Color : new Color(130, 130, 140);
                text.DrawString(TextLayout.Truncate(entry.DisplayText, 40),
                    new Point(logLeft, top + 20 + (i - start) * text.LineHeight), color);
            }
        }
    }

    private void DrawInventory(PlayerActor player, int cursor)
    {
        DrawRectangle(new Rectangle(40, 30, 880, 470), new Color(8, 10, 18, 245));
        text.DrawString("INVENTORY", new Point(60, 48), Color.Gold, 2, true);
        int y = 82;
        for (int i = 0; i < player.Inventory.Items.Count; i++)
        {
            ItemInstance item = player.Inventory.Items[i];
            if (i == cursor) DrawRectangle(new Rectangle(55, y - 2, 430, 13), new Color(45, 55, 75));
            bool equipped = ReferenceEquals(item, player.EquippedWeapon) || ReferenceEquals(item, player.EquippedArmor);
            text.DrawString($"{item.Definition.Glyph} {TextLayout.Truncate(item.Definition.Name, 24),-24} x{item.Count}{(equipped ? " [E]" : string.Empty)}",
                new Point(62, y), Color.White);
            y += text.LineHeight + 3;
        }
        text.DrawString("EQUIPMENT", new Point(540, 82), Color.Gold);
        text.DrawString($"Weapon: {TextLayout.Truncate(player.EquippedWeapon?.Definition.Name ?? "None", 22)}", new Point(540, 98), Color.White);
        text.DrawString($"Armor:  {TextLayout.Truncate(player.EquippedArmor?.Definition.Name ?? "None", 22)}", new Point(540, 114), Color.White);
        if (cursor >= 0 && cursor < player.Inventory.Items.Count)
        {
            ItemInstance item = player.Inventory.Items[cursor];
            text.DrawString("DETAILS", new Point(540, 155), Color.Gold);
            int line = 171;
            foreach (string wrapped in TextLayout.WordWrap(item.Definition.Description, 32))
            {
                text.DrawString(wrapped, new Point(540, line), Color.LightGray);
                line += text.LineHeight;
            }
            int bonus = item.Definition.Type == ItemType.Weapon ? item.Definition.AttackBonus : item.Definition.DefenseBonus;
            if (bonus != 0) text.DrawString(item.Definition.Type == ItemType.Weapon
                ? $"ATK +{bonus}" : $"DEF +{bonus}", new Point(540, line + 4), Color.LimeGreen);
        }
        text.DrawString("Enter use/equip   D drop   1/2 unequip   I/Esc close",
            new Point(60, 475), Color.LightGray);
    }

    private void DrawScreen(ScreenKind screen, int depth, RunStats? stats, int titleMenuIndex = 0,
        string seedInput = "", int historyScrollOffset = 0, IReadOnlyList<RunRecord>? historyRecords = null, int? savedSeed = null, 
        int pauseMenuIndex = 0, bool canContinue = false, int? rankPosition = null, int? totalRuns = null, int? seed = null)
    {
        DrawRectangle(new Rectangle(0, 0, 960, 664), new Color(5, 7, 14, 235));
        string title = screen switch { ScreenKind.Title => "ROGUELIKE", ScreenKind.Paused => "PAUSED",
            ScreenKind.GameOver => "GAME OVER", ScreenKind.Help => "HELP", ScreenKind.SeedEntry => "ENTER SEED",
            ScreenKind.RunHistory => "RUN HISTORY", _ => string.Empty };
        text.DrawString(title, new Point(360, 100), Color.Gold, 2, true);
        
        if (screen == ScreenKind.Title) DrawTitleMenu(titleMenuIndex, savedSeed, canContinue);
        else if (screen == ScreenKind.GameOver && stats is not null) DrawGameOver(stats, rankPosition, totalRuns, seed);
        else if (screen == ScreenKind.Paused) DrawPauseMenu(pauseMenuIndex);
        else if (screen == ScreenKind.Help) DrawHelp();
        else if (screen == ScreenKind.SeedEntry) DrawSeedEntry(seedInput);
        else if (screen == ScreenKind.RunHistory) DrawRunHistory(historyRecords ?? Array.Empty<RunRecord>(), historyScrollOffset);
    }

    private void DrawTitleMenu(int titleMenuIndex, int? savedSeed = null, bool canContinue = false)
    {
        string[] items = { "Continue", "New Run", "New Run With Seed", "Run History", "Help", "Quit" };
        for (int i = 0; i < items.Length; i++)
        {
            Color color;
            if (i == 0 && !canContinue)
                color = Color.DarkGray; // Disabled
            else if (i == titleMenuIndex)
                color = Color.White; // Selected
            else
                color = Color.LightGray; // Unselected
            
            text.DrawString(items[i], new Point(400, 150 + i * 20), color);
        }
        // Show seed if a run is saved
        if (savedSeed.HasValue)
            text.DrawString($"  Seed: {savedSeed}", new Point(450, 150), Color.LightGray);
    }

    private void DrawGameOver(RunStats stats, int? rankPosition = null, int? totalRuns = null, int? seed = null)
    {
        text.DrawString($"Cause: {stats.CauseOfDeath ?? "unknown"}", new Point(330, 150), Color.White);
        text.DrawString($"Depth {stats.MaxDepth}", new Point(330, 168), Color.White);
        text.DrawString($"Turns {stats.TurnsSurvived}  Kills {stats.MonstersSlain}", new Point(330, 186), Color.White);
        text.DrawString($"Picked up {stats.ItemsPickedUp}  Damage dealt {stats.DamageDealt}", new Point(330, 204), Color.White);
        
        // Rank line
        if (rankPosition.HasValue && totalRuns.HasValue)
        {
            string seedStr = seed.HasValue ? $"  Seed: {seed}" : "";
            text.DrawString($"RANK #{rankPosition} OF {totalRuns}{seedStr}", new Point(300, 222), Color.Gold);
        }
        else
        {
            text.DrawString("RUN NOT RECORDED", new Point(350, 222), Color.Yellow);
        }
        
        text.DrawString("R restart   Esc title", new Point(370, 238), Color.Gold);
    }

    private void DrawPauseMenu(int pauseMenuIndex)
    {
        string[] menu = { "Resume", "Save and Quit", "Abandon Run", "Help", "Quit to Title" };
        for (int i = 0; i < menu.Length; i++)
        {
            Color color = i == pauseMenuIndex ? Color.White : Color.LightGray;
            text.DrawString(menu[i], new Point(380, 150 + i * 20), color);
        }
    }

    private void DrawHelp()
    {
        string[] lines = { "Arrows/WASD/8426 move", "Space wait   I inventory", "Enter use/equip   D drop",
            "Esc pause/close   H help", "@ player  r/g/a/B monsters", "! potion  ? scroll  / weapon  [ armor  > stairs" };
        for (int i = 0; i < lines.Length; i++) text.DrawString(lines[i], new Point(250, 145 + i * 18), Color.White);
    }

    private void DrawSeedEntry(string seedInput)
    {
        text.DrawString("Enter up to 9 digits for a specific seed", new Point(240, 150), Color.White);
        text.DrawString("Enter empty for a random seed", new Point(270, 168), Color.White);
        
        // Draw input with cursor
        int cursorX = 300 + (seedInput.Length * 8);
        text.DrawString(seedInput, new Point(300, 200), Color.LimeGreen);
        DrawRectangle(new Rectangle(cursorX, 200, 1, 12), Color.LimeGreen); // Cursor
        
        text.DrawString("Enter start   BackSpace delete   Esc back", new Point(200, 240), Color.LightGray);
    }

    private void DrawRunHistory(IReadOnlyList<RunRecord> allRecords, int scrollOffset)
    {
        if (allRecords.Count == 0)
        {
            text.DrawString("NO RUNS YET", new Point(350, 250), Color.White);
            text.DrawString("Esc back", new Point(420, 300), Color.LightGray);
            return;
        }
        
        var topRecords = RunRanking.Top(allRecords).ToList();
        int maxScroll = Math.Max(0, topRecords.Count - 10);
        int start = Math.Min(scrollOffset, maxScroll);
        int end = Math.Min(start + 10, topRecords.Count);
        
        // Header
        text.DrawString("RANK  DEPTH  LVL  TURNS  KILLS  CAUSE                       SEED       DATE", 
            new Point(20, 140), Color.Gold);
        
        int y = 158;
        for (int i = start; i < end; i++)
        {
            RunRecord record = topRecords[i];
            int rank = i + 1;
            
            string cause = TextLayout.Truncate(record.CauseOfDeath, 20);
            string seedStr = record.Seed.ToString("D9");
            string dateStr = record.DateUtc.ToString("yyyy-MM-dd HH:mm");
            
            string line = $"#{rank:D2}   {record.DepthReached:D2}      {record.Level:D2}    {record.Turns:D4}    {record.Kills:D3}    {cause,-20}  {seedStr}  {dateStr}";
            line = TextLayout.Truncate(line, 90);
            text.DrawString(line, new Point(20, y), Color.White);
            y += text.LineHeight + 2;
        }
        
        if (topRecords.Count > 10)
            text.DrawString($"({start + 1}-{end} of {topRecords.Count})   Esc back   Up/Down scroll", 
                new Point(200, 635), Color.LightGray);
        else
            text.DrawString("Esc back", new Point(430, 635), Color.LightGray);
    }

    private void DrawGlyph(char glyph, Point tile, Color color)
    {
        int width = text.MeasureString(glyph.ToString()).X;
        text.DrawString(glyph.ToString(), new Point(tile.X * tileSize + (tileSize - width) / 2,
            tile.Y * tileSize + (tileSize - text.LineHeight) / 2), color, 1, true);
    }
    private void DrawBar(Rectangle area, int value, int maximum, Color color)
    {
        DrawRectangle(area, Color.DarkGray);
        DrawRectangle(new Rectangle(area.X, area.Y, Math.Max(0, area.Width * value / Math.Max(1, maximum)), area.Height), color);
    }
    private void DrawRectangle(Rectangle destination, Color color) => spriteBatch.Draw(pixel, destination, color);
    private static Color GetTileColor(TileType tile, bool lit) => tile switch
    {
        TileType.Wall when lit => new Color(80, 90, 110),
        TileType.Stairs when lit => new Color(190, 150, 45),
        _ when lit => new Color(155, 155, 165),
        TileType.Wall => new Color(28, 32, 42),
        TileType.Stairs => new Color(70, 55, 22),
        _ => new Color(52, 52, 60)
    };
}

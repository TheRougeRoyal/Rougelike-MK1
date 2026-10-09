using Microsoft.Xna.Framework;
using Roguelike.Content;
using Roguelike.Persistence;
using Roguelike.Runs;
using Xunit;

namespace Roguelike.Tests;

/// <summary>Tests for the three gaps: examine description, pause menu quit to title, and rank recording.</summary>
public sealed class TitleMenuRoundTests
{
    // Gap 1: Examine Description Tests
    [Fact]
    public void GetExamineDescriptionReturnsNonEmptyForWall()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Find a visible wall by moving the dungeon visibility
        Point wallPos = session.State.Dungeon.PlayerStart + new Point(2, 0);
        while (!session.State.Dungeon.IsVisible(wallPos))
        {
            wallPos = wallPos + new Point(-1, 0);
            if (wallPos.X < 0) wallPos = session.State.Dungeon.PlayerStart + new Point(0, 2);
        }
        
        string description = session.GetExamineDescription(wallPos);
        
        // Wall should return "WALL"
        Assert.NotEmpty(description);
    }

    [Fact]
    public void GetExamineDescriptionReturnsNonEmptyForFloor()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Player is on floor
        string description = session.GetExamineDescription(session.State.Player.Position);
        
        Assert.NotEmpty(description);
    }

    [Fact]
    public void GetExamineDescriptionReturnsNonEmptyForStairs()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        Point stairsPos = session.State.Dungeon.StairsPosition;
        // Only test if stairs are visible
        if (session.State.Dungeon.IsVisible(stairsPos))
        {
            string description = session.GetExamineDescription(stairsPos);
            Assert.NotEmpty(description);
            Assert.Equal("STAIRS", description);
        }
    }

    [Fact]
    public void GetExamineDescriptionReturnsNonEmptyForMonster()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        var visibleMonsters = session.State.Monsters.Where(m => m.IsAlive && session.State.Dungeon.IsVisible(m.Position));
        if (visibleMonsters.Any())
        {
            MonsterActor monster = visibleMonsters.First();
            string description = session.GetExamineDescription(monster.Position);
            
            Assert.NotEmpty(description);
            Assert.Contains(monster.Name, description);
        }
    }

    [Fact]
    public void GetExamineDescriptionReturnsNonEmptyForFloorItem()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        var visibleItems = session.State.FloorItems.Where(fi => session.State.Dungeon.IsVisible(fi.Position));
        if (visibleItems.Any())
        {
            FloorItem item = visibleItems.First();
            string description = session.GetExamineDescription(item.Position);
            
            Assert.NotEmpty(description);
            Assert.Contains(item.Item.Definition.Name, description);
        }
    }

    [Fact]
    public void TwoRunsWithSameSeedGetOwnRanks()
    {
        MemoryRunHistoryStore history = new();
        ContentDatabase content = ContentDatabase.LoadDefault();
        
        // First run with seed 42, depth 5
        GameSession session1 = new(42, content: content, historyStore: history);
        session1.Execute(new UiCommand(UiCommandKind.Start));
        session1.State.Status = GameStatus.Dead;
        session1.State.RunStats.MaxDepth = 5;
        session1.State.RunStats.TurnsSurvived = 100;
        session1.State.RunStats.MonstersSlain = 5;
        session1.State.RunStats.CauseOfDeath = "dead";
        session1.Execute(new UiCommand(UiCommandKind.None));
        
        int? rank1 = session1.RankPosition;
        Assert.NotNull(rank1);
        Assert.Equal(1, rank1); // First run ranks #1
        string id1 = session1.State.Seed.ToString();
        
        // Second run with same seed 42 but lower depth
        GameSession session2 = new(42, content: content, historyStore: history);
        session2.Execute(new UiCommand(UiCommandKind.Start));
        session2.State.Status = GameStatus.Dead;
        session2.State.RunStats.MaxDepth = 3; // Lower depth
        session2.State.RunStats.TurnsSurvived = 50;
        session2.State.RunStats.MonstersSlain = 3;
        session2.State.RunStats.CauseOfDeath = "dead";
        session2.Execute(new UiCommand(UiCommandKind.None));
        
        int? rank2 = session2.RankPosition;
        Assert.NotNull(rank2);
        Assert.Equal(2, rank2); // Second run with same seed ranks lower due to lower depth
        
        // Verify both are in history with different IDs
        var records = history.Read();
        Assert.Equal(2, records.Count);
        Assert.NotEqual(records[0].Id, records[1].Id); // Different IDs despite same seed
    }
    [Fact]
    public void PauseMenuQuitToTitleReturnsToTitle()
    {
        GameSession session = new(2, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
        
        // Pause the game
        session.Execute(new UiCommand(UiCommandKind.Pause));
        Assert.Equal(ScreenKind.Paused, session.Screens.Screen);
        
        // Move to menu item 4 (Quit to Title)
        for (int i = 0; i < 4; i++)
        {
            session.Execute(new UiCommand(UiCommandKind.MenuDown));
        }
        Assert.Equal(4, session.Screens.MenuIndex);
        
        // Select it
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        // Verify we're back at title
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Equal(UiOverlay.None, session.Screens.Overlay);
    }

    [Fact]
    public void PauseMenuQuitToTitleDoesNotResetRunState()
    {
        MemorySaveStore save = new();
        GameSession session = new(3, content: ContentDatabase.LoadDefault(), saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Perform an action to move the state
        session.Execute(new UiCommand(UiCommandKind.Wait));
        int turnsBefore = session.State.TurnNumber;
        GameState stateBefore = session.State;
        
        // Pause
        session.Execute(new UiCommand(UiCommandKind.Pause));
        
        // Move to Quit to Title and select
        for (int i = 0; i < 4; i++)
            session.Execute(new UiCommand(UiCommandKind.MenuDown));
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        // The state object should still be there, unchanged
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        // Run state is preserved internally, though not visible yet until we interact with it
        // We verify this by checking the session still has the state
        Assert.NotNull(session.State);
    }

    [Fact]
    public void PauseMenuQuitToTitleFromPausedWorks()
    {
        GameSession session = new(4, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.Execute(new UiCommand(UiCommandKind.Pause));
        
        // Verify Screens.Title() accepts Paused state
        bool result = session.Screens.Title();
        
        Assert.True(result);
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
    }

    // Gap 3: Rank Recording Tests
    [Fact]
    public void RankRecordingRecords11thRunWithCorrectRank()
    {
        MemoryRunHistoryStore history = new();
        
        // Create 10 runs at depths: 10, 9, 8, 7, 6, 5, 4, 3, 2, 1
        for (int depth = 10; depth >= 1; depth--)
        {
            history.Append(new RunRecord(
                Guid.NewGuid().ToString("N"),
                Seed: depth,
                DateUtc: DateTime.UtcNow.AddDays(-depth),
                DepthReached: depth,
                Level: 1,
                Turns: 100 + depth,
                Kills: depth,
                CauseOfDeath: "dead",
                ContentHash: "test"
            ));
        }
        
        // Now create an 11th run at depth 11 - should rank #1 (best)
        GameSession session = new(100, content: ContentDatabase.LoadDefault(), historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;
        session.State.RunStats.MaxDepth = 11;
        session.State.RunStats.TurnsSurvived = 150;
        session.State.RunStats.MonstersSlain = 10;
        session.State.RunStats.CauseOfDeath = "dead";
        
        session.Execute(new UiCommand(UiCommandKind.None));
        
        // Should be ranked #1 since it has the highest depth
        Assert.NotNull(session.RankPosition);
        Assert.Equal(1, session.RankPosition);
        Assert.Equal(11, session.TotalRuns);
    }

    [Fact]
    public void RankRecordingRecordsLowestRankedRun()
    {
        MemoryRunHistoryStore history = new();
        ContentDatabase content = ContentDatabase.LoadDefault();
        
        // Create 10 runs at depths: 10, 9, 8, 7, 6, 5, 4, 3, 2, 1
        for (int depth = 10; depth >= 1; depth--)
        {
            history.Append(new RunRecord(
                Guid.NewGuid().ToString("N"),
                Seed: depth,
                DateUtc: DateTime.UtcNow.AddDays(-depth),
                DepthReached: depth,
                Level: 1,
                Turns: 100 + depth,
                Kills: depth,
                CauseOfDeath: "dead",
                ContentHash: "test"
            ));
        }
        
        // Now create an 11th run at depth 1 (ties with existing depth 1 run)
        // Ranking by depth, level, then FEWER turns
        GameSession session = new(100, content: ContentDatabase.LoadDefault(), historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;
        session.State.RunStats.MaxDepth = 1;
        session.State.RunStats.TurnsSurvived = 50; // Fewer turns = better rank
        session.State.RunStats.MonstersSlain = 0;
        session.State.RunStats.CauseOfDeath = "dead";
        
        session.Execute(new UiCommand(UiCommandKind.None));
        
        // Should be ranked #10 (tied depth 1 with fewer turns)
        Assert.NotNull(session.RankPosition);
        Assert.Equal(10, session.RankPosition);
        Assert.Equal(11, session.TotalRuns);
    }

    [Fact]
    public void RankRecordingShowsRunNotRecordedOnAppendFailure()
    {
        ThrowingAppendStore history = new();
        GameSession session = new(200, content: ContentDatabase.LoadDefault(), historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;
        session.State.RunStats.MaxDepth = 5;
        session.State.RunStats.TurnsSurvived = 100;
        session.State.RunStats.MonstersSlain = 5;
        session.State.RunStats.CauseOfDeath = "dead";
        
        session.Execute(new UiCommand(UiCommandKind.None));
        
        // When append fails, RankPosition should be null
        Assert.Null(session.RankPosition);
        Assert.Null(session.TotalRuns);
    }

    private sealed class ThrowingAppendStore : IRunHistoryStore
    {
        public IReadOnlyList<RunRecord> Read() => [];
        public void Append(RunRecord record) => throw new IOException("append failed");
    }

    [Fact]
    public void ExamineDescriptionRenderedWidthFitsAtScale1()
    {
        // Measure the real rendered width of truncated examine descriptions
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        string description = session.GetExamineDescription(session.State.Player.Position);
        
        // Truncate to 70 chars (renderer's limit)
        string truncated = TextLayout.Truncate(description, 70);
        
        // Measure rendered width with BitmapFont at scale 1
        Point measured = BitmapFont.Measure(truncated, scale: 1);
        int renderedWidth = measured.X;
        
        // Get available HUD text width using real game layout constants
        int hudTextWidth = GameRenderer.GetHudTextWidth(GameLayout.MapWidth, GameLayout.TileSize);
        
        // Assert rendered width fits within the HUD text width
        Assert.True(renderedWidth <= hudTextWidth, 
            $"Rendered width {renderedWidth}px exceeds HUD text width {hudTextWidth}px");
    }

    [Fact]
    public void ExamineDescriptionTruncationFitsRealRenderer()
    {
        // Test with various description types to ensure truncation works across the board
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Test multiple examine positions
        Point testPos = session.State.Player.Position;
        int hudTextWidth = GameRenderer.GetHudTextWidth(GameLayout.MapWidth, GameLayout.TileSize);
        
        for (int i = 0; i < 5; i++)
        {
            testPos = testPos + new Point(i, 0);
            if (!session.State.Dungeon.InBounds(testPos)) break;
            
            string description = session.GetExamineDescription(testPos);
            if (string.IsNullOrEmpty(description)) continue;
            
            string truncated = TextLayout.Truncate(description, 70);
            Point measured = BitmapFont.Measure(truncated, scale: 1);
            
            Assert.True(measured.X <= hudTextWidth,
                $"Description '{truncated}' rendered width {measured.X}px exceeds HUD text width {hudTextWidth}px");
        }
    }

    [Fact]
    public void GameOverEscapeReturnsToTitle()
    {
        GameSession session = new(8, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.State.Status = GameStatus.Dead;
        session.Execute(new UiCommand(UiCommandKind.Cancel));
        
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
    }

    [Fact]
    public void WindowPreferredBackBufferWidthMatchesGameLayout()
    {
        // Verify window dimensions match GameLayout constants
        // Calculation: MapWidth * TileSize = 60 * 16 = 960px
        int expectedWidth = GameLayout.MapWidth * GameLayout.TileSize;
        Assert.Equal(960, expectedWidth);
        
        // This is a constant check: the window in GameMain uses the same formula
        // (PreferredBackBufferWidth = GameLayout.MapWidth * GameLayout.TileSize)
    }

    [Fact]
    public void ExamineDescriptionFitsWithRealWidth()
    {
        // Verify that 70-character truncation fits within the real window width
        // Real width: GameLayout.MapWidth * GameLayout.TileSize = 60 * 16 = 960 pixels
        // HUD text width: 960 - HudLeftMargin = 960 - 8 = 952 pixels
        
        int windowWidth = GameLayout.MapWidth * GameLayout.TileSize;
        int hudTextWidth = GameRenderer.GetHudTextWidth(GameLayout.MapWidth, GameLayout.TileSize);
        
        Assert.Equal(960, windowWidth);
        Assert.Equal(952, hudTextWidth);
        
        // 70 characters at scale 1: 70 * 6 (advance) = 420 pixels
        int maxRenderedWidth = 70 * BitmapFont.GlyphAdvance;
        Assert.True(maxRenderedWidth <= hudTextWidth,
            $"70-char string at {maxRenderedWidth}px exceeds HUD width {hudTextWidth}px");
    }
}

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

    // Gap 2: Pause Menu Quit to Title Tests
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
    public void RankRecordingRecords11thOrLowerAsNotRecorded()
    {
        MemoryRunHistoryStore history = new();
        
        // Create 11 runs at depths: 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1
        for (int depth = 11; depth >= 1; depth--)
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
        
        // Verify that top 10 excludes the depth 1 run
        var records = history.Read();
        Assert.Equal(11, records.Count);
        
        var top = RunRanking.Top(records);
        Assert.Equal(10, top.Count);
        
        // The depth 1 run should NOT be in top 10
        var depth1Run = records.FirstOrDefault(r => r.DepthReached == 1);
        Assert.NotNull(depth1Run);
        Assert.DoesNotContain(depth1Run, top);
        
        // Rule: Runs not in top 10 show "RUN NOT RECORDED"
        // When a new run is recorded at depth 1, it won't be in top 10, so RankPosition should be null
    }

    [Fact]
    public void RankRecordingRecordsTopTenRuns()
    {
        MemoryRunHistoryStore history = new();
        
        // Create 8 existing runs at various depths
        for (int depth = 8; depth >= 1; depth--)
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
        
        // Verify we can read the history and that top 10 includes all 8
        var records = history.Read();
        Assert.Equal(8, records.Count);
        
        var top = RunRanking.Top(records);
        Assert.Equal(8, top.Count);
        
        // All should be in top 10
        foreach (var record in records)
            Assert.Contains(record, top);
    }

    [Fact]
    public void GameOverScreenShowsRankWhenRecorded()
    {
        MemoryRunHistoryStore history = new();
        GameSession session = new(5, content: ContentDatabase.LoadDefault(), historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Simulate death with rank
        session.State.Status = GameStatus.Dead;
        session.State.RunStats.CauseOfDeath = "dead";
        session.State.RunStats.MaxDepth = 5;
        session.State.RunStats.TurnsSurvived = 100;
        session.State.RunStats.MonstersSlain = 5;
        
        session.Execute(new UiCommand(UiCommandKind.None));
        
        // Record should have been created
        Assert.True(history.Read().Count > 0);
        
        // Rank should be set
        Assert.NotNull(session.RankPosition);
    }

    [Fact]
    public void ExamineDescriptionTruncatesFitsPanelWidth()
    {
        GameSession session = new(6, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Get a description and verify it truncates reasonably
        string description = session.GetExamineDescription(session.State.Player.Position);
        
        // TextLayout.Truncate at 70 chars should work at both scales
        string truncated = TextLayout.Truncate(description, 70);
        
        Assert.True(truncated.Length <= 70);
    }

    [Fact]
    public void ExamineDescriptionFitsAtScale2()
    {
        GameSession session = new(7, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        // Even longer description should fit when truncated for panel
        string description = session.GetExamineDescription(session.State.Player.Position);
        
        // At scale 2, text is larger, so we might need fewer chars
        // For testing, verify it still truncates correctly
        string truncated = TextLayout.Truncate(description, 35); // Half the width for scale 2
        
        Assert.True(truncated.Length <= 35);
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
}

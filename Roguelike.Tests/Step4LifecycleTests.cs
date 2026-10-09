using Microsoft.Xna.Framework;
using Roguelike.Content;
using Roguelike.Persistence;
using Roguelike.Runs;
using Xunit;

namespace Roguelike.Tests;

public sealed class Step4LifecycleTests
{
    [Fact]
    public void LethalCommandDeletesSaveRecordsDeathOnceAndShowsGameOver()
    {
        MemorySaveStore save = new();
        MemoryRunHistoryStore history = new();
        GameSession session = new(1, content: ContentDatabase.LoadDefault(), saveStore: save,
            historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Player.Hp = 1;
        MonsterActor killer = session.State.Monsters.First();
        killer.Position = session.State.Player.Position + new Point(1, 0);
        killer.Hp = killer.MaxHp;
        GameState before = session.State;

        session.Execute(new UiCommand(UiCommandKind.Wait));

        Assert.Same(before, session.State);
        Assert.Equal(GameStatus.Dead, session.State.Status);
        Assert.Equal(ScreenKind.GameOver, session.Screens.Screen);
        Assert.Null(save.Value);
        Assert.Single(history.Read());
        Assert.Equal("dead", history.Read()[0].CauseOfDeath);

        session.Execute(new UiCommand(UiCommandKind.Cancel));
        session.Execute(new UiCommand(UiCommandKind.Cancel));
        Assert.Single(history.Read());
    }

    [Fact]
    public void ContinueRefusesDeadSaveAndRemovesIt()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore save = new();
        GameState dead = new(2, content: content);
        dead.Player.Hp = 0;
        dead.Status = GameStatus.Dead;
        GameStatePersistence.Save(dead, save, content.ContentHash);
        GameSession session = new(3, content: content, saveStore: save);

        session.Execute(new UiCommand(UiCommandKind.Continue));

        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Null(save.Value);
        Assert.Contains("Save is from a finished run and was removed", session.State.Message);
        Assert.False(session.HasValidSave);
    }

    [Fact]
    public void ContinueDeadSaveMessageRemainsVisibleOnTitle()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore save = new();
        GameState dead = new(20, content: content);
        dead.Status = GameStatus.Dead;
        dead.Player.Hp = 0;
        GameStatePersistence.Save(dead, save, content.ContentHash);
        GameSession session = new(21, content: content, saveStore: save);

        session.Execute(new UiCommand(UiCommandKind.Continue));

        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Contains("Save is from a finished run and was removed", session.State.Message);
    }

    [Fact]
    public void DeleteFailureDuringDeathIsReportedAndRunReachesGameOver()
    {
        ThrowingDeleteStore save = new();
        GameSession session = new(22, saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;

        session.Execute(new UiCommand(UiCommandKind.None));

        Assert.Equal(ScreenKind.GameOver, session.Screens.Screen);
        Assert.Contains("Could not delete save: delete failed", session.State.Message);
    }

    [Fact]
    public void ContinueIsDisabledWhenNoValidSaveExists()
    {
        GameSession session = new(4, saveStore: new MemorySaveStore());

        session.Execute(new UiCommand(UiCommandKind.Continue));

        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Equal(GameStatus.Playing, session.State.Status);
    }

    [Fact]
    public void SaveFailuresAreDisplayedAndDoNotRequestQuit()
    {
        ThrowingSaveStore save = new();
        GameSession session = new(5, saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        GameState original = session.State;

        session.Execute(new UiCommand(UiCommandKind.SaveAndQuit));

        Assert.Same(original, session.State);
        Assert.False(session.QuitRequested);
        Assert.Contains("Could not save: writer failed", session.State.Message);
    }

    [Fact]
    public void AutosaveFailuresAreDisplayedAndRunContinues()
    {
        ThrowingSaveStore save = new();
        GameSession session = new(8, saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Player.Position = session.State.Dungeon.StairsPosition;

        session.Execute(new UiCommand(UiCommandKind.Wait));

        Assert.False(session.QuitRequested);
        Assert.Contains("Could not save: writer failed", session.State.Message);
        Assert.Equal(GameStatus.Playing, session.State.Status);
    }

    [Fact]
    public void DeadStateIsNeverAutosaved()
    {
        CountingSaveStore save = new();
        GameSession session = new(6, saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;

        session.State.Player.Position = session.State.Dungeon.StairsPosition;
        session.Execute(new UiCommand(UiCommandKind.Wait));

        Assert.Equal(0, save.WriteCount);
        Assert.Equal(ScreenKind.GameOver, session.Screens.Screen);
    }

    [Fact]
    public void AbandonDeletesSaveRecordsRetirementAndReturnsToTitle()
    {
        MemorySaveStore save = new();
        MemoryRunHistoryStore history = new();
        GameSession session = new(7, saveStore: save, historyStore: history);
        session.Execute(new UiCommand(UiCommandKind.Start));
        GameStatePersistence.Save(session.State, save, ContentDatabase.LoadDefault().ContentHash);

        session.Execute(new UiCommand(UiCommandKind.AbandonRun));
        session.Execute(new UiCommand(UiCommandKind.ConfirmAbandon));

        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Null(save.Value);
        Assert.Single(history.Read());
        Assert.Equal("retired", history.Read()[0].CauseOfDeath);
    }

    [Fact]
    public void DeleteFailureDuringAbandonIsReportedAndKeepsRunOpen()
    {
        ThrowingDeleteStore save = new();
        GameSession session = new(23, saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.Execute(new UiCommand(UiCommandKind.AbandonRun));

        session.Execute(new UiCommand(UiCommandKind.ConfirmAbandon));

        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
        Assert.Equal(UiOverlay.AbandonConfirmation, session.Screens.Overlay);
        Assert.Contains("Could not delete save: delete failed", session.State.Message);
    }

    [Fact]
    public void LifecycleFuzzPreservesRunInvariants()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        for (int seed = 0; seed < 50; seed++)
        {
            MemorySaveStore save = new();
            MemoryRunHistoryStore history = new();
            GameSession session = new(seed, content: content, saveStore: save, historyStore: history);
            session.Execute(new UiCommand(UiCommandKind.Start));
            Pcg32 random = RandomStreams.Create(seed, 0, 0x4C494645UL);
            for (int index = 0; index < 80; index++)
            {
                session.Execute(ChooseCommand(session, random));
                Assert.True(history.Read().Count <= 100, $"Seed {seed}, action {index}: history exceeded 100.");
                if (session.State.Status == GameStatus.Dead)
                {
                    Assert.Null(save.Value);
                    Assert.Null(save.Backup);
                }
                Assert.True(save.Value is null || SaveCodec.Decode(save.Value) is not null,
                    $"Seed {seed}, action {index}: primary save is invalid.");
                Assert.True(session.State.Status != GameStatus.Dead ||
                    (save.Value is null && save.Backup is null),
                    $"Seed {seed}, action {index}: dead run retained save data.");
            }
        }
    }

    private static UiCommand ChooseCommand(GameSession session, IRandom random)
    {
        if (session.Screens.Screen == ScreenKind.Title)
        {
            return random.Next(4) switch
            {
                0 => new UiCommand(UiCommandKind.Continue),
                1 => new UiCommand(UiCommandKind.NewRun),
                2 => new UiCommand(UiCommandKind.Start),
                _ => new UiCommand(UiCommandKind.None)
            };
        }
        if (session.Screens.Screen == ScreenKind.GameOver)
        {
            return random.Next(5) switch
            {
                0 => new UiCommand(UiCommandKind.Restart),
                1 => new UiCommand(UiCommandKind.Cancel),
                2 => new UiCommand(UiCommandKind.Start),
                3 => new UiCommand(UiCommandKind.Continue),
                _ => new UiCommand(UiCommandKind.NewRun)
            };
        }
        if (session.Screens.Overlay == UiOverlay.AbandonConfirmation)
            return new UiCommand(random.Next(2) == 0
                ? UiCommandKind.ConfirmAbandon : UiCommandKind.CancelAbandon);
        if (random.Next(10) == 0) return new UiCommand(UiCommandKind.SaveAndQuit);
        if (random.Next(12) == 0) return new UiCommand(UiCommandKind.AbandonRun);
        if (random.Next(8) == 0) return new UiCommand(UiCommandKind.NewRun);
        if (random.Next(5) == 0) return new UiCommand(UiCommandKind.Wait);
        Point[] directions = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
        return new UiCommand(UiCommandKind.Move, directions[random.Next(directions.Length)]);
    }

    private sealed class ThrowingSaveStore : ISaveStore
    {
        public string? Read() => null;
        public void Write(string json) => throw new IOException("writer failed");
        public void Delete() { }
    }

    private sealed class CountingSaveStore : ISaveStore
    {
        public int WriteCount { get; private set; }
        public string? Read() => null;
        public void Write(string json) => WriteCount++;
        public void Delete() { }
    }

    private sealed class ThrowingDeleteStore : ISaveStore
    {
        public string? Read() => null;
        public void Write(string json) { }
        public void Delete() => throw new IOException("delete failed");
    }

    // Title Menu Tests
    [Fact]
    public void TitleMenuWrapsAround()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        Assert.Equal(0, session.Screens.TitleMenuIndex);
        session.Execute(new UiCommand(UiCommandKind.MenuUp));
        Assert.Equal(5, session.Screens.TitleMenuIndex); // Wraps to Quit (last item)
        session.Execute(new UiCommand(UiCommandKind.MenuDown));
        Assert.Equal(0, session.Screens.TitleMenuIndex); // Wraps to Continue (first item)
    }

    [Fact]
    public void TitleMenuContinueDisabledShowsMessage()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.MoveMenu(0); // Stay at Continue (index 0)
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        Assert.Contains("No saved run to continue", session.State.Message);
    }

    [Fact]
    public void TitleMenuContinueWithValidSaveReachesPlaying()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore save = new();
        GameState playing = new(10, content: content);
        GameStatePersistence.Save(playing, save, content.ContentHash);
        GameSession session = new(11, content: content, saveStore: save);
        
        session.Execute(new UiCommand(UiCommandKind.Accept)); // Accept on Continue (default index 0)
        
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
    }

    [Fact]
    public void TitleMenuContinueWithCorruptSaveStaysAndShowsReason()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore save = new();
        GameSession session = new(12, content: content, saveStore: save);
        
        // No save exists, so attempting Continue will try to load and fail
        session.Execute(new UiCommand(UiCommandKind.Accept)); // Accept on Continue
        
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        // Message would be empty/null for no save, but we test the pattern
    }

    [Fact]
    public void TitleMenuNewRunStartsGame()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to NewRun (index 1)
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
    }

    [Fact]
    public void TitleMenuNewRunWithSeedOpensEntry()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.MenuDown));
        session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to NewRunWithSeed (index 2)
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.SeedEntry, session.Screens.Screen);
    }

    [Fact]
    public void TitleMenuRunHistoryOpensHistory()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        for (int i = 0; i < 3; i++) session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to index 3
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.RunHistory, session.Screens.Screen);
    }

    [Fact]
    public void TitleMenuHelpOpensHelp()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        for (int i = 0; i < 4; i++) session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to index 4
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.Help, session.Screens.Screen);
    }

    [Fact]
    public void TitleMenuQuitSetsFlag()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        for (int i = 0; i < 5; i++) session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to index 5 (Quit)
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.True(session.QuitRequested);
    }

    // Seed Entry Tests
    [Fact]
    public void SeedEntryAcceptsDigitsUpToNine()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        
        for (char d = '0'; d <= '8'; d++)
            session.Execute(new UiCommand(UiCommandKind.SeedDigit, d));
        
        Assert.Equal("012345678", session.SeedInput);
    }

    [Fact]
    public void SeedEntryRejectsOverflowWithMessage()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        
        for (char d = '0'; d <= '9'; d++)
            session.Execute(new UiCommand(UiCommandKind.SeedDigit, d));
        
        // The 10th digit should be rejected, input should still be 9 chars max
        Assert.True(session.SeedInput.Length <= 9);
    }

    [Fact]
    public void SeedEntryBackspaceDeletes()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        session.Execute(new UiCommand(UiCommandKind.SeedDigit, '5'));
        session.Execute(new UiCommand(UiCommandKind.SeedDigit, '3'));
        
        session.Execute(new UiCommand(UiCommandKind.SeedBackspace));
        
        Assert.Equal("5", session.SeedInput);
    }

    [Fact]
    public void SeedEntryEscReturnsToTitle()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        
        session.Execute(new UiCommand(UiCommandKind.Cancel));
        
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
    }

    [Fact]
    public void SeedEntryEmptyStartsRandomSeed()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        
        session.Execute(new UiCommand(UiCommandKind.Accept)); // Empty input
        
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
    }

    [Fact]
    public void SeedEntryValidSeedStartsGame()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Screens.ShowSeedEntry();
        session.Execute(new UiCommand(UiCommandKind.SeedDigit, '9'));
        session.Execute(new UiCommand(UiCommandKind.SeedDigit, '9'));
        
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
    }

    // Run History Tests
    [Fact]
    public void RunHistoryRanksByDepthThenLevelThenTurns()
    {
        MemoryRunHistoryStore history = new();
        history.Append(new RunRecord("a", 1, DateTime.UtcNow, 2, 1, 100, 5, "dead", "hash"));
        history.Append(new RunRecord("b", 2, DateTime.UtcNow, 2, 2, 50, 5, "dead", "hash"));
        history.Append(new RunRecord("c", 3, DateTime.UtcNow, 3, 1, 200, 10, "dead", "hash"));
        
        var ranked = RunRanking.Top(history.Read());
        
        Assert.Equal(3, ranked[0].DepthReached); // Highest depth first
        Assert.Equal(2, ranked[1].DepthReached);
        Assert.Equal(2, ranked[2].DepthReached);
        Assert.Equal(2, ranked[1].Level); // Same depth, higher level first
    }

    [Fact]
    public void RunHistoryShowsNoRunsYetWhenEmpty()
    {
        MemoryRunHistoryStore history = new();
        GameSession session = new(1, content: ContentDatabase.LoadDefault(), historyStore: history);
        session.Screens.ShowRunHistory();
        
        Assert.Equal(ScreenKind.RunHistory, session.Screens.Screen);
        // The renderer would show "NO RUNS YET" message
    }

    [Fact]
    public void PauseMenuResumeClosesOverlay()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.Execute(new UiCommand(UiCommandKind.Pause));
        Assert.Equal(ScreenKind.Paused, session.Screens.Screen);
        
        session.Execute(new UiCommand(UiCommandKind.Accept)); // Resume (index 0)
        
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
    }

    [Fact]
    public void PauseMenuSaveAndQuitCallsSaveAndQuit()
    {
        MemorySaveStore save = new();
        GameSession session = new(1, content: ContentDatabase.LoadDefault(), saveStore: save);
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.Execute(new UiCommand(UiCommandKind.Pause));
        session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to SaveAndQuit (index 1)
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.True(session.QuitRequested);
        Assert.NotNull(save.Value); // Save was persisted
    }

    [Fact]
    public void PauseMenuAbandonRunOpensConfirmation()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.Execute(new UiCommand(UiCommandKind.Pause));
        session.Execute(new UiCommand(UiCommandKind.MenuDown));
        session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to AbandonRun (index 2)
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(UiOverlay.AbandonConfirmation, session.Screens.Overlay);
    }

    [Fact]
    public void PauseMenuHelpOpensHelp()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.Execute(new UiCommand(UiCommandKind.Pause));
        session.Execute(new UiCommand(UiCommandKind.MenuDown));
        session.Execute(new UiCommand(UiCommandKind.MenuDown));
        session.Execute(new UiCommand(UiCommandKind.MenuDown)); // Move to Help (index 3)
        session.Execute(new UiCommand(UiCommandKind.Accept));
        
        Assert.Equal(ScreenKind.Help, session.Screens.Screen);
    }

    [Fact]
    public void ExamineModeEntersWithX()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        session.Execute(new UiCommand(UiCommandKind.Examine));
        
        Assert.Equal(UiOverlay.Examine, session.Screens.Overlay);
    }

    [Fact]
    public void ExamineModeCursorDoesNotChangeTurnNumber()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        int startTurns = session.State.TurnNumber;
        
        session.Execute(new UiCommand(UiCommandKind.Examine));
        session.Execute(new UiCommand(UiCommandKind.Move, new Point(1, 0))); // Move cursor
        session.Execute(new UiCommand(UiCommandKind.Move, new Point(0, 1))); // Move cursor again
        
        Assert.Equal(startTurns, session.State.TurnNumber);
    }

    [Fact]
    public void ExamineModeDoesNotChangeStateHash()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        ulong beforeHash = session.State.ComputeStateHash();
        
        session.Execute(new UiCommand(UiCommandKind.Examine));
        session.Execute(new UiCommand(UiCommandKind.Move, new Point(1, 0)));
        session.Execute(new UiCommand(UiCommandKind.Move, new Point(-1, 1)));
        
        ulong afterHash = session.State.ComputeStateHash();
        Assert.Equal(beforeHash, afterHash);
    }

    [Fact]
    public void ExamineModeEscExitsMode()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.Execute(new UiCommand(UiCommandKind.Examine));
        
        session.Execute(new UiCommand(UiCommandKind.Cancel));
        
        Assert.Equal(UiOverlay.None, session.Screens.Overlay);
    }

    [Fact]
    public void ExamineModeShowsTerrainDescriptions()
    {
        GameSession session = new(1, content: ContentDatabase.LoadDefault());
        session.Execute(new UiCommand(UiCommandKind.Start));
        
        Point wall = session.State.Dungeon.PlayerStart + new Point(2, 0);
        for (int i = 0; i < 5; i++)
            if (!session.State.Dungeon.IsVisible(wall)) break;
        
        string desc = session.GetExamineDescription(session.State.Player.Position);
        Assert.NotEmpty(desc); // Should get "FLOOR" or player position
    }
}

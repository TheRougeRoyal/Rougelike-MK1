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
}

using Roguelike.Content;
using Roguelike.Persistence;
using Xunit;

namespace Roguelike.Tests;

public sealed class Phase5ArchitectureTests
{
    [Fact]
    public void PcgStateRoundTripContinuesIdentically()
    {
        Pcg32 first = new(1234);
        _ = first.Next(int.MaxValue);
        ulong state = first.State;
        int expected = first.Next(1000000);
        Pcg32 second = new(0) { State = state };
        Assert.Equal(expected, second.Next(1000000));
    }

    [Fact]
    public void DefaultContentLoadsAndHashIsStable()
    {
        ContentDatabase first = ContentDatabase.LoadDefault();
        ContentDatabase second = ContentDatabase.LoadDefault();
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Contains(first.Monsters, monster => monster.Id == "slime");
        Assert.Contains(first.Items, item => item.Id == new ItemId("antidote"));
    }

    [Fact]
    public void SaveLoadPreservesStateHash()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState original = new(77);
        original.Process(GameAction.Wait);
        original.Process(GameAction.Wait);
        MemorySaveStore store = new();
        GameStatePersistence.Save(original, store, content.ContentHash);
        GameState loaded = GameStatePersistence.Load(store, content.ContentHash, content);
        Assert.Equal(original.GameplayRandomState, loaded.GameplayRandomState);
        Assert.Equal(original.Player.Position, loaded.Player.Position);
        Assert.Equal(original.Player.Hp, loaded.Player.Hp);
        Assert.Equal(original.Player.MaxHp, loaded.Player.MaxHp);
        Assert.Equal(original.Player.Level, loaded.Player.Level);
        Assert.Equal(original.Player.Experience, loaded.Player.Experience);
        Assert.Equal(original.Player.TotalAttack, loaded.Player.TotalAttack);
        Assert.Equal(original.Player.TotalDefense, loaded.Player.TotalDefense);
        Assert.Equal(original.Player.Inventory.Items.Select(item => (item.Definition.Id, item.Count)),
            loaded.Player.Inventory.Items.Select(item => (item.Definition.Id, item.Count)));
        Assert.Equal(original.Monsters.Count, loaded.Monsters.Count);
        Assert.Equal(original.FloorItems.Count, loaded.FloorItems.Count);
        Assert.Equal(original.Depth, loaded.Depth);
        Assert.Equal(original.TurnNumber, loaded.TurnNumber);
        Assert.Equal(original.Status, loaded.Status);
        Assert.Equal(original.Monsters.Select(monster => (monster.Definition.Id, monster.Position, monster.Hp)),
            loaded.Monsters.Select(monster => (monster.Definition.Id, monster.Position, monster.Hp)));
        Assert.Equal(original.FloorItems.Select(item => (item.Position, item.Item.Definition.Id, item.Item.Count)),
            loaded.FloorItems.Select(item => (item.Position, item.Item.Definition.Id, item.Item.Count)));
        Assert.Equal(original.StateHash, loaded.StateHash);
    }

    [Fact]
    public void GameOverEscapeReturnsToTitleAndStartCreatesFreshRun()
    {
        GameSession session = new(9);
        session.Execute(new UiCommand(UiCommandKind.Start));
        session.State.Status = GameStatus.Dead;
        session.Execute(new UiCommand(UiCommandKind.Cancel));
        Assert.Equal(ScreenKind.Title, session.Screens.Screen);
        session.Execute(new UiCommand(UiCommandKind.Start));
        Assert.Equal(ScreenKind.Playing, session.Screens.Screen);
        Assert.Equal(GameStatus.Playing, session.State.Status);
    }

    [Fact]
    public void SeedParserHandlesEmptyDigitsAndOverflow()
    {
        Assert.True(SeedParser.TryParse(string.Empty, out _, out _));
        Assert.True(SeedParser.TryParse("12345", out int seed, out _));
        Assert.Equal(12345, seed);
        Assert.False(SeedParser.TryParse("9999999999", out _, out string error));
        Assert.Equal("Seed must contain at most 9 digits.", error);
    }
}

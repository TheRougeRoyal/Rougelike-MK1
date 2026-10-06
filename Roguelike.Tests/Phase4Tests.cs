using Microsoft.Xna.Framework;
using Xunit;

namespace Roguelike.Tests;

public sealed class Phase4Tests
{
    [Fact]
    public void TextLayoutWrapsLongWordsAndAlignsExactly()
    {
        Assert.Equal(new[] { "hello", "world" }, TextLayout.WordWrap("hello world", 5));
        Assert.Equal(new[] { "abcde", "f" }, TextLayout.WordWrap("abcdef", 5));
        Assert.Empty(TextLayout.WordWrap(string.Empty, 10));
        Assert.Equal("abcd…", TextLayout.Truncate("abcdef", 5));
        Assert.Equal(15, TextLayout.AlignX(10, 20, 10, TextAlignment.Center));
        Assert.Equal(20, TextLayout.AlignX(10, 20, 10, TextAlignment.Right));
    }

    [Fact]
    public void MessageLogCollapsesAndBoundsEntries()
    {
        MessageLog log = new(3);
        log.Add("hit", Color.White, 1);
        log.Add("hit", Color.White, 2);
        log.Add("miss", Color.Yellow, 3);
        log.Add("new", Color.White, 4);
        Assert.Equal(3, log.Entries.Count);
        Assert.Equal("hit", log.Entries[0].Text);
        Assert.Equal("new", log.Entries[2].Text);
        Assert.Equal(2, log.Entries[0].Count);
    }

    [Fact]
    public void RestartResetsLogAndRunStats()
    {
        GameState state = new(12);
        state.RunStats.ItemsPickedUp = 4;
        state.SetFeedback("temporary", Color.Red);
        state.Restart();
        Assert.Equal(0, state.RunStats.ItemsPickedUp);
        Assert.Single(state.MessageLog.Entries);
        Assert.Equal("Explore the dungeon.", state.MessageLog.Entries[0].Text);
    }

    [Fact]
    public void TeleportNeverChoosesStairsAcrossFiftySeeds()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            GameState state = new(seed, 20, 12);
            state.ConfigureLevelForTesting(CreateOpenDungeon(), new Point(1, 1));
            state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(ItemId.ScrollOfTeleportation)));
            Assert.True(state.Process(GameAction.UseItem(1)));
            Assert.NotEqual(state.Dungeon.StairsPosition, state.Player.Position);
        }
    }

    [Fact]
    public void InputMapperRepeatsMovementAtTheRequiredBoundaries()
    {
        InputMapper mapper = new();
        HashSet<string> empty = new();
        HashSet<string> held = new() { "Right" };
        Assert.Equal(UiCommandKind.Move, mapper.Map(empty, held, ScreenKind.Playing, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.None, mapper.Map(held, held, ScreenKind.Playing, UiOverlay.None,
            TimeSpan.FromMilliseconds(249)).Kind);
        Assert.Equal(UiCommandKind.Move, mapper.Map(held, held, ScreenKind.Playing, UiOverlay.None,
            TimeSpan.FromMilliseconds(1)).Kind);
        Assert.Equal(UiCommandKind.None, mapper.Map(held, held, ScreenKind.Playing, UiOverlay.None,
            TimeSpan.FromMilliseconds(89)).Kind);
        Assert.Equal(UiCommandKind.Move, mapper.Map(held, held, ScreenKind.Playing, UiOverlay.None,
            TimeSpan.FromMilliseconds(1)).Kind);
        Assert.Equal(UiCommandKind.None, mapper.Map(empty, held, ScreenKind.Paused, UiOverlay.None, TimeSpan.FromSeconds(1)).Kind);
    }

    [Fact]
    public void ScreensEnforceRunTransitions()
    {
        ScreenStateMachine screens = new();
        screens.Start();
        screens.Pause();
        Assert.Equal(ScreenKind.Paused, screens.Screen);
        screens.Resume();
        screens.GameOver();
        screens.RestartRun();
        Assert.Equal(ScreenKind.Playing, screens.Screen);
        screens.GameOver();
        screens.Title();
        Assert.Equal(ScreenKind.Title, screens.Screen);
    }

    private static Dungeon CreateOpenDungeon()
    {
        TileType[,] map = new TileType[20, 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++)
            map[x, y] = TileType.Floor;
        return new Dungeon(map, new Point(1, 1), new Point(18, 10));
    }
}

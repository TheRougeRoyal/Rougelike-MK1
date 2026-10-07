using Microsoft.Xna.Framework;
using Xunit;
using Roguelike.Content;


namespace Roguelike.Tests;

public sealed class Phase4Tests
{
    [Fact]
    public void TextLayoutWrapsLongWordsAndAlignsExactly()
    {
        Assert.Equal(new[] { "hello", "world" }, TextLayout.WordWrap("hello world", 5));
        Assert.Equal(new[] { "abcde", "f" }, TextLayout.WordWrap("abcdef", 5));
        Assert.Empty(TextLayout.WordWrap(string.Empty, 10));
        Assert.Equal("ab...", TextLayout.Truncate("abcdef", 5));
        Assert.All(new[] { 1, 2, 3, 5, 10 }, width =>
            Assert.All(TextLayout.Truncate("a long string", width),
                character => Assert.Contains(character, BitmapFont.SupportedCharacters)));
        Assert.Equal(15, TextLayout.AlignX(10, 20, 10, TextAlignment.Center));
        Assert.Equal(20, TextLayout.AlignX(10, 20, 10, TextAlignment.Right));
    }

    [Fact]
    public void BitmapFontCoversPrintableAsciiAndMeasuresAtBothScales()
    {
        for (char character = ' '; character <= '~'; character++)
            Assert.Contains(character, BitmapFont.SupportedCharacters);
        Assert.Equal('?', BitmapFont.NormalizeCharacter('\u2603'));
        Assert.Equal(new Point(18, 8), BitmapFont.Measure("abc", 1));
        Assert.Equal(new Point(36, 16), BitmapFont.Measure("abc", 2));
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
            GameState state = new(seed, 20, 12, 1, ContentDatabase.LoadDefault());
            state.ConfigureLevel(CreateOpenDungeon(), new Point(1, 1));
            ItemContent teleportContent = ContentDatabase.LoadDefault().GetItem(ItemId.ScrollOfTeleportation);
            ItemDefinition teleport = new(teleportContent.Id, teleportContent.Name, teleportContent.Description, teleportContent.Type, teleportContent.Color, teleportContent.MinDepth, teleportContent.Weight, teleportContent.MaxStack, teleportContent.AttackBonus, teleportContent.DefenseBonus, teleportContent.Glyph);
            state.Player.Inventory.TryAdd(new ItemInstance(teleport));
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
        Assert.Equal(UiCommandKind.MenuDown, mapper.Map(empty, new HashSet<string> { "Down" },
            ScreenKind.Paused, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.Accept, mapper.Map(empty, new HashSet<string> { "Enter" },
            ScreenKind.Paused, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.CloseHelp, mapper.Map(empty, new HashSet<string> { "Escape" },
            ScreenKind.Help, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.Quit, mapper.Map(empty, new HashSet<string> { "Escape" },
            ScreenKind.Title, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.ConfirmRestart, mapper.Map(empty, new HashSet<string> { "Y" },
            ScreenKind.Playing, UiOverlay.RestartConfirmation, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.CancelRestart, mapper.Map(empty, new HashSet<string> { "N" },
            ScreenKind.Playing, UiOverlay.RestartConfirmation, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.MenuDown, mapper.Map(empty, new HashSet<string> { "S" },
            ScreenKind.Playing, UiOverlay.Inventory, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.UnequipArmor, mapper.Map(empty, new HashSet<string> { "D2" },
            ScreenKind.Playing, UiOverlay.Inventory, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.None, mapper.Map(new HashSet<string> { "Down" },
            new HashSet<string> { "Down" }, ScreenKind.Paused, UiOverlay.None,
            TimeSpan.FromSeconds(10)).Kind);
        Assert.Equal(UiCommandKind.None, mapper.Map(empty, new HashSet<string> { "2" },
            ScreenKind.Playing, UiOverlay.None, TimeSpan.Zero).Kind);
        Assert.Equal(UiCommandKind.Move, mapper.Map(empty, new HashSet<string> { "NumPad2" },
            ScreenKind.Playing, UiOverlay.None, TimeSpan.Zero).Kind);
    }

    [Fact]
    public void ScreensAcceptValidAndRejectInvalidTransitions()
    {
        ScreenStateMachine screens = new();
        Assert.False(screens.Resume());
        Assert.True(screens.Start());
        Assert.False(screens.Start());
        Assert.True(screens.Pause());
        Assert.False(screens.Pause());
        Assert.True(screens.ShowHelp());
        Assert.True(screens.CloseHelp());
        Assert.Equal(ScreenKind.Paused, screens.Screen);
        Assert.True(screens.Resume());
        Assert.True(screens.RequestRestart());
        Assert.False(screens.RequestRestart());
        Assert.True(screens.CancelRestart());
        Assert.True(screens.RequestRestart());
        Assert.True(screens.ConfirmRestart());
        Assert.False(screens.ConfirmRestart());
        Assert.True(screens.GameOver());
        Assert.True(screens.RestartRun());
        Assert.True(screens.GameOver());
        Assert.True(screens.Title());
        Assert.True(screens.ShowHelp());
        Assert.True(screens.CloseHelp());
        Assert.Equal(ScreenKind.Title, screens.Screen);
    }

    [Fact]
    public void RunStatsTrackCombatPickupDepthAndDeath()
    {
        GameState state = CreateOpenState();
        MonsterDefinition definition = MonsterCatalog.Get(MonsterType.Rat) with { MaxHp = 1 };
        state.AddMonsterForScenario(new MonsterActor(definition, state.Player.Position + new Point(1, 0)));
        Assert.True(state.Process(GameAction.Move(new Point(1, 0))));
        Assert.Equal(1, state.RunStats.MonstersSlain);
        Assert.True(state.RunStats.DamageDealt >= 1);

        state.Player.Position = new Point(1, 1);
        state.AddFloorItem(state.Player.Position + new Point(1, 0),
            new ItemInstance(ItemCatalog.Get(ItemId.Dagger)));
        Assert.True(state.Process(GameAction.Move(new Point(1, 0))));
        Assert.Equal(1, state.RunStats.ItemsPickedUp);

        state.Player.Position = state.Dungeon.StairsPosition;
        Assert.True(state.Process(GameAction.Wait));
        Assert.Equal(2, state.RunStats.MaxDepth);

        GameState death = CreateOpenState();
        MonsterDefinition killer = MonsterCatalog.Get(MonsterType.Brute) with { Attack = 100, MaxHp = 100 };
        death.AddMonsterForScenario(new MonsterActor(killer, death.Player.Position + new Point(1, 0)));
        Assert.True(death.Process(GameAction.Wait));
        Assert.Equal("Brute", death.RunStats.CauseOfDeath);
        Assert.True(death.RunStats.DamageTaken > 0);
    }

    [Fact]
    public void StateHashIgnoresLogStatsAndUiState()
    {
        GameState first = new(44);
        GameState second = new(44);
        ulong expected = first.StateHash;
        first.SetFeedback("visible", Color.Red);
        first.RunStats.DamageDealt = 99;
        first.MessageLog.Clear();
        ScreenStateMachine ui = new();
        ui.Start();
        ui.RequestRestart();
        Assert.Equal(expected, first.StateHash);
        Assert.Equal(expected, second.StateHash);
    }

    [Fact]
    public void RestartOverlayOnlyOpensOnUnoverlaidPlay()
    {
        ScreenStateMachine screens = new();
        screens.Start();
        screens.ToggleInventory();
        Assert.False(screens.RequestRestart());
        screens.CloseOverlay();
        Assert.True(screens.RequestRestart());
        Assert.True(screens.CancelRestart());
    }

    private static Dungeon CreateOpenDungeon()
    {
        TileType[,] map = new TileType[20, 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++)
            map[x, y] = TileType.Floor;
        return new Dungeon(map, new Point(1, 1), new Point(18, 10));
    }

    private static GameState CreateOpenState()
    {
        GameState state = new(1, 20, 12);
        state.ConfigureLevel(CreateOpenDungeon(), new Point(1, 1));
        return state;
    }
}

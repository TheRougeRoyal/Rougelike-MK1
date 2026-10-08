using Microsoft.Xna.Framework;
using Xunit;
using Roguelike.Content;


namespace Roguelike.Tests;

public sealed class Phase2Tests
{
    [Fact]
    public void PlayerHasRequiredStatsAndLevelRules()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        PlayerActor player = new(Point.Zero, content);
        Assert.Equal(content.Balance.StartingHp, player.Hp);
        Assert.Equal(content.Balance.StartingHp, player.MaxHp);
        Assert.Equal(content.Balance.StartingDefense, player.Defense);
        Assert.Equal(20, player.ExperienceToNextLevel);


        player.Hp = 10;
        Assert.Equal(10, player.Heal(10));
        player.AddExperience(20);
        Assert.Equal(2, player.Level);
        Assert.Equal(35, player.MaxHp);
        Assert.Equal(6, player.Attack);
        Assert.Equal(1, player.Defense);

        player.AddExperience(40);
        Assert.Equal(3, player.Level);
        Assert.Equal(2, player.Defense);
    }

    [Fact]
    public void CombatResolverUsesSuppliedRandomAndMinimumDamage()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        PlayerActor player = new(Point.Zero, content);
        MonsterDefinition toughDefinition = new(
            "tough", "Tough", 'T', 20, 1, 10, 5,
            MonsterBehavior.Chase, Color.Red, 1, 1,
            new Dictionary<string, int> { ["alertTurns"] = 5 });
        MonsterActor firstMonster = new(toughDefinition, new Point(1, 0));
        MonsterActor secondMonster = new(toughDefinition, new Point(1, 0));


        CombatResult first = CombatResolver.Resolve(player, firstMonster, new Pcg32(123));
        CombatResult second = CombatResolver.Resolve(player, secondMonster, new Pcg32(123));

        Assert.Same(player, first.Attacker);
        Assert.Same(firstMonster, first.Defender);
        Assert.Equal(1, first.Damage);
        Assert.Equal(first.Damage, second.Damage);
    }

    [Fact]
    public void CatalogContainsRequiredMonsterKindsAndFields()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        Assert.Contains("rat", content.Monsters.Select(d => d.Id));
        Assert.Contains("goblin", content.Monsters.Select(d => d.Id));
        Assert.Contains("archer", content.Monsters.Select(d => d.Id));
        Assert.Contains("brute", content.Monsters.Select(d => d.Id));
        Assert.All(content.Monsters, definition =>
        {
            Assert.True(definition.SightRadius > 0);
            Assert.True(definition.MinDepth > 0);
            Assert.True(definition.Xp > 0);
        });
    }

    [Fact]
    public void PathfinderFindsShortestPathAndRespectsWallsAndBlockedTiles()
    {
        TileType[,] map = CreateFloorMap(7, 7);
        for (int y = 0; y < 6; y++)
        {
            map[3, y] = TileType.Wall;
        }

        Dungeon dungeon = new(map, new Point(1, 1), new Point(5, 5));
        IReadOnlyList<Point> path = new Pathfinder().FindPath(dungeon, new Point(1, 1), new Point(5, 5));

        Assert.NotEmpty(path);
        Assert.Equal(new Point(5, 5), path[^1]);
        Assert.DoesNotContain(new Point(3, 5), path);
        Assert.Empty(new Pathfinder().FindPath(
            dungeon, new Point(1, 1), new Point(5, 5), point => point != new Point(5, 5)));
    }

    [Fact]
    public void SameSeedDepthAndRestartAreDeterministic()
    {
        GameState first = new(42, 60, 34, 1, ContentDatabase.LoadDefault());
        GameState second = new(42, 60, 34, 1, ContentDatabase.LoadDefault());
        Assert.Equal(first.LayoutFingerprint, second.LayoutFingerprint);


        ulong initialHash = first.StateHash;
        first.Process(GameAction.Wait);
        first.Restart();

        Assert.Equal(initialHash, first.StateHash);
        Assert.Equal(
            new HeadlessSimulation().RunHash(42, new[] { GameAction.Wait }),
            new HeadlessSimulation().RunHash(42, new[] { GameAction.Wait }));
    }

    [Fact]
    public void FieldOfViewStopsAtWalls()
    {
        TileType[,] map = new TileType[5, 1];
        map[0, 0] = TileType.Floor;
        map[1, 0] = TileType.Floor;
        map[2, 0] = TileType.Wall;
        map[3, 0] = TileType.Floor;
        map[4, 0] = TileType.Floor;

        Dungeon dungeon = new(map, new Point(0, 0), new Point(4, 0));
        dungeon.UpdateFieldOfView(new Point(0, 0));

        Assert.True(dungeon.IsVisible(new Point(1, 0)));
        Assert.False(dungeon.IsVisible(new Point(3, 0)));
    }

    [Fact]
    public void KillingFirstAdjacentMonsterDoesNotSkipSecondMonster()
    {
        GameState state = new(123, 60, 34, 1, ContentDatabase.LoadDefault());
        state.MutableMonsters.Clear();
        Point playerPosition = state.Player.Position;
        Point firstPosition = FindWalkableNeighbor(state.Dungeon, playerPosition, null);
        Point secondPosition = FindWalkableNeighbor(state.Dungeon, playerPosition, firstPosition);
        MonsterDefinition firstDefinition = CreateDefinition("First", 1);
        MonsterDefinition secondDefinition = CreateDefinition("Second", 1);
        MonsterActor first = new(firstDefinition, firstPosition);
        MonsterActor second = new(secondDefinition, secondPosition);
        GameStateTestHooks.AddMonster(state, first);
        GameStateTestHooks.AddMonster(state, second);
        int playerHp = state.Player.Hp;


        Assert.True(state.Process(ActionFor(playerPosition, firstPosition)));

        Assert.DoesNotContain(first, state.Monsters);
        Assert.True(second.IsAlive);
        Assert.True(state.Player.Hp < playerHp);
    }

    [Fact]
    public void ArcherAtDistanceThreeWithLineOfSightDamagesPlayer()
    {
        GameState state = new(9, 12, 10, 1, ContentDatabase.LoadDefault());
        TileType[,] map = CreateFloorMap(12, 10);
        Dungeon dungeon = new(map, new Point(1, 1), new Point(10, 8));
        state.ConfigureLevel(dungeon, new Point(1, 1));
        ContentDatabase content = ContentDatabase.LoadDefault();
        MonsterContent archerContent = content.GetMonster("archer");
        MonsterDefinition archerDef = new(archerContent.Id, archerContent.Name, archerContent.Glyph, archerContent.MaxHp, archerContent.Attack, archerContent.Defense, archerContent.SightRadius, archerContent.Behavior, archerContent.Color, archerContent.MinDepth, archerContent.Xp, archerContent.Params);
        MonsterActor archer = new(archerDef, new Point(4, 1));
        GameStateTestHooks.AddMonster(state, archer);
        int playerHp = state.Player.Hp;


        Assert.True(state.Process(GameAction.Wait));

        Assert.True(state.Player.Hp < playerHp);
        Assert.Contains("shoots", state.Message);
    }

    [Fact]
    public void LevelUpReportsTheNewLevel()
    {
        GameState state = new(77, 60, 34, 1, ContentDatabase.LoadDefault());
        state.MutableMonsters.Clear();
        Point playerPosition = state.Player.Position;
        Point monsterPosition = FindWalkableNeighbor(state.Dungeon, playerPosition, null);
        MonsterDefinition definition = CreateDefinition("Veteran", 1) with { XpValue = 20 };
        GameStateTestHooks.AddMonster(state, new MonsterActor(definition, monsterPosition));


        Assert.True(state.Process(ActionFor(playerPosition, monsterPosition)));
        Assert.Equal(2, state.Player.Level);
        Assert.Equal("Level 2!", state.Message);
    }

    [Fact]
    public void SpawnedMonstersAvoidStartRoomWallsStairsAndEachOther()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            for (int depth = 1; depth <= 10; depth++)
            {
                GameState state = new(seed, 60, 34, depth, ContentDatabase.LoadDefault());
                HashSet<Point> positions = new();
                foreach (MonsterActor monster in state.Monsters)
                {
                    Assert.True(state.Dungeon.IsWalkable(monster.Position));
                    Assert.False(state.Dungeon.StartRoom.Contains(monster.Position));
                    Assert.NotEqual(state.Dungeon.StairsPosition, monster.Position);
                    Assert.NotEqual(state.Player.Position, monster.Position);
                    Assert.True(positions.Add(monster.Position));
                }
            }
        }
    }

    [Fact]
    public void GenerationConnectivityHoldsAcrossTwoHundredSeeds()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            Dungeon dungeon = new(60, 34, RandomStreams.Create(GameState.CreateLevelSeed(seed, 1), 1, 0x4C455645UL));
            IReadOnlyList<Point> path = new Pathfinder().FindPath(
                dungeon, dungeon.PlayerStart, dungeon.StairsPosition);
            Assert.NotEmpty(path);
        }
    }

    [Fact]
    public void FuzzedTurnsPreserveInvariantsAndReplayHash()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            List<GameAction> actions = CreateFuzzActions(seed);
            ulong firstHash = RunAndValidate(seed, actions);
            ulong secondHash = RunAndValidate(seed, actions);
            Assert.Equal(firstHash, secondHash);
        }
    }

    private static ulong RunAndValidate(int seed, IReadOnlyList<GameAction> actions)
    {
        GameState state = new(seed);
        for (int i = 0; i < actions.Count; i++)
        {
            state.Process(actions[i]);
            ValidateState(state);
        }

        return state.StateHash;
    }

    private static List<GameAction> CreateFuzzActions(int seed)
    {
        Random random = new(seed * 17 + 3);
        List<GameAction> actions = new(500);
        GameState state = new(seed);

        for (int turn = 0; turn < 500; turn++)
        {
            GameAction action;
            if (state.Status == GameStatus.Dead)
            {
                action = GameAction.Restart;
            }
            else
            {
                action = random.Next(0, 5) switch
                {
                    0 => GameAction.Wait,
                    1 => GameAction.Move(new Point(0, -1)),
                    2 => GameAction.Move(new Point(0, 1)),
                    3 => GameAction.Move(new Point(-1, 0)),
                    _ => GameAction.Move(new Point(1, 0))
                };
            }

            actions.Add(action);
            state.Process(action);
        }

        return actions;
    }

    private static void ValidateState(GameState state)
    {
        Assert.InRange(state.Player.Hp, 0, state.Player.MaxHp);
        Assert.InRange(state.Player.Position.X, 0, state.Dungeon.Width - 1);
        Assert.InRange(state.Player.Position.Y, 0, state.Dungeon.Height - 1);
        Assert.True(state.Dungeon.IsWalkable(state.Player.Position));

        HashSet<Point> positions = new();
        foreach (MonsterActor monster in state.Monsters)
        {
            Assert.True(monster.IsAlive);
            Assert.InRange(monster.Hp, 0, monster.MaxHp);
            Assert.InRange(monster.Position.X, 0, state.Dungeon.Width - 1);
            Assert.InRange(monster.Position.Y, 0, state.Dungeon.Height - 1);
            Assert.True(state.Dungeon.IsWalkable(monster.Position));
            Assert.NotEqual(state.Player.Position, monster.Position);
            Assert.True(positions.Add(monster.Position));
        }
    }

    private static TileType[,] CreateFloorMap(int width, int height)
    {
        TileType[,] map = new TileType[width, height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            map[x, y] = TileType.Floor;
        return map;
    }

    private static MonsterDefinition CreateDefinition(string name, int maxHp) =>
        new("goblin", name, 'm', maxHp, 1, 0, 8,
            MonsterBehavior.Chase, Color.Red, 1, 1,
            new Dictionary<string, int> { ["alertTurns"] = 5 });

    private static Point FindWalkableNeighbor(Dungeon dungeon, Point origin, Point? excluded)
    {
        Point[] candidates =
        {
            new(origin.X + 1, origin.Y),
            new(origin.X - 1, origin.Y),
            new(origin.X, origin.Y + 1),
            new(origin.X, origin.Y - 1)
        };

        foreach (Point candidate in candidates)
        {
            if (candidate != excluded && dungeon.IsWalkable(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No walkable neighbor found.");
    }

    private static GameAction ActionFor(Point origin, Point destination)
    {
        Point delta = destination - origin;
        return delta switch
        {
            { X: 1, Y: 0 } => GameAction.Move(new Point(1, 0)),
            { X: -1, Y: 0 } => GameAction.Move(new Point(-1, 0)),
            { X: 0, Y: 1 } => GameAction.Move(new Point(0, 1)),
            _ => GameAction.Move(new Point(0, -1))
        };
    }
}

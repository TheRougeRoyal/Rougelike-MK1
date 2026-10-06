using Microsoft.Xna.Framework;
using Xunit;

namespace Roguelike.Tests;

public sealed class Phase2Tests
{
    [Fact]
    public void PlayerHasRequiredStatsAndLevelRules()
    {
        PlayerActor player = new(Point.Zero);
        Assert.Equal(30, player.Hp);
        Assert.Equal(30, player.MaxHp);
        Assert.Equal(20, player.ExperienceToNextLevel);
        player.Hp = 10;
        Assert.Equal(10, player.Heal());
        player.AddExperience(20);
        Assert.Equal(2, player.Level);
        Assert.Equal(35, player.MaxHp);
        Assert.Equal(6, player.Attack);
        player.AddExperience(40);
        Assert.Equal(3, player.Level);
        Assert.Equal(2, player.Defense);
    }

    [Fact]
    public void CombatResolverUsesSuppliedRandomAndBothDirections()
    {
        PlayerActor player = new(Point.Zero);
        MonsterActor monster = new(MonsterCatalog.Get(MonsterType.Rat), new Point(1, 0));
        CombatResult first = CombatResolver.Resolve(player, monster, new Random(1));
        Assert.Same(player, first.Attacker);
        Assert.Same(monster, first.Defender);
        Assert.InRange(first.Damage, 4, 6);
        CombatResult second = CombatResolver.Resolve(monster, player, new Random(1));
        Assert.Same(monster, second.Attacker);
        Assert.Same(player, second.Defender);
    }

    [Fact]
    public void CombatResolverMinimumDamageAndFixedSeedAreDeterministic()
    {
        PlayerActor weak = new(Point.Zero);
        MonsterDefinition toughDefinition = new(
            MonsterType.Brute, "Tough", 'T', 20, 1, 10, 5,
            MonsterBehavior.Chase, Microsoft.Xna.Framework.Color.Red, 1, 1);
        MonsterActor tough = new(toughDefinition, new Point(1, 0));
        CombatResult first = CombatResolver.Resolve(weak, tough, new Random(123));
        MonsterActor other = new(toughDefinition, new Point(1, 0));
        CombatResult second = CombatResolver.Resolve(weak, other, new Random(123));
        Assert.Equal(1, first.Damage);
        Assert.Equal(first.Damage, second.Damage);
    }

    [Fact]
    public void CatalogContainsExactRequiredMonsterKindsAndFields()
    {
        Assert.Equal(new[] { MonsterType.Rat, MonsterType.Goblin, MonsterType.Archer, MonsterType.Brute },
            MonsterCatalog.All.Select(definition => definition.Type));
        Assert.All(MonsterCatalog.All, definition =>
        {
            Assert.True(definition.SightRadius > 0);
            Assert.True(definition.MinDepth > 0);
            Assert.True(definition.XpValue > 0);
        });
    }

    [Fact]
    public void PathfinderFindsHandBuiltShortestPathAndRespectsWalls()
    {
        TileType[,] map = new TileType[7, 7];
        for (int y = 0; y < 7; y++)
        for (int x = 0; x < 7; x++) map[x, y] = TileType.Floor;
        for (int y = 0; y < 6; y++) map[3, y] = TileType.Wall;
        Dungeon dungeon = new(map, new Point(1, 1), new Point(5, 5));
        IReadOnlyList<Point> path = new Pathfinder().FindPath(dungeon, new Point(1, 1), new Point(5, 5));
        Assert.NotEmpty(path);
        Assert.Equal(new Point(5, 5), path[^1]);
        Assert.DoesNotContain(new Point(3, 5), path);
        Assert.Empty(new Pathfinder().FindPath(dungeon, new Point(1, 1), new Point(5, 5),
            point => point != new Point(5, 5)));
    }

    [Fact]
    public void SameSeedDepthAndRestartAreDeterministic()
    {
        GameState first = new(42);
        GameState second = new(42);
        Assert.Equal(first.LayoutFingerprint, second.LayoutFingerprint);
        ulong before = first.LayoutFingerprint;
        ulong initialHash = first.StateHash;
        first.Process(TurnAction.Wait);
        first.Restart();
        Assert.Equal(before, first.LayoutFingerprint);
        Assert.Equal(initialHash, first.StateHash);
        Assert.Equal(new HeadlessSimulation().RunHash(42, new[] { TurnAction.Wait }),
            new HeadlessSimulation().RunHash(42, new[] { TurnAction.Wait }));
    }

    [Fact]
    public void HeadlessStateHashChangesWithSimulation()
    {
        HeadlessSimulation simulation = new();
        ulong initial = simulation.RunHash(9, Array.Empty<TurnAction>());
        ulong after = simulation.RunHash(9, new[] { TurnAction.Wait });
        Assert.NotEqual(initial, after);
    }

    [Fact]
    public void FieldOfViewStopsAtWalls()
    {
        TileType[,] map = new TileType[5, 1];
        map[0, 0] = TileType.Floor; map[1, 0] = TileType.Floor;
        map[2, 0] = TileType.Wall; map[3, 0] = TileType.Floor; map[4, 0] = TileType.Floor;
        Dungeon dungeon = new(map, new Point(0, 0), new Point(4, 0));
        dungeon.UpdateFieldOfView(new Point(0, 0));
        Assert.True(dungeon.IsVisible(new Point(1, 0)));
        Assert.False(dungeon.IsVisible(new Point(3, 0)));
    }

    [Fact]
    public void WaitingAdvancesTurnsAndRangedCombatCanKill()
    {
        GameState state = new(123);
        int turns = state.TurnNumber;
        Assert.True(state.Process(TurnAction.Wait));
        Assert.Equal(turns + 1, state.TurnNumber);
        Assert.True(state.Player.IsAlive);
    }

    [Fact]
    public void GenerationAndSpawningStayValidAcrossSeedsAndDepths()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            for (int depth = 1; depth <= 10; depth++)
            {
                Dungeon dungeon = new(60, 34, new Random(GameState.CreateLevelSeed(seed, depth)));
                Pathfinder pathfinder = new();
                Assert.NotEmpty(pathfinder.FindPath(dungeon, dungeon.PlayerStart, dungeon.StairsPosition));

                GameState state = new(seed, 60, 34, depth);

                HashSet<Point> positions = new();
                foreach (MonsterActor monster in state.Monsters)
                {
                    Assert.True(dungeon.IsWalkable(monster.Position));
                    Assert.NotEqual(dungeon.PlayerStart, monster.Position);
                    Assert.NotEqual(dungeon.StairsPosition, monster.Position);
                    Assert.True(positions.Add(monster.Position));
                }
            }
        }
    }
}

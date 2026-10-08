namespace Roguelike.Tests;

internal static class GameStateTestHooks
{
    public static GameState AddMonster(GameState state, MonsterActor monster) =>
        new(state, state.Monsters.Append(monster));

    public static GameState ClearMonsters(GameState state) =>
        new(state, Array.Empty<MonsterActor>());

    public static void DropLoot(GameState state, MonsterActor monster) =>
        state.DropLoot(monster);
}

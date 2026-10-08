namespace Roguelike.Tests;

internal static class GameStateTestHooks
{
    public static void AddMonster(GameState state, MonsterActor monster) =>
        state.MutableMonsters.Add(monster);

    public static void DropLoot(GameState state, MonsterActor monster) =>
        state.DropLoot(monster);
}

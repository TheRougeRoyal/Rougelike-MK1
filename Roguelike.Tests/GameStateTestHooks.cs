namespace Roguelike.Tests;

internal static class GameStateTestHooks
{
    public static void AddMonster(GameState state, MonsterActor monster) =>
        state.ReplaceMonstersForTesting(state.Monsters.Append(monster).ToArray());

    public static void ClearMonsters(GameState state) =>
        state.ReplaceMonstersForTesting(Array.Empty<MonsterActor>());

    public static void DropLoot(GameState state, MonsterActor monster) =>
        state.DropLoot(monster);
}

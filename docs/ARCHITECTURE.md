# Architecture

The project is split into a headless core (`GameState`, actors, dungeon, turns), content (`Content/ContentDatabase` and embedded JSON), persistence (`Persistence`), run history (`Runs`), and MonoGame UI (`GameMain`, `GameSession`, `UI`).

Gameplay randomness uses independent PCG32 streams seeded by SplitMix64-derived values for level generation, monster placement, loot, and gameplay. `IRandom.State` is serializable; deterministic state includes the live gameplay stream state. Content is canonicalized and hashed before a run can use it.

To add a monster or item, edit the corresponding JSON file. Monster behavior must be one of the registered behavior names and item effects must be registered in `EffectRegistry`; invalid content is rejected before play starts. A complete override directory must contain `monsters.json`, `items.json`, and `balance.json`.

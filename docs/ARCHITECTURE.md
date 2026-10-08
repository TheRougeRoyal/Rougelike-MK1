# Architecture

## Layers

- **Core:** `GameState`, `Dungeon`, actors, inventory, combat, turns, and `IRandom`.
  This layer is headless and is the source of truth for gameplay.
- **Content:** `ContentDatabase` validates `monsters.json`, `items.json`, and
  `balance.json`, then exposes immutable records and a stable SHA-256 content hash.
- **Persistence:** `Persistence/` stores a versioned envelope containing the seed,
  depth, gameplay RNG state, actor/item state, explored bitmap, run statistics, and
  the last 100 message-log entries. The dungeon layout is regenerated from seed/depth.
- **Runs:** `Runs/` records ended runs and ranks them by depth, level, then fewer turns.
- **UI:** `GameSession` is the command-driven, window-free coordinator. `GameMain`
  only supplies MonoGame input/rendering and passes the loaded content database in.

## RNG and determinism

`Pcg32` is the only production random generator. `RandomStreams.Create` derives
independent streams for level generation, monster placement, loot, and gameplay
from the run seed, depth, and stream salt. Generation and loot never consume the
gameplay stream. `IRandom.State` is saved so loading and continuing produces the
same result as an uninterrupted run. New gameplay randomness must use an injected
`IRandom` or a named derived stream; do not use `System.Random` or `Random.Shared`.

## Content schema

Every content file has `schemaVersion: 2`.

`monsters.json` contains `monsters[]` entries with `id`, `name`, printable `glyph`,
`color`, `maxHp`, `attack`, `defense`, `xp`, `sightRadius`, `minDepth`,
`spawnWeight`, `behavior`, and optional integer `params`. Parameters currently used
by the turn manager include `alwaysChase`, `minRange`, `maxRange`,
`actEveryNTurns`, and `alertTurns`; the loader requires behavior-specific
parameters instead of supplying gameplay fallbacks.

`items.json` contains `items[]` entries with `id`, `name`, `description`, printable
`glyph`, `color`, `type` (`consumable`, `weapon`, or `armor`), `slot`,
`minDepth`, `weight`, `maxStack`, attack/defense bonuses, and an `effects` list.
Declarative effect types are `heal` (`amount`), `buff` (`stat`, `amount`, `turns`),
`teleport` (`minDistance`), and `reveal_map`. Effects are dispatched by the
effect registry; item ids are not special-cased by gameplay code.

`balance.json` owns starting stats and loadout, spawn and loot counts, drop chance,
stair healing percentage, feedback duration, minimum spawn distance, XP curve,
level-up bonuses, depth scaling, and the level-10 coverage rules. A custom
directory passed with `--content <dir>` must provide all three files and replaces
the embedded defaults before the game starts.

To add content, add a JSON entry with a unique id, satisfy validation, and include
the id in `balance.json` only if it is part of the starting loadout. No C# catalog
or item-specific code is required.

## Save format and durability

`save.json` is a checksummed versioned envelope. The checksum is verified using
the schema version that was written, then a supported older version is migrated.
Writes go to a temporary write-through stream, call `Flush(true)`, preserve
`save.json.bak`, and rename the temporary file into place. If the primary file is
unreadable or fails checksum/JSON validation, the backup is attempted before the
load error is reported. The save content hash prevents loading a run with
different content.

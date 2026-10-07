using Microsoft.Xna.Framework;
using Roguelike.Content;

namespace Roguelike;

internal readonly record struct ItemId(string Value)
{
    public static ItemId HealingPotion => new("healing_potion");
    public static ItemId GreaterHealingPotion => new("greater_healing_potion");
    public static ItemId PotionOfStrength => new("potion_of_strength");
    public static ItemId ScrollOfTeleportation => new("scroll_of_teleportation");
    public static ItemId ScrollOfMapping => new("scroll_of_mapping");
    public static ItemId Dagger => new("dagger");
    public static ItemId ShortSword => new("short_sword");
    public static ItemId BattleAxe => new("battle_axe");
    public static ItemId LeatherArmor => new("leather_armor");
    public static ItemId ChainMail => new("chain_mail");
    public static ItemId PlateArmor => new("plate_armor");
    public static implicit operator string(ItemId id) => id.Value;
    public static bool operator ==(string left, ItemId right) => left == right.Value;
    public static bool operator !=(string left, ItemId right) => left != right.Value;
    public static bool operator ==(ItemId left, string right) => left.Value == right;
    public static bool operator !=(ItemId left, string right) => left.Value != right;
}

internal static class ItemCatalog
{
    public static ItemDefinition Get(ItemId id) => ContentDatabase.LoadDefault().CreateDefinition(id.Value);
}

internal enum MonsterType { Rat, Goblin, Archer, Brute, Slime }

internal static class MonsterCatalog
{
    public static MonsterDefinition Get(MonsterType type)
    {
        string id = type.ToString().ToLowerInvariant();
        MonsterContent monster = ContentDatabase.LoadDefault().GetMonster(id);
        return new MonsterDefinition(monster.Id, monster.Name, monster.Glyph, monster.MaxHp, monster.Attack,
            monster.Defense, monster.SightRadius, monster.Behavior, monster.Color, monster.MinDepth,
            monster.Xp, monster.Params);
    }
}

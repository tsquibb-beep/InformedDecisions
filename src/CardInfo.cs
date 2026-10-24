using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace InformedDecisions;

/// <summary>
/// Works out what the badges should say. Reads the card exactly the way the game does when it
/// prints the cost and when it plays the card, so the two can never disagree.
/// </summary>
internal static class CardInfo
{
    /// <summary>
    /// The card's normal energy cost when something has changed what it shows, else null.
    ///
    /// "Normal" is <see cref="CostModifiers.None"/>, which already includes upgrades, so an
    /// upgrade's cost cut is never flagged. The printed cost is <see cref="CostModifiers.All"/>,
    /// as in NCard.UpdateEnergyCostVisuals. X-cost and unplayable (negative) costs are skipped.
    /// </summary>
    public static int? OriginalEnergyCost(CardModel card)
    {
        CardEnergyCost cost = card.EnergyCost;
        if (cost.CostsX)
        {
            return null;
        }

        int normal = cost.GetWithModifiers(CostModifiers.None);
        if (normal < 0)
        {
            return null;
        }

        int shown = cost.GetWithModifiers(CostModifiers.All);
        return shown != normal ? normal : null;
    }

    /// <summary>
    /// The same for the Regent's star cost. The game prints <c>GetStarCostWithModifiers()</c>
    /// and compares it against <c>BaseStarCost</c> when it colours the number.
    /// </summary>
    public static int? OriginalStarCost(CardModel card)
    {
        if (card.HasStarCostX)
        {
            return null;
        }

        int normal = card.BaseStarCost;
        if (normal < 0)
        {
            return null;
        }

        int shown = card.GetStarCostWithModifiers();
        return shown != normal ? normal : null;
    }

    /// <summary>
    /// How many extra times the card would be played if it were played now: its own Replay
    /// (keyword, enchantment, Hidden Gem...) plus extra plays from powers and relics (Echo Form,
    /// Burst, Duplication...).
    ///
    /// Mirrors the game's play routine (<c>GetEnchantedReplayCount() + 1</c>, then
    /// <see cref="Hook.ModifyCardPlayCount"/>). The hook only reads state; the powers count
    /// themselves down in a separate hook after the real play. There is no target yet, so a
    /// target-dependent bonus (Tag Team on a marked enemy) is not counted.
    /// </summary>
    public static int ExtraPlays(CardModel card, bool includePowers)
    {
        int plays = card.GetEnchantedReplayCount() + 1;
        if (includePowers && card.CombatState is { } combat)
        {
            plays = Hook.ModifyCardPlayCount(combat, card, plays, null, out _);
        }

        return plays - 1;
    }
}

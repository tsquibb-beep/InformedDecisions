using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace InformedDecisions;

/// <summary>
/// Hooks the points where the game itself reprints and recolours a card's cost, so the badges
/// update exactly when the cost number does (hand refreshes, power changes, pile moves).
/// UpdateVisuals calls both cost methods; SetPretendCardCanBePlayed and the cost randomiser call
/// them directly. OnFreedToPool is where the card goes back to the pool.
/// </summary>
[HarmonyPatch(typeof(NCard))]
internal static class NCardPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("UpdateEnergyCostVisuals")]
    private static void UpdateEnergyCostVisuals_Postfix(NCard __instance)
    {
        CostBadges.Refresh(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("UpdateStarCostVisuals")]
    private static void UpdateStarCostVisuals_Postfix(NCard __instance)
    {
        CostBadges.Refresh(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NCard.OnFreedToPool))]
    private static void OnFreedToPool_Prefix(NCard __instance)
    {
        CostBadges.Release(__instance);
    }
}

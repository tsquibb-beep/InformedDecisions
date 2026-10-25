using System;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace InformedDecisions;

/// <summary>
/// `idec diag` lists what the badges should say for every card in your hand (also written to the
/// game log); `idec reload` re-reads the config and redraws the hand.
///
/// The dev console discovers commands in loaded mods by reflection, so shipping this public
/// class with a parameterless constructor is enough to register it.
/// </summary>
public class InfoConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "idec";

    public override string Args => "[diag|reload|effect <ripple|glow|compare|none>]";

    public override string Description => "InformedDecisions: show the hand's cost/replay values, or reload the config.";

    /// <summary>Badges are local to this client — nothing to synchronise.</summary>
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        switch (args.FirstOrDefault()?.ToLowerInvariant() ?? "diag")
        {
            case "diag":
                return Diag();

            case "reload":
                InfoConfig.Reload();
                Redraw();
                return new CmdResult(success: true, "Config reloaded.");

            case "effect":
                string? kind = args.Skip(1).FirstOrDefault()?.ToLowerInvariant();
                if (kind is not (ReplayEffects.Ripple or ReplayEffects.Glow or ReplayEffects.Compare or ReplayEffects.None))
                {
                    return new CmdResult(success: false, "Use: idec effect ripple, glow, compare or none.");
                }

                // Until the next reload or restart; the config file is not changed.
                InfoConfig.Current.ReplayEffect = kind;
                Redraw();
                return new CmdResult(success: true, $"Replay gem effect: {kind}.");

            default:
                return new CmdResult(success: false, "Use: idec diag, idec reload, idec effect <name>.");
        }
    }

    private static void Redraw()
    {
        CostBadges.Restyle();
        if (NPlayerHand.Instance is { } hand)
        {
            foreach (var holder in hand.ActiveHolders)
            {
                holder.UpdateCard();
            }
        }
    }

    private static CmdResult Diag()
    {
        if (NPlayerHand.Instance is not { } hand)
        {
            return new CmdResult(success: false, "Start a fight first.");
        }

        var text = new StringBuilder();
        foreach (var holder in hand.ActiveHolders)
        {
            if (holder.CardNode?.Model is not CardModel card)
            {
                continue;
            }

            text.Append(card.Title).Append(": energy ").Append(Energy(card));
            if (card.HasStarCostX || card.BaseStarCost >= 0)
            {
                text.Append(", stars ").Append(card.HasStarCostX ? "X" : card.GetStarCostWithModifiers().ToString())
                    .Append(" (normal ").Append(card.HasStarCostX ? "X" : card.BaseStarCost.ToString())
                    .Append(CardInfo.OriginalStarCost(card) is { } star ? $", badge {star}" : ", no badge").Append(')');
            }

            int own = card.GetEnchantedReplayCount();
            int total = CardInfo.ExtraPlays(card, includePowers: true);
            text.Append(", replay ").Append(own).Append(" own + ").Append(total - own).Append(" from powers")
                .Append(total > 0 ? $" -> R{total}" : "").AppendLine();
        }

        string result = text.Length > 0 ? text.ToString().TrimEnd() : "Your hand is empty.";
        Log.Info($"[InformedDecisions] idec diag:\n{result}");
        return new CmdResult(success: true, result);
    }

    private static string Energy(CardModel card)
    {
        CardEnergyCost cost = card.EnergyCost;
        if (cost.CostsX)
        {
            return "X";
        }

        string badge = CardInfo.OriginalEnergyCost(card) is { } normal ? $"badge {normal}" : "no badge";
        return $"{cost.GetWithModifiers(CostModifiers.All)} (normal {cost.GetWithModifiers(CostModifiers.None)}, {badge})";
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            return CompleteArgument(new[] { "diag", "reload", "effect" }, Array.Empty<string>(), args.FirstOrDefault() ?? "");
        }

        if (args.Length == 2 && args[0].Equals("effect", StringComparison.OrdinalIgnoreCase))
        {
            return CompleteArgument(new[] { ReplayEffects.Ripple, ReplayEffects.Glow, ReplayEffects.Compare, ReplayEffects.None },
                Array.Empty<string>(), args[1]);
        }

        return new CompletionResult { Type = CompletionType.Argument, ArgumentContext = CmdName };
    }
}

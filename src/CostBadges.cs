using System;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace InformedDecisions;

/// <summary>
/// Keeps the small labels by a card's cost in sync with its model: the normal energy cost, the
/// normal star cost and "R#".
///
/// The labels are children of the game's own %EnergyIcon / %StarIcon, so they move, scale and
/// hide with the gem (the gem is hidden on cards with no cost). NCard is pooled, so the labels
/// are made once per node and reused; <see cref="Release"/> hides them when the card goes back.
/// </summary>
internal static class CostBadges
{
    private sealed class Entry
    {
        public Label? EnergyCost;
        public Label? StarCost;
        public Label? Replay;
        public int StyledFor = -1;
    }

    private static readonly ConditionalWeakTable<NCard, Entry> _entries = new();

    /// <summary>Bumped by `idec reload` so existing labels pick up new sizes and colours.</summary>
    private static int _styleGeneration;

    private static bool _loggedFailure;

    public static void Restyle() => _styleGeneration++;

    /// <summary>Re-reads the card. Cheap: called from the game's own cost refreshes.</summary>
    public static void Refresh(NCard? card)
    {
        if (card == null || !GodotObject.IsInstanceValid(card) || !card.IsNodeReady())
        {
            return;
        }

        try
        {
            InfoConfig config = InfoConfig.Current;
            CardModel? model = card.Model;
            bool active = config.Enabled && model != null && card.Visibility == ModelVisibility.Visible;

            Entry? entry;
            if (!active)
            {
                if (_entries.TryGetValue(card, out entry))
                {
                    Hide(entry);
                }

                return;
            }

            int? energyCost = config.ShowOriginalCost ? CardInfo.OriginalEnergyCost(model!) : null;
            int? starCost = config.ShowOriginalCost ? CardInfo.OriginalStarCost(model!) : null;
            int replay = config.ShowReplay ? CardInfo.ExtraPlays(model!, config.ReplayIncludesPowers) : 0;

            // Cards that never need a badge never get labels.
            if (energyCost == null && starCost == null && replay <= 0 && !_entries.TryGetValue(card, out _))
            {
                return;
            }

            entry = _entries.GetValue(card, _ => new Entry());
            if (entry.StyledFor != _styleGeneration)
            {
                Restyle(entry, config);
                entry.StyledFor = _styleGeneration;
            }

            Show(ref entry.EnergyCost, card, "%EnergyIcon", energyCost?.ToString(), config, Badge.EnergyCost);
            Show(ref entry.StarCost, card, "%StarIcon", starCost?.ToString(), config, Badge.StarCost);
            Show(ref entry.Replay, card, "%EnergyIcon", replay > 0 ? $"R{replay}" : null, config, Badge.Replay);
        }
        catch (Exception ex)
        {
            // A visual mod must never take the game down with it. Log once, not every frame.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log.Error($"[InformedDecisions] Failed to refresh a card: {ex}");
            }
        }
    }

    public static void Release(NCard? card)
    {
        if (card != null && _entries.TryGetValue(card, out Entry? entry))
        {
            Hide(entry);
        }
    }

    private enum Badge
    {
        EnergyCost,
        StarCost,
        Replay,
    }

    private static void Show(ref Label? label, NCard card, string gemPath, string? text, InfoConfig config, Badge badge)
    {
        if (text == null)
        {
            if (IsAlive(label))
            {
                label!.Visible = false;
            }

            return;
        }

        if (!IsAlive(label))
        {
            Control? gem = card.GetNodeOrNull<Control>(gemPath);
            if (gem == null)
            {
                return;
            }

            label = Create(badge);
            gem.AddChild(label);
            Style(label, card, config, badge);
        }

        label!.Text = text;
        label.Visible = true;
    }

    private static Label Create(Badge badge) => new()
    {
        Name = $"InformedDecisions{badge}",
        MouseFilter = Control.MouseFilterEnum.Ignore,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Visible = false,
    };

    private static void Restyle(Entry entry, InfoConfig config)
    {
        if (IsAlive(entry.EnergyCost)) Style(entry.EnergyCost!, null, config, Badge.EnergyCost);
        if (IsAlive(entry.StarCost)) Style(entry.StarCost!, null, config, Badge.StarCost);
        if (IsAlive(entry.Replay)) Style(entry.Replay!, null, config, Badge.Replay);
    }

    /// <summary>
    /// Font from the game's cost label (Kreon bold), size, colours and a fixed box centred on the
    /// configured point. The box is wider than any text it will hold so the centre never drifts.
    /// </summary>
    private static void Style(Label label, NCard? card, InfoConfig config, Badge badge)
    {
        if (card?.GetNodeOrNull<Label>("%EnergyLabel") is { } gameLabel)
        {
            label.AddThemeFontOverride("font", gameLabel.GetThemeFont("font"));
        }

        bool replay = badge == Badge.Replay;
        int fontSize = replay ? config.ReplayFontSize : config.OriginalCostFontSize;
        Color color = InfoConfig.ToColor(replay ? config.ReplayColor : config.OriginalCostColor, Colors.White);
        Color outline = InfoConfig.ToColor(replay ? config.ReplayOutlineColor : config.OriginalCostOutlineColor, Colors.Black);
        Vector2 centre = badge switch
        {
            Badge.EnergyCost => InfoConfig.ToVector(config.OriginalCostOffset, new Vector2(58, 6)),
            Badge.StarCost => InfoConfig.ToVector(config.OriginalStarCostOffset, new Vector2(52, 8)),
            _ => InfoConfig.ToVector(config.ReplayOffset, new Vector2(62, 40)),
        };

        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", outline);
        label.AddThemeConstantOverride("outline_size", Math.Max(4, fontSize / 2));

        var size = new Vector2(fontSize * 4, fontSize * 2);
        label.Size = size;
        label.Position = centre - size / 2;
    }

    private static void Hide(Entry entry)
    {
        if (IsAlive(entry.EnergyCost)) entry.EnergyCost!.Visible = false;
        if (IsAlive(entry.StarCost)) entry.StarCost!.Visible = false;
        if (IsAlive(entry.Replay)) entry.Replay!.Visible = false;
    }

    private static bool IsAlive(Label? label) => label != null && GodotObject.IsInstanceValid(label);
}

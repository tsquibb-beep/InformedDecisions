using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace InformedDecisions;

/// <summary>
/// Draws the extra information on a card, built from the game's own pieces so it looks native:
///
/// * **Normal cost:** a smaller copy of the card's cost gem (same texture, font, outline and
///   shadow as the game's), tucked behind the real gem.
/// * **Extra plays:** darkened copies of the card's own frame stacked behind it, one per extra
///   play, so a Replay card looks like a small pile of cards. Past <c>replayStackMax</c> (or with
///   <c>replayStyle</c> "text"/"both") an "R#" label appears by the gem as well.
///
/// Everything is a child of %CardContainer, inserted at a fixed draw-order slot: the mini gems
/// just behind the real gems, the stack just in front of the card's glow (%Highlight) so the
/// glow does not tint it. NCard is pooled, so nodes are made once per card and reused;
/// <see cref="Release"/> hides them when the card goes back to the pool.
/// </summary>
internal static class CostBadges
{
    private sealed class MiniGem
    {
        public required TextureRect Gem;
        public required Label Label;
    }

    private sealed class Entry
    {
        public MiniGem? EnergyGem;
        public MiniGem? StarGem;
        public Label? ReplayText;
        public readonly List<TextureRect> Stack = new();
        public int StyledFor = -1;
    }

    private static readonly ConditionalWeakTable<NCard, Entry> _entries = new();

    /// <summary>Bumped by `idec reload` so existing nodes pick up new sizes and positions.</summary>
    private static int _styleGeneration;

    private static bool _loggedFailure;

    // From card.tscn: the energy gem is 64x64 with a size-32 label, the star gem 58x58 with size 22.
    private const float EnergyGemSize = 64;
    private const int EnergyFontSize = 32;
    private const float StarGemSize = 58;
    private const int StarFontSize = 22;

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

            // Cards that never need a badge never get nodes.
            if (energyCost == null && starCost == null && replay <= 0 && !_entries.TryGetValue(card, out _))
            {
                return;
            }

            entry = _entries.GetValue(card, _ => new Entry());
            if (entry.StyledFor != _styleGeneration)
            {
                // Rebuild from scratch so new sizes, positions and stack depth all apply.
                Free(entry);
                entry.StyledFor = _styleGeneration;
            }

            UpdateMiniGem(ref entry.EnergyGem, card, "%EnergyIcon", energyCost, EnergyGemSize, EnergyFontSize,
                config.OriginalCostScale, config.OriginalCostOffset, new Vector2(60, 6), model!.Pool.EnergyOutlineColor, config);
            UpdateMiniGem(ref entry.StarGem, card, "%StarIcon", starCost, StarGemSize, StarFontSize,
                config.OriginalStarCostScale, config.OriginalStarCostOffset, new Vector2(50, 50), StsColors.defaultStarCostOutline, config);
            UpdateReplay(entry, card, model, replay, config);
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

    // ---- Normal cost: mini gem ---------------------------------------------------------------

    private static void UpdateMiniGem(ref MiniGem? mini, NCard card, string gemPath, int? cost, float gemSize,
        int fontSize, float scale, float[]? offset, Vector2 fallbackOffset, Color outline, InfoConfig config)
    {
        TextureRect? gem = card.GetNodeOrNull<TextureRect>(gemPath);

        // The game hides its gem on cards without that kind of cost; ours goes with it.
        if (cost == null || gem == null || !gem.Visible)
        {
            if (mini != null && IsAlive(mini.Gem))
            {
                mini.Gem.Visible = false;
            }

            return;
        }

        if (mini == null || !IsAlive(mini.Gem))
        {
            mini = CreateMiniGem(card, gem, gemSize, fontSize, scale, offset, fallbackOffset, config);
        }

        // Kept current on every refresh: the texture is per character and the gem can move.
        mini.Gem.Texture = gem.Texture;
        float size = gemSize * scale;
        Vector2 centre = InfoConfig.ToVector(offset, fallbackOffset);
        mini.Gem.Position = gem.Position + centre - new Vector2(size, size) / 2;
        mini.Label.AddThemeColorOverride("font_outline_color", outline);
        mini.Label.Text = cost.Value.ToString();
        mini.Gem.Visible = true;
    }

    private static MiniGem CreateMiniGem(NCard card, TextureRect gem, float gemSize, int fontSize, float scale,
        float[]? offset, Vector2 fallbackOffset, InfoConfig config)
    {
        float size = gemSize * scale;
        float shade = Math.Clamp(config.OriginalCostGemShade, 0f, 1f);
        var rect = new TextureRect
        {
            Name = $"InformedDecisionsMini{gem.Name}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(size, size),
            // Only the gem is shaded, so it reads as secondary; the number stays full brightness.
            SelfModulate = new Color(shade, shade, shade),
            Visible = false,
        };

        // The game's cost label style, scaled: Kreon bold, cream, outline, soft drop shadow.
        int scaledFont = Math.Max(12, (int)MathF.Round(fontSize * scale));
        int outlineSize = Math.Max(4, (int)MathF.Round(fontSize / 2f * scale));
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // The game's label sits 2 units below the gem's centre.
            Position = new Vector2(-size / 2, -size / 2 + 2 * scale),
            Size = new Vector2(size * 2, size * 2),
        };
        if (gem.GetChildOrNull<Label>(0) is { } gameLabel)
        {
            label.AddThemeFontOverride("font", gameLabel.GetThemeFont("font"));
        }

        label.AddThemeFontSizeOverride("font_size", scaledFont);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.188235f));
        label.AddThemeConstantOverride("outline_size", outlineSize);
        label.AddThemeConstantOverride("shadow_outline_size", outlineSize);
        label.AddThemeConstantOverride("shadow_offset_x", Math.Max(1, (int)MathF.Round(2 * scale)));
        label.AddThemeConstantOverride("shadow_offset_y", Math.Max(1, (int)MathF.Round(2 * scale)));
        rect.AddChild(label);

        // Behind the real gem, in front of everything the gem is in front of.
        Node container = gem.GetParent();
        container.AddChild(rect);
        container.MoveChild(rect, gem.GetIndex());
        return new MiniGem { Gem = rect, Label = label };
    }

    // ---- Extra plays: stacked frames ----------------------------------------------------------

    private static void UpdateReplay(Entry entry, NCard card, CardModel model, int replay, InfoConfig config)
    {
        string style = (config.ReplayStyle ?? "stack").Trim().ToLowerInvariant();
        bool stack = style is "stack" or "both";
        int max = Math.Max(0, config.ReplayStackMax);
        int layers = stack ? Math.Min(replay, max) : 0;
        bool text = replay > 0 && (style is "text" or "both" || replay > layers);

        UpdateStack(entry, card, model, layers, max, config);
        UpdateReplayText(entry, card, text ? $"R{replay}" : null, config);
    }

    private static void UpdateStack(Entry entry, NCard card, CardModel model, int layers, int max, InfoConfig config)
    {
        Control? frame = card.GetNodeOrNull<TextureRect>("%Frame");
        if (layers <= 0 || frame == null)
        {
            foreach (TextureRect layer in entry.Stack)
            {
                if (IsAlive(layer)) layer.Visible = false;
            }

            return;
        }

        if (entry.Stack.Count < max || entry.Stack.Exists(l => !IsAlive(l)))
        {
            BuildStack(entry, card, frame, max);
        }

        // Ancient cards hide %Frame and use their own border, so read the model, not the node.
        Texture2D texture = model.Frame;
        Material material = model.FrameMaterial;
        Vector2 step = InfoConfig.ToVector(config.ReplayStackOffset, new Vector2(7, -9));
        float shadeStep = Math.Clamp(config.ReplayStackShade, 0f, 0.3f);
        for (int i = 0; i < entry.Stack.Count; i++)
        {
            TextureRect layer = entry.Stack[i];
            if (i >= layers)
            {
                layer.Visible = false;
                continue;
            }

            if (layer.Texture != texture) layer.Texture = texture;
            if (layer.Material != material) layer.Material = material;
            layer.Size = frame.Size;
            layer.Position = frame.Position + step * (i + 1);
            float shade = Math.Max(0.15f, 1f - shadeStep * (i + 1));
            layer.Modulate = new Color(shade, shade, shade);
            layer.Visible = true;
        }
    }

    /// <summary>
    /// Makes <paramref name="max"/> frame copies (index 0 is nearest the card) and orders them so
    /// the deepest is drawn first, all just in front of the glow and behind the card itself.
    /// </summary>
    private static void BuildStack(Entry entry, NCard card, Control frame, int max)
    {
        foreach (TextureRect old in entry.Stack)
        {
            if (IsAlive(old)) old.QueueFree();
        }

        entry.Stack.Clear();

        var source = (TextureRect)frame;
        Node container = frame.GetParent();
        for (int i = 0; i < max; i++)
        {
            var layer = new TextureRect
            {
                Name = $"InformedDecisionsReplay{i + 1}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = source.ExpandMode,
                StretchMode = source.StretchMode,
                PivotOffset = source.PivotOffset,
                Visible = false,
            };
            container.AddChild(layer);
            entry.Stack.Add(layer);
        }

        Node? glow = card.GetNodeOrNull<Node>("%Highlight");
        int slot = glow != null && glow.GetParent() == container ? glow.GetIndex() + 1 : 0;
        for (int i = max - 1; i >= 0; i--)
        {
            container.MoveChild(entry.Stack[i], slot++);
        }
    }

    private static void UpdateReplayText(Entry entry, NCard card, string? text, InfoConfig config)
    {
        if (text == null)
        {
            if (IsAlive(entry.ReplayText)) entry.ReplayText!.Visible = false;
            return;
        }

        if (!IsAlive(entry.ReplayText))
        {
            TextureRect? gem = card.GetNodeOrNull<TextureRect>("%EnergyIcon");
            if (gem == null)
            {
                return;
            }

            entry.ReplayText = CreateReplayText(gem, config);
        }

        entry.ReplayText!.Text = text;
        entry.ReplayText.Visible = true;
    }

    private static Label CreateReplayText(TextureRect gem, InfoConfig config)
    {
        int fontSize = config.ReplayFontSize;
        var label = new Label
        {
            Name = "InformedDecisionsReplayText",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        if (gem.GetChildOrNull<Label>(0) is { } gameLabel)
        {
            label.AddThemeFontOverride("font", gameLabel.GetThemeFont("font"));
        }

        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", StsColors.gold);
        label.AddThemeColorOverride("font_outline_color", new Color("2B2118"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.188235f));
        label.AddThemeConstantOverride("outline_size", Math.Max(4, fontSize / 2));
        label.AddThemeConstantOverride("shadow_outline_size", Math.Max(4, fontSize / 2));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);

        var size = new Vector2(fontSize * 4, fontSize * 2);
        label.Size = size;
        label.Position = InfoConfig.ToVector(config.ReplayOffset, new Vector2(62, 40)) - size / 2;
        gem.AddChild(label);
        return label;
    }

    // ---- Housekeeping ---------------------------------------------------------------------------

    private static void Hide(Entry entry)
    {
        if (entry.EnergyGem != null && IsAlive(entry.EnergyGem.Gem)) entry.EnergyGem.Gem.Visible = false;
        if (entry.StarGem != null && IsAlive(entry.StarGem.Gem)) entry.StarGem.Gem.Visible = false;
        if (IsAlive(entry.ReplayText)) entry.ReplayText!.Visible = false;
        foreach (TextureRect layer in entry.Stack)
        {
            if (IsAlive(layer)) layer.Visible = false;
        }
    }

    private static void Free(Entry entry)
    {
        if (entry.EnergyGem != null && IsAlive(entry.EnergyGem.Gem)) entry.EnergyGem.Gem.QueueFree();
        if (entry.StarGem != null && IsAlive(entry.StarGem.Gem)) entry.StarGem.Gem.QueueFree();
        if (IsAlive(entry.ReplayText)) entry.ReplayText!.QueueFree();
        foreach (TextureRect layer in entry.Stack)
        {
            if (IsAlive(layer)) layer.QueueFree();
        }

        entry.EnergyGem = null;
        entry.StarGem = null;
        entry.ReplayText = null;
        entry.Stack.Clear();
    }

    private static bool IsAlive(GodotObject? node) =>
        node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
}

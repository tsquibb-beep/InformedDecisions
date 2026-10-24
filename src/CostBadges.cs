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
/// * **Extra plays:** "R2" in a third, dimmer mini gem, plus darkened copies of the card's own
///   frame stacked behind it (at most <c>replayStackMax</c>, as a signal rather than a count), so
///   a Replay card looks like a small pile of cards.
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

    /// <summary>How one kind of mini gem looks and where it goes, relative to the game's gem.</summary>
    private readonly record struct GemStyle(string GemPath, float GemSize, int FontSize, float Scale, float Shade,
        float Opacity, float[]? Offset, Vector2 FallbackOffset);

    private sealed class Entry
    {
        public MiniGem? EnergyGem;
        public MiniGem? StarGem;
        public MiniGem? ReplayGem;
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

            Color energyOutline = model!.Pool.EnergyOutlineColor;
            UpdateMiniGem(ref entry.EnergyGem, card, energyCost?.ToString(), energyOutline,
                new GemStyle("%EnergyIcon", EnergyGemSize, EnergyFontSize, config.OriginalCostScale,
                    config.OriginalCostGemShade, 1f, config.OriginalCostOffset, new Vector2(60, 6)));
            UpdateMiniGem(ref entry.StarGem, card, starCost?.ToString(), StsColors.defaultStarCostOutline,
                new GemStyle("%StarIcon", StarGemSize, StarFontSize, config.OriginalStarCostScale,
                    config.OriginalCostGemShade, 1f, config.OriginalStarCostOffset, new Vector2(50, 50)));

            // "R2" is two characters, so its text is a notch smaller than a cost in the same gem. It sits on
            // the card's top edge beside the real gem, one slot further right when the cost gem is there.
            // (Below the gem it was covered by the gem's corner and lost on the title banner.)
            bool costGemShown = entry.EnergyGem is { } costGem && IsAlive(costGem.Gem) && costGem.Gem.Visible;
            UpdateMiniGem(ref entry.ReplayGem, card, config.ReplayGem && replay > 0 ? $"R{replay}" : null, energyOutline,
                new GemStyle("%EnergyIcon", EnergyGemSize, (int)(EnergyFontSize * 0.85f), config.ReplayGemScale,
                    config.ReplayGemShade, config.ReplayGemOpacity,
                    costGemShown ? config.ReplayGemOffsetWithCost : config.ReplayGemOffset,
                    costGemShown ? new Vector2(104, 10) : new Vector2(66, 10)));
            UpdateStack(entry, card, model, config.ReplayStack ? Math.Min(replay, Math.Max(0, config.ReplayStackMax)) : 0,
                Math.Max(0, config.ReplayStackMax), config);
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

    // ---- Mini gems: normal cost and replay count -----------------------------------------------

    private static void UpdateMiniGem(ref MiniGem? mini, NCard card, string? text, Color outline, GemStyle style)
    {
        TextureRect? gem = card.GetNodeOrNull<TextureRect>(style.GemPath);

        // The game hides its gem on cards without that kind of cost; ours goes with it.
        if (text == null || gem == null || !gem.Visible)
        {
            if (mini != null && IsAlive(mini.Gem))
            {
                mini.Gem.Visible = false;
            }

            return;
        }

        if (mini == null || !IsAlive(mini.Gem))
        {
            mini = CreateMiniGem(gem, style);
        }

        // Kept current on every refresh: the texture is per character and the gem can move.
        mini.Gem.Texture = gem.Texture;
        float size = style.GemSize * style.Scale;
        Vector2 centre = InfoConfig.ToVector(style.Offset, style.FallbackOffset);
        mini.Gem.Position = gem.Position + centre - new Vector2(size, size) / 2;
        mini.Label.AddThemeColorOverride("font_outline_color", outline);
        mini.Label.Text = text;
        mini.Gem.Visible = true;
    }

    private static MiniGem CreateMiniGem(TextureRect gem, GemStyle style)
    {
        float scale = style.Scale;
        float size = style.GemSize * scale;
        float shade = Math.Clamp(style.Shade, 0f, 1f);
        var rect = new TextureRect
        {
            Name = "InformedDecisionsMiniGem",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(size, size),
            // Only the gem is shaded, so it reads as secondary; the number stays full brightness.
            SelfModulate = new Color(shade, shade, shade),
            // Fades gem and number together.
            Modulate = new Color(1, 1, 1, Math.Clamp(style.Opacity, 0f, 1f)),
            Visible = false,
        };

        // The game's cost label style, scaled: Kreon bold, cream, outline, soft drop shadow.
        int scaledFont = Math.Max(12, (int)MathF.Round(style.FontSize * scale));
        int outlineSize = Math.Max(4, (int)MathF.Round(style.FontSize / 2f * scale));
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

    // ---- Housekeeping ---------------------------------------------------------------------------

    private static void Hide(Entry entry)
    {
        if (entry.EnergyGem != null && IsAlive(entry.EnergyGem.Gem)) entry.EnergyGem.Gem.Visible = false;
        if (entry.StarGem != null && IsAlive(entry.StarGem.Gem)) entry.StarGem.Gem.Visible = false;
        if (entry.ReplayGem != null && IsAlive(entry.ReplayGem.Gem)) entry.ReplayGem.Gem.Visible = false;
        foreach (TextureRect layer in entry.Stack)
        {
            if (IsAlive(layer)) layer.Visible = false;
        }
    }

    private static void Free(Entry entry)
    {
        if (entry.EnergyGem != null && IsAlive(entry.EnergyGem.Gem)) entry.EnergyGem.Gem.QueueFree();
        if (entry.StarGem != null && IsAlive(entry.StarGem.Gem)) entry.StarGem.Gem.QueueFree();
        if (entry.ReplayGem != null && IsAlive(entry.ReplayGem.Gem)) entry.ReplayGem.Gem.QueueFree();
        foreach (TextureRect layer in entry.Stack)
        {
            if (IsAlive(layer)) layer.QueueFree();
        }

        entry.EnergyGem = null;
        entry.StarGem = null;
        entry.ReplayGem = null;
        entry.Stack.Clear();
    }

    private static bool IsAlive(GodotObject? node) =>
        node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
}

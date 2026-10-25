using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace InformedDecisions;

/// <summary>
/// Settings, read from InformedDecisions.config.jsonc. Missing or malformed files fall back to
/// these defaults, so the mod never fails to load because of a bad config. `idec reload`
/// re-reads it.
///
/// Gem positions are in the cost gem's own units: the energy gem is 64x64, the star gem 58x58,
/// (0, 0) is the gem's top-left, and everything scales with the card.
/// </summary>
internal sealed class InfoConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Show the normal cost beside a cost that an effect has changed.</summary>
    [JsonPropertyName("showOriginalCost")]
    public bool ShowOriginalCost { get; set; } = true;

    /// <summary>Show "R#" by the cost for cards that will be played more than once.</summary>
    [JsonPropertyName("showReplay")]
    public bool ShowReplay { get; set; } = true;

    /// <summary>Count extra plays from powers and relics (Echo Form, Burst...), not just Replay.</summary>
    [JsonPropertyName("replayIncludesPowers")]
    public bool ReplayIncludesPowers { get; set; } = true;

    /// <summary>Size of the small normal-cost gem relative to the real one.</summary>
    [JsonPropertyName("originalCostScale")]
    public float OriginalCostScale { get; set; } = 0.62f;

    [JsonPropertyName("originalStarCostScale")]
    public float OriginalStarCostScale { get; set; } = 0.7f;

    /// <summary>Brightness of the small gem (1 = same as the real gem). The number is not dimmed.</summary>
    [JsonPropertyName("originalCostGemShade")]
    public float OriginalCostGemShade { get; set; } = 0.8f;

    /// <summary>Centre of the small energy gem, relative to the energy gem.</summary>
    [JsonPropertyName("originalCostOffset")]
    public float[] OriginalCostOffset { get; set; } = { 60, 6 };

    /// <summary>Centre of the small star gem, relative to the star gem.</summary>
    [JsonPropertyName("originalStarCostOffset")]
    public float[] OriginalStarCostOffset { get; set; } = { 50, 50 };

    /// <summary>Show "R2" in a small gem by the cost.</summary>
    [JsonPropertyName("replayGem")]
    public bool ReplayGem { get; set; } = true;

    [JsonPropertyName("replayGemScale")]
    public float ReplayGemScale { get; set; } = 0.58f;

    /// <summary>Brightness of the replay gem; darker than the cost gem so it stays in the background.</summary>
    [JsonPropertyName("replayGemShade")]
    public float ReplayGemShade { get; set; } = 0.7f;

    /// <summary>Opacity of the whole replay gem, number included.</summary>
    [JsonPropertyName("replayGemOpacity")]
    public float ReplayGemOpacity { get; set; } = 1f;

    /// <summary>"arrows": the count inside two arching arrows (the usual repeat icon). "R": "R2".</summary>
    [JsonPropertyName("replaySymbol")]
    public string ReplaySymbol { get; set; } = "arrows";

    /// <summary>Centre of the replay gem, relative to the energy gem.</summary>
    [JsonPropertyName("replayGemOffset")]
    public float[] ReplayGemOffset { get; set; } = { 66, 10 };

    /// <summary>Centre of the replay gem when the normal-cost gem is showing too: right of the real gem, below the cost gem.</summary>
    [JsonPropertyName("replayGemOffsetWithCost")]
    public float[] ReplayGemOffsetWithCost { get; set; } = { 74, 34 };

    /// <summary>Motion on the replay gem: "ripple", "glow", "compare" (alternates the two along the hand) or "none".</summary>
    [JsonPropertyName("replayEffect")]
    public string ReplayEffect { get; set; } = ReplayEffects.Ripple;

    /// <summary>Only animate replay gems in your hand; the deck view and rewards stay still.</summary>
    [JsonPropertyName("replayEffectInHandOnly")]
    public bool ReplayEffectInHandOnly { get; set; } = true;

    /// <summary>Seconds from one ripple to the next.</summary>
    [JsonPropertyName("replayRipplePeriod")]
    public float ReplayRipplePeriod { get; set; } = 2.4f;

    /// <summary>How visible the ripple is (0 to 2).</summary>
    [JsonPropertyName("replayRippleStrength")]
    public float ReplayRippleStrength { get; set; } = 0.9f;

    /// <summary>Seconds for one full glow pulse.</summary>
    [JsonPropertyName("replayGlowPeriod")]
    public float ReplayGlowPeriod { get; set; } = 2.4f;

    /// <summary>How bright the glow is at its peak (0 to 2).</summary>
    [JsonPropertyName("replayGlowStrength")]
    public float ReplayGlowStrength { get; set; } = 0.8f;

    /// <summary>[r, g, b] from 0 to 1 for the effect; unset uses the gem's own colour.</summary>
    [JsonPropertyName("replayEffectColor")]
    public float[]? ReplayEffectColor { get; set; }

    /// <summary>Stack card edges behind a card that will be played more than once.</summary>
    [JsonPropertyName("replayStack")]
    public bool ReplayStack { get; set; } = false;

    /// <summary>Most card edges drawn. They signal "replays"; the gem gives the number.</summary>
    [JsonPropertyName("replayStackMax")]
    public int ReplayStackMax { get; set; } = 2;

    /// <summary>How far each card edge sits from the one in front, in card units.</summary>
    [JsonPropertyName("replayStackOffset")]
    public float[] ReplayStackOffset { get; set; } = { 7, -9 };

    /// <summary>How much darker each card edge is than the one in front (0 to 0.3).</summary>
    [JsonPropertyName("replayStackShade")]
    public float ReplayStackShade { get; set; } = 0.2f;

    public static Vector2 ToVector(float[]? pair, Vector2 fallback) =>
        pair is { Length: >= 2 } ? new Vector2(pair[0], pair[1]) : fallback;

    private static InfoConfig? _current;

    public static InfoConfig Current => _current ??= Load();

    public static void Reload() => _current = Load();

    private const string FileName = "InformedDecisions.config.jsonc";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Looks for the config next to the game's save data first, then beside the mod DLL.
    ///
    /// The save-data copy wins so that settings survive a Vortex update, which replaces the
    /// whole mod folder. The file is .jsonc rather than .json deliberately: the game scans
    /// mods/ recursively for *.json and tries to parse every one as a mod manifest.
    /// </summary>
    private static InfoConfig Load()
    {
        foreach (string path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                InfoConfig? loaded = JsonSerializer.Deserialize<InfoConfig>(File.ReadAllText(path), _jsonOptions);
                if (loaded != null)
                {
                    Log.Info($"[InformedDecisions] Loaded config from {path}.");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[InformedDecisions] Could not read {path} ({ex.Message}), trying the next location.");
            }
        }

        Log.Info("[InformedDecisions] No config file found, using defaults.");
        return new InfoConfig();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string? userDir = null;
        try
        {
            userDir = OS.GetUserDataDir();
        }
        catch (Exception)
        {
            // Not fatal: fall back to the mod folder.
        }

        if (!string.IsNullOrEmpty(userDir))
        {
            yield return Path.Combine(userDir, FileName);
        }

        string? modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(modDir))
        {
            yield return Path.Combine(modDir, FileName);
        }
    }
}

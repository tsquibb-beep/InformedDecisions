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
/// Positions are in the cost gem's own units: the energy gem is 64x64, the star gem 58x58,
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

    [JsonPropertyName("originalCostFontSize")]
    public int OriginalCostFontSize { get; set; } = 20;

    [JsonPropertyName("originalCostColor")]
    public string OriginalCostColor { get; set; } = "#FFF6E2";

    [JsonPropertyName("originalCostOutlineColor")]
    public string OriginalCostOutlineColor { get; set; } = "#2B2118";

    /// <summary>Centre of the original energy cost, relative to the energy gem.</summary>
    [JsonPropertyName("originalCostOffset")]
    public float[] OriginalCostOffset { get; set; } = { 58, 6 };

    /// <summary>Centre of the original star cost, relative to the star gem.</summary>
    [JsonPropertyName("originalStarCostOffset")]
    public float[] OriginalStarCostOffset { get; set; } = { 52, 8 };

    [JsonPropertyName("replayFontSize")]
    public int ReplayFontSize { get; set; } = 20;

    [JsonPropertyName("replayColor")]
    public string ReplayColor { get; set; } = "#EFC851";

    [JsonPropertyName("replayOutlineColor")]
    public string ReplayOutlineColor { get; set; } = "#2B2118";

    /// <summary>Centre of the "R#" text, relative to the energy gem.</summary>
    [JsonPropertyName("replayOffset")]
    public float[] ReplayOffset { get; set; } = { 62, 40 };

    public static Vector2 ToVector(float[]? pair, Vector2 fallback) =>
        pair is { Length: >= 2 } ? new Vector2(pair[0], pair[1]) : fallback;

    public static Color ToColor(string? html, Color fallback) =>
        !string.IsNullOrWhiteSpace(html) && Color.HtmlIsValid(html) ? Color.FromHtml(html) : fallback;

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

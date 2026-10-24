using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace InformedDecisions;

/// <summary>
/// Entry point. The loader finds this via <see cref="ModInitializerAttribute"/> and calls
/// <see cref="Init"/> once at startup.
/// </summary>
[ModInitializer(nameof(Init))]
internal static class InformedDecisionsMod
{
    private const string HarmonyId = "tomasapan.InformedDecisions";

    private static void Init()
    {
        try
        {
            _ = InfoConfig.Current;
            new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("[InformedDecisions] Initialised.");
        }
        catch (Exception ex)
        {
            Log.Error($"[InformedDecisions] Failed to initialise: {ex}");
        }
    }
}

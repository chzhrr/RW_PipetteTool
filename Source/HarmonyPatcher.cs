using HarmonyLib;
using JetBrains.Annotations;
using Verse;

namespace PipetteTool
{
    [StaticConstructorOnStartup]
    [UsedImplicitly]
    public static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            Harmony instance = new Harmony("Telardo.PipetteTool");
            instance.PatchAll();
        }
    }
}
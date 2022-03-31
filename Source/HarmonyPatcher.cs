using HarmonyLib;
using Verse;

namespace PipetteTool
{
    [StaticConstructorOnStartup]
    internal static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            Harmony instance = new Harmony("Telardo.PipetteTool");
            instance.PatchAll();
        }
    }
}
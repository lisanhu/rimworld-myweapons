using HarmonyLib;
using RimWorld;
using Verse;

namespace MyWeapons;

[HarmonyPatch(typeof(StatWorker), nameof(StatWorker.StatOffsetFromGear))]
public static class StatOffsetFromGearPatch
{
    public static void Postfix(Thing gear, StatDef stat, ref float __result)
    {
        CompUniversalToolbox comp = gear.TryGetComp<CompUniversalToolbox>();
        if (comp != null)
        {
            __result += comp.OffsetFor(stat);
        }
    }
}

using HarmonyLib;
using RimWorld;
using Verse;

namespace MyWeapons;

[HarmonyPatch(typeof(StatWorker), "GearAffectsStat")]
public static class StatGearBreakdownPatch
{
    public static void Postfix(ThingDef gearDef, StatDef stat, ref bool __result)
    {
        if (!__result && gearDef.HasComp(typeof(CompUniversalToolbox)) &&
            (stat == QualityOffsetDefOf.MW_PawnCreatedQualityOffset ||
             stat == StatDefOf.WorkSpeedGlobal ||
             stat == StatDefOf.ResearchSpeed ||
             stat == StatDefOf.EntityStudyRate))
        {
            __result = true;
        }
    }
}

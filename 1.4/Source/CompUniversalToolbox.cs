using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MyWeapons;

public class CompUniversalToolbox : ThingComp
{
    public int qualityOffset;
    public float workSpeedOffset;
    public float researchSpeedOffset;

    private Gizmo_UniversalToolbox gizmo;

    public CompProperties_UniversalToolbox Props => (CompProperties_UniversalToolbox)props;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        qualityOffset = Props.defaultQualityOffset;
        workSpeedOffset = Props.defaultWorkSpeedOffset;
        researchSpeedOffset = Props.defaultResearchSpeedOffset;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref qualityOffset, "qualityOffset", Props.defaultQualityOffset);
        Scribe_Values.Look(ref workSpeedOffset, "workSpeedOffset", 0f);
        Scribe_Values.Look(ref researchSpeedOffset, "researchSpeedOffset", 0f);
    }

    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        gizmo ??= new Gizmo_UniversalToolbox(this);
        yield return gizmo;
    }

    public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

    public void Notify_SettingsChanged()
    {
        Wearer?.health?.capacities?.Notify_CapacityLevelsDirty();
    }

    public float OffsetFor(StatDef stat)
    {
        if (stat == QualityOffsetDefOf.MW_PawnCreatedQualityOffset)
        {
            return qualityOffset;
        }
        if (stat == StatDefOf.WorkSpeedGlobal)
        {
            return workSpeedOffset;
        }
        if (stat == StatDefOf.ResearchSpeed)
        {
            return researchSpeedOffset;
        }
        return 0f;
    }
}

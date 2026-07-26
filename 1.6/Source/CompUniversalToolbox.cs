using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MyWeapons;

public class CompUniversalToolbox : ThingComp
{
    public int qualityOffset;
    public float workSpeedOffset;
    public float researchSpeedOffset;
    public float entityStudyRateOffset;

    private Gizmo_UniversalToolbox gizmo;

    public CompProperties_UniversalToolbox Props => (CompProperties_UniversalToolbox)props;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        qualityOffset = Props.defaultQualityOffset;
        workSpeedOffset = Props.defaultWorkSpeedOffset;
        researchSpeedOffset = Props.defaultResearchSpeedOffset;
        entityStudyRateOffset = Props.defaultEntityStudyRateOffset;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref qualityOffset, "qualityOffset", 3);
        Scribe_Values.Look(ref workSpeedOffset, "workSpeedOffset", 0f);
        Scribe_Values.Look(ref researchSpeedOffset, "researchSpeedOffset", 0f);
        Scribe_Values.Look(ref entityStudyRateOffset, "entityStudyRateOffset", 0f);
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
        if (stat == StatDefOf.EntityStudyRate)
        {
            return entityStudyRateOffset;
        }
        return 0f;
    }
}

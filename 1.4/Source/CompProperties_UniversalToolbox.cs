using Verse;

namespace MyWeapons;

public class CompProperties_UniversalToolbox : CompProperties
{
    public int defaultQualityOffset = 3;
    public float defaultWorkSpeedOffset;
    public float defaultResearchSpeedOffset;

    public CompProperties_UniversalToolbox()
    {
        compClass = typeof(CompUniversalToolbox);
    }
}

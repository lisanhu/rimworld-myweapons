using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MyWeapons
{

    class SavableListOfDefs<T> : IExposable
     where T : Def
    {
        public List<T> defs;

        public SavableListOfDefs()
        {
            defs = new List<T>();
        }

        public static implicit operator List<T>(SavableListOfDefs<T> savable)
        {
            return savable.defs;
        }

        public static implicit operator SavableListOfDefs<T>(List<T> list)
        {
            SavableListOfDefs<T> savable = new SavableListOfDefs<T>();
            savable.defs = list;
            return savable;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref defs, "MyWeapons.SavableListOfDefs.defs", LookMode.Def);
        }
    }
    public class PrayRecipeGameComponent : GameComponent
    {
        private int recipeId = 0;
        private List<int> recipeIds = new List<int>();
        private List<int> recipeHashes = new List<int>();
        private List<SavableListOfDefs<ThingDef>> recipeProducts = new List<SavableListOfDefs<ThingDef>>();
        private List<RecipeDef> createdRecipes = new List<RecipeDef>();
        public List<RecipeDef> CreatedRecipes { get => createdRecipes; set => createdRecipes = value; }
        public int RecipeId { get => recipeId; set => recipeId = value; }

        public static readonly AccessTools.FieldRef<Dictionary<Type, HashSet<ushort>>> takenShortHashes = AccessTools.StaticFieldRefAccess<Dictionary<Type, HashSet<ushort>>>(AccessTools.Field(typeof(ShortHashGiver), "takenHashesPerDeftype"));
        private delegate void GiveShortHash(Def def, Type defType, HashSet<ushort> takenHashes);
        private static readonly GiveShortHash giveShortHash = AccessTools.MethodDelegate<GiveShortHash>(AccessTools.Method(type: typeof(ShortHashGiver), name: "GiveShortHash"));

        public PrayRecipeGameComponent(Game game) : base()
        {
            recipeId = 0;
            CreatedRecipes = new List<RecipeDef>();
            recipeIds = new List<int>();
            recipeHashes = new List<int>();
            recipeProducts = new List<SavableListOfDefs<ThingDef>>();
        }

        public void AddRecipe(List<ThingDef> products, int recipeId = -1, int recipeHash = -1)
        {
            RecipeDef recipe = new RecipeDef();
            if (recipeId != -1)
            {
                recipe.defName = "Pray_" + recipeId;
            }
            else
            {
                recipe.defName = "Pray_" + RecipeId++;
            }
            recipe.label = "PrayRecipeLabel".Translate(string.Join(", ", products.Select(d => d.label).ToArray()));
            recipe.jobString = "WorkingOnPrayedRecipe".Translate();
            recipe.workAmount = 1f;
            var speedStat = DefDatabase<StatDef>.GetNamed("DrugCookingSpeed");
            recipe.workSpeedStat = speedStat;
            recipe.workSkill = SkillDefOf.Crafting;
            recipe.effectWorking = DefDatabase<EffecterDef>.GetNamed("Cook");
            recipe.targetCountAdjustment = 5;
            ThingDef praySpot2 = DefDatabase<ThingDef>.GetNamed("PraySpotTwo");
            recipe.recipeUsers = new List<ThingDef>
                    {
                        praySpot2
                    };
            recipe.defaultIngredientFilter = new ThingFilter();
            recipe.descriptionHyperlinks = new List<DefHyperlink>();
            foreach (ThingDef def in products)
            {
                Log.Warning($"Adding {def.defName} to recipe {recipe.defName}");
                recipe.products.Add(new ThingDefCountClass(def, def.stackLimit));
                recipe.descriptionHyperlinks.Add(new DefHyperlink(def));
            }

            recipe.ResolveReferences();
            recipe.PostLoad();
            var _takenHashes = takenShortHashes()[typeof(RecipeDef)];
            if (recipeHash == -1)
            {
                giveShortHash(recipe, typeof(RecipeDef), _takenHashes);
            }
            else
            {
                recipe.shortHash = (ushort)recipeHash;
                _takenHashes.Add(recipe.shortHash);
            }

            DefDatabase<RecipeDef>.Add(recipe);
            CreatedRecipes.Insert(0, recipe);

            if (recipeId == -1)
            {
                recipeIds.Insert(0, RecipeId - 1);
                recipeHashes.Insert(0, recipe.shortHash);
                recipeProducts.Insert(0, products);
            }
        }

        public void RemoveRecipe(RecipeDef recipe)
        {
            var rid = int.Parse(recipe.defName.Split('_')[1]);
            var idx = recipeIds.Find(id => id == rid);
            recipeProducts.RemoveAt(idx);
            recipeHashes.RemoveAt(idx);
            recipeIds.RemoveAt(idx);
            CreatedRecipes.Remove(recipe);
        }

        public override void FinalizeInit()
        {
            DefDatabase<RecipeDef>.AllDefsListForReading.FindAll(r => r.defName.StartsWith("Pray_")).ForEach(r => recipeId = Math.Max(recipeId, int.Parse(r.defName.Split('_')[1]) + 1));
            for (int i = 0; i < recipeIds.Count; ++i)
            {
                var rid = recipeIds[i];
                var rhash = recipeHashes[i];
                var rproducts = recipeProducts[i];
                AddRecipe(rproducts, rid, rhash);

                recipeId = Math.Max(recipeId, rid + 1);
            }
        }

        public override void ExposeData()
        {
            if (recipeIds == null)
            {
                recipeIds = new List<int>();
            }
            if (recipeHashes == null)
            {
                recipeHashes = new List<int>();
            }
            if (recipeProducts == null)
            {
                recipeProducts = new List<SavableListOfDefs<ThingDef>>();
            }
            if (createdRecipes == null)
            {
                createdRecipes = new List<RecipeDef>();
            }

            Scribe_Values.Look(ref recipeId, "MyWeapons.PrayRecipeWindow.recipeId");
            Scribe_Collections.Look(ref recipeIds, "MyWeapons.PrayRecipeWindow.recipeIds", LookMode.Value);
            Scribe_Collections.Look(ref recipeHashes, "MyWeapons.PrayRecipeWindow.recipeHashes", LookMode.Value);
            Scribe_Collections.Look(ref recipeProducts, "MyWeapons.PrayRecipeWindow.recipeProducts", LookMode.Deep);
        }
    }

    class PrayRecipeWindow : Window
    {
        QuickSearchWidget searchWidget = new QuickSearchWidget();
        private float y = 0f;
        private const float lineHeight = 30f;
        private const float scrollbarWidth = 16f;
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 createdRecipesScrollPosition = Vector2.zero;

        private Dictionary<ThingDef, Rect> selectedDefs = new Dictionary<ThingDef, Rect>();
        PrayRecipeGameComponent gc = Current.Game.GetComponent<PrayRecipeGameComponent>();

        private void GapVertical(float gap = lineHeight)
        {
            y += gap;
        }

        private void GetRow(out Rect top, out Rect bot, Rect inRect, float height = lineHeight)
        {
            top = inRect.TopPartPixels(height);
            bot = inRect.BottomPartPixels(inRect.height - height);
            y += height;
        }

        public PrayRecipeWindow() : base()
        {
            draggable = true;
            resizeable = true;
        }
        public override void DoWindowContents(Rect inRect)
        {
            /// Search Widget
            GetRow(out Rect top, out Rect bot, inRect);
            searchWidget.OnGUI(top, delegate
            {
                selectedDefs.Clear();
            });
            GetRow(out top, out bot, bot);
            Widgets.Label(top, searchWidget.filter.Text);

            /// Scroll area (searching results)
            var textLineHeight = Text.LineHeight + 2f;
            GetRow(out top, out bot, bot, 10 * textLineHeight);
            Rect viewRect = top;
            Rect scrollRect = viewRect;
            List<ThingDef> defs = new List<ThingDef>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                // if (!searchWidget.filter.Text.Trim().NullOrEmpty() && searchWidget.filter.Matches(def.label))
                if (searchWidget.filter.Active && searchWidget.filter.Matches(def.label))
                {
                    defs.Add(def);
                }
                if (defs.Count > 50)
                {
                    break;
                }
            }
            scrollRect.width -= scrollbarWidth;
            scrollRect.height = defs.Count * textLineHeight;
            Widgets.BeginScrollView(viewRect, ref scrollPosition, scrollRect, true);
            foreach (ThingDef def in defs)
            {
                GetRow(out top, out scrollRect, scrollRect, textLineHeight);
                Widgets.DefLabelWithIcon(top, def);

                if (Widgets.ButtonInvisible(top))
                {
                    if (selectedDefs.ContainsKey(def))
                    {
                        selectedDefs.Remove(def);
                    }
                    else
                    {
                        selectedDefs.Add(def, top);
                    }
                }
            }

            foreach (KeyValuePair<ThingDef, Rect> kvp in selectedDefs)
            {
                Widgets.DrawHighlight(kvp.Value);
            }
            Widgets.EndScrollView();

            /// Gap
            GetRow(out top, out bot, bot);
            /// Bottom buttons
            /// Pray button
            GetRow(out top, out bot, bot);
            var buttonRect = top.LeftPartPixels(100f);
            if (Widgets.ButtonText(buttonRect, "Pray".Translate()))
            {
                if (!selectedDefs.NullOrEmpty())
                {
                    var products = selectedDefs.Keys.ToList();
                    gc.AddRecipe(products);
                }
            }

            /// Close button
            buttonRect = top.RightPartPixels(100f);
            GUI.color = new Color(1f, 0.3f, 0.35f);
            if (Widgets.ButtonText(buttonRect, "Close".Translate()))
            {
                // GUI.color = Color.white;
                ToggleIconPatcher.flag = false;
                Close();
            }
            GUI.color = Color.white;

            /// Scrollview for created recipes
            viewRect = bot;
            scrollRect = viewRect;
            scrollRect.width -= scrollbarWidth;
            scrollRect.height = gc.CreatedRecipes.Count * textLineHeight;
            Widgets.BeginScrollView(viewRect, ref createdRecipesScrollPosition, scrollRect, true);
            List<RecipeDef> toRemove = new List<RecipeDef>();
            foreach (RecipeDef recipe in gc.CreatedRecipes)
            {
                GetRow(out top, out scrollRect, scrollRect, textLineHeight);
                Rect iconRect = top.LeftPartPixels(textLineHeight);
                Rect labelRect = top.RightPartPixels(top.width - textLineHeight);
                iconRect = iconRect.ContractedBy(2f);
                labelRect = labelRect.ContractedBy(2f);
                if (Widgets.ButtonImage(iconRect, TexButton.CloseXSmall))
                {
                    toRemove.Add(recipe);
                }
                Widgets.Label(labelRect, string.Join(", ", recipe.products.ConvertAll(p => p.thingDef.label).ToArray()));
            }
            foreach (RecipeDef recipe in toRemove)
            {
                gc.RemoveRecipe(recipe);
            }
            Widgets.EndScrollView();
        }
    }


    [HarmonyPatch(typeof(ThingDef), "AllRecipes", MethodType.Getter)]

    class ThingDef_AllRecipes_Patch
    {
        static void Postfix(ThingDef __instance, ref List<RecipeDef> __result)
        {
            if (__instance.defName == "PraySpotTwo")
            {
                __result = Current.Game.GetComponent<PrayRecipeGameComponent>()?.CreatedRecipes;
                // var res = Current.Game.GetComponent<PrayRecipeGameComponent>()?.CreatedRecipes;
                // __result = res ?? __result ?? new List<RecipeDef>();
            }
        }
    }
}
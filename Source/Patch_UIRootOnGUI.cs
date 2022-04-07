using HarmonyLib;
using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System;
using System.Linq;

namespace PipetteTool
{
    [HarmonyPatch(typeof(UIRoot_Play), "UIRootOnGUI")]
    public static class Patch_UIRootOnGUI
    {
        private static readonly Func<Gizmo, Gizmo, int> SortByOrder = (Gizmo lhs, Gizmo rhs) => lhs.order.CompareTo(rhs.order);

        private static List<Designator> AllDesignators;
        private static Designator CurrentDesignator;
        private static List<Thing> SelectableList = new List<Thing>();

        private static Thing cacheThing;
        private static int searchStartingIndex;

        public static void Postfix()
        {
            if (Find.Selector.NumSelected == 0
                && Find.MainTabsRoot.OpenTab == null
                && !WorldRendererUtility.WorldRenderedNow
                && !(Find.DesignatorManager.SelectedDesignator is Designator_Place))
            {
                if (CustomKeyBindingDefOf.PipetteToolHotKey.KeyDownEvent)
                {
                    if (AllDesignators == null)
                    {
                        ResolveAllDesignators();
                    }
                    GetSelectableListUnderMouse();
                    Thing thing = SelectableList.FirstOrDefault();
                    if (thing == null)
                    {
                        return;
                    }
                    // if current thing is not cached
                    if (thing != cacheThing)
                    {
                        // start searching at first
                        searchStartingIndex = 0;
                    }
                    CurrentDesignator = GetNextAllowedDesignator(thing);
                    if (CurrentDesignator != null)
                    {
                        Find.DesignatorManager.Select(CurrentDesignator);
                    }
                    else
                    {
                        Find.DesignatorManager.Deselect();
                    }
                    SelectableList.Clear();
                }
            }

            void ResolveAllDesignators()
            {
                AllDesignators = new List<Designator>(Find.ReverseDesignatorDatabase.AllDesignators);
                Type selectSimilarType = AccessTools.TypeByName("AllowTool.Designator_SelectSimilarReverse");
                if (selectSimilarType != null)
                {
                    AllDesignators.RemoveAll((Designator des) => des.GetType() == selectSimilarType);
                }
                AllDesignators.Add(new Designator_Forbid());
                AllDesignators.Add(new Designator_Unforbid());
                AllDesignators.SortStable(SortByOrder);
            }

            Designator GetNextAllowedDesignator(Thing thing)
            {
                cacheThing = thing;
                for (int i = searchStartingIndex; i < AllDesignators.Count; i++)
                {
                    Designator designator = AllDesignators[i];
                    AcceptanceReport acceptanceReport = designator.CanDesignateThing(thing);
                    if (acceptanceReport.Accepted)
                    {
                        // next time we should start from the next designator
                        searchStartingIndex = i + 1;
                        return designator;
                    }
                }
                searchStartingIndex = 0;
                return null;
            }

            void GetSelectableListUnderMouse()
            {
                foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(IntVec3.FromVector3(UI.MouseMapPosition())))
                {
                    // exempt: not selectable or under fog
                    if (ThingSelectionUtility.SelectableByMapClick(thing))
                    {
                        SelectableList.Add(thing);
                    }
                }
                // higher altitude -> lower altitude
                SelectableList.Sort(CompareThingsByDrawAltitude);
            }

            // We sort things list in ascending order,
            // thing at higher altitude is smaller in this comparison.
            int CompareThingsByDrawAltitude(Thing A, Thing B)
            {
                if (A.def.Altitude < B.def.Altitude)
                {
                    return 1;
                }
                if (A.def.Altitude == B.def.Altitude)
                {
                    return 0;
                }
                return -1;
            }
        }
    }
}
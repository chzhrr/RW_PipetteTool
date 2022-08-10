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
        // copied from vanilla GizmoGridDrawer
        private static readonly Func<Gizmo, Gizmo, int> SortByOrder = (Gizmo lhs, Gizmo rhs) => lhs.order.CompareTo(rhs.order);
        
        // all designators in the database
        private static List<Designator> AllAllowedDesignators;

        // current activated designator
        private static Designator CurrentDesignator;

        // temp list for selectable items at mouse position
        private static readonly List<Thing> SelectableList = new List<Thing>();

        // last operated thing
        private static string cachedThingID;

        // last activated designator's index
        private static int searchStartingIndex;

        public static void Postfix()
        {
            if (// nothing is selected
                Find.Selector.NumSelected == 0
                // no tab is activated
                && Find.MainTabsRoot.OpenTab == null
                // not at world view
                && !WorldRendererUtility.WorldRenderedNow
                // Q is the default rotate hot key when holding an item to place
                && !(Find.DesignatorManager.SelectedDesignator is Designator_Place))
            {
                if (CustomKeyBindingDefOf.PipetteToolHotKey.KeyDownEvent)
                {
                    // get all allowed designators at the first time
                    if (AllAllowedDesignators == null)
                    {
                        ResolveAllDesignators();
                    }
                    GetSelectableListUnderMouse();
                    Thing thing = SelectableList.FirstOrDefault();
                    if (thing == null)
                    {
                        return;
                    }
                    // if current thing is not cached or has a designation
                    if (thing.ThingID != cachedThingID || thing.Map?.designationManager?.DesignationOn(thing) != null)
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
                AllAllowedDesignators = new List<Designator>(Find.ReverseDesignatorDatabase.AllDesignators);
                Type selectSimilarType = AccessTools.TypeByName("AllowTool.Designator_SelectSimilarReverse");
                // select similar needs selecting and registering one thing
                if (selectSimilarType != null)
                {
                    AllAllowedDesignators.RemoveAll((Designator des) => des.GetType() == selectSimilarType);
                }
                AllAllowedDesignators.Add(new Designator_Forbid());
                AllAllowedDesignators.Add(new Designator_Unforbid());
                // inspired by GizmoGridDrawer.DrawGizmoGrid
                // same order as gizmos' drawing order
                AllAllowedDesignators.SortStable(SortByOrder);
            }

            Designator GetNextAllowedDesignator(Thing thing)
            {
                // If we have activate one designator for this item,
                // we should activate the next allowed designator next time we press the hot key.
                // Otherwise, restart from the first allowed one.
                cachedThingID = thing.ThingID;
                for (int i = searchStartingIndex; i < AllAllowedDesignators.Count; i++)
                {
                    Designator designator = AllAllowedDesignators[i];
                    AcceptanceReport acceptanceReport = designator.CanDesignateThing(thing);
                    if (acceptanceReport.Accepted)
                    {
                        // next time we should start from the next designator
                        searchStartingIndex = i + 1;
                        return designator;
                    }
                }
                // when switching items, reset
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
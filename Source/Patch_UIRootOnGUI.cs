using HarmonyLib;
using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System;
using System.Linq;
using JetBrains.Annotations;

namespace PipetteTool
{
    [HarmonyPatch(typeof(UIRoot_Play), "UIRootOnGUI")]
    [UsedImplicitly]
    public static class Patch_UIRootOnGUI
    {
        // copied from vanilla GizmoGridDrawer
        private static readonly Func<Gizmo, Gizmo, int> SortByOrder = (Gizmo lhs, Gizmo rhs) => lhs.order.CompareTo(rhs.order);

        // all designators in the database
        private static List<Designator> s_allAllowedDesignators;

        // current activated designator
        private static Designator s_currentDesignator;

        // temp list for selectable items at mouse position
        private static readonly List<Thing> SelectableList = new List<Thing>();

        // last operated thing
        private static string s_cachedThingId;

        // last activated designator's index
        private static int s_searchStartingIndex;

        [UsedImplicitly]
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
                    if (s_allAllowedDesignators == null)
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
                    if (thing.ThingID != s_cachedThingId || thing.Map?.designationManager?.DesignationOn(thing) != null)
                    {
                        // start searching at first
                        s_searchStartingIndex = 0;
                    }
                    s_currentDesignator = GetNextAllowedDesignator(thing);
                    if (s_currentDesignator != null)
                    {
                        Find.DesignatorManager.Select(s_currentDesignator);
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
                s_allAllowedDesignators = new List<Designator>(Find.ReverseDesignatorDatabase.AllDesignators);
                Type selectSimilarType = AccessTools.TypeByName("AllowTool.Designator_SelectSimilarReverse");
                // select similar needs selecting and registering one thing
                if (selectSimilarType != null)
                {
                    s_allAllowedDesignators.RemoveAll((Designator des) => des.GetType() == selectSimilarType);
                }
                s_allAllowedDesignators.Add(new Designator_Forbid());
                s_allAllowedDesignators.Add(new Designator_Unforbid());
                // inspired by GizmoGridDrawer.DrawGizmoGrid
                // same order as gizmos' drawing order
                s_allAllowedDesignators.SortStable(SortByOrder);
            }

            Designator GetNextAllowedDesignator(Thing thing)
            {
                // If we have activate one designator for this item,
                // we should activate the next allowed designator next time we press the hot key.
                // Otherwise, restart from the first allowed one.
                s_cachedThingId = thing.ThingID;
                for (int i = s_searchStartingIndex; i < s_allAllowedDesignators.Count; i++)
                {
                    Designator designator = s_allAllowedDesignators[i];
                    AcceptanceReport acceptanceReport = designator.CanDesignateThing(thing);
                    if (acceptanceReport.Accepted)
                    {
                        // next time we should start from the next designator
                        s_searchStartingIndex = i + 1;
                        return designator;
                    }
                }
                // when switching items, reset
                s_searchStartingIndex = 0;
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
            int CompareThingsByDrawAltitude(Thing thingA, Thing thingB)
            {
                if (thingA.def.Altitude < thingB.def.Altitude)
                {
                    return 1;
                }
                if (thingA.def.Altitude == thingB.def.Altitude)
                {
                    return 0;
                }
                return -1;
            }
        }
    }
}
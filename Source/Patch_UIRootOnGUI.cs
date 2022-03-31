using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
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

        private static List<Designator> AllDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
        private static List<Designator> AllowedDesignators = new List<Designator>();

        private static Thing cacheThing;
        private static int currentIndex;

        public static void Postfix()
        {
            if (Find.Selector.NumSelected == 0 && Find.MainTabsRoot.OpenTab == null && !WorldRendererUtility.WorldRenderedNow)
            {
                if (CustomKeyBindingDefOf.PipetteToolHotKey.KeyDownEvent)
                {
                    List<Thing> selectableList = Find.CurrentMap.thingGrid.ThingsAt(IntVec3.FromVector3(UI.MouseMapPosition())).ToList();
                    Thing thing = selectableList.FirstOrDefault();
                    if (thing == null)
                    {
                        return;
                    }
                    // invoke our shortcut
                    if (Find.DesignatorManager.SelectedDesignator == null || thing != cacheThing)
                    {
                        RefreshAllowedDesignators(thing);
                    }
                    else
                    {
                        currentIndex++;
                    }
                    if (AllowedDesignators.Count > currentIndex)
                    {
                        Find.DesignatorManager.Select(AllowedDesignators[currentIndex]);
                    }
                    else
                    {
                        Find.DesignatorManager.Deselect();
                    }
                    selectableList.Clear();
                }
            }

            void RefreshAllowedDesignators(Thing thing)
            {
                AllowedDesignators.Clear();
                for (int i = 0; i < AllDesignators.Count; i++)
                {
                    Designator designator = AllDesignators[i];
                    AcceptanceReport acceptanceReport = designator.CanDesignateThing(thing);
                    if (acceptanceReport.Accepted)
                    {
                        AllowedDesignators.Add(designator);
                    }
                }
                AllowedDesignators.SortStable(SortByOrder);
                cacheThing = thing;
                currentIndex = 0;
            }
        }
    }
}
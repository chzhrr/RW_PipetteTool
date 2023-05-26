using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using JetBrains.Annotations;

namespace PipetteTool
{
    [HarmonyPatch(typeof(MapInterface), "HandleMapClicks")]
    [UsedImplicitly]
    public static class Patch_MapInterface
    {
        #region members

        /// <summary>
        /// Copied from vanilla GizmoGridDrawer, ordering in gizmo buttons rendering.
        /// </summary>
        private static readonly Func<Gizmo, Gizmo, int> SortByOrder = (lhs, rhs) => lhs.Order.CompareTo(rhs.Order);

        /// <summary>
        /// All reversed designators in the database plus Forbid, UnForbid and BuildCopy.
        /// </summary>
        private static List<Designator> s_allAllowedDesignators;

        private static List<Designator> s_allowedDesignatorsInCurrentCycle;

        /// <summary>
        /// Self learning, we want to minimize total efforts (hot-key pressed times).
        /// </summary>
        private static Dictionary<Designator, int> s_hotkeyPressedTimesByDesignators;

        /// <summary>
        /// Pressed times in this cycle.
        /// </summary>
        private static int s_hotkeyPressedTimesInThisCycle;

        /// <summary>
        /// Current activated designator.
        /// </summary>
        private static Designator s_currentDesignator;

        /// <summary>
        /// Temp list for selectable items at mouse position.
        /// </summary>
        private static readonly List<Thing> s_selectableList = new List<Thing>();

        /// <summary>
        /// Last operated thing.
        /// </summary>
        private static string s_cachedThingId;

        /// <summary>
        /// ThingDef of last operated thing, used to distinguish from standard Place command
        /// or place command in cycle.
        /// </summary>
        private static ThingDef s_cachedThingDef;

        /// <summary>
        /// Index of last activated designator + 1.
        /// </summary>
        private static int s_searchStartingIndex;

        #endregion

        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            MethodInfo insertMethod = typeof(Patch_MapInterface).GetMethod("ProcessInputEvents");
            int targetMethodIndex = codes.FindIndex((x) => x.IsLdarg(0));
            codes.Insert(targetMethodIndex, new CodeInstruction(OpCodes.Call, insertMethod));
            return codes;
        }

        [UsedImplicitly]
        public static void ProcessInputEvents()
        {
            if ( // nothing is selected
                Find.Selector.NumSelected == 0
                // no tab is activated
                && Find.MainTabsRoot.OpenTab == null
                // if we press the hot key Q
                && CustomKeyBindingDefOf.PipetteToolHotKey.KeyDownEvent)
            {
                // avoid conflict with rotate left designator with default hot-key Q
                if (NotPlaceCommandOrInCycle())
                {
                    // get all allowed designators at the first time
                    if (s_allAllowedDesignators == null)
                    {
                        ResolveAllDesignators();
                    }
                    Thing thing = GetFirstThingUnderMouse();
                    // no thing then copy floor
                    if (thing == null)
                    {
                        TerrainDef terrainDef = GetTerrainDefUnderMouse();
                        if (terrainDef != null && (terrainDef.IsCarpet || terrainDef.IsFloor))
                        {
                            Designator_Build buildTerrain = BuildCopyCommandUtility.FindAllowedDesignator(terrainDef);

                            if (buildTerrain != null)
                            {
                                Find.DesignatorManager.Select(buildTerrain);
                            }
                        }
                        return;
                    }
                    // if current thing is not cached or has a designation
                    if (thing.ThingID != s_cachedThingId || thing.Map?.designationManager?.DesignationOn(thing) != null)
                    {
#if DEBUG
                        Log.Message("[Debug] Start a new cycle.");
#endif
                        // start a new cycle
                        s_searchStartingIndex = 0;
                        s_hotkeyPressedTimesInThisCycle = 0;
                        s_allAllowedDesignators.SortStable(CompareDesignatorEfforts);
                        s_allowedDesignatorsInCurrentCycle = new List<Designator>(s_allAllowedDesignators);
                        if (thing is Building building)
                        {
                            Designator_Build buildCopyDesignator = GetBuildCopyDesignator(building);
                            if (buildCopyDesignator != null)
                            {
                                s_allowedDesignatorsInCurrentCycle.Insert(0, buildCopyDesignator);
                            }
                        }
                        s_allowedDesignatorsInCurrentCycle.SortStable(CompareDesignatorEfforts);
                        // if has designation, add cancel to the first
                        // Note that if current thing has designation,
                        // pressing key will always return a cancel designator.
                        // since we restart the cycle every time and cancel is accepted at index 0.
                        // why? The first reason is that you need to cancel first before choosing another designator
                        // The second reason: you need to rebuild allowed designator list to add back cancel designator
                        // otherwise, it is not in the list because in the first cycle it's not accepted and removed
                        if (thing.Map?.designationManager?.DesignationOn(thing) != null)
                        {
                            Designator_Cancel cancel = Find.ReverseDesignatorDatabase.Get<Designator_Cancel>();
                            if (cancel != null)
                            {
                                s_allowedDesignatorsInCurrentCycle.Remove(cancel);
                                s_allowedDesignatorsInCurrentCycle.Insert(0, cancel);
                            }
                        }
                    }
#if DEBUG
                    Log.Message("[Debug] Designator in current cycle:");
                    foreach (var designator in s_allowedDesignatorsInCurrentCycle)
                    {
                        Log.Message($"{designator.Label}");
                    }
                    Log.Message("==============");
#endif
                    s_currentDesignator = GetNextAllowedDesignator(thing);
                    if (s_currentDesignator != null)
                    {
                        Find.DesignatorManager.Select(s_currentDesignator);
                        // don't count build copy command,
                        // they will always be the first command in the cycle
                        if (!(s_currentDesignator is Designator_Build))
                        {
                            if (s_hotkeyPressedTimesByDesignators.ContainsKey(s_currentDesignator))
                            {
                                s_hotkeyPressedTimesByDesignators[s_currentDesignator] += s_hotkeyPressedTimesInThisCycle;
                            }
                            else
                            {
                                s_hotkeyPressedTimesByDesignators[s_currentDesignator] = s_hotkeyPressedTimesInThisCycle;
                            }
                        }
                    }
                    // if there is no other allowed designator, deselect current one
                    else
                    {
                        Find.DesignatorManager.Deselect();
                    }
                    s_selectableList.Clear();
                }
                // deselect terrain copy designator when pressing it again
                if (SelectedTerrainDesignator())
                {
                    Find.DesignatorManager.Deselect();
                }
            }

            bool NotPlaceCommandOrInCycle()
            {
                /*
                 * Scenario 1:
                 * Selected a bed to build from architect menu,
                 * when pressing Q, do nothing and let it rotate.
                 *
                 * Scenario 2:
                 * Press Q to get a build copy command,
                 * and press Q again, since we are in cycle, move forward to next designator.
                 */
                return !(Find.DesignatorManager.SelectedDesignator is Designator_Place des)
                       || (s_cachedThingDef != null && s_cachedThingDef == des?.PlacingDef);
            }

            bool SelectedTerrainDesignator()
            {
                Designator_Place des = Find.DesignatorManager.SelectedDesignator as Designator_Place;
                if (des == null)
                {
                    return false;
                }
                TerrainDef terrainDef = des?.PlacingDef as TerrainDef;
                bool isCarpet = terrainDef?.IsCarpet ?? false;
                bool isFloor = terrainDef?.IsFloor ?? false;
                return isCarpet || isFloor;
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
                if (ModsConfig.BiotechActive)
                {
                    s_allAllowedDesignators.RemoveAll(des => des.GetType() == typeof(Designator_MechControlGroup));
                }
                s_allAllowedDesignators.Add(new Designator_Forbid());
                s_allAllowedDesignators.Add(new Designator_Unforbid());
                // inspired by GizmoGridDrawer.DrawGizmoGrid
                // same order as gizmos' drawing order
                s_allAllowedDesignators.SortStable(SortByOrder);
                s_hotkeyPressedTimesByDesignators = Enumerable.Range(0, s_allAllowedDesignators.Count)
                    .ToDictionary(i => s_allAllowedDesignators[i], i => 0);
            }

            // We sort things list in descending order,
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

            Thing GetFirstThingUnderMouse()
            {
                foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(IntVec3.FromVector3(UI.MouseMapPosition())))
                {
                    // exempt: not selectable or under fog
                    if (ThingSelectionUtility.SelectableByMapClick(thing))
                    {
                        s_selectableList.Add(thing);
                    }
                }
                // higher altitude -> lower altitude
                s_selectableList.Sort(CompareThingsByDrawAltitude);
                return s_selectableList.FirstOrDefault();
            }

            TerrainDef GetTerrainDefUnderMouse()
            {
                IntVec3 pos = IntVec3.FromVector3(UI.MouseMapPosition());
                return pos.GetTerrain(Find.CurrentMap);
            }

            Designator GetNextAllowedDesignator(Thing thing)
            {
                // If we have activate one designator for this item,
                // we should activate the next allowed designator next time we press the hot key.
                // Otherwise, restart from the first allowed one.
                s_cachedThingId = thing.ThingID;
                s_cachedThingDef = thing.def;
#if DEBUG
                Log.Message("[Debug] Getting next allowed designator:");
                Log.Message($"Start from {s_searchStartingIndex}");
#endif
                for (int i = s_searchStartingIndex; i < s_allowedDesignatorsInCurrentCycle.Count; i++)
                {
                    Designator designator = s_allowedDesignatorsInCurrentCycle[i];
                    AcceptanceReport acceptanceReport = designator.CanDesignateThing(thing);
#if DEBUG
                    Log.Message($"Checking {designator.Label}");
                    Log.Message($"Accepted? {acceptanceReport.Accepted}");
                    Log.Message($"Designator is build copy? {designator is Designator_Build}");
#endif
                    if (acceptanceReport.Accepted || designator is Designator_Build)
                    {
                        // next time we should start from the next designator
                        s_searchStartingIndex = i + 1;
                        s_hotkeyPressedTimesInThisCycle += 1;
#if DEBUG
                        Log.Message($"{designator.Label} accepted, start from {s_searchStartingIndex} next time.");
#endif
                        return designator;
                    }
#if DEBUG
                    Log.Message($"Removing {designator.Label} from all allowed set.");
#endif
                    s_allowedDesignatorsInCurrentCycle.RemoveAt(i);
                    i--;
                }
                // restart the cycle if run out of all designators
                s_searchStartingIndex = 0;
#if DEBUG
                Log.Message($"Run out of designators, set start index to 0.");
#endif
                return null;
            }

            int CompareDesignatorEfforts(Designator lhs, Designator rhs)
            {
                bool lhsFixed = s_hotkeyPressedTimesByDesignators.ContainsKey(lhs);
                bool rhsFixed = s_hotkeyPressedTimesByDesignators.ContainsKey(rhs);
                if (!lhsFixed && !rhsFixed)
                {
                    return SortByOrder(lhs, rhs);
                }
                // make sure build copy command is the first in the cycle
                if (!lhsFixed)
                {
                    return -1;
                }
                if (!rhsFixed)
                {
                    return 1;
                }
                // cancel first
                if (lhs is Designator_Cancel)
                {
                    return -1;
                }
                if (rhs is Designator_Cancel)
                {
                    return 1;
                }
                // designator with more pressed times comes first in the cycle
                // we want to minimize total pressed times
                if (s_hotkeyPressedTimesByDesignators[lhs] > s_hotkeyPressedTimesByDesignators[rhs])
                {
                    return -1;
                }
                // if tied, use gizmo rendering order
                return s_hotkeyPressedTimesByDesignators[lhs] == s_hotkeyPressedTimesByDesignators[rhs]
                    ? SortByOrder(lhs, rhs)
                    : 1;
            }

            Designator_Build GetBuildCopyDesignator(Building building)
            {
                Designator_Build des = BuildCopyCommandUtility.FindAllowedDesignator(building.def);
                if (des == null)
                {
                    return null;
                }
                if (building.def.MadeFromStuff && building.Stuff == null)
                {
                    return des;
                }
                ColorInt? glowerColorOverride = null;
                CompGlower comp;
                if ((comp = building.GetComp<CompGlower>()) != null && comp.HasGlowColorOverride)
                {
                    glowerColorOverride = comp.GlowColor;
                }
                des.glowerColorOverride = glowerColorOverride;
                des.SetTemporaryVars(building.Stuff, true);
                ThingDef stuffDefRaw = des.StuffDefRaw;
                des.SetStuffDef(building.Stuff);
                des.styleDef = building.StyleDef;
                des.sourcePrecept = building.StyleSourcePrecept as Precept_Building;
                // TODO: side effect?
                // des.SetStuffDef(stuffDefRaw);
                return des;
            }
        }
    }
}
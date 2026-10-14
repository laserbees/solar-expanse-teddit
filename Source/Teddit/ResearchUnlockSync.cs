using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game;
using Game.CompanyScripts;
using HarmonyLib;
using Manager;
using ScriptableObjectScripts;

namespace Teddit
{
    /// <summary>
    /// The game applies a research's unlocks once, when the research completes
    /// (ResearchDataProgress.CompleteFunction). Research a scenario starts with is completed
    /// during game setup, before Teddit applies mods, so unlocks a mod adds to such research
    /// (e.g. a facility on research_mine_2) never reach the companies that already have it,
    /// and the facility stays locked for the whole game.
    ///
    /// Teddit records every unlock it sets on a research; after all mods are applied, each
    /// company that has completed that research gets the unlocks it doesn't have yet. Only
    /// list-type unlocks (facility, vehicle, spacecraft, research, space component) are
    /// replayed: they can be checked first, so replaying is safe on every load. Other kinds
    /// (bonuses, contracts, ...) could apply twice and are only logged.
    /// </summary>
    internal static class ResearchUnlockSync
    {
        static readonly Dictionary<ResearchDefinition, List<UnlockData>> _unlocks = new Dictionary<ResearchDefinition, List<UnlockData>>();

        static readonly FieldInfo _dictionaryUnlockDataFi = AccessTools.Field(typeof(Company), "dictionaryUnlockData");

        static readonly HashSet<EActionUnlock> _replayable = new HashSet<EActionUnlock>
        {
            EActionUnlock.UnlockFacility,
            EActionUnlock.UnlockVehicleType,
            EActionUnlock.UnlockSpacecraftType,
            EActionUnlock.UnlockResearch,
            EActionUnlock.UnlockSpaceComponent,
        };

        internal static void Reset() => _unlocks.Clear();

        /// <summary>Remembers unlocks a mod set on a research.</summary>
        internal static void Record(ResearchDefinition rd, IEnumerable<object> unlocks)
        {
            if (rd == null || unlocks == null) return;
            if (!_unlocks.TryGetValue(rd, out var list))
            {
                list = new List<UnlockData>();
                _unlocks[rd] = list;
            }
            list.AddRange(unlocks.OfType<UnlockData>());
        }

        /// <summary>
        /// Gives every company the recorded unlocks of research it has already completed.
        /// Call after all mod folders have been applied.
        /// </summary>
        internal static void ApplyToCompletedResearch()
        {
            const string prefix = "[ResearchUnlockSync]";
            var companies = MonoBehaviourSingleton<GameManager>.Instance?.Companies;
            if (companies == null || _unlocks.Count == 0) return;

            int applied = 0;
            foreach (Company company in companies)
            {
                var rm = company?.ResearchManager;
                if (rm == null) continue;
                var unlocked = _dictionaryUnlockDataFi?.GetValue(company) as Dictionary<EActionUnlock, List<string>>;

                foreach (var kv in _unlocks)
                {
                    if (!rm.IsComplete(kv.Key)) continue;
                    foreach (var ud in kv.Value)
                    {
                        if (ud == null || ud.actionUnlock == EActionUnlock.None) continue;
                        if (!_replayable.Contains(ud.actionUnlock))
                        {
                            Plugin.Log.LogInfo($"{prefix} {company.ID}: {kv.Key.ID} is already complete; its {ud.actionUnlock} unlock can't be replayed safely.");
                            continue;
                        }
                        if (unlocked != null && unlocked.TryGetValue(ud.actionUnlock, out var ids) && ids != null && ids.Contains(ud.parameter1))
                            continue;

                        company.UnlockFeature(ud, suppressNewContracts: true);
                        applied++;
                        Plugin.Log.LogInfo($"{prefix} {company.ID}: {ud.actionUnlock} {ud.parameter1} (from completed {kv.Key.ID}).");
                    }
                }
            }
            Plugin.Log.LogInfo($"{prefix} Done — {applied} unlocks applied to already-completed research.");
        }
    }
}

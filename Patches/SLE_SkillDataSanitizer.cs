using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Repairs the runtime Skills.m_skillData dictionary without changing any
    /// valid skill level or accumulated XP values.
    ///
    /// SkillType.None is not a real skill and has no SkillDef. If it appears in
    /// m_skillData, vanilla SkillsDialog.Setup can dereference a null m_info and
    /// abort while building the list. Null dictionary values have the same issue.
    ///
    /// Missing m_info references for otherwise valid skills are reconnected from
    /// Skills.m_skills when possible.
    /// </summary>
    internal static class SLE_SkillDataSanitizer
    {
        internal readonly struct Result
        {
            internal readonly int Removed;
            internal readonly int Repaired;
            internal readonly int Unresolved;

            internal Result(int removed, int repaired, int unresolved)
            {
                Removed = removed;
                Repaired = repaired;
                Unresolved = unresolved;
            }

            internal bool Changed => Removed > 0 || Repaired > 0;
        }

        internal static Result Sanitize(global::Skills skills, string source, bool logUnresolved)
        {
            if (skills == null)
                return default;

            var skillData = Traverse.Create(skills)
                .Field("m_skillData")
                .GetValue<Dictionary<global::Skills.SkillType, global::Skills.Skill>>();

            if (skillData == null || skillData.Count == 0)
                return default;

            var skillDefs = Traverse.Create(skills)
                .Field("m_skills")
                .GetValue<List<global::Skills.SkillDef>>();

            var remove = new List<global::Skills.SkillType>();
            int repaired = 0;
            int unresolved = 0;

            foreach (var pair in skillData)
            {
                // None is a sentinel enum value, never a real player skill.
                // All is also a command/helper sentinel and should never be stored.
                if (pair.Key == global::Skills.SkillType.None ||
                    pair.Key == global::Skills.SkillType.All ||
                    pair.Value == null)
                {
                    remove.Add(pair.Key);
                    continue;
                }

                var skill = pair.Value;
                var info = Traverse.Create(skill)
                    .Field("m_info")
                    .GetValue<global::Skills.SkillDef>();

                if (info != null)
                    continue;

                global::Skills.SkillDef replacement = null;

                if (skillDefs != null)
                {
                    for (int i = 0; i < skillDefs.Count; i++)
                    {
                        var candidate = skillDefs[i];
                        if (candidate != null && candidate.m_skill == pair.Key)
                        {
                            replacement = candidate;
                            break;
                        }
                    }
                }

                if (replacement != null)
                {
                    Traverse.Create(skill)
                        .Field("m_info")
                        .SetValue(replacement);
                    repaired++;
                }
                else
                {
                    unresolved++;

                    if (logUnresolved)
                    {
                        SkillLimitExtenderPlugin.Logger?.LogWarning(
                            $"[SLE] Skill data ({source}): could not resolve SkillDef for {pair.Key} ({(int)pair.Key}), " +
                            $"level={skill.m_level:F2}, accumulator={skill.m_accumulator:F2}");
                    }
                }
            }

            for (int i = 0; i < remove.Count; i++)
                skillData.Remove(remove[i]);

            if (remove.Count > 0 || repaired > 0)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] Skill data ({source}): removed {remove.Count} invalid entr{(remove.Count == 1 ? "y" : "ies")}, " +
                    $"repaired {repaired} SkillDef reference{(repaired == 1 ? "" : "s")}");
            }

            return new Result(remove.Count, repaired, unresolved);
        }
    }

    /// <summary>
    /// Sanitize immediately after the character skill block is loaded. This is
    /// what permanently recovers old saves which already contain SkillType.None:
    /// the bad entry is removed from runtime state and the next save writes only
    /// the valid entries.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills), "Load", new Type[] { typeof(ZPackage) })]
    internal static class SLE_Hook_Skills_Load_Sanitize
    {
        [HarmonyPostfix]
        [HarmonyPriority(-9000)]
        private static void Postfix(global::Skills __instance)
        {
            try
            {
                SLE_SkillDataSanitizer.Sanitize(
                    __instance,
                    "load",
                    logUnresolved: true);
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] Skill data sanitize after load failed: {ex}");
            }
        }
    }
}

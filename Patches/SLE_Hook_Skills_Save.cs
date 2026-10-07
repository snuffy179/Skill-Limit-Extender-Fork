using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Safe Skills.Save replacement.
    ///
    /// Vanilla identifies each saved skill by its SkillType. m_skillData already uses
    /// SkillType as the dictionary key, so persistence does not need to dereference
    /// Skill.m_info. This avoids the upstream NRE when a Skill temporarily loses its
    /// SkillDef reference and also guarantees that the written count exactly matches
    /// the number of entries that follow.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.Save))]
    internal static class SLE_Hook_Skills_Save_Cleanup
    {
        private readonly struct SavedSkill
        {
            internal readonly global::Skills.SkillType Type;
            internal readonly float Level;
            internal readonly float Accumulator;

            internal SavedSkill(global::Skills.SkillType type, float level, float accumulator)
            {
                Type = type;
                Level = level;
                Accumulator = accumulator;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(global::Skills __instance, ZPackage pkg)
        {
            try
            {
                var skillData = Traverse.Create(__instance)
                    .Field("m_skillData")
                    .GetValue<Dictionary<global::Skills.SkillType, global::Skills.Skill>>();

                if (skillData == null)
                {
                    SkillLimitExtenderPlugin.Logger?.LogError(
                        "[SLE] Skills.Save: m_skillData is null; writing an empty valid skill block");
                    pkg.Write(2);
                    pkg.Write(0);
                    return false;
                }

                var entries = new List<SavedSkill>(skillData.Count);
                int skipped = 0;

                foreach (var pair in skillData)
                {
                    if (pair.Key == global::Skills.SkillType.None)
                        continue;

                    var skill = pair.Value;
                    if (skill == null)
                    {
                        skipped++;
                        SkillLimitExtenderPlugin.Logger?.LogWarning(
                            $"[SLE] Skills.Save: skipped null skill entry {pair.Key} ({(int)pair.Key})");
                        continue;
                    }

                    entries.Add(new SavedSkill(
                        pair.Key,
                        skill.m_level,
                        skill.m_accumulator));
                }

                // Build the final list first, then write its exact count.
                // Never continue past a failed entry after the count has been emitted.
                pkg.Write(2);
                pkg.Write(entries.Count);

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    pkg.Write((int)entry.Type);
                    pkg.Write(entry.Level);
                    pkg.Write(entry.Accumulator);
                }

                if (skipped > 0)
                {
                    SkillLimitExtenderPlugin.Logger?.LogWarning(
                        $"[SLE] Skills.Save: wrote {entries.Count} skills and skipped {skipped} null entr{(skipped == 1 ? "y" : "ies")}");
                }
                else if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                {
                    SkillLimitExtenderPlugin.Logger?.LogDebug(
                        $"[SLE] Skills.Save: wrote {entries.Count} skills");
                }

                return false;
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] Skills.Save replacement failed; falling back to vanilla Save: {ex}");
                return true;
            }
        }
    }
}

using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Overrides Skills.GetSkillFactor and applies the bonus factor cap (BonusFactorCap).
    /// Clamps the computed factor to cap/100 while preserving behavior via settings.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.GetSkillFactor))]
    internal static class SLE_Hook_Skills_GetSkillFactor
    {
        [HarmonyPrefix]
        private static bool Prefix(global::Skills __instance, global::Skills.SkillType skillType, ref float __result)
        {
            try
            {
                // SkillType.None / All are sentinel values, not real player skills.
                // Calling Skills.GetSkill(None) can create a bogus None entry in
                // m_skillData, which later crashes SkillsDialog.Setup.
                if (skillType == global::Skills.SkillType.None ||
                    skillType == global::Skills.SkillType.All)
                {
                    __result = 0f;
                    return false;
                }

                if (SLE_SkillFactorOverride.TryGet(skillType, out float overrideFactor))
                {
                    __result = overrideFactor;
                    return false;
                }

                var skill = SLE_SkillsExtensions.GetSkillSafe(__instance, skillType);
                float level = skill != null ? skill.m_level : 0f;

                // Read per-skill settings from the BepInEx config
                int bonusCap = System.Math.Max(1, SkillConfigManager.GetBonusCap(skillType));
                float maxFactor = bonusCap / 100f;

                // Choose scaling mode
                float factor;
                bool relative = SkillConfigManager.IsRelative(skillType);
                if (relative)
                {
                    // Relative scaling: progress against cap * maxFactor
                    int capInt = SkillConfigManager.GetCap(skillType);
                    float capF = capInt > 0 ? capInt : 100f;
                    float progress = capF > 0f ? (level / capF) : 0f;
                    factor = progress * maxFactor;
                }
                else
                {
                    // Vanilla-aligned: denominator 100
                    float denom = SkillConfigManager.GetFactorDenominator(skillType); // fixed 100
                    factor = level / denom;
                }

                // Clamp to [0, cap/100]
                if (factor < 0f) factor = 0f;
                if (factor > maxFactor) factor = maxFactor;

                __result = factor;
                return false; // Skip original implementation
            }
            catch
            {
                // If anything goes wrong, fall back to original
                return true;
            }
        }
    }
}
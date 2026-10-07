using System;
using HarmonyLib;
using UnityEngine;

namespace SkillLimitExtender
{
    /// <summary>
    /// Centralized XP requirement calculation. Both the actual level-up logic
    /// and the displayed progress percentage use this implementation.
    /// </summary>
    internal static class SLE_SkillGrowthCalculator
    {
        internal static float GetRequirement(
            global::Skills.Skill skill,
            global::Skills.SkillType skillType)
        {
            if (skill == null)
                return 1f;

            if (!SkillConfigManager.UseCustomGrowthCurve(skillType))
                return GetVanillaRequirement(skill.m_level);

            float exponent = SkillConfigManager.GetGrowthExponent(skillType);
            float multiplier = SkillConfigManager.GetGrowthMultiplier(skillType);
            float constant = SkillConfigManager.GetGrowthConstant(skillType);

            // Vanilla calculates the XP needed for the NEXT integer level.
            float nextLevel = Mathf.Floor(skill.m_level + 1f);

            // Custom formula documented by SLE:
            //     nextLevel^exponent * multiplier + constant
            float result = Mathf.Pow(nextLevel, exponent) * multiplier + constant;

            if (float.IsNaN(result) || float.IsInfinity(result) || result <= 0f)
            {
                float fallback = GetVanillaRequirement(skill.m_level);
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE Growth Curve] Invalid XP requirement for {skillType}: {result}. " +
                    $"Falling back to vanilla value {fallback:F2}.");
                return fallback;
            }

            if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
            {
                float vanilla = GetVanillaRequirement(skill.m_level);
                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE Growth Curve] Skill: {skillType}, Level: {skill.m_level:F1}, " +
                    $"Mode: Custom, Params: exp={exponent:F2}, mult={multiplier:F2}, const={constant:F2}, " +
                    $"Vanilla: {vanilla:F2}, Custom: {result:F2}");
            }

            return result;
        }

        internal static float GetVanillaRequirement(float level)
        {
            return Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f + 0.5f;
        }
    }

    /// <summary>
    /// Patches the requirement that Skills.Skill.Raise actually uses.
    /// The original implementation is left untouched unless a custom curve is
    /// enabled for the current skill.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills.Skill), "GetNextLevelRequirement")]
    internal static class SLE_Skill_GetNextLevelRequirement_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::Skills.Skill __instance, ref float __result)
        {
            if (!SLE_SkillHelpers.TryGetSkillType(__instance, out var skillType))
                return true;

            if (!SkillConfigManager.UseCustomGrowthCurve(skillType))
                return true;

            __result = SLE_SkillGrowthCalculator.GetRequirement(__instance, skillType);
            return false;
        }
    }

    /// <summary>
    /// Keeps the skill progress UI consistent with configured caps and with the
    /// same XP requirement used by the actual level-up calculation.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills.Skill), nameof(global::Skills.Skill.GetLevelPercentage))]
    internal static class SLE_Skill_GetLevelPercentage_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::Skills.Skill __instance, ref float __result)
        {
            try
            {
                if (!SLE_SkillHelpers.TryGetSkillType(__instance, out var skillType))
                    return true;

                int skillCap = SkillConfigManager.GetCap(skillType);
                if (__instance.m_level >= skillCap)
                {
                    __result = 0f;
                    return false;
                }

                float requirement = SLE_SkillGrowthCalculator.GetRequirement(__instance, skillType);
                __result = requirement > 0f
                    ? Mathf.Clamp01(__instance.m_accumulator / requirement)
                    : 0f;

                return false;
            }
            catch (Exception ex)
            {
                if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                {
                    SkillLimitExtenderPlugin.Logger?.LogError(
                        $"[SLE Growth Curve] GetLevelPercentage patch failed: {ex.Message}");
                }

                return true;
            }
        }
    }
}

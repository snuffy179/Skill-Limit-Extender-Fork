using HarmonyLib;
using UnityEngine;

namespace SkillLimitExtender
{
    /// <summary>
    /// Makes the displayed skill damage range use SLE's extended damage curve.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.GetRandomSkillRange))]
    internal static class SLE_Skill_GetRandomSkillRange_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            global::Skills __instance,
            ref float min,
            ref float max,
            global::Skills.SkillType skillType)
        {
            float skillFactor = __instance.GetSkillFactor(skillType);
            SLE_ExtendedScaling.GetDamageRange(skillFactor, out min, out max);
            return false;
        }
    }

    /// <summary>
    /// Makes actual attack damage use the same range as the displayed tooltip.
    /// </summary>
    [HarmonyPatch(
        typeof(global::Skills),
        nameof(global::Skills.GetRandomSkillFactor),
        new[] { typeof(global::Skills.SkillType) })]
    internal static class SLE_Skill_GetRandomSkillFactor_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            global::Skills __instance,
            global::Skills.SkillType skillType,
            ref float __result)
        {
            float skillFactor = __instance.GetSkillFactor(skillType);
            SLE_ExtendedScaling.GetDamageRange(skillFactor, out float min, out float max);
            __result = Mathf.Lerp(min, max, Random.value);
            return false;
        }
    }
}

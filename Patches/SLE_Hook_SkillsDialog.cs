using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SkillLimitExtender
{
    /// <summary>
    /// Keeps vanilla SkillsDialog.Setup intact, but sanitizes invalid skill data
    /// before the dialog is built and corrects presentation for raw levels above
    /// the vanilla 100-level UI assumptions.
    /// </summary>
    [HarmonyPatch(typeof(global::SkillsDialog), nameof(global::SkillsDialog.Setup))]
    internal static class SLE_Hook_SkillsDialog_LevelBars
    {
        [HarmonyPrefix]
        private static bool Prefix(ref Player player)
        {
            try
            {
                if (player == null)
                    player = SLE_SkillHelpers.GetSafeLocalPlayer();

                if (player == null)
                {
                    SkillLimitExtenderPlugin.Logger?.LogWarning(
                        "[SLE] SkillsDialog.Setup called without a valid player; skipping dialog setup");
                    return false;
                }

                var skills = player.GetSkills();
                if (skills == null)
                {
                    SkillLimitExtenderPlugin.Logger?.LogWarning(
                        "[SLE] SkillsDialog.Setup called while Player.GetSkills() is null; skipping dialog setup");
                    return false;
                }

                SLE_SkillDataSanitizer.Sanitize(
                    skills,
                    "SkillsDialog",
                    logUnresolved: true);

                if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                {
                    SkillLimitExtenderPlugin.Logger?.LogDebug(
                        $"[SLE] SkillsDialog.Setup - Player: {player.GetPlayerName()}");
                }

                return true;
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] SkillsDialog.Setup preflight failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Valheim 1.0 has a separate blue skill-bonus display. Its vanilla UI
        /// assumes stored levels never exceed 100 and can misinterpret an extended
        /// raw level as a bonus. After vanilla successfully builds the rows, write
        /// the actual stored level and bars back into the row.
        ///
        /// We preserve a genuine positive effective-level bonus when the game's
        /// GetSkillLevel result is greater than the raw stored level. If vanilla
        /// clamps the effective result below an extended raw level, it is not shown
        /// as a fake blue bonus.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(-9000)]
        private static void Postfix(global::SkillsDialog __instance, Player player)
        {
            try
            {
                if (__instance == null || player == null || __instance.m_elements == null)
                    return;

                var skills = player.GetSkills();
                if (skills == null)
                    return;

                var skillList = skills.GetSkillList();
                if (skillList == null)
                    return;

                int count = Math.Min(skillList.Count, __instance.m_elements.Count);

                for (int i = 0; i < count; i++)
                {
                    var skill = skillList[i];
                    var row = __instance.m_elements[i];

                    if (skill == null || skill.m_info == null || row == null)
                        continue;

                    var skillType = skill.m_info.m_skill;
                    if (skillType == global::Skills.SkillType.None ||
                        skillType == global::Skills.SkillType.All)
                        continue;

                    float rawLevel = skill.m_level;
                    float cap = Math.Max(1f, SkillConfigManager.GetCap(skillType));

                    // Vanilla's raw level text is correct below 100. Above 100,
                    // explicitly overwrite it so the UI does not show e.g. 100+100.
                    if (rawLevel > 100f)
                    {
                        SetChildText(row, "leveltext", Mathf.FloorToInt(rawLevel).ToString());

                        float effectiveLevel;
                        try
                        {
                            effectiveLevel = skills.GetSkillLevel(skillType);
                        }
                        catch
                        {
                            effectiveLevel = rawLevel;
                        }

                        float realBonus = Mathf.Max(0f, effectiveLevel - rawLevel);
                        SetChildText(
                            row,
                            "bonustext",
                            realBonus >= 1f ? $"+{Mathf.FloorToInt(realBonus)}" : string.Empty);

                        // Keep the yellow/base and blue/total bars meaningful for
                        // extended caps. If vanilla clamps GetSkillLevel below the raw
                        // level, the total bar is never allowed to shrink below base.
                        SetChildBar(row, "currentlevel", Mathf.Clamp01(rawLevel / cap));
                        SetChildBar(row, "levelbar_total", Mathf.Clamp01(Mathf.Max(rawLevel, effectiveLevel) / cap));
                    }
                }
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] SkillsDialog extended-level presentation fix failed: {ex.Message}");
            }
        }

        private static void SetChildText(GameObject row, string childName, string value)
        {
            var child = Utils.FindChild(row.transform, childName, (IterativeSearchType)0);
            if (child == null)
                return;

            // Avoid a hard compile-time dependency on Unity.TextMeshPro.dll.
            var components = child.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                var type = component.GetType();
                var property = AccessTools.Property(type, "text");
                if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                    continue;

                property.SetValue(component, value, null);
                return;
            }
        }

        private static void SetChildBar(GameObject row, string childName, float value)
        {
            var child = Utils.FindChild(row.transform, childName, (IterativeSearchType)0);
            if (child == null)
                return;

            var bar = child.GetComponent<GuiBar>();
            if (bar != null)
                bar.SetValue(value);
        }

        [HarmonyTranspiler]
        [HarmonyPriority(-9000)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var getUiDenominator = AccessTools.Method(
                typeof(SkillConfigManager),
                nameof(SkillConfigManager.GetUiDenominator));

            if (getUiDenominator == null)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    "[SLE] SkillsDialog: GetUiDenominator method missing; keeping vanilla UI denominator");
                return codes;
            }

            int replacementCount = 0;

            for (int i = 0; i < codes.Count - 1; i++)
            {
                var instruction = codes[i];
                var next = codes[i + 1];

                if (instruction.opcode != OpCodes.Ldc_R4 ||
                    !(instruction.operand is float value) ||
                    Math.Abs(value - 100f) >= 0.0001f ||
                    next.opcode != OpCodes.Div)
                {
                    continue;
                }

                bool feedsSetValue = false;
                for (int j = i + 1; j < Math.Min(codes.Count, i + 12); j++)
                {
                    if ((codes[j].opcode == OpCodes.Call || codes[j].opcode == OpCodes.Callvirt) &&
                        codes[j].operand?.ToString()?.Contains("SetValue") == true)
                    {
                        feedsSetValue = true;
                        break;
                    }
                }

                if (!feedsSetValue)
                    continue;

                instruction.opcode = OpCodes.Call;
                instruction.operand = getUiDenominator;
                replacementCount++;
            }

            SkillLimitExtenderPlugin.Logger?.LogInfo(
                $"[SLE] SkillsDialog: replaced {replacementCount} UI denominator constant(s)");
            return codes;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Player player)
        {
            if (__exception == null)
                return null;

            try
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] SkillsDialog.Setup threw {__exception.GetType().Name}: {__exception.Message}");

                var skills = player?.GetSkills();
                if (skills != null)
                {
                    SLE_SkillDataSanitizer.Sanitize(
                        skills,
                        "SkillsDialog-finalizer",
                        logUnresolved: true);
                }
            }
            catch (Exception diagnosticEx)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] SkillsDialog diagnostics failed: {diagnosticEx.Message}");
            }

            return __exception;
        }
    }
}

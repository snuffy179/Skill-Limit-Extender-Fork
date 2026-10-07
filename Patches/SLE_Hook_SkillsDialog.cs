using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Keeps vanilla SkillsDialog.Setup intact, sanitizes invalid skill data
    /// before the dialog is built, and replaces the vanilla level-100 UI
    /// denominator with SLE's configured denominator.
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

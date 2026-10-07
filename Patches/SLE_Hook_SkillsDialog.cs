using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Keeps the vanilla SkillsDialog.Setup implementation intact, while:
    /// 1. repairing missing Skill.m_info references before the dialog is built;
    /// 2. replacing the vanilla UI denominator (100) with SLE's configured UI denominator.
    ///
    /// The transpiler deliberately changes only the existing ldc.r4 instruction in-place.
    /// This preserves Harmony labels / exception blocks and does not change the IL stack shape.
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

                int repaired = RepairMissingSkillInfo(skills, logUnresolved: true);
                if (repaired > 0)
                {
                    SkillLimitExtenderPlugin.Logger?.LogWarning(
                        $"[SLE] SkillsDialog: repaired {repaired} missing skill definition reference(s) before building the UI");
                }

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
        /// A loaded Skill normally keeps a reference to its SkillDef in m_info.
        /// If that reference is missing, vanilla SkillsDialog.Setup dereferences it while
        /// creating rows and can abort half-way through the list with NullReferenceException.
        ///
        /// Reconnect m_info from Skills.m_skills without changing level or XP data.
        /// </summary>
        private static int RepairMissingSkillInfo(global::Skills skills, bool logUnresolved)
        {
            var skillData = Traverse.Create(skills)
                .Field("m_skillData")
                .GetValue<Dictionary<global::Skills.SkillType, global::Skills.Skill>>();

            if (skillData == null || skillData.Count == 0)
                return 0;

            var skillDefs = Traverse.Create(skills)
                .Field("m_skills")
                .GetValue<List<global::Skills.SkillDef>>();

            int repaired = 0;

            foreach (var pair in skillData)
            {
                var skill = pair.Value;
                if (skill == null)
                {
                    if (logUnresolved)
                    {
                        SkillLimitExtenderPlugin.Logger?.LogWarning(
                            $"[SLE] SkillsDialog: skill entry {pair.Key} ({(int)pair.Key}) is null");
                    }
                    continue;
                }

                var currentInfo = Traverse.Create(skill)
                    .Field("m_info")
                    .GetValue<global::Skills.SkillDef>();

                if (currentInfo != null)
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
                else if (logUnresolved)
                {
                    SkillLimitExtenderPlugin.Logger?.LogWarning(
                        $"[SLE] SkillsDialog: could not resolve SkillDef for {pair.Key} ({(int)pair.Key}), " +
                        $"level={skill.m_level:F2}, accumulator={skill.m_accumulator:F2}");
                }
            }

            return repaired;
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

                // Only touch the two divisions that feed UI SetValue calls.
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

                // Mutate the existing CodeInstruction instead of replacing/inserting it.
                // This preserves labels and exception-block metadata exactly.
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
                    RepairMissingSkillInfo(skills, logUnresolved: true);
            }
            catch (Exception diagnosticEx)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] SkillsDialog diagnostics failed: {diagnosticEx.Message}");
            }

            // Do not swallow the exception. If another UI incompatibility remains,
            // keep Unity's normal stack trace so it can be diagnosed correctly.
            return __exception;
        }
    }
}

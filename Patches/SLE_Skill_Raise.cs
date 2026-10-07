using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Replaces the vanilla level-100 checks inside Skills.Skill.Raise with
    /// the configured cap for the current skill.
    /// </summary>
    [HarmonyPatch(typeof(global::Skills.Skill), nameof(global::Skills.Skill.Raise))]
    internal static class SLE_Skill_Raise_Transpiler
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var getSkillType = AccessTools.Method(
                typeof(SLE_SkillHelpers),
                nameof(SLE_SkillHelpers.GetSkillTypeOrNone));

            var getCap = AccessTools.Method(
                typeof(SkillConfigManager),
                nameof(SkillConfigManager.GetCap));

            if (getSkillType == null || getCap == null)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    "[SLE] Skill.Raise transpiler failed: required helper method was not found.");
                return codes;
            }

            int replacementCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];

                if (instruction.opcode != OpCodes.Ldc_R4 ||
                    instruction.operand is not float value ||
                    Math.Abs(value - 100f) >= 0.0001f)
                {
                    continue;
                }

                var replacement = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, getSkillType),
                    new CodeInstruction(OpCodes.Call, getCap),
                    new CodeInstruction(OpCodes.Conv_R4)
                };

                // Preserve labels and exception blocks that belonged to the
                // original instruction. Losing these can produce invalid IL.
                replacement[0].labels.AddRange(instruction.labels);
                replacement[0].blocks.AddRange(instruction.blocks);

                codes[i] = replacement[0];
                codes.InsertRange(i + 1, replacement.GetRange(1, replacement.Count - 1));
                i += replacement.Count - 1;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    "[SLE] Skill.Raise transpiler found no level-100 constants to replace.");
            }
            else
            {
                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE] Skill.Raise: replaced {replacementCount} level-100 checks with configured skill caps.");
            }

            return codes;
        }
    }
}

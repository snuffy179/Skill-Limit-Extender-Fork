using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SkillLimitExtender
{
    /// <summary>
    /// Shared helpers for mechanics whose vanilla skill formula decreases a
    /// resource cost or action time toward a level-100 endpoint.
    /// </summary>
    internal static class SLE_ExtendedScalingPatches
    {
        private static readonly MethodInfo? VanillaLerp = AccessTools.Method(
            typeof(Mathf),
            nameof(Mathf.Lerp),
            new[] { typeof(float), typeof(float), typeof(float) });

        private static readonly MethodInfo? ExtendedLerp = AccessTools.Method(
            typeof(SLE_ExtendedScaling),
            nameof(SLE_ExtendedScaling.ExtendedCostLerp));

        internal sealed class CostState
        {
            internal bool Active;
            internal float SkillFactor;
            internal IDisposable? OverrideScope;

            internal void Restore()
            {
                OverrideScope?.Dispose();
                OverrideScope = null;
            }
        }

        internal static IEnumerable<CodeInstruction> ReplaceLerps(
            IEnumerable<CodeInstruction> instructions,
            string methodName,
            int expectedCount)
        {
            var codes = new List<CodeInstruction>(instructions);

            if (VanillaLerp == null || ExtendedLerp == null)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] {methodName}: could not resolve Lerp helper methods.");
                return codes;
            }

            int count = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code.opcode == OpCodes.Call && Equals(code.operand, VanillaLerp))
                {
                    code.operand = ExtendedLerp;
                    count++;
                }
            }

            if (count != expectedCount)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] {methodName}: expected {expectedCount} Mathf.Lerp call(s), found {count}. " +
                    "Only the calls that were found were replaced.");
            }
            else
            {
                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE] {methodName}: extended {count} decreasing skill Lerp(s) above level 100.");
            }

            return codes;
        }

        internal static CostState BeginCostOverride(Player? player, global::Skills.SkillType skillType)
        {
            var state = new CostState();

            if (!SLE_ExtendedScaling.CostScalingEnabled ||
                player == null ||
                skillType == global::Skills.SkillType.None)
            {
                return state;
            }

            float skillFactor = player.GetSkillFactor(skillType);
            if (skillFactor <= 1f)
                return state;

            state.Active = true;
            state.SkillFactor = skillFactor;
            state.OverrideScope = SLE_SkillFactorOverride.Push(skillType, 1f);
            return state;
        }

        internal static CostState BeginAttackCostOverride(
            Character? character,
            ItemDrop.ItemData? item = null)
        {
            if (character is not Player player)
                return new CostState();

            ItemDrop.ItemData? weapon = item;
            if (weapon == null && character is Humanoid humanoid)
                weapon = humanoid.GetCurrentWeapon();

            global::Skills.SkillType skillType =
                weapon?.m_shared?.m_skillType ?? global::Skills.SkillType.None;

            return BeginCostOverride(player, skillType);
        }

        internal static global::Skills.SkillType GetBuildSkill(Player player, ItemDrop.ItemData? item = null)
        {
            if (item?.m_shared != null && item.m_shared.m_skillType != global::Skills.SkillType.None)
                return item.m_shared.m_skillType;

            try
            {
                PieceTable? table = Traverse.Create(player)
                    .Field("m_buildPieces")
                    .GetValue<PieceTable>();

                if (table != null && table.m_skill != global::Skills.SkillType.None)
                    return table.m_skill;
            }
            catch
            {
                // Fall through to the equipped item.
            }

            ItemDrop.ItemData? equipped = player.GetCurrentWeapon();
            return equipped?.m_shared?.m_skillType ?? global::Skills.SkillType.None;
        }

        internal static void FinishCost(ref float result, CostState? state)
        {
            if (state == null)
                return;

            state.Restore();

            if (!state.Active)
                return;

            float level100Value = Mathf.Max(0f, result);
            result = level100Value * SLE_ExtendedScaling.GetExtendedCostMultiplier(state.SkillFactor);
        }

        internal static Exception? RestoreOnException(Exception? exception, CostState? state)
        {
            state?.Restore();
            return exception;
        }
    }

    // ---------------------------------------------------------------------
    // Vanilla methods that clamp their skill Lerp at factor 1.0.
    // Replace the Lerp with a helper that preserves vanilla 0-100 and extends
    // only decreasing values above 100.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(Player), "CheckRun", new Type[] { typeof(Vector3), typeof(float) })]
    internal static class SLE_Player_CheckRun_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "Player.CheckRun", 1);
    }

    [HarmonyPatch(typeof(Player), "OnSwimming", new Type[] { typeof(Vector3), typeof(float) })]
    internal static class SLE_Player_OnSwimming_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "Player.OnSwimming", 1);
    }

    [HarmonyPatch(typeof(Player), "GetDodgeStaminaUse")]
    internal static class SLE_Player_GetDodgeStaminaUse_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "Player.GetDodgeStaminaUse", 1);
    }

    // Sneak uses Lerp(1, 0.25, sqrt(skillFactor)) in vanilla. Above 100
    // that Lerp clamps at the level-100 endpoint. Keep the vanilla formula
    // untouched, but reduce the base drain by our asymptotic continuation so
    // the final cost becomes valueAt100 / skillFactor^ExtendedCostExponent.
    [HarmonyPatch(typeof(Player), "OnSneaking", new Type[] { typeof(float) })]
    internal static class SLE_Player_OnSneaking_Extended_Patch
    {
        private sealed class State
        {
            internal bool Active;
            internal float OriginalDrain;
        }

        [HarmonyPrefix]
        private static void Prefix(Player __instance, out State __state)
        {
            __state = new State();

            if (!SLE_ExtendedScaling.CostScalingEnabled ||
                __instance != Player.m_localPlayer)
            {
                return;
            }

            float skillFactor = __instance.GetSkillFactor(global::Skills.SkillType.Sneak);
            if (skillFactor <= 1f)
                return;

            __state.Active = true;
            __state.OriginalDrain = __instance.m_sneakStaminaDrain;
            __instance.m_sneakStaminaDrain =
                __state.OriginalDrain * SLE_ExtendedScaling.GetExtendedCostMultiplier(skillFactor);
        }

        [HarmonyPostfix]
        private static void Postfix(Player __instance, State __state)
        {
            if (__state?.Active == true)
                __instance.m_sneakStaminaDrain = __state.OriginalDrain;
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, Player __instance, State __state)
        {
            if (__state?.Active == true)
                __instance.m_sneakStaminaDrain = __state.OriginalDrain;

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "GetAttackDrawPercentage")]
    internal static class SLE_Humanoid_GetAttackDrawPercentage_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "Humanoid.GetAttackDrawPercentage", 1);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetWeaponLoadingTime")]
    internal static class SLE_ItemData_GetWeaponLoadingTime_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "ItemData.GetWeaponLoadingTime", 1);
    }

    [HarmonyPatch(typeof(Sadle), "UpdateRiding", new Type[] { typeof(float) })]
    internal static class SLE_Sadle_UpdateRiding_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "Sadle.UpdateRiding", 1);
    }

    [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
    internal static class SLE_FishingFloat_FixedUpdate_Extended_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SLE_ExtendedScalingPatches.ReplaceLerps(instructions, "FishingFloat.FixedUpdate", 3);
    }

    // ---------------------------------------------------------------------
    // Attack resource costs. Vanilla can extrapolate linearly beyond skill
    // factor 1 and eventually produce zero/negative costs. Calculate the
    // original method at factor 1, then asymptotically reduce that baseline.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(Attack), "GetAttackStamina", new Type[] { })]
    internal static class SLE_Attack_GetAttackStamina_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Humanoid ___m_character,
            out SLE_ExtendedScalingPatches.CostState __state)
        {
            __state = SLE_ExtendedScalingPatches.BeginAttackCostOverride(___m_character);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    [HarmonyPatch(
        typeof(Attack),
        "GetAttackEitr",
        new Type[] { typeof(Character), typeof(ItemDrop.ItemData) })]
    internal static class SLE_Attack_GetAttackEitr_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __0,
            ItemDrop.ItemData __1,
            out SLE_ExtendedScalingPatches.CostState __state)
        {
            __state = SLE_ExtendedScalingPatches.BeginAttackCostOverride(__0, __1);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    [HarmonyPatch(typeof(Attack), "GetAttackHealth", new Type[] { })]
    internal static class SLE_Attack_GetAttackHealth_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Humanoid ___m_character,
            out SLE_ExtendedScalingPatches.CostState __state)
        {
            __state = SLE_ExtendedScalingPatches.BeginAttackCostOverride(___m_character);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    // ---------------------------------------------------------------------
    // Draw/hold resource costs. These are pure item cost getters, so the same
    // level-100 baseline technique can be used safely.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetDrawStaminaDrain", new Type[] { })]
    internal static class SLE_ItemData_GetDrawStaminaDrain_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemDrop.ItemData __instance, out SLE_ExtendedScalingPatches.CostState __state)
        {
            global::Skills.SkillType skillType =
                __instance?.m_shared?.m_skillType ?? global::Skills.SkillType.None;

            __state = SLE_ExtendedScalingPatches.BeginCostOverride(Player.m_localPlayer, skillType);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetDrawEitrDrain", new Type[] { })]
    internal static class SLE_ItemData_GetDrawEitrDrain_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemDrop.ItemData __instance, out SLE_ExtendedScalingPatches.CostState __state)
        {
            global::Skills.SkillType skillType =
                __instance?.m_shared?.m_skillType ?? global::Skills.SkillType.None;

            __state = SLE_ExtendedScalingPatches.BeginCostOverride(Player.m_localPlayer, skillType);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    // ---------------------------------------------------------------------
    // Building/farming/cooking tool costs.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(Player), "GetBuildStamina", new Type[] { })]
    internal static class SLE_Player_GetBuildStamina_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, out SLE_ExtendedScalingPatches.CostState __state)
        {
            global::Skills.SkillType skillType = SLE_ExtendedScalingPatches.GetBuildSkill(__instance);
            __state = SLE_ExtendedScalingPatches.BeginCostOverride(__instance, skillType);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    [HarmonyPatch(typeof(Player), "GetPlaceDurability", new Type[] { typeof(ItemDrop.ItemData) })]
    internal static class SLE_Player_GetPlaceDurability_Extended_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Player __instance,
            ItemDrop.ItemData __0,
            out SLE_ExtendedScalingPatches.CostState __state)
        {
            global::Skills.SkillType skillType = SLE_ExtendedScalingPatches.GetBuildSkill(__instance, __0);
            __state = SLE_ExtendedScalingPatches.BeginCostOverride(__instance, skillType);
        }

        [HarmonyPostfix]
        private static void Postfix(ref float __result, SLE_ExtendedScalingPatches.CostState __state)
        {
            SLE_ExtendedScalingPatches.FinishCost(ref __result, __state);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, SLE_ExtendedScalingPatches.CostState __state) =>
            SLE_ExtendedScalingPatches.RestoreOnException(__exception, __state);
    }

    // ---------------------------------------------------------------------
    // Blocking. Valheim already scales block power above factor 1 when SLE
    // exposes a larger skill factor, so multiplying the entire final result
    // would double-scale it. Instead, rebuild the result from the level-0
    // value plus the vanilla level-100 skill bonus, then extend only that
    // bonus with the same final multiplier used by damage.
    //
    // Example:
    //   blockAt0   = 78
    //   blockAt100 = 117
    //   factor 2 multiplier = 1.5
    //   result = 78 + (117 - 78) * 1.5 = 136.5
    // ---------------------------------------------------------------------

    [HarmonyPatch(
        typeof(ItemDrop.ItemData),
        "GetBlockPower",
        new Type[] { typeof(int), typeof(float) })]
    internal static class SLE_ItemData_GetBlockPower_Extended_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ItemDrop.ItemData __instance,
            ref float __result,
            int __0,
            float __1)
        {
            if (!SLE_ExtendedScaling.BlockingScalingEnabled || __1 <= 1f)
                return;

            // Vanilla GetBlockPower is:
            // baseBlock + baseBlock * skillFactor * 0.5.
            // Therefore the complete vanilla skill bonus at level 100 is
            // exactly 50% of the level-0 block value.
            float blockAt0 = __instance.GetBaseBlockPower(__0);
            float level100SkillBonus = blockAt0 * 0.5f;

            float multiplier =
                SLE_ExtendedScaling.GetExtendedFinalMultiplier(__1);

            __result =
                blockAt0 +
                level100SkillBonus * multiplier;
        }
    }

    // ---------------------------------------------------------------------
    // Optional Jump-based safe fall distance.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
    internal static class SLE_Character_UpdateGroundContact_JumpFall_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            ref float ___m_maxAirAltitude,
            bool ___m_groundContact)
        {
            if (!SLE_ExtendedScaling.SafeFallDistanceScalingEnabled ||
                !___m_groundContact ||
                __instance != Player.m_localPlayer)
            {
                return;
            }

            Player? player = __instance as Player;
            if (player == null)
                return;

            float jumpFactor = player.GetSkillFactor(global::Skills.SkillType.Jump);
            if (jumpFactor <= 1f)
                return;

            float currentY = __instance.transform.position.y;
            float fallDistance = ___m_maxAirAltitude - currentY;
            const float vanillaSafeDistance = 4f;

            if (fallDistance <= vanillaSafeDistance)
                return;

            float safeDistance = SLE_ExtendedScaling.GetSafeFallDistance(jumpFactor);
            float extraSafeDistance = Mathf.Max(0f, safeDistance - vanillaSafeDistance);
            float adjustedFallDistance = Mathf.Max(0f, fallDistance - extraSafeDistance);

            ___m_maxAirAltitude = currentY + adjustedFallDistance;
        }
    }
}

using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace SkillLimitExtender
{
    /// <summary>
    /// Global non-XP gameplay scaling.
    ///
    /// Skill-specific XP curves remain in their own sections. Mathematical
    /// controls that change damage, costs, action times, durability use, or
    /// fall-distance scaling live here in [3 - Extended Scaling].
    /// </summary>
    internal static class SLE_ExtendedScaling
    {
        internal static ConfigEntry<bool> EnableDamageScaling { get; private set; } = null!;
        internal static ConfigEntry<int> DamageReferenceSkillLevel { get; private set; } = null!;
        internal static ConfigEntry<float> DamageReferenceMultiplier { get; private set; } = null!;
        internal static ConfigEntry<float> VanillaDamageMultiplier { get; private set; } = null!;
        internal static ConfigEntry<float> VanillaDamageExponent { get; private set; } = null!;

        internal static ConfigEntry<bool> EnableExtendedCostScaling { get; private set; } = null!;
        internal static ConfigEntry<float> ExtendedCostExponent { get; private set; } = null!;
        internal static ConfigEntry<bool> EnableBlockingScaling { get; private set; } = null!;
        internal static ConfigEntry<bool> ScaleSafeFallDistance { get; private set; } = null!;
        internal static ConfigEntry<float> SafeFallDistanceExponent { get; private set; } = null!;

        private static bool _hasServerOverride;
        private static bool _serverEnableDamageScaling;
        private static int _serverDamageReferenceSkillLevel;
        private static float _serverDamageReferenceMultiplier;
        private static float _serverVanillaDamageMultiplier;
        private static float _serverVanillaDamageExponent;
        private static bool _serverEnableExtendedCostScaling;
        private static float _serverExtendedCostExponent;
        private static bool _serverEnableBlockingScaling;
        private static bool _serverScaleSafeFallDistance;
        private static float _serverSafeFallDistanceExponent;

        internal static bool DamageScalingEnabled =>
            _hasServerOverride ? _serverEnableDamageScaling : EnableDamageScaling.Value;

        internal static bool CostScalingEnabled =>
            _hasServerOverride ? _serverEnableExtendedCostScaling : EnableExtendedCostScaling.Value;

        internal static bool BlockingScalingEnabled =>
            _hasServerOverride ? _serverEnableBlockingScaling : EnableBlockingScaling.Value;

        internal static bool SafeFallDistanceScalingEnabled =>
            _hasServerOverride ? _serverScaleSafeFallDistance : ScaleSafeFallDistance.Value;

        private static int EffectiveDamageReferenceSkillLevel =>
            _hasServerOverride ? _serverDamageReferenceSkillLevel : DamageReferenceSkillLevel.Value;

        private static float EffectiveDamageReferenceMultiplier =>
            _hasServerOverride ? _serverDamageReferenceMultiplier : DamageReferenceMultiplier.Value;

        private static float EffectiveVanillaDamageMultiplier =>
            _hasServerOverride ? _serverVanillaDamageMultiplier : VanillaDamageMultiplier.Value;

        private static float EffectiveVanillaDamageExponent =>
            _hasServerOverride ? _serverVanillaDamageExponent : VanillaDamageExponent.Value;

        private static float EffectiveExtendedCostExponent =>
            _hasServerOverride ? _serverExtendedCostExponent : ExtendedCostExponent.Value;

        private static float EffectiveSafeFallDistanceExponent =>
            _hasServerOverride ? _serverSafeFallDistanceExponent : SafeFallDistanceExponent.Value;

        internal static void Initialize(ConfigFile config)
        {
            const string section = "3 - Extended Scaling";

            EnableDamageScaling = config.Bind(
                section,
                "EnableDamageScaling",
                true,
                "Enable SLE's damage scaling. From level 0-100 the configurable vanilla curve is used; " +
                "above 100 the configured level-100 damage becomes the baseline for the extended curve. " +
                "Blocking has a separate EnableBlockingScaling toggle but can reuse the same reference curve.");

            DamageReferenceSkillLevel = config.Bind(
                section,
                "DamageReferenceSkillLevel",
                200,
                "Reference level used to calibrate the shared final multiplier above level 100. Must be above 100. " +
                "Final formula: finalMultiplier = skillFactor^e, where " +
                "e = ln(DamageReferenceMultiplier) / ln(DamageReferenceSkillLevel / 100). " +
                "With Relative=false, skillFactor is level/100 until BonusCap is reached.");

            DamageReferenceMultiplier = config.Bind(
                section,
                "DamageReferenceMultiplier",
                1.5f,
                "Final multiplier reached at DamageReferenceSkillLevel. " +
                "For damage, it multiplies the configured level-100 damage. For Blocking, it multiplies only the " +
                "vanilla level-100 skill bonus above the level-0 block value. " +
                "Final formula: finalMultiplier = skillFactor^e, where " +
                "e = ln(DamageReferenceMultiplier) / ln(DamageReferenceSkillLevel / 100). " +
                "The per-skill BonusCap can stop further scaling by clamping skillFactor.");

            VanillaDamageMultiplier = config.Bind(
                section,
                "VanillaDamageMultiplier",
                1.0f,
                "Controls how much of the normal skill-based damage increase is applied from level 0 to 100. " +
                "1.0 preserves vanilla; 0.5 keeps half of the normal skill contribution without halving base weapon damage. " +
                "Final formula: center = 0.4 + 0.6 * (level / 100)^VanillaDamageExponent * VanillaDamageMultiplier.");

            VanillaDamageExponent = config.Bind(
                section,
                "VanillaDamageExponent",
                1.0f,
                "Controls the shape of damage progression from level 0 to 100. 1.0 preserves vanilla; " +
                "above 1.0 is slower early; below 1.0 is faster early. " +
                "Final formula: center = 0.4 + 0.6 * (level / 100)^VanillaDamageExponent * VanillaDamageMultiplier.");

            EnableExtendedCostScaling = config.Bind(
                section,
                "EnableExtendedCostScaling",
                true,
                "Extend decreasing skill-based values beyond level 100 without ever making them negative. " +
                "This covers resource costs (stamina/eitr/health), decreasing action times such as bow draw/crossbow reload, " +
                "and supported tool durability costs. Level 0-100 remains vanilla.");

            ExtendedCostExponent = config.Bind(
                section,
                "ExtendedCostExponent",
                0.42f,
                "Controls how quickly costs/times continue decreasing above level 100. Higher values reduce them faster. " +
                "The level-100 value is always preserved exactly and zero is approached asymptotically. " +
                "Final formula for skillFactor > 1: value = valueAt100 / skillFactor^ExtendedCostExponent. " +
                "With Relative=false, skillFactor = level/100 until BonusCap is reached. " +
                "Example for a vanilla 33% discount: exponent 0.42 gives about 50% discount at level 200 and 58% at 300.");

            EnableBlockingScaling = config.Bind(
                section,
                "EnableBlockingScaling",
                true,
                "If true, Blocking above level 100 extends only the skill-derived block bonus using the same final multiplier as damage. " +
                "The level-0 block value is never multiplied. At factor 1 the vanilla level-100 value is preserved exactly. " +
                "Final formula for factor > 1: finalBlockPower = blockAt0 + (blockAt100 - blockAt0) * skillFactor^e, where " +
                "e = ln(DamageReferenceMultiplier) / ln(DamageReferenceSkillLevel / 100).");

            ScaleSafeFallDistance = config.Bind(
                section,
                "ScaleSafeFallDistance",
                true,
                "If true, Jump skill above level 100 increases the no-damage fall-distance breakpoint. " +
                "This changes only the safe fall breakpoint; it does not change jump force. " +
                "Vanilla 4m is preserved at factor 1 and only the extra distance is extended. " +
                "Final formula in meters for factor > 1: safeFallDistance = 4 + (skillFactor^SafeFallDistanceExponent - 1).");

            SafeFallDistanceExponent = config.Bind(
                section,
                "SafeFallDistanceExponent",
                2.0f,
                "Controls how quickly the additional safe fall distance grows above Jump level 100. " +
                "2.0 gives 4m at factor 1, 7m at factor 2, 12m at factor 3, and 28m at factor 5. " +
                "Final formula in meters for factor > 1: safeFallDistance = 4 + (skillFactor^SafeFallDistanceExponent - 1).");
        }

        internal static void GetDamageRange(float skillFactor, out float min, out float max)
        {
            if (!DamageScalingEnabled)
            {
                float center = Mathf.Lerp(0.4f, 1f, skillFactor);
                min = Mathf.Clamp01(center - 0.15f);
                max = Mathf.Clamp01(center + 0.15f);
                return;
            }

            if (skillFactor <= 1.0f)
            {
                GetBaseDamageRange(skillFactor, out min, out max);
                return;
            }

            GetBaseDamageRange(1.0f, out float level100Min, out float level100Max);
            float extendedMultiplier = GetExtendedDamageMultiplier(skillFactor);

            min = level100Min * extendedMultiplier;
            max = level100Max * extendedMultiplier;
        }

        private static void GetBaseDamageRange(float skillFactor, out float min, out float max)
        {
            float center = GetBaseDamageCenter(skillFactor);
            const float range = 0.15f;
            min = Mathf.Clamp01(center - range);
            max = Mathf.Clamp01(center + range);
        }

        private static float GetBaseDamageCenter(float skillFactor)
        {
            float normalizedFactor = Mathf.Clamp01(skillFactor);
            float multiplier = Mathf.Max(0f, EffectiveVanillaDamageMultiplier);
            float exponent = SanitizePositiveExponent(EffectiveVanillaDamageExponent, 1f);
            float progression = Mathf.Pow(normalizedFactor, exponent);
            return 0.4f + 0.6f * progression * multiplier;
        }

        /// <summary>
        /// Final multiplier applied to the configured level-100 damage result.
        /// </summary>
        internal static float GetExtendedDamageMultiplier(float skillFactor)
        {
            return DamageScalingEnabled
                ? GetExtendedFinalMultiplier(skillFactor)
                : 1.0f;
        }

        /// <summary>
        /// Shared final multiplier calibrated by DamageReferenceSkillLevel and
        /// DamageReferenceMultiplier. Damage and optional Blocking scaling both
        /// use this curve, but each feature has its own enable toggle.
        /// </summary>
        internal static float GetExtendedFinalMultiplier(float skillFactor)
        {
            if (skillFactor <= 1.0f)
                return 1.0f;

            float referenceFactor = EffectiveDamageReferenceSkillLevel / 100f;
            float targetMultiplier = EffectiveDamageReferenceMultiplier;

            if (referenceFactor <= 1.0f || float.IsNaN(referenceFactor) || float.IsInfinity(referenceFactor))
                referenceFactor = 2.0f;

            if (targetMultiplier <= 0f || float.IsNaN(targetMultiplier) || float.IsInfinity(targetMultiplier))
                targetMultiplier = 1.0f;

            if (Mathf.Approximately(targetMultiplier, 1.0f))
                return 1.0f;

            float exponent = Mathf.Log(targetMultiplier) / Mathf.Log(referenceFactor);
            return Mathf.Pow(skillFactor, exponent);
        }

        /// <summary>
        /// Multiplier applied to a value already calculated at the vanilla
        /// level-100 endpoint. For factor > 1 it is always positive and trends
        /// toward zero without crossing it.
        /// </summary>
        internal static float GetExtendedCostMultiplier(float skillFactor)
        {
            if (!CostScalingEnabled || skillFactor <= 1f)
                return 1f;

            float exponent = SanitizePositiveExponent(EffectiveExtendedCostExponent, 0.42f);
            return 1f / Mathf.Pow(skillFactor, exponent);
        }

        /// <summary>
        /// Replacement for decreasing Mathf.Lerp skill formulas.
        /// Vanilla behavior is preserved through factor 1. Above 1, only a
        /// decreasing Lerp is extended; increasing/equal Lerps remain at their
        /// vanilla level-100 endpoint.
        /// </summary>
        internal static float ExtendedCostLerp(float from, float at100, float skillFactor)
        {
            if (!CostScalingEnabled || skillFactor <= 1f)
                return Mathf.Lerp(from, at100, skillFactor);

            // Do not reinterpret increasing bonuses as costs/times.
            if (at100 >= from)
                return at100;

            // Negative endpoints are not meaningful for an asymptotic cost.
            if (at100 < 0f)
                return at100;

            return at100 * GetExtendedCostMultiplier(skillFactor);
        }

        internal static float GetSafeFallDistance(float jumpSkillFactor)
        {
            const float vanillaSafeDistance = 4f;

            if (jumpSkillFactor <= 1f)
                return vanillaSafeDistance;

            float exponent = SanitizePositiveExponent(EffectiveSafeFallDistanceExponent, 2f);

            // Preserve the vanilla 4m breakpoint at factor 1 and scale only
            // the additional safe distance above level 100.
            //
            // safe = 4 + (factor^exponent - 1)
            return vanillaSafeDistance +
                   (Mathf.Pow(jumpSkillFactor, exponent) - 1f);
        }

        internal static string BuildSyncRecord()
        {
            return string.Join("|", new[]
            {
                "E",
                EnableDamageScaling.Value ? "1" : "0",
                DamageReferenceSkillLevel.Value.ToString(CultureInfo.InvariantCulture),
                DamageReferenceMultiplier.Value.ToString("R", CultureInfo.InvariantCulture),
                VanillaDamageMultiplier.Value.ToString("R", CultureInfo.InvariantCulture),
                VanillaDamageExponent.Value.ToString("R", CultureInfo.InvariantCulture),
                EnableExtendedCostScaling.Value ? "1" : "0",
                ExtendedCostExponent.Value.ToString("R", CultureInfo.InvariantCulture),
                EnableBlockingScaling.Value ? "1" : "0",
                ScaleSafeFallDistance.Value ? "1" : "0",
                SafeFallDistanceExponent.Value.ToString("R", CultureInfo.InvariantCulture)
            });
        }

        internal static void ApplyServerRecord(string record)
        {
            if (string.IsNullOrWhiteSpace(record))
            {
                _hasServerOverride = false;
                return;
            }

            string[] parts = record.Split('|');
            if (parts.Length != 11 || parts[0] != "E")
                return;

            _serverEnableDamageScaling = parts[1] == "1";
            _serverDamageReferenceSkillLevel = ParseInt(parts[2], 200);
            _serverDamageReferenceMultiplier = ParseFloat(parts[3], 1.5f);
            _serverVanillaDamageMultiplier = ParseFloat(parts[4], 1.0f);
            _serverVanillaDamageExponent = ParseFloat(parts[5], 1.0f);
            _serverEnableExtendedCostScaling = parts[6] == "1";
            _serverExtendedCostExponent = ParseFloat(parts[7], 0.42f);
            _serverEnableBlockingScaling = parts[8] == "1";
            _serverScaleSafeFallDistance = parts[9] == "1";
            _serverSafeFallDistanceExponent = ParseFloat(parts[10], 2.0f);
            _hasServerOverride = true;
        }

        private static float SanitizePositiveExponent(float value, float fallback)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : fallback;
        }

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;

        private static float ParseFloat(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
    }
}

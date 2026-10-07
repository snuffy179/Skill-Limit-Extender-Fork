using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Shared helpers used by skill-related patches.
    /// </summary>
    internal static class SLE_SkillHelpers
    {
        internal static Player? GetSafeLocalPlayer()
        {
            try
            {
                if (Player.m_localPlayer != null)
                    return Player.m_localPlayer;

                if (Game.instance?.GetPlayerProfile()?.GetPlayerID() != 0)
                {
                    var players = Player.GetAllPlayers();
                    if (players != null)
                    {
                        foreach (var player in players)
                        {
                            if (player != null && player.IsOwner())
                                return player;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                    SkillLimitExtenderPlugin.Logger?.LogDebug($"[SLE] Failed to resolve local player: {ex.Message}");
            }

            return null;
        }

        internal static bool TryGetSkillType(global::Skills.Skill skill, out global::Skills.SkillType skillType)
        {
            skillType = global::Skills.SkillType.None;

            if (skill == null)
                return false;

            try
            {
                var info = Traverse.Create(skill)
                    .Field("m_info")
                    .GetValue<global::Skills.SkillDef>();

                if (info != null)
                {
                    skillType = Traverse.Create(info)
                        .Field("m_skill")
                        .GetValue<global::Skills.SkillType>();

                    return skillType != global::Skills.SkillType.None;
                }

                // Some MOD skills have no usable m_info. Resolve them by finding
                // the exact Skill instance in the local player's m_skillData map.
                var skills = GetSafeLocalPlayer()?.GetSkills();
                if (skills == null)
                    return false;

                var skillData = Traverse.Create(skills)
                    .Field("m_skillData")
                    .GetValue<Dictionary<global::Skills.SkillType, global::Skills.Skill>>();

                if (skillData == null)
                    return false;

                foreach (var entry in skillData)
                {
                    if (!ReferenceEquals(entry.Value, skill))
                        continue;

                    skillType = entry.Key;
                    return skillType != global::Skills.SkillType.None;
                }
            }
            catch (Exception ex)
            {
                if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                    SkillLimitExtenderPlugin.Logger?.LogDebug($"[SLE] Failed to resolve skill type: {ex.Message}");
            }

            return false;
        }

        internal static global::Skills.SkillType GetSkillTypeOrNone(global::Skills.Skill skill)
        {
            return TryGetSkillType(skill, out var skillType)
                ? skillType
                : global::Skills.SkillType.None;
        }
    }
}

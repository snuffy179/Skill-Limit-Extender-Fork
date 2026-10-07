using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// Safely obtain a Skill from Skills (covers private API via reflection)
    /// </summary>
    internal static class SLE_SkillsExtensions
    {
        private static readonly MethodInfo _miGetSkill =
            AccessTools.Method(typeof(global::Skills), "GetSkill", new Type[] { typeof(global::Skills.SkillType) });

        private static readonly FieldInfo _fiSkillData =
            AccessTools.Field(typeof(global::Skills), "m_skillData");

        internal static global::Skills.Skill? GetSkillSafe(this global::Skills skills, global::Skills.SkillType st)
        {
            if (skills == null) return null;

            // Never pass sentinel skill types into the game's private GetSkill.
            // GetSkill(None) may create an invalid cached entry in m_skillData.
            if (st == global::Skills.SkillType.None ||
                st == global::Skills.SkillType.All)
                return null;

            // 1) Invoke private GetSkill(skillType) via reflection
            if (_miGetSkill != null)
            {
                try
                {
                    return (global::Skills.Skill?)_miGetSkill.Invoke(skills, new object[] { st });
                }
                catch { /* fallbackへ */ }
            }

            // 2) Read m_skillData directly (Dictionary<SkillType, Skill>)
            if (_fiSkillData != null)
            {
                try
                {
                    var dict = _fiSkillData.GetValue(skills) as IDictionary<global::Skills.SkillType, global::Skills.Skill>;
                    if (dict != null && dict.TryGetValue(st, out var s))
                        return s;
                }
                catch { /* ignore */ }
            }

            return null;
        }
    }
}

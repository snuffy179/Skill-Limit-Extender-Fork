using System;
using System.Collections.Generic;

namespace SkillLimitExtender
{
    /// <summary>
    /// Temporarily overrides one Skills.GetSkillFactor result while a vanilla
    /// method calculates its level-100 baseline. This lets us preserve all of
    /// Valheim's native item/status-effect logic and then extend only the final
    /// cost above level 100.
    /// </summary>
    internal static class SLE_SkillFactorOverride
    {
        private readonly struct Entry
        {
            internal readonly global::Skills.SkillType SkillType;
            internal readonly float Factor;

            internal Entry(global::Skills.SkillType skillType, float factor)
            {
                SkillType = skillType;
                Factor = factor;
            }
        }

        [ThreadStatic]
        private static List<Entry>? _entries;

        internal static IDisposable Push(global::Skills.SkillType skillType, float factor)
        {
            _entries ??= new List<Entry>();
            _entries.Add(new Entry(skillType, factor));
            return new Scope(_entries.Count - 1);
        }

        internal static bool TryGet(global::Skills.SkillType skillType, out float factor)
        {
            if (_entries != null)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].SkillType != skillType)
                        continue;

                    factor = _entries[i].Factor;
                    return true;
                }
            }

            factor = 0f;
            return false;
        }

        private sealed class Scope : IDisposable
        {
            private int _index;
            private bool _disposed;

            internal Scope(int index)
            {
                _index = index;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_entries == null || _entries.Count == 0)
                    return;

                // Normal use is strict LIFO. Keep a defensive fallback so an
                // exception in another mod cannot leave our override active.
                if (_index == _entries.Count - 1)
                {
                    _entries.RemoveAt(_index);
                    return;
                }

                if (_index >= 0 && _index < _entries.Count)
                    _entries.RemoveAt(_index);
            }
        }
    }
}

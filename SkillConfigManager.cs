using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;

namespace SkillLimitExtender
{
    /// <summary>
    /// Per-skill configuration stored directly in the normal BepInEx config.
    /// Vanilla skills are exposed as simple sections such as [Swords], [Bows],
    /// [Jump], etc. Server-locked values are distributed as a small internal
    /// snapshot; no external serialization library is required.
    /// </summary>
    internal static class SkillConfigManager
    {
        private sealed class SkillSettings
        {
            internal readonly string Section;
            internal readonly ConfigEntry<int> Cap;
            internal readonly ConfigEntry<int> BonusCap;
            internal readonly ConfigEntry<bool> Relative;
            internal readonly ConfigEntry<bool> UseCustomGrowthCurve;
            internal readonly ConfigEntry<float> GrowthExponent;
            internal readonly ConfigEntry<float> GrowthMultiplier;
            internal readonly ConfigEntry<float> GrowthConstant;

            internal SkillSettings(ConfigFile config, string section)
            {
                Section = section;

                Cap = config.Bind(
                    section,
                    "Cap",
                    DefaultCapFallback,
                    "Maximum level this skill can reach. Formula: finalLevel <= Cap.");

                BonusCap = config.Bind(
                    section,
                    "BonusCap",
                    DefaultBonusCapFallback,
                    "Maximum skill-effect factor expressed as a level-like percentage. " +
                    "100 = factor 1.0, 250 = factor 2.5, 500 = factor 5.0. " +
                    "Formula: maxSkillFactor = BonusCap / 100.");

                Relative = config.Bind(
                    section,
                    "Relative",
                    false,
                    "Controls how the skill-effect factor is calculated. " +
                    "true: the configured Cap becomes the point where BonusCap is reached. " +
                    "false: every 100 skill levels add 1.0 factor until BonusCap is reached. " +
                    "Formula (true): factor = (level / Cap) * (BonusCap / 100). " +
                    "Formula (false): factor = level / 100. Both modes clamp to BonusCap / 100.");

                UseCustomGrowthCurve = config.Bind(
                    section,
                    "UseCustomGrowthCurve",
                    true,
                    "Use the custom XP requirement formula below instead of vanilla XP progression.");

                GrowthExponent = config.Bind(
                    section,
                    "GrowthExponent",
                    2.1f,
                    "Exponent used only for XP required to gain the next level. Vanilla = 1.5. " +
                    "Final formula: XP = nextLevel^GrowthExponent * GrowthMultiplier + GrowthConstant.");

                GrowthMultiplier = config.Bind(
                    section,
                    "GrowthMultiplier",
                    0.04f,
                    "Multiplier used only for XP required to gain the next level. Vanilla = 0.5. " +
                    "Final formula: XP = nextLevel^GrowthExponent * GrowthMultiplier + GrowthConstant.");

                GrowthConstant = config.Bind(
                    section,
                    "GrowthConstant",
                    8.0f,
                    "Constant added only to XP required to gain the next level. Vanilla = 0.5. " +
                    "Final formula: XP = nextLevel^GrowthExponent * GrowthMultiplier + GrowthConstant.");

            }
        }

        private sealed class SkillValues
        {
            internal int Cap;
            internal int BonusCap;
            internal bool Relative;
            internal bool UseCustomGrowthCurve;
            internal float GrowthExponent;
            internal float GrowthMultiplier;
            internal float GrowthConstant;
        }

        internal static ConfigEntry<bool> ServerConfigLocked { get; private set; } = null!;

        internal const int DefaultCapFallback = 1000;
        internal const int DefaultBonusCapFallback = 500;

        private static readonly Dictionary<string, SkillSettings> EntriesByName =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, string> SkillKeysById = new();

        private static Dictionary<string, SkillValues> _serverEntries =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<int> WarnedSkillIds = new();

        private static ConfigFile _config = null!;
        private static bool _serverInitialized;
        private static bool _skillsInitialized;
        private static bool _isServerConfig;
        private static string _lastSnapshotHash = string.Empty;

        internal static void InitializeServer(ConfigFile config)
        {
            if (_serverInitialized)
                return;

            _config = config;

            ServerConfigLocked = config.Bind(
                "2 - Server",
                "LockConfiguration",
                false,
                new ConfigDescription(
                    "If true, the server sends its Skill Limit Extender gameplay configuration to clients.",
                    null,
                    new object[]
                    {
                        new ConfigurationManagerAttributes
                        {
                            Category = "2 - Server",
                            Order = -1000,
                            IsAdminOnly = true
                        }
                    }));

            _serverInitialized = true;
        }

        internal static void InitializeSkills()
        {
            if (_skillsInitialized)
                return;

            if (!_serverInitialized)
                throw new InvalidOperationException("SkillConfigManager.InitializeServer must be called before InitializeSkills.");

            BindVanillaSkills();
            _skillsInitialized = true;

            SkillLimitExtenderPlugin.Logger?.LogInfo(
                $"[SLE] BepInEx skill configuration initialized ({EntriesByName.Count} skill sections)");
        }

        internal static void Initialize(ConfigFile config)
        {
            InitializeServer(config);
            InitializeSkills();
        }

        private static void BindVanillaSkills()
        {
            foreach (global::Skills.SkillType skillType in Enum.GetValues(typeof(global::Skills.SkillType)))
            {
                if (skillType == global::Skills.SkillType.None ||
                    skillType == global::Skills.SkillType.All)
                {
                    continue;
                }

                string name = skillType.ToString();
                if (int.TryParse(name, out _))
                    continue;

                BindSkill(name);
                SkillKeysById[(int)skillType] = name;
            }
        }

        private static SkillSettings BindSkill(string section)
        {
            section = NormalizeSectionName(section);

            if (EntriesByName.TryGetValue(section, out SkillSettings existing))
                return existing;

            var created = new SkillSettings(_config, section);
            EntriesByName[section] = created;
            return created;
        }

        private static SkillSettings GetSettings(global::Skills.SkillType skillType)
        {
            string key = GetSkillKeyForLookup(skillType);
            return BindSkill(key);
        }

        private static string GetSkillKeyForLookup(global::Skills.SkillType skillType)
        {
            int skillId = (int)skillType;

            if (SkillKeysById.TryGetValue(skillId, out string cached))
                return cached;

            string enumName = skillType.ToString();
            if (!int.TryParse(enumName, out _))
            {
                SkillKeysById[skillId] = enumName;
                return enumName;
            }

            string actualName = GetActualModSkillName(skillType);
            string section = NormalizeSectionName(actualName);

            if (string.IsNullOrWhiteSpace(section) || int.TryParse(section, out _))
                section = enumName;

            SkillKeysById[skillId] = section;

            if (!EntriesByName.ContainsKey(section) && WarnedSkillIds.Add(skillId))
            {
                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE] Discovered MOD skill {skillId}; using config section [{section}]");
            }

            return section;
        }

        private static string GetActualModSkillName(global::Skills.SkillType skillType)
        {
            try
            {
                var player = SLE_SkillHelpers.GetSafeLocalPlayer();
                var skills = player?.GetSkills();
                if (skills == null)
                    return skillType.ToString();

                var skillData = HarmonyLib.Traverse.Create(skills)
                    .Field("m_skillData")
                    .GetValue<Dictionary<global::Skills.SkillType, global::Skills.Skill>>();

                if (skillData == null || !skillData.TryGetValue(skillType, out var skill) || skill == null)
                    return skillType.ToString();

                var info = HarmonyLib.Traverse.Create(skill)
                    .Field("m_info")
                    .GetValue<global::Skills.SkillDef>();

                if (info == null)
                    return skillType.ToString();

                string description = HarmonyLib.Traverse.Create(info)
                    .Field("m_description")
                    .GetValue<string>();

                if (!string.IsNullOrWhiteSpace(description))
                {
                    if (description.StartsWith("$"))
                    {
                        string localized = Localization.instance?.Localize(description);
                        if (!string.IsNullOrWhiteSpace(localized) && localized != description)
                            return localized;
                    }
                    else
                    {
                        return description;
                    }
                }
            }
            catch (Exception ex)
            {
                if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                    SkillLimitExtenderPlugin.Logger?.LogDebug($"[SLE] MOD skill name lookup failed: {ex.Message}");
            }

            return skillType.ToString();
        }

        private static string NormalizeSectionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "UnknownSkill";

            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    builder.Append(c);
            }

            return builder.Length > 0 ? builder.ToString() : "UnknownSkill";
        }

        private static bool TryGetServerValues(string section, out SkillValues values)
        {
            values = null!;
            return _isServerConfig && _serverEntries.TryGetValue(section, out values!);
        }

        internal static int GetCap(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            if (TryGetServerValues(entry.Section, out var server) && server.Cap > 0)
                return server.Cap;
            return entry.Cap.Value > 0 ? entry.Cap.Value : DefaultCapFallback;
        }

        internal static int GetBonusCap(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            if (TryGetServerValues(entry.Section, out var server) && server.BonusCap > 0)
                return server.BonusCap;
            return entry.BonusCap.Value > 0 ? entry.BonusCap.Value : DefaultBonusCapFallback;
        }

        internal static bool IsRelative(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            return TryGetServerValues(entry.Section, out var server)
                ? server.Relative
                : entry.Relative.Value;
        }

        internal static bool UseCustomGrowthCurve(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            return TryGetServerValues(entry.Section, out var server)
                ? server.UseCustomGrowthCurve
                : entry.UseCustomGrowthCurve.Value;
        }

        internal static float GetGrowthExponent(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            return TryGetServerValues(entry.Section, out var server)
                ? server.GrowthExponent
                : entry.GrowthExponent.Value;
        }

        internal static float GetGrowthMultiplier(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            return TryGetServerValues(entry.Section, out var server)
                ? server.GrowthMultiplier
                : entry.GrowthMultiplier.Value;
        }

        internal static float GetGrowthConstant(global::Skills.SkillType skillType)
        {
            SkillSettings entry = GetSettings(skillType);
            return TryGetServerValues(entry.Section, out var server)
                ? server.GrowthConstant
                : entry.GrowthConstant.Value;
        }

        internal static int GetCartographySkillCap()
        {
            foreach (string key in new[] { "Cartography", "CartographySkill", "1337" })
            {
                if (_isServerConfig && _serverEntries.TryGetValue(key, out var server) && server.Cap > 0)
                    return server.Cap;

                if (EntriesByName.TryGetValue(key, out var local) && local.Cap.Value > 0)
                    return local.Cap.Value;
            }

            return DefaultCapFallback;
        }

        internal static bool IsConfiguredSkill(global::Skills.SkillType skillType)
        {
            string key = GetSkillKeyForLookup(skillType);
            return EntriesByName.ContainsKey(key) || _serverEntries.ContainsKey(key);
        }

        internal static int GetCapByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return DefaultCapFallback;

            if (Enum.TryParse(name, true, out global::Skills.SkillType skillType) &&
                skillType != global::Skills.SkillType.None &&
                skillType != global::Skills.SkillType.All)
            {
                return GetCap(skillType);
            }

            string section = NormalizeSectionName(name);
            if (_isServerConfig && _serverEntries.TryGetValue(section, out var server) && server.Cap > 0)
                return server.Cap;
            if (EntriesByName.TryGetValue(section, out var local) && local.Cap.Value > 0)
                return local.Cap.Value;

            return DefaultCapFallback;
        }

        internal static int GetSkillLimit(global::Skills.SkillType skillType) => GetCap(skillType);
        internal static float GetFactorDenominator(global::Skills.SkillType skillType) => 100f;
        internal static float GetUiDenominator() => Math.Max(1, DefaultCapFallback);
        internal static float GetUiDenominatorForSkill(global::Skills.SkillType skillType) => Math.Max(1, GetCap(skillType));

        internal static float GetUiDenominatorForSkillSafe(object? skillMaybe)
        {
            try
            {
                if (skillMaybe is global::Skills.Skill skill &&
                    SLE_SkillHelpers.TryGetSkillType(skill, out var skillType))
                {
                    return GetUiDenominatorForSkill(skillType);
                }
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] GetUiDenominatorForSkillSafe failed: {ex.Message}");
            }

            return GetUiDenominator();
        }

        internal static void ReloadFromConfig()
        {
            try
            {
                _config.Reload();
                SkillLimitExtenderPlugin.Logger?.LogInfo("[SLE] BepInEx configuration reloaded");
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] Config reload failed: {ex}");
            }
        }

        internal static string GetConfigPath() => _config.ConfigFilePath;

        internal static void OnConfigReceivedStatic(long sender, string snapshot, int protocolVersion)
        {
            if (ZNet.instance?.IsServer() == true)
                return;

            if (!VersionInfo.IsCompatible(protocolVersion))
            {
                SkillLimitExtenderPlugin.Logger?.LogWarning(
                    $"[SLE] Config protocol mismatch: remote={protocolVersion}, local={VersionInfo.ProtocolVersion}");
                return;
            }

            try
            {
                var parsed = new Dictionary<string, SkillValues>(StringComparer.OrdinalIgnoreCase);
                string extendedRecord = string.Empty;

                foreach (string rawLine in (snapshot ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = rawLine.TrimEnd('\r');
                    string[] parts = line.Split('|');
                    if (parts.Length == 0)
                        continue;

                    if (parts[0] == "S" && parts.Length == 9)
                    {
                        string section = Decode(parts[1]);
                        parsed[section] = new SkillValues
                        {
                            Cap = ParseInt(parts[2], DefaultCapFallback),
                            BonusCap = ParseInt(parts[3], DefaultBonusCapFallback),
                            Relative = ParseBool(parts[4], false),
                            UseCustomGrowthCurve = ParseBool(parts[5], true),
                            GrowthExponent = ParseFloat(parts[6], 2.1f),
                            GrowthMultiplier = ParseFloat(parts[7], 0.04f),
                            GrowthConstant = ParseFloat(parts[8], 8.0f)
                        };
                    }
                    else if (parts[0] == "E")
                    {
                        extendedRecord = line;
                    }
                }

                _serverEntries = parsed;
                _isServerConfig = true;
                SLE_ExtendedScaling.ApplyServerRecord(extendedRecord);

                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE] Received server configuration ({_serverEntries.Count} skill sections)");
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] Failed to apply server config: {ex}");
            }
        }

        internal static void SendConfigToClients()
        {
            if (ZNet.instance?.IsServer() != true || ServerConfigLocked?.Value != true)
                return;

            try
            {
                string snapshot = BuildSnapshot();
                ZRoutedRpc.instance.InvokeRoutedRPC(
                    0L,
                    "SLE_ConfigSync",
                    snapshot,
                    VersionInfo.ProtocolVersion);

                _lastSnapshotHash = ComputeHash(snapshot);
                SkillLimitExtenderPlugin.Logger?.LogInfo(
                    $"[SLE] Server configuration sent to clients ({snapshot.Length} chars)");
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] Failed to send server config: {ex}");
            }
        }

        internal static void SendConfigToClientsIfChanged()
        {
            if (ZNet.instance?.IsServer() != true || ServerConfigLocked?.Value != true)
                return;

            try
            {
                string snapshot = BuildSnapshot();
                string hash = ComputeHash(snapshot);
                if (string.Equals(hash, _lastSnapshotHash, StringComparison.Ordinal))
                    return;

                ZRoutedRpc.instance.InvokeRoutedRPC(
                    0L,
                    "SLE_ConfigSync",
                    snapshot,
                    VersionInfo.ProtocolVersion);

                _lastSnapshotHash = hash;
                SkillLimitExtenderPlugin.Logger?.LogInfo("[SLE] Changed server configuration broadcasted to clients");
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] Failed to broadcast server config: {ex}");
            }
        }

        private static string BuildSnapshot()
        {
            var builder = new StringBuilder();

            foreach (var pair in EntriesByName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                SkillSettings entry = pair.Value;
                builder.Append("S|")
                    .Append(Encode(entry.Section)).Append('|')
                    .Append(entry.Cap.Value).Append('|')
                    .Append(entry.BonusCap.Value).Append('|')
                    .Append(entry.Relative.Value ? "1" : "0").Append('|')
                    .Append(entry.UseCustomGrowthCurve.Value ? "1" : "0").Append('|')
                    .Append(entry.GrowthExponent.Value.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.GrowthMultiplier.Value.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.GrowthConstant.Value.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            builder.Append(SLE_ExtendedScaling.BuildSyncRecord()).Append('\n');
            return builder.ToString();
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string Decode(string value) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(value));

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;

        private static float ParseFloat(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;

        private static bool ParseBool(string value, bool fallback)
        {
            if (value == "1") return true;
            if (value == "0") return false;
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static string ComputeHash(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }
}

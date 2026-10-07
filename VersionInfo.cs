using System;

namespace SkillLimitExtender
{
    /// <summary>
    /// Versioning and compatibility checks
    /// </summary>
    internal static class VersionInfo
    {
        // Semantic version (user-defined)
        public const string Version = "1.3.4";
        public const string Prerelease = ""; // Release build
        public const string Build = "20261007";

        // Development flag (used in logs)
        public const bool IsDevelopmentVersion = false;

        // Full version string (constants)
        public const string FullVersion = "1.3.4";
        public const string FullVersionWithBuild = "1.3.4.0";

        // Compatibility for config/RPC (kept as int; RPC exchanges int)
        public const int ProtocolVersion = 6;
        public const int ConfigSchemaVersion = 6;

        // Display string (includes prerelease and build)
        public static string DisplayVersion => string.IsNullOrEmpty(Prerelease) ? FullVersion : $"{FullVersion}-{Prerelease}";
        public static string VersionString => $"v{DisplayVersion} (build {Build}, proto={ProtocolVersion}, cfg={ConfigSchemaVersion})";

        public static bool IsCompatible(int remoteProtocolVersion) => remoteProtocolVersion == ProtocolVersion;
    }

    /// <summary>
    /// Meta-attribute stub for ConfigurationManager plugin.
    /// Defined here to compile without plugin reference; if present, plugin type wins.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent { get; set; }
        public bool? IsAdvanced { get; set; }
        public int? Order { get; set; }
        public string? Category { get; set; }
        public bool? Browsable { get; set; }
        public object? DefaultValue { get; set; }
        public string? ReadOnly { get; set; }
        public bool? HideDefaultButton { get; set; }
        public bool? HideSettingName { get; set; }
        public bool? IsAdminOnly { get; set; }
    }
}
namespace SkillLimitExtender
{
    internal static class SLE_TerminalCommands
    {
        public static void Register()
        {
            try
            {
                new Terminal.ConsoleCommand(
                    "sle_config_reload",
                    "Reload Skill Limit Extender BepInEx configuration",
                    args =>
                    {
                        SkillConfigManager.ReloadFromConfig();

                        if (ZNet.instance?.IsServer() == true)
                            SkillConfigManager.SendConfigToClientsIfChanged();

                        args.Context.AddString("SLE: configuration reloaded.");
                    },
                    isCheat: true,
                    hideBehindDevCommands: false);


                new Terminal.ConsoleCommand(
                    "sle_config_path",
                    "Show current Skill Limit Extender config path",
                    args =>
                    {
                        args.Context.AddString($"SLE config path: {SkillConfigManager.GetConfigPath()}");
                    },
                    isCheat: true,
                    hideBehindDevCommands: false);
            }
            catch (System.Exception e)
            {
                SkillLimitExtenderPlugin.Logger?.LogError(
                    $"[SLE] Failed to register console command: {e}");
            }
        }
    }
}

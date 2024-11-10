﻿using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2_SimpleAdmin;

[MinimumApiVersion(284)]
public partial class CS2_SimpleAdmin : BasePlugin, IPluginConfig<CS2_SimpleAdminConfig>
{
    internal static CS2_SimpleAdmin Instance { get; private set; } = new();

    public override string ModuleName => "CS2-SimpleAdmin" + (Helper.IsDebugBuild ? " (DEBUG)" : " (RELEASE)");
    public override string ModuleDescription => "Simple admin plugin for Counter-Strike 2 :)";
    public override string ModuleAuthor => "daffyy & Dliix66";
    public override string ModuleVersion => "1.6.8a";
    
    public override void Load(bool hotReload)
    {
        Instance = this;

        RegisterEvents();

        if (hotReload)
        {
            ServerLoaded = false;
            OnGameServerSteamAPIActivated();
            OnMapStart(string.Empty);

            AddTimer(2.0f, () =>
            {
                if (Database == null) return;

                Helper.GetValidPlayers().ForEach(player =>
                {
                    var playerManager = new PlayerManager();
                    playerManager.LoadPlayerData(player);
                });
            });
        }
        _cBasePlayerControllerSetPawnFunc = new MemoryFunctionVoid<CBasePlayerController, CCSPlayerPawn, bool, bool>(GameData.GetSignature("CBasePlayerController_SetPawn"));

        SimpleAdminApi = new Api.CS2_SimpleAdminApi();
        Capabilities.RegisterPluginCapability(ICS2_SimpleAdminApi.PluginCapability, () => SimpleAdminApi);
        
        new PlayerManager().CheckPlayersTimer();
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        AddTimer(3.0f, () => ReloadAdmins(null));

        MenuApi = MenuCapability.Get();
        if (MenuApi == null)
            Logger.LogError("MenuManager Core not found...");
        
        RegisterCommands.InitializeCommands();
    }

    public void OnConfigParsed(CS2_SimpleAdminConfig config)
    {
        Instance = this;
        _logger = Logger;
        
        if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
        {
            throw new Exception("[CS2-SimpleAdmin] You need to setup Database credentials in config!");
        }
        
        MySqlConnectionStringBuilder builder = new()
        {
            Server = config.DatabaseHost,
            Database = config.DatabaseName,
            UserID = config.DatabaseUser,
            Password = config.DatabasePassword,
            Port = (uint)config.DatabasePort,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 640,
        };

        DbConnectionString = builder.ConnectionString;
        Database = new Database.Database(DbConnectionString);

        if (!Database.CheckDatabaseConnection())
        {
            Logger.LogError("Unable connect to database!");
            Unload(false);
            return;
        }

        Task.Run(() => Database.DatabaseMigration());
        
        Config = config;
        Helper.UpdateConfig(config);

        if (!Directory.Exists(ModuleDirectory + "/data"))
        {
            Directory.CreateDirectory(ModuleDirectory + "/data");
        }

        _localizer = Localizer;

        if (!string.IsNullOrEmpty(Config.Discord.DiscordLogWebhook))
            DiscordWebhookClientLog = new DiscordManager(Config.Discord.DiscordLogWebhook);

        PluginInfo.ShowAd(ModuleVersion);
        if (Config.EnableUpdateCheck)
            Task.Run(async () => await PluginInfo.CheckVersion(ModuleVersion, _logger));
        
        PermissionManager = new PermissionManager(Database);
        BanManager = new BanManager(Database);
        MuteManager = new MuteManager(Database);
        WarnManager = new WarnManager(Database);
    }

    private static TargetResult? GetTarget(CommandInfo command)
    {
        var matches = command.GetArgTargetResult(1);

        if (!matches.Any())
        {
            command.ReplyToCommand($"Target {command.GetArg(1)} not found.");
            return null;
        }

        if (command.GetArg(1).StartsWith('@'))
            return matches;

        if (matches.Count() == 1)
            return matches;

        command.ReplyToCommand($"Multiple targets found for \"{command.GetArg(1)}\".");
        return null;
    }
}
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using MenuFox = MenuManager;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;

namespace CustomWeapons;
public class CustomWeapons : BasePlugin, IPluginConfig<CustomWeapons_Config>
{
    public override string ModuleName => "Custom Weapons";
    public override string ModuleAuthor => "Ganter1234";
    public override string ModuleVersion => "2.1";
    public CustomWeapons_Config Config { get; set; } = new();
    public string dbConnectionString = string.Empty;
    public string[] accessWeapons = new string[65];
    public Dictionary<string, string>[] WeaponInfo = new Dictionary<string, string>[65];
    private MenuFox.IMenuApi? _apiMenuFox;
    public Timer[] regiveTimer = new Timer[65];
    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventItemPickup>(OnItemPickup);

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);

		if(hotReload)
		{
			foreach(var player in Utilities.GetPlayers())
			{
				if(player.AuthorizedSteamID != null)
					OnClientAuthorized(player.Slot, player.AuthorizedSteamID);
			}
		}
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try
        {
            PluginCapability<MenuFox.IMenuApi?> _pluginCapability = new("menu:nfcore");
            _apiMenuFox = _pluginCapability.Get();
        }
        catch (KeyNotFoundException ex) { _ = ex; }
    }

    public void OnServerPrecacheResources(ResourceManifest manifest)
    {
        foreach (var weapons in Config.CW_List)
        {
            foreach(var settings in weapons.Value)
            {
                manifest.AddResource(settings.Value.Model);
            }
        }
    }

    public void OnClientAuthorized(int playerSlot, SteamID steamID)
	{
		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid)
			return;

        WeaponInfo[player.Slot] = [];
        accessWeapons[player.Slot] = "";

        if(player.IsBot || player.IsHLTV)
            return;

        ulong steamid = steamID.SteamId64;
        Task.Run(async () => 
        {
            try
            {
                await using (var connection = new MySqlConnection(dbConnectionString))
                {
                    await connection.OpenAsync();
                    IEnumerable<dynamic> data = await connection.QueryAsync("SELECT * FROM `custom_weapons` WHERE `auth` = @Auth;", new
                    {
                        Auth = steamid
                    });

                    if(data != null && data.Count() > 0)
                    {
                        foreach(var row in data)
                        {
                            foreach (var property in row)
                            {
                                string key = property.Key;
                                var value = property.Value;
                                if(key.StartsWith("weapon_") && value != null && value!.Length > 0)
                                {
                                    WeaponInfo[player.Slot].Add(key, value);
                                    //Console.WriteLine($"Key {key} | Value {value}");
                                }

                                if(key.Equals("access_weapons") && value != null && value!.Length > 0)
                                {
                                    accessWeapons[player.Slot] = value!;
                                }
                            }
                        }
                    }
                    else
                    {
                        await connection.QueryAsync("INSERT INTO `custom_weapons` (`auth`, `access_weapons`) VALUES (@Auth, '');", new
                        {
                            Auth = steamid
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("{OnClientAuthorized} Failed get info in database | " + ex.Message);
                Logger.LogDebug(ex.Message);
                throw new Exception("[CW] Failed get info in database! | " + ex.Message);
            }
        });
    }

    #region Setup

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (!entity.DesignerName.StartsWith("weapon_"))
        {
            return;
        }

        CBasePlayerWeapon weapon = entity.As<CBasePlayerWeapon>();
        SetWeaponModel(weapon, false);
    }

    public void SetWeaponModel(CBasePlayerWeapon? weapon, bool update, bool remove = false)
    {
        Server.NextWorldUpdate(() =>
        {
            if (weapon == null || !weapon.IsValid || weapon.OwnerEntity.Value == null || weapon.OwnerEntity.Index <= 0)
                return;

            CCSPlayerPawn? pawn = weapon.OwnerEntity.Value?.As<CCSPlayerPawn>();

            if(pawn == null || !pawn.IsValid)
                return;

            CCSPlayerController player = pawn.OriginalController.Value!;
            if(player.IsBot)
                return;

            var designerName = GetWeaponNameFromDefIndex(weapon);

            ModelInfo? settings = null;
            if(!WeaponInfo[player.Slot].TryGetValue(designerName, out var modelname) || !Config.CW_List.TryGetValue(designerName, out var value) || !value.TryGetValue(modelname, out settings))
                remove = true;

            if(!remove)
            {
                if(update)
                {
                    RegiveWeapon(player, designerName, weapon);
                    return;
                }

                weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName = settings!.Model;
                weapon.SetModel(settings.Model);
                if(!string.IsNullOrEmpty(settings.CustomName)) weapon.AttributeManager.Item.CustomName = settings.CustomName;
            }
            else
            {
                if(update)
                    RegiveWeapon(player, designerName, weapon);
            }
        });
    }

    public void RegiveWeapon(CCSPlayerController player, string weaponName, CBasePlayerWeapon weapon)
    {
        var slot = player.Slot;
        if(regiveTimer[slot] != null) return;

        if (weaponName.Contains("knife") || weaponName.Contains("bayonet"))
        {
            weaponName = "weapon_knife";
        }

        var clip1 = weapon.Clip1;
        var reserve = weapon.ReserveAmmo[0];
        player.RemoveItemByDesignerName(weaponName);

        regiveTimer[slot] = AddTimer(.15f, () =>
        {
            regiveTimer[slot] = null!;
            if(!player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if(playerPawn == null || !playerPawn.IsValid || playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(weaponName));
            if(newWeapon == null || !newWeapon.IsValid) return;

            newWeapon.Clip1 = clip1;
            newWeapon.ReserveAmmo[0] = reserve;
        });
    }

    public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;

        if (player == null || !player.IsValid || player.IsBot) 
            return HookResult.Continue;

        foreach(var weapons in player.PlayerPawn.Value?.WeaponServices?.MyWeapons!)
        {
            CBasePlayerWeapon weapon;
            if(weapons != null && (weapon = weapons.Value!) != null && weapon.IsValid)
            {
                // Особенный метод потому что тут передают только DefIndex
                string weaponName = GetWeaponNameFromDefIndex(null!, (int)@event.Defindex);
                if(string.IsNullOrEmpty(weaponName)) weaponName = "weapon_" + @event.Item;

                if(weaponName == GetWeaponNameFromDefIndex(weapon))
                {
                    SetWeaponModel(weapon, false);
                    break;
                }
            }
        }

        return HookResult.Continue;
    }

    public string GetWeaponNameFromDefIndex(CBasePlayerWeapon? weapon, int _defIndex = -1)
    {
        int defIndex = _defIndex != -1 ? _defIndex : (weapon?.AttributeManager.Item.ItemDefinitionIndex ?? 0);

        string? weaponName = defIndex switch
        {
            23 => "weapon_mp5sd",
            60 => "weapon_m4a1_silencer",
            61 => "weapon_usp_silencer",
            63 => "weapon_cz75a",
            64 => "weapon_revolver",
            42 or 59 => "weapon_knife",
            500 => "weapon_bayonet",
            503 => "weapon_knife_css",
            505 => "weapon_knife_flip",
            506 => "weapon_knife_gut",
            507 => "weapon_knife_karambit",
            508 => "weapon_knife_m9_bayonet",
            509 => "weapon_knife_tactical",
            512 => "weapon_knife_falchion",
            514 => "weapon_knife_survival_bowie",
            515 => "weapon_knife_butterfly",
            516 => "weapon_knife_push",
            517 => "weapon_knife_cord",
            518 => "weapon_knife_canis",
            519 => "weapon_knife_ursus",
            520 => "weapon_knife_gypsy_jackknife",
            521 => "weapon_knife_outdoor",
            522 => "weapon_knife_stiletto",
            523 => "weapon_knife_widowmaker",
            525 => "weapon_knife_skeleton",
            526 => "weapon_knife_kukri",
            _ => null
        };

        return weaponName ?? weapon?.DesignerName ?? "";
    }

    #endregion
    #region Menu

    public IMenu CreateMenu(string title)
    {
        if (_apiMenuFox != null)
        {
            return _apiMenuFox.GetMenu(title);
        }

        CenterHtmlMenu menu = new CenterHtmlMenu(title, this);
        return menu;
    }

    [ConsoleCommand("css_cw")]
    public void CW_Menu(CCSPlayerController? player, CommandInfo _)
    {
        if(player == null)
            return;

        IMenu menu = CreateMenu(Localizer["MainMenuTitle"]);
        foreach (var weapons in Config.CW_List)
        {
            menu.AddMenuOption(Localizer[weapons.Key], (player, _) => { CW_ModelMenu(player, weapons.Value, weapons.Key); } );
        }
        menu.Open(player);
    }

    public void CW_ModelMenu(CCSPlayerController player, Dictionary<string, ModelInfo> models, string weapon_name)
    {
        IMenu menu = CreateMenu(Localizer["MenuChooseSkinTitle"]);
        menu.AddMenuOption(Localizer["ItemDisableSkin"], (player, _) => 
        { 
            CW_SetModel(player, weapon_name, "", true);
            player.PrintToChat(StringExtensions.ReplaceColorTags(Localizer["SkinSuccessRemove"]));
        });
        foreach (var weapons in models)
        {
            bool not_access = true;
            if(Config.FreeSkinsAll) not_access = false;
            else
            {
                if(accessWeapons[player.Slot].Count(x => x == ';') > 0)
                {
                    foreach(var WeaponModelName in accessWeapons[player.Slot].Split(";"))
                    {
                        if(WeaponModelName.Equals(weapons.Key, StringComparison.Ordinal))
                            not_access = false;
                    }
                }
                else
                {
                    if(accessWeapons[player.Slot].Equals(weapons.Key, StringComparison.Ordinal))
                        not_access = false;
                }
            }

            menu.AddMenuOption(weapons.Key, (player, _) => 
            {
                CW_SetModel(player, weapon_name, weapons.Key, false);
                player.PrintToChat(StringExtensions.ReplaceColorTags(Localizer["SkinSuccessInstall", weapons.Key]));
            }, not_access);
        }
        menu.Open(player);
    }

    public void CW_SetModel(CCSPlayerController player, string weapon_name, string model_name, bool remove)
    {
        WeaponInfo[player.Slot].Remove(weapon_name);

        if(!remove) WeaponInfo[player.Slot].Add(weapon_name, model_name);

        var playerPawn = player.PlayerPawn.Value;
        if(playerPawn != null && playerPawn.IsValid && playerPawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
        {
            foreach(var weapons in player.PlayerPawn.Value?.WeaponServices?.MyWeapons!)
            {
                CBasePlayerWeapon myweapon;
                if(weapons != null && (myweapon = weapons.Value!) != null && myweapon.IsValid)
                {
                    if(weapon_name == GetWeaponNameFromDefIndex(myweapon))
                    {
                        SetWeaponModel(myweapon, true, remove);
                        break;
                    }
                }
            }
        }

        ulong steamid = player.AuthorizedSteamID!.SteamId64;
        Task.Run(async () => 
        {
            try
            {
                await using (var connection = new MySqlConnection(dbConnectionString))
                {
                    await connection.OpenAsync();
                    await connection.QueryAsync($"UPDATE `custom_weapons` SET `{weapon_name}` = @WeaponModelName WHERE `auth` = @Auth;", new
                    {
                        WeaponModelName = model_name,
                        Auth = steamid
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("{CW_SetModel} Failed get info in database | " + ex.Message);
                Logger.LogDebug(ex.Message);
                throw new Exception("[CW] Failed get info in database! | " + ex.Message);
            }
        });
    }

    #endregion
    #region Commands

    [ConsoleCommand("css_cw_give")]
    [CommandHelper(minArgs: 2, usage: "<userid/name/steamid> <model_name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void CW_GiveWeapon(CCSPlayerController? _, CommandInfo commandInfo)
    {
        if(!commandInfo.GetArg(1).StartsWith("#"))
        {
            var targets = commandInfo.GetArgTargetResult(1);

            foreach(var target in targets)
            {
                if(target == null) continue;
                string modelname = commandInfo.GetArg(2);
                if(accessWeapons[target.Slot].Length > 0) accessWeapons[target.Slot] += modelname + ";";
                else accessWeapons[target.Slot] = modelname + ";";

                ulong steamid = target.AuthorizedSteamID!.SteamId64;
                
                Task.Run(async () => 
                {
                    try
                    {
                        await using (var connection = new MySqlConnection(dbConnectionString))
                        {
                            await connection.OpenAsync();
                            var data = await connection.QueryFirstOrDefaultAsync<string?>("SELECT `access_weapons` FROM `custom_weapons` WHERE `auth` = @Auth;", new
                            {
                                Auth = steamid
                            });

                            await connection.QueryAsync("UPDATE `custom_weapons` SET `access_weapons` = @AccessList WHERE `auth` = @Auth;", new
                            {
                                AccessList = accessWeapons[target.Slot],
                                Auth = steamid
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("{CW_GiveWeapon} Failed get info in database | " + ex.Message);
                        Logger.LogDebug(ex.Message);
                        throw new Exception("[CW] Failed get info in database! | " + ex.Message);
                    }
                });
            }
        }
        else
        {
            ulong steamid = Convert.ToUInt64(commandInfo.GetArg(1).Replace("#", ""));
            string modelname = commandInfo.GetArg(2);
            var target = Utilities.GetPlayers().FirstOrDefault(x => !x.IsBot && !x.IsHLTV && x.AuthorizedSteamID!.SteamId64 == steamid);
            if(target != null)
            {
                if(accessWeapons[target.Slot].Length > 0) accessWeapons[target.Slot] += modelname + ";";
                else accessWeapons[target.Slot] = modelname + ";";
            }
            
            Task.Run(async () => 
            {
                try
                {
                    await using (var connection = new MySqlConnection(dbConnectionString))
                    {
                        await connection.OpenAsync();
                        var data = await connection.QueryFirstOrDefaultAsync<string?>("SELECT `access_weapons` FROM `custom_weapons` WHERE `auth` = @Auth;", new
                        {
                            Auth = steamid
                        });

                        string model = "";
                        if(data != null)
                        {
                            model = data + modelname + ";";
                        }
                        else
                        {
                            model = modelname + ";";
                        }

                        await connection.QueryAsync("UPDATE `custom_weapons` SET `access_weapons` = @AccessList WHERE `auth` = @Auth;", new
                        {
                            AccessList = model,
                            Auth = steamid
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("{CW_GiveWeapon} Failed get info in database | " + ex.Message);
                    Logger.LogDebug(ex.Message);
                    throw new Exception("[CW] Failed get info in database! | " + ex.Message);
                }
            });
        }
        
        commandInfo.ReplyToCommand("Model gived!");
    }

    [ConsoleCommand("css_cw_remove")]
    [CommandHelper(minArgs: 2, usage: "<userid/name/#steamid> <model_name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void CW_RemoveWeapon(CCSPlayerController? _, CommandInfo commandInfo)
    {
        if(!commandInfo.GetArg(1).StartsWith("#"))
        {
            var targets = commandInfo.GetArgTargetResult(1);

            foreach(var target in targets)
            {
                if(target == null) continue;
                string modelname = commandInfo.GetArg(2);

                accessWeapons[target.Slot] = accessWeapons[target.Slot].Replace(modelname + ";", "");

                ulong steamid = target.AuthorizedSteamID!.SteamId64;
                
                Task.Run(async () => 
                {
                    try
                    {
                        await using (var connection = new MySqlConnection(dbConnectionString))
                        {
                            await connection.OpenAsync();
                            var data = await connection.QueryFirstOrDefaultAsync<string?>("SELECT `access_weapons` FROM `custom_weapons` WHERE `auth` = @Auth;", new
                            {
                                Auth = steamid
                            });

                            await connection.QueryAsync("UPDATE `custom_weapons` SET `access_weapons` = @AccessList WHERE `auth` = @Auth;", new
                            {
                                AccessList = accessWeapons[target.Slot],
                                Auth = steamid
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("{CW_GiveWeapon} Failed get info in database | " + ex.Message);
                        Logger.LogDebug(ex.Message);
                        throw new Exception("[CW] Failed get info in database! | " + ex.Message);
                    }
                });
            }
        }
        else
        {
            ulong steamid = Convert.ToUInt64(commandInfo.GetArg(1).Replace("#", ""));
            string modelname = commandInfo.GetArg(2);
            var target = Utilities.GetPlayers().FirstOrDefault(x => !x.IsBot && !x.IsHLTV && x.AuthorizedSteamID!.SteamId64 == steamid);
            if(target != null)
            {
                accessWeapons[target.Slot] = accessWeapons[target.Slot].Replace(modelname + ";", "");
            }
            
            Task.Run(async () => 
            {
                try
                {
                    await using (var connection = new MySqlConnection(dbConnectionString))
                    {
                        await connection.OpenAsync();
                        var data = await connection.QueryFirstOrDefaultAsync<string?>("SELECT `access_weapons` FROM `custom_weapons` WHERE `auth` = @Auth;", new
                        {
                            Auth = steamid
                        });

                        string model = "";
                        if(data != null)
                        {
                            model = data.Replace(modelname + ";", "");
                        }

                        await connection.QueryAsync("UPDATE `custom_weapons` SET `access_weapons` = @AccessList WHERE `auth` = @Auth;", new
                        {
                            AccessList = model,
                            Auth = steamid
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("{CW_GiveWeapon} Failed get info in database | " + ex.Message);
                    Logger.LogDebug(ex.Message);
                    throw new Exception("[CW] Failed get info in database! | " + ex.Message);
                }
            });
        }
        
        commandInfo.ReplyToCommand("Model removed!");
    }

    #endregion

    #region Config
    public void OnConfigParsed(CustomWeapons_Config config)
	{
        Config = config;
        if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
			return;

        MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
		{
			Server = config.DatabaseHost,
			Database = config.DatabaseName,
			UserID = config.DatabaseUser,
			Password = config.DatabasePassword,
			Port = (uint)config.DatabasePort,
		};

        dbConnectionString = builder.ConnectionString;

        Task.Run(async () => 
        {
            try
            {
                await using (var connection = new MySqlConnection(dbConnectionString))
                {
                    connection.Open();

                    string sql = @"CREATE TABLE IF NOT EXISTS `custom_weapons` (
                                    `id` int NOT NULL PRIMARY KEY AUTO_INCREMENT,
                                    `auth` VARCHAR(32) NOT NULL,
                                    `access_weapons` TEXT,
                                    `weapon_m4a1` VARCHAR(64),
                                    `weapon_m4a1_silencer` VARCHAR(64),
                                    `weapon_famas` VARCHAR(64),
                                    `weapon_aug` VARCHAR(64),
                                    `weapon_ak47` VARCHAR(64),
                                    `weapon_galilar` VARCHAR(64),
                                    `weapon_sg556` VARCHAR(64),
                                    `weapon_scar20` VARCHAR(64),
                                    `weapon_awp` VARCHAR(64),
                                    `weapon_ssg08` VARCHAR(64),
                                    `weapon_g3sg1` VARCHAR(64),
                                    `weapon_mp9` VARCHAR(64),
                                    `weapon_mp7` VARCHAR(64),
                                    `weapon_mp5sd` VARCHAR(64),
                                    `weapon_ump45` VARCHAR(64),
                                    `weapon_p90` VARCHAR(64),
                                    `weapon_bizon` VARCHAR(64),
                                    `weapon_mac10` VARCHAR(64),
                                    `weapon_usp_silencer` VARCHAR(64),
                                    `weapon_hkp2000` VARCHAR(64),
                                    `weapon_glock` VARCHAR(64),
                                    `weapon_elite` VARCHAR(64),
                                    `weapon_p250` VARCHAR(64),
                                    `weapon_fiveseven` VARCHAR(64),
                                    `weapon_cz75a` VARCHAR(64),
                                    `weapon_tec9` VARCHAR(64),
                                    `weapon_revolver` VARCHAR(64),
                                    `weapon_deagle` VARCHAR(64),
                                    `weapon_nova` VARCHAR(64),
                                    `weapon_xm1014` VARCHAR(64),
                                    `weapon_mag7` VARCHAR(64),
                                    `weapon_sawedoff` VARCHAR(64),
                                    `weapon_m249` VARCHAR(64),
                                    `weapon_negev` VARCHAR(64),
                                    `weapon_taser` VARCHAR(64),
                                    `weapon_hegrenade` VARCHAR(64),
                                    `weapon_molotov` VARCHAR(64),
                                    `weapon_incgrenade` VARCHAR(64),
                                    `weapon_smokegrenade` VARCHAR(64),
                                    `weapon_flashbang` VARCHAR(64),
                                    `weapon_decoy` VARCHAR(64),
                                    `weapon_knife` VARCHAR(64),
                                    `weapon_knife_karambit` VARCHAR(64),
                                    `weapon_knife_m9_bayonet` VARCHAR(64),
                                    `weapon_knife_butterfly` VARCHAR(64),
                                    `weapon_bayonet` VARCHAR(64),
                                    `weapon_knife_tactical` VARCHAR(64),
                                    `weapon_knife_stiletto` VARCHAR(64),
                                    `weapon_knife_widowmaker` VARCHAR(64),
                                    `weapon_knife_skeleton` VARCHAR(64),
                                    `weapon_knife_ursus` VARCHAR(64),
                                    `weapon_knife_css` VARCHAR(64),
                                    `weapon_knife_push` VARCHAR(64),
                                    `weapon_knife_survival_bowie` VARCHAR(64),
                                    `weapon_knife_falchion` VARCHAR(64),
                                    `weapon_knife_gypsy_jackknife` VARCHAR(64),
                                    `weapon_knife_flip` VARCHAR(64),
                                    `weapon_knife_cord` VARCHAR(64),
                                    `weapon_knife_gut` VARCHAR(64),
                                    `weapon_knife_outdoor` VARCHAR(64),
                                    `weapon_knife_canis` VARCHAR(64),
                                    `weapon_knife_kukri` VARCHAR(64)
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;";

                    await connection.ExecuteAsync(sql);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("{OnConfigParsed} Unable to connect to database! | " + ex.Message);
                Logger.LogDebug(ex.Message);
                throw new Exception("[CW] Unable to connect to Database! | " + ex.Message);
            }
        });
    }
    #endregion
}

public class ModelInfo
{
    public required string Model { get; set;}
    public required string CustomName { get; set;}
}
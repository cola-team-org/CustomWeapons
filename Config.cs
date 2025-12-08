using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace CustomWeapons;

public class CustomWeapons_Config : BasePluginConfig
{
	[JsonPropertyName("WeaponList")] public Dictionary<string, Dictionary<string, ModelInfo>> CW_List {get; set;} = new()
    {
        {"weapon_m4a1", new Dictionary<string, ModelInfo>(){ {"Тестовая модель", new ModelInfo { Model = "weapons/model.vmdl", CustomName = "" } } }}
    };

    [JsonPropertyName("FreeSkinsAll")]
    public bool FreeSkinsAll { get; set; } = false;

    [JsonPropertyName("DatabaseHost")]
    public string DatabaseHost { get; set; } = "";

    [JsonPropertyName("DatabasePort")]
    public int DatabasePort { get; set; } = 3306;

    [JsonPropertyName("DatabaseUser")]
    public string DatabaseUser { get; set; } = "";

    [JsonPropertyName("DatabasePassword")]
    public string DatabasePassword { get; set; } = "";

    [JsonPropertyName("DatabaseName")]
    public string DatabaseName { get; set; } = "";
}
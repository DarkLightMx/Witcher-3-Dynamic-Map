using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Witcher_3_Dynamic_Map;

/// <summary>UI texts in English (default) and Russian. The chosen language is remembered in settings.json.</summary>
public static class Loc
{
    public const string English = "en";
    public const string Russian = "ru";

    public static string Language { get; private set; } = English;
    public static event Action? Changed;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Witcher3DynamicMap", "settings.json");

    private sealed class Settings { public string Language { get; set; } = English; }

    /// <summary>Reads the saved language; English when there is no (valid) settings file.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (s?.Language is Russian) Language = Russian;
            }
        }
        catch { }
    }

    public static void Set(string language)
    {
        if (language != English && language != Russian) return;
        if (language == Language) return;
        Language = language;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Language = language }));
        }
        catch { }
        Changed?.Invoke();
    }

    public static string T(string key)
        => Table.TryGetValue(key, out var v) ? (Language == Russian ? v.Ru : v.En) : key;

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    public static string RegionTitle(string region)
        => Table.TryGetValue("region." + region, out var v) ? (Language == Russian ? v.Ru : v.En) : region;

    /// <summary>Title of a POI type; unknown types are shown as "Some Type" from "SomeType".</summary>
    public static string PoiTitle(string type)
        => Table.TryGetValue("poi." + type, out var v)
            ? (Language == Russian ? v.Ru : v.En)
            : Regex.Replace(type, "(?<=[a-z])(?=[A-Z])", " ");

    private static readonly Dictionary<string, (string En, string Ru)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // status line
        ["status.starting"] = ("Starting...", "Запуск..."),
        ["status.notRunning"] = ("Witcher 3 is not running", "Witcher 3 не запущен"),
        ["status.waiting"] = ("Waiting for data from the mod...", "Ожидание данных от мода..."),
        ["status.noLog"] = ("No scriptslog.txt (-debugscripts)", "Нет файла scriptslog.txt (-debugscripts)"),
        ["status.connected"] = ("Game connected", "Игра подключена"),

        // side panel
        ["card.geralt"] = ("GERALT", "ГЕРАЛЬТ"),
        ["card.quest"] = ("TRACKED QUEST", "ОТСЛЕЖИВАЕМЫЙ КВЕСТ"),
        ["quest.noData"] = ("No data from the game", "Нет данных из игры"),
        ["quest.none"] = ("No quest selected", "Квест не выбран"),
        ["quest.needMod"] = ("The mod and -debugscripts are required", "Нужен мод и запуск с -debugscripts"),
        ["quest.noLog"] = ("No scriptslog.txt file", "Нет файла scriptslog.txt"),
        ["quest.visited"] = ("Visited {0} of {1}", "Посещено {0} из {1}"),
        ["poi.title"] = ("Places of interest", "Места интереса"),
        ["poi.all"] = ("All", "Все"),
        ["poi.none"] = ("None", "Нет"),
        ["switch.follow"] = ("Follow Geralt", "Следовать за Геральтом"),
        ["switch.hideVisited"] = ("Hide visited", "Скрывать посещённые"),
        ["button.center"] = ("Center on Geralt", "Центр на Геральте"),
        ["tip.panel"] = ("Hide / show the panel (Ctrl+B)", "Скрыть / показать панель (Ctrl+B)"),
        ["msg.noTiles"] = ("Map tiles not found. Expected folder:\n{0}", "Не найдены тайлы карты. Ожидаемая папка:\n{0}"),

        // worlds
        ["region.Velen"] = ("Velen and Novigrad", "Велен и Новиград"),
        ["region.Skellige"] = ("Skellige", "Скеллиге"),
        ["region.Kaer Morhen"] = ("Kaer Morhen", "Каэр Морхен"),
        ["region.Toussaint"] = ("Toussaint", "Туссент"),
        ["region.White Orchard"] = ("White Orchard", "Белый Сад"),

        // POI types (the game's map pin types)
        ["poi.RoadSign"] = ("Signposts", "Указатели"),
        ["poi.Boat"] = ("Boats", "Лодки"),
        ["poi.TreasureHuntMappin"] = ("Treasure hunts", "Охота за сокровищами"),
        ["poi.BanditCampfire"] = ("Bandit campfires", "Костры бандитов"),
        ["poi.BanditCamp"] = ("Bandit camps", "Лагеря бандитов"),
        ["poi.BossAndTreasure"] = ("Guarded treasures", "Охраняемые сокровища"),
        ["poi.MonsterNest"] = ("Monster nests", "Гнёзда монстров"),
        ["poi.Entrance"] = ("Entrances", "Входы"),
        ["poi.Teleport"] = ("Teleports", "Порталы"),
        ["poi.NoticeBoard"] = ("Notice boards", "Доски объявлений"),
        ["poi.Whetstone"] = ("Whetstones", "Точильные камни"),
        ["poi.ArmorRepairTable"] = ("Repair tables", "Столы починки"),
        ["poi.Harbor"] = ("Harbors", "Гавани"),
        ["poi.PlaceOfPower"] = ("Places of power", "Места силы"),
        ["poi.SpoilsOfWar"] = ("Spoils of war", "Военные трофеи"),
        ["poi.Contraband"] = ("Smugglers", "Контрабандисты"),
        ["poi.Shopkeeper"] = ("Merchants", "Торговцы"),
        ["poi.Enchanter"] = ("Enchanters", "Зачарователи"),
        ["poi.Herb"] = ("Herbs", "Травы"),
        ["poi.Herbalist"] = ("Herbalists", "Травники"),
        ["poi.Alchemic"] = ("Alchemists", "Алхимики"),
        ["poi.AlchemyTable"] = ("Alchemy tables", "Алхимические столы"),
        ["poi.Innkeeper"] = ("Innkeepers", "Трактирщики"),
        ["poi.Blacksmith"] = ("Blacksmiths", "Кузнецы"),
        ["poi.Armorer"] = ("Armorers", "Оружейники"),
        ["poi.InfestedVineyard"] = ("Infested vineyards", "Заражённые виноградники"),
        ["poi.RescuingTown"] = ("Villages in need", "Спасение поселений"),
        ["poi.DungeonCrawl"] = ("Dungeons", "Подземелья"),
        ["poi.MagicLamp"] = ("Magic lamps", "Волшебные лампы"),
        ["poi.Rift"] = ("Rifts", "Разломы"),
        ["poi.PlayerStash"] = ("Stashes", "Тайники"),
    };
}

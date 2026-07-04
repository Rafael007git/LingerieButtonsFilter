using BepInEx.Configuration;

namespace LingerieButtonsFilter
{
    // Анатомический справочник для Mod Tool и плагина
    public enum CustomSlotType
    {
        Accessorie = 100,
        Hats = 101,
        Eyes = 102,
        Mouth = 103,
        Earrings = 104,
        Wrists = 111,
        Neck = 112,
        Nipples = 113
    }

    public static class ModConfig
    {
        // UI Геометрия
        public static ConfigEntry<float> StartY;
        public static ConfigEntry<float> ButtonWidth;
        public static ConfigEntry<float> ButtonHeight;
        public static ConfigEntry<float> Spacing;

        // Словари-распределители для старых/чужих предметов под вашу новую анатомию
        public static ConfigEntry<string> KeywordsAccessorie;
        public static ConfigEntry<string> KeywordsHats;
        public static ConfigEntry<string> KeywordsEyes;
        public static ConfigEntry<string> KeywordsMouth;
        public static ConfigEntry<string> KeywordsEarrings;
        public static ConfigEntry<string> KeywordsWrists;
        public static ConfigEntry<string> KeywordsNeck;
        public static ConfigEntry<string> KeywordsNipples;

        public static void Init(ConfigFile config)
        {
            // UI настройки
            StartY = config.Bind("UI Geometry", "StartY", -250f, "Смещение всего ряда.");
            ButtonWidth = config.Bind("UI Geometry", "ButtonWidth", 176f, "Ширина кнопок.");
            ButtonHeight = config.Bind("UI Geometry", "ButtonHeight", 46f, "Высота кнопок.");
            Spacing = config.Bind("UI Geometry", "Spacing", 7f, "Отступ между кнопками.");

            // Мощная рантайм-таблица для распределения чужих модов по именам файлов (Обновленная!)
            KeywordsAccessorie = config.Bind("Filters Other", "KeywordsAccessorie", "Flower,Tail,Wing,Harness,Belt,Dress,Corset,Skirt,Ribbon", "Общие украшения, платья, портупеи.");
            KeywordsHats = config.Bind("Filters Masks", "KeywordsHats", "Hat,Crown,Cap,Helmet,Tiara,Wig,Hair", "Шляпы, короны, парики.");
            KeywordsEyes = config.Bind("Filters Masks", "KeywordsEyes", "Mask,Blindfold,Glasses,Visor,Goggles,Monocle,Eyepatch", "Очки, повязки и маски на глаза.");
            KeywordsMouth = config.Bind("Filters Masks", "KeywordsMouth", "Gag,Mouth,Bit,BallGag,RingGag,Tape,Cleave", "Кляпы и фиксаторы рта.");
            KeywordsEarrings = config.Bind("Filters Masks", "KeywordsEarrings", "Earring,Ears,Piercing,Stud,Hoop", "Серьги и пирсинг ушей.");

            KeywordsWrists = config.Bind("Filters Other", "KeywordsWrists", "Cuff,Bracers,Bracelet,Shackles,Gloves,Mittens,Anklet,Wrist", "Наручники, браслеты, перчатки.");
            KeywordsNeck = config.Bind("Filters Other", "KeywordsNeck", "Collar,Choker,Necklace,Neck,CollarBell", "Ошейники, чокеры.");
            KeywordsNipples = config.Bind("Filters Other", "KeywordsNipples", "Nipple,Pasties,Nude,PiercingBody", "Наклейки на соски, интимный пирсинг.");
        }
    }
}
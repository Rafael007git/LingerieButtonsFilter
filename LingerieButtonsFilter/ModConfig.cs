using BepInEx.Configuration;

namespace LingerieButtonsFilter
{
    // Anatomical directory for the mod and UI systems
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
        // UI Layout Geometry
        public static ConfigEntry<float> StartY;
        public static ConfigEntry<float> ButtonWidth;
        public static ConfigEntry<float> ButtonHeight;
        public static ConfigEntry<float> Spacing;

        // Keyword filters for legacy/external items redistribution
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
            // UI layout configuration
            StartY = config.Bind("UI Geometry", "StartY", -40f, "Vertical layout offset for the custom button group row.");
            ButtonWidth = config.Bind("UI Geometry", "ButtonWidth", 176f, "Custom button width layout property.");
            ButtonHeight = config.Bind("UI Geometry", "ButtonHeight", 46f, "Custom button height layout property.");
            Spacing = config.Bind("UI Geometry", "Spacing", 7f, "Padding spacing distance between layout buttons.");

            // Dynamic runtime tables mapping items by filenames to new category slots
            KeywordsAccessorie = config.Bind("Filters Other", "KeywordsAccessorie", "Flower,Tail,Wing,Harness,Belt,Dress,Corset,Skirt,Ribbon", "General accessories, dresses, harnesses, belts, skirts.");
            KeywordsHats = config.Bind("Filters Masks", "KeywordsHats", "Hat,Crown,Cap,Helmet,Tiara,Wig,Hair", "Hats, crowns, wigs, and helmets.");
            KeywordsEyes = config.Bind("Filters Masks", "KeywordsEyes", "Mask,Blindfold,Glasses,Visor,Goggles,Monocle,Eyepatch", "Glasses, blindfolds, and eye masks.");
            KeywordsMouth = config.Bind("Filters Masks", "KeywordsMouth", "Gag,Mouth,Bit,BallGag,RingGag,Tape,Cleave", "Gags and mouth adjusters.");
            KeywordsEarrings = config.Bind("Filters Masks", "KeywordsEarrings", "Earring,Ears,Piercing,Stud,Hoop", "Earrings and ear piercings.");
        }
    }
}
using BepInEx.Configuration;

namespace LingerieButtonsFilter
{
    public static class ModConfig
    {
        // Объявляем записи конфигурации BepInEx
        public static ConfigEntry<float> StartY;
        public static ConfigEntry<float> ButtonWidth;
        public static ConfigEntry<float> ButtonHeight;
        public static ConfigEntry<float> Spacing;

        // Метод инициализации конфига, который мы вызовем при старте мода
        public static void Init(ConfigFile config)
        {
            StartY = config.Bind("UI Geometry", "StartY", 0f, "Смещение всего ряда. Если нужно опустить еще ниже — введите отрицательное число (например, -20).");
            ButtonWidth = config.Bind("UI Geometry", "ButtonWidth", 150f, "Ширина кнопок в пикселях (если не сработал адаптивный режим).");
            ButtonHeight = config.Bind("UI Geometry", "ButtonHeight", 36f, "Высота каждой кнопки в пикселях.");
            Spacing = config.Bind("UI Geometry", "Spacing", 5f, "Отступ между кнопками в пикселях.");
        }
    }
}
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

namespace LingerieButtonsFilter
{
    [BepInPlugin("com.yourname.swpt.inventoryfilter", "SWPT Advanced Wardrobe", "1.2.0")]
    public class MainPlugin : BaseUnityPlugin
    {
        public static int FilterMode = 0; // 0 - дефолт, 1 - MASKS, 2 - OTHER
        public static bool IsUiCustomized = false;
        public static Sprite MasksSprite;
        public static Sprite OtherSprite;

        // Наша база данных: Ключ — имя предмета (в нижнем регистре), Значение — ID нового слота
        public static Dictionary<string, int> ItemMappingTable = new Dictionary<string, int>();

        private void Awake()
        {
            // 1. Инициализируем конфигурацию BepInEx и загружаем ресурсы
            ModConfig.Init(Config);
            LoadEmbeddedIcons();
            LoadItemMappingTable(); // Запускаем чтение нашей текстовой базы данных!

            // 2. ПОДПИСКА НА СЦЕНЫ: Здесь живет СТРОГО верстка интерфейса, которая обновляется при загрузках
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                IsUiCustomized = false;

                // Находим инвентарь на сцене (даже если он временно деактивирован разработчиками)
                UIInventory uiInventory = Resources.FindObjectsOfTypeAll<UIInventory>().Length > 0
                    ? Resources.FindObjectsOfTypeAll<UIInventory>()[0]
                    : null;

                if (uiInventory != null)
                {
                    Transform cat2 = uiInventory.transform.Find("Right/Lingerie Group/Category (2)");
                    if (cat2 != null && cat2.gameObject.GetComponent<InventoryUiController>() == null)
                    {
                        // Подселяем наш контроллер верстки на панель кнопок
                        cat2.gameObject.AddComponent<InventoryUiController>();
                    }
                }
            };

            // 3. ЖЕЛЕЗНАЯ СИСТЕМНАЯ АКТИВИЗАЦИЯ ХАРМОНИ (СТРОГО ОДИН РАЗ И ВНЕ ЗАГРУЗКИ СЦЕН)
            // Этот вызов автоматически найдет и активирует ВСЕ наши патчи в проекте:
            // и фильтрацию предметов (InventoryFilterPatch), и щит от спама логов (PMC_Setting_GetKeyDown_ShieldPatch)!
            var harmony = new Harmony("com.yourname.swpt.inventoryfilter");
            harmony.PatchAll();

            Logger.LogInfo("Финальный релиз мода гардероба 1.2.0 успешно запущен и защищен!");
        }

        private void LoadEmbeddedIcons()
        {
            try
            {
                string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                MasksSprite = LoadSpriteFromResource($"{assemblyName}.icon_masks.png");
                OtherSprite = LoadSpriteFromResource($"{assemblyName}.icon_other.png");

                if (MasksSprite != null && OtherSprite != null)
                {
                    Logger.LogInfo("[SWPT Assets] Кастомные иконки MASKS и OTHER успешно активированы!");
                }
                else
                {
                    Logger.LogWarning("[SWPT Assets] Спрайты вернули null. Доступные ресурсы:");
                    foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    {
                        Logger.LogInfo($" -> '{res}'");
                    }
                }
            }
            catch (Exception ex) { Logger.LogError($"[SWPT Assets] Ошибка ресурсов: {ex.Message}"); }
        }

        private Sprite LoadSpriteFromResource(string resourcePath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream == null) return null;
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(buffer))
                {
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            return null;
        }

        private void LoadItemMappingTable()
        {
            try
            {
                // ИДЕАЛЬНОЕ МЕСТО: Переносим файл строго в BepInEx\config\Lingerie_Item_Mapping.txt
                string filePath = Path.Combine(BepInEx.Paths.ConfigPath, "Lingerie_Item_Mapping.txt");

                // Если файла нет, генерируем красивый, понятный шаблон-инструкцию для игрока
                if (!File.Exists(filePath))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# === ТАБЛИЦА РАСПРЕДЕЛЕНИЯ ПРЕДМЕТОВ ГАРДЕРОБА ===");
                    sb.AppendLine("# Укажите имя объекта из UnityExplorer и через знак '=' присвойте ему анатомический тип.");
                    sb.AppendLine("# Доступные типы: Accessorie, Hats, Eyes, Mouth, Earrings, Wrists, Neck, Nipples");
                    sb.AppendLine("# Все неуказанные кастомные предметы автоматически станут 'Accessorie'.");
                    sb.AppendLine("# Пример:");
                    sb.AppendLine("BDSM_Collar_Black = Neck");
                    sb.AppendLine("Super_Sexy_Gag_v2 = Mouth");
                    File.WriteAllText(filePath, sb.ToString());
                }

                ItemMappingTable.Clear();
                string[] lines = File.ReadAllLines(filePath);

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    string[] parts = trimmed.Split('=');
                    if (parts.Length == 2)
                    {
                        string itemName = parts[0].Trim().ToLower(); // Берем левую часть (имя вещи)
                        string typeStr = parts[1].Trim();            // Берем правую часть (тип)

                        // Магия превращения текста в системный ID (через наш Enum CustomSlotType)
                        int targetSlotId = 100; // По умолчанию Accessorie, если текст не распознан

                        if (System.Enum.TryParse(typeStr, true, out CustomSlotType matchedType))
                        {
                            targetSlotId = (int)matchedType;
                        }

                        if (!ItemMappingTable.ContainsKey(itemName))
                        {
                            ItemMappingTable.Add(itemName, targetSlotId);
                        }
                    }
                }
                Logger.LogInfo($"[SWPT] Загружена конфигурация маппинга из папки config. Записей: {ItemMappingTable.Count}");
            }
            catch (System.Exception ex) { Logger.LogError($"Ошибка загрузки Lingerie_Item_Mapping.txt: {ex.Message}"); }
        }

    }
}

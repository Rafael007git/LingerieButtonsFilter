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
        // ОБЪЯВЛЯЕМ ЗДЕСЬ (чтобы кнопка из другого файла могла его вызвать):
        public static Action FilterModeChanged;

        public static int FilterMode = 0; // 0 - дефолт, 1 - MASKS, 2 - OTHER
        public static bool IsUiCustomized = false;
        public static Sprite MasksSprite;
        public static Sprite OtherSprite;

        // Наша база данных: Ключ — имя предмета (в нижнем регистре), Значение — ID нового слота
        public static Dictionary<string, int> ItemMappingTable = new Dictionary<string, int>();

        private void Awake()
        {
            // Инициализируем конфигурацию и загружаем ресурсы
            ModConfig.Init(Config);
            LoadEmbeddedIcons();
            LoadItemMappingTable();

            // ПОДПИСКА НА СЦЕНЫ (Чистый, стабильный вариант с защитой от массивов Unity)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                IsUiCustomized = false;

                // Находим массив всех объектов UIInventory на сцене
                UIInventory[] foundInventories = Resources.FindObjectsOfTypeAll<UIInventory>();

                // Если массив не пустой — берем строго первый элемент!
                UIInventory uiInventory = (foundInventories != null && foundInventories.Length > 0)
                    ? foundInventories[0]
                    : null;

                if (uiInventory != null)
                {
                    Transform cat2 = uiInventory.transform.Find("Right/Lingerie Group/Category (2)");
                    if (cat2 != null && cat2.gameObject.GetComponent<InventoryUiController>() == null)
                    {
                        cat2.gameObject.AddComponent<InventoryUiController>();
                    }
                }
            };

            // ЗАПУСК ХАРМОНИ ДЛЯ ВСЕХ СТАБИЛЬНЫХ КЛАССОВ (UI-Фильтр и Щит от спама)
            // Он выполнится мгновенно и без единой ошибки, так как капризный шкаф из него убран!
            var harmony = new Harmony("com.yourname.swpt.inventoryfilter");
            harmony.PatchAll();

            Logger.LogInfo("Мод гардероба 1.2.0 успешно запущен и защищен!");
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
                // ЖЕЛЕЗНЫЙ ПУТЬ BEPINEX: Прямое попадание в настоящую папку BepInEx\config
                string configFolder = BepInEx.Paths.ConfigPath;
                string filePath = Path.Combine(configFolder, "Lingerie_Item_Mapping.txt");

                Logger.LogInfo($"[SWPT] Проверяем путь маппинга: {filePath}");

                // Если папки config вдруг нет (фантастика, но всё же), создаем её
                if (!Directory.Exists(configFolder))
                {
                    Directory.CreateDirectory(configFolder);
                }

                // Генерируем красивый, понятный шаблон-инструкцию, если файла еще нет
                if (!File.Exists(filePath))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# === ТАБЛИЦА РАСПРЕДЕЛЕНИЯ ПРЕДМЕТОВ ГАРДЕРОБА ===");
                    sb.AppendLine("# Укажите имя объекта из UnityExplorer и через знак '=' присвойте ему анатомический тип.");
                    sb.AppendLine("# Доступные типы: Accessorie, Hats, Eyes, Mouth, Earrings, Wrists, Neck, Nipples");
                    sb.AppendLine("# Все неуказанные кастомные предметы автоматически станут 'Accessorie'.");
                    sb.AppendLine("# ------------------------------------------------------------------------------");
                    sb.AppendLine("# Пример:");
                    sb.AppendLine("BDSM_Collar_Black = Neck");
                    sb.AppendLine("Super_Sexy_Gag_v2 = Mouth");

                    File.WriteAllText(filePath, sb.ToString());
                    Logger.LogInfo("[SWPT] Файл Lingerie_Item_Mapping.txt успешно создан автоматически!");
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
                        // ЖЕСТКИЕ ИНДЕКСЫ МАССИВА: [0] — левая часть, [1] — правая часть
                        string itemName = parts[0].Trim().ToLower();
                        string typeStr = parts[1].Trim().ToLower();

                        int targetSlotId = 100; // По умолчанию Accessorie

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
                Logger.LogInfo($"[SWPT] Успешно загружена конфигурация маппинга. Записей: {ItemMappingTable.Count}");
            }
            catch (System.Exception ex)
            {
                // Теперь мы ЖЕСТКО выведем ошибку в консоль, если Windows или BepInEx заблокируют запись!
                Logger.LogError($"[SWPT КРИТ] Ошибка инициализации Lingerie_Item_Mapping.txt: {ex.Message}");
            }
        }


    }
}

using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    [HarmonyPatch]
    public class ClosetClickPatch
    {
        // ДИНАМИЧЕСКИЙ ПОИСК ЦЕЛИ: Находит скрытый класс InventoryClosetItem в памяти игры при старте,
        // полностью избавляя проект от ошибок отсутствия DLL в References!
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type targetType = assembly.GetType("InventoryClosetItem");
                if (targetType != null)
                {
                    return targetType.GetMethod("ButtonTryOn", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            return null;
        }

        [HarmonyPrefix]
        public static bool Prefix(object __instance)
        {
            if (__instance == null) return true;

            try
            {
                // Вытаскиваем поле "item" через рефлексию, так как мы работаем с универсальным типом object
                FieldInfo itemField = __instance.GetType().GetField("item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemField == null) return true;

                Transform itemTransform = (Transform)itemField.GetValue(__instance);
                if (itemTransform == null) return true;

                string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

                // ----------------====================================================
                // ЧИТАЕМ БЛОКНОТ НА ЛЕТУ ПРЯМО ПРИ КЛИКЕ ДЛЯ ГАРАНТИИ
                // ----------------====================================================
                Dictionary<string, int> clickMapTable = new Dictionary<string, int>();
                string filePath = Path.Combine(BepInEx.Paths.ConfigPath, "Lingerie_Item_Mapping.txt");
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                        string[] parts = trimmed.Split(new char[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim().ToLower();
                            string val = parts[1].Trim().ToLower();
                            int id = 100;
                            if (Enum.TryParse(val, true, out CustomSlotType mType)) id = (int)mType;
                            if (!clickMapTable.ContainsKey(key)) clickMapTable.Add(key, id);
                        }
                    }
                }

                // Проверяем предмет по нашему Блокноту
                if (clickMapTable.TryGetValue(itemNameLower, out int customCategoryId))
                {
                    var itemComponent = itemTransform.GetComponent<Item>();

                    // Accessories (100) пропускаем — пускай идут по дефолтной логике игры
                    if (customCategoryId == 100 && itemComponent != null && itemComponent.slotType == SlotType.none) return true;

                    CharacterCustomization curCustomization = Global.code?.uiInventory?.curCustomization;
                    if (curCustomization == null) return true;

                    curCustomization.showArmor = false;
                    bool isAlreadyWearing = curCustomization.IsWearing(itemTransform.name);

                    // Создаем уникальный виртуальный текстовый маркер слота куклы!
                    string customMarker = $"custom_{customCategoryId}";

                    // ХИРУРГИЧЕСКАЯ ОЧИСТКА СЛОТА (ВЫТЕСНЕНИЕ ВЕЩЕЙ ОДНОЙ КАТЕГОРИИ)
                    foreach (Transform child in curCustomization.GetComponentsInChildren<Transform>(true))
                    {
                        if (child == null || !child.gameObject.activeSelf) continue;
                        string childName = child.name.ToLower().Replace("(clone)", "").Trim();

                        if (clickMapTable.TryGetValue(childName, out int wornCategoryId))
                        {
                            if (wornCategoryId == customCategoryId && childName != itemNameLower)
                            {
                                Debug.Log($"[SWPT ШКАФ]: Вытеснение! Насильно удаляем старую модель '{child.name}' из категории {customCategoryId}...");
                                child.gameObject.SetActive(false);
                                GameObject.Destroy(child.gameObject);
                            }
                        }
                    }

                    // СПАВН И ЭКИПИРОВКА НА ПУСТОЕ МЕСТО
                    if (!isAlreadyWearing)
                    {
                        Debug.Log($"[SWPT ШКАФ]: Спавним предмет '{itemTransform.name}' на виртуальный маркер '{customMarker}'...");
                        Transform newModelTransform = Utility.Instantiate(itemTransform);

                        // Вызываем метод AddItem куклы напрямую, подсовывая наш кастомный маркер!
                        // Теперь маска улетит на "custom_102" и НЕ сотрет перчатки!
                        curCustomization.AddItem(newModelTransform, customMarker);
                    }

                    // Обновляем интерфейс шкафа стандартными командами игры.
                    // Игра сама проиграет нужные звуки внутри этих вызовов!
                    Global.code?.uiInventory?.ButtonUnderwearGroup();
                    Global.code?.uiInventory?.RefreshEquipment();

                    // ПОЛНОСТЬЮ БЛОКИРУЕМ ОРИГИНАЛЬНЫЙ МЕТОД ИГРЫ! Защита перчаток активирована!
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SWPT ШКАФ КРИТ]: Ошибка в патче клика гардероба: {ex.Message}");
            }

            return true;
        }
    }
}

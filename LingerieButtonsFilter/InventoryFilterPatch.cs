using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    [HarmonyPatch]
    public class InventoryFilterPatch
    {
        private static List<Transform> originalItemsBackup = null;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type classType = typeof(UIInventory);
            foreach (Type nestedType in classType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (nestedType.Name.Contains("GenerateIcons"))
                {
                    return nestedType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            return null;
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                Type iteratorType = __instance.GetType();
                FieldInfo stateField = iteratorType.GetField("<>1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateField == null) return;

                int state = (int)stateField.GetValue(__instance);

                // Перехватываем рантайм на первом шаге генерации иконок
                if (state == 1 && MainPlugin.FilterMode != 0 && originalItemsBackup == null)
                {
                    if (Global.code?.playerLingerieStorage?.items?.items == null) return;
                    List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;

                    originalItemsBackup = new List<Transform>(gameItemsList);

                    // ВРЕМЕННЫЙ ШПИОН: Печатает в консоль точные системные имена всех вещей игрока
                    foreach (Transform t in originalItemsBackup)
                    {
                        if (t != null) Debug.Log($"[SWPT ШПИОН ИМЕН]: '{t.name}'");
                    }

                    List<Transform> filteredItems = new List<Transform>();

                    foreach (Transform itemTransform in originalItemsBackup)
                    {
                        if (itemTransform == null) continue;

                        var itemComponent = itemTransform.GetComponent<Item>();
                        if (itemComponent != null)
                        {
                            var slotType = itemComponent.slotType;
                            int slotTypeInt = (int)slotType;
                            string itemNameLower = itemTransform.name.ToLower();

                            // --- ЖЕЛЕЗНАЯ ЛОГИКА ТАБЛИЦЫ СООТВЕТСТВИЙ ---
                            // 1. Стандартное белье игры (лифчики, трусики и т.д.) защищаем от любых изменений
                            bool isStandard = (slotType == SlotType.bra || slotType == SlotType.panties ||
                                               slotType == SlotType.stockings || slotType == SlotType.suspenders ||
                                               slotType == SlotType.heels);

                            if (!isStandard)
                            {
                                // 2. Ищем предмет в текстовой базе данных Lingerie_Item_Mapping.txt
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    slotTypeInt = customSlotId; // Успешно перераспределяем в ваш точный слот
                                }
                                else if (slotTypeInt < 100)
                                {
                                    // 3. Если предмета нет в списке и это чужой старый мод (ID < 100, например none или gloves),
                                    // насильно отправляем его в базовую категорию Accessorie (100)
                                    slotTypeInt = 100;
                                } // <-- Вот здесь должна быть просто ЧИСТАЯ скобка, без всяких "room"
                            }

                            // --- РАСПРЕДЕЛЕНИЕ ПО ФИЗИЧЕСКИМ КНОПКАМ ИНТЕРФЕЙСА ---
                            if (MainPlugin.FilterMode == 1)
                            {
                                // КНОПКА MASKS: Собирает всё, что на голове (ID 101, 102, 103, 104) 
                                // и оставляет оригинальный тип 7 (lingeriegloves), если он не был переназначен на тело
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                // КНОПКА OTHER: Собирает всё, что на теле (ID 100 — Accessorie, 111 — Wrists, 112 — Neck, 113 — Nipples)
                                if (slotTypeInt == 100 || (slotTypeInt >= 111 && slotTypeInt <= 113))
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                        }
                    }

                    // Подменяем список игры на наш кастомный отфильтрованный набор
                    gameItemsList.Clear();
                    gameItemsList.AddRange(filteredItems);
                }
            }
            catch (Exception ex) { Debug.LogError($"[SWPT Filter] Ошибка Prefix: {ex.Message}"); }
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance, ref bool __result)
        {
            if (!__result && originalItemsBackup != null)
            {
                RestoreImmediately();
                return;
            }

            try
            {
                Type iteratorType = __instance.GetType();
                FieldInfo stateField = iteratorType.GetField("<>1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateField != null && (int)stateField.GetValue(__instance) == -1)
                {
                    RestoreImmediately();
                }
            }
            catch { }
        }

        private static void RestoreImmediately()
        {
            if (originalItemsBackup != null && Global.code?.playerLingerieStorage?.items?.items != null)
            {
                try
                {
                    List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;
                    gameItemsList.Clear();
                    gameItemsList.AddRange(originalItemsBackup);
                    originalItemsBackup = null;
                }
                catch (Exception ex) { Debug.LogError($"[SWPT Filter] Ошибка восстановления: {ex.Message}"); }
            }
        }
    }

    // ЖЕЛЕЗНЫЙ ХАНИНГ-ЩИТ: Навсегда спасает Player.log и процессор от багов игры с инпутом
    [HarmonyPatch(typeof(PMC_Setting), "GetKeyDown")]
    public class PMC_Setting_GetKeyDown_ShieldPatch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception != null)
            {
                __result = false;
                return null;
            }
            return null;
        }
    }
}

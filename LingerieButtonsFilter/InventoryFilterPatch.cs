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

                if (state == 1 && MainPlugin.FilterMode != 0 && originalItemsBackup == null)
                {
                    if (Global.code?.playerLingerieStorage?.items?.items == null) return;
                    List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;

                    originalItemsBackup = new List<Transform>(gameItemsList);
                    List<Transform> filteredItems = new List<Transform>();

                    // Читаем обновленные массивы слов из конфигурации
                    string[] kwAccessorie = ModConfig.KeywordsAccessorie.Value.Split(',');
                    string[] kwHats = ModConfig.KeywordsHats.Value.Split(',');
                    string[] kwEyes = ModConfig.KeywordsEyes.Value.Split(',');
                    string[] kwMouth = ModConfig.KeywordsMouth.Value.Split(',');
                    string[] kwEarrings = ModConfig.KeywordsEarrings.Value.Split(',');
                    string[] kwWrists = ModConfig.KeywordsWrists.Value.Split(',');
                    string[] kwNeck = ModConfig.KeywordsNeck.Value.Split(',');
                    string[] kwNipples = ModConfig.KeywordsNipples.Value.Split(',');

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
                            // 1. Если это стандартная вещь игры (лифчик, трусы и т.д.), мы её НЕ трогаем
                            bool isStandard = (slotType == SlotType.bra || slotType == SlotType.panties ||
                                               slotType == SlotType.stockings || slotType == SlotType.suspenders ||
                                               slotType == SlotType.heels);

                            if (!isStandard)
                            {
                                // 2. Проверяем, записан ли этот предмет в нашем файле lingerie_mapping.txt
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    slotTypeInt = customSlotId; // Перераспределяем в ваш точный слот!
                                }
                                else if (slotTypeInt < 100)
                                {
                                    // 3. Предмета нет в списке, и это чужой мод (ID меньше 100, например none=10 или gloves=9).
                                    // Пускай падает в дефолтную категорию Accessorie (100), как вы и просили!
                                    slotTypeInt = 100;
                                }
                            }

                            // Распределяем полученный ID по двум нашим физическим кнопкам на UI
                            if (MainPlugin.FilterMode == 1)
                            {
                                // КНОПКА MASKS: Собираем всё, что на голове (101-104)
                                if (slotTypeInt == 101 || slotTypeInt == 102 || slotTypeInt == 103 || slotTypeInt == 104 || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                // КНОПКА OTHER: Собираем всё, что на теле (100, 111, 112, 113)
                                if (slotTypeInt == 100 || slotTypeInt == 111 || slotTypeInt == 112 || slotTypeInt == 113)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                        }
                    }

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
                // Тихо говорим игре, что кнопка не нажата, и гасим ошибку
                __result = false;
                return null;
            }
            return null;
        }
    }

}

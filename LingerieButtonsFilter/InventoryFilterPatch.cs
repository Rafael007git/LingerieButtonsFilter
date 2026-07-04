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

                    foreach (Transform itemTransform in originalItemsBackup)
                    {
                        if (itemTransform == null) continue;

                        var itemComponent = itemTransform.GetComponent<Item>();
                        if (itemComponent != null)
                        {
                            int slotTypeInt = (int)itemComponent.slotType;
                            string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

                            bool isStandard = (itemComponent.slotType == SlotType.bra ||
                                               itemComponent.slotType == SlotType.panties ||
                                               itemComponent.slotType == SlotType.stockings ||
                                               itemComponent.slotType == SlotType.suspenders ||
                                               itemComponent.slotType == SlotType.heels);

                            if (!isStandard)
                            {
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    slotTypeInt = customSlotId;

                                    // Возвращаем легальный тип none (10), чтобы игра не блокировала клики,
                                    // но запоминаем кастомную категорию для нашего кастомного менеджера конфликтов ниже!
                                    itemComponent.slotType = SlotType.none;
                                }
                                else if (slotTypeInt < 100)
                                {
                                    slotTypeInt = 100;
                                }
                            }

                            if (MainPlugin.FilterMode == 1)
                            {
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                if (slotTypeInt == 100 || (slotTypeInt >= 111 && slotTypeInt <= 113))
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
                if (stateField == null) return;

                if ((int)stateField.GetValue(__instance) == -1)
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

    // ====================================================================
    // АВТОНОМНЫЙ ПАТЧ АНАТОМИЧЕСКИХ КОНФЛИКТОВ (УПРАВЛЕНИЕ НАДЕВАНИЕМ)
    // Перехватывает метод куклы персонажа в момент, когда на неё надевают ЛЮБУЮ вещь!
    // ====================================================================
    [HarmonyPatch(typeof(CharacterCustomization), "WearLingerie")] // Метод сидит в коде куклы
    public class CharacterCustomization_Wear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Transform itemTransform, CharacterCustomization __instance)
        {
            if (itemTransform == null || __instance == null) return;

            string newItemName = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

            // 1. Проверяем, записана ли НОВАЯ вещь в нашем Блокноте
            if (MainPlugin.ItemMappingTable.TryGetValue(newItemName, out int newCustomCategory))
            {
                // Нам нельзя трогать общую категорию Accessories (100) — пусть платья надеваются свободно
                if (newCustomCategory == 100) return;

                // 2. Бежим шпионить по всему инвентарю надетого белья игрока!
                if (Global.code?.playerLingerieStorage?.items?.items == null) return;

                Transform itemToTakeOff = null;

                foreach (Transform wornItem in Global.code.playerLingerieStorage.items.items)
                {
                    if (wornItem == null || wornItem == itemTransform) continue;

                    string wornItemName = wornItem.name.ToLower().Replace("(clone)", "").Trim();

                    // 3. Ищем, надета ли на героиню СТАРАЯ вещь из ТОЙ ЖЕ САМОЙ категории Блокнота?
                    if (MainPlugin.ItemMappingTable.TryGetValue(wornItemName, out int wornCustomCategory))
                    {
                        if (wornCustomCategory == newCustomCategory)
                        {
                            // Нашли! Например, мы надеваем пирсинг 'Barbell' (113), а на ней уже висит 'Heart' (113)
                            itemToTakeOff = wornItem;
                            break;
                        }
                    }
                }

                // 4. Насильно снимаем старую вещь этой же категории перед тем, как игра наденет новую!
                if (itemToTakeOff != null)
                {
                    try
                    {
                        // Вызываем встроенный игровой метод принудительного снятия предмета с куклы
                        __instance.TakeOffLingerie(itemToTakeOff);
                        Debug.Log($"[SWPT АНАТОМИЯ]: Автоматически снята старая вещь '{itemToTakeOff.name}', освобождая категорию для '{itemTransform.name}'!");
                    }
                    catch { }
                }
            }
        }
    }

    // ЖЕЛЕЗНЫЙ ХАНИНГ-ЩИТ: Навсегда спасает Player.log от спама
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

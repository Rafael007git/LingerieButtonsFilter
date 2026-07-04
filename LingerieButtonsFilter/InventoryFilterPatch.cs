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
                            // Сохраняем исходные данные для диагностики
                            SlotType originalType = itemComponent.slotType;
                            int slotTypeInt = (int)originalType;
                            string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();
                            string mappingResult = "ACCESSORIE (По умолчанию)";

                            bool isStandard = (originalType == SlotType.bra ||
                                               originalType == SlotType.panties ||
                                               originalType == SlotType.stockings ||
                                               originalType == SlotType.suspenders ||
                                               originalType == SlotType.heels);

                            if (!isStandard)
                            {
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    slotTypeInt = customSlotId;
                                    mappingResult = ((CustomSlotType)customSlotId).ToString();

                                    // Наш временный маппинг по слотам (18-24)
                                    switch (customSlotId)
                                    {
                                        case 101: itemComponent.slotType = (SlotType)18; break;
                                        case 102: itemComponent.slotType = (SlotType)19; break;
                                        case 103: itemComponent.slotType = (SlotType)20; break;
                                        case 104: itemComponent.slotType = (SlotType)21; break;
                                        case 111: itemComponent.slotType = (SlotType)22; break;
                                        case 112: itemComponent.slotType = (SlotType)23; break;
                                        case 113: itemComponent.slotType = (SlotType)24; break;
                                        default: break;
                                    }
                                }
                                else if (slotTypeInt < 100)
                                {
                                    slotTypeInt = 100;
                                }
                            }

                            // ДИДНОСТИЧЕСКИЙ ВЫВОД: Выводим подробный лог по КАЖДОМУ кастомному предмету,
                            // который сейчас обрабатывается интерфейсом!
                            if (!isStandard)
                            {
                                Debug.Log($"[SWPT ДИАГНОСТИКА]: Предмет='{itemTransform.name}' | " +
                                          $"Исходный тип в ассете={originalType} ({(int)originalType}) | " +
                                          $"Распознан в Блокноте как={mappingResult} | " +
                                          $"Итоговый слот для куклы={itemComponent.slotType} ({(int)itemComponent.slotType})");
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

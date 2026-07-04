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
                                    slotTypeInt = customSlotId; // Используем СТРОГО для фильтрации кнопок UI!
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
    // АВТОНОМНЫЙ ПАТЧ АНАТОМИЧЕСКИХ КОНФЛИКТОВ
    // Работает точечно в момент вызова метода Item.Use().
    // Самостоятельно находит и выключает старые вещи той же категории на кукле персонажа!
    // ====================================================================
    [HarmonyPatch(typeof(Item), "Use")]
    public class Item_Use_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, CharacterCustomization ___characterCustomization)
        {
            if (__instance == null) return;

            string newItemName = __instance.gameObject.name.ToLower().Replace("(clone)", "").Trim();

            // 1. Проверяем, записана ли НОВАЯ вещь, по которой кликнули, в нашем Блокноте
            if (MainPlugin.ItemMappingTable.TryGetValue(newItemName, out int newCategory))
            {
                // Для общей категории Accessories (100) конфликты не нужны — пусть платья надеваются свободно
                if (newCategory == 100) return;

                // Получаем доступ к кукле персонажа (если Harmony не смог прокинуть её через тройное подчеркивание, берем глобально)
                CharacterCustomization cc = ___characterCustomization ?? GameObject.FindObjectOfType<CharacterCustomization>();
                if (cc == null) return;

                // 2. Нам нужно найти на сцене уже надетые трехмерные объекты старых модов.
                // В Unity все надетые вещи спавнятся как дочерние объекты внутри скелета куклы.
                // Мы ищем их по именам, которые записаны в нашей таблице маппинга!
                foreach (var pair in MainPlugin.ItemMappingTable)
                {
                    // Ищем СТАРИНУЮ вещь, которая относится к ТОЙ ЖЕ САМОЙ категории, что и новая
                    if (pair.Value == newCategory && pair.Key != newItemName)
                    {
                        // Пытаемся найти трехмерный объект старой вещи на кукле персонажа
                        // Ищем и по чистому имени, и с приставкой (Clone), так как игра спавнит префабы
                        Transform oldWornVisual = cc.transform.Find($"Submesh/{pair.Key}") ??
                                                  cc.transform.Find($"Submesh/{pair.Key}(Clone)") ??
                                                  cc.transform.Find(pair.Key) ??
                                                  cc.transform.Find($"{pair.Key}(Clone)");

                        // Если старого объекта на сцене нет, пробуем глубокий поиск по всему скелету куклы
                        if (oldWornVisual == null)
                        {
                            foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                            {
                                string childName = child.name.ToLower().Replace("(clone)", "").Trim();
                                if (childName == pair.Key && child.gameObject.activeSelf)
                                {
                                    oldWornVisual = child;
                                    break;
                                }
                            }
                        }

                        // 3. Нашли старый надетый пирсинг или кляп! Насильно ВЫКЛЮЧАЕМ его трехмерную модель,
                        // имитируя чистое и мгновенное снятие предмета с куклы персонажа!
                        if (oldWornVisual != null && oldWornVisual.gameObject.activeSelf)
                        {
                            oldWornVisual.gameObject.SetActive(false);
                            Debug.Log($"[SWPT АНАТОМИЯ]: Хирургически снята старая модель '{oldWornVisual.name}' для освобождения категории {newCategory}!");
                        }
                    }
                }
            }
        }
    }

    // ЖЕЛЕЗНЫЙ ХАНИНГ-ЩИТ: Навсегда спасает Player.log от спама инпута
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

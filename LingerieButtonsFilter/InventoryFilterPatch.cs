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

                // Перехватываем рантайм при открытии инвентаря или переключении вкладок
                if (state == 1 && MainPlugin.FilterMode != 0 && originalItemsBackup == null)
                {
                    if (Global.code?.playerLingerieStorage?.items?.items == null) return;
                    List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;

                    originalItemsBackup = new List<Transform>(gameItemsList);
                    List<Transform> filteredItems = new List<Transform>();

                    // ----------------====================================================
                    // ШАГ 1: СКАНЕР КУКЛЫ И ПЕЧАТЬ ТАБЛИЦЫ СЛОТОВ (ПРЯМО ЗДЕСЬ!)
                    // ----------------====================================================
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    List<Transform> visualObjectsToDestroy = new List<Transform>();

                    if (cc != null)
                    {
                        Debug.Log("====================================================================");
                        Debug.Log("[SWPT АНАТОМИЯ]: Инвентарь обновлен! Сканируем 3D-модели на кукле...");

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;

                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            int matchedCategory = -1;
                            foreach (var pair in MainPlugin.ItemMappingTable)
                            {
                                if (cleanName.Contains(pair.Key))
                                {
                                    matchedCategory = pair.Value;
                                    break;
                                }
                            }

                            if (matchedCategory > 100)
                            {
                                if (virtualSlotsMap.ContainsKey(matchedCategory))
                                {
                                    string oldItemName = virtualSlotsMap[matchedCategory];
                                    Debug.LogWarning($" -> [КОНФЛИКТ СЛОТА {matchedCategory}]: Модель '{child.name}' наложилась на '{oldItemName}'!");
                                    if (!visualObjectsToDestroy.Contains(child)) visualObjectsToDestroy.Add(child);
                                }
                                else
                                {
                                    virtualSlotsMap.Add(matchedCategory, child.name);
                                }
                            }
                        }

                        // ПЕЧАТЬ ТАБЛИЦЫ
                        Debug.Log("--- ТЕКУЩЕЕ СОСТОЯНИЕ АНАТОМИЧЕСКИХ СЛОТОВ ПЕРСОНАЖА ---");
                        Debug.Log($" -> Слот 101 (Hats):       {(virtualSlotsMap.ContainsKey(101) ? virtualSlotsMap[101] : "[Свободен]")}");
                        Debug.Log($" -> Слот 102 (Eyes/Маски):  {(virtualSlotsMap.ContainsKey(102) ? virtualSlotsMap[102] : "[Свободен]")}");
                        Debug.Log($" -> Слот 103 (Mouth/Кляпы): {(virtualSlotsMap.ContainsKey(103) ? virtualSlotsMap[103] : "[Свободен]")}");
                        Debug.Log($" -> Слот 104 (Earrings):   {(virtualSlotsMap.ContainsKey(104) ? virtualSlotsMap[104] : "[Свободен]")}");
                        Debug.Log($" -> Слот 111 (Wrists):     {(virtualSlotsMap.ContainsKey(111) ? virtualSlotsMap[111] : "[Свободен]")}");
                        Debug.Log($" -> Слот 112 (Neck):       {(virtualSlotsMap.ContainsKey(112) ? virtualSlotsMap[112] : "[Свободен]")}");
                        Debug.Log($" -> Слот 113 (Nipples):    {(virtualSlotsMap.ContainsKey(113) ? virtualSlotsMap[113] : "[Свободен]")}");
                        Debug.Log("-------------------------------------------------------");

                        // ХИРУРГИЧЕСКОЕ ВЫТЕСНЕНИЕ СТАРЫХ МОДЕЛЕЙ С ТЕЛА
                        if (visualObjectsToDestroy.Count > 0)
                        {
                            foreach (Transform oldVisual in visualObjectsToDestroy)
                            {
                                if (oldVisual != null)
                                {
                                    Debug.Log($"[SWPT АНАТОМИЯ]: Уничтожаем лишнюю модель '{oldVisual.name}'...");
                                    oldVisual.gameObject.SetActive(false);
                                    GameObject.Destroy(oldVisual.gameObject);
                                }
                            }
                        }
                    }

                    // ----------------====================================================
                    // ШАГ 2: ЭТАЛОННАЯ ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI ПО ЛОКАЛЬНОЙ ТАБЛИЦЕ
                    // ----------------====================================================
                    foreach (Transform itemTransform in originalItemsBackup)
                    {
                        if (itemTransform == null) continue;

                        var itemComponent = itemTransform.GetComponent<Item>();
                        if (itemComponent != null)
                        {
                            int slotTypeInt = (int)itemComponent.slotType;
                            string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

                            // ЭТАП 1: Проверяем, защищено ли это белье стандартными флагами игры
                            bool isStandard = (itemComponent.slotType == SlotType.bra ||
                                               itemComponent.slotType == SlotType.panties ||
                                               itemComponent.slotType == SlotType.stockings ||
                                               itemComponent.slotType == SlotType.suspenders ||
                                               itemComponent.slotType == SlotType.heels);

                            // ЭТАП 2: Включаем текстовый маппинг только для кастомных аксессуаров мододелов!
                            int uiCategory = -1;
                            bool foundInMap = false;

                            foreach (var pair in localMappingTable)
                            {
                                if (itemNameLower.Contains(pair.Key))
                                {
                                    uiCategory = pair.Value;
                                    foundInMap = true;
                                    break;
                                }
                            }

                            if (foundInMap)
                            {
                                // Нашли точное совпадение в Блокноте (улетает в Masks или специфический Other)
                                slotTypeInt = uiCategory;
                            }
                            else if (!isStandard)
                            {
                                // Вещи НЕТ в блокноте, и это НЕ стандартная вещь игры (чужой неопознанный мод).
                                // Вот только ОНА имеет право упасть в общую категорию Accessories (100)!
                                slotTypeInt = 100;
                            }
                            // Если вещь стандартная (лифчик/трусы) и её нет в Блокноте - мы её НЕ трогаем.
                            // Она сохраняет свой родной slotTypeInt (11, 12, 13...) и НЕ падает в Others!

                            // --- СТРОГОЕ РАСПРЕДЕЛЕНИЕ ПО ФИЗИЧЕСКИМ КНОПКАМ UI ---
                            if (MainPlugin.FilterMode == 1)
                            {
                                // КНОПКА MASKS: Собирает только Hats (101), Eyes (102), Mouth (103), Earrings (104)
                                // И оставляет родные перчатки мода (7), если их нет в блокноте
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                // КНОПКА OTHER: Собирает СТРОГО Accessories (100), Wrists (111), Neck (112), Nipples (113)
                                // Стандартные лифчики (11) или трусики (12) сюда теперь физически НЕ ПРОЛЕЗУТ!
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
            catch (Exception ex) { Debug.LogError($"[SWPT Filter] Ошибка монолитного патча: {ex.Message}"); }
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
                    gameItemsList.Clear(); gameItemsList.AddRange(originalItemsBackup); originalItemsBackup = null;
                }
                catch (Exception ex) { Debug.LogError($"[SWPT Filter] Ошибка восстановления: {ex.Message}"); }
            }
        }
    }
}
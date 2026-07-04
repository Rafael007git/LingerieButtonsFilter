using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
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

                    // ----------------====================================================
                    // БРОНЕБОЙНАЯ АВТО-ЗАГРУЗКА БЛОКНОТА НА ЛЕТУ
                    // ----------------====================================================
                    Dictionary<string, int> localMappingTable = new Dictionary<string, int>();
                    try
                    {
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
                                    string itemName = parts[0].Trim().ToLower();
                                    string typeStr = parts[1].Trim().ToLower();

                                    int targetSlotId = 100;
                                    if (Enum.TryParse(typeStr, true, out CustomSlotType matchedType))
                                    {
                                        targetSlotId = (int)matchedType;
                                    }

                                    if (!localMappingTable.ContainsKey(itemName))
                                    {
                                        localMappingTable.Add(itemName, targetSlotId);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Debug.LogError($"[SWPT] Ошибка авто-чтения файла: {ex.Message}"); }

                    // ----------------====================================================
                    // ШАГ 1: ШПИОН СЦЕНЫ И СКАНЕР КУКЛЫ
                    // ----------------====================================================
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    List<Transform> visualObjectsToDestroy = new List<Transform>();

                    if (cc != null)
                    {
                        Debug.Log("====================================================================");
                        Debug.Log($"[SWPT АНАТОМИЯ]: Инвентарь обновлен! Найдена кукла: {cc.gameObject.name}. Сканируем 3D-модели...");

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;

                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            int matchedCategory = -1;
                            foreach (var pair in localMappingTable)
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

                        // ПЕЧАТЬ ТАБЛИЦЫ С ТОЧНЫМИ ЗНАЧЕНИЯМИ СТРОК
                        Debug.Log("--- ТЕКУЩЕЕ СОСТОЯНИЕ АНАТОМИЧЕСКИХ СЛОТОВ ПЕРСОНАЖА ---");
                        Debug.Log(" -> Слот 101 (Hats):       " + (virtualSlotsMap.ContainsKey(101) ? virtualSlotsMap[101] : "[Свободен]"));
                        Debug.Log(" -> Слот 102 (Eyes/Маски):  " + (virtualSlotsMap.ContainsKey(102) ? virtualSlotsMap[102] : "[Свободен]"));
                        Debug.Log(" -> Слот 103 (Mouth/Кляпы): " + (virtualSlotsMap.ContainsKey(103) ? virtualSlotsMap[103] : "[Свободен]"));
                        Debug.Log(" -> Слот 104 (Earrings):   " + (virtualSlotsMap.ContainsKey(104) ? virtualSlotsMap[104] : "[Свободен]"));
                        Debug.Log(" -> Слот 111 (Wrists):     " + (virtualSlotsMap.ContainsKey(111) ? virtualSlotsMap[111] : "[Свободен]"));
                        Debug.Log(" -> Слот 112 (Neck):       " + (virtualSlotsMap.ContainsKey(112) ? virtualSlotsMap[112] : "[Свободен]"));
                        Debug.Log(" -> Слот 113 (Nipples):    " + (virtualSlotsMap.ContainsKey(113) ? virtualSlotsMap[113] : "[Свободен]"));
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

                            bool isStandard = (itemComponent.slotType == SlotType.bra ||
                                               itemComponent.slotType == SlotType.panties ||
                                               itemComponent.slotType == SlotType.stockings ||
                                               itemComponent.slotType == SlotType.suspenders ||
                                               itemComponent.slotType == SlotType.heels);

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
                                slotTypeInt = uiCategory;
                            }
                            else if (!isStandard)
                            {
                                slotTypeInt = 100; // Только левые кастомные аксессуары улетают сюда
                            }

                            // Распределение по физическим кнопкам интерфейса
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
                    gameItemsList.Clear();
                    gameItemsList.AddRange(originalItemsBackup);
                    originalItemsBackup = null;
                }
                catch (Exception ex) { Debug.LogError($"[SWPT Filter] Ошибка восстановления: {ex.Message}"); }
            }
        }
    }

    // ====================================================================
    // ХИРУРГИЧЕСКИЙ ПЕРЕХВАТЧИК КЛИКА ДЛЯ МАСОК И ПЕРЧАТОК
    // В момент клика временно превращает маску в тип none (10),
    // полностью блокируя вытеснение перчаток из слота рук!
    // ====================================================================
    [HarmonyPatch(typeof(Item), "Use")]
    public class Item_Use_MaskFix_Patch
    {
        private static SlotType originalSavedType;
        private static bool wasModified = false;
        private static Item activeItemComponent = null;

        [HarmonyPrefix]
        public static void Prefix(Item __instance)
        {
            wasModified = false;
            activeItemComponent = null;

            if (__instance == null) return;

            string nameLower = __instance.gameObject.name.ToLower().Replace("(clone)", "").Trim();

            // Если в Блокноте эта вещь записана как Маска (102) или Кляп (103) -
            // мы на ОДНУ МИЛЛИСЕКУНДУ превращаем её в тип none (10) прямо перед экипировкой!
            // Игра посчитает её обычным аксессуаром и НЕ тронет кружевные перчатки!
            if (MainPlugin.ItemMappingTable.TryGetValue(nameLower, out int customSlotId))
            {
                if (customSlotId == 102 || customSlotId == 103)
                {
                    originalSavedType = __instance.slotType;
                    activeItemComponent = __instance;
                    wasModified = true;

                    __instance.slotType = SlotType.none; // Временная мутация ассета
                    Debug.Log($"[SWPT АНАТОМИЯ]: Маска/Кляп '{nameLower}' временно переведена в тип NONE для защиты перчаток!");
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Как только метод Use() завершился и маска заспавнилась на тело,
            // мгновенно возвращаем оригинальный тип, чтобы не ломать логику сохранений игры
            if (wasModified && activeItemComponent != null)
            {
                activeItemComponent.slotType = originalSavedType;
            }
        }
    }

}

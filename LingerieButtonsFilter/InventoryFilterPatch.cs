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

        // Предохранитель от зацикливания при автоснятии вещи
        private static bool isProcessingAutoUnequip = false;

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
            if (isProcessingAutoUnequip) return;

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
                                    string key = parts[0].Trim().ToLower();
                                    string val = parts[1].Trim().ToLower();

                                    int id = 100;
                                    if (Enum.TryParse(val, true, out CustomSlotType matchedType)) id = (int)matchedType;
                                    if (!localMappingTable.ContainsKey(key)) localMappingTable.Add(key, id);
                                }
                            }
                        }
                    }
                    catch { }

                    // ----------------====================================================
                    // ШАГ 1: СКАНЕР КУКЛЫ И ИНИЦИАЦИЯ ВИРТУАЛЬНОГО КЛИКА СНЯТИЯ
                    // ----------------====================================================
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    if (cc != null)
                    {
                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;

                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            int matchedCategory = -1;
                            foreach (var pair in localMappingTable)
                            {
                                if (cleanName.Contains(pair.Key)) { matchedCategory = pair.Value; break; }
                            }

                            if (matchedCategory > 100)
                            {
                                if (virtualSlotsMap.ContainsKey(matchedCategory))
                                {
                                    // ОБНАРУЖЕНО НАЛОЖЕНИЕ! На кукле горят две вещи одной категории!
                                    string oldItemName = virtualSlotsMap[matchedCategory];
                                    Debug.Log($"[SWPT АНАТОМИЯ]: Конфликт категории {matchedCategory}! Модель '{child.name}' наложилась на '{oldItemName}'");

                                    // БРОНЕБОЙНЫЙ ПОИСК СТАРЕЙ ВЕЩИ В СУНДУКЕ (Используем Бэкап списка и Contains!)
                                    foreach (Transform t in originalItemsBackup)
                                    {
                                        if (t == null) continue;
                                        string storageNameLower = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();
                                        string oldItemNameLower = oldItemName.ToLower().Replace("(clone)", "").Trim();

                                        if (oldItemNameLower.Contains(storageNameLower) || storageNameLower.Contains(oldItemNameLower))
                                        {
                                            itemToClickTakeOff = t.GetComponent<Item>();
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    virtualSlotsMap.Add(matchedCategory, child.name);
                                }
                            }
                        }

                        // ПЕЧАТЬ ТАБЛИЦЫ СЛОТОВ В КОНСОЛЬ В СЕКУНДУ КЛИКА
                        Debug.Log("--- ТЕКУЩЕЕ СОСТОЯНИЕ АНАТОМИЧЕСКИХ СЛОТОВ ПЕРСОНАЖА ---");
                        Debug.Log(" -> Слот 101 (Hats):       " + (virtualSlotsMap.ContainsKey(101) ? virtualSlotsMap[101] : "[Свободен]"));
                        Debug.Log(" -> Слот 102 (Eyes/Маски):  " + (virtualSlotsMap.ContainsKey(102) ? virtualSlotsMap[102] : "[Свободен]"));
                        Debug.Log(" -> Слот 103 (Mouth/Кляпы): " + (virtualSlotsMap.ContainsKey(103) ? virtualSlotsMap[103] : "[Свободен]"));
                        Debug.Log(" -> Слот 104 (Earrings):   " + (virtualSlotsMap.ContainsKey(104) ? virtualSlotsMap[104] : "[Свободен]"));
                        Debug.Log(" -> Слот 111 (Wrists):     " + (virtualSlotsMap.ContainsKey(111) ? virtualSlotsMap[111] : "[Свободен]"));
                        Debug.Log(" -> Слот 112 (Neck):       " + (virtualSlotsMap.ContainsKey(112) ? virtualSlotsMap[112] : "[Свободен]"));
                        Debug.Log(" -> Слот 113 (Nipples):    " + (virtualSlotsMap.ContainsKey(113) ? virtualSlotsMap[113] : "[Свободен]"));
                        Debug.Log("-------------------------------------------------------");

                        // ЗАПУСКАЕМ ВИРТУАЛЬНЫЙ КЛИК СНЯТИЯ СТАРОЙ ВЕЩИ
                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                Debug.Log($"[SWPT АНАТОМИЯ]: Запускаем симуляцию повторного клика для авто-снятия вещи '{itemToClickTakeOff.gameObject.name}'...");
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc); // Просим вещь снять саму себя по правилам игры!
                                isProcessingAutoUnequip = false;
                            }
                            catch (Exception ex)
                            {
                                isProcessingAutoUnequip = false;
                                Debug.LogError($"Ошибка виртуального клика: {ex.Message}");
                            }
                        }
                    }

                    // ----------------====================================================
                    // ШАГ 2: ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI И ЗАЩИТА ПЕРЧАТОК ЧЕРЕЗ MUTATION
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
                                if (itemNameLower.Contains(pair.Key)) { uiCategory = pair.Value; foundInMap = true; break; }
                            }

                            if (foundInMap)
                            {
                                slotTypeInt = uiCategory;

                                // МИКРО-МУТАЦИЯ МАСОК ПРЯМО В СПИСКЕ:
                                // Переписываем тип масок и кляпов на легальный none (10) в памяти!
                                // Теперь игра при клике на них в шкафу НИКОГДА больше не сотрет кружевные перчатки!
                                if (itemComponent.slotType == SlotType.lingeriegloves && (uiCategory == 102 || uiCategory == 103))
                                {
                                    itemComponent.slotType = SlotType.none;
                                }
                            }
                            else if (!isStandard)
                            {
                                slotTypeInt = 100;
                            }

                            if (MainPlugin.FilterMode == 1)
                            {
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7) filteredItems.Add(itemTransform);
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                if (slotTypeInt == 100 || (slotTypeInt >= 111 && slotTypeInt <= 113)) filteredItems.Add(itemTransform);
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
            if (!__result && originalItemsBackup != null) { RestoreImmediately(); return; }
            try
            {
                Type iteratorType = __instance.GetType();
                FieldInfo stateField = iteratorType.GetField("<>1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateField != null && (int)stateField.GetValue(__instance) == -1) RestoreImmediately();
            }
            catch { }
        }

        private static void RestoreImmediately()
        {
            if (originalItemsBackup != null && Global.code?.playerLingerieStorage?.items?.items != null)
            {
                List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;
                gameItemsList.Clear(); gameItemsList.AddRange(originalItemsBackup); originalItemsBackup = null;
            }
        }
    }
}

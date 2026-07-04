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
        private static bool isProcessingAutoUnequip = false;

        // Живые ссылки на компоненты Item для реставрации по всей игре
        public static Item lastActiveGlovesItem = null;
        public static Item lastActiveMaskItem = null;

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

        // ====================================================================
        // PREFIX ЦИТАДЕЛИ ИНТЕРФЕЙСА: СНИМОК СЦЕНЫ И ФИЛЬТРАЦИЯ UI
        // ====================================================================
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

                    // ЗАГРУЗКА БЛОКНОТА
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
                                    if (Enum.TryParse(val, true, out CustomSlotType mType)) id = (int)mType;
                                    if (!localMappingTable.ContainsKey(key)) localMappingTable.Add(key, id);
                                }
                            }
                        }
                    }
                    catch { }

                    // РАНТАЙМ-СНИМОК СЦЕНЫ
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    if (cc != null)
                    {
                        bool maskPresent = false;
                        bool glovesPresent = false;

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;
                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            if (cleanName.Contains("gloves") && !cleanName.Contains("blindfold") && !cleanName.Contains("gag")) glovesPresent = true;
                            if (cleanName.Contains("blindfold") || cleanName.Contains("gag") || cleanName.Contains("mask") || cleanName.Contains("collar")) maskPresent = true;

                            int matchedCategory = -1;
                            foreach (var pair in localMappingTable)
                            {
                                if (cleanName.Contains(pair.Key)) { matchedCategory = pair.Value; break; }
                            }

                            if (matchedCategory > 100)
                            {
                                if (virtualSlotsMap.ContainsKey(matchedCategory))
                                {
                                    string oldItemName = virtualSlotsMap[matchedCategory];
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
                                else virtualSlotsMap.Add(matchedCategory, child.name);
                            }
                        }

                        // ОБНОВЛЯЕМ ЖИВЫЕ ССЫЛКИ НА ПРЕДМЕТЫ ПРИ ЛЮБОМ СЛУЧАЕ
                        foreach (Transform t in originalItemsBackup)
                        {
                            if (t == null) continue;
                            var itemComponent = t.GetComponent<Item>();
                            if (itemComponent == null) continue;

                            string sName = t.name.ToLower().Replace("(clone)", "").Trim();

                            if (glovesPresent && sName.Contains("gloves") && !sName.Contains("blindfold") && !sName.Contains("gag")) lastActiveGlovesItem = itemComponent;
                            if (maskPresent && (sName.Contains("blindfold") || sName.Contains("gag") || sName.Contains("mask") || sName.Contains("collar"))) lastActiveMaskItem = itemComponent;
                        }

                        // ОНЛАЙН-РЕСТАВРАТОР ВНУТРИ ИНТЕРФЕЙСА
                        if (maskPresent && !glovesPresent && lastActiveGlovesItem != null)
                        {
                            Transform restored = Utility.Instantiate(lastActiveGlovesItem.transform);
                            cc.AddItem(restored, "misc1");
                            glovesPresent = true;
                        }
                        if (glovesPresent && !maskPresent && lastActiveMaskItem != null)
                        {
                            Transform restored = Utility.Instantiate(lastActiveMaskItem.transform);
                            cc.AddItem(restored, "misc2");
                            maskPresent = true;
                        }

                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc);
                                isProcessingAutoUnequip = false;
                            }
                            catch { isProcessingAutoUnequip = false; }
                        }
                    }

                    // ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI
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

                            if (foundInMap) slotTypeInt = uiCategory;
                            else if (!isStandard) slotTypeInt = 100;

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

    // ====================================================================
    // ГЛОБАЛЬНЫЙ ГАРДЕРОБНЫЙ ЦЕМЕНТ: ГАРАНТИРУЕТ СОВМЕСТИМОСТЬ В МИРЕ И ВЕЗДЕ
    // Насильно удерживает вторую вещь и рапортует о состоянии крючков misc1/misc2!
    // ====================================================================
    [HarmonyPatch(typeof(UIInventory), "RefreshEquipment")]
    public class UIInventory_GlobalCement_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                if (cc == null) return;

                bool maskPresent = false;
                bool glovesPresent = false;

                string misc1ItemName = "[Пусто]";
                string misc2ItemName = "[Пусто]";

                // Быстро сканируем скелет куклы после оригинальной очистки игры
                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || !child.gameObject.activeSelf) continue;
                    string nLower = child.name.ToLower();

                    if (nLower.Contains("blindfold") || nLower.Contains("gag") || nLower.Contains("mask") || nLower.Contains("collar")) maskPresent = true;
                    if (nLower.Contains("gloves") && !nLower.Contains("blindfold") && !nLower.Contains("gag")) glovesPresent = true;

                    // ОПЕРАЦИЯ ПРОЗРЕНИЕ: Ловим то, что игра или наш реставратор привязали к крючкам misc!
                    if (child.parent != null)
                    {
                        string parentName = child.parent.name.ToLower();
                        if (parentName == "misc1") misc1ItemName = child.name;
                        if (parentName == "misc2") misc2ItemName = child.name;
                    }
                }

                // ЦЕМЕНТ СИТУАЦИИ А: Маска горит, но перчатки стёрты — возвращаем их на misc1!
                if (maskPresent && !glovesPresent && InventoryFilterPatch.lastActiveGlovesItem != null)
                {
                    Transform restored = Utility.Instantiate(InventoryFilterPatch.lastActiveGlovesItem.transform);
                    cc.AddItem(restored, "misc1");
                    misc1ItemName = InventoryFilterPatch.lastActiveGlovesItem.gameObject.name;
                }

                // ЦЕМЕНТ СИТУАЦИИ Б: Перчатки горят, но маска стёрта — возвращаем её на misc2!
                if (glovesPresent && !maskPresent && InventoryFilterPatch.lastActiveMaskItem != null)
                {
                    Transform restored = Utility.Instantiate(InventoryFilterPatch.lastActiveMaskItem.transform);
                    cc.AddItem(restored, "misc2");
                    misc2ItemName = InventoryFilterPatch.lastActiveMaskItem.gameObject.name;
                }

                // РАПОРТ ДЛЯ СПОКОЙНОГО СНА СТАРШЕГО ИНЖЕНЕРА 🛡️💤
                Debug.Log("====================================================================");
                Debug.Log("[SWPT МЕНЕДЖЕР АНАТОМИИ]: Проверка физических вешалок куклы в мире:");
                Debug.Log($"   -> Крючок МISC1 (Резерв Перчаток): {misc1ItemName}");
                Debug.Log($"   -> Крючок МISC2 (Резерв Масок):    {misc2ItemName}");
                Debug.Log("====================================================================");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SWPT ЦЕМЕНТ КРИТ]: Ошибка проверки вешалок: {ex.Message}");
            }
        }
    }
}

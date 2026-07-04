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

                    // 1. АВТО-ЗАГРУЗКА БЛОКНОТА
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

                    // 2. УЛЬТИМАТИВНЫЙ 3D-РАДАР СЦЕНЫ (ВЫТЕСНЕНИЕ ПИРСИНГОВ)
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    // Сканируем строго ТРЕХМЕРНЫЕ МОДЕЛИ ОДЕЖДЫ на всей сцене! Никакого UI!
                    SkinnedMeshRenderer[] activeMeshes = GameObject.FindObjectsOfType<SkinnedMeshRenderer>();
                    foreach (var smr in activeMeshes)
                    {
                        if (smr == null || !smr.gameObject.activeInHierarchy) continue;

                        string cleanMeshName = smr.gameObject.name.ToLower().Replace("(clone)", "").Trim();

                        int matchedCategory = -1;
                        foreach (var pair in localMappingTable)
                        {
                            if (cleanMeshName.Contains(pair.Key)) { matchedCategory = pair.Value; break; }
                        }

                        if (matchedCategory > 100)
                        {
                            if (virtualSlotsMap.ContainsKey(matchedCategory))
                            {
                                string oldItemName = virtualSlotsMap[matchedCategory];
                                Debug.Log($"[SWPT АНАТОМИЯ]: Конфликт категории {matchedCategory}! Модель '{smr.gameObject.name}' наложилась на '{oldItemName}'");

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
                                virtualSlotsMap.Add(matchedCategory, smr.gameObject.name);
                            }
                        }
                    }

                    // ВЫЗЫВАЕМ СНЯТИЕ ПИРСИНГА
                    if (itemToClickTakeOff != null)
                    {
                        try
                        {
                            CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                            isProcessingAutoUnequip = true;
                            itemToClickTakeOff.Use(cc);
                            isProcessingAutoUnequip = false;
                        }
                        catch { isProcessingAutoUnequip = false; }
                    }

                    // 3. ЧИСТАЯ ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI
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
        public static void Postfix()
        {
            try
            {
                CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                if (cc == null) return;
                if (Global.code?.playerLingerieStorage?.items?.items == null) return;

                bool isMaskActive = false;
                bool isRealGlovesActive = false;

                // ЖИРНЫЙ РАЗДЕЛИТЕЛЬ ДЛЯ ГЛАЗ В КОНСОЛИ
                Debug.Log("====================================================================");
                Debug.Log("[SWPT СУПЕР-ДЕБАГ]: Начинаем точечный анализ скелета куклы...");

                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null) continue;

                    // 1. Проверяем статус активности объекта (включен/выключен)
                    bool isActive = child.gameObject.activeSelf;
                    string nameLower = child.name.ToLower();

                    // 2. ОТСЕКАЕМ UI И СКЕЛЕТНЫЙ МУСОР: ловим только ключевые слова одежды модов и перчаток!
                    bool isInteresting = nameLower.Contains("gloves") ||
                                          nameLower.Contains("blindfold") ||
                                          nameLower.Contains("gag") ||
                                          nameLower.Contains("mask") ||
                                          nameLower.Contains("piercing") ||
                                          nameLower.Contains("collar") ||
                                          nameLower.Contains("backup");

                    if (isInteresting)
                    {
                        // ВЫВОДИМ В КОНСОЛЬ ТОЛЬКО ИНТЕРЕСНЫЕ НАХОДКИ ЖИРНЫМ МАРКЕРОМ!
                        Debug.Log($"   [НАХОДКА В КОСТЯХ]: Имя='{child.name}' | Активен на сцене={isActive} | Родитель='{child.parent?.name ?? "Корень"}'");

                        // Если объект физически включен (активен) на теле — выставляем радар-флаги
                        if (isActive)
                        {
                            if (nameLower.Contains("blindfold") || nameLower.Contains("gag") || nameLower.Contains("mask"))
                            {
                                isMaskActive = true;
                                Debug.Log($"      ======> ФЛАГ: Зафиксирована АКТИВНАЯ МАСКА/КЛЯП: '{child.name}'");
                            }
                            // Считаем перчатками только то, что имеет "gloves" в имени, но НЕ является маской/кляпом
                            if (nameLower.Contains("gloves") && !nameLower.Contains("blindfold") && !nameLower.Contains("gag"))
                            {
                                isRealGlovesActive = true;
                                Debug.Log($"      ======> ФЛАГ: Зафиксированы АКТИВНЫЕ ПЕРЧАТКИ: '{child.name}'");
                            }
                        }
                    }
                }

                // ВЫВОДИМ ИТОГОВЫЙ СТАТУС КАДРА ОБНОВЛЕНИЯ
                Debug.Log($"[SWPT СУПЕР-ДЕБАГ]: ИТОГ КАДРА -> Маска на теле есть? = {isMaskActive} | Перчатки на теле есть? = {isRealGlovesActive}");

                // КРИТИЧЕСКИЙ МИГ РЕСТАВРАЦИИ
                if (isMaskActive && !isRealGlovesActive)
                {
                    Debug.Log("[SWPT СУПЕР-ДЕБАГ]: ТРИГГЕР СРАБОТАЛ! Маска горит, перчаток нет. Запускаем поиск в сундуке...");

                    foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                    {
                        if (t == null) continue;
                        string storageName = t.name.ToLower();

                        var itemComponent = t.GetComponent<Item>();
                        if (itemComponent != null && itemComponent.slotType == SlotType.lingeriegloves && !storageName.Contains("blindfold") && !storageName.Contains("gag"))
                        {
                            Debug.Log($"[SWPT СУПЕР-ДЕБАГ]: Спавним перчатки из сундука: '{t.name}' на резервный маркер...");
                            Transform restoredGloves = Utility.Instantiate(t);
                            cc.AddItem(restoredGloves, "lingerieGloves_backup");
                            break;
                        }
                    }
                }
                Debug.Log("====================================================================");
            }
            catch (Exception ex) { Debug.LogError($"[SWPT СУПЕР-ДЕБАГ КРИТ]: {ex.Message}"); }
        }
    }
}


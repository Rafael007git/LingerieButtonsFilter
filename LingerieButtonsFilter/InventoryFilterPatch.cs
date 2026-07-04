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

        // ИСПРАВЛЕНО ТОЧЕЧНО: теперь храним живые ссылки на компоненты из сундука!
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
        // PREFIX: ДВУХЭТАПНЫЙ ФИЛЬТРАТОР И РАНТАЙМ-СНИМОК СЦЕНЫ
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

                    // 1. АВТО-ЗАГРУЗКА БЛОКНОТА НА ЛЕТУ
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

                    // 2. РАНТАЙМ-СНИМОК И ТОТАЛЬНЫЙ ДЕБАГ КОСТЕЙ
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    if (cc != null)
                    {
                        bool maskPresent = false;
                        bool glovesPresent = false;

                        Debug.Log("====================================================================");
                        Debug.Log("[SWPT ДЕБАГ]: Цитадель GenerateIcons активна! Начинаем сканирование...");

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;
                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            // ШПИОН: Ищем только маски и перчатки на теле персонажа
                            if (cleanName.Contains("gloves") || cleanName.Contains("blindfold") || cleanName.Contains("gag") || cleanName.Contains("mask"))
                            {
                                Debug.Log($" -> [РАДАР ЗАСЁК ОБЪЕКТ]: Имя='{child.name}' | Кость='{child.parent?.name ?? "Корень"}'");
                            }

                            if (cleanName.Contains("gloves") && !cleanName.Contains("blindfold") && !cleanName.Contains("gag"))
                            {
                                // ТЕКСТОВОЕ ИМЯ БОЛЬШЕ НЕ ПИШЕМ СЮДА — ПРОСТО СТАВИМ ФЛАГ ОБНАРУЖЕНИЯ!
                                glovesPresent = true;
                            }
                            if (cleanName.Contains("blindfold") || cleanName.Contains("gag") || cleanName.Contains("mask"))
                            {
                                // ТЕКСТОВОЕ ИМЯ БОЛЬШЕ НЕ ПИШЕМ СЮДА — ПРОСТО СТАВИМ ФЛАГ ОБНАРУЖЕНИЯ!
                                maskPresent = true;
                            }

                            // Логика вытеснения пирсингов
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
                        } // <--- ЗДЕСЬ ОФИЦИАЛЬНО ЗАВЕРШИЛСЯ ЦИКЛ FOREACH ПО КОСТЯМ КУКЛЫ

                        // ====================================================================
                        // НАШ НОВЫЙ БРОНЕБОЙНЫЙ БЛОК: СНИМОК НА ОСНОВЕ НАЛИЧИЯ МЕШЕЙ НА СЦЕНЕ!
                        // Полностью игнорируем капризный метод IsWearing игры!
                        // ====================================================================
                        foreach (Transform t in originalItemsBackup)
                        {
                            if (t == null) continue;
                            var itemComponent = t.GetComponent<Item>();
                            if (itemComponent == null) continue;

                            string sName = t.name.ToLower().Replace("(clone)", "").Trim();

                            // Если наш радар засёк перчатки на кукле, и имя префаба из сундука совпадает — запоминаем этот Item!
                            if (glovesPresent && sName.Contains("gloves") && !sName.Contains("blindfold") && !sName.Contains("gag"))
                            {
                                lastActiveGlovesItem = itemComponent;
                            }
                            // Если радар засёк маску на кукле, и имя префаба из сундука совпадает — запоминаем этот Item!
                            if (maskPresent && (sName.Contains("blindfold") || sName.Contains("gag") || sName.Contains("mask") || sName.Contains("collar")))
                            {
                                lastActiveMaskItem = itemComponent;
                            }
                        }

                        // Вычисляем имена для красивого лога из живых ссылок компонентов Item
                        string currentMaskLogName = (lastActiveMaskItem != null) ? lastActiveMaskItem.gameObject.name : "Нет";
                        string currentGlovesLogName = (lastActiveGlovesItem != null) ? lastActiveGlovesItem.gameObject.name : "Нет";

                        // НАШ ОБНОВЛЕННЫЙ СЛЕПОК КАДРА (СОХРАНЯЕТ ВСЕ ЛОГИ И НЕ ПАДАЕТ!)
                        Debug.Log($"[SWPT ДЕБАГ]: СЛЕПОК КАДРА -> Маска={maskPresent} (Имя: '{currentMaskLogName}') | Перчатки={glovesPresent} (Имя: '{currentGlovesLogName}')");

                        // ----------------====================================================
                        // ОНЛАЙН-РЕСТАВРАТОР НА ОСНОВЕ ЖИВЫХ ССЫЛОК И С ОХРАНОЙ ВСЕХ ЛОГОВ!
                        // ----------------====================================================
                        // Ситуация А: Маска горит, но игра стёрла перчатки.
                        if (maskPresent && !glovesPresent && lastActiveGlovesItem != null)
                        {
                            Debug.Log($"[SWPT ДЕБАГ]: Замятие! Маска есть, перчатки стёрты. Насильно возвращаем '{lastActiveGlovesItem.gameObject.name}' на резервный маркер костей!");
                            Transform restored = Utility.Instantiate(lastActiveGlovesItem.transform);
                            cc.AddItem(restored, "lingerieGloves_backup");

                            // ЖЕЛЕЗНЫЙ ПРЕДОХРАНИТЕЛЬ: Фиксируем, что перчатки реставрированы, гася бесконечный цикл!
                            glovesPresent = true;
                        }

                        // Ситуация Б: Перчатки горят, но игра стёрла маску.
                        if (glovesPresent && !maskPresent && lastActiveMaskItem != null)
                        {
                            Debug.Log($"[SWPT ДЕБАГ]: Замятие! Перчатки есть, маска стёрта. Насильно возвращаем '{lastActiveMaskItem.gameObject.name}' на резервный маркер костей!");
                            Transform restored = Utility.Instantiate(lastActiveMaskItem.transform);
                            cc.AddItem(restored, "lingerieGloves_backup2");

                            // ЖЕЛЕЗНЫЙ ПРЕДОХРАНИТЕЛЬ: Фиксируем, что маска реставрирована, гася бесконечный цикл!
                            maskPresent = true;
                        }

                        // Вызываем автоснятие старого пирсинга (ОСТАЕТСЯ АБСОЛЮТНО БЕЗ ИЗМЕНЕНИЙ!)
                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                Debug.Log($"[SWPT ДЕБАГ]: Авто-вытеснение пирсинга! Кликаем по '{itemToClickTakeOff.gameObject.name}'");
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc);
                                isProcessingAutoUnequip = false;
                            }
                            catch { isProcessingAutoUnequip = false; }
                        }

                        Debug.Log("====================================================================");
                    } // <--- ЗАКРЫВАЕТСЯ ИФ КУКЛЫ И ВЕСЬ БЛОК ДЕБАГА

                    // 3. ДВУХЭТАПНАЯ UI-ФИЛЬТРАЦИЯ
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
}

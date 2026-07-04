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

                    // ----------------====================================================
                    // ДЕБАГ-ШАГ 1: ТОТАЛЬНЫЙ СКАНЕР КУКЛЫ И АВТОСНЯТИЕ
                    // ----------------====================================================
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    if (cc != null)
                    {
                        Debug.Log("====================================================================");
                        Debug.Log($"[SWPT ДЕБАГ UI]: GenerateIcons сработал! Сканируем куклу '{cc.gameObject.name}'...");

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;

                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            // ШПИОН: выводим вообще каждую активную кость или меш на кукле!
                            Debug.Log($"   -> [КУКЛА ОБЪЕКТ]: На кукле горит меш с именем: '{child.name}' (В нижнем регистре: '{cleanName}')");

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
                                    Debug.LogWarning($"   => [ОБНАРУЖЕН КОНФЛИКТ КАТЕГОРИИ {matchedCategory}]: '{child.name}' воюет с '{oldItemName}'!");

                                    foreach (Transform t in originalItemsBackup)
                                    {
                                        if (t == null) continue;
                                        string storageNameLower = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();
                                        string oldItemNameLower = oldItemName.ToLower().Replace("(clone)", "").Trim();

                                        if (oldItemNameLower.Contains(storageNameLower) || storageNameLower.Contains(oldItemNameLower))
                                        {
                                            itemToClickTakeOff = t.GetComponent<Item>();
                                            Debug.Log($"   => [КАНДИДАТ НА СНЯТИЕ]: Нашли старый предмет на складе: '{t.gameObject.name}'");
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    virtualSlotsMap.Add(matchedCategory, child.name);
                                    Debug.Log($"   -> [ФИКСАЦИЯ СЛОТА]: Слот {matchedCategory} успешно занят моделью '{child.name}'");
                                }
                            }
                        }

                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                Debug.Log($"[SWPT ДЕБАГ UI]: Инициируем виртуальный клик снятия для: '{itemToClickTakeOff.gameObject.name}'");
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc);
                                isProcessingAutoUnequip = false;
                                Debug.Log("[SWPT ДЕБАГ UI]: Виртуальный клик завершен.");
                            }
                            catch (Exception ex) { isProcessingAutoUnequip = false; Debug.LogError($"Ошибка клика: {ex.Message}"); }
                        }
                    }

                    // ШАГ 2: ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI
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
    // ТОТАЛЬНЫЙ ДЕБАГ-РЕСТАВРАТОР КРУЖЕВНЫХ ПЕРЧАТОК
    // Выворачивает наизнанку логику поиска мешей при каждом обновлении!
    // ====================================================================
    [HarmonyPatch(typeof(UIInventory), "RefreshEquipment")]
    public class UIInventory_Refresh_GlovesRestorer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                if (cc == null)
                {
                    Debug.LogError("[SWPT ДЕБАГ РЕСТАВРАТОР]: Кукла CharacterCustomization не найдена на сцене!");
                    return;
                }
                if (Global.code?.playerLingerieStorage?.items?.items == null)
                {
                    Debug.LogError("[SWPT ДЕБАГ РЕСТАВРАТОР]: Склад playerLingerieStorage пуст или null!");
                    return;
                }

                bool isMaskActive = false;
                bool isRealGlovesActive = false;

                Debug.Log("====================================================================");
                Debug.Log("[SWPT ДЕБАГ РЕСТАВРАТОР]: Запущен точечный скан костей для реставрации перчаток...");

                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || !child.gameObject.activeSelf) continue;
                    string nameLower = child.name.ToLower();

                    // ШПИОН: Печатаем абсолютно всё, что видит реставратор в костях куклы!
                    Debug.Log($"   -> [РЕСТАВРАТОР СКАНИРУЕТ OBJ]: '{child.name}'");

                    if (nameLower.Contains("blindfold") || nameLower.Contains("gag") || nameLower.Contains("mask"))
                    {
                        isMaskActive = true;
                        Debug.Log($"      ==> ХИТ! Найдена активная маска/кляп по ключевому слову: '{child.name}'");
                    }
                    if (nameLower.Contains("gloves") && !nameLower.Contains("blindfold") && !nameLower.Contains("gag"))
                    {
                        isRealGlovesActive = true;
                        Debug.Log($"      ==> ХИТ! Найдены активные перчатки по ключевому слову: '{child.name}'");
                    }
                }

                Debug.Log($"[SWPT ДЕБАГ РЕСТАВРАТОР]: Итоги сканирования скелета -> Маска активна? = {isMaskActive}, Перчатки активны? = {isRealGlovesActive}");

                // КРИТИЧЕСКИЙ МИГ: Игра стерла перчатки из-за надетой маски!
                if (isMaskActive && !isRealGlovesActive)
                {
                    Debug.Log("[SWPT ДЕБАГ РЕСТАВРАТОР]: Условия выполнены (Маска есть, перчаток нет). Прочесываем сундук в поисках перчаток...");

                    foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                    {
                        if (t == null) continue;
                        string storageName = t.name.ToLower();

                        var itemComponent = t.GetComponent<Item>();
                        if (itemComponent != null && itemComponent.slotType == SlotType.lingeriegloves && !storageName.Contains("blindfold") && !storageName.Contains("gag"))
                        {
                            Debug.Log($"[SWPT ДЕБАГ РЕСТАВРАТОР]: Нашли кандидата на реставрацию в сундуке: '{t.name}'. Вызываем спавн...");
                            Transform restoredGloves = Utility.Instantiate(t);

                            // Спавним на изолированный маркер, чтобы игра их больше не стирала
                            cc.AddItem(restoredGloves, "lingerieGloves_backup");
                            Debug.Log("[SWPT ДЕБАГ РЕСТАВРАТОР]: Метод AddItem для реставрации успешно выполнен!");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SWPT КРИТ ОШИБКА РЕСТАВРАТОРА]: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}


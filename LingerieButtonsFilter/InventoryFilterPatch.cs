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
                    // ШАГ 1: СКАНЕР КУКЛЫ И ИНИЦИАЦИЯ СНЯТИЯ ДЛЯ ПИРСИНГОВ / ОШЕЙНИКОВ
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
                                    string oldItemName = virtualSlotsMap[matchedCategory];
                                    Debug.Log($"[SWPT АНАТОМИЯ]: Наложение категории {matchedCategory}! '{child.name}' наложилась на '{oldItemName}'");

                                    // Ищем старый пирсинг на складе, чтобы симулировать его автоснятие повторным кликом
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

                        // ВЫЗЫВАЕМ АВТОСНЯТИЕ СТАРОГО ПИРСИНГА
                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc); // Старый пирсинг чисто снимает сам себя!
                                isProcessingAutoUnequip = false;
                            }
                            catch { isProcessingAutoUnequip = false; }
                        }
                    }

                    // ----------------====================================================
                    // ШАГ 2: ЧИСТАЯ ДВУХЭТАПНАЯ ФИЛЬТРАЦИЯ UI (БЕЗ ОПАСНЫХ МУТАЦИЙ ТИПОВ!)
                    // --------------------------------====================================
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
    // АВТОНОМНЫЙ РЕСТАВРАТОР КРУЖЕВНЫХ ПЕРЧАТОК
    // Срабатывает в самый последний кадр клика по шкафу.
    // Если на кукле горит маска, но исчезли перчатки — он мгновенно возвращает их на руки!
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
                if (cc == null || Global.code?.playerLingerieStorage?.items?.items == null) return;

                // Проверяем по скелету: горит ли сейчас на лице маска?
                bool isMaskActive = false;
                bool isRealGlovesActive = false;

                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || !child.gameObject.activeSelf) continue;
                    string nameLower = child.name.ToLower();

                    if (nameLower.Contains("blindfold") || nameLower.Contains("gag") || nameLower.Contains("mask")) isMaskActive = true;
                    if (nameLower.Contains("gloves") && !nameLower.Contains("blindfold") && !nameLower.Contains("gag")) isRealGlovesActive = true;
                }

                // КРИТИЧЕСКИЙ МИГ: Маска на лице горит, а перчатки стёрлись оригинальным кодом шкафа!
                if (isMaskActive && !isRealGlovesActive)
                {
                    // Сканируем сундук игрока, выуживаем оттуда оригинальные кружевные перчатки и спавним их обратно!
                    foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                    {
                        if (t == null) continue;
                        string storageName = t.name.ToLower();

                        // Ищем оригинальный предмет перчаток (которого нет в Блокноте маппинга)
                        var itemComponent = t.GetComponent<Item>();
                        if (itemComponent != null && itemComponent.slotType == SlotType.lingeriegloves && !storageName.Contains("blindfold") && !storageName.Contains("gag"))
                        {
                            Debug.Log($"[SWPT АНАТОМИЯ]: Обнаружено замятие перчаток маской! Авто-реставрация перчаток '{t.name}' на руки куклы...");
                            Transform restoredGloves = Utility.Instantiate(t);

                            // Спавним их на альтернативный виртуальный маркер костей, чтобы игра их больше никогда не стёрла!
                            cc.AddItem(restoredGloves, "lingerieGloves_backup");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[SWPT АНАТОМИЯ КРИТ]: Ошибка авто-реставратора перчаток: {ex.Message}"); }
        }
    }
}

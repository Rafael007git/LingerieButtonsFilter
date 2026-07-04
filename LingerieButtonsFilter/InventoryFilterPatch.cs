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

                                    // --- РАНТАЙМ-МУТАЦИЯ ФАЛЬШИВЫХ ПЕРЧАТОК ---
                                    // Если автор мода зашил маске или кляпу (Eyes/Mouth) тип lingeriegloves,
                                    // мы принудительно переписываем его в ассете на чистый none (10).
                                    // Вещь навсегда теряет связь со слотом рук и больше НИКОГДА не снимет перчатки!
                                    if (itemComponent.slotType == SlotType.lingeriegloves && (customSlotId == 102 || customSlotId == 103))
                                    {
                                        itemComponent.slotType = SlotType.none;
                                    }
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
    // ДИАГНОСТИЧЕСКИЙ АНАТОМИЧЕСКИЙ ДИСПЕТЧЕР
    // Выводит в консоль BepInEx каждый чих рантайма при попытке надеть вещь!
    // ====================================================================
    [HarmonyPatch(typeof(Item), "Use")]
    public class Item_Use_Patch
    {
        private static bool isProcessingCustomEquip = false;

        [HarmonyPrefix]
        public static bool Prefix(Item __instance)
        {
            if (__instance == null) return true;

            // Логируем ЛЮБОЙ факт вызова метода Use, чтобы проверить, реагирует ли игра на клики!
            Debug.Log($"[SWPT ДЕБАГ]: Вызван Item.Use() для предмета '{__instance.gameObject.name}'. Предохранитель рекурсии={isProcessingCustomEquip}");

            if (isProcessingCustomEquip) return true;

            try
            {
                string newItemName = __instance.gameObject.name.ToLower().Replace("(clone)", "").Trim();

                // 1. Проверяем, занесен ли кликнутый предмет в наш Блокнот
                if (MainPlugin.ItemMappingTable.TryGetValue(newItemName, out int newCategory))
                {
                    Debug.Log($" -> [SWPT ДЕБАГ]: Предмет '{__instance.gameObject.name}' найден в Блокноте! Категория = {newCategory}");

                    if (newCategory == 100)
                    {
                        Debug.Log(" -> [SWPT ДЕБАГ]: Это общие Accessories (100). Пропускаем авто-снятие.");
                        return true;
                    }

                    // 2. Ищем куклу персонажа
                    CharacterCustomization cc = null;
                    if (Global.code?.uiCloset?.curcustomization != null)
                    {
                        cc = Global.code.uiCloset.curcustomization;
                        Debug.Log($" -> [SWPT ДЕБАГ]: Кукла успешно найдена через uiCloset! Имя объекта куклы: {cc.gameObject.name}");
                    }
                    if (cc == null)
                    {
                        cc = GameObject.FindObjectOfType<CharacterCustomization>();
                        if (cc != null) Debug.Log($" -> [SWPT ДЕБАГ]: Кукла найдена через FindObjectOfType! Имя: {cc.gameObject.name}");
                    }

                    if (cc == null)
                    {
                        Debug.LogError(" -> [SWPT ОШИБКА]: Кукла CharacterCustomization ВООБЩЕ НЕ НАЙДЕНА на сцене!");
                        return true;
                    }

                    // 3. Проверяем, надета ли эта вещь прямо сейчас
                    bool isNewItemAlreadyWorn = false;
                    foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name.ToLower().Replace("(clone)", "").Trim() == newItemName && child.gameObject.activeSelf)
                        {
                            isNewItemAlreadyWorn = true;
                            break;
                        }
                    }

                    Debug.Log($" -> [SWPT ДЕБАГ]: Статус новой вещи '{__instance.gameObject.name}' на кукле: Уже надета? = {isNewItemAlreadyWorn}");
                    if (isNewItemAlreadyWorn) return true; // Игрок снимает вещь вручную

                    // 4. Ищем старую вещь этой же категории на кукле
                    if (Global.code?.playerLingerieStorage?.items?.items == null)
                    {
                        Debug.LogWarning(" -> [SWPT ДЕБАГ]: playerLingerieStorage.items.items равен null!");
                        return true;
                    }

                    Debug.Log($" -> [SWPT ДЕБАГ]: Начинаем сканировать сундук (всего вещей: {Global.code.playerLingerieStorage.items.items.Count}) в поисках старой вещи категории {newCategory}...");
                    Item itemToTakeOff = null;

                    foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                    {
                        if (t == null) continue;
                        string checkedName = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();
                        if (checkedName == newItemName) continue;

                        if (MainPlugin.ItemMappingTable.TryGetValue(checkedName, out int wornCategory))
                        {
                            if (wornCategory == newCategory)
                            {
                                // Нашли потенциального кандидата на складе, проверяем, горит ли его моделька на кукле?
                                bool isVisualActive = false;
                                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                                {
                                    if (child.name.ToLower().Replace("(clone)", "").Trim() == checkedName && child.gameObject.activeSelf)
                                    {
                                        isVisualActive = true;
                                        itemToTakeOff = t.GetComponent<Item>();
                                        break;
                                    }
                                }
                                Debug.Log($"   -> Найдена вещь той же категории: '{t.gameObject.name}'. Активна на кукле? = {isVisualActive}");
                            }
                        }
                        if (itemToTakeOff != null) break;
                    }

                    // 5. Инициируем авто-снятие
                    if (itemToTakeOff != null)
                    {
                        Debug.Log($"[SWPT АНАТОМИЯ]: Найдена старая надетая вещь '{itemToTakeOff.gameObject.name}' в категории {newCategory}. Запускаем виртуальный клик снятия...");

                        isProcessingCustomEquip = true;
                        itemToTakeOff.Use(cc);
                        isProcessingCustomEquip = false;

                        Debug.Log("[SWPT АНАТОМИЯ]: Виртуальный клик снятия успешно завершен!");
                    }
                    else
                    {
                        Debug.Log($" -> [SWPT ДЕБАГ]: В категории {newCategory} на кукле сейчас ничего не надето. Чистая установка!");
                    }
                }
                else
                {
                    Debug.Log($" -> [SWPT ДЕБАГ]: Предмет '{__instance.gameObject.name}' отсутствует в Блокноте маппинга. Игнорируем.");
                }
            }
            catch (Exception ex)
            {
                isProcessingCustomEquip = false;
                Debug.LogError($"[SWPT КРИТ ОШИБКА] Сбой в дебаг-патче: {ex.Message}\n{ex.StackTrace}");
            }

            return true;
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
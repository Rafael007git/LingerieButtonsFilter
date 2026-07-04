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
    // ВИРТУАЛЬНЫЙ АНАТОМИЧЕСКИЙ ДИСПЕТЧЕР (КЛИК СЦЕНАРИЙ "С")
    // Перехватывает точечный метод Item.Use() в момент клика по предмету.
    // Находит старую надеваную вещь той же категории и заставляет ее СНЯТЬ СЕБЯ САМУ повторным вызовом Use()!
    // ====================================================================
    [HarmonyPatch(typeof(Item), "Use")]
    public class Item_Use_Patch
    {
        // ПЕРЕНЕСЛИ СЮДА: Теперь переменная видна внутри этого контекста!
        private static bool isProcessingCustomEquip = false;

        [HarmonyPrefix]
        public static bool Prefix(Item __instance, CharacterCustomization _customization)
        {
            // Если этот вызов инициирован нашим же модом для снятия старой вещи — 
            // мы просто пропускаем его, позволяя игре честно снять предмет!
            if (isProcessingCustomEquip) return true;

            if (__instance == null) return true;

            string newItemName = __instance.gameObject.name.ToLower().Replace("(clone)", "").Trim();

            // 1. Проверяем, занесен ли КЛИКНУТЫЙ предмет в наш Блокнот маппинга
            if (MainPlugin.ItemMappingTable.TryGetValue(newItemName, out int newCategory))
            {
                // Accessories (100) пропускаем — пусть платья и портупеи надеваются свободно
                if (newCategory == 100) return true;

                // Проверяем, надета ли вещь СЕЙЧАС (В игре надетые вещи проверяются по наличию их 3D-модели на кукле,
                // либо по внутренним галочкам. Но самый надежный способ — спросить у менеджера шкафа/персонажа, 
                // либо проверить, включен ли визуальный объект на кукле.
                // Чтобы не гадать, мы просто смотрим: если игрок кликнул по УЖЕ НАДЕТОЙ вещи — он хочет её снять.
                // В таком случае мы ничего не вытесняем и отдаем управление игре!
                CharacterCustomization cc = _customization ?? GameObject.FindObjectOfType<CharacterCustomization>();

                // Проверяем глубоким поиском, горит ли уже моделька этой НОВОЙ вещи на персонаже?
                bool isNewItemAlreadyWorn = false;
                if (cc != null)
                {
                    foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name.ToLower().Replace("(clone)", "").Trim() == newItemName && child.gameObject.activeSelf)
                        {
                            isNewItemAlreadyWorn = true;
                            break;
                        }
                    }
                }

                if (isNewItemAlreadyWorn) return true; // Игрок кликнул по надетому пирсингу -> игра его снимет сама

                // 2. ИГРОК НАДЕВАЕТ НОВУЮ ВЕЩЬ! Ищем в сундуке старую вещь этой же анатомической категории
                if (Global.code?.playerLingerieStorage?.items?.items == null || cc == null) return true;

                Item itemToTakeOff = null;

                foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                {
                    if (t == null || t.gameObject.name.ToLower().Replace("(clone)", "").Trim() == newItemName) continue;

                    string checkedName = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();

                    // Проверяем, относится ли эта вещь со склада к нашей категории?
                    if (MainPlugin.ItemMappingTable.TryGetValue(checkedName, out int wornCategory))
                    {
                        if (wornCategory == newCategory)
                        {
                            // Нашли вещь из этой же категории в сундуке. Проверяем, горит ли её моделька на теле девушки?
                            foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                            {
                                if (child.name.ToLower().Replace("(clone)", "").Trim() == checkedName && child.gameObject.activeSelf)
                                {
                                    itemToTakeOff = t.GetComponent<Item>();
                                    break;
                                }
                            }
                        }
                    }
                    if (itemToTakeOff != null) break;
                }

                // 3. СИМУЛЯЦИЯ ПОВТОРНОГО КЛИКА: Если нашли старую надетую вещь, заставляем её СНЯТЬ СЕБЯ!
                if (itemToTakeOff != null)
                {
                    try
                    {
                        Debug.Log($"[SWPT АНАТОМИЯ]: Категория {newCategory} занята предметом '{itemToTakeOff.gameObject.name}'. Инициируем виртуальный повторный клик для авто-снятия...");

                        isProcessingCustomEquip = true;

                        // Вызываем родной метод Use() старой вещи! Игра сама идеально уберет графику,
                        // погасит галочку и обновит списки инвентаря без единого бага.
                        itemToTakeOff.Use(cc);

                        isProcessingCustomEquip = false;
                    }
                    catch (Exception ex)
                    {
                        isProcessingCustomEquip = false;
                        Debug.LogError($" Ошибка симуляции снятия: {ex.Message}");
                    }
                }
            }

            return true; // Возвращаем true, давая игре штатно надеть наш новый предмет на пустое место!
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
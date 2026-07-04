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
                                // Ищем предмет в нашей текстовой базе данных Lingerie_Item_Mapping.txt
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    slotTypeInt = customSlotId;

                                    // ====================================================================
                                    // УЛЬТИМАТИВНЫЙ АНАТОМИЧЕСКИЙ МАКРО-МАППИНГ:
                                    // Мы изолируем ТОЛЬКО специфические категории, выдавая им жесткие скрытые слоты,
                                    // чтобы они гарантированно не воевали с перчатками и платьями.
                                    // А категорию Accessories (100) мы ВООБЩЕ НЕ ТРОГАЕМ — игра сама отдаст ей 
                                    // ВСЕ оставшиеся свободные слоты куклы misc1-misc8!
                                    // ====================================================================

                                    switch (customSlotId)
                                    {
                                        case 101: // Hats -> Отправляем в выделенный слот misc2 (18)
                                            itemComponent.slotType = (SlotType)18; break;
                                        case 102: // Eyes (Маски) -> Отправляем в выделенный слот misc3 (19) (Спасаем перчатки!)
                                            itemComponent.slotType = (SlotType)19; break;
                                        case 103: // Mouth (Кляпы) -> Отправляем в выделенный слот misc4 (20)
                                            itemComponent.slotType = (SlotType)20; break;
                                        case 104: // Earrings -> Отправляем в выделенный слот misc5 (21)
                                            itemComponent.slotType = (SlotType)21; break;
                                        case 111: // Wrists -> Отправляем в выделенный слот misc6 (22)
                                            itemComponent.slotType = (SlotType)22; break;
                                        case 112: // Neck (Ошейники) -> Отправляем в выделенный слот misc7 (23)
                                            itemComponent.slotType = (SlotType)23; break;
                                        case 113: // Nipples -> Отправляем в выделенный слот misc8 (24)
                                            itemComponent.slotType = (SlotType)24; break;

                                        case 100: // Accessories -> Специально пропускаем! 
                                        default:
                                            // Ничего не меняем в компоненте игры. Вещь сохраняет свой тип none/misc,
                                            // и движок игры сам красиво распределит её по оставшемуся пулу слотов!
                                            break;
                                    }
                                }
                                else if (slotTypeInt < 100)
                                {
                                    // Если вещи нет в блокноте, она по умолчанию считается общим аксессуаром.
                                    // Мы просто переводим её маркер для UI-кнопки в 100, но код игры НЕ трогаем!
                                    slotTypeInt = 100;
                                }
                            }


                            // --- РАСПРЕДЕЛЕНИЕ ПО ФИЗИЧЕСКИМ КНОПКАМ ИНТЕРФЕЙСА ---
                            if (MainPlugin.FilterMode == 1)
                            {
                                // КНОПКА MASKS: Собирает Hats (101), Eyes (102), Mouth (103), Earrings (104)
                                // И оставляем родной тип 7 (перчатки), если вы его не переназначили
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                // КНОПКА OTHER: Собирает Accessorie (100), Wrists (111), Neck (112), Nipples (113)
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
                if (stateField != null && (int)stateField.GetValue(__instance) == -1)
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

    // ЖЕЛЕЗНЫЙ ХАНИНГ-ЩИТ: Навсегда спасает Player.log и процессор от багов игры с инпутом
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

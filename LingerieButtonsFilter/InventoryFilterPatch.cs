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

                // Перехватываем рантайм на первом шаге генерации иконок
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
                            string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

                            // --- ЖЕЛЕЗНАЯ ЛОГИКА ТАБЛИЦЫ СООТВЕТСТВИЙ ---
                            // 1. Стандартное белье игры (лифчики, трусики и т.д.) защищаем от любых изменений
                            bool isStandard = (itemComponent.slotType == SlotType.bra ||
                                               itemComponent.slotType == SlotType.panties ||
                                               itemComponent.slotType == SlotType.stockings ||
                                               itemComponent.slotType == SlotType.suspenders ||
                                               itemComponent.slotType == SlotType.heels);

                            if (!isStandard)
                            {
                                // 2. Ищем предмет в нашей текстовой базе данных Lingerie_Item_Mapping.txt
                                if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
                                {
                                    // НАСИЛЬНО ПЕРЕЗАПИСЫВАЕМ ТИП В КОМПОНЕНТЕ ИГРЫ!
                                    // Теперь игра физически запомнит, что этот предмет имеет тип 101-113, 
                                    // и не будет конфликтовать при надевании!
                                    itemComponent.slotType = (SlotType)customSlotId;
                                }
                                else if ((int)itemComponent.slotType < 100)
                                {
                                    // 3. Если предмета нет в списке и это чужой старый неопознанный мод,
                                    // принудительно делаем его Accessorie (100) на уровне компонента!
                                    itemComponent.slotType = (SlotType)100;
                                }
                            }

                            // Считываем уже обновленный, железно прописанный тип для фильтрации кнопок
                            int finalSlotId = (int)itemComponent.slotType;

                            // --- РАСПРЕДЕЛЕНИЕ ПО ФИЗИЧЕСКИМ КНОПКАМ ИНТЕРФЕЙСА ---
                            if (MainPlugin.FilterMode == 1)
                            {
                                // КНОПКА MASKS: Собирает всё, что на голове (ID 101 — Hats, 102 — Eyes, 103 — Mouth, 104 — Earrings)
                                // И родной тип 7 (lingeriegloves), если вы его вручную не переписали на тело
                                if ((finalSlotId >= 101 && finalSlotId <= 104) || finalSlotId == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2)
                            {
                                // КНОПКА OTHER: Собирает всё, что на теле (ID 100 — Accessorie, 111 — Wrists, 112 — Neck, 113 — Nipples)
                                if (finalSlotId == 100 || (finalSlotId >= 111 && finalSlotId <= 113))
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                        }
                    }

                    // Подменяем список игры на наш кастомный отфильтрованный набор
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

        // УЛЬТИМАТИВНЫЙ ХИРУРГИЧЕСКИЙ ПАТЧ НА КЛИК: 
        // Ловит момент, когда игрок кликает по вещи в инвентаре, чтобы надеть её!
        [HarmonyPatch(typeof(UIInventory), "ClickLingerie")] // Стандартное имя метода в SWPT
        [HarmonyPrefix]
        public static void ClickLingeriePrefix(Transform itemTransform)
        {
            if (itemTransform == null) return;
            var itemComponent = itemTransform.GetComponent<Item>();
            if (itemComponent == null) return;

            string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

            // Если эта маска записана в нашем Блокноте как Eyes или Mouth — 
            // мы временно ПЕРЕЗАПИСЫВАЕМ тип прямо перед тем, как игра начнет её надевать!
            if (MainPlugin.ItemMappingTable.TryGetValue(itemNameLower, out int customSlotId))
            {
                itemComponent.slotType = (SlotType)customSlotId;
            }
        }

        [HarmonyPatch(typeof(UIInventory), "ClickLingerie")]
        [HarmonyPostfix]
        public static void ClickLingeriePostfix(Transform itemTransform)
        {
            // Сразу после того, как игра надела предмет, мы возвращаем его оригинальный тип,
            // чтобы не сломать стандартную систему хранения и сброса вещей игры!
            if (itemTransform == null) return;
            var itemComponent = itemTransform.GetComponent<Item>();
            if (itemComponent != null)
            {
                // Если вещь была маской (7), но мы её временно меняли — возвращаем её законный тип 7
                string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();
                if (MainPlugin.ItemMappingTable.ContainsKey(itemNameLower))
                {
                    // Если это была маска, возвращаем ей тип 7 (Lingeriegloves), 
                    // чтобы при снятии она знала, куда возвращаться.
                    if (itemNameLower.Contains("mask") || itemNameLower.Contains("gag"))
                    {
                        itemComponent.slotType = SlotType.lingeriegloves;
                    }
                    else
                    {
                        itemComponent.slotType = SlotType.none;
                    }
                }
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

using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    [HarmonyPatch(typeof(CharacterCustomization), "RefreshEquipment")]
    public class AnatomyManagerPatch
    {
        private static bool isProcessingUnequip = false;

        [HarmonyPrefix]
        public static void Prefix(CharacterCustomization __instance)
        {
            if (isProcessingUnequip) return;
            if (__instance == null || Global.code?.playerLingerieStorage?.items?.items == null) return;

            try
            {
                Debug.Log("====================================================================");
                Debug.Log("[SWPT АНАТОМИЯ]: Запущено обновление куклы! Сканируем виртуальные слоты...");

                Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                List<Item> itemsToDeactivate = new List<Item>();

                // Шаг 1: Сканируем куклу и ищем на ней активные 3D-модели предметов модов
                foreach (Transform child in __instance.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.gameObject.activeSelf) continue;

                    string cleanChildName = child.name.ToLower().Replace("(clone)", "").Trim();

                    if (MainPlugin.ItemMappingTable.TryGetValue(cleanChildName, out int customCategoryId))
                    {
                        if (customCategoryId == 100) continue; // Accessories пропускаем

                        if (virtualSlotsMap.ContainsKey(customCategoryId))
                        {
                            // Обнаружено наложение двух вещей в один виртуальный слот!
                            string oldItemName = virtualSlotsMap[customCategoryId];
                            Debug.LogWarning($" -> [ОБНАРУЖЕН КОНФЛИКТ СЛОТА {customCategoryId}]: Предмет '{child.name}' пытается занять слот, где уже надет '{oldItemName}'!");

                            foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                            {
                                if (t == null) continue;
                                string storageItemName = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();

                                if (storageItemName == oldItemName.ToLower().Replace("(clone)", "").Trim())
                                {
                                    Item oldItemComponent = t.GetComponent<Item>();
                                    if (oldItemComponent != null && !itemsToDeactivate.Contains(oldItemComponent))
                                    {
                                        itemsToDeactivate.Add(oldItemComponent);
                                    }
                                }
                            }
                        }
                        else
                        {
                            virtualSlotsMap.Add(customCategoryId, child.name);
                        }
                    }
                }

                // ШАГ 2: РАСПЕЧАТКА КАРТЫ СЛОТОВ В КОНСОЛЬ
                Debug.Log("--- ТЕКУЩЕЕ СОСТОЯНИЕ АНАТОМИЧЕСКИХ СЛОТОВ ПЕРСОНАЖА ---");
                Debug.Log($" -> Слот 101 (Hats):       {(virtualSlotsMap.ContainsKey(101) ? virtualSlotsMap[101] : "[Свободен]")}");
                Debug.Log($" -> Слот 102 (Eyes/Маски):  {(virtualSlotsMap.ContainsKey(102) ? virtualSlotsMap[102] : "[Свободен]")}");
                Debug.Log($" -> Слот 103 (Mouth/Кляпы): {(virtualSlotsMap.ContainsKey(103) ? virtualSlotsMap[103] : "[Свободен]")}");
                Debug.Log($" -> Слот 104 (Earrings):   {(virtualSlotsMap.ContainsKey(104) ? virtualSlotsMap[104] : "[Свободен]")}");
                Debug.Log($" -> Слот 111 (Wrists):     {(virtualSlotsMap.ContainsKey(111) ? virtualSlotsMap[111] : "[Свободен]")}");
                Debug.Log($" -> Слот 112 (Neck):       {(virtualSlotsMap.ContainsKey(112) ? virtualSlotsMap[112] : "[Свободен]")}");
                Debug.Log($" -> Слот 113 (Nipples):    {(virtualSlotsMap.ContainsKey(113) ? virtualSlotsMap[113] : "[Свободен]")}");
                Debug.Log("-------------------------------------------------------");

                // ШАГ 3: ИНИЦИИРУЕМ АВТО-СНЯТИЕ ПУТЕМ ПОВТОРНОГО КЛИКА (USE)
                if (itemsToDeactivate.Count > 0)
                {
                    isProcessingUnequip = true;
                    foreach (Item oldItem in itemsToDeactivate)
                    {
                        Debug.Log($"[SWPT АНАТОМИЯ]: Насильно вызываем Use() для снятия вытесненного предмета '{oldItem.gameObject.name}'...");
                        oldItem.Use(__instance);
                    }
                    isProcessingUnequip = false;

                    // Обновляем куклу еще раз, чтобы применить чистый результат
                    __instance.RefreshEquipment();
                }
            }
            catch (Exception ex)
            {
                isProcessingUnequip = false;
                Debug.LogError($"[SWPT АНАТОМИЯ КРИТ]: Ошибка сканирования слотов: {ex.Message}");
            }
        }
    }
}

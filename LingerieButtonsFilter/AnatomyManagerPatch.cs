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
                Debug.Log("[SWPT АНАТОМИЯ]: Метод RefreshEquipment вызван! Сканируем куклу персонажа...");

                // Наша рантайм-карта: Ключ - ID категории, Значение - Имя активного GameObject на теле
                Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                List<Transform> visualObjectsToDestroy = new List<Transform>();

                // ШАГ 1: Глубокий поиск по всему скелету куклы персонажа.
                // Ищем любые активные трехмерные модели, которые сейчас горят на теле героини!
                foreach (Transform child in __instance.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || !child.gameObject.activeSelf) continue;

                    string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                    // Проверяем вещь по нашему Блокноту маппинга (используем Contains для 100% надежности!)
                    int matchedCategory = -1;
                    foreach (var pair in MainPlugin.ItemMappingTable)
                    {
                        if (cleanName.Contains(pair.Key))
                        {
                            matchedCategory = pair.Value;
                            break;
                        }
                    }

                    // Если нашли кастомный предмет (и это не общие Accessories=100)
                    if (matchedCategory > 100)
                    {
                        if (virtualSlotsMap.ContainsKey(matchedCategory))
                        {
                            // КРИТИЧЕСКИЙ КОНФЛИКТ: Два предмета одной категории горят на теле одновременно!
                            string oldItemName = virtualSlotsMap[matchedCategory];
                            Debug.LogWarning($" -> [КОНФЛИКТ АНАТОМИИ]: Модель '{child.name}' наложилась на '{oldItemName}' в слоте {matchedCategory}!");

                            // Запоминаем старую модельку, чтобы принудительно удалить её со сцены
                            if (!visualObjectsToDestroy.Contains(child))
                            {
                                visualObjectsToDestroy.Add(child);
                            }
                        }
                        else
                        {
                            // Фиксируем, что этот анатомический слот сейчас занят этой вещью
                            virtualSlotsMap.Add(matchedCategory, child.name);
                        }
                    }
                }

                // ШАГ 2: ВЕЛИКАЯ ВЕЧЕРНЯЯ РАСПЕЧАТКА ВИРТУАЛЬНЫХ СЛОТОВ ГАРДЕРОБА
                Debug.Log("--- ТЕКУЩЕЕ СОСТОЯНИЕ АНАТОМИЧЕСКИХ СЛОТОВ ПЕРСОНАЖА ---");
                Debug.Log($" -> Слот 101 (Hats):       {(virtualSlotsMap.ContainsKey(101) ? virtualSlotsMap[101] : "[Свободен]")}");
                Debug.Log($" -> Слот 102 (Eyes/Маски):  {(virtualSlotsMap.ContainsKey(102) ? virtualSlotsMap[102] : "[Свободен]")}");
                Debug.Log($" -> Слот 103 (Mouth/Кляпы): {(virtualSlotsMap.ContainsKey(103) ? virtualSlotsMap[103] : "[Свободен]")}");
                Debug.Log($" -> Слот 104 (Earrings):   {(virtualSlotsMap.ContainsKey(104) ? virtualSlotsMap[104] : "[Свободен]")}");
                Debug.Log($" -> Слот 111 (Wrists):     {(virtualSlotsMap.ContainsKey(111) ? virtualSlotsMap[111] : "[Свободен]")}");
                Debug.Log($" -> Слот 112 (Neck):       {(virtualSlotsMap.ContainsKey(112) ? virtualSlotsMap[112] : "[Свободен]")}");
                Debug.Log($" -> Слот 113 (Nipples):    {(virtualSlotsMap.ContainsKey(113) ? virtualSlotsMap[113] : "[Свободен]")}");
                Debug.Log("-------------------------------------------------------");

                // ШАГ 3: АВТОНОМНОЕ ВЫТЕСНЕНИЕ СТАРЫХ МОДЕЛЕЙ С ТЕЛА
                if (visualObjectsToDestroy.Count > 0)
                {
                    isProcessingUnequip = true;
                    foreach (Transform oldVisual in visualObjectsToDestroy)
                    {
                        if (oldVisual != null && oldVisual.gameObject != null)
                        {
                            Debug.Log($"[SWPT АНАТОМИЯ]: Хирургически гасим старую 3D-модель '{oldVisual.name}', освобождая кость...");

                            // Гасим старый объект со сцены. Игра увидит, что место свободно, 
                            // и не будет конфликтовать со следующими вещами!
                            oldVisual.gameObject.SetActive(false);
                            GameObject.Destroy(oldVisual.gameObject);
                        }
                    }
                    isProcessingUnequip = false;

                    // Синхронизируем интерфейс инвентаря, чтобы убрать маркеры "Надето" у старых вещей
                    UIInventory ui = GameObject.FindObjectOfType<UIInventory>();
                    if (ui != null) ui.ButtonUnderwearGroup();
                }
            }
            catch (Exception ex)
            {
                isProcessingUnequip = false;
                Debug.LogError($"[SWPT АНАТОМИЯ КРИТ]: Ошибка сканирования: {ex.Message}");
            }
        }
    }
}

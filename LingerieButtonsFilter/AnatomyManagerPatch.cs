using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    [HarmonyPatch(typeof(CharacterCustomization), "RefreshEquipment")]
    public class AnatomyManagerPatch
    {
        // Храним оригинальный префаб перчаток/маски, чтобы игра не потеряла его
        private static Transform savedLingerieGlovesPrefab = null;
        private static string activeCustomMarker = "lingerieGloves";
        private static bool wasMarkerRedirected = false;

        [HarmonyPrefix]
        public static void Prefix(CharacterCustomization __instance)
        {
            savedLingerieGlovesPrefab = null;
            activeCustomMarker = "lingerieGloves";
            wasMarkerRedirected = false;

            if (__instance == null || __instance.lingerieGloves == null) return;

            // 1. Считываем имя префаба, который игра приготовилась заспавнить
            string prefabNameLower = __instance.lingerieGloves.name.ToLower().Replace("(clone)", "").Trim();

            // 2. Смотрим в наш Блокнот маппинга Lingerie_Item_Mapping.txt
            if (MainPlugin.ItemMappingTable.TryGetValue(prefabNameLower, out int customCategoryId))
            {
                // Accessories (100) оставляем на дефолтном маркере
                if (customCategoryId == 100) return;

                // Перенаправляем текстовый маркер куклы на уникальное имя анатомической категории!
                // Теперь ошейник Neck получит маркер "custom_112", а маска Eyes - "custom_102".
                // Метод AddItem больше НИКОГДА не удалит маску при надевании ошейника!
                activeCustomMarker = $"custom_{customCategoryId}";
                savedLingerieGlovesPrefab = __instance.lingerieGloves;
                wasMarkerRedirected = true;

                try
                {
                    // Симулируем оригинальный спавн игры, но со своим УНИКАЛЬНЫМ маркером слота!
                    Transform instantiatedModel = Utility.Instantiate(savedLingerieGlovesPrefab);

                    // Вызываем метод AddItem куклы напрямую, подсовывая наш кастомный маркер!
                    __instance.AddItem(instantiatedModel, activeCustomMarker);

                    // Гасим оригинальное выполнение спавна для этого кадра, временно обнуляя lingerieGloves,
                    // чтобы игра не продублировала этот же предмет на стандартный маркер перчаток!
                    __instance.lingerieGloves = null;

                    Debug.Log($"[SWPT АНАТОМИЯ]: Предмет '{prefabNameLower}' успешно изолирован на виртуальный маркер '{activeCustomMarker}'!");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SWPT АНАТОМИЯ КРИТ]: Ошибка подмены маркера: {ex.Message}");
                    if (savedLingerieGlovesPrefab != null) __instance.lingerieGloves = savedLingerieGlovesPrefab;
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(CharacterCustomization __instance)
        {
            // Как только метод RefreshEquipment завершился, мы возвращаем ссылку на префаб на место,
            // чтобы логика сундука и сохранения игры работала в штатном режиме
            if (wasMarkerRedirected && savedLingerieGlovesPrefab != null && __instance != null)
            {
                __instance.lingerieGloves = savedLingerieGlovesPrefab;
            }
        }
    }
}

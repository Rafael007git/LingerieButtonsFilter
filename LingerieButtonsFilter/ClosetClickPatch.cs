using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    // ====================================================================
    // АВТОМАТИЧЕСКИЙ РАНТАЙМ-РЕСТАВРАТОР ПЕРЧАТОК И ВЫТЕСНИТЕЛЬ ПИРСИНГОВ
    // Перехватывает фабричный метод Utility.Instantiate, который 100% сидит
    // в основной памяти игры и вызывается при любой экипировке в шкафу!
    // ====================================================================
    [HarmonyPatch(typeof(Utility), "Instantiate", new Type[] { typeof(Transform) })]
    public class ClosetClickPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Transform original)
        {
            if (original == null) return;

            try
            {
                string itemNameLower = original.name.ToLower().Replace("(clone)", "").Trim();

                // ----------------====================================================
                // ЧИТАЕМ БЛОКНОТ НА ЛЕТУ ПРЯМО В МОМЕНТ СПАВНА
                // ----------------====================================================
                Dictionary<string, int> clickMapTable = new Dictionary<string, int>();
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
                            if (!clickMapTable.ContainsKey(key)) clickMapTable.Add(key, id);
                        }
                    }
                }

                // Проверяем, занесен ли спавнящийся предмет в наш Блокнот
                if (clickMapTable.TryGetValue(itemNameLower, out int customCategoryId))
                {
                    CharacterCustomization curCustomization = Global.code?.uiInventory?.curCustomization;
                    if (curCustomization == null) return;

                    // ----------------====================================================
                    // СИТУАЦИЯ 1: СПАСЕНИЕ КРУЖЕВНЫХ ПЕРЧАТОК ОТ УНИЧТОЖЕНИЯ
                    // Если игра спавнит Маску (102) или Кляп (103), но перед этим стерла перчатки -
                    // мы перехватываем этот миг и ищем, лежали ли у игрока перчатки в сундуке?
                    // ----------------====================================================
                    if (customCategoryId == 102 || customCategoryId == 103)
                    {
                        // Проверяем: если на кукле СЕЙЧАС физически пропал объект перчаток (lingerieGloves равен null),
                        // мы сканируем сундук игрока, находим там настоящие кружевные перчатки и спавним их обратно!
                        if (curCustomization.lingerieGloves == null && Global.code?.playerLingerieStorage?.items?.items != null)
                        {
                            foreach (Transform t in Global.code.playerLingerieStorage.items.items)
                            {
                                if (t == null) continue;
                                string storageItemName = t.name.ToLower().Replace("(clone)", "").Trim();

                                // Если вещь на складе НЕ занесена в блокнот - значит это стандартные перчатки игры!
                                var itemComponent = t.GetComponent<Item>();
                                if (itemComponent != null && itemComponent.slotType == SlotType.lingeriegloves && !clickMapTable.ContainsKey(storageItemName))
                                {
                                    // Реставрируем перчатки обратно на руки героини!
                                    Debug.Log($"[SWPT АНАТОМИЯ]: Защита активирована! Реставрируем вытесненные перчатки '{t.name}' обратно на руки!");
                                    Transform restoredGloves = Utility.Instantiate(t);
                                    curCustomization.AddItem(restoredGloves, "lingerieGloves");
                                    break;
                                }
                            }
                        }
                    }

                    // ----------------====================================================
                    // СИТУАЦИЯ 2: ХИРУРГИЧЕСКОЕ ВЫТЕСНЕНИЕ ПИРСИНГОВ / ОШЕЙНИКОВ
                    // Находим и принудительно выключаем старую модель ЭТОЙ ЖЕ категории на кукле,
                    // гарантируя, что один пирсинг чисто снимет другой, а не спавнится кашей поверх!
                    // ----------------====================================================
                    if (customCategoryId != 100)
                    {
                        foreach (Transform child in curCustomization.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;
                            string childName = child.name.ToLower().Replace("(clone)", "").Trim();

                            if (clickMapTable.TryGetValue(childName, out int wornCategoryId))
                            {
                                // Если на теле горит старая вещь из ТОЙ ЖЕ категории (и это не тот же самый предмет)
                                if (wornCategoryId == customCategoryId && childName != itemNameLower)
                                {
                                    Debug.Log($"[SWPT АНАТОМИЯ]: Вытеснение! Насильно удаляем старую модель '{child.name}' из категории {customCategoryId}...");
                                    child.gameObject.SetActive(false);
                                    GameObject.Destroy(child.gameObject);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SWPT АНАТОМИЯ КРИТ]: Ошибка в реставраторе Instantiate: {ex.Message}");
            }
        }
    }
}

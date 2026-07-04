using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace LingerieButtonsFilter
{
    // ====================================================================
    // ТОЧЕЧНЫЙ ПАТЧ НА ОТКРЫТИЕ ПАНЕЛИ ГАРДЕРОБА
    // Ловит момент, когда игрок открывает вкладку белья, и накатывает защиту на лету!
    // ====================================================================
    [HarmonyPatch]
    public class ClosetTogglePatch
    {
        private static bool isClosetPatched = false;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            // Находим метод ToggleLingerie, который вы раскопали в коде игры!
            // Он гарантированно сидит в классах управления интерфейсом
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = assembly.GetName().Name;
                if (asmName.StartsWith("System") || asmName.StartsWith("mscorlib") || asmName.StartsWith("Mono")) continue;

                foreach (Type type in assembly.GetTypes())
                {
                    if (type != null && (type.Name == "UIInventory" || type.Name == "UICloset" || type.Name == "Mainframe"))
                    {
                        MethodInfo method = type.GetMethod("ToggleLingerie", BindingFlags.Public | BindingFlags.Instance);
                        if (method != null) return method;
                    }
                }
            }
            return null;
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            // Если патч на кнопки шкафа уже активен — отдыхаем
            if (isClosetPatched) return;

            try
            {
                var manualHarmony = new Harmony("com.yourname.swpt.closetclick");

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = assembly.GetName().Name;
                    if (asmName.StartsWith("System") || asmName.StartsWith("mscorlib") || asmName.StartsWith("Mono")) continue;

                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type != null && type.Name == "InventoryClosetItem")
                        {
                            MethodInfo originalMethod = type.GetMethod("ButtonTryOn", BindingFlags.Public | BindingFlags.Instance);
                            MethodInfo prefixMethod = typeof(ClosetClickPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);

                            if (originalMethod != null && prefixMethod != null)
                            {
                                manualHarmony.Patch(originalMethod, prefix: new HarmonyMethod(prefixMethod));
                                isClosetPatched = true;
                                Debug.Log("====================================================================");
                                Debug.Log("[SWPT АНАТОМИЯ]: КЛАСС InventoryClosetItem РАСПАКОВАН В ПАМЯТИ! ПАТЧ УСПЕШНО НАКАТАН!");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[SWPT КРИТ] Ошибка отложенного наката: {ex.Message}"); }
        }
    }

    // ====================================================================
    // САМ ПАТЧ ЗАЩИТЫ КЛИКА (Выполняется, когда кликаем по иконке в шкафу)
    // ====================================================================
    public class ClosetClickPatch
    {
        public static bool Prefix(object __instance)
        {
            if (__instance == null) return true;

            try
            {
                FieldInfo itemField = __instance.GetType().GetField("item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemField == null) return true;

                Transform itemTransform = (Transform)itemField.GetValue(__instance);
                if (itemTransform == null) return true;

                string itemNameLower = itemTransform.name.ToLower().Replace("(clone)", "").Trim();

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
                            // ИСПРАВЛЕНО: Теперь обрезаем пробелы СТРОГО у элементов массива, а не у самого массива!
                            string key = parts[0].Trim().ToLower();
                            string val = parts[1].Trim().ToLower();

                            int id = 100;
                            if (Enum.TryParse(val, true, out CustomSlotType mType)) id = (int)mType;
                            if (!clickMapTable.ContainsKey(key)) clickMapTable.Add(key, id);
                        }
                    }
                }

                if (clickMapTable.TryGetValue(itemNameLower, out int customCategoryId))
                {
                    var itemComponent = itemTransform.GetComponent<Item>();
                    if (customCategoryId == 100 && itemComponent != null && itemComponent.slotType == SlotType.none) return true;

                    CharacterCustomization curCustomization = Global.code?.uiInventory?.curCustomization;
                    if (curCustomization == null) return true;

                    curCustomization.showArmor = false;
                    bool isAlreadyWearing = curCustomization.IsWearing(itemTransform.name);
                    string customMarker = $"custom_{customCategoryId}";

                    foreach (Transform child in curCustomization.GetComponentsInChildren<Transform>(true))
                    {
                        if (child == null || !child.gameObject.activeSelf) continue;
                        string childName = child.name.ToLower().Replace("(clone)", "").Trim();

                        int wornCategoryId = -1;
                        foreach (var pair in clickMapTable)
                        {
                            if (childName.Contains(pair.Key))
                            {
                                wornCategoryId = pair.Value;
                                break;
                            }
                        }

                        if (wornCategoryId == customCategoryId && childName != itemNameLower)
                        {
                            Debug.Log($"[SWPT ШКАФ]: Вытеснение! Насильно удаляем старую модель '{child.name}' из категории {customCategoryId}...");
                            child.gameObject.SetActive(false);
                            GameObject.Destroy(child.gameObject);
                        }
                    }

                    if (!isAlreadyWearing)
                    {
                        Debug.Log($"[SWPT ШКАФ]: Спавним предмет '{itemTransform.name}' на виртуальный маркер '{customMarker}'...");
                        Transform newModelTransform = Utility.Instantiate(itemTransform);
                        curCustomization.AddItem(newModelTransform, customMarker);
                    }

                    Global.code?.uiInventory?.ButtonUnderwearGroup();
                    Global.code?.uiInventory?.RefreshEquipment();
                    return false;
                }
            }
            catch (Exception ex) { Debug.LogError($"[SWPT ШКАФ КРИТ]: {ex.Message}"); }

            return true;
        }
    }
}

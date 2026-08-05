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

        // Persistent runtime tracking for clothing assets restoration
        public static Item lastActiveGlovesItem = null;
        public static Item lastActiveMaskItem = null;

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

        // ====================================================================
        // HARMONY PREFIX: INVENTORY RENDERING AND COLLECTION FILTERING MATRIX
        // ====================================================================
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

                    // INITIALIZE EXTRACTED TEXT MAPPING DICTIONARY
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
                                    if (Enum.TryParse(val, true, out CustomSlotType mType)) id = (int)mType;
                                    if (!localMappingTable.ContainsKey(key)) localMappingTable.Add(key, id);
                                }
                            }
                        }
                    }
                    catch { }

                    // RUNTIME VISUAL TRANSFORMS CAPTURE
                    CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                    Dictionary<int, string> virtualSlotsMap = new Dictionary<int, string>();
                    Item itemToClickTakeOff = null;

                    if (cc != null)
                    {
                        bool maskPresent = false;
                        bool glovesPresent = false;

                        foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == null || !child.gameObject.activeSelf) continue;
                            string cleanName = child.name.ToLower().Replace("(clone)", "").Trim();

                            // STRICT OVERRIDE FOR TRUE PHYSICAL EQUIPMENT DETECTIONS
                            int matchedCategory = -1;
                            foreach (var pair in localMappingTable)
                            {
                                if (cleanName == pair.Key) // FIXED: Strictly matching exact database names instead of Contains
                                {
                                    matchedCategory = pair.Value;
                                    break;
                                }
                            }

                            // Dynamic flag mapping triggers matching underlying categories
                            if (matchedCategory == (int)CustomSlotType.Wrists) glovesPresent = true;
                            if (matchedCategory == (int)CustomSlotType.Hats || matchedCategory == (int)CustomSlotType.Eyes || matchedCategory == (int)CustomSlotType.Mouth || matchedCategory == (int)CustomSlotType.Neck) maskPresent = true;

                            if (matchedCategory > 100)
                            {
                                if (virtualSlotsMap.ContainsKey(matchedCategory))
                                {
                                    string oldItemName = virtualSlotsMap[matchedCategory];
                                    foreach (Transform t in originalItemsBackup)
                                    {
                                        if (t == null) continue;
                                        string storageNameLower = t.gameObject.name.ToLower().Replace("(clone)", "").Trim();
                                        string oldItemNameLower = oldItemName.ToLower().Replace("(clone)", "").Trim();

                                        if (oldItemNameLower == storageNameLower) // FIXED: Restricting to direct string equals checks
                                        {
                                            itemToClickTakeOff = t.GetComponent<Item>();
                                            break;
                                        }
                                    }
                                }
                                else virtualSlotsMap.Add(matchedCategory, child.name);
                            }
                        }

                        // CAPTURING LIVE RUNTIME REFERENCES FOR RE-EQUIP PROCEDURES
                        foreach (Transform t in originalItemsBackup)
                        {
                            if (t == null) continue;
                            var itemComponent = t.GetComponent<Item>();
                            if (itemComponent == null) continue;

                            string sName = t.name.ToLower().Replace("(clone)", "").Trim();

                            // Extract precise mapping lookup to cross-match active equipped tracking
                            if (localMappingTable.TryGetValue(sName, out int runtimeSlotId))
                            {
                                if (glovesPresent && runtimeSlotId == (int)CustomSlotType.Wrists) lastActiveGlovesItem = itemComponent;
                                if (maskPresent && (runtimeSlotId == (int)CustomSlotType.Hats || runtimeSlotId == (int)CustomSlotType.Eyes || runtimeSlotId == (int)CustomSlotType.Mouth || runtimeSlotId == (int)CustomSlotType.Neck)) lastActiveMaskItem = itemComponent;
                            }
                        }

                        // RUNTIME INTERFACE AUTO-RESTORATION PIPELINE
                        if (maskPresent && !glovesPresent && lastActiveGlovesItem != null)
                        {
                            Transform restored = Utility.Instantiate(lastActiveGlovesItem.transform);
                            cc.AddItem(restored, "misc1");
                            glovesPresent = true;
                        }
                        if (glovesPresent && !maskPresent && lastActiveMaskItem != null)
                        {
                            Transform restored = Utility.Instantiate(lastActiveMaskItem.transform);
                            cc.AddItem(restored, "misc2");
                            maskPresent = true;
                        }

                        if (itemToClickTakeOff != null)
                        {
                            try
                            {
                                isProcessingAutoUnequip = true;
                                itemToClickTakeOff.Use(cc);
                                isProcessingAutoUnequip = false;
                            }
                            catch { isProcessingAutoUnequip = false; }
                        }
                    }

                    // TWO-STAGE UI COLLECTION FILTERING PASS
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

                            // FIXED: Strict dictionary lookup replaces slow and unsafe loop Contains operations
                            if (localMappingTable.TryGetValue(itemNameLower, out int foundSlotId))
                            {
                                uiCategory = foundSlotId;
                                foundInMap = true;
                            }

                            if (foundInMap) slotTypeInt = uiCategory;
                            else if (!isStandard) slotTypeInt = 100;

                            if (MainPlugin.FilterMode == 1) // MASKS
                            {
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7)
                                {
                                    filteredItems.Add(itemTransform);
                                }
                            }
                            else if (MainPlugin.FilterMode == 2) // OTHER
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
            catch (Exception ex)
            {
                Debug.LogError($"[AdvancedWardrobe] Monolithic patch execution exception: {ex.Message}");
            }
        }
    }
}

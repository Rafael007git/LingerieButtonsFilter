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
        // HARMONY PREFIX: INVENTORY RENDERING AND STORAGE FILTERING MATRIX
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

                    Dictionary<string, int> localMappingTable = MainPlugin.ItemMappingTable;
                    if (localMappingTable == null) return;

                    // VISUAL TRANSFORMS CAPTURE
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

                            int matchedCategory = -1;
                            if (localMappingTable.TryGetValue(cleanName, out int foundId))
                            {
                                matchedCategory = foundId;
                            }

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

                                        if (oldItemNameLower == storageNameLower)
                                        {
                                            itemToClickTakeOff = t.GetComponent<Item>();
                                            break;
                                        }
                                    }
                                }
                                else virtualSlotsMap.Add(matchedCategory, child.name);
                            }
                        }

                        // CAPTURING TRACKING REFERENCES FOR ATTACHMENTS
                        foreach (Transform t in originalItemsBackup)
                        {
                            if (t == null) continue;
                            var itemComponent = t.GetComponent<Item>();
                            if (itemComponent == null) continue;

                            string sName = t.name.ToLower().Replace("(clone)", "").Trim();

                            if (localMappingTable.TryGetValue(sName, out int runtimeSlotId))
                            {
                                if (glovesPresent && runtimeSlotId == (int)CustomSlotType.Wrists) lastActiveGlovesItem = itemComponent;
                                if (maskPresent && (runtimeSlotId == (int)CustomSlotType.Hats || runtimeSlotId == (int)CustomSlotType.Eyes || runtimeSlotId == (int)CustomSlotType.Mouth || runtimeSlotId == (int)CustomSlotType.Neck)) lastActiveMaskItem = itemComponent;
                            }
                        }

                        // INTERFACE AUTO-RESTORATION PIPELINE
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

                            if (localMappingTable.TryGetValue(itemNameLower, out int foundSlotId))
                            {
                                uiCategory = foundSlotId;
                                foundInMap = true;
                            }

                            if (foundInMap)
                            {
                                slotTypeInt = uiCategory;
                            }
                            else
                            {
                                // FIX: Protect vanilla clothing assets from falling into OTHER slot 100
                                if (itemComponent.slotType == SlotType.none) slotTypeInt = 100;
                                else slotTypeInt = (int)itemComponent.slotType;
                            }

                            if (MainPlugin.FilterMode == 1) // MASKS
                            {
                                if ((slotTypeInt >= 101 && slotTypeInt <= 104) || slotTypeInt == 7) filteredItems.Add(itemTransform);
                            }
                            else if (MainPlugin.FilterMode == 2) // OTHER
                            {
                                if (slotTypeInt == 100 || (slotTypeInt >= 111 && slotTypeInt <= 113)) filteredItems.Add(itemTransform);
                            }
                        }
                    }

                    gameItemsList.Clear();
                    gameItemsList.AddRange(filteredItems);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AdvancedWardrobe] Monolithic patch execution failure: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance, ref bool __result)
        {
            if (isProcessingAutoUnequip) return;
            if (!__result && originalItemsBackup != null) { RestoreImmediately(); return; }
            try
            {
                Type iteratorType = __instance.GetType();
                FieldInfo stateField = iteratorType.GetField("<>1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateField != null && (int)stateField.GetValue(__instance) == -1) RestoreImmediately();
            }
            catch { }
        }

        private static void RestoreImmediately()
        {
            if (originalItemsBackup != null && Global.code?.playerLingerieStorage?.items?.items != null)
            {
                List<Transform> gameItemsList = Global.code.playerLingerieStorage.items.items;
                gameItemsList.Clear();
                gameItemsList.AddRange(originalItemsBackup);
                originalItemsBackup = null;
            }
        }
    }

    // ====================================================================
    // GLOBAL CEMENT: PERSISTENT MULTI-SLOT EQUIPMENT OVERRIDE PIPELINE 🧷⚙️
    // ====================================================================
    [HarmonyPatch(typeof(UIInventory), "RefreshEquipment")]
    public class UIInventory_GlobalCement_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                CharacterCustomization cc = GameObject.FindObjectOfType<CharacterCustomization>();
                if (cc == null) return;

                // 1. FORCE TRANSFORMS PURGE (Prevents mesh duplication overlap anomalies)
                foreach (Transform child in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null) continue;
                    if (child.parent != null)
                    {
                        string parentName = child.parent.name.ToLower();
                        if (parentName == "misc1" || parentName == "misc2")
                        {
                            child.gameObject.SetActive(false);
                            GameObject.Destroy(child.gameObject);
                        }
                    }
                }

                // 2. HARD LOCK ATTACHMENT: Force-inject onto misc1 anchor
                if (InventoryFilterPatch.lastActiveGlovesItem != null)
                {
                    Transform restoredGloves = Utility.Instantiate(InventoryFilterPatch.lastActiveGlovesItem.transform);
                    cc.AddItem(restoredGloves, "misc1");
                }

                // 3. HARD LOCK ATTACHMENT: Force-inject onto misc2 anchor
                if (InventoryFilterPatch.lastActiveMaskItem != null)
                {
                    Transform restoredMask = Utility.Instantiate(InventoryFilterPatch.lastActiveMaskItem.transform);
                    cc.AddItem(restoredMask, "misc2");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AdvancedWardrobe Runtime Error]: Equipment anchor failure: {ex.Message}");
            }
        }
    }
}

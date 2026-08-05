using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

namespace LingerieButtonsFilter
{
    [BepInPlugin("com.lorifel.swpt.lingeriebuttonsfilter", "SWPT Advanced Wardrobe", "0.0.3")] // Updated release version string
    public class MainPlugin : BaseUnityPlugin
    {
        // Thread-safe invocation action allowing cross-file interface refresh commands
        public static Action FilterModeChanged;

        // 0 = Default, 1 = MASKS, 2 = OTHER UI display categories row state index
        public static int FilterMode = 0;
        public static bool IsUiCustomized = false;
        public static Sprite MasksSprite;
        public static Sprite OtherSprite;

        // Core database dictionary: Key = lowercase unique asset identity, Value = virtual slot integer index
        public static Dictionary<string, int> ItemMappingTable = new Dictionary<string, int>();

        private void Awake()
        {
            // Initialize user definitions properties configurations and load custom asset bundles
            ModConfig.Init(Config);
            LoadEmbeddedIcons();
            LoadItemMappingTable();

            // SYSTEM SCENE INJECTION: Stable container setup attached to inventory initialization cycles
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                IsUiCustomized = false;

                // Safely query active instances within current active canvas allocations
                UIInventory[] foundInventories = Resources.FindObjectsOfTypeAll<UIInventory>();

                // Isolate the root workspace execution block targeting the dominant interface reference
                UIInventory uiInventory = (foundInventories != null && foundInventories.Length > 0)
                    ? foundInventories[0]
                    : null;

                if (uiInventory != null)
                {
                    Transform cat2 = uiInventory.transform.Find("Right/Lingerie Group/Category (2)");
                    if (cat2 != null && cat2.gameObject.GetComponent<InventoryUiController>() == null)
                    {
                        cat2.gameObject.AddComponent<InventoryUiController>();
                    }
                }
            };

            // INITIALIZE HARMONY ENGINE INJECTIONS
            var harmony = new Harmony("com.lorifel.swpt.inventoryfilter");
            harmony.PatchAll();

            Logger.LogInfo("Advanced Wardrobe Filter Engine successfully initialized.");
        }

        private void LoadEmbeddedIcons()
        {
            try
            {
                string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                MasksSprite = LoadSpriteFromResource($"{assemblyName}.icon_masks.png");
                OtherSprite = LoadSpriteFromResource($"{assemblyName}.icon_other.png");

                if (MasksSprite != null && OtherSprite != null)
                {
                    Logger.LogInfo("[AdvancedWardrobe] Custom categories MASKS and OTHER sprite icons successfully activated.");
                }
                else
                {
                    Logger.LogWarning("[AdvancedWardrobe] Custom asset sprites returned null. Registered manifest references:");
                    foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    {
                        Logger.LogInfo($" -> '{res}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AdvancedWardrobe] Critical embedded UI resource loading exception: {ex.Message}");
            }
        }

        private Sprite LoadSpriteFromResource(string resourcePath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream == null) return null;
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(buffer))
                {
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            return null;
        }

        private void LoadItemMappingTable()
        {
            try
            {
                // Resolve direct system configurations paths tracking definitions text file
                string configFolder = BepInEx.Paths.ConfigPath;
                string filePath = Path.Combine(configFolder, "Lingerie_Item_Mapping.txt");

                Logger.LogInfo($"[AdvancedWardrobe] Validating dictionary tables routing path: {filePath}");

                if (!Directory.Exists(configFolder))
                {
                    Directory.CreateDirectory(configFolder);
                }

                // Generates an english template configuration file if missing from disk layout
                if (!File.Exists(filePath))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# === ADVANCED WARDROBE FILTER: ITEM SELECTION DICTIONARY ===");
                    sb.AppendLine("# Locate the exact game object asset name using UnityExplorer, then bind it using '=' symbol.");
                    sb.AppendLine("# Supported custom categories: Accessorie, Hats, Eyes, Mouth, Earrings, Wrists, Neck, Nipples");
                    sb.AppendLine("# All unlisted custom equipment items will fall back to 'Accessorie' by default.");
                    sb.AppendLine("# ------------------------------------------------------------------------------");
                    sb.AppendLine("# Operational Examples:");
                    sb.AppendLine("BDSM_Collar_Black = Neck");
                    sb.AppendLine("Super_Sexy_Gag_v2 = Mouth");

                    File.WriteAllText(filePath, sb.ToString());
                    Logger.LogInfo("[AdvancedWardrobe] Configuration mapping template Lingerie_Item_Mapping.txt created successfully.");
                }

                ItemMappingTable.Clear();
                string[] lines = File.ReadAllLines(filePath);

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    string[] parts = trimmed.Split('=');
                    if (parts.Length == 2)
                    {
                        string itemName = parts[0].Trim().ToLower();
                        string typeStr = parts[1].Trim().ToLower();

                        int targetSlotId = 100; // Default fallback index definitions match

                        if (System.Enum.TryParse(typeStr, true, out CustomSlotType matchedType))
                        {
                            targetSlotId = (int)matchedType;
                        }

                        if (!ItemMappingTable.ContainsKey(itemName))
                        {
                            ItemMappingTable.Add(itemName, targetSlotId);
                        }
                    }
                }
                Logger.LogInfo($"[AdvancedWardrobe] Data mapping layout loaded successfully. Active item definitions records count: {ItemMappingTable.Count}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[AdvancedWardrobe Critical Failure] System file storage configuration execution error: {ex.Message}");
            }
        }
    }
}

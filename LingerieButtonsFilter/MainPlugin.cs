using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Reflection;

namespace LingerieButtonsFilter
{
    [BepInPlugin("com.yourname.swpt.inventoryfilter", "SWPT Advanced Wardrobe", "1.2.0")]
    public class MainPlugin : BaseUnityPlugin
    {
        public static int FilterMode = 0; // 0 - дефолт, 1 - MASKS, 2 - OTHER
        public static bool IsUiCustomized = false;
        public static Sprite MasksSprite;
        public static Sprite OtherSprite;

        private void Awake()
        {
            // Инициализируем наш новый конфиг при старте мода
            ModConfig.Init(Config);

            LoadEmbeddedIcons();

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                IsUiCustomized = false;

                // ФИКС: Ищем инвентарь, даже если он скрыт/выключен разработчиками при старте сцены
                UIInventory uiInventory = Resources.FindObjectsOfTypeAll<UIInventory>().Length > 0
                    ? Resources.FindObjectsOfTypeAll<UIInventory>()[0]
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

            var harmony = new Harmony("com.yourname.swpt.inventoryfilter");
            harmony.PatchAll();

            Logger.LogInfo("Финальный релиз мода гардероба 1.2.0 успешно запущен!");
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
                    Logger.LogInfo("[SWPT Assets] Кастомные иконки MASKS и OTHER успешно активированы!");
                }
                else
                {
                    Logger.LogWarning("[SWPT Assets] Спрайты вернули null. Доступные ресурсы:");
                    foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    {
                        Logger.LogInfo($" -> '{res}'");
                    }
                }
            }
            catch (Exception ex) { Logger.LogError($"[SWPT Assets] Ошибка ресурсов: {ex.Message}"); }
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
    }
}

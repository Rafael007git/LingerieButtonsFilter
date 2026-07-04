using UnityEngine;
using UnityEngine.UI;
using System;

namespace LingerieButtonsFilter
{
    [DisallowMultipleComponent]
    public class InventoryUiController : MonoBehaviour
    {
        // Вызывается один раз при подселении скрипта на сцену сейва
        private void Start()
        {
            ModifyInterface(this.transform);
        }

        // Вызывается каждый раз, когда игрок открывает вкладку белья
        private void OnEnable()
        {
            ModifyInterface(this.transform);
        }

        private void ModifyInterface(Transform cat2)
        {
            // СТРАХОВКА: Если мы зашли сюда через OnEnable, а кнопок МАСОК еще нет на сцене,
            // значит интерфейс "чистый", и мы обязаны принудительно разрешить кастомизацию!
            if (cat2.Find("Button MASKS") == null)
            {
                MainPlugin.IsUiCustomized = false;
            }

            try
            {
                // Ищем стандартные кнопки игры (с расширенным поиском на случай скрытия)
                Transform btnBra = cat2.Find("Button Bra") ?? cat2.Find("Button (0)");
                Transform btnPanties = cat2.Find("Button Panties") ?? cat2.Find("Button (1)");
                Transform btnGarter = cat2.Find("Button Suspenders") ?? cat2.Find("Button GarterBelt") ?? cat2.Find("Button (2)");
                Transform btnStockings = cat2.Find("Button stockings") ?? cat2.Find("Button (3)") ?? cat2.GetComponentInChildren<Button>()?.transform;
                Transform btnHeels = cat2.Find("Button Heels") ?? cat2.Find("Button (4)");
                Transform btnAll = cat2.Find("Button ALL") ?? cat2.Find("Button (5)");

                Transform templateBtn = btnStockings;
                if (templateBtn == null) return;

                // Скрываем стандартную кнопку ALL
                if (btnAll != null) btnAll.gameObject.SetActive(false);

                // Ищем или создаем кнопку MASKS
                Transform maskBtnTransform = cat2.Find("Button MASKS");
                GameObject maskBtnObj;
                if (maskBtnTransform == null)
                {
                    maskBtnObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, cat2);
                    maskBtnObj.name = "Button MASKS";
                    maskBtnObj.transform.SetParent(cat2, false); // Выдергиваем в корень Категории (2)
                    maskBtnObj.transform.localScale = Vector3.one;
                    ApplyTextAndCustomIcon(maskBtnObj, "MASKS", MainPlugin.MasksSprite);

                    Button maskBtn = maskBtnObj.GetComponent<Button>();
                    maskBtn.onClick.RemoveAllListeners();
                    maskBtn.onClick.AddListener(() => {
                        MainPlugin.FilterMode = 1;
                        TriggerGameRefresh();
                    });
                }
                else
                {
                    maskBtnObj = maskBtnTransform.gameObject;
                    maskBtnObj.transform.SetParent(cat2, false);
                }

                // Ищем или создаем кнопку OTHER
                Transform otherBtnTransform = cat2.Find("Button OTHER");
                GameObject otherBtnObj;
                if (otherBtnTransform == null)
                {
                    otherBtnObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, cat2);
                    otherBtnObj.name = "Button OTHER";
                    otherBtnObj.transform.SetParent(cat2, false);
                    otherBtnObj.transform.localScale = Vector3.one;
                    ApplyTextAndCustomIcon(otherBtnObj, "OTHER", MainPlugin.OtherSprite);

                    Button otherBtn = otherBtnObj.GetComponent<Button>();
                    otherBtn.onClick.RemoveAllListeners();

                    // ДОБАВИЛИ ВЫЗОВ ТРИГГЕРА ДЛЯ OTHER СЮДА:
                    otherBtn.onClick.AddListener(() => {
                        MainPlugin.FilterMode = 2;
                        TriggerGameRefresh();
                    });
                }
                else
                {
                    otherBtnObj = otherBtnTransform.gameObject;
                    otherBtnObj.transform.SetParent(cat2, false);
                }


                // Накатываем сброс нашего режима на стандартные кнопки игры
                foreach (Transform child in cat2)
                {
                    if (child.name != "Button MASKS" && child.name != "Button OTHER")
                    {
                        Button b = child.GetComponent<Button>();
                        if (b != null)
                        {
                            b.onClick.RemoveListener(() => { MainPlugin.FilterMode = 0; });
                            b.onClick.AddListener(() => { MainPlugin.FilterMode = 0; });
                        }
                    }
                }

                // УБИВАЕМ АВТОВЕРСТКУ ИГРЫ (Она нам больше не указ)
                var verticalLayout = cat2.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    UnityEngine.Object.DestroyImmediate(verticalLayout);
                }

                // ВЫДЕРГИВАЕМ ЧУЛОК В КОРЕНЬ (Избавляемся от растяжения Stretch)
                if (btnStockings != null)
                {
                    btnStockings.SetParent(cat2, false);
                }

                // УЛЬТИМАТИВНОЕ ЕДИНООБРАЗИЕ: Собираем ВСЕ кнопки, которые есть на панели прямо сейчас
                var allButtons = cat2.GetComponentsInChildren<Button>(true);
                var finalOrderedList = new System.Collections.Generic.List<Transform>();

                // Создаем временные переменные для точечной сортировки
                Transform faceBtn = null;
                Transform handBtn = null;
                Transform braBtn = null;
                Transform pantiesBtn = null;
                Transform garterBtn = null;
                Transform stockingsBtn = null;
                Transform heelsBtn = null;

                // Распределяем кнопки на основе их имён или индексов (без привязки к регистру)
                foreach (var btn in allButtons)
                {
                    string n = btn.name.ToLower();
                    if (n.Contains("masks")) faceBtn = btn.transform;
                    else if (n.Contains("other")) handBtn = btn.transform;
                    else if (n.Contains("bra") || n.Contains("(0)")) braBtn = btn.transform;
                    else if (n.Contains("panties") || n.Contains("(1)")) pantiesBtn = btn.transform;
                    else if (n.Contains("garter") || n.Contains("suspenders") || n.Contains("(2)")) garterBtn = btn.transform;
                    else if (n.Contains("stockings") || n.Contains("(3)")) stockingsBtn = btn.transform;
                    else if (n.Contains("heels") || n.Contains("(4)")) heelsBtn = btn.transform;
                }

                // Заполняем список в ЖЕСТКОМ АНАТОМИЧЕСКОМ ПОРЯДКЕ, который мы хотим:
                // Лицо -> Рука -> Лиф -> Трусы -> Пояс -> Чулки -> Туфли
                if (faceBtn != null) finalOrderedList.Add(faceBtn);
                if (handBtn != null) finalOrderedList.Add(handBtn);
                if (braBtn != null) finalOrderedList.Add(braBtn);
                if (pantiesBtn != null) finalOrderedList.Add(pantiesBtn);
                if (garterBtn != null) finalOrderedList.Add(garterBtn);
                if (stockingsBtn != null) finalOrderedList.Add(stockingsBtn);
                if (heelsBtn != null) finalOrderedList.Add(heelsBtn);

                // Если какая-то кнопка не опозналась по ключевым словам, добавляем её в конец, чтобы не потерять
                foreach (var btn in allButtons)
                {
                    if (btn.name != "Button ALL" && !finalOrderedList.Contains(btn.transform))
                    {
                        finalOrderedList.Add(btn.transform);
                    }
                }

                // ЖЕЛЕЗНЫЙ ЦИКЛ ВЕРСТКИ: Возвращаем оригинальные крупные размеры игры!
                // float buttonWidth = 160f;   // Настоящая полная ширина оригинальных кнопок игры
                // float buttonHeight = 42f;   // Настоящая высота крупных кнопок игры
                // float spacing = 6f;         // Красивый отступ между ними
                float buttonWidth = ModConfig.ButtonWidth.Value;   // Считывает ширину (например, 176.0)
                float buttonHeight = ModConfig.ButtonHeight.Value; // Считывает высоту (например, 46.0)
                float spacing = ModConfig.Spacing.Value;           // Считывает отступ (например, 7.0)

                // Фиксированная стартовая точка Y. Мы берем базовый сдвиг -290f пикселей,
                // чтобы верхняя кнопка "Лицо" гарантированно вышла из-за края экрана,
                // и добавляем ручную настройку StartY из файла конфигурации!
                float currentY = -290f + ModConfig.StartY.Value;

                foreach (Transform btn in finalOrderedList)
                {
                    if (btn == null) continue;

                    btn.gameObject.SetActive(true);

                    var animator = btn.GetComponent<Animator>();
                    if (animator != null) animator.enabled = false;

                    RectTransform rect = btn.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        // Жестко выравниваем по левому верхнему углу (0, 1) для стабильности размеров
                        rect.anchorMin = new Vector2(0f, 1f);
                        rect.anchorMax = new Vector2(0f, 1f);
                        rect.pivot =  new Vector2(0f, 1f); // Левый верхний угол самой кнопки

                        // Возвращаем кнопкам их исходный крупный размер 160x42
                        rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);

                        // X ставим в 0 (идеальный левый край панели), а Y шагает вниз
                        rect.anchoredPosition = new Vector2(0f, currentY);

                        currentY -= (buttonHeight + spacing);
                    }
                }

                // Корректно расширяем саму панель по высоте, чтобы нижняя кнопка (туфли) не резалась
                cat2.GetComponent<RectTransform>()?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(currentY));

                MainPlugin.IsUiCustomized = true;

            }
            catch (Exception ex) { Debug.LogError($"[SWPT UI] Ошибка тотальной верстки UI: {ex.Message}"); }
        }


        private void TriggerGameRefresh()
        {
            // ФИНАЛЬНЫЙ ТРИГГЕР-ОХОТНИК: Шкаф 100% открыт на экране, DLL распакована!
            // Накатываем защиту кликов в ту же миллисекунду, когда игрок пользуется фильтрами!
            ClosetClickPatch.ApplyManualPatch();

            UIInventory uiInventory = GameObject.FindObjectOfType<UIInventory>();
            if (uiInventory != null)
            {
                uiInventory.curSlotType = SlotType.none;
                uiInventory.ButtonUnderwearGroup();
            }
        }

        private void ApplyTextAndCustomIcon(GameObject obj, string txt, Sprite customSprite)
        {
            foreach (var c in obj.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.GetType().Name.Contains("Text") || c.GetType().Name.Contains("TMP"))
                {
                    c.GetType().GetProperty("text")?.SetValue(c, txt, null);
                    break;
                }
            }

            if (customSprite != null)
            {
                Image btnImage = obj.GetComponent<Image>() ?? obj.GetComponentInChildren<Image>();
                if (btnImage != null)
                {
                    btnImage.sprite = customSprite;
                }
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using System;

namespace LingerieButtonsFilter
{
    [DisallowMultipleComponent]
    public class InventoryUiController : MonoBehaviour
    {
        private void Start()
        {
            ModifyInterface(this.transform);
        }

        private void OnEnable()
        {
            ModifyInterface(this.transform);
        }

        private void ModifyInterface(Transform cat2)
        {
            if (cat2.Find("Button MASKS") == null)
            {
                MainPlugin.IsUiCustomized = false;
            }

            try
            {
                // RESOLVE EXACT CANONICAL VANILLA OBJECT NAMES
                Transform btnBra = cat2.Find("Button bras");
                Transform btnPanties = cat2.Find("Button panties");
                Transform btnGarter = cat2.Find("Button garter");
                Transform btnStockings = cat2.Find("Button stockings");
                Transform btnHeels = cat2.Find("Button heels");
                Transform btnAll = cat2.Find("Button ALL") ?? cat2.Find("Button (5)");

                Transform templateBtn = btnStockings;
                if (templateBtn == null) return;

                if (btnAll != null) btnAll.gameObject.SetActive(false);

                // INITIALIZE CUSTOM MASKS FILTER BUTTON
                Transform maskBtnTransform = cat2.Find("Button MASKS");
                GameObject maskBtnObj;
                if (maskBtnTransform == null)
                {
                    maskBtnObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, cat2);
                    maskBtnObj.name = "Button MASKS";
                    maskBtnObj.transform.SetParent(cat2, false);
                    maskBtnObj.transform.localScale = Vector3.one;
                    ApplyTextAndCustomIcon(maskBtnObj, "MASKS", MainPlugin.MasksSprite);

                    Button maskBtn = maskBtnObj.GetComponent<Button>();
                    maskBtn.onClick.RemoveAllListeners();
                    maskBtn.onClick.AddListener(() => {
                        MainPlugin.FilterMode = 1;
                        TriggerGameRefresh();
                    });
                    maskBtnTransform = maskBtnObj.transform;
                }
                else
                {
                    maskBtnObj = maskBtnTransform.gameObject;
                    maskBtnObj.transform.SetParent(cat2, false);
                }

                // INITIALIZE CUSTOM OTHER FILTER BUTTON
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
                    otherBtn.onClick.AddListener(() => {
                        MainPlugin.FilterMode = 2;
                        TriggerGameRefresh();
                    });
                    otherBtnTransform = otherBtnObj.transform;
                }
                else
                {
                    otherBtnObj = otherBtnTransform.gameObject;
                    otherBtnObj.transform.SetParent(cat2, false);
                }

                // RESTORE LINGERIE RESET LOGIC FOR VANILLA BUTTONS
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

                var verticalLayout = cat2.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    UnityEngine.Object.DestroyImmediate(verticalLayout);
                }

                if (btnStockings != null) btnStockings.SetParent(cat2, false);

                // BUILD INTENDED LAYOUT MATRIX DESIGN SEQUENCING
                var allButtons = cat2.GetComponentsInChildren<Button>(true);
                var finalOrderedList = new System.Collections.Generic.List<Transform>();

                if (maskBtnTransform != null) finalOrderedList.Add(maskBtnTransform);
                if (otherBtnTransform != null) finalOrderedList.Add(otherBtnTransform);
                if (btnBra != null) finalOrderedList.Add(btnBra);
                if (btnPanties != null) finalOrderedList.Add(btnPanties);
                if (btnGarter != null) finalOrderedList.Add(btnGarter);
                if (btnStockings != null) finalOrderedList.Add(btnStockings);
                if (btnHeels != null) finalOrderedList.Add(btnHeels);

                foreach (var btn in allButtons)
                {
                    if (btn.name != "Button ALL" && !finalOrderedList.Contains(btn.transform))
                    {
                        finalOrderedList.Add(btn.transform);
                    }
                }

                // MANUAL POSITIONS CALCULATOR ENGINE
                float buttonWidth = ModConfig.ButtonWidth.Value;
                float buttonHeight = ModConfig.ButtonHeight.Value;
                float spacing = ModConfig.Spacing.Value;
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
                        rect.anchorMin = new Vector2(0f, 1f);
                        rect.anchorMax = new Vector2(0f, 1f);
                        rect.pivot = new Vector2(0f, 1f);
                        rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
                        rect.anchoredPosition = new Vector2(0f, currentY);
                        currentY -= (buttonHeight + spacing);
                    }
                }

                cat2.GetComponent<RectTransform>()?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(currentY));
                MainPlugin.IsUiCustomized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AdvancedWardrobe UI] Layout matrix override failure: {ex.Message}");
            }
        }

        private void TriggerGameRefresh()
        {
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
                if (btnImage != null) btnImage.sprite = customSprite;
            }
        }
    }
}

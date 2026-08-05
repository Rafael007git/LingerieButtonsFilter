using UnityEngine;
using UnityEngine.UI;
using System;

namespace LingerieButtonsFilter
{
    [DisallowMultipleComponent]
    public class InventoryUiController : MonoBehaviour
    {
        // Triggered once when the script component is attached to the Save Scene context
        private void Start()
        {
            ModifyInterface(this.transform);
        }

        // Triggered every time the player opens or toggles the Lingerie group tab
        private void OnEnable()
        {
            ModifyInterface(this.transform);
        }

        private void ModifyInterface(Transform cat2)
        {
            // SAFEGUARD: If opened via OnEnable but MASKS button is missing from the scene hierarchy,
            // the UI state is considered reset, and customization must be forced again.
            if (cat2.Find("Button MASKS") == null)
            {
                MainPlugin.IsUiCustomized = false;
            }

            try
            {
                // Resolve references to standard game inventory category layout buttons
                Transform btnBra = cat2.Find("Button Bra") ?? cat2.Find("Button (0)");
                Transform btnPanties = cat2.Find("Button Panties") ?? cat2.Find("Button (1)");
                Transform btnGarter = cat2.Find("Button Suspenders") ?? cat2.Find("Button GarterBelt") ?? cat2.Find("Button (2)");
                Transform btnStockings = cat2.Find("Button stockings") ?? cat2.Find("Button (3)") ?? cat2.GetComponentInChildren<Button>()?.transform;
                Transform btnHeels = cat2.Find("Button Heels") ?? cat2.Find("Button (4)");
                Transform btnAll = cat2.Find("Button ALL") ?? cat2.Find("Button (5)");

                Transform templateBtn = btnStockings;
                if (templateBtn == null) return;

                // Deactivate the vanilla "ALL" filter button to release canvas space
                if (btnAll != null) btnAll.gameObject.SetActive(false);

                // Resolve or dynamically instantiate the custom MASKS subcategory filter button
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
                }
                else
                {
                    maskBtnObj = maskBtnTransform.gameObject;
                    maskBtnObj.transform.SetParent(cat2, false);
                }

                // Resolve or dynamically instantiate the custom OTHER subcategory filter button
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
                }
                else
                {
                    otherBtnObj = otherBtnTransform.gameObject;
                    otherBtnObj.transform.SetParent(cat2, false);
                }


                // Bind reset logic to the standard vanilla wardrobe buttons
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

                // DISABLE VANILLA AUTO-LAYOUT COMPONENT to allow pixel-perfect manual positioning overrides
                var verticalLayout = cat2.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    UnityEngine.Object.DestroyImmediate(verticalLayout);
                }

                // Detach stockings item button to parent container root to prevent canvas stretching issues
                if (btnStockings != null)
                {
                    btnStockings.SetParent(cat2, false);
                }

                // UNIFIED BUTTONS: Collect all active layout button transformations present on the panel
                var allButtons = cat2.GetComponentsInChildren<Button>(true);
                var finalOrderedList = new System.Collections.Generic.List<Transform>();

                Transform faceBtn = null;
                Transform handBtn = null;
                Transform braBtn = null;
                Transform pantiesBtn = null;
                Transform garterBtn = null;
                Transform stockingsBtn = null;
                Transform heelsBtn = null;

                // =========================================================================
                // STRICT ANATOMICAL LAYOUT ORDER SELECTION (NO CONTAINS FILTERING) 📐✨
                // =========================================================================
                // Build the interface row strictly following the intended design hierarchy:
                // Masks (Face) -> Other (Wrists/Neck) -> Bra -> Panties -> Garter -> Stockings -> Heels
                if (maskBtnTransform != null) finalOrderedList.Add(maskBtnTransform);
                if (otherBtnTransform != null) finalOrderedList.Add(otherBtnTransform);
                if (btnBra != null) finalOrderedList.Add(btnBra);
                if (btnPanties != null) finalOrderedList.Add(btnPanties);
                if (btnGarter != null) finalOrderedList.Add(btnGarter);
                if (btnStockings != null) finalOrderedList.Add(btnStockings);
                if (btnHeels != null) finalOrderedList.Add(btnHeels); // Фикс регистра: btnHeels с маленькой буквы

                // Fallback: Append any unexpected custom or external mod buttons 
                // to the end of the stack to completely prevent UI element loss
                foreach (var btn in allButtons)
                {
                    if (btn.name != "Button ALL" && !finalOrderedList.Contains(btn.transform))
                    {
                        finalOrderedList.Add(btn.transform);
                    }
                }

                // MANUAL CANVAS RE-ANCHORING ENGINE BLOCK
                float buttonWidth = ModConfig.ButtonWidth.Value;
                float buttonHeight = ModConfig.ButtonHeight.Value;
                float spacing = ModConfig.Spacing.Value;

                // Calculated vertical anchor entry point offset pulled from configurations
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
                        // Anchor calculations relative to the top-left boundaries (0, 1) for layout calculations stability
                        rect.anchorMin = new Vector2(0f, 1f);
                        rect.anchorMax = new Vector2(0f, 1f);
                        rect.pivot =  new Vector2(0f, 1f);

                        rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
                        rect.anchoredPosition = new Vector2(0f, currentY);

                        currentY -= (buttonHeight + spacing);
                    }
                }

                // Dynamically expand container layout canvas bounds matching total children scale height
                cat2.GetComponent<RectTransform>()?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(currentY));

                MainPlugin.IsUiCustomized = true;

            }
            catch (Exception ex) { Debug.LogError($"[SWPT UI] Critical layout matrix override failure: {ex.Message}");
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
                if (btnImage != null)
                {
                    btnImage.sprite = customSprite;
                }
            }
        }
    }
}

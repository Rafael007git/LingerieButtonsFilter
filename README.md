Lingerie System Rework Plan

1. BACKGROUND & MOTIVATION (WHY WE NEED IT)
 Currently, player character accessory customization in SWPT is heavily bottlenecked by slot limitations. It is impossible to equip a hat, a facial mask, earrings, a gag, and a collar on the same character simultaneously.
For example, equipping any mask from MrEsturk's mod automatically forces the character to unequip their gloves. This strict "one-slot-excludes-another" behavior kills the potential of any modded accessory packs, as items cannot be layered or combined together.

2. CURRENT STATE OF THE GAME (WHAT WE HAVE)
•	Hardcoded Slots: SWPT natively supports only 6 lingerie slots: lingeriegloves, bra, panties, suspender, stockings, and shoes.
•	Unused Mesh Hooks: The base character model hierarchy contains 8 unused transforms/hooks named misc1 through misc8. These are ideal anchor points for attaching custom accessory meshes via our mod.

3. THE TARGET ARCHITECTURE (WHAT WE WANT)
 We aim to introduce 6 new functional accessory categories:
 - Headwear (Hats, crowns)
 - Eyes/Face (Masks, glasses)
 - Mouth (Gags, etc.)
 - Earrings
 - Neck (Collars, necklaces)
 - Nipples (Piercings, clamps)
 
UI/UX Layout:
 To prevent overcrowding the Character Customization UI, we can group these new categories into two distinct visual sections:
•	"Head Accessories" (combining Headwear, Face, Mouth, and Earrings).
•	"Body Accessories" (combining Neck, Nipples, and the existing Lingerie Gloves, as there are only 2 vanilla types of gloves in the game anyway).

4. PROGRESS SO FAR (WHAT I HAVE DONE)
•	Modified the Customization UI by adding a new item type button ("Head") and renaming the messy vanilla "All" button to "Other".
•	Designed and integrated custom icons for these UI tabs: "Masked face" and "Lace glove".
•	Rearranged the buttons in anatomical order (going from head to heels)
•	Successfully filtered items: masks from MrEsturk's mod now show up properly under the "Head" section, and lingerie gloves are isolated under the "Other" section (improving vanilla navigation immensely).

5. TECHNICAL ROADBLOCKS (WHAT I COULDN'T DO)
 I attempted to expand the slot count by virtually splitting the native lingeriegloves slot into multiple custom subtypes (managing them via a wrapper system sitting on top of the game's native inventory), but hit two critical walls:
1.	The Equip Override Bug: No matter what I tried, equipping any item assigned to the base lingeriegloves type automatically forces the game's internal manager to unequip the previously equipped lingeriegloves item. The game's native ItemManager / EquipmentSystem completely overrides our virtual subtypes.
2.	Mesh Attachment Issue: I haven't successfully managed to intercept the equipment spawn pipeline to force specific item meshes to attach to the misc1–misc8 bone hooks instead of default hand/arm transforms.

6. ROADMAP & NEXT STEPS (WHAT HAS TO BE DONE)
 To make this system fully functional, we need to collaborate on the following tasks:
•	Equipment Pipeline Hooking: We need to find and patch the exact method inside the game's equipment manager (likely EquipItem, WearLingerie, or similar) where the hardcoded slot check happens. We must prevent it from triggering an unequip event if the items belong to different custom subtypes.
•	Bone Anchor Redirection: Write a Harmony patch that checks the custom subtype of an item before rendering, and routes its instantiated mesh to anchor directly onto the corresponding miscX transform on the character model hierarchy.
•	Save/Load Serialization Sync: Ensure that when a character is saved or loaded, the state of all 6 newly sub-typed accessory slots is fully preserved and serialized without breaking vanilla save file integrity.

# Preparation and newspaper integration

## Project audit

- `SampleScene.unity` is the template scene and the only enabled scene in Build Settings.
- `AuctionDemo.unity` is the playable auction demo. Its scene-local `AuctionBootstrap` starts the six demo lots automatically. Its Canvas, `AuctionUI`, and Input System EventSystem can support the new overlay.
- `MvpAuctionLevel`, `AuctionItem`, and `ItemDatabase` live in `Assets/Scripts/Auction/Data`. The production assets are under `Assets/AuctionMvp`. The level defines the ordered run, the full catalog reference, the budget, the profit target, and the approved Slovak newspaper copy.
- `AuctionManager.StartAuction(run, catalog)` accepts `MvpAuctionLevel.CreateRun()` and `MvpAuctionLevel.Database`. The manager uses the scene's existing `PlayerEconomy` balance. It does not accept a budget or profit target argument.
- No GameManager, runtime scene loader, Game End scene, or Report Card UI was found. `AuctionManager.OnAuctionPhaseFinished` is the current end-of-auction event.
- There were no existing Preparation, Newspaper, or SALE UI components or auction-specific prefabs. TextMesh Pro is configured with the project's Liberation Sans font and a dynamic fallback for additional characters.
- Developer A's auction manager, AI, and auction-end responsibilities are outside this change. The audit also treated the checked-in demo scene, bootstrap, auction UI, and economy as shared integration surfaces.

## Approved clue mappings

Each clue points to a catalog item ID. Both variants of that item type share the article because the view renders the entire approved edition from the level.

| Section | Clue key | Catalog item ID |
| --- | --- | --- |
| Main articles | `news_clay_tablet` | `clay-tablet` |
| Main articles | `news_ibis` | `ibis` |
| Main articles | `news_blue_horse` | `blue-horse` |
| Short reports | `news_obsidian_blade` | `obsidian-blade` |
| Short reports | `news_bronze_statue` | `bronze-statue` |
| Short reports | `news_glass_vase` | `glass-vase` |

## Inspector setup

1. Duplicate `Assets/Scenes/AuctionDemo.unity` into your own test or integration scene. Alternatively, use the destination auction scene once its owner is ready to integrate. Keep its working auction Canvas and EventSystem.
2. Select `AuctionSystem`. Disable its **Auction Bootstrap** component so the scene begins with the existing manager idle.
3. On `AuctionSystem > Player Economy`, set **Starting Budget** to the value on `Assets/AuctionMvp/MvpAuctionLevel.asset`, currently **1000**. Recheck the Inspector value if the level budget changes. Preparation checks the balance before starting and displays an error if the two differ.
4. Create a root GameObject named **Preparation** and add **Preparation Phase**. Assign the production **Mvp Auction Level** asset and drag the scene's **Auction Manager** into **Auction**. Add or reuse a **Canvas Group** on the auction controls and assign it to **Auction Controls**. The component creates its overlay Canvas at runtime. Keep **Sorting Order** above the auction Canvas.
5. Customize **Sale Brief** in the Inspector. Use `{0}` for the formatted budget and `{1}` for the formatted target. The default renders the requested English brief.
6. Press Play. Open and close the newspaper in Preparation, then start the auction. Use **Newspaper** in the lower-right corner during the run. Scroll through the edition using the mouse wheel or drag. **Close Newspaper** stays visible while scrolling.

The existing manager and AI continue updating underneath the full-screen newspaper. The overlay blocks pointer raycasts to the auction Canvas and moves keyboard or gamepad focus to **Close Newspaper**. At auction completion the component closes the overlay and hides the opener.

To use an existing auction button, wire its **On Click** to `PreparationPhase.OpenNewspaper()`. A host can also use `CloseNewspaper()`. To reuse the view independently, put `NewspaperView` on a dedicated full-stretch RectTransform under a topmost Canvas with a GraphicRaycaster. Call `Initialize(level, auctionControls)` once, then call `Open()` and `Close()` from the host. Keep the scene's EventSystem active.

## Validation and handoff

Run the Edit Mode test suite in Unity Test Runner. The new `PreparationPhaseTests` fixture checks the article keys, sections, and catalog mappings. Its Play Mode portion loads the existing demo and configures Preparation on the temporary scene instance. It checks the exact player-facing article copy across lots, different level amounts, one startup, modal raycast blocking, focus restoration, timer continuity, and live AI bidding and lot transitions while the overlay is open.

The final scene needs the Inspector wiring above. To make that scene the application's entry point, enable it in Build Settings and set the build scene order when the team integrates its scene flow.

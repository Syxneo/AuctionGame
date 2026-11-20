# Preparation and newspaper integration

## Project audit

This audit describes the project before scene integration. `AuctionDemo` now uses Preparation and the existing SALE Report.

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

## Inspector setup in AuctionDemo

1. Open `Assets/Scenes/AuctionDemo.unity`. Its scene-local EventSystem serves the auction, Preparation, and Report controls.
2. On **AuctionSystem**, **Auction Bootstrap** is disabled. **Preparation Phase** references the existing **Auction Manager** and `Assets/AuctionMvp/MvpAuctionLevel.asset`.
3. **Player Economy > Starting Budget Level** references the same level asset. Preparation shows the level's **1000** budget and **300** profit target. The **Sale Brief** uses `{0}` for the formatted budget and `{1}` for the formatted target.
4. **Preparation Phase > Auction Controls** references the **Canvas Group** on **AuctionPanel**. Preparation builds its briefing and newspaper UI at runtime on an overlay Canvas with sorting order **100**. The view reads all six articles from the level asset.
5. Press Play. Open and close the newspaper in Preparation, then start the auction. Use **Newspaper** in the lower-right corner during the run. Scroll through the edition using the mouse wheel or drag. **Close Newspaper** stays visible while scrolling.

The existing manager and AI continue updating underneath the full-screen newspaper. The overlay blocks pointer raycasts to the auction Canvas and moves keyboard or gamepad focus to **Close Newspaper**. At auction completion the component closes the overlay and hides the opener.

To use an existing auction button, wire its **On Click** to `PreparationPhase.OpenNewspaper()`. A host can also use `CloseNewspaper()`. To reuse the view independently, put `NewspaperView` on a dedicated full-stretch RectTransform under a topmost Canvas with a GraphicRaycaster. Call `Initialize(level, auctionControls)` once, then call `Open()` and `Close()` from the host. Keep the scene's EventSystem active.

## Validation and handoff

Run the Edit Mode suite in Unity Test Runner. Its scene tests enter Play Mode and load the checked-in `AuctionDemo` wiring. They cover the approved article copy, a single start, modal raycast blocking, timer continuity, AI bidding, lot transitions, and opening the newspaper in the last three seconds. They also exercise Game Over, the existing Retry into the same six-lot run with reset budgets, and Success with itemized profit.

Build Settings still open the template scene. Open `AuctionDemo` directly to play the integrated flow.

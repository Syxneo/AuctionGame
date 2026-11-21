# NEWSSSS preparation in AuctionDemo

Open `Assets/Scenes/AuctionDemo.unity` and press Play. The SALE brief reads the configured budget and profit target from the MVP level. Select **Read NEWSSSS**, advance each broadcast with **tap to continue**, then select **Start Auction**. On GAME OVER, **Retry** resets the run and returns to the SALE briefing.

## Inspector configuration

- **Assets/AuctionMvp/MvpAuctionLevel.asset → Newspaper Entries** controls presentation order. Each row has an **Item Type**, **Headline**, and **Body Text**. Keep clues about the item type and its distinguishing features. The list is authored, so adding a lot does not generate a broadcast. Incomplete rows are skipped, and an empty edition still allows the auction to start.
- **Auction Item Type → Newspaper Sprite** supplies an optional full-color illustration. Without one, NEWSSSS independently chooses a valid sprite from that type's Item Database variants once per run and tints the Unity UI Image black. The UI tint preserves source alpha. If no image is available, the page displays **Image unavailable** and logs a warning.
- **AuctionDemo → AuctionSystem → Preparation Phase → News Appearance** controls the reporter and paper sprites, fonts, palette, and continue prompt. Assign the approved **Reporter Sprite** when it is available. The current scene uses a labeled placeholder.
- **AuctionDemo → AuctionSystem → Auction Report UI → Preparation** points to the scene's Preparation Phase. This lets Retry call the manager's level reset and wait for the player to finish Preparation before the auction starts.

NEWSSSS uses the existing Preparation Canvas and scene EventSystem. Longer articles can be scrolled within the text area while the continue button stays in place. The Preparation Canvas is disabled during the auction.

## Validation

Run the Edit Mode suite in Unity Test Runner. Preparation tests load the checked-in AuctionDemo in Play Mode. The suite also exercises the auction, AI, report, and economy.

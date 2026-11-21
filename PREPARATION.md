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

## Five-day campaign in AuctionIntegrated

Open `Assets/Scenes/AuctionIntegrated.unity` and press Play. Each day starts at the safe-area day indicator and goes through manual SALE pickup and NEWSSSS. The campaign entries on `Assets/AuctionMvp/MvpAuctionLevel.asset` hold the day title, focus, narrative tone, economy, lot limits, and optional opening SALE lines. Dialogue supports `{budget}` and `{profit}`. The phone call includes an economy briefing when the configured lines omit it. A maximum of zero leaves the count bounded by the catalog and daily value limit. The generation-attempt limit applies independently to each day.

The player starts each attempt with the configured budget. When bidding starts, AI wallets are rebuilt from each personality's base budget times the day multiplier, rounded to whole dollars with midpoint ties away from zero. The theoretical profit check uses the real $10 minimum opening bid and selects the highest positive gains the player can afford at that price.

Retry keeps the accepted plan object and NEWSSSS edition. Each run clears purchases and results. Campaign progress is saved locally in PlayerPrefs under `AuctionGame.Campaign.v1`, including the current and highest unlocked days, completion, and the accepted seed and ordered lot IDs. Reopening an unfinished day starts it from the phone call with fresh budgets and reconstructs the saved plan and deterministic news from authored assets. If an earlier day was already won before quitting, the next launch opens the newly unlocked day. If Day 5 was won, the next launch shows Campaign Complete. An invalid saved plan or configuration stops preparation with a diagnostic. The Inspector's fixed seed option uses an isolated session with no campaign save.

`PreparationPhase.OnCampaignCompletePresented` is the hook for a future ending when the completion screen opens.

### Architecture audit

| Area | Existing implementation |
| --- | --- |
| Plan ownership | `PreparationPhase.Start` called `MvpAuctionLevel.CreateDailyRun` before the intro and cached the resulting `DailyRunPlan`. The plan owns its seed, ordered lots and family articles. Retry reused this object. |
| Economy | `MvpAuctionLevel.asset` supplied the player budget through `PlayerEconomy` and the report target through `AuctionReportUI`. `AuctionManager.StartRun` built `AIBidder` wallets from `AIPersonality.StartingBudget`. |
| Report and transitions | `AuctionReport` totals player purchase gains and losses and compares them with the target. Continue previously displayed a static level completion view. Retry called `AuctionManager.ResetLevel`, whose Idle event reopened the intro through Preparation. |
| Presentation | `GameIntroFlow` cached the level reference for budget and target substitution. `BroadcastEdition` selected deterministic articles from the plan and bidder pool, and `BreakingNewsView` presented them. The legacy Preparation path uses `NewspaperView`. |
| Auction handoff | Start Auction previously gave `AuctionManager.StartDailyAuction` the plan and full item catalog. The manager cached both for its run. The result cinematic gates advancement before the final item triggers `OnAuctionPhaseFinished`. SALE and the in-auction bubble are hosted by the existing presentation components. |
| Progression foundation | The existing Idle event, wallet reset, accepted plan and report callbacks support an in-scene transition. Campaign progression now provides the day selection and save state behind those entry points. |

### Implementation files

| Area | Files |
| --- | --- |
| Campaign data and generation | `MvpAuctionLevel.asset`, `MvpAuctionLevel.cs`, `CampaignDay.cs`, `CampaignProgress.cs`, `DailyItemGenerator.cs` |
| Runtime economy | `AuctionManager.cs`, `PlayerEconomy.cs`, `AIBidder.cs` |
| Integrated flow | `AuctionIntegrated.unity`, `PreparationPhase.cs`, `GameIntroFlow.cs`, `AuctionReportUI.cs` |
| Coverage | `CampaignTests.cs`, `DailyItemGenerationTests.cs`, `GameIntroTestDriver.cs` |
| New Unity metadata | `CampaignDay.cs.meta`, `CampaignProgress.cs.meta`, `CampaignTests.cs.meta` |

### Verification

Unity 6000.3.22f1 passed all 17 targeted tests. The integrated checks covered Day 1 failure and deterministic Retry, Day 1 success and single-step Continue, and the Day 5 SOLD cinematic and Campaign Complete.

The 100 seeds per day all passed on the first attempt. Observed lot counts by day were 3–4, 4–6, 4–8, 7–15 and 10–17. A separate test forced a rejected candidate, confirmed deterministic acceptance on attempt two and verified that exhausting the attempt limit preserves progress.

The single full Edit Mode run passed 120 of 122 tests. A focused run against the original baseline reproduced both failures. The HUD test expects the Current Bid object to be absent, and the Hall debug dialogue test expects three bidder bindings where the scene supplies five.

Physical-device portrait safe-area fit, long purchase-list scrolling and audio presentation remain manual QA.

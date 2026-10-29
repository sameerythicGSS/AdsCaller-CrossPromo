# Fullscreen ad controls and timer reels

Implemented in both Unity 6 and Unity 2022 cross-promo projects. The exported setup instructions are in the [integration guide](../../Assets/AdsCaller/SalonAds/_Docs/Integration-Guide-CrossPromo-AdsCaller.md#fullscreen-controls-and-rolling-timers); implementation details are in the [developer guide](../../Assets/AdsCaller/SalonAds/_Docs/Developer-Guide.md#fullscreen-controls-and-timer-implementation).

## Usage

On SalonInterstitialAd and OutSalonRewardedAd, enable `showStoreArrowBeforeClose` to show an external-link arrow when the existing timer/reward requirement completes. The default is false. Configure the portrait and landscape prefab instances as needed.

The existing close button's hit area is reused. Its icon changes to an arrow; tapping it restores the close icon before invoking the existing ad-click/store callback. The large central ad button retains its store action. Same-frame duplicate arrow clicks are ignored. No additional title, game icon, store URL, sprite, button reference, or prefab wiring is required.

The timer automatically creates masked digit reels from the existing Text component. Only changed digits roll, using unscaled time over 0.2 seconds. Reward in and the seconds suffix stay steady; completion rolls into Reward unlocked. These visuals do not drive eligibility, closing, or success callbacks.

## Failure handling

Both formats handle preparation errors/timeouts. Rewarded ads also detect stalled playback and streams without a duration/completion signal. Displayed failures bypass the arrow and expose Close; rejected setup can dismiss invalid UI immediately. Interstitial video closing follows its independent close countdown. Unknown-duration rewarded videos use `unknownVideoDurationTimeout` (120 seconds by default) and fail without granting a reward when that limit expires. Existing known-duration reward timing is preserved.

A missing close reference gets a runtime fallback control. A failed store callback leaves Close visible. Ad reuse resets the arrow, timer and failure state. Intro animations are cancelled on disable/replacement/failure. Reward callbacks are consumed once, including external dismissal. Rewarded closing restores the prior time scale and audio volume. Interstitial closing does the same when `revertStatesOnClose` is true (the default); false sets both to 1.

## Validation

Run the matching installed Unity editor with:

    Unity.exe -batchmode -projectPath <project> -executeMethod SalonFullscreenAdRegression.Run -logFile <log-path>

Do not add -quit: the suite exits after its play-mode checks finish. Reports and rendered prefab PNGs are written to Validation/FullscreenAds. The suite uses synthetic media and does not open a real store or make video network requests. Injected failure log entries are expected.

The latest run passed 14 regression groups in each version: both orientation prefabs with the option on/off; repeated clicks; exact-once reward callbacks; state restoration; failures and reuse; external dismissal; preparation timeout; playback stall; unknown duration; missing close reference; store callback failure; and timer animation/completion. Real device store round-trips and network-video decoding still require a device smoke test.

## Export coverage

CrossPromo and Combined exports include the updated controllers, `SalonAdCloseControl.cs`, `SalonAdTimerReel.cs`, their metadata, prefabs, and `_Docs`. Main-only exports exclude these CrossPromo files. The regression runner under `SalonAds/Tests` and this `Validation/FullscreenAds` directory are not exported. Generate fresh packages to include the current changes.

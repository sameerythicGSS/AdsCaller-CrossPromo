# CrossPromo v1.8 Developer Guide

## Ownership boundary

CrossPromo code must depend on shared Core contracts, never on the concrete Network `AdsCaller` class. Network reward selection and fallback belong to AdsMediator.

CrossPromo provider entry points:

- `AdsCallerSalon`
- `CatalogLoader`
- `AdUIFactory`
- `AdsConfiguration`
- `GameCatalog` / `GameAdEntry`
- CrossPromo UI treatments

## Initialization states

AdsCallerSalon registers with `AdsProviderRegistry` while enabled. Registration means the component exists; it does not mean creatives are ready.

Readiness requires:

1. AdsConfiguration exists and permits initialization.
2. Addressable UI prefabs loaded.
3. CatalogLoader published at least one eligible campaign.
4. The requested UI body exists.
5. No conflicting CrossPromo fullscreen request is active.

Report a detailed state/reason through `IAdsAvailabilityProvider`; do not return a generic false when the cause is knowable.

## Result contract

Interstitial and rewarded requests implement `IResultOutAdsProvider`.

- Invoke the result exactly once.
- Use `NotReady` when no creative can start.
- Use `Failed` when preparation/display fails.
- Use `Closed` after an interstitial was visible and then dismissed.
- Use `Completed` after reward completion.
- Never request provider fallback directly; return the outcome to AdsMediator.

## Catalog development

Remote JSON parsing must tolerate missing optional media fields and preserve configured local fallbacks. Validate all remote URLs and use HTTPS where possible.

When adding fields:

1. Update remote DTO parsing.
2. Update `GameAdEntry`.
3. Define local fallback behavior.
4. Add orientation/platform coverage.
5. Update safe-list cleanup if storage naming changes.
6. Document the schema change in the changelog.

## Media caching

Image cache paths must use sanitized game names. Video filenames must retain an unambiguous package prefix and URL-derived identity.

Do not delete cached media until the new runtime catalog has been accepted. The cleanup safe list must include active remote campaigns and protected local fallback campaigns.

## Addressables

Addressable keys used by AdUIFactory must remain stable across upgrades. Before release:

- Build Addressables for every target platform.
- Verify portrait/landscape variants.
- Exercise clean-install and content-update flows.
- Confirm handles are released on provider destruction.
- Test missing-key diagnostics.

## Presentation safety

Keep fullscreen request flags set during media preparation. Before presentation, abort invalid setup and complete the result path. Once the UI is displayed, a video failure must expose a working Close control directly; dismissal then completes the pending request. Clear request state on completion and early-exit paths, and never leave the user waiting on an animation to exit.

Banner/pop-ad cancellation token sources must be cancelled, disposed, and replaced cleanly.

## Fullscreen controls and timer implementation

The controllers retain their existing setup method signatures and serialized media/button references. The former public `showStoreArrowBeforeClose` prefab field has been replaced by two settings on AdsConfiguration:

- `AdsConfiguration.cs`: `interstitialShowStoreArrowBeforeClose` and `rewardedShowStoreArrowBeforeClose`, both false by default.
- `SalonInterstitialAd.cs`: interstitial presentation and close timer; snapshots the interstitial configuration value at setup.
- `SalonRewardedAd.cs`: `OutSalonRewardedAd`, reward eligibility, completion/failure callbacks, and video timeouts; snapshots the rewarded configuration value at setup.
- `SalonAdCloseControl.cs`: shared control state and runtime arrow geometry. It reuses the close button's hit area and restores its original icon. If the close reference is missing, it creates a fallback button. It creates no new store destination.
- `SalonAdTimerReel.cs`: a component attached automatically to the existing `UnityEngine.UI.Text`. It renders masked per-digit reels and a completion message while retaining the source Text value for inspection. Its labels do not intercept raycasts.

`AdsCallerSalon` passes its assigned `config` to all four image/video setup paths on every presentation, including cached UI reuse. The controllers store only a private, non-serialized snapshot so a configuration edit cannot change an ad already on screen. Factory and orientation-prefab selection remain unchanged.

Custom code that calls UI components directly can use the configuration-aware overloads:

```csharp
interstitial.SetupImage(sprite, config);
interstitial.SetupVideo(clip, url, config);
rewarded.SetupImageAd(sprite, onSuccess, onFail, config);
rewarded.SetupVideoAd(clip, onSuccess, onFail, url, config);
```

Existing setup overloads still work and default to direct Close when no configuration is supplied. Move custom code that assigned the former public `showStoreArrowBeforeClose` field to the matching AdsConfiguration field. Re-enable any previously customized prefab arrow choice on the configuration asset; the old per-prefab value is no longer used.

Keep close/reward state independent of animation. `SalonAdCloseControl.HandleArrowClick` commits the Close state before invoking the controller's existing store-click handler. A thrown callback is logged and leaves Close available. Duplicate arrow clicks in the same frame are consumed, so they cannot also dismiss the ad. Normal central-ad clicks continue through their original handlers.

`SalonAdTimerReel` uses `Time.unscaledDeltaTime` for a 0.2-second transition. Repeated updates with the same displayed value do not restart it. Only changed digits roll; interrupted transitions settle to the latest value. Reward completion rolls the countdown into **Reward unlocked** without adding a wait to the reward/close path. The same helper implementations work in both Unity versions.

The Unity 6 controllers retain Awaitables and cancellation tokens. The Unity 2022 controllers use coroutines; video tracking is yielded within its parent routine so stopping preparation also stops tracking. Cancel intro animations on disable, replacement, and failure. Reset timer, arrow, and failure state before reusing a presentation.

For rewarded ads, guard completion against an already failed or inactive presentation. Clear success/failure delegates before invoking the selected callback. External disable resolves a pending reward request once according to whether eligibility was reached. An arrow/store click must not invoke the success callback.

Preparation timeouts apply to both formats. Rewarded ads additionally check playback stalls and apply `unknownVideoDurationTimeout` to unknown-duration streams. Interstitial video closing follows the independent `closeButtonDelay`. Failure handling must make the root visible and expose Close directly so cancelling an intro cannot leave the exit control transparent. Invalid setup may close the UI immediately instead.

Rewarded dismissal restores the captured time scale and audio volume. Interstitial dismissal restores them when `revertStatesOnClose` is true (the default); false retains the existing behavior of setting both to 1.

## Fullscreen regression checks

The source projects contain `Assets/AdsCaller/SalonAds/Tests/Editor/SalonFullscreenAdRegression.cs`. Run it with the matching Unity editor while the project is not already open:

```text
Unity.exe -batchmode -projectPath "<project>" -executeMethod SalonFullscreenAdRegression.Run -logFile "<log-path>"
```

Do not pass `-quit`; the suite enters Play mode and exits after its checks. It writes `results.txt` and rendered prefab PNGs under `Validation/FullscreenAds`. Injected failure messages are expected. The runner uses synthetic media and does not open a real store or request a network video.

The recorded runs passed 16 regression groups in Unity 6.0.79f1 and Unity 2022.3.62f3. Coverage includes both orientations with the arrow on/off, duplicate clicks, one-time reward callbacks, state restoration, failure/reuse, external dismissal, preparation timeout, playback stall, unknown duration, missing close references, store callback exceptions, and digit/completion animation. The additional configuration checks cover all combinations of independent toggles, changes between cached UI presentations, stable settings during an active ad, and defaults when no configuration is supplied.

The runner and validation output are source-project tools and are excluded from package exports. Device store round-trips and real network-video decoding remain device smoke checks; the automated results do not establish those behaviors.

## Combined testing

Test these routes explicitly:

- Network interstitial → CrossPromo fallback on no fill.
- CrossPromo banner → Network fallback before catalog readiness.
- Network rewarded → CrossPromo fallback before presentation.
- CrossPromo fullscreen active → second request rejected as busy.
- CrossPromo-only installation with no Network types.
- Main-only installation with no CrossPromo types.

Also test local-only catalog operation, malformed remote JSON, empty mapping, offline startup, corrupted cache, unavailable stream, and removed ads.

## Logging

Use:

- `[AdsCaller/CrossPromo]` for provider preparation/presentation failures.
- `[CatalogLoader]` for catalog and cache operations.
- `[AdsMediator]` for provider routing/fallback.
- `[AdsDiagnostics]` for snapshots.

A warning should say that the ad did not appear and provide the reason a developer can act on.


## Build Automator 2.0

Open **Build Automator > Dashboard** for build presets, readiness checks, guided USB/Android
wireless/Fire TV setup, device nicknames, multiple-device deployment, Install Last Build,
build history, and app-log/storage diagnostics. Failed builds preserve previous artifacts
and restore version settings. ADB commands support cancellation/timeouts and verified results.
No game scripts or reporting package are required by the standalone Editor tool.

See the [Build Automator guide](../../../Editor/BuildAutomator/README.md) for installation, troubleshooting, preset sharing,
CI environment variables, and verification. Distribute the complete tool folder and its
canonical metadata; the AdsCaller package exporter includes it automatically.

## Build Automator 2.1 — Shared build location and devices

- In **Build Automator > Dashboard > Build**, use **Build location — shared across projects > Browse...**
  or enter an absolute folder path. The selected folder is reused by every updated project
  under the same OS account. A preset can explicitly override it under Advanced.
- Discovered and manually added devices, nicknames, and connection aliases are shared.
  Existing project device lists migrate on first dashboard open. Duplicate endpoints merge;
  USB/Wi-Fi aliases merge when a usable hardware serial verifies they are the same device.
- Output and ADB preferences refresh in other idle dashboards automatically. Shared saves
  use a lock, merge the latest state, and retain a backup.
- Target selections, presets, build history, and signing remain project-specific. An IP
  address changing to a different known device clears its selection for safety.

On Windows, shared data lives at **%LOCALAPPDATA%/OutTools/BuildAutomator/settings.json**.
It is not included in package exports. See the [Build Automator guide](../../../Editor/BuildAutomator/README.md) for migration,
output precedence, connection setup, storage, and troubleshooting.
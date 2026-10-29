# AdsCaller CrossPromo v1.8 Integration Guide

CrossPromo v1.8 is an Addressables-driven provider for the AdsCaller Core. It can run standalone or alongside the Main Network provider without either optional package referencing the other.

## 1. What is new in v1.8

- Core API version 2 compatibility.
- Readiness and structured result reporting.
- Safe provider fallback through AdsMediator.
- Shared fullscreen locking with Network ads.
- Clear explanations when the catalog, UI, or creative is not ready.
- Updated combined routing defaults.
- Structured lifecycle analytics support.
- Same-release Core validation during package export.
- Existing parameterless `AdsMediator.Instance.ShowInterstitial()` calls remain valid.

See [CHANGELOG.md](CHANGELOG.md) for the complete release notes and [Developer-Guide.md](Developer-Guide.md) for implementation details.

## 2. Requirements

CrossPromo requires:

- `com.unity.addressables`
- `com.unity.nuget.newtonsoft-json`
- AdsCaller Core v1.8 / `AdsCoreApi.Version == 2`

For legacy `.unitypackage` distribution, install Addressables and Newtonsoft JSON before importing CrossPromo. If imported first, install the missing packages and let Unity complete its compilation/domain reload before running setup.

Main v1.8 and CrossPromo v1.8 must come from the same release. Shared Core files intentionally have identical contents and Unity GUIDs.

## 3. Supported installations

| Installation | Standard formats | Rewarded |
| --- | --- | --- |
| CrossPromo standalone | CrossPromo | CrossPromo |
| Main standalone | Network | Network |
| Combined v1.8 default | CrossPromo banner/MREC; Network interstitial | Network |
| Combined fallback | Other provider when configured and ready | Other provider before presentation |

Fallback never occurs after a fullscreen creative has opened.

## 4. Installation

1. Install Addressables and Newtonsoft JSON.
2. Import CrossPromo v1.8.
3. Open **Ads Caller > Setup Wizard**.
4. Select an enabled boot scene.
5. Enable **Include Cross Promotion Addon**.
6. Optionally enable **Include Main Ads Caller** when its real provider is installed.
7. Select **Execute Setup**.
8. Assign or verify the catalog/configuration references on the injected prefabs.
9. Build Addressables content for the intended platform.

Setup injects:

- AdsMediator
- FbAnalytics
- SEHandler
- AdsCallerSalon
- CatalogLoader
- AdsCaller when Main is selected

It also creates Addressables settings and the `Salon_AdTypes` group when required.

Do not run setup repeatedly on the same configured scene unless duplicate injection is intentional.

## 5. Combined v1.8 routing

For a newly configured combined scene, AdsMediator enables advanced routing:

| Format | Primary |
| --- | --- |
| Banner | CrossPromo |
| Large banner/MREC | CrossPromo |
| Interstitial | Network |
| Rewarded | Network |

`allowProviderFallback` is enabled. An existing scene retains its serialized values; enable `useAdvancedRouting` manually if upgrading in place.

## 6. Configuration

### AdsConfiguration

Controls:

- Provider initialization.
- Banner and pop-ad cycling.
- Sequential/random campaign selection.
- Image/video selection.
- Independent interstitial/rewarded store-arrow settings, shared across orientations.
- Banner and large-banner positions.
- Show/click analytics formats.
- Removed-ads preference key.

### CatalogLoader

Controls:

- Local fallback catalog.
- Remote catalog/mapping endpoints.
- Platform and orientation filtering.
- Video mode: preload, lazy load, or stream.
- Remote-versus-local video priority.
- Local media fallback behavior.

The current application identifier must match the package mapping in the remote catalog.

### Fullscreen controls and rolling timers

Select the CrossPromo `AdsConfiguration` asset assigned to `AdsCallerSalon.config` (the supplied asset is `Assets/AdsCaller/SalonAds/AdsConfiguration.asset`). Under **Fullscreen Ads**, set:

- **Interstitial Show Store Arrow Before Close** (`interstitialShowStoreArrowBeforeClose`).
- **Rewarded Show Store Arrow Before Close** (`rewardedShowStoreArrowBeforeClose`).

Both default to `false`. Each setting applies to image and video ads in both portrait and landscape. The provider reads the current configuration at each presentation; changes affect the next ad, while the active ad keeps its initial value. The arrow setting is no longer exposed on individual ad-body components. These settings apply only to the CrossPromo provider.

The remaining timing and visual settings stay on the prefabs under `Assets/AdsCaller/SalonAds/Prefabs/AdBodies`:

| Prefab | Component |
| --- | --- |
| `Interstitial.prefab` | `SalonInterstitialAd` |
| `Interstitial_landscape.prefab` | `SalonInterstitialAd` |
| `Rewarded.prefab` | `OutSalonRewardedAd` (script file: `SalonRewardedAd.cs`) |
| `Rewarded_landscape.prefab` | `OutSalonRewardedAd` (script file: `SalonRewardedAd.cs`) |

Configure the two arrow toggles once in the assigned configuration asset; no per-orientation arrow setup is required. Include the updated configuration in your next application build. Rebuild Addressables content when changing ad-body prefab timing or visuals; moving these toggles does not require prefab rewiring.

- **Disabled:** the existing countdown/reward requirement completes, then Close appears.
- **Enabled:** completion exposes an external-link arrow in the existing close-button hit area. Tapping it reveals Close immediately, then invokes the existing store-click handler. Returning from the store leaves Close available. The arrow click itself does not dismiss the ad or grant a reward.
- The large central ad button continues to open the store. It does not consume the optional arrow step. No separate View Game button, game title, game icon, store URL, or arrow reference is required.
- The existing timer Text automatically gets masked digit reels. Changed digits roll upward over 0.2 seconds; unchanged digits and the rewarded prefix/suffix remain steady. Completion changes the rewarded message to **Reward unlocked**. The visuals use unscaled time and never delay close availability or reward eligibility.

| Setting | Owner | Code default | Purpose |
| --- | --- | --- | --- |
| `interstitialShowStoreArrowBeforeClose` | AdsConfiguration | `false` | Store-arrow step for interstitials in either orientation |
| `rewardedShowStoreArrowBeforeClose` | AdsConfiguration | `false` | Store-arrow step for rewarded ads in either orientation |
| `closeButtonDelay` | Interstitial | 5 seconds | Delay before arrow/Close; for video, starts after preparation and the Play call |
| `imageRewardDuration` | Rewarded | 5 seconds | Image-view duration before reward eligibility |
| `videoPreparationTimeout` | Both | 15 seconds | Maximum wait for video preparation |
| `videoStallTimeout` | Rewarded | 15 seconds | Maximum time without playback progress |
| `unknownVideoDurationTimeout` | Rewarded | 120 seconds | Fail a stream that has no known duration and has not completed |

Saved asset values control the arrow defaults; saved prefab values control the timing defaults. Existing rewarded video timing is preserved: known videos up to 10 seconds use their full duration; videos longer than 10 seconds use 30% of their duration. The success callback runs once on dismissal after eligibility; opening the store is not a reward condition.

Video failure paths bypass the optional arrow. An already displayed failed ad exposes Close directly; setup rejection can instead dismiss the invalid UI immediately. Rewarded playback failures do not grant a reward. Interstitials retain their independent close countdown rather than a rewarded-style playback-progress requirement. See [Developer-Guide.md](Developer-Guide.md#fullscreen-controls-and-timer-implementation) for lifecycle and validation details.

## 7. Public API

Use AdsMediator from gameplay code:

```csharp
AdsMediator.Instance.ShowInterstitial();

AdsMediator.Instance.ShowBanner();
AdsMediator.Instance.ShowBanner(EnumsJar_Salon.AdPosition.BottomCenter);
AdsMediator.Instance.HideBanner();

AdsMediator.Instance.ShowLargeBanner();
AdsMediator.Instance.HideLargeBanner();

AdsMediator.Instance.ShowRewardedAd(
    success: () => GrantReward(),
    failure: () => RestoreRewardButton());
```

The interstitial call requires no action parameter. AdsMediator handles CrossPromo outcomes internally for the fullscreen lock, fallback, pacing, lifecycle analytics, and diagnostics.

Pop-ad slots remain CrossPromo-specific and are managed by the CrossPromo presentation components.

## 8. Readiness and fallback

CrossPromo reports why it cannot currently show a format. Common states include:

- Addressable UI prefabs still loading.
- Catalog not ready or empty.
- AdsConfiguration missing or initialization disabled.
- No eligible image or video creative.
- Video cache/stream not ready.
- Another CrossPromo fullscreen request active.
- Ads removed.

AdsMediator may try Network when fallback is enabled and the failure happened before presentation.

## 9. Caching behavior

CatalogLoader applies the local catalog first, then attempts remote synchronization. It downloads missing images, optionally preloads videos, and preserves local fallbacks based on configuration.

Cached images are stored below `Application.persistentDataPath/AdCache`. Cached videos use package/ad-type/hash-based filenames. Orphan cleanup removes media not represented by the active remote or protected local catalogs.

## 10. Troubleshooting

| Symptom | Likely reason | Resolution |
| --- | --- | --- |
| Addressables/JSON compiler errors | Dependency missing | Install both required UPM packages |
| Provider absent warning | AdsCallerSalon prefab missing/disabled | Inject or enable the real provider prefab |
| Catalog loading state | Remote mapping pending and no usable local campaigns | Verify local catalog and remote URLs |
| No eligible creative | Campaign lacks the configured media type | Add media or change the selected media type |
| Video not ready | Lazy download/stream unavailable | Check connectivity, URL conversion, and cache mode |
| Network unexpectedly selected | CrossPromo was not ready and fallback is enabled | Inspect `[AdsMediator]` warning and diagnostics |
| Duplicate canvases/providers | Setup executed more than once | Remove duplicate scene objects |

Filter Console/Logcat using `[AdsCaller/CrossPromo]`, `[AdsMediator]`, or `[AdsDiagnostics]`.


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
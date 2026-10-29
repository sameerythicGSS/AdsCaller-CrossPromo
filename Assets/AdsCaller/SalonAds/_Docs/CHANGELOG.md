# CrossPromo Changelog

> Ads Caller UPM release: use [Ads Caller installation and migration](../../AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. Any older unitypackage instructions below apply only to legacy distribution.


## 2026-09-03 — Padded double-chevron store button

- Replaced the diagonal store arrow with a centered double chevron (`»`) in both interstitial and rewarded ads.
- Reduced the symbol size and stroke thickness to give it padding inside the existing button, in portrait and landscape.
- Kept the original hit area, configuration toggles, store action, and close/reward flow. The icon uses UI geometry and needs no font or sprite asset.

## 2026-09-03 — Arrow settings moved to AdsConfiguration

- Moved arrow configuration to **Fullscreen Ads** on the assigned CrossPromo AdsConfiguration asset: `interstitialShowStoreArrowBeforeClose` and `rewardedShowStoreArrowBeforeClose`. Both default to false.
- Removed the per-prefab Inspector toggle. Each format now shares its setting across portrait/landscape and image/video presentations. Previously customized prefab choices should be set on the configuration asset.
- AdsCallerSalon forwards the configuration on every setup call. Controllers snapshot it for the active ad; later configuration changes apply on the next presentation, including reused UI.
- Preserved existing setup method signatures with direct-close defaults, and added configuration-aware overloads for custom callers.
- Updated guides and added regression coverage for independent toggles, cached UI reuse, configuration snapshots, and missing-configuration defaults. Exporter coverage already includes the configuration script and asset.
- Verified 16 passing regression groups in each Unity version. Unity 6 used an isolated copy with identical runtime/test inputs while the source editor remained open.

## 2026-09-03 — Fullscreen controls, timer reels, and export documentation

- Added optional `showStoreArrowBeforeClose` to interstitial and rewarded components; the default is false. An external-link arrow reuses the close hit area and reveals Close before opening the store through the existing click handler.
- Preserved the large central ad button as the store link. No separate title, icon, View Game button, or prefab wiring is required.
- Added `SalonAdCloseControl` and `SalonAdTimerReel`: masked rolling digits, unscaled 0.2-second transitions, and the **Reward unlocked** message. Animation never delays close availability or reward eligibility.
- Hardened intro cancellation, reuse, video failure recovery, missing-close fallback, and same-frame arrow-click handling. Reward callbacks are consumed once, including external dismissal; failed ads reject late reward completion.
- Added the rewarded `unknownVideoDurationTimeout` safety limit (120 seconds by default), alongside preparation and playback-stall timeouts. Failure paths bypass the arrow. Existing known-duration reward timing remains unchanged.
- Recorded 14 passing fullscreen regression groups in each source project, using Unity 6.0.79f1 and Unity 2022.3.62f3. Real-device store return and network-video decoding remain smoke checks.
- Confirmed that CrossPromo and Combined exports include the two helpers, updated controllers, their metadata, and `_Docs` recursively. Main-only exports omit CrossPromo runtime code. Source-only regression tests and validation output are excluded.

See the [integration guide](Integration-Guide-CrossPromo-AdsCaller.md#fullscreen-controls-and-rolling-timers) for prefab settings and the [developer guide](Developer-Guide.md#fullscreen-controls-and-timer-implementation) for lifecycle details. This update does not change Core API version 2.



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

## Build Automator 2.0.1 — Keystore validation fix

- Resolve keystores through Unity instead of assuming relative paths are project-relative.
- Show configured and resolved paths in Advanced; report actual missing files with their resolved path.
- Keep ambiguous relative paths non-blocking if Unity's resolver is unavailable; final signing validation remains with Unity.
- Preserve existing signing/profile settings and passwords. Verified on Unity 2022.3 and Unity 6.



## Build Automator 2.0

Open **Build Automator > Dashboard** for build presets, readiness checks, guided USB/Android
wireless/Fire TV setup, device nicknames, multiple-device deployment, Install Last Build,
build history, and app-log/storage diagnostics. Failed builds preserve previous artifacts
and restore version settings. ADB commands support cancellation/timeouts and verified results.
No game scripts or reporting package are required by the standalone Editor tool.

See the [Build Automator guide](../../../Editor/BuildAutomator/README.md) for installation, troubleshooting, preset sharing,
CI environment variables, and verification. Distribute the complete tool folder and its
canonical metadata; the AdsCaller package exporter includes it automatically.

## v1.8 — Core API v2 and combined reliability

### Added

- `IAdsAvailabilityProvider` implementation for catalog/UI/readiness reporting.
- `IResultOutAdsProvider` implementation for interstitial and rewarded outcomes.
- Shared fullscreen coordination through AdsMediator.
- Safe automatic fallback to Network before presentation.
- Detailed CrossPromo failure reasons.
- Structured lifecycle analytics compatibility.
- Updated standalone and combined documentation.

### Changed

- Combined default routing uses CrossPromo for banner/MREC and Network for interstitial/rewarded.
- Parameterless `AdsMediator.Instance.ShowInterstitial()` remains the recommended API.
- Interstitial image/video failures now complete the result path rather than silently returning.
- Rewarded completion/failure is forwarded as a structured result.
- Package export requires matching Core API version 2.

### Compatibility

- Import CrossPromo v1.8 with Main v1.8 only.
- Existing scenes retain serialized AdsMediator settings.
- Addressables and Newtonsoft JSON remain required.
- Legacy `IOutAdsProvider` gameplay calls remain compatible.

## v1.6 — Previously documented baseline

- Automated setup wizard.
- Scriptable AdsConfiguration.
- Addressable AdUIFactory.
- Remote catalog loading and local fallback.
- Differential media caching and cleanup.
- Banner, interstitial, rewarded, MREC, and pop-ad treatments.
- Build Automator integration.

Version 1.8 supersedes the older v1.6 integration documentation in this package.


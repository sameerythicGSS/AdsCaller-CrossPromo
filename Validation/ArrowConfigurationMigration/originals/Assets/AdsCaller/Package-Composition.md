# AdsCaller package composition

The AdsCaller runtime is composed from one shared Core plus zero, one, or both provider modules.

Current shared Core API: **Version 2**. Version 2 adds optional readiness/result capabilities,
hybrid per-format routing, provider fallback, a shared fullscreen lock, pacing, diagnostics,
structured lifecycle analytics, and a network consent/runtime gate. Legacy `IOutAdsProvider`
calls remain source-compatible.

| Installation | Required contents |
| --- | --- |
| Main AdsCaller | Core + Network |
| Cross-Promo standalone | Core + CrossPromo |
| Main with Cross-Promo | Core + Network + CrossPromo |

## Core ownership

The following assets are Core. They must be exported from the same source revision, with identical contents and identical `.meta` GUIDs in every distribution:

- `Scripts/Core/AdsMediator.cs`
- `Scripts/Core/IOutAdsProvider.cs`
- `Scripts/Core/AdsProviderRegistry.cs`
- `Scripts/Core/AdsAnalytics.cs`
- `Scripts/Data/EnumsJar_Salon.cs`
- `Scripts/Editor/AdsCallerSetup.cs`
- `Scripts/Editor/AdsCallerPackageExporter.cs`
- `Scripts/Firebase/FbAnalytics.cs`
- `Scripts/General/Logger.cs`
- `Prefabs/AdsMediator.prefab`
- `Prefabs/FbAnalytics.prefab`
- `MMPHandler/SEHandler.cs`
- `MMPHandler/SEHandler.prefab`
- `Assets/Editor/OutBuildAutomator.cs` (legacy entry-point shim)

Do not edit separate copies of these assets. Apply a Core change to both package source projects before exporting either distribution.

## Provider ownership

Network-only assets include `AdsCaller`, `AdsCallerConfig`, the network prefab, MAX/AdMob integration, credentials editor, network demo assets, and the v1.8 documentation under `Assets/AdsCaller/Docs`.

CrossPromo-only assets include `Assets/AddressableAssetsData`, `AdsCallerSalon`, `AdsConfiguration`, catalogs, caching, Addressables UI, cross-promo prefabs, cross-promo demo assets, and the provider documentation under `Assets/AdsCaller/SalonAds/_Docs`.

The main distribution must not contain a placeholder `AdsCallerSalon` class. Missing providers are handled by `AdsProviderRegistry`.

## Export rules

- Main package: export Core and Network assets. Exclude the `SalonAds` provider implementation.
- CrossPromo package: export Core, CrossPromo assets, and `Assets/AddressableAssetsData`. Exclude `AdsCaller.cs`, `AdsCallerConfig.cs`, the network prefab, MAX, and AdMob.
- Combined package: export Core once, followed by both provider asset sets.
- When importing separate Main and CrossPromo packages, use packages from the same release. Their Core files share GUIDs and must have the same `AdsCoreApi.Version`.
- Never regenerate `.meta` files for Core assets between exports.
- CrossPromo requires Addressables and Newtonsoft JSON. Install those dependencies before importing a legacy `.unitypackage`, or declare them as package dependencies when distributing through UPM.

## Upgrade note

Older main packages included a fake `Assets/AdsCaller/SalonAds/Scripts/Core/AdsCallerSalon.cs`. Remove that legacy file before upgrading an already-installed project. New main packages must not export it.

The setup wizard discovers optional provider assets and opens the network credentials window through reflection, so it compiles when either provider is absent.

For combined installs, the setup wizard enables advanced routing with CrossPromo banners/MREC,
Network interstitial/rewarded, and fallback to the other provider when the selected provider is
absent or not ready. Runtime warnings use the `[AdsMediator]`, `[AdsCaller/Network]`, and
`[AdsCaller/CrossPromo]` prefixes. MAX no-fill warnings are always emitted even when test logging is disabled.

## Package Exporter

Open **Ads Caller > Package Exporter** in Unity. Select an output folder, enter the release version, and export Main, CrossPromo, Combined, or all three packages.

Exporting all three requires the real Network and CrossPromo providers to be installed together in the current Unity project. The exporter validates every required asset and canonical Core GUID before writing a package. It also offers switches for demo assets and bundled MAX/External Dependency Manager assets.

### Fullscreen UI files and documentation

In both cross-promo source projects, `CrossPromoAssets` includes `Assets/AdsCaller/SalonAds/Scripts`, `Prefabs`, and `_Docs`. The exporter passes `ExportPackageOptions.Recurse`, so CrossPromo and Combined packages automatically include:

- `Scripts/AdTypes/SalonInterstitialAd.cs` and `SalonRewardedAd.cs` with their current changes.
- `Scripts/AdTypes/SalonAdCloseControl.cs` and `SalonAdTimerReel.cs`.
- The scripts' Unity metadata and the portrait/landscape ad-body prefabs.
- The integration guide, developer guide, behavior guide, and changelog under `_Docs`.

These runtime files are included whether or not **Include demo scenes and sample scripts** is enabled. They are omitted from Main-only exports. `Assets/AdsCaller/SalonAds/Tests` and `Validation/FullscreenAds` are outside the exported roots, so the regression runner, logs, screenshots, and source-only validation notes are not bundled.

No exporter list change is needed for these helpers. Export fresh packages after updating code or docs; existing `.unitypackage` files do not update automatically. Use the source project matching the target Unity version. Rebuild Addressables content after modifying the ad-body prefab settings.

## Build Automator 2.0

All Main, CrossPromo, and Combined distributions include the complete
`Assets/Editor/BuildAutomator` folder and `Assets/Editor/OutBuildAutomator.cs` shim.
Every tool source, README, package manifest, assembly definition, and `.meta` GUID is shared
across the Unity 6 and Unity 2022 source projects. The exporter validates the canonical GUIDs.
Do not export only the legacy shim; it depends on the new standalone Editor assembly.

See the [Build Automator guide](../Editor/BuildAutomator/README.md) for setup, USB/wireless/Fire TV
connections, presets, safe builds, deployment, diagnostics, standalone/UPM installation, and CI.
The standalone export includes only the tool folder. No AdsCaller/reporting package is required.
Version 2.1 stores the chosen build folder, devices, nicknames, and ADB preferences in the OS
account's local application-data folder, shared across all projects using the updated tool.
Presets remain in ProjectSettings; device selections and history remain in UserSettings.
Neither machine/local configuration nor signing passwords are bundled into package exports.
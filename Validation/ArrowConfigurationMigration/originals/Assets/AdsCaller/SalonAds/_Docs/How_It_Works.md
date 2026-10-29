# How AdsCaller CrossPromo v1.8 Works

## Composition

CrossPromo is an optional provider around a shared AdsCaller Core. Core owns routing, provider discovery, analytics contracts, fullscreen coordination, logging, setup, and export. CrossPromo owns campaigns, remote catalog loading, media caching, Addressable UI, and presentation.

This division allows three reliable compositions:

```text
Core + Network
Core + CrossPromo
Core + Network + CrossPromo
```

Both v1.8 packages carry Core API version 2 with the same Unity GUIDs. Core is imported once logically even when packages are imported separately.

## Request flow

A gameplay call remains simple:

```csharp
AdsMediator.Instance.ShowInterstitial();
```

AdsMediator then:

1. Checks removed-ads and pacing policy.
2. Selects the configured provider for the format.
3. Checks whether that provider is registered and ready.
4. Selects the fallback provider when allowed.
5. Acquires the shared fullscreen lock.
6. Requests the ad.
7. Releases the lock when it closes, completes, or fails.
8. Records lifecycle analytics and a clear failure reason.

CrossPromo implements optional readiness and result capabilities while preserving the legacy provider interface.

## Catalog pipeline

CatalogLoader starts with the local fallback catalog so initialization does not depend entirely on a remote request. It then:

1. Downloads the package-to-campaign mapping.
2. Matches `Application.identifier`.
3. Downloads and parses eligible campaign rows.
4. Filters by platform and orientation.
5. Converts supported Google Drive links to direct URLs.
6. Loads cached images or downloads missing images.
7. Applies local media fallbacks where configured.
8. Publishes the runtime catalog to AdsCallerSalon.
9. Starts video preloading when selected.
10. Cleans orphaned cached media.

If remote synchronization fails, usable local campaigns remain the recovery path.

## Addressable UI lifecycle

AdUIFactory loads the banner, interstitial, rewarded, and large-banner bodies asynchronously. AdsCallerSalon reports `Loading` until both UI and catalog data are usable.

UI is created only when required by the provider and is kept separate from campaign data. This avoids hard-wiring scene canvases to remote media.

## Creative selection

Each format maintains its own campaign and media indices. Sequential mode advances predictably; random mode selects a campaign at runtime. When a selected campaign lacks a creative, the provider searches other eligible campaigns before failing.

For video, CatalogLoader can:

- Preload on launch.
- Download lazily when requested.
- Stream directly.

The readiness/result path reports a failure back to AdsMediator when no playable creative can be produced.

## Fullscreen presentation

The large central ad button remains the store link. The ad image/video supplies the promoted game's identity; the fullscreen controls do not require separate title or icon data.

Both controllers expose `showStoreArrowBeforeClose`, defaulting to false. With it disabled, the existing timer/reward requirement leads directly to Close. With it enabled:

1. The interstitial close timer or rewarded eligibility requirement completes.
2. An external-link arrow appears in the existing close-button area.
3. Tapping the arrow changes it to Close before invoking the existing store-click callback.
4. The ad remains open, ready to close when the user returns. A failed store callback also leaves Close available.

The central-ad store action does not advance the arrow state. The arrow state resets for each new presentation. On video failure, the UI skips the arrow and exposes Close directly, or dismisses immediately if setup was rejected.

Timer text uses a clipped vertical reel for each changing digit. Unchanged digits and the rewarded prefix/suffix remain still. Transitions take 0.2 seconds using unscaled time, so they continue while gameplay is paused. The visual layer never controls eligibility or postpones dismissal.

## Rewarded behavior

Rewarded image/video UI tracks eligibility separately from dismissal. Image ads use `imageRewardDuration`. Known videos up to 10 seconds use their full duration; longer videos retain the existing 30% watch-time rule. Unknown-duration videos can complete through the player completion event, but preparation/stall/unknown-duration timeouts prevent indefinite waiting.

At eligibility, the timer rolls into **Reward unlocked** and the configured arrow or Close becomes available. Opening the store does not grant a reward. On dismissal, the success callback is forwarded through AdsMediator once if eligibility was reached; otherwise the failure path runs. External disable also resolves a pending callback once. Failed presentations reject late completion signals.

A fallback provider can only be selected before the CrossPromo rewarded creative opens.

## Banner and pop-ad cycling

Banner and pop-ad routines use cancellation tokens and Unity Awaitables. They can start, stop, and refresh without blocking the main thread. Position-aware banner requests use `IPositionedOutAdsProvider`.

## Analytics

CrossPromo emits existing formatted impression/click events and participates in v1.8 structured lifecycle analytics. Firebase and SolarEngine remain optional subscribers to the Core analytics bus.

## Diagnostics

CrossPromo logs why UI did not appear, including missing configuration, unfinished Addressable loading, empty catalog, busy fullscreen state, or missing creative. AdsMediator separately logs routing and fallback decisions.

Use:

```csharp
AdsMediator.Instance.LogDiagnostics();
```

to print registered providers, readiness, removed-ads state, network gate state, and current fullscreen lock.


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
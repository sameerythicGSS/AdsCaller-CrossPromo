using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Core system for managing and presenting custom cross-promotional ads.
/// Handles dynamic Addressable loading, cycling routines, and runtime analytics tracking.
/// </summary>
[RequireComponent(typeof(AdUIFactory))]
public class AdsCallerSalon : MonoBehaviour, IOutAdsProvider, IPositionedOutAdsProvider, IAdsAvailabilityProvider, IResultOutAdsProvider, IManagedFullscreenProvider, IRewardedUnavailableFeedback
{
    private AdsShowSession fullscreenSession;

    public static AdsCallerSalon Instance;

    #region Configuration
    [Header("Scriptable Configuration")]
    public AdsConfiguration config;
    #endregion

    #region Store Platform Detection
    private static EnumsJar_Salon.PlatformType? detectedStorePlatform;

    /// <summary>
    /// Resolves the store from the runtime device so projects do not need a manual platform setting.
    /// Fire OS is Android at Unity's API level, so Amazon devices require an additional device check.
    /// </summary>
    public static EnumsJar_Salon.PlatformType DetectedStorePlatform
    {
        get
        {
            if (!detectedStorePlatform.HasValue)
                detectedStorePlatform = DetectStorePlatform();

            return detectedStorePlatform.Value;
        }
    }

    private static EnumsJar_Salon.PlatformType DetectStorePlatform()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return EnumsJar_Salon.PlatformType.iOS;
#elif UNITY_ANDROID && !UNITY_EDITOR
        return IsFireOsDevice()
            ? EnumsJar_Salon.PlatformType.Amazon
            : EnumsJar_Salon.PlatformType.Android;
#else
        if (Application.platform == RuntimePlatform.IPhonePlayer)
            return EnumsJar_Salon.PlatformType.iOS;

        return EnumsJar_Salon.PlatformType.Android;
#endif
    }

    private static bool IsFireOsDevice()
    {
#if UNITY_ANDROID
        try
        {
            using (AndroidJavaClass build = new AndroidJavaClass("android.os.Build"))
            {
                string manufacturer = build.GetStatic<string>("MANUFACTURER");
                string brand = build.GetStatic<string>("BRAND");
                string fingerprint = build.GetStatic<string>("FINGERPRINT");
                string display = build.GetStatic<string>("DISPLAY");

                return ContainsIgnoreCase(manufacturer, "Amazon") ||
                       ContainsIgnoreCase(brand, "Amazon") ||
                       ContainsIgnoreCase(fingerprint, "Amazon") ||
                       ContainsIgnoreCase(display, "Fire OS");
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"Unable to inspect Android device details for Fire OS: {exception.Message}. Falling back to Google Play.");
        }
#endif
        return false;
    }

    private static bool ContainsIgnoreCase(string value, string marker)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    #endregion

    #region Runtime Data & Callbacks
    private CancellationTokenSource popAdCTS;
    private CancellationTokenSource bannerCTS;
    private bool interstitialVideoRequestInProgress;
    private bool rewardedVideoRequestInProgress;
    private Action<AdShowOutcome> pendingInterstitialResult;

    private List<GameAdEntry> runtimeCatalog;

    [HideInInspector] public bool isInitialized = false;
    public Action OnAdsCallerInitialized;

    [HideInInspector] public bool bannerCycleActive = false;
    [HideInInspector] public bool popAdCycleActive = false;
    [HideInInspector] public bool ArePrefabsLoaded { get; private set; } = false;

    [HideInInspector] public int currentBannerIndex = -1;
    [HideInInspector] public int currentInterstitialIndex = -1;
    [HideInInspector] public int currentRewardedIndex = -1;
    [HideInInspector] public int currentLargeBannerIndex = -1;
    [HideInInspector] public int currentPopAdIndex = -1;

    // Trackers for per-game image indices
    private int[] gameBannerImageIndices;
    private int[] gameInterstitialImageIndices;
    private int[] gameInterstitialVideoIndices;
    private int[] gameRewardedImageIndices;
    private int[] gameRewardedVideoIndices;
    private int[] gameLargeBannerImageIndices;
    private int[] gamePopAdImageIndices;

    private Dictionary<int, int> activePopAdSlots = new Dictionary<int, int>();

    public Action<int> OnBannerClicked;
    public Action<int> OnInterstitialClicked;
    public Action<int> OnRewardedClicked;
    public Action<int> OnLargeBannerClicked;
    public Action<int> OnPopAdClicked;

    public Action OnInterstitialOpened;
    public Action OnInterstitialClosed;
    public Action<int, int, Sprite> OnPopAdRefreshed;

    public bool IsBannerAdShowing => bannerAdUI != null && bannerAdUI.gameObject.activeSelf;
    public bool IsLargeBannerAdShowing => largeBannerAdUI != null && largeBannerAdUI.gameObject.activeSelf;
    public bool IsRewardedAdShowing => rewardedAdUI != null && rewardedAdUI.gameObject.activeInHierarchy;
    public bool IsInterstitialShowing => interstitialAdUI != null && interstitialAdUI.gameObject.activeInHierarchy;
    public bool AreAdsRemoved => config != null && PlayerPrefs.GetInt(config.removeAdsPlayerPrefKey, 0) == 1;
    public bool CanShowPopAds => config != null && config.canShowPopAd && !AreAdsRemoved;
    public bool IsInitialized => isInitialized;
    public event Action<AdAvailability> AvailabilityChanged;

    private AdUIFactory uiFactory;

    private SalonBannerAd bannerAdUI;
    private SalonInterstitialAd interstitialAdUI;
    private OutSalonRewardedAd rewardedAdUI;
    private SalonLargeBannerAd largeBannerAdUI;
    private SalonPopAd popAdUI;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (TryGetComponent<AdUIFactory>(out AdUIFactory factoryScript)) uiFactory = factoryScript;
            else uiFactory = gameObject.AddComponent<AdUIFactory>();

            _ = LoadAdPrefabsAsync();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        if (Instance == this)
            AdsProviderRegistry.Register(AdsProviderKind.CrossPromo, this);

        OnBannerClicked += HandleBannerClicked;
        OnInterstitialClicked += HandleInterstitialClicked;
        OnRewardedClicked += HandleRewardedClicked;
        OnLargeBannerClicked += HandleLargeBannerClicked;
        OnPopAdClicked += HandlePopAdClicked;
    }

    private void OnDisable()
    {
        AdsProviderRegistry.Unregister(AdsProviderKind.CrossPromo, this);

        OnBannerClicked -= HandleBannerClicked;
        OnInterstitialClicked -= HandleInterstitialClicked;
        OnRewardedClicked -= HandleRewardedClicked;
        OnLargeBannerClicked -= HandleLargeBannerClicked;
        OnPopAdClicked -= HandlePopAdClicked;
    }

    private void OnDestroy()
    {
        fullscreenSession?.Complete(AdShowResult.Disabled, "CrossPromo provider destroyed");
        fullscreenSession = null;
        AdsProviderRegistry.Unregister(AdsProviderKind.CrossPromo, this);

        if (Instance == this)
        {
            Instance = null;
        }

        OnBannerClicked -= HandleBannerClicked;
        OnInterstitialClicked -= HandleInterstitialClicked;
        OnRewardedClicked -= HandleRewardedClicked;
        OnLargeBannerClicked -= HandleLargeBannerClicked;
        OnPopAdClicked -= HandlePopAdClicked;

        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = null;

        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = null;
    }
    #endregion

    #region Event Handlers
    private void HandleBannerClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Banner);
    private void HandleInterstitialClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Inter);
    private void HandleRewardedClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Rewarded);
    private void HandleLargeBannerClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.LBanner);
    private void HandlePopAdClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.PopAd);
    #endregion

    #region Initialization
    /// <summary>
    /// Loads UI prefabs asynchronously using Addressables.
    /// </summary>
    private async Awaitable LoadAdPrefabsAsync()
    {
        try
        {
            bannerAdUI = await uiFactory.CreateBannerAsync(transform);
            if (bannerAdUI != null && bannerAdUI.snapContainer != null && config != null)
            {
                SnapToPosition(bannerAdUI.snapContainer, config.bannerPosition);
                bannerAdUI.bannerImage.color = Color.black;
            }
            else this.Error("Failed to load or configure Banner prefab.");

            interstitialAdUI = await uiFactory.CreateInterstitialAsync(transform);
            if (interstitialAdUI == null) this.Error("Failed to load Interstitial prefab.");

            rewardedAdUI = await uiFactory.CreateRewardedAsync(transform);
            if (rewardedAdUI == null) this.Error("Failed to load Rewarded prefab.");

            largeBannerAdUI = await uiFactory.CreateLargeBannerAsync(transform);
            if (largeBannerAdUI != null && largeBannerAdUI.snapContainer != null && config != null)
            {
                SnapToPosition(largeBannerAdUI.snapContainer, config.largeBannerPosition);
            }
            else this.Error("Failed to load or configure Large Banner prefab.");

            if (bannerAdUI != null && interstitialAdUI != null && rewardedAdUI != null && largeBannerAdUI != null)
            {
                ArePrefabsLoaded = true;
                Logger.Note("AdsCaller_Salon prefabs loaded successfully via Factory.");
                CheckAndInitialize();
            }
        }
        catch (System.Exception ex)
        {
            this.Error($"[AdsCaller_Salon] Fatal error during Factory LoadAdPrefabsAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Validates system readiness across prefabs, catalog data, and configurations before initialization.
    /// </summary>
    public void CheckAndInitialize()
    {
        if (config == null || !config.canInitialize) return;

        if (!ArePrefabsLoaded)
        {
            Logger.Note("AdsCaller_Salon waiting for prefabs to load...");
            return;
        }

        if (CatalogLoader.Instance == null || !CatalogLoader.Instance.IsCatalogReady)
        {
            Logger.Note("AdsCaller_Salon waiting for CatalogLoader...");
            return;
        }

        SetRuntimeCatalog(CatalogLoader.Instance.runtimeCatalog);
        InitializeSystem();
    }

    /// <summary>
    /// Prepares tracking arrays, aligns active pop ad slots, and kicks off initial cycling routines.
    /// </summary>
    public void InitializeSystem()
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0)
        {
            Logger.Warn("Runtime catalog is empty. Aborting AdsCaller initialization.");
            return;
        }

        isInitialized = true;
        NotifyAllAvailability();

        int gameCount = runtimeCatalog.Count;
        gameBannerImageIndices = new int[gameCount];
        gameInterstitialImageIndices = new int[gameCount];
        gameInterstitialVideoIndices = new int[gameCount];
        gameLargeBannerImageIndices = new int[gameCount];
        gamePopAdImageIndices = new int[gameCount];
        gameRewardedImageIndices = new int[gameCount];
        gameRewardedVideoIndices = new int[gameCount];

        // Selection methods advance before reading, so -1 makes the first sequential ad use index 0.
        currentBannerIndex = -1;
        currentInterstitialIndex = -1;
        currentRewardedIndex = -1;
        currentLargeBannerIndex = -1;
        currentPopAdIndex = -1;

        List<int> existingSlots = new List<int>(activePopAdSlots.Keys);
        foreach (int slot in existingSlots)
        {
            activePopAdSlots[slot] = 0;
            RefreshPopAdSlot(slot);
        }

        OnAdsCallerInitialized?.Invoke();

        if (config.canShowBanner && config.showBannerOnStart && !AreAdsRemoved)
        {
            if (bannerAdUI != null)
                bannerAdUI.gameObject.SetActive(true);

            bannerCycleActive = true;
            StartBannerCycle();
        }

        if (CanShowPopAds && config.startPopAdCycleOnStart)
        {
            popAdCycleActive = true;
            StartPopAdCycle();
        }
    }

    public void SetRuntimeCatalog(List<GameAdEntry> catalog)
    {
        runtimeCatalog = catalog;
    }
    #endregion

    #region Pop Ad Logic
    public void StartPopAdCycle()
    {
        if (!CanShowPopAds) return;
        Logger.Note("Initializing Pop Ad cycle...");

        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = new CancellationTokenSource();

        _ = StartPopAdCycling(popAdCTS.Token);
    }

    private async Awaitable StartPopAdCycling(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Awaitable.WaitForSecondsAsync(config.popAdCycleSpeed, token);

                if (token.IsCancellationRequested) break;

                CycleAllPopAds();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Note("Pop Ad cycle was successfully cancelled.");
        }
    }

    private void CycleAllPopAds()
    {
        if (!CanShowPopAds)
        {
            // Let registered UI slots hide before stopping the cycle.
            foreach (int slotID in new List<int>(activePopAdSlots.Keys))
                OnPopAdRefreshed?.Invoke(slotID, -1, null);
            StopPopAdCycle();
            return;
        }
        if (runtimeCatalog == null || runtimeCatalog.Count == 0) return;

        Logger.Note("Cycling all active Pop Ads.");
        List<int> currentSlots = new List<int>(activePopAdSlots.Keys);

        foreach (int slotID in currentSlots)
        {
            int currentGame = activePopAdSlots[slotID];
            int nextGame = config.popAdSequence == EnumsJar_Salon.AdSequenceType.Sequential
                ? (currentGame + 1) % runtimeCatalog.Count
                : UnityEngine.Random.Range(0, runtimeCatalog.Count);

            activePopAdSlots[slotID] = nextGame;
            RefreshPopAdSlot(slotID);
        }
    }

    public void RegisterPopAdSlot(int slotID)
    {
        if (!CanShowPopAds) return;
        if (runtimeCatalog == null || runtimeCatalog.Count == 0) return;

        if (!activePopAdSlots.ContainsKey(slotID))
        {
            int startIndex = slotID % runtimeCatalog.Count;
            activePopAdSlots.Add(slotID, startIndex);
        }

        RefreshPopAdSlot(slotID);
    }

    public void UnregisterPopAdSlot(int slotID)
    {
        if (activePopAdSlots.ContainsKey(slotID))
        {
            activePopAdSlots.Remove(slotID);
        }
    }

    private void RefreshPopAdSlot(int slotID)
    {
        if (!CanShowPopAds) return;
        if (!activePopAdSlots.ContainsKey(slotID)) return;

        int proposedGameIndex = activePopAdSlots[slotID];
        Sprite spriteToShow = null;
        int finalGameIndex = proposedGameIndex;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (proposedGameIndex + i) % totalGames;
            spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.PopAd);

            if (spriteToShow != null)
            {
                finalGameIndex = checkIndex;
                break;
            }
        }

        if (spriteToShow != null)
        {
            activePopAdSlots[slotID] = finalGameIndex;
            OnPopAdRefreshed?.Invoke(slotID, finalGameIndex, spriteToShow);

            GameAdEntry entry = runtimeCatalog[finalGameIndex];
            string safeGameName = entry.gameName.ToLower().Trim().Replace(" ", "_");
            string eventMsg = string.Format(config.showEventFormat, "popad", safeGameName);

            AdsAnalytics.LogEvent(eventMsg);
        }
        else
        {
            Logger.Warn($"Missing Pop Ad image for game: {runtimeCatalog[finalGameIndex].gameName}");
        }
    }

    public void StopPopAdCycle()
    {
        popAdCycleActive = false;
        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = null;
        Logger.Note("Pop ad cycle stopped.");
    }
    #endregion

    #region Banner Logic
    public void StartBannerCycle()
    {
        if (config == null || !config.canShowBanner || AreAdsRemoved) return;

        Logger.Note("Initializing banner ad cycle...");

        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = new CancellationTokenSource();

        _ = StartBannerCycling(bannerCTS.Token);
    }

    private async Awaitable StartBannerCycling(CancellationToken token)
    {
        Logger.Note("Starting banner ad cycle...");

        try
        {
            while (!token.IsCancellationRequested)
            {
                ChangeBannerAd();
                await Awaitable.WaitForSecondsAsync(config.bannerCycleSpeed, token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Note("Banner Ad cycle was successfully cancelled.");
        }
    }

    public void ChangeBannerAd()
    {
        if (config == null || !config.canShowBanner || AreAdsRemoved)
        {
            HideBanner();
            return;
        }

        if (bannerAdUI == null || runtimeCatalog.Count == 0) return;

        int proposedIndex = config.bannerSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentBannerIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        Sprite spriteToShow = null;
        int finalIndex = proposedIndex;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (proposedIndex + i) % totalGames;
            spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.Banner);

            if (spriteToShow != null)
            {
                finalIndex = checkIndex;
                break;
            }
        }

        if (spriteToShow != null)
        {
            currentBannerIndex = finalIndex;

            if (!bannerAdUI.gameObject.activeInHierarchy)
            {
                bannerAdUI.gameObject.SetActive(true);
            }

            _ = bannerAdUI.UpdateBannerVisualsAsync(spriteToShow);

            Logger.Note($"Banner ad changed to: {runtimeCatalog[currentBannerIndex].gameName}");

            GameAdEntry entry = runtimeCatalog[currentBannerIndex];
            string gameName = entry.gameName.ToLower().Trim().Replace(" ", "_");
            string eventMsg = string.Format(config.showEventFormat, "banner", gameName);

            AdsAnalytics.LogEvent(eventMsg);
        }
        else
        {
            Logger.Warn("No Banner images found in the ENTIRE catalog. Hiding banner.");
            HideBanner();
        }
    }

    public void ShowBanner()
    {
        if (config == null || !config.canShowBanner || AreAdsRemoved) return;

        if (!bannerCycleActive) StartBannerCycle();

        if (bannerAdUI != null)
            bannerAdUI.gameObject.SetActive(true);
        else this.Error("No Banner UI instance found while trying to use it");
    }

    public void ShowBanner(EnumsJar_Salon.AdPosition adPos)
    {
        if (config == null || !config.canShowBanner || AreAdsRemoved) return;

        if (!bannerCycleActive) StartBannerCycle();

        if (bannerAdUI != null)
        {
            bannerAdUI.gameObject.SetActive(true);
            SnapToPosition(bannerAdUI.snapContainer, adPos);
        }
        else this.Error("No Banner UI instance found while trying to use it");
    }

    public void HideBanner()
    {
        if (bannerAdUI != null)
            bannerAdUI.gameObject.SetActive(false);
        else this.Error("No Banner UI instance found while trying to use it");

        StopBannerCycle();
    }

    public void StopBannerCycle()
    {
        bannerCycleActive = false;
        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = null;

        Logger.Note("Banner ad cycle stopped.");
    }
    #endregion

    #region Interstitial Logic
    public void ShowInterstitial()
    {
        ShowInterstitial(null);
    }

    public void ShowInterstitial(Action<AdShowOutcome> onResult)
    {
        if (!AdsShowSession.TryBegin(OutAdFormat.Interstitial, AdsProviderKind.CrossPromo, null, onResult, out var session)) return;
        fullscreenSession = session;
        try { ShowInterstitialInternal(outcome => { fullscreenSession = null; session.Complete(outcome.Result, outcome.Reason); }); }
        catch (Exception e) { fullscreenSession = null; session.Complete(AdShowResult.Failed, e.Message); }
    }

    private void ShowInterstitialInternal(Action<AdShowOutcome> onResult)
    {
        AdAvailability availability = GetAvailability(OutAdFormat.Interstitial);
        if (!availability.CanShow)
        {
            Logger.Warn($"[AdsCaller/CrossPromo] Interstitial did not appear. Reason: state={availability.State}; {availability.Reason}");
            onResult?.Invoke(new AdShowOutcome(availability.State == AdAvailabilityState.Disabled ? AdShowResult.Disabled : AdShowResult.NotReady, OutAdFormat.Interstitial, AdsProviderKind.CrossPromo, availability.Reason));
            return;
        }
        pendingInterstitialResult = onResult;

        int proposedIndex = config.interstitialSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentInterstitialIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        if (config.interstitialMediaType == EnumsJar_Salon.AdMediaType.Image)
        {
            Sprite spriteToShow = null;
            int finalIndex = proposedIndex;
            int totalGames = runtimeCatalog.Count;

            for (int i = 0; i < totalGames; i++)
            {
                int checkIndex = (proposedIndex + i) % totalGames;
                spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.Inter);

                if (spriteToShow != null)
                {
                    finalIndex = checkIndex;
                    break;
                }
            }

            if (spriteToShow != null)
            {
                currentInterstitialIndex = finalIndex;
                GameAdEntry entry = runtimeCatalog[currentInterstitialIndex];
                string gameName = entry.gameName.ToLower().Trim().Replace(" ", "_");

                OnInterstitialOpened?.Invoke();

                string eventMsg = string.Format(config.showEventFormat, "inter", gameName);
                AdsAnalytics.LogEvent(eventMsg);

                interstitialAdUI.gameObject.SetActive(true);
                interstitialAdUI.SetupImage(spriteToShow, config);
                _ = WaitAfterInter();
                Logger.Note($"Showing interstitial image ad for: {gameName}");
            }
            else
            {
                Logger.Warn("No Interstitial images found in the ENTIRE catalog.");
                CompleteInterstitial(AdShowResult.NotReady, "no interstitial images exist in the active catalog");
            }
        }
        else if (config.interstitialMediaType == EnumsJar_Salon.AdMediaType.Video)
        {
            if (TryGetVideoAdData(ref proposedIndex, false, out UnityEngine.Video.VideoClip localClip, out string remoteUrl))
            {
                currentInterstitialIndex = proposedIndex;
                GameAdEntry entry = runtimeCatalog[currentInterstitialIndex];

                interstitialVideoRequestInProgress = true;
                _ = ProcessAndShowInterstitialVideoAsync(entry, localClip, remoteUrl);
            }
            else
            {
                Logger.Warn("Interstitial video is not ready yet.");
                ShowToast("Video ads are currently loading. Please wait a moment.");
                CompleteInterstitial(AdShowResult.NotReady, "no cached or streamable interstitial video is ready");
            }
        }
    }

    private async Awaitable WaitAfterInter()
    {
        while (interstitialAdUI.gameObject.activeSelf)
        {
            await Awaitable.EndOfFrameAsync();
        }

        Logger.Note("Interstitial Ended! Ready to go...");
        CompleteInterstitial(AdShowResult.Closed, "user closed the CrossPromo interstitial");
    }
    #endregion

    #region Rewarded Logic
    public void ShowRewardedAd(Action onSuccess, Action onFail)
    {
        ShowRewardedAd(onSuccess, outcome => { if (outcome.Result != AdShowResult.Completed) onFail?.Invoke(); });
    }

    private void ShowRewardedInternal(Action onSuccess, Action onFail)
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0 || rewardedAdUI == null || IsRewardedAdShowing || IsInterstitialShowing || rewardedVideoRequestInProgress || interstitialVideoRequestInProgress)
        {
            Logger.Warn("Cannot show Rewarded Ad. Catalog empty or UI missing.");
            onFail?.Invoke();
            return;
        }

        int proposedIndex = config.rewardedSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentRewardedIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        if (config.rewardedMediaType == EnumsJar_Salon.AdMediaType.Image)
        {
            Sprite spriteToShow = null;
            int finalIndex = proposedIndex;
            int totalGames = runtimeCatalog.Count;

            for (int i = 0; i < totalGames; i++)
            {
                int checkIndex = (proposedIndex + i) % totalGames;
                spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.Rewarded);

                if (spriteToShow != null)
                {
                    finalIndex = checkIndex;
                    break;
                }
            }

            if (spriteToShow != null)
            {
                currentRewardedIndex = finalIndex;
                GameAdEntry entry = runtimeCatalog[currentRewardedIndex];
                string gameName = entry.gameName.ToLower().Trim().Replace(" ", "_");

                string eventMsg = string.Format(config.showEventFormat, "rewarded", gameName);

                AdsAnalytics.LogEvent(eventMsg);

                Logger.Note($"Showing rewarded image ad for: {gameName}");

                rewardedAdUI.gameObject.SetActive(true);
                rewardedAdUI.SetupImageAd(spriteToShow, onSuccess, onFail, config);
                if (rewardedAdUI.gameObject.activeInHierarchy) fullscreenSession?.Open();
            }
            else
            {
                this.Error("No Rewarded images found in the ENTIRE catalog. Ad failed.");
                onFail?.Invoke();
            }
        }
        else if (config.rewardedMediaType == EnumsJar_Salon.AdMediaType.Video)
        {
            if (TryGetVideoAdData(ref proposedIndex, true, out UnityEngine.Video.VideoClip localClip, out string remoteUrl))
            {
                currentRewardedIndex = proposedIndex;
                GameAdEntry entry = runtimeCatalog[currentRewardedIndex];

                rewardedVideoRequestInProgress = true;
                _ = ProcessAndShowRewardedVideoAsync(entry, localClip, remoteUrl, onSuccess, onFail);
            }
            else
            {
                this.Error("Rewarded video is not ready yet.");
                onFail?.Invoke();
            }
        }
    }

    public void ShowRewardedAd(Action onSuccess, Action<AdShowOutcome> onResult)
    {
        onResult = AdNotAvailablePopup.WithFeedback(onResult, ShowRewardedUnavailablePopup);
        var availability = GetAvailability(OutAdFormat.Rewarded);
        if (!availability.CanShow)
        {
            onResult?.Invoke(new AdShowOutcome(AdShowResult.NotReady, OutAdFormat.Rewarded, AdsProviderKind.CrossPromo, availability.Reason));
            return;
        }
        if (!AdsShowSession.TryBegin(OutAdFormat.Rewarded, AdsProviderKind.CrossPromo, onSuccess, onResult, out var session)) return;
        fullscreenSession = session;
        try
        {
            ShowRewardedInternal(
                () => { session.EarnReward(); fullscreenSession = null; session.Complete(AdShowResult.Completed); },
                () => { fullscreenSession = null; session.Complete(session.DidOpen ? AdShowResult.Closed : AdShowResult.NotReady, session.DidOpen ? "Rewarded closed without reward" : "Rewarded creative is unavailable"); });
        }
        catch (Exception e) { fullscreenSession = null; session.Complete(AdShowResult.Failed, e.Message); }
    }

    public void ShowRewardedUnavailablePopup() => AdNotAvailablePopup.Show(config != null && config.showAdNotAvailablePopup, config != null ? config.adNotAvailablePopup : null);

    public AdAvailability GetAvailability(OutAdFormat format)
    {
        if (config == null) return new AdAvailability(format, AdAvailabilityState.ProviderUnavailable, "AdsConfiguration is missing");
        if (!config.canInitialize) return new AdAvailability(format, AdAvailabilityState.Disabled, "CrossPromo initialization is disabled");
        if ((format == OutAdFormat.Banner && !config.canShowBanner) ||
            (format == OutAdFormat.LargeBanner && !config.canShowLargeBanner) ||
            (format == OutAdFormat.Interstitial && !config.canShowInterstitial))
            return new AdAvailability(format, AdAvailabilityState.Disabled, $"{format} ads are disabled in AdsConfiguration");
        if (!ArePrefabsLoaded) return new AdAvailability(format, AdAvailabilityState.Loading, "Addressable ad UI prefabs are still loading");
        if (!isInitialized || runtimeCatalog == null || runtimeCatalog.Count == 0)
            return new AdAvailability(format, AdAvailabilityState.Loading, "catalog is not ready or contains no eligible campaigns");
        if (format != OutAdFormat.Rewarded && AreAdsRemoved) return new AdAvailability(format, AdAvailabilityState.Disabled, "ads were removed by the user");
        if ((format == OutAdFormat.Interstitial || format == OutAdFormat.Rewarded) && (IsInterstitialShowing || IsRewardedAdShowing || interstitialVideoRequestInProgress || rewardedVideoRequestInProgress))
            return new AdAvailability(format, AdAvailabilityState.NotReady, "another CrossPromo fullscreen request is showing or loading");
        if (format == OutAdFormat.Banner && bannerAdUI == null) return new AdAvailability(format, AdAvailabilityState.NotReady, "banner UI is missing");
        if (format == OutAdFormat.LargeBanner && largeBannerAdUI == null) return new AdAvailability(format, AdAvailabilityState.NotReady, "large-banner UI is missing");
        if (format == OutAdFormat.Interstitial && interstitialAdUI == null) return new AdAvailability(format, AdAvailabilityState.NotReady, "interstitial UI is missing");
        if (format == OutAdFormat.Rewarded && rewardedAdUI == null) return new AdAvailability(format, AdAvailabilityState.NotReady, "rewarded UI is missing");
        return new AdAvailability(format, AdAvailabilityState.Ready);
    }

    private void CompleteInterstitial(AdShowResult result, string reason)
    {
        Action<AdShowOutcome> callback = pendingInterstitialResult;
        pendingInterstitialResult = null;
        callback?.Invoke(new AdShowOutcome(result, OutAdFormat.Interstitial, AdsProviderKind.CrossPromo, reason));
    }

    private void NotifyAllAvailability()
    {
        AvailabilityChanged?.Invoke(GetAvailability(OutAdFormat.Banner));
        AvailabilityChanged?.Invoke(GetAvailability(OutAdFormat.LargeBanner));
        AvailabilityChanged?.Invoke(GetAvailability(OutAdFormat.Interstitial));
        AvailabilityChanged?.Invoke(GetAvailability(OutAdFormat.Rewarded));
    }
    #endregion

    #region Large Banner Logic

    public void ShowLargeBanner()
    {
        if (config == null || !config.canShowLargeBanner || AreAdsRemoved) return;
        ShowLargeBanner(config.largeBannerPosition);
    }

    public void ShowLargeBanner(EnumsJar_Salon.AdPosition adPos)
    {
        if (config == null || !config.canShowLargeBanner || runtimeCatalog == null || runtimeCatalog.Count == 0 || largeBannerAdUI == null || AreAdsRemoved) return;

        int proposedIndex = config.largeBannerSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentLargeBannerIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        Sprite spriteToShow = null;
        int finalIndex = proposedIndex;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (proposedIndex + i) % totalGames;
            spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.LBanner);

            if (spriteToShow != null)
            {
                finalIndex = checkIndex;
                break;
            }
        }

        if (spriteToShow != null)
        {
            currentLargeBannerIndex = finalIndex;
            GameAdEntry entry = runtimeCatalog[currentLargeBannerIndex];
            string gameName = entry.gameName.ToLower().Trim().Replace(" ", "_");
            string eventMsg = string.Format(config.showEventFormat, "mrec", gameName);

            AdsAnalytics.LogEvent(eventMsg);

            largeBannerAdUI.image.sprite = spriteToShow;
            largeBannerAdUI.gameObject.SetActive(true);
            SnapToPosition(largeBannerAdUI.snapContainer, adPos);
        }
        else
        {
            Logger.Warn("No Large Banner images found in the ENTIRE catalog. Ad failed.");
            HideLargeBanner();
        }
    }

    public void HideLargeBanner()
    {
        _ = DelayHideLB();
    }

    public async Awaitable DelayHideLB()
    {
        while (largeBannerAdUI.gameObject.activeInHierarchy)
        {
            await Awaitable.EndOfFrameAsync();
            if (largeBannerAdUI != null && largeBannerAdUI.gameObject.activeInHierarchy)
            {
                largeBannerAdUI.gameObject.SetActive(false);
            }
        }
    }
    #endregion

    #region Redirection Logic
    private void ProcessAdClick(int index, EnumsJar_Salon.AdPlacementType adType)
    {
        if (runtimeCatalog == null || index < 0 || index >= runtimeCatalog.Count) return;

        GameAdEntry entry = runtimeCatalog[index];
        string gameName = entry.gameName.ToLower().Trim().Replace(" ", "_");
        string package = entry.gamePackageName;

        string prefKey = $"AdClicks_{package}_{adType}";

        int currentClicks = PlayerPrefs.GetInt(prefKey, 0);
        PlayerPrefs.SetInt(prefKey, currentClicks + 1);
        PlayerPrefs.Save();

        string eventMsg = string.Format(config.clickEventFormat, adType.ToString().ToLower(), gameName);

        AdsAnalytics.LogEvent(eventMsg, AdsAnalyticsDestination.Firebase);
        Logger.Note($"Recorded ad click event: {eventMsg}");

        if (DetectedStorePlatform == EnumsJar_Salon.PlatformType.Android)
        {
            Application.OpenURL($"market://details?id={package}");
        }
        else if (DetectedStorePlatform == EnumsJar_Salon.PlatformType.Amazon)
        {
            AdsAnalytics.LogEvent(eventMsg, AdsAnalyticsDestination.SolarEngine);

            Application.OpenURL($"amzn://apps/android?initiatePurchaseFlow=true&asin={entry.asin}");
        }
        else if (DetectedStorePlatform == EnumsJar_Salon.PlatformType.iOS)
        {
            Application.OpenURL($"https://apps.apple.com/app/id{package}");
        }
    }
    #endregion

    #region Utility/Helpers
        #region Toast Notification System

    /// <summary>
    /// Generates a temporary Android-style toast message on the screen.
    /// </summary>
    public void ShowToast(string message, float duration = 2.5f)
    {
        _ = ShowToastAsync(message, duration);
    }

    private async Awaitable ShowToastAsync(string message, float duration)
    {
        // Find the main canvas to attach the toast to
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // Create a basic background panel
        GameObject toastObj = new GameObject("ToastMessage");
        toastObj.transform.SetParent(canvas.transform, false);

        UnityEngine.UI.Image bgImage = toastObj.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform rect = toastObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, 100);
        rect.anchoredPosition = new Vector2(0, -300); // Position at bottom of screen

        // Create the text element
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(toastObj.transform, false);

        UnityEngine.UI.Text text = textObj.AddComponent<UnityEngine.UI.Text>();
        text.text = message;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        try
        {
            float remaining = Mathf.Max(0f, duration);
            while (remaining > 0f)
            {
                await Awaitable.NextFrameAsync(destroyCancellationToken);
                remaining -= Time.unscaledDeltaTime;
            }
        }
        catch (OperationCanceledException)
        {
            // The owner was destroyed.
        }

        if (toastObj != null) Destroy(toastObj);
    }

    #endregion
    private Sprite GetAdSprite(int gameIndex, EnumsJar_Salon.AdPlacementType placementType)
    {
        GameAdEntry entry = runtimeCatalog[gameIndex];
        List<Sprite> images = null;
        int currentIndex = 0;

        switch (placementType)
        {
            case EnumsJar_Salon.AdPlacementType.Banner:
                images = entry.gameBannerImages;
                currentIndex = gameBannerImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.Inter:
                images = entry.gameInterstitialImages;
                currentIndex = gameInterstitialImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.Rewarded:
                images = entry.gameRewardedImages;
                currentIndex = gameRewardedImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.LBanner:
                images = entry.gameLargeBannerImages;
                currentIndex = gameLargeBannerImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.PopAd:
                images = entry.gamePopAdImages;
                currentIndex = gamePopAdImageIndices[gameIndex];
                break;
        }

        if (images == null || images.Count == 0) return null;

        Sprite selectedSprite;

        if (entry.gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential)
        {
            selectedSprite = images[currentIndex];
            currentIndex = (currentIndex + 1) % images.Count;
        }
        else
        {
            int randomIndex = UnityEngine.Random.Range(0, images.Count);
            selectedSprite = images[randomIndex];
            currentIndex = randomIndex;
        }

        switch (placementType)
        {
            case EnumsJar_Salon.AdPlacementType.Banner:
                gameBannerImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.Inter:
                gameInterstitialImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.LBanner:
                gameLargeBannerImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.PopAd:
                gamePopAdImageIndices[gameIndex] = currentIndex;
                break;
        }

        return selectedSprite;
    }

    private bool TryGetVideoAdData(ref int gameIndex, bool isRewarded, out UnityEngine.Video.VideoClip localClip, out string remoteUrl)
    {
        localClip = null;
        remoteUrl = null;

        bool prioritizeRemote = true;
        if (CatalogLoader.Instance != null)
        {
            prioritizeRemote = isRewarded
                ? CatalogLoader.Instance.prioritizeRemoteRewarded
                : CatalogLoader.Instance.prioritizeRemoteInterstitial;
        }

        if (prioritizeRemote)
        {
            if (CheckRemoteVideos(ref gameIndex, isRewarded, out remoteUrl)) return true;
            if (CheckLocalVideos(ref gameIndex, isRewarded, out localClip)) return true;
        }
        else
        {
            if (CheckLocalVideos(ref gameIndex, isRewarded, out localClip)) return true;
            if (CheckRemoteVideos(ref gameIndex, isRewarded, out remoteUrl)) return true;
        }

        return false;
    }

    private async Awaitable ProcessAndShowInterstitialVideoAsync(GameAdEntry entry, UnityEngine.Video.VideoClip localClip, string remoteUrl)
    {
        try
        {
            string gameName = GetAnalyticsGameName(entry);
            string mediaUrlToPlay = null;

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                if (CatalogLoader.Instance != null && CatalogLoader.Instance.videoCacheMode == EnumsJar_Salon.VideoCacheMode.Stream)
                {
                    mediaUrlToPlay = OutMediaUrlHelper.GetDirectDriveUrl(remoteUrl.Trim());
                }
                else
                {
                    string localPath = await VideoCacher.GetOrDownloadVideoAsync(remoteUrl, entry.gamePackageName, "inter");
                    if (!string.IsNullOrEmpty(localPath)) mediaUrlToPlay = new Uri(localPath).AbsoluteUri;
                }
            }

            if (localClip == null && string.IsNullOrEmpty(mediaUrlToPlay))
            {
                Logger.Warn($"Interstitial video could not be loaded for: {gameName}");
                ShowToast("The video could not be loaded. Please try again.");
                CompleteInterstitial(AdShowResult.Failed, $"video creative could not be loaded for {gameName}");
                return;
            }

            if (config == null || !config.canShowInterstitial || AreAdsRemoved)
            {
                CompleteInterstitial(AdShowResult.Disabled, "interstitial ads were disabled while the video was loading");
                return;
            }

            interstitialAdUI.gameObject.SetActive(true);
            if (!interstitialAdUI.SetupVideo(localClip, mediaUrlToPlay, config))
            {
                interstitialAdUI.gameObject.SetActive(false);
                ShowToast("The video player is unavailable. Please try again.");
                CompleteInterstitial(AdShowResult.Failed, "video player rejected the CrossPromo creative");
                return;
            }

            OnInterstitialOpened?.Invoke();
            string eventMsg = string.Format(config.showEventFormat, "inter", gameName);
            AdsAnalytics.LogEvent(eventMsg);

            _ = WaitAfterInter();
            Logger.Note($"Showing interstitial video ad for: {gameName}");
        }
        catch (Exception exception)
        {
            Logger.Warn($"Failed to show interstitial video: {exception.Message}");
            if (interstitialAdUI != null && interstitialAdUI.gameObject.activeSelf)
                interstitialAdUI.gameObject.SetActive(false);
            ShowToast("The video could not be loaded. Please try again.");
            CompleteInterstitial(AdShowResult.Failed, exception.Message);
        }
        finally
        {
            interstitialVideoRequestInProgress = false;
        }
    }

    private async Awaitable ProcessAndShowRewardedVideoAsync(GameAdEntry entry, UnityEngine.Video.VideoClip localClip, string remoteUrl, Action onSuccess, Action onFail)
    {
        try
        {
            string gameName = GetAnalyticsGameName(entry);
            string mediaUrlToPlay = null;

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                if (CatalogLoader.Instance != null && CatalogLoader.Instance.videoCacheMode == EnumsJar_Salon.VideoCacheMode.Stream)
                {
                    mediaUrlToPlay = OutMediaUrlHelper.GetDirectDriveUrl(remoteUrl.Trim());
                }
                else
                {
                    string localPath = await VideoCacher.GetOrDownloadVideoAsync(remoteUrl, entry.gamePackageName, "rewarded");
                    if (!string.IsNullOrEmpty(localPath)) mediaUrlToPlay = new Uri(localPath).AbsoluteUri;
                }
            }

            if (localClip == null && string.IsNullOrEmpty(mediaUrlToPlay))
            {
                Logger.Warn($"Rewarded video could not be loaded for: {gameName}");
                onFail?.Invoke();
                return;
            }

            rewardedAdUI.gameObject.SetActive(true);
            if (!rewardedAdUI.SetupVideoAd(localClip, onSuccess, onFail, mediaUrlToPlay, config)) return;
            if (rewardedAdUI.gameObject.activeInHierarchy) fullscreenSession?.Open();

            string eventMsg = string.Format(config.showEventFormat, "rewarded", gameName);
            AdsAnalytics.LogEvent(eventMsg);

            Logger.Note($"Showing rewarded video ad for: {gameName}");
        }
        catch (Exception exception)
        {
            Logger.Warn($"Failed to show rewarded video: {exception.Message}");
            if (rewardedAdUI != null && rewardedAdUI.gameObject.activeSelf)
                rewardedAdUI.gameObject.SetActive(false);
            onFail?.Invoke();
        }
        finally
        {
            rewardedVideoRequestInProgress = false;
        }
    }

    private static string GetAnalyticsGameName(GameAdEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.gameName)
            ? "unknown_game"
            : entry.gameName.ToLowerInvariant().Trim().Replace(" ", "_");
    }

    private bool CheckRemoteVideos(ref int gameIndex, bool isRewarded, out string remoteUrl)
    {
        remoteUrl = null;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (gameIndex + i) % totalGames;
            var remoteUrls = isRewarded ? runtimeCatalog[checkIndex].remoteRewardedVideoURLs : runtimeCatalog[checkIndex].remoteInterstitialVideoURLs;

            if (remoteUrls != null && remoteUrls.Count > 0)
            {
                int count = remoteUrls.Count;
                int subIndex = isRewarded ? gameRewardedVideoIndices[checkIndex] : gameInterstitialVideoIndices[checkIndex];
                bool sequential = runtimeCatalog[checkIndex].gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential;

                if (!sequential)
                    subIndex = UnityEngine.Random.Range(0, count);
                else if (subIndex >= count)
                    subIndex = 0;

                for (int videoOffset = 0; videoOffset < count; videoOffset++)
                {
                    int candidateIndex = (subIndex + videoOffset) % count;
                    if (string.IsNullOrWhiteSpace(remoteUrls[candidateIndex])) continue;

                    gameIndex = checkIndex;
                    remoteUrl = remoteUrls[candidateIndex].Trim();
                    if (sequential)
                    {
                        if (isRewarded) gameRewardedVideoIndices[gameIndex] = (candidateIndex + 1) % count;
                        else gameInterstitialVideoIndices[gameIndex] = (candidateIndex + 1) % count;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private bool CheckLocalVideos(ref int gameIndex, bool isRewarded, out UnityEngine.Video.VideoClip localClip)
    {
        localClip = null;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (gameIndex + i) % totalGames;
            var entry = runtimeCatalog[checkIndex];

            var localVideos = isRewarded
                ? (entry.gameRewardedVideos != null ? new List<UnityEngine.Video.VideoClip>(entry.gameRewardedVideos) : null)
                : entry.gameInterstitialVideos;

            if (localVideos != null && localVideos.Count > 0)
            {
                int count = localVideos.Count;
                int subIndex = isRewarded ? gameRewardedVideoIndices[checkIndex] : gameInterstitialVideoIndices[checkIndex];
                bool sequential = runtimeCatalog[checkIndex].gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential;

                if (!sequential)
                    subIndex = UnityEngine.Random.Range(0, count);
                else if (subIndex >= count)
                    subIndex = 0;

                for (int videoOffset = 0; videoOffset < count; videoOffset++)
                {
                    int candidateIndex = (subIndex + videoOffset) % count;
                    if (localVideos[candidateIndex] == null) continue;

                    gameIndex = checkIndex;
                    localClip = localVideos[candidateIndex];
                    if (sequential)
                    {
                        if (isRewarded) gameRewardedVideoIndices[gameIndex] = (candidateIndex + 1) % count;
                        else gameInterstitialVideoIndices[gameIndex] = (candidateIndex + 1) % count;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private void SnapToPosition(RectTransform rectTransform, EnumsJar_Salon.AdPosition position)
    {
        if (rectTransform == null) return;

        Vector2 anchorAndPivot = Vector2.zero;

        switch (position)
        {
            case EnumsJar_Salon.AdPosition.TopLeft: anchorAndPivot = new Vector2(0f, 1f); break;
            case EnumsJar_Salon.AdPosition.TopCenter: anchorAndPivot = new Vector2(0.5f, 1f); break;
            case EnumsJar_Salon.AdPosition.TopRight: anchorAndPivot = new Vector2(1f, 1f); break;

            case EnumsJar_Salon.AdPosition.LeftCenter: anchorAndPivot = new Vector2(0f, 0.5f); break;
            case EnumsJar_Salon.AdPosition.MiddleCenter: anchorAndPivot = new Vector2(0.5f, 0.5f); break;
            case EnumsJar_Salon.AdPosition.RightCenter: anchorAndPivot = new Vector2(1f, 0.5f); break;

            case EnumsJar_Salon.AdPosition.BottomLeft: anchorAndPivot = new Vector2(0f, 0f); break;
            case EnumsJar_Salon.AdPosition.BottomCenter: anchorAndPivot = new Vector2(0.5f, 0f); break;
            case EnumsJar_Salon.AdPosition.BottomRight: anchorAndPivot = new Vector2(1f, 0f); break;
        }

        rectTransform.anchorMin = anchorAndPivot;
        rectTransform.anchorMax = anchorAndPivot;
        rectTransform.pivot = anchorAndPivot;

        rectTransform.anchoredPosition = Vector2.zero;
    }
    #endregion
}

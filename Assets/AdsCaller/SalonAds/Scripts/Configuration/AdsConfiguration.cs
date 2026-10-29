using UnityEngine;

/// <summary>
/// Core configuration data for the AdsCallerSalon system. 
/// </summary>
[CreateAssetMenu(fileName = "AdsConfiguration", menuName = "Ads Caller/Cross Promo/Configuration")]
public class AdsConfiguration : ScriptableObject
{
    #region General
    [Header("General")]
    public bool canInitialize;
    public string removeAdsPlayerPrefKey = "RemoveAds";
    #endregion

    [Header("Involuntary Ads")]
    [Tooltip("Allow banner ads, including startup display and cycling.")]
    public bool canShowBanner = true;
    [Tooltip("Allow large banner (MREC) ads.")]
    public bool canShowLargeBanner = true;
    [Tooltip("Allow interstitial image and video ads.")]
    public bool canShowInterstitial = true;
    [Tooltip("Allow Pop Ad slots and their cycling.")]
    public bool canShowPopAd = true;

    [Header("Rewarded availability feedback")]
    [Tooltip("Show AdNotAvailable for two real-time seconds when a rewarded request cannot be shown.")]
    public bool showAdNotAvailablePopup = false;
    [Tooltip("Optional override. When empty, the shared AdNotAvailable Resources prefab is used.")]
    public GameObject adNotAvailablePopup;

    #region Ad Timings
    [Header("Banner Ads")]
    [Tooltip("(in seconds) How long a banner ad stays before showing other content")]
    public int bannerCycleSpeed = 7;
    public bool showBannerOnStart = true;

    [Header("Pop Ads")]
    [Tooltip("(in seconds) How long a pop ad stays before cycling to the next game")]
    public int popAdCycleSpeed = 3;
    public bool startPopAdCycleOnStart = true;

    [Header("Level Ads")]
    public int levelAdCycleSpeed = 5;
    public bool startLevelAdCycleOnStart = true;
    #endregion

    #region Fullscreen Ads
    [Header("Fullscreen Ads")]
    [Tooltip("Show a store arrow before Close on interstitial image/video ads in both orientations. Tapping it reveals Close before opening the store. Failures show Close directly.")]
    public bool interstitialShowStoreArrowBeforeClose = false;

    [Tooltip("Show a store arrow before Close after rewarded image/video eligibility in both orientations. Tapping it reveals Close before opening the store; it does not grant the reward. Failures show Close directly.")]
    public bool rewardedShowStoreArrowBeforeClose = false;
    #endregion

    #region Ad Sequencing
    [Header("Ad Sequencing")]
    public EnumsJar_Salon.AdSequenceType bannerSequence = EnumsJar_Salon.AdSequenceType.Sequential;
    public EnumsJar_Salon.AdSequenceType interstitialSequence = EnumsJar_Salon.AdSequenceType.Random;
    public EnumsJar_Salon.AdSequenceType largeBannerSequence = EnumsJar_Salon.AdSequenceType.Random;
    public EnumsJar_Salon.AdSequenceType popAdSequence = EnumsJar_Salon.AdSequenceType.Sequential;
    public EnumsJar_Salon.AdSequenceType rewardedSequence = EnumsJar_Salon.AdSequenceType.Random;
    #endregion

    #region Ad Positioning & Media
    [Header("Ad Positioning")]
    public EnumsJar_Salon.AdPosition bannerPosition = EnumsJar_Salon.AdPosition.BottomCenter;
    public EnumsJar_Salon.AdPosition largeBannerPosition = EnumsJar_Salon.AdPosition.BottomCenter;

    [Header("Media Settings")]
    public EnumsJar_Salon.AdMediaType interstitialMediaType = EnumsJar_Salon.AdMediaType.Image;
    public EnumsJar_Salon.AdMediaType rewardedMediaType = EnumsJar_Salon.AdMediaType.Video;
    #endregion

    #region Analytics Formats
    [Header("Analytics String Formats")]
    [Tooltip("Format for ad impressions. Use {0} for ad placement (e.g., popad, banner) and {1} for game name.")]
    public string showEventFormat = "show_{0}_{1}";

    [Tooltip("Format for ad clicks. Use {0} for ad placement (e.g., PopAd, Banner) and {1} for game name.")]
    public string clickEventFormat = "click_{0}_{1}";
    #endregion
}

using UnityEngine;

/// <summary>
/// Core configuration data for the AdsCallerSalon system. 
/// </summary>
[CreateAssetMenu(fileName = "AdsConfiguration", menuName = "Ads Caller/Cross Promo/Configuration")]
public class AdsConfiguration : ScriptableObject
{
    #region General
    [Header("General")]
    public EnumsJar_Salon.PlatformType platformType;
    public bool canInitialize;
    public string removeAdsPlayerPrefKey = "RemoveAds";
    #endregion

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
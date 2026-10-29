using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Factory responsible for loading, instantiating, and releasing Ad UI prefabs via Addressables.
/// </summary>
public class AdUIFactory : MonoBehaviour
{
    #region Addressable Handles
    private AsyncOperationHandle<GameObject> bannerHandle;
    private AsyncOperationHandle<GameObject> largeBannerHandle;
    private AsyncOperationHandle<GameObject> interstitialHandle;
    private AsyncOperationHandle<GameObject> rewardedHandle;
    #endregion

    #region Factory Methods

    public async Awaitable<SalonBannerAd> CreateBannerAsync(Transform parent)
    {
        bannerHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Banner");
        GameObject prefab = await bannerHandle.Task;

        if (prefab == null) return null;

        GameObject obj = Instantiate(prefab, parent);
        obj.SetActive(false);
        return obj.GetComponent<SalonBannerAd>();
    }

    public async Awaitable<SalonInterstitialAd> CreateInterstitialAsync(Transform parent)
    {
        string key = Screen.width > Screen.height ? "SalonAds_Interstitial_landscape" : "SalonAds_Interstitial";
        interstitialHandle = Addressables.LoadAssetAsync<GameObject>(key);
        GameObject prefab = await interstitialHandle.Task;

        if (prefab == null) return null;

        GameObject obj = Instantiate(prefab, parent);
        obj.SetActive(false);
        return obj.GetComponent<SalonInterstitialAd>();
    }

    public async Awaitable<OutSalonRewardedAd> CreateRewardedAsync(Transform parent)
    {
        string key = Screen.width > Screen.height ? "SalonAds_Rewarded_landscape" : "SalonAds_Rewarded";
        rewardedHandle = Addressables.LoadAssetAsync<GameObject>(key);
        GameObject prefab = await rewardedHandle.Task;

        if (prefab == null) return null;

        GameObject obj = Instantiate(prefab, parent);
        obj.SetActive(false);
        return obj.GetComponent<OutSalonRewardedAd>();
    }

    public async Awaitable<SalonLargeBannerAd> CreateLargeBannerAsync(Transform parent)
    {
        largeBannerHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_LargeBanner");
        GameObject prefab = await largeBannerHandle.Task;

        if (prefab == null) return null;

        GameObject obj = Instantiate(prefab, parent);
        obj.SetActive(false);
        return obj.GetComponent<SalonLargeBannerAd>();
    }

    #endregion

    #region Memory Cleanup

    private void OnDestroy()
    {
        if (bannerHandle.IsValid()) Addressables.Release(bannerHandle);
        if (interstitialHandle.IsValid()) Addressables.Release(interstitialHandle);
        if (rewardedHandle.IsValid()) Addressables.Release(rewardedHandle);
        if (largeBannerHandle.IsValid()) Addressables.Release(largeBannerHandle);
    }

    #endregion
}
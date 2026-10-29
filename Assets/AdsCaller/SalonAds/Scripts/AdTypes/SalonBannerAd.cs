using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the UI and interactions for the salon banner ad.
/// </summary>
public class SalonBannerAd : MonoBehaviour
{
    #region Fields
    [Tooltip("The actual UI element (Panel or Image) inside the Canvas to snap.")]
    public RectTransform snapContainer;

    public Image bannerImage;
    public Button bannerButton;
    public Button closeButton;

    [Tooltip("Reference to the animator handling transitions. Can be null if no animation is needed.")]
    public BannerAnimator bannerAnimator;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        bannerButton.onClick.AddListener(OnBannerClicked);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }
    }

    private void OnDisable()
    {
        Time.timeScale = 1;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Updates the banner's visual sprite, triggering an animation if the banner is currently visible.
    /// </summary>
    public async Awaitable UpdateBannerVisualsAsync(Sprite nextSprite)
    {
        // Only animate if the animator exists and the banner is already on screen
        if (bannerAnimator != null && gameObject.activeInHierarchy)
        {
            bannerImage.color = Color.white;
            await bannerAnimator.PlaySlotTransitionAsync(nextSprite);
        }
        else
        {
            // Instant snap for the very first load or if animator is missing
            bannerImage.color = Color.white;
            bannerImage.sprite = nextSprite;
        }
    }

    private void OnBannerClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;

        if (instance != null)
        {
            instance.OnBannerClicked?.Invoke(instance.currentBannerIndex);
        }
    }

    private void OnCloseButtonClicked()
    {
        gameObject.SetActive(false);
    }
    #endregion
}
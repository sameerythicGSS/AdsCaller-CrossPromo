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

    /// <summary>Raised once after the close button hides this banner. Not raised by programmatic hiding.</summary>
    public event System.Action OnCloseButtonClicked;

    [Tooltip("Reference to the animator handling transitions. Can be null if no animation is needed.")]
    public BannerAnimator bannerAnimator;
    #endregion

    private AdsConfiguration configuration;
    private Vector2 lastCanvasSize;
    private AdsConfiguration.BannerStyle lastStyle;
    private bool layoutApplied;

    public void Configure(AdsConfiguration value)
    {
        configuration = value;
        layoutApplied = false;
        RefreshCloseButton();
        RefreshLayout();
    }

    private void OnEnable()
    {
        layoutApplied = false;
        RefreshCloseButton();
        RefreshLayout();
    }

    private void LateUpdate()
    {
        RefreshCloseButton();
        RefreshLayout();
    }

    private void RefreshCloseButton()
    {
        if (configuration == null || closeButton == null) return;
        bool visible = configuration.showBannerCloseButton;
        if (closeButton.gameObject.activeSelf != visible) closeButton.gameObject.SetActive(visible);
    }

    private void RefreshLayout()
    {
        if (configuration == null || snapContainer == null || bannerImage == null) return;
        var canvasRect = snapContainer.parent as RectTransform;
        if (canvasRect == null) return;
        Vector2 available = canvasRect.rect.size;
        if (available.x <= 0f || available.y <= 0f) return;
        var style = configuration.bannerStyle;
        if (layoutApplied && available == lastCanvasSize && style == lastStyle) return;

        // Resolve the current reel before changing its travel distance or anchors.
        if (bannerAnimator != null) bannerAnimator.CancelTransition();
        Vector2 size;
        switch (style)
        {
            case AdsConfiguration.BannerStyle.Adaptive:
                size = new Vector2(available.x, available.x * (193f / 1283f));
                break;
            case AdsConfiguration.BannerStyle.FullWidth:
                size = new Vector2(available.x, 193f);
                break;
            case AdsConfiguration.BannerStyle.Small:
                size = new Vector2(960f, 150f);
                break;
            default:
                size = new Vector2(1283f, 193f);
                break;
        }
        // Fit compact displays without distorting either fixed-size style.
        size *= Mathf.Min(1f, Mathf.Min(available.x / size.x, available.y / size.y));
        snapContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        snapContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

        RectTransform imageRect = bannerImage.rectTransform;
        var content = imageRect.parent as RectTransform;
        if (content != null && content != snapContainer) Stretch(content);
        Stretch(imageRect);
        if (closeButton != null)
        {
            var closeRect = (RectTransform)closeButton.transform;
            closeRect.SetParent(snapContainer, false);
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = Vector2.one * Mathf.Clamp(size.y * 0.22f, 32f, 48f);
        }
        lastCanvasSize = available;
        lastStyle = style;
        layoutApplied = true;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    #region Unity Lifecycle
    private void Start()
    {
        bannerButton.onClick.AddListener(OnBannerClicked);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(HandleCloseButtonClicked);
        }
    }

    private void OnDisable()
    {
        if (bannerAnimator != null) bannerAnimator.CancelTransition();
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

    private void HandleCloseButtonClicked()
    {
        if (!isActiveAndEnabled || closeButton == null || !closeButton.gameObject.activeInHierarchy ||
            !closeButton.IsInteractable() || (configuration != null && !configuration.showBannerCloseButton)) return;

        // Hide first so subscribers observe a closed banner and a cleaned-up reel.
        gameObject.SetActive(false);
        OnCloseButtonClicked?.Invoke();
    }
    #endregion
}
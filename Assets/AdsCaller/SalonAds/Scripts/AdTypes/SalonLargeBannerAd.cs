using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the presentation and UI interactions for the large banner ad, 
/// including a delayed fade-in animation on activation.
/// </summary>
public class SalonLargeBannerAd : MonoBehaviour
{
    #region Fields
    [Tooltip("The actual UI element (Panel or Image) inside the Canvas to snap.")]
    public RectTransform snapContainer;

    public Image image;
    public Button button;
    public Button closeButton;

    private CanvasGroup _canvasGroup;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        // Ensure we have a CanvasGroup to control the alpha
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        // Instantly hide the banner the moment it activates
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
        }

        // Fire and forget the fade-in sequence
        _ = PlayFadeInAnimationAsync();
    }

    private void Start()
    {
        if (button != null)
        {
            button.onClick.AddListener(OnLargeBannerClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnLargeBannerClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
        }
    }
    #endregion

    #region Animations
    /// <summary>
    /// Waits for a short delay, then fades the banner in smoothly, using unscaled time.
    /// </summary>
    private async Awaitable PlayFadeInAnimationAsync()
    {
        try
        {
            // Initial 0.15 second delay using unscaled time
            float delayDuration = 0.15f;
            float elapsedDelay = 0f;

            while (elapsedDelay < delayDuration)
            {
                elapsedDelay += Time.unscaledDeltaTime;
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }

            // 0.3 second fade-in duration using unscaled time
            float fadeDuration = 0.3f;
            float elapsedFade = 0f;

            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsedFade / fadeDuration);
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }

            // Snap to 1 to ensure it's fully visible at the end
            _canvasGroup.alpha = 1f;
        }
        catch (OperationCanceledException)
        {
            // Safely swallow the exception if the banner gets destroyed or disabled mid-animation
            Debug.Log("[SalonLargeBannerAd] Fade animation cancelled.");
        }
    }
    #endregion

    #region Event Handlers
    private void OnLargeBannerClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;

        if (instance != null)
        {
            instance.OnLargeBannerClicked?.Invoke(instance.currentLargeBannerIndex);
        }
    }

    private void OnCloseButtonClicked()
    {
        // Route the close action through the main caller to keep state centralized
        if (AdsCallerSalon.Instance != null)
        {
            AdsCallerSalon.Instance.HideLargeBanner();
        }
    }
    #endregion
}
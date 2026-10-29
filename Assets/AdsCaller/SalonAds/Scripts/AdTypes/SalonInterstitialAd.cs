using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Controls the presentation, animation, and lifecycle of the interstitial ad.
/// </summary>
public class SalonInterstitialAd : MonoBehaviour
{
    [Header("Media Renderers")]
    public Image image;
    public RawImage videoDisplay;
    public VideoPlayer videoPlayer;

    public Button button;

    [Header("Close Button & Timer")]
    public float videoPreparationTimeout = 15f;
    public Button closeButton;

    // Snapshot of the provider configuration for this presentation; not a prefab setting.
    private bool _showStoreArrowBeforeClose;

    private SalonAdCloseControl _closeControl;
    private SalonAdTimerReel _timerReel;
    public float closeButtonDelay = 5f;

    [Tooltip("The parent GameObject containing the timer text/graphics. Deactivates when done.")]
    public GameObject timerBody;
    public Text timerText;

    [Space]
    [Tooltip("If true, the time scale and audio volume will be reverted to their previous states when the ad is closed.")]
    public bool revertStatesOnClose = true;

    private float _previousTimeScale;
    private float _previousAudioVolume;
    private CanvasGroup _canvasGroup;
    private Vector2 _originalImagePos;
    private Vector2 _originalVideoPos;
    private CancellationTokenSource _presentationCts;
    private bool _videoFailed;

    private void Awake()
    {
        _closeControl = new SalonAdCloseControl(transform, closeButton);
        closeButton = _closeControl.Button;
        _timerReel = SalonAdTimerReel.Attach(timerText);
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (image != null) _originalImagePos = image.rectTransform.anchoredPosition;
        if (videoDisplay != null) _originalVideoPos = videoDisplay.rectTransform.anchoredPosition;

        if (button != null) button.onClick.AddListener(OnInterstitialClicked);
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseButtonClicked);
    }

    private void OnEnable()
    {
        _previousTimeScale = Time.timeScale;
        _previousAudioVolume = AudioListener.volume;

        _closeControl.Hide();
        if (timerBody != null) timerBody.SetActive(false);

        Time.timeScale = 0f;
        AudioListener.volume = 0f;
    }

    private void Update()
    {
        if (WasCancelPressed())
        {
            if (CanPress(closeButton)) closeButton.onClick.Invoke();
            return;
        }

        if (WasConfirmPressed())
        {
            Button target = _closeControl.ShowingArrow ? closeButton : button;
            if (CanPress(target)) target.onClick.Invoke();
        }
    }

    private static bool WasConfirmPressed() => AdsUiInput.WasConfirmPressed();

    private static bool WasCancelPressed() => AdsUiInput.WasCancelPressed();

    private static bool CanPress(Button target)
    {
        return target != null && target.isActiveAndEnabled && target.interactable;
    }

    private void OnDisable()
    {
        CancelPresentation();
        StopVideoPlayback();

        if (revertStatesOnClose)
        {
            Time.timeScale = _previousTimeScale;
            AudioListener.volume = _previousAudioVolume;
        }
        else
        {
            Time.timeScale = 1f;
            AudioListener.volume = 1f;
        }
    }

    private void OnDestroy()
    {
        CancelPresentation();
        StopVideoPlayback();

        if (button != null) button.onClick.RemoveListener(OnInterstitialClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseButtonClicked);
    }

    public void SetupImage(Sprite sprite)
    {
        SetupImage(sprite, null);
    }

    public void SetupImage(Sprite sprite, AdsConfiguration configuration)
    {
        _showStoreArrowBeforeClose = configuration != null && configuration.interstitialShowStoreArrowBeforeClose;
        StopVideoPlayback();

        if (image != null)
        {
            image.sprite = sprite;
            image.gameObject.SetActive(true);
        }

        if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
        if (videoPlayer != null) videoPlayer.gameObject.SetActive(false);

        BeginPresentation(startCloseTimer: true);
        if (sprite == null || image == null) HandleVideoFailure("Ad image is unavailable.");
    }

    public bool SetupVideo(VideoClip clip, string url = null)
    {
        return SetupVideo(clip, url, null);
    }

    public bool SetupVideo(VideoClip clip, string url, AdsConfiguration configuration)
    {
        _showStoreArrowBeforeClose = configuration != null && configuration.interstitialShowStoreArrowBeforeClose;
        if (videoPlayer == null || (clip == null && string.IsNullOrWhiteSpace(url)))
        {
            _videoFailed = false;
            HandleVideoFailure("Cannot show video: the player or video source is missing.");
            return false;
        }

        StopVideoPlayback();
        _videoFailed = false;

        if (image != null) image.gameObject.SetActive(false);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(true);
        videoPlayer.gameObject.SetActive(true);

        if (!string.IsNullOrWhiteSpace(url))
        {
            videoPlayer.clip = null;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = url;
        }
        else
        {
            videoPlayer.url = string.Empty;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
        }

        videoPlayer.isLooping = false;
        videoPlayer.playOnAwake = false;
        videoPlayer.errorReceived += OnVideoError;

        CancellationToken token = BeginPresentation(startCloseTimer: false);
        try { videoPlayer.Prepare(); }
        catch (Exception exception)
        {
            HandleVideoFailure(exception.Message);
            return false;
        }
        _ = PrepareAndPlayVideoAsync(token);
        return true;
    }

    private async Awaitable PrepareAndPlayVideoAsync(CancellationToken token)
    {
        float elapsed = 0f;

        try
        {
            while (videoPlayer != null && !videoPlayer.isPrepared && !_videoFailed)
            {
                if (elapsed >= Mathf.Max(1f, SafeDuration(videoPreparationTimeout, 15f)))
                {
                    HandleVideoFailure("Video preparation timed out.");
                    return;
                }

                await Awaitable.NextFrameAsync(token);
                elapsed += Time.unscaledDeltaTime;
            }

            if (token.IsCancellationRequested || _videoFailed) return;
            if (videoPlayer == null) { HandleVideoFailure("Video player disappeared."); return; }

            videoPlayer.Play();
            _ = DelayShowingCloseButton(token);
        }
        catch (OperationCanceledException)
        {
            // The ad was closed or replaced.
        }
        catch (Exception exception) { HandleVideoFailure(exception.Message); }
    }

    private void OnVideoError(VideoPlayer player, string message)
    {
        HandleVideoFailure($"Video playback failed: {message}");
    }

    private void HandleVideoFailure(string message)
    {
        if (_videoFailed || !isActiveAndEnabled) return;
        _videoFailed = true;
        Debug.LogError($"[SalonInterstitialAd] {message}");

        CancelPresentation();
        StopVideoPlayback();
        _canvasGroup.alpha = 1f;
        if (timerBody != null) timerBody.SetActive(false);
        _timerReel?.SetMessage("Video unavailable", false);
        ShowCloseButtonImmediately();
    }

    private void OnInterstitialClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;
        if (instance != null)
        {
            instance.OnInterstitialClicked?.Invoke(instance.currentInterstitialIndex);
        }
    }

    public void OnCloseButtonClicked()
    {
        if (!gameObject.activeSelf) return;
        if (_closeControl.HandleArrowClick(OnInterstitialClicked)) return;

        gameObject.SetActive(false);
        AdsCallerSalon.Instance?.OnInterstitialClosed?.Invoke();
    }

    private CancellationToken BeginPresentation(bool startCloseTimer)
    {
        CancelPresentation();
        _presentationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        CancellationToken token = _presentationCts.Token;
        _videoFailed = false;
        _timerReel?.Clear();
        if (!startCloseTimer)
        {
            if (timerBody != null) timerBody.SetActive(true);
            _timerReel?.SetMessage("Loading...", false);
        }

        _closeControl.Hide();
        _ = PlayIntroAnimationAsync(token);
        if (startCloseTimer) _ = DelayShowingCloseButton(token);
        return token;
    }

    private async Awaitable PlayIntroAnimationAsync(CancellationToken token)
    {
        try
        {
            _canvasGroup.alpha = 0f;

            bool showingImage = image != null && image.gameObject.activeSelf;
            RectTransform activeRect = showingImage ? image.rectTransform : (videoDisplay != null ? videoDisplay.rectTransform : null);
            Vector2 targetPos = showingImage ? _originalImagePos : _originalVideoPos;
            Vector2 startPos = targetPos + new Vector2(0f, -2000f);

            if (activeRect != null) activeRect.anchoredPosition = startPos;

            float fadeDuration = 0.2f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                await Awaitable.NextFrameAsync(token);
            }

            _canvasGroup.alpha = 1f;

            if (activeRect != null)
            {
                float slideDuration = 0.5f;
                elapsed = 0f;
                while (elapsed < slideDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / slideDuration);
                    float easeT = 1f - (1f - t) * (1f - t);
                    activeRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, easeT);
                    await Awaitable.NextFrameAsync(token);
                }

                activeRect.anchoredPosition = targetPos;
            }
        }
        catch (OperationCanceledException)
        {
            // The ad was closed or replaced.
        }
    }

    private async Awaitable DelayShowingCloseButton(CancellationToken token)
    {
        try
        {
            if (timerBody != null) timerBody.SetActive(true);
            float remaining = SafeDuration(closeButtonDelay, 5f);
            while (remaining > 0f)
            {
                _timerReel?.SetCountdown(Mathf.CeilToInt(remaining));
                await Awaitable.NextFrameAsync(token);
                remaining -= Time.unscaledDeltaTime;
            }
            if (timerBody != null) timerBody.SetActive(false);
            _closeControl.Show(_showStoreArrowBeforeClose && !_videoFailed);
        }
        catch (OperationCanceledException) { }
    }

    private void ShowCloseButtonImmediately()
    {
        _closeControl.Show(false);
    }

    private static float SafeDuration(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
    }

    private void StopVideoPlayback()
    {
        if (videoPlayer == null) return;

        videoPlayer.errorReceived -= OnVideoError;
        videoPlayer.Stop();
    }

    private void CancelPresentation()
    {
        if (_presentationCts == null) return;

        _presentationCts.Cancel();
        _presentationCts.Dispose();
        _presentationCts = null;
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns one interruptible slot-reel transition at a time.</summary>
public class BannerAnimator : MonoBehaviour
{
    [SerializeField] private Image primaryBannerImage;
    [SerializeField] private RectTransform bannerContainer;
    [SerializeField] private float spinDuration = 0.5f;
    [SerializeField] private AnimationCurve spinCurve;

    private Image phantomImage;
    private Sprite pendingSprite;
    private Vector2 restingPosition;
    private int transitionVersion;
    private bool transitioning;

    private void OnDisable() => CancelTransition();

    public void CancelTransition()
    {
        ++transitionVersion;
        if (transitioning && primaryBannerImage != null)
        {
            primaryBannerImage.sprite = pendingSprite;
            primaryBannerImage.rectTransform.anchoredPosition = restingPosition;
        }
        transitioning = false;
        if (phantomImage != null)
        {
            // Destroy is deferred; hide immediately so two reels never render together.
            phantomImage.gameObject.SetActive(false);
            Destroy(phantomImage.gameObject);
            phantomImage = null;
        }
    }

#if UNITY_2023_1_OR_NEWER
    public async Awaitable PlaySlotTransitionAsync(Sprite nextSprite)
    {
        var routine = PlaySlotTransitionRoutine(nextSprite);
        try
        {
            while (routine.MoveNext()) await Awaitable.NextFrameAsync();
        }
        finally { (routine as System.IDisposable)?.Dispose(); }
    }
#endif

    public IEnumerator PlaySlotTransitionRoutine(Sprite nextSprite)
    {
        CancelTransition();
        if (primaryBannerImage == null) yield break;
        if (!isActiveAndEnabled || bannerContainer == null || spinDuration <= 0f)
        {
            primaryBannerImage.sprite = nextSprite;
            yield break;
        }

        var primaryRect = primaryBannerImage.rectTransform;
        restingPosition = primaryRect.anchoredPosition;
        pendingSprite = nextSprite;
        transitioning = true;
        int version = transitionVersion;
        try
        {
            Canvas.ForceUpdateCanvases();
            phantomImage = Instantiate(primaryBannerImage, bannerContainer);
            phantomImage.sprite = nextSprite;
            phantomImage.raycastTarget = false;
            // The source image also carries a Button in the shipped prefab.
            var button = phantomImage.GetComponent<Button>();
            if (button != null) button.enabled = false;
            var phantomRect = phantomImage.rectTransform;
            Vector2 travel = Vector2.up * primaryRect.rect.height;
            phantomRect.anchoredPosition = restingPosition + travel;
            float elapsed = 0f;
            while (elapsed < spinDuration)
            {
                if (version != transitionVersion || !isActiveAndEnabled) yield break;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / spinDuration);
                float curved = spinCurve != null ? spinCurve.Evaluate(t) : t;
                primaryRect.anchoredPosition = restingPosition - travel * curved;
                phantomRect.anchoredPosition = restingPosition + travel * (1f - curved);
                yield return null;
            }
        }
        finally
        {
            // An older async continuation must never reset a newer transition.
            if (version == transitionVersion) CancelTransition();
        }
    }
}

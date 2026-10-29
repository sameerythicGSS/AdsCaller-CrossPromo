using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

/// <summary>
/// Handles transition effects for a single-image banner setup.
/// </summary>
public class BannerAnimator : MonoBehaviour
{
    #region Fields
    [SerializeField] private Image primaryBannerImage;
    [SerializeField] private RectTransform bannerContainer;
    [SerializeField] private float spinDuration = 0.5f;
    [SerializeField] private AnimationCurve spinCurve;
    #endregion

    #region Methods
    /// <summary>
    /// Creates a slot-reel transition by instantiating a temporary image, scrolling it, and cleaning up.
    /// </summary>
    /// <param name="nextSprite">The incoming banner sprite.</param>
    public async Awaitable PlaySlotTransitionAsync(Sprite nextSprite)
    {
        primaryBannerImage.rectTransform.anchoredPosition = Vector2.zero;
        Image phantomImage = Instantiate(primaryBannerImage, bannerContainer);
        phantomImage.sprite = nextSprite;

        RectTransform primaryRect = primaryBannerImage.rectTransform;
        RectTransform phantomRect = phantomImage.rectTransform;

        float bannerHeight = primaryRect.rect.height;

        phantomRect.anchoredPosition = primaryRect.anchoredPosition + new Vector2(0, bannerHeight);

        float elapsedTime = 0f;
        Vector2 startPrimaryPos = primaryRect.anchoredPosition;
        Vector2 targetPrimaryPos = startPrimaryPos - new Vector2(0, bannerHeight);

        Vector2 startPhantomPos = phantomRect.anchoredPosition;
        Vector2 targetPhantomPos = startPrimaryPos;

        while (elapsedTime < spinDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / spinDuration);
            float curveT = spinCurve != null ? spinCurve.Evaluate(t) : t;

            primaryRect.anchoredPosition = Vector2.Lerp(startPrimaryPos, targetPrimaryPos, curveT);
            phantomRect.anchoredPosition = Vector2.Lerp(startPhantomPos, targetPhantomPos, curveT);

            await Awaitable.NextFrameAsync();
        }

        primaryBannerImage.sprite = nextSprite;
        //primaryRect.anchoredPosition = startPrimaryPos;
        primaryBannerImage.rectTransform.anchoredPosition = Vector2.zero;

        Destroy(phantomImage.gameObject);
    }
    #endregion
}
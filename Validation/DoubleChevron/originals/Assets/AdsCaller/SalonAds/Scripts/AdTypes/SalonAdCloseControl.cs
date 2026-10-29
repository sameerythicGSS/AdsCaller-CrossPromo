using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reuses the existing close hit area; no prefab rewiring or store URL is needed.</summary>
public sealed class SalonAdCloseControl
{
    public Button Button { get; }
    public bool IsReady { get; private set; }
    public bool ShowingArrow { get; private set; }

    private readonly List<GameObject> originalIcons = new List<GameObject>();
    private readonly GameObject arrowIcon;
    private int arrowClickedFrame = -1;

    public SalonAdCloseControl(Transform owner, Button closeButton)
    {
        if (closeButton == null)
        {
            // A damaged/missing prefab reference must not strand the player.
            var fallback = new GameObject("CloseButton (fallback)", typeof(RectTransform), typeof(Image), typeof(Button));
            fallback.layer = owner.gameObject.layer;
            fallback.transform.SetParent(owner, false);
            var rect = (RectTransform)fallback.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-16f, -16f);
            rect.sizeDelta = new Vector2(140f, 140f);
            var background = fallback.GetComponent<Image>();
            background.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            closeButton = fallback.GetComponent<Button>();
            closeButton.targetGraphic = background;
            var cross = NewIcon(closeButton.transform, "Close icon");
            Stroke(cross.transform, new Vector2(-0.23f, -0.23f), new Vector2(0.23f, 0.23f));
            Stroke(cross.transform, new Vector2(-0.23f, 0.23f), new Vector2(0.23f, -0.23f));
        }

        Button = closeButton;
        foreach (Transform child in Button.transform)
            if (child.gameObject.activeSelf) originalIcons.Add(child.gameObject);

        arrowIcon = NewIcon(Button.transform, "Store arrow");
        // Draw an external-link arrow with UI geometry, avoiding font-glyph dependencies.
        Stroke(arrowIcon.transform, new Vector2(-0.24f, -0.24f), new Vector2(0.24f, 0.24f));
        Stroke(arrowIcon.transform, new Vector2(-0.12f, 0.24f), new Vector2(0.24f, 0.24f));
        Stroke(arrowIcon.transform, new Vector2(0.24f, -0.12f), new Vector2(0.24f, 0.24f));
        Hide();
    }

    public void Hide()
    {
        IsReady = false;
        ShowingArrow = false;
        arrowClickedFrame = -1;
        SetIcon(false);
        if (Button != null) Button.gameObject.SetActive(false);
    }

    public void Show(bool showArrow)
    {
        IsReady = true;
        ShowingArrow = showArrow;
        SetIcon(showArrow);
        if (Button == null) return;
        var group = Button.GetComponent<CanvasGroup>();
        if (group == null) group = Button.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        Button.enabled = true;
        Button.interactable = true;
        if (Button.targetGraphic != null) Button.targetGraphic.raycastTarget = true;
        Button.transform.SetAsLastSibling();
        Button.gameObject.SetActive(true);
    }

    /// <returns>True when this click must not close the ad.</returns>
    public bool HandleArrowClick(Action openStore)
    {
        if (!IsReady || arrowClickedFrame == Time.frameCount) return true;
        if (!ShowingArrow) return false;

        arrowClickedFrame = Time.frameCount;
        Show(false); // Commit the close state BEFORE any external callback/pause/exception.
        try { openStore?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
        return true;
    }

    private void SetIcon(bool arrow)
    {
        foreach (var icon in originalIcons)
            if (icon != null) icon.SetActive(!arrow);
        if (arrowIcon != null) arrowIcon.SetActive(arrow);
    }

    private static GameObject NewIcon(Transform parent, string name)
    {
        var icon = new GameObject(name, typeof(RectTransform));
        icon.layer = parent.gameObject.layer;
        icon.transform.SetParent(parent, false);
        var rect = (RectTransform)icon.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return icon;
    }

    private static void Stroke(Transform parent, Vector2 start, Vector2 end)
    {
        var stroke = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
        stroke.layer = parent.gameObject.layer;
        stroke.transform.SetParent(parent, false);
        var rect = (RectTransform)stroke.transform;
        // A square reference frame keeps the arrow consistent in both orientation prefabs.
        float size = Mathf.Min(((RectTransform)parent.parent).rect.width, ((RectTransform)parent.parent).rect.height);
        if (size <= 0f) size = 140f;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2((end - start).magnitude * size, size * 0.055f);
        rect.anchoredPosition = (start + end) * (size * 0.5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg);
        stroke.GetComponent<Image>().raycastTarget = false;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class PanelAdTreatmentSalon : MonoBehaviour
{
    public EnumsJar_Salon.ToDo treatBanner;
    public EnumsJar_Salon.ToDo treatLargeBanner;

    public bool showInterstitial;

    [Header("Delays Configuration")]
    public float delayBanners = 0.5f;
    public float delayInterstitial = 0.5f;

    [Header("Independent Disable Treatments")]
    public bool invertBannerOnDisable;
    public bool invertLargeBannerOnDisable;

    private void OnEnable()
    {
        _ = TreatAds();
    }
    public async Task TreatAds()
    {
        _ = DelayInterstitial();

        await Task.Delay(TimeSpan.FromSeconds(delayBanners));

        switch (treatBanner)
        {
            case EnumsJar_Salon.ToDo.Hide:
                AdsCallerSalon.Instance.HideBanner();
                break;
            case EnumsJar_Salon.ToDo.Show:
                AdsCallerSalon.Instance.ShowBanner();
                break;
        }

        switch (treatLargeBanner)
        {
            case EnumsJar_Salon.ToDo.Hide:
                AdsCallerSalon.Instance.HideLargeBanner();
                break;
            case EnumsJar_Salon.ToDo.Show:
                AdsCallerSalon.Instance.ShowLargeBanner();
                break;
        }

    }

    public async Awaitable DelayInterstitial()
    {
        if (showInterstitial)
        {
            await Task.Delay(TimeSpan.FromSeconds(delayInterstitial));
            AdsCallerSalon.Instance.ShowInterstitial();
        }
    }

    private void OnDisable()
    {
        // 1. Add this safety check to handle random destruction order on game exit
        if (AdsCallerSalon.Instance == null) return;

        if (invertBannerOnDisable)
        {
            switch (treatBanner)
            {
                case EnumsJar_Salon.ToDo.Hide:
                    AdsCallerSalon.Instance.StartBannerCycle();
                    AdsCallerSalon.Instance.ShowBanner();
                    break;
                case EnumsJar_Salon.ToDo.Show:
                    AdsCallerSalon.Instance.HideBanner();
                    break;
            }
        }

        if (invertLargeBannerOnDisable)
        {
            switch (treatLargeBanner)
            {
                case EnumsJar_Salon.ToDo.Hide:
                    AdsCallerSalon.Instance.ShowLargeBanner();
                    break;
                case EnumsJar_Salon.ToDo.Show:
                    AdsCallerSalon.Instance.HideLargeBanner();
                    break;
            }
        }
    }
}

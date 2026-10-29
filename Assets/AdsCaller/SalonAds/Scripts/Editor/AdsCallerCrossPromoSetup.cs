using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>Loaded only after UPM has resolved CrossPromo's mandatory dependencies.</summary>
public static class AdsCallerCrossPromoSetup
{
    private static readonly string[] Guids = { "0470c87d86da1c847bb2c045cbe2d0ce", "2d2c3c283f78a0e43bbb6ca170d40674", "4e12013eff1e6504ca39b61765df95e8", "78eb89ea6d6ac444e8d439e1c321c6e6", "91f95585a50802047a029881c564e313", "955f21ac495693343835fc20f1853e81", "f9f33d7a546aa5242aa1a4f2a6375c84" };
    private static readonly string[] Addresses = { "SalonAds_Rewarded", "SalonAds_Banner", "SalonAds_Interstitial_landscape", "SalonAds_Interstitial", "SalonAds_PopAd", "SalonAds_LargeBanner", "SalonAds_Rewarded_landscape" };
    public static void EnsureConfigured()
    {
        for (int i = 0; i < Guids.Length; i++)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(Guids[i])) == null)
                throw new InvalidOperationException("Missing CrossPromo UI prefab for " + Addresses[i]);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            settings = AddressableAssetSettings.Create("Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
            AddressableAssetSettingsDefaultObject.Settings = settings;
        }
        // Avoid stealing an address from a project's own entry or overwriting its custom address.
        for (int i = 0; i < Guids.Length; i++)
        {
            string guid = Guids[i], address = Addresses[i];
            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.address != address)
                throw new InvalidOperationException("CrossPromo prefab has a custom address: " + entry.address + ". Restore " + address + " or update the runtime factory.");
            if (settings.groups.Where(g => g != null).SelectMany(g => g.entries).Any(e => e.address == address && e.guid != guid))
                throw new InvalidOperationException("Address already belongs to another asset: " + address);
        }
        var group = settings.FindGroup("Salon_AdTypes") ?? settings.CreateGroup("Salon_AdTypes", false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        for (int i = 0; i < Guids.Length; i++)
        {
            var entry = settings.FindAssetEntry(Guids[i]);
            if (entry == null) { entry = settings.CreateOrMoveEntry(Guids[i], group); entry.address = Addresses[i]; }
        }
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
}

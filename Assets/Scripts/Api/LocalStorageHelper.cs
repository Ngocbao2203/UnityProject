using System.Runtime.InteropServices;
using UnityEngine;

public static class LocalStorageHelper
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string GetLocalStorageItemJS(string key);

    [DllImport("__Internal")]
    private static extern void SetLocalStorageItemJS(string key, string value);

    public static string GetLocalStorageItem(string key)
    {
        return GetLocalStorageItemJS(key);
    }

    public static void SetLocalStorageItem(string key, string value)
    {
        SetLocalStorageItemJS(key, value);
    }
#else
    public static string GetLocalStorageItem(string key)
    {
        return PlayerPrefs.GetString(key, "");
    }

    public static void SetLocalStorageItem(string key, string value)
    {
        PlayerPrefs.SetString(key, value);
        PlayerPrefs.Save();
    }
#endif

    public static string GetToken()
    {
        var token = GetLocalStorageItem("token");
        //Debug.Log($"[Unity] Token lấy từ LocalStorage: {token}");
        return token;
    }

    public static void SaveToken(string token)
    {
        //Debug.Log($"[Unity] Lưu token vào LocalStorage: {token}");
        SetLocalStorageItem("token", token);
    }
}
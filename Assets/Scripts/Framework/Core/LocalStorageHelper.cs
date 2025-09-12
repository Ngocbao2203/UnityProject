using System;
using System.Runtime.InteropServices;
using UnityEngine;
using CGP.Framework;
using UnityEngine.Networking;

namespace CGP.Framework
{
    public static class LocalStorageHelper
    {
        // ===== Keys =====
        public const string TOKEN_KEY = "token";

        // runtime cache để giảm I/O
        private static string _cachedToken;
        private static bool _tokenLoaded;

        // ===== WebGL interop (tùy chọn) =====
        // Chỉ bật nếu bạn chắc chắn đã có *.jslib định nghĩa 2 hàm này.
        // Nếu không, giữ DEFAULT: dùng PlayerPrefs cho an toàn.
        //#define USE_WEBGL_LS

#if UNITY_WEBGL && !UNITY_EDITOR && USE_WEBGL_LS
        [DllImport("__Internal")]
        private static extern string GetLocalStorageItemJS(string key);

        [DllImport("__Internal")]
        private static extern void SetLocalStorageItemJS(string key, string value);

        private static string GetLocalStorageItem(string key)
        {
            try { return GetLocalStorageItemJS(key); }
            catch { return PlayerPrefs.GetString(key, ""); } // fallback an toàn
        }

        private static void SetLocalStorageItem(string key, string value)
        {
            try { SetLocalStorageItemJS(key, value); }
            catch
            {
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
            }
        }
#else
        private static string GetLocalStorageItem(string key) =>
            PlayerPrefs.GetString(key, "");

        private static void SetLocalStorageItem(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
#endif

        // ===== Token API =====
        public static string GetToken()
        {
            if (!_tokenLoaded)
            {
                _cachedToken = GetLocalStorageItem(TOKEN_KEY);
                _tokenLoaded = true;
            }
            return _cachedToken;
        }

        public static bool TryGetToken(out string token)
        {
            token = GetToken();
            return !string.IsNullOrEmpty(token);
        }

        public static void SaveToken(string token)
        {
            _cachedToken = token ?? string.Empty;
            _tokenLoaded = true;
            SetLocalStorageItem(TOKEN_KEY, _cachedToken);
        }

        public static void ClearToken()
        {
            _cachedToken = string.Empty;
            _tokenLoaded = true;
            PlayerPrefs.DeleteKey(TOKEN_KEY); // ok cả WebGL (IndexedDB)
            PlayerPrefs.Save();
        }

        public static bool HasToken() => !string.IsNullOrEmpty(GetToken());

        // ===== Helpers cho UnityWebRequest (dùng chung ở Networking) =====
        public static void AttachAuthHeader(UnityWebRequest req)
        {
            if (req == null) return;
            if (TryGetToken(out var token))
                req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        public static void AttachJsonHeaders(UnityWebRequest req, bool withAuth = true)
        {
            if (req == null) return;
            req.SetRequestHeader("Content-Type", "application/json");
            if (withAuth) AttachAuthHeader(req);
        }
    }
}

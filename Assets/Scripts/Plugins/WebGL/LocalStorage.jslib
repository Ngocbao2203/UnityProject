mergeInto(LibraryManager.library, {
    GetLocalStorageItemJS: function (keyPtr) {
    try {
        var key = UTF8ToString(keyPtr);
        console.log("JS: All keys in localStorage:", Object.keys(localStorage));
        var value = localStorage.getItem(key) || "";
        console.log("JS: Retrieved value for " + key + ": " + value);
        var bufferSize = lengthBytesUTF8(value) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(value, buffer, bufferSize);
        return buffer;
    } catch (e) {
        console.error("JS Error accessing localStorage: " + e);
        var buffer = _malloc(1);
        stringToUTF8("", buffer, 1);
        return buffer;
    }
},
    SetLocalStorageItemJS: function (keyPtr, valuePtr) {
        try {
            var key = UTF8ToString(keyPtr);
            var value = UTF8ToString(valuePtr);
            console.log("JS: Attempting to set " + key + " to: " + value);
            localStorage.setItem(key, value);
            console.log("JS: Successfully set " + key);
        } catch (e) {
            console.error("JS Error setting localStorage: " + e);
        }
    }
});
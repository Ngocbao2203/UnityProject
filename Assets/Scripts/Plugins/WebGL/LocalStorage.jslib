mergeInto(LibraryManager.library, {
    GetLocalStorageItemJS: function (keyPtr) {
    try {
        var key = UTF8ToString(keyPtr);
        var value = localStorage.getItem(key) || "";
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
            localStorage.setItem(key, value);
        } catch (e) {
            console.error("JS Error setting localStorage: " + e);
        }
    }
});
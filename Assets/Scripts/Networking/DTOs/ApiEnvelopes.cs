using System;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class ApiEnvelope<T>
    {
        public int error;           // 0 = OK (tuỳ BE)
        public string message;      // thông báo
        public T data;              // payload: object/array/...
        public int count;           // tuỳ API
    }

    // Tuỳ chọn: alias cho API trả về mảng
    [Serializable]
    public class ApiListEnvelope<T> : ApiEnvelope<T[]>
    {
        // Kế thừa: error, message, count, data (data = T[])
    }
}

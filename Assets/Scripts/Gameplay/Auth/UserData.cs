using System;

namespace CGP.Gameplay.Auth
{
    [Serializable]
    public class UserData
    {
        public string id;
        public string userName;
        public string email;
        public string phoneNumber;
        public string status;
        public int roleId;
    }
}

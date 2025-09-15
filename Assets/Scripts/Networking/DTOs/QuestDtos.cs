using System;
using System.Collections.Generic;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class QuestMeta
    {
        public string id;
        public string questName;
        public string description;
        public string questType;    // "DailyLogin", ...
        public string reward;       // ItemData.id (GUID)
        public int amountReward;
        public bool isDaily;
        // (Nếu backend sau này có "target" ở meta, có thể thêm ở đây)
    }

    [Serializable]
    public class QuestMetaListResponse
    {
        public int error;
        public string message;
        public int count;
        public List<QuestMeta> data;
    }

    [Serializable]
    public class UserQuestState
    {
        public string id;           // userQuestId
        public string userId;
        public string questId;
        public int progress;
        public int target;         
        public string status;       
        public bool rewardClaimed;  
        public string completedAt;  
        public string createdAt;

        // --- Helpers (tùy chọn) ---
        public bool IsClaimed() => rewardClaimed;
        public bool CanClaim() => !rewardClaimed && string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class UserQuestListResponse
    {
        public int error;
        public string message;
        public int count;
        public List<UserQuestState> data;
    }

    [Serializable]
    public class BasicResponse
    {
        public int error;
        public string message;
    }
}

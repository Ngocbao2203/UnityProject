using UnityEngine;

[CreateAssetMenu(fileName = "TutorialData", menuName = "GameData/Tutorial Data")]
public class TutorialData : ScriptableObject
{
    [System.Serializable]
    public class TutorialPage
    {
        public Sprite image;   // Ảnh minh họa
        [TextArea(3, 5)]
        public string text;    // Nội dung mô tả
    }

    public TutorialPage[] pages;
}

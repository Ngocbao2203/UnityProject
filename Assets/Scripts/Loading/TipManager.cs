using UnityEngine;
using TMPro;

public class TipManager : MonoBehaviour
{
    public string[] tips;
    public TextMeshProUGUI tipText;

    void Start()
    {
        int rand = Random.Range(0, tips.Length);
        tipText.text = "Tip: " + tips[rand];
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance;

    public int coins = 0;
    public TextMeshProUGUI coinText;

    void Awake()
    {
        // Singleton để dễ truy cập
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Start()
    {
        UpdateCoinUI();
    }

    public void AddCoins(int amount)
    {
        coins += amount;
        UpdateCoinUI();
    }

    public bool SpendCoins(int amount)
    {
        if (coins >= amount)
        {
            coins -= amount;
            UpdateCoinUI();
            return true;
        }
        return false;
    }
    public bool TrySpendCoins(int amount)
    {
        if (coins >= amount)
        {
            coins -= amount;
            return true;
        }
        return false;
    }

    private void UpdateCoinUI()
    {
        if (coinText != null)
            coinText.text = coins.ToString(); // đổi text hiển thị = số xu
    }

    public bool HasEnoughCoins(int amount)
    {
        return coins >= amount;
    }
}
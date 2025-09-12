using System.Collections;
using UnityEngine;

public class TimeManager : MonoBehaviour
{
    public float dayLengthInSeconds = 3f;

    private void Start()
    {
        StartCoroutine(DayCycle());
    }

    private IEnumerator DayCycle()
    {
        while (true)
        {
            yield return new WaitForSeconds(dayLengthInSeconds);
            Debug.Log("🌞 New Day Begins!");
            // Không cần gọi OnNewDay() nữa vì cây đã tự xử lý qua Update()
        }
    }
}

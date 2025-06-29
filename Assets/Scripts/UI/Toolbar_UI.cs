using System.Collections; // Importing System namespace for basic functionalities
using System.Collections.Generic; // Importing System.Collections.Generic for List<T> usage
using UnityEngine;

public class Toolbar_UI : MonoBehaviour
{
    public List<Slot_UI> toolbarSlots = new List<Slot_UI>(); // Array of Slot_UI components for toolbar slots

    private Slot_UI seclectedSlots; // Array to hold the Slot_UI components


    private void Start()
    {
        SeclectSlot(0);
    }
    private void Update()
    {
        CheckAlphaNumricKeys(); // Check for numeric key inputs to select toolbar slots
    }
    public void SeclectSlot(int index)
    {
        if (toolbarSlots.Count == 7) 
        {
            if(seclectedSlots != null)
            {                 
                seclectedSlots.SetHighlight(false); // Remove highlight from previously selected slot
            }
            seclectedSlots = toolbarSlots[index];
            seclectedSlots.SetHighlight(true); // Highlight the newly selected slot

            GameManager.instance.player.inventoryManager.toolbar.SelectSlot(index);
        }
    }
    private void CheckAlphaNumricKeys()
    {
        if(Input.GetKeyDown(KeyCode.Alpha1))
        {
            SeclectSlot(0); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SeclectSlot(1); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SeclectSlot(2); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SeclectSlot(3); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            SeclectSlot(4); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            SeclectSlot(5); 
        }
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            SeclectSlot(6); 
        }
    }    
}

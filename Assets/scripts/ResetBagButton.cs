using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ResetBagButton : MonoBehaviour
{
    [SerializeField] private Button button;

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        bool showButton = nm != null && nm.IsListening && nm.IsServer;
        if (button != null)
            button.gameObject.SetActive(showButton);
    }

    public void OnResetClicked()
    {
        if (BagManager.Instance != null)
            BagManager.Instance.RequestReset();
    }
}
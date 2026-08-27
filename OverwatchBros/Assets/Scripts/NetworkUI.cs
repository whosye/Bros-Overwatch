using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    public GameObject menuPanel;
    public TMP_InputField ipInputField;
    public TMP_InputField scoreToWinInput;

    public void OnHostClicked()
    {
        if (int.TryParse(scoreToWinInput.text, out int scoreToWin) && scoreToWin > 0)
            MatchManager.Instance.scoreToWin = scoreToWin;

        NetworkManager.Singleton.StartHost();
        menuPanel.SetActive(false);
    }

    public void OnJoinClicked()
    {
        string ip = string.IsNullOrEmpty(ipInputField.text) ? "127.0.0.1" : ipInputField.text;
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(ip, transport.ConnectionData.Port);

        NetworkManager.Singleton.StartClient();
        menuPanel.SetActive(false);
    }
}

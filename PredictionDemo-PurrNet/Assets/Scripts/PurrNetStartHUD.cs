using PurrNet;
using PurrNet.Transports;
using UnityEngine;

namespace DefaultNamespace
{
    // Minimal start menu, the PurrNet counterpart of Mirror's NetworkManagerHUD:
    // pick Host, Server or Client, and set the address/port the client connects to.
    //
    // Clears the NetworkManager's auto-start flags in Awake (before NetworkManager.Start runs AutoStart),
    // otherwise the editor would start a host on its own and the buttons would have nothing to do.
    public class PurrNetStartHUD : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private bool disableAutoStart = true;
        [SerializeField] private Vector2 offset = new Vector2(10, 10);

        private string _address = "127.0.0.1";
        private string _port = "5000";

        private void Awake()
        {
            if (!networkManager)
                networkManager = FindAnyObjectByType<NetworkManager>();

            if (!networkManager)
            {
                Debug.LogError("[PurrNetStartHUD][Awake] No NetworkManager found.", this);
                return;
            }

            if (disableAutoStart)
            {
                networkManager.startServerFlags = StartFlags.None;
                networkManager.startClientFlags = StartFlags.None;
            }

            if (networkManager.transport is UDPTransport udp)
            {
                _address = udp.address;
                _port = udp.serverPort.ToString();
            }
        }

        private void OnGUI()
        {
            if (!networkManager)
                return;

            GUILayout.BeginArea(new Rect(offset.x, offset.y, 220, 200));

            bool serverOff = networkManager.serverState == ConnectionState.Disconnected;
            bool clientOff = networkManager.clientState == ConnectionState.Disconnected;

            if (serverOff && clientOff)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60));
                _address = GUILayout.TextField(_address);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", GUILayout.Width(60));
                _port = GUILayout.TextField(_port);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Host (Server + Client)"))
                {
                    ApplyTransportSettings();
                    networkManager.StartHost();
                }
                if (GUILayout.Button("Server Only"))
                {
                    ApplyTransportSettings();
                    networkManager.StartServer();
                }
                if (GUILayout.Button("Client"))
                {
                    ApplyTransportSettings();
                    networkManager.StartClient();
                }
            }
            else
            {
                GUILayout.Label($"Server: {networkManager.serverState}");
                GUILayout.Label($"Client: {networkManager.clientState}");

                if (GUILayout.Button("Stop"))
                {
                    if (!clientOff)
                        networkManager.StopClient();
                    if (!serverOff)
                        networkManager.StopServer();
                }
            }

            GUILayout.EndArea();
        }

        void ApplyTransportSettings()
        {
            if (networkManager.transport is not UDPTransport udp)
                return;

            udp.address = _address;
            if (ushort.TryParse(_port, out ushort port))
                udp.serverPort = port;
            else
                Debug.LogWarning($"[PurrNetStartHUD][ApplyTransportSettings] Invalid port '{_port}', keeping {udp.serverPort}.", this);
        }
    }
}

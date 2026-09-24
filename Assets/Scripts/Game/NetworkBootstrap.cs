using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class NetworkBootstrap
{
    public static NetworkManager Ensure()
    {
        if (NetworkManager.Singleton != null)
            return NetworkManager.Singleton;

        GameObject holder = new GameObject("Network Manager");
        holder.SetActive(false);
        UnityTransport transport = holder.AddComponent<UnityTransport>();
        NetworkManager manager = holder.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false
        };
        holder.SetActive(true);
        return manager;
    }
}

using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Kdyz se skripty prekompiluji behem Play modu, sitovy socket hosta by zustal viset v editoru
// a dalsi spusteni hry by hlasilo "port 7777 is already in use" az do restartu Unity.
// Pred prekompilovanim behem Play modu proto sit natvrdo zavreme.
[InitializeOnLoad]
public static class NetworkPortGuard
{
    static NetworkPortGuard()
    {
        // Pri beznem odchodu z Play modu si sit zavre Netcode samo (zasah odsud mu jen pusobil chybu v konzoli).
        AssemblyReloadEvents.beforeAssemblyReload += CloseNetwork;
    }

    static void CloseNetwork()
    {
        if (!Application.isPlaying) return;

        var network = NetworkManager.Singleton;
        if (network == null || !network.IsListening) return;

        var transport = network.NetworkConfig.NetworkTransport;
        network.Shutdown(true);
        if (transport != null)
            transport.Shutdown();
    }
}

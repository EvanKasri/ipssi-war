using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// UDP LAN ipssi-war — port 47777 (jeu sur 7777).
/// Les clients envoient IPSSIWAR|REQ|port pour solliciter une réponse directe de l'hôte,
/// plus fiable que la seule diffusion broadcast selon pare-feu / Wi‑Fi.
/// </summary>
public class LanLobbyDiscovery : MonoBehaviour
{
    public const string Magic = "IPSSIWAR";
    public int m_DiscoveryPort = 47777;

    UdpClient m_Udp;
    bool m_HostBeacon;
    bool m_ListenActive;
    float m_LastBeaconTime;
    int m_GamePort = 7777;

    readonly ConcurrentQueue<LanServerEntry> m_Pending = new ConcurrentQueue<LanServerEntry>();

    Action<LanServerEntry> m_OnDiscovered;
    float m_NextAutoDiscoveryPing;

    public struct LanServerEntry
    {
        public string Address;
        public int Port;
        public string HostName;
        public int MaxPlayers;
    }

    /// <summary>Nombre de joueurs requis (diffusé dans le beacon).</summary>
    public int CurrentMaxPlayers = 2;

    private void Update()
    {
        while (m_Pending.TryDequeue(out LanServerEntry e))
            m_OnDiscovered?.Invoke(e);

        if (m_HostBeacon && Time.realtimeSinceStartup - m_LastBeaconTime >= 1f)
        {
            m_LastBeaconTime = Time.realtimeSinceStartup;
            SendBeaconBroadcast();
        }

        if (!m_HostBeacon && m_ListenActive && m_OnDiscovered != null && m_Udp != null &&
            Time.realtimeSinceStartup >= m_NextAutoDiscoveryPing)
        {
            m_NextAutoDiscoveryPing = Time.realtimeSinceStartup + 4f;
            SendDiscoveryRequest(7777);
        }
    }

    private void OnDestroy()
    {
        StopAll();
    }

    /// <summary>Demande aux hôtes du LAN de répondre en direct (unicast). À appeler au clic Actualiser.</summary>
    public void SendDiscoveryRequest(int gamePort = 7777)
    {
        if (!m_ListenActive || m_Udp == null || m_HostBeacon) return;
        string payload = $"{Magic}|REQ|{gamePort}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            m_Udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, m_DiscoveryPort));
            foreach (IPAddress ua in GetOtherBroadcastAddresses())
            {
                try { m_Udp.Send(bytes, bytes.Length, new IPEndPoint(ua, m_DiscoveryPort)); }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] REQ LAN : " + ex.Message);
        }
    }

    public void StartHostBeacon(int gamePort)
    {
        StopUdpInternal();
        m_GamePort = gamePort;
        try
        {
            if (!TryBindUdp())
            {
                Debug.LogWarning("[LanLobbyDiscovery] Bind UDP impossible pour le beacon hôte.");
                return;
            }
            m_HostBeacon = true;
            m_ListenActive = true;
            m_LastBeaconTime = 0f;
            BeginReceive();
            SendBeaconBroadcast();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] Beacon hôte : " + ex.Message);
            m_HostBeacon = false;
            m_ListenActive = false;
        }
    }

    public void StopHostBeacon()
    {
        m_HostBeacon = false;
    }

    public void StartClientListen(Action<LanServerEntry> onDiscovered)
    {
        if (onDiscovered == null) return;
        StopUdpInternal();
        m_OnDiscovered = onDiscovered;
        try
        {
            if (!TryBindUdp())
            {
                Debug.LogWarning("[LanLobbyDiscovery] Écoute LAN impossible (pare-feu ou port " + m_DiscoveryPort + " occupé).");
                return;
            }
            m_ListenActive = true;
            m_HostBeacon = false;
            m_NextAutoDiscoveryPing = Time.realtimeSinceStartup + 4f;
            BeginReceive();
            SendDiscoveryRequest(7777);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] Client LAN : " + ex.Message);
            m_ListenActive = false;
        }
    }

    public void StopClientListen()
    {
        m_OnDiscovered = null;
        if (!m_HostBeacon)
            StopUdpInternal();
    }

    public void StopAll()
    {
        m_HostBeacon = false;
        m_OnDiscovered = null;
        StopUdpInternal();
    }

    bool TryBindUdp()
    {
        try
        {
            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, m_DiscoveryPort));
            udp.EnableBroadcast = true;
            m_Udp = udp;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] Bind : " + ex.Message);
            return false;
        }
    }

    void StopUdpInternal()
    {
        m_ListenActive = false;
        if (m_Udp != null)
        {
            try { m_Udp.Close(); } catch { /* ignore */ }
            m_Udp = null;
        }
    }

    void BeginReceive()
    {
        if (!m_ListenActive || m_Udp == null) return;
        try
        {
            m_Udp.BeginReceive(OnUdpReceive, null);
        }
        catch
        {
            m_ListenActive = false;
        }
    }

    void OnUdpReceive(IAsyncResult ar)
    {
        if (!m_ListenActive || m_Udp == null) return;
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] data;
        try
        {
            data = m_Udp.EndReceive(ar, ref remote);
        }
        catch
        {
            BeginReceive();
            return;
        }

        BeginReceive();

        try
        {
            string msg = Encoding.UTF8.GetString(data);
            if (!msg.StartsWith(Magic + "|", StringComparison.Ordinal)) return;

            string[] p = msg.Split('|');
            if (p.Length >= 2 && p[1] == "REQ")
            {
                if (m_HostBeacon)
                    SendBeaconUnicast(remote);
                return;
            }

            if (p.Length < 5) return;
            if (!int.TryParse(p[2], out int port)) return;

            string ip = p[3];
            string hostName = p[4];
            int maxPlayers = 2;
            if (p.Length >= 6) int.TryParse(p[5], out maxPlayers);

            m_Pending.Enqueue(new LanServerEntry
            {
                Address = ip,
                Port = port,
                HostName = hostName,
                MaxPlayers = maxPlayers
            });
        }
        catch
        {
            // ignore
        }
    }

    void SendBeaconUnicast(IPEndPoint target)
    {
        if (m_Udp == null) return;
        string ip = GetPreferredLanIPv4();
        string host = Environment.MachineName ?? "host";
        string payload = $"{Magic}|1|{m_GamePort}|{ip}|{host}|{CurrentMaxPlayers}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            m_Udp.Send(bytes, bytes.Length, target);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] Réponse unicast : " + ex.Message);
        }
    }

    void SendBeaconBroadcast()
    {
        if (m_Udp == null) return;
        string ip = GetPreferredLanIPv4();
        string host = Environment.MachineName ?? "host";
        string payload = $"{Magic}|1|{m_GamePort}|{ip}|{host}|{CurrentMaxPlayers}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            m_Udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, m_DiscoveryPort));
            foreach (IPAddress ua in GetOtherBroadcastAddresses())
            {
                try { m_Udp.Send(bytes, bytes.Length, new IPEndPoint(ua, m_DiscoveryPort)); }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LanLobbyDiscovery] Broadcast : " + ex.Message);
        }
    }

    static string GetPreferredLanIPv4()
    {
        // Règle : interface avec passerelle IPv4 = vraiment connectée, même si c'est un vEthernet.
        // (ex: Hyper-V external switch bridge une carte physique → a une vraie gateway)
        // Les interfaces SANS gateway et avec un nom virtuel sont ignorées.
        string fallback = null;
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var props = ni.GetIPProperties();

                // Vérifier présence d'une passerelle IPv4
                bool hasIPv4Gateway = false;
                if (props.GatewayAddresses != null)
                    foreach (var gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                        { hasIPv4Gateway = true; break; }

                // Filtrer les adaptateurs SANS gateway qui sont clairement virtuels
                if (!hasIPv4Gateway)
                {
                    string desc = ni.Description.ToLowerInvariant();
                    string name = ni.Name.ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("hyper-v") ||
                        desc.Contains("vmware")  || desc.Contains("vpn")     ||
                        desc.Contains("tap ")    || name.Contains("vethernet")) continue;
                }

                foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string a = u.Address.ToString();
                    if (a.StartsWith("169.254.") || a == "127.0.0.1") continue;

                    if (hasIPv4Gateway) return a;  // ← meilleur choix : interface active
                    fallback ??= a;
                }
            }
        }
        catch { /* ignore */ }
        return fallback ?? "127.0.0.1";
    }

    static System.Collections.Generic.List<IPAddress> GetOtherBroadcastAddresses()
    {
        var list = new System.Collections.Generic.List<IPAddress>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties ipp = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation u in ipp.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    try
                    {
                        if (u.IPv4Mask == null) continue;
                        byte[] ipBytes = u.Address.GetAddressBytes();
                        byte[] mask = u.IPv4Mask.GetAddressBytes();
                        if (ipBytes.Length != 4 || mask.Length != 4) continue;
                        byte[] bc = new byte[4];
                        for (int i = 0; i < 4; i++)
                            bc[i] = (byte)(ipBytes[i] | (~mask[i]));
                        list.Add(new IPAddress(bc));
                    }
                    catch { /* IPv4Mask indispo selon plateforme */ }
                }
            }
        }
        catch { /* ignore */ }
        return list;
    }
}

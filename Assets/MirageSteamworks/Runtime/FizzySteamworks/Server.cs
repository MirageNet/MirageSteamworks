using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Connection //: IConnection
    {

    }

    public class Server : Common
    {
        public event Action<int> OnConnected;
        public event Action<int, byte[], Channel> OnReceivedData;
        public event Action<int> OnDisconnected;
        public event Action<int, string> OnReceivedError;

        private readonly List<Connection> connections = new List<Connection>();

        private BidirectionalDictionary<HSteamNetConnection, int> connToMirrorID;
        private BidirectionalDictionary<CSteamID, int> steamIDToMirrorID;
        private int maxConnections;
        private int nextConnectionID;

        private HSteamListenSocket listenSocket;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        public Server(int maxConnections, bool gameServer) : base(gameServer)
        {
            this.maxConnections = maxConnections;
            connToMirrorID = new BidirectionalDictionary<HSteamNetConnection, int>();
            steamIDToMirrorID = new BidirectionalDictionary<CSteamID, int>();
            nextConnectionID = 1;
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
        }

        public void Start()
        {
            try
            {
                if (GameServer)
                {
                    SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
                }
                else
                {
                    SteamNetworkingUtils.InitRelayNetworkAccess();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            Host();
        }

        private void Host()
        {
            var options = new SteamNetworkingConfigValue_t[] { };
            if (GameServer)
            {
                listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
            }
            else
            {
                listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                if (connections.Count >= maxConnections)
                {
                    Debug.Log($"Incoming connection {clientSteamID} would exceed max connection count. Rejecting.");
                    if (GameServer)
                    {
                        SteamGameServerNetworkingSockets.CloseConnection(param.m_hConn, 0, "Max Connection Count", false);
                    }
                    else
                    {
                        SteamNetworkingSockets.CloseConnection(param.m_hConn, 0, "Max Connection Count", false);
                    }
                    return;
                }

                EResult res;
                if (GameServer)
                {
                    res = SteamGameServerNetworkingSockets.AcceptConnection(param.m_hConn);
                }
                else
                {
                    res = SteamNetworkingSockets.AcceptConnection(param.m_hConn);
                }

                if (res == EResult.k_EResultOK)
                {
                    Debug.Log($"Accepting connection {clientSteamID}");
                }
                else
                {
                    Debug.Log($"Connection {clientSteamID} could not be accepted: {res}");
                }
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                int connectionId = nextConnectionID++;
                connToMirrorID.Add(param.m_hConn, connectionId);
                steamIDToMirrorID.Add(param.m_info.m_identityRemote.GetSteamID(), connectionId);
                OnConnected?.Invoke(connectionId);
                Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connectionId}");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (connToMirrorID.TryGetValue(param.m_hConn, out int connId))
                {
                    InternalDisconnect(connId, param.m_hConn);
                }
            }
            else
            {
                Debug.Log($"Connection {clientSteamID} state changed: {param.m_info.m_eState}");
            }
        }

        private void InternalDisconnect(int connId, HSteamNetConnection socket)
        {
            OnDisconnected?.Invoke(connId);
            if (GameServer)
            {
                SteamGameServerNetworkingSockets.CloseConnection(socket, 0, "Graceful disconnect", false);
            }
            else
            {
                SteamNetworkingSockets.CloseConnection(socket, 0, "Graceful disconnect", false);
            }
            connToMirrorID.Remove(connId);
            steamIDToMirrorID.Remove(connId);
            Debug.Log($"Client with ConnectionID {connId} disconnected.");
        }

        public void Disconnect(int connectionId)
        {
            if (connToMirrorID.TryGetValue(connectionId, out HSteamNetConnection conn))
            {
                Debug.Log($"Connection id {connectionId} disconnected.");
                if (GameServer)
                {
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Disconnected by server", false);
                }
                else
                {
                    SteamNetworkingSockets.CloseConnection(conn, 0, "Disconnected by server", false);
                }
                steamIDToMirrorID.Remove(connectionId);
                connToMirrorID.Remove(connectionId);
                OnDisconnected?.Invoke(connectionId);
            }
            else
            {
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
            }
        }

        public void FlushData()
        {
            foreach (HSteamNetConnection conn in connToMirrorID.FirstTypes.ToList())
            {
                if (GameServer)
                {
                    SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
                }
                else
                {
                    SteamNetworkingSockets.FlushMessagesOnConnection(conn);
                }
            }
        }

        public void ReceiveData()
        {
            foreach (HSteamNetConnection conn in connToMirrorID.FirstTypes.ToList())
            {
                if (connToMirrorID.TryGetValue(conn, out int connId))
                {
                    var ptrs = new IntPtr[MAX_MESSAGES];
                    int messageCount;

                    if (GameServer)
                    {
                        messageCount = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, MAX_MESSAGES);
                    }
                    else
                    {
                        messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, MAX_MESSAGES);
                    }

                    if (messageCount > 0)
                    {
                        for (int i = 0; i < messageCount; i++)
                        {
                            (byte[] data, Channel ch) = ProcessMessage(ptrs[i]);
                            OnReceivedData?.Invoke(connId, data, ch);
                        }
                    }
                }
            }
        }

        public void Send(int connectionId, byte[] data, Channel channelId)
        {
            if (connToMirrorID.TryGetValue(connectionId, out HSteamNetConnection conn))
            {
                EResult res = SendSocket(conn, data, channelId);

                if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
                {
                    Debug.Log($"Connection to {connectionId} was lost.");
                    InternalDisconnect(connectionId, conn);
                }
                else if (res != EResult.k_EResultOK)
                {
                    Debug.LogError($"Could not send: {res}");
                }
            }
            else
            {
                Debug.LogError("Trying to send on an unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, "ERROR Unknown Connection");
            }
        }

        public string ServerGetClientAddress(int connectionId)
        {
            if (steamIDToMirrorID.TryGetValue(connectionId, out CSteamID steamId))
            {
                return steamId.ToString();
            }
            else
            {
                Debug.LogError("Trying to get info on an unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, "ERROR Unknown Connection");
                return string.Empty;
            }
        }

        public void Shutdown()
        {
            if (GameServer)
            {
                SteamGameServerNetworkingSockets.CloseListenSocket(listenSocket);
            }
            else
            {
                SteamNetworkingSockets.CloseListenSocket(listenSocket);
            }

            c_onConnectionChange?.Dispose();
            c_onConnectionChange = null;
        }
    }

    public class BidirectionalDictionary<T1, T2> : IEnumerable
    {
        private Dictionary<T1, T2> t1ToT2Dict = new Dictionary<T1, T2>();
        private Dictionary<T2, T1> t2ToT1Dict = new Dictionary<T2, T1>();

        public IEnumerable<T1> FirstTypes => t1ToT2Dict.Keys;
        public IEnumerable<T2> SecondTypes => t2ToT1Dict.Keys;

        public IEnumerator GetEnumerator() => t1ToT2Dict.GetEnumerator();

        public int Count => t1ToT2Dict.Count;

        public void Add(T1 key, T2 value)
        {
            if (t1ToT2Dict.ContainsKey(key))
            {
                Remove(key);
            }

            t1ToT2Dict[key] = value;
            t2ToT1Dict[value] = key;
        }

        public void Add(T2 key, T1 value)
        {
            if (t2ToT1Dict.ContainsKey(key))
            {
                Remove(key);
            }

            t2ToT1Dict[key] = value;
            t1ToT2Dict[value] = key;
        }

        public T2 Get(T1 key) => t1ToT2Dict[key];

        public T1 Get(T2 key) => t2ToT1Dict[key];

        public bool TryGetValue(T1 key, out T2 value) => t1ToT2Dict.TryGetValue(key, out value);

        public bool TryGetValue(T2 key, out T1 value) => t2ToT1Dict.TryGetValue(key, out value);

        public bool Contains(T1 key) => t1ToT2Dict.ContainsKey(key);

        public bool Contains(T2 key) => t2ToT1Dict.ContainsKey(key);

        public void Remove(T1 key)
        {
            if (Contains(key))
            {
                T2 val = t1ToT2Dict[key];
                t1ToT2Dict.Remove(key);
                t2ToT1Dict.Remove(val);
            }
        }
        public void Remove(T2 key)
        {
            if (Contains(key))
            {
                T1 val = t2ToT1Dict[key];
                t1ToT2Dict.Remove(val);
                t2ToT1Dict.Remove(key);
            }
        }

        public T1 this[T2 key]
        {
            get => t2ToT1Dict[key];
            set
            {
                Add(key, value);
            }
        }

        public T2 this[T1 key]
        {
            get => t1ToT2Dict[key];
            set
            {
                Add(key, value);
            }
        }
    }
}

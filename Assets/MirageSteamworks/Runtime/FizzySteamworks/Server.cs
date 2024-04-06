using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class SteamEndPoint : IEndPoint
    {
        public SteamConnection Connection;
        public CSteamID HostId;

        public SteamEndPoint() { }
        public SteamEndPoint(CSteamID hostId)
        {
            HostId = hostId;
        }
        private SteamEndPoint(SteamConnection conn)
        {
            Connection = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        IEndPoint IEndPoint.CreateCopy()
        {
            return new SteamEndPoint(Connection);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is SteamEndPoint other))
                return false;

            // both null
            if (Connection == null && other.Connection == null)
                return true;

            return Connection.Equals(other.Connection);
        }

        public override int GetHashCode()
        {
            if (Connection != null)
            {
                return Connection.GetHashCode();
            }
            else
            {
                return HostId.GetHashCode();
            }
        }
    }
    public class SteamConnection
    {
        public readonly HSteamNetConnection id;
        public readonly CSteamID cSteamID;
        public bool Disconnected;

        public SteamConnection(HSteamNetConnection hConn, CSteamID cSteamID)
        {
            id = hConn;
            this.cSteamID = cSteamID;
        }

        public override string ToString()
        {
            return $"SteamConnection({cSteamID})";
        }
    }

    public class Server : Common
    {
        public event Action<SteamConnection> OnConnected;
        //public event Action<SteamConnection, byte[], Channel> OnReceivedData;
        public event Action<SteamConnection> OnDisconnected;
        public event Action<SteamConnection, string> OnReceivedError;

        private HSteamListenSocket listenSocket;
        private HSteamNetPollGroup pollGroup;
        private readonly Dictionary<HSteamNetConnection, SteamConnection> connections = new Dictionary<HSteamNetConnection, SteamConnection>();

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        public Server(bool gameServer) : base(gameServer)
        {
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
                pollGroup = SteamGameServerNetworkingSockets.CreatePollGroup();
            }
            else
            {
                listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
                pollGroup = SteamNetworkingSockets.CreatePollGroup();
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                // TODO validate if user can join, or call CloseConnection

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
                var connection = new SteamConnection(param.m_hConn, param.m_info.m_identityRemote.GetSteamID());
                connections.Add(param.m_hConn, connection);
                OnConnected?.Invoke(connection);

                if (GameServer)
                    SteamGameServerNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);
                else
                    SteamNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);

                Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connection}");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (connections.TryGetValue(param.m_hConn, out SteamConnection conn))
                {
                    InternalDisconnect(conn, "Graceful disconnect");
                }
            }
            else
            {
                Debug.Log($"Connection {clientSteamID} state changed: {param.m_info.m_eState}");
            }
        }

        private void InternalDisconnect(SteamConnection conn, string reason)
        {
            HSteamNetConnection connId = conn.id;
            if (GameServer)
            {
                SteamGameServerNetworkingSockets.SetConnectionPollGroup(connId, HSteamNetPollGroup.Invalid);
                SteamGameServerNetworkingSockets.CloseConnection(connId, 0, reason, false);
            }
            else
            {
                SteamNetworkingSockets.SetConnectionPollGroup(connId, HSteamNetPollGroup.Invalid);
                SteamNetworkingSockets.CloseConnection(connId, 0, reason, false);
            }

            conn.Disconnected = true;
            connections.Remove(conn.id);
            Debug.Log($"Connection id {conn} disconnected with reason: {reason}");
            OnDisconnected?.Invoke(conn);
        }

        public void Disconnect(SteamConnection conn)
        {
            if (conn.Disconnected)
            {
                Debug.LogWarning($"Trying to disconnect {conn} but it is already disconnected");
                return;
            }

            InternalDisconnect(conn, "Disconnected by server");
        }

        public void FlushData()
        {
            foreach (HSteamNetConnection conn in connections.Keys)
            {
                if (GameServer)
                    SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
                else
                    SteamNetworkingSockets.FlushMessagesOnConnection(conn);
            }
        }

        protected override bool CanPoll()
        {
            return connections.Count > 0;
        }

        protected override void ReceiveData()
        {
            int messageCount = GameServer
                ? SteamGameServerNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, receivePtrs, receivePtrs.Length)
                : SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, receivePtrs, receivePtrs.Length);

            for (int i = 0; i < messageCount; i++)
            {
                try
                {
                    SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(receivePtrs[i]);
                    HSteamNetConnection connId = data.m_conn;
                    if (connections.TryGetValue(connId, out SteamConnection conn))
                    {
                        Buffer buffer = CopyToBuffer(data);
                        receiveQueue.Enqueue((conn, buffer));
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to find connection for {connId}");
                    }
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(receivePtrs[i]);
                }
            }
        }

        public override void Send(SteamConnection conn, ArraySegment<byte> data, Channel channelId)
        {
            HSteamNetConnection connId = conn.id;
            EResult res = SendSocket(connId, data, channelId);

            if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
            {
                Debug.Log($"Connection to {conn} was lost.");
                InternalDisconnect(conn, "No Connection");
            }
            else if (res != EResult.k_EResultOK)
            {
                Debug.LogError($"Could not send: {res}");
            }
        }

        public override void Shutdown()
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
}

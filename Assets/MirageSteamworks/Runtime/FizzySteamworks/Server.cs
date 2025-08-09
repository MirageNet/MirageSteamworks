using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Server : Common
    {
        private HSteamListenSocket listenSocket;
        private HSteamNetPollGroup pollGroup;
        private readonly Dictionary<HSteamNetConnection, SteamConnection> connections = new Dictionary<HSteamNetConnection, SteamConnection>();

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        public Server(bool gameServer) : base(gameServer)
        {
            if (gameServer)
                c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnConnectionStatusChanged);
            else
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
            var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
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
                CallOnConnected(connection);

                if (GameServer)
                    SteamGameServerNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);
                else
                    SteamNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);

                Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connection}");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (connections.TryGetValue(param.m_hConn, out var conn))
                {
                    InternalDisconnect(conn, "Graceful disconnect");
                }
            }
            else
            {
                Debug.Log($"Connection {clientSteamID} state changed: {param.m_info.m_eState}");
            }
        }

        protected override void InternalDisconnect(SteamConnection conn, string reason)
        {
            var connId = conn.ConnId;
            connections.Remove(connId);

            conn.Disconnected = true;

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

            Debug.Log($"Connection id {conn} disconnected with reason: {reason}");
            CallOnDisconnected(conn);
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

        public override void FlushData()
        {
            foreach (var conn in connections.Keys)
            {
                if (GameServer)
                    SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
                else
                    SteamNetworkingSockets.FlushMessagesOnConnection(conn);
            }
        }

        public override void ReceiveData()
        {
            if (connections.Count == 0)
                return;

            var messageCount = GameServer
                ? SteamGameServerNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, receivePtrs, receivePtrs.Length)
                : SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, receivePtrs, receivePtrs.Length);

            for (var i = 0; i < messageCount; i++)
            {
                try
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(receivePtrs[i]);
                    if (connections.TryGetValue(msg.m_conn, out var conn))
                    {
                        var buffer = CopyToBuffer(msg);
                        CallOnData(conn, buffer);
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to find connection for {msg.m_conn}");
                    }
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(receivePtrs[i]);
                }
            }
        }

        public override void Send(SteamConnection connection, ArraySegment<byte> data, Channel channelId)
        {
            if (connection.Disconnected)
                Debug.LogWarning("Send called after Disconnected");

            var res = SendSocket(connection.ConnId, data, channelId);

            if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
            {
                Debug.Log($"Connection to {connection} was lost.");
                InternalDisconnect(connection, "No Connection");
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
                SteamGameServerNetworkingSockets.DestroyPollGroup(pollGroup);
            }
            else
            {
                SteamNetworkingSockets.CloseListenSocket(listenSocket);
                SteamNetworkingSockets.DestroyPollGroup(pollGroup);
            }

            pollGroup = default;
            c_onConnectionChange?.Dispose();
            c_onConnectionChange = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Server : Common
    {
        public delegate bool AcceptConnectionCallback(SteamNetConnectionStatusChangedCallback_t param);

        private readonly AcceptConnectionCallback acceptCallback;

        private HSteamListenSocket listenSocket;
        private HSteamNetPollGroup pollGroup;
        private readonly Dictionary<HSteamNetConnection, SteamConnection> connections = new Dictionary<HSteamNetConnection, SteamConnection>();

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        private readonly Queue<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChanges = new Queue<SteamNetConnectionStatusChangedCallback_t>();
        private readonly Queue<SteamConnection> _pendingDisconnections = new Queue<SteamConnection>();

        public Server(bool gameServer, int maxBufferSize, bool noNagle, AcceptConnectionCallback acceptCallback) : base(gameServer, maxBufferSize, noNagle)
        {
            this.acceptCallback = acceptCallback;
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
            _connectionStatusChanges.Enqueue(param);
        }

        private void ProcessConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            var state = param.m_info.m_eState;
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    OnSteamConnecting(param);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    OnSteamConnected(param);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    OnSteamDisconnected(param);
                    break;

                default:
                    Debug.Log($"Connection {clientSteamID} state changed: {param.m_info.m_eState}");
                    break;
            }
        }

        private void OnSteamConnecting(SteamNetConnectionStatusChangedCallback_t param)
        {
            var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            var accept = true; // default accept new connection
            if (acceptCallback != null)
            {
                // custom call back that developers can use to check for kick/ban before accepting connection
                accept = acceptCallback.Invoke(param);
            }

            if (accept)
            {
                EResult res;
                if (GameServer)
                    res = SteamGameServerNetworkingSockets.AcceptConnection(param.m_hConn);
                else
                    res = SteamNetworkingSockets.AcceptConnection(param.m_hConn);

                if (res == EResult.k_EResultOK)
                    Debug.Log($"Accepting connection {clientSteamID}");
                else
                    Debug.Log($"Connection {clientSteamID} could not be accepted: {res}");
            }
            else
            {
                var debugMsg = "Rejected by application";
                if (GameServer)
                    _ = SteamGameServerNetworkingSockets.CloseConnection(param.m_hConn, k_ESteamNetConnectionEnd_App_RejectedCallback, debugMsg, false);
                else
                    _ = SteamNetworkingSockets.CloseConnection(param.m_hConn, k_ESteamNetConnectionEnd_App_RejectedCallback, debugMsg, false);

                // note: dont log here, dev can log inside acceptCallback if they want to
            }
        }

        private void OnSteamConnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            var connection = new SteamConnection(this, param.m_info.m_identityRemote.GetSteamID(), param.m_hConn);
            connections.Add(param.m_hConn, connection);
            CallOnConnected(connection);

            if (GameServer)
                SteamGameServerNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);
            else
                SteamNetworkingSockets.SetConnectionPollGroup(param.m_hConn, pollGroup);

            Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connection}");
        }

        private void OnSteamDisconnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            if (connections.TryGetValue(param.m_hConn, out var conn))
            {
                var reason = param.m_info.m_eEndReason;
                var debug = param.m_info.m_szEndDebug;
                Debug.LogWarning($"Connection {conn} was closed by peer or problem detected, Reason: {reason}, Debug: {debug}");
                InternalDisconnect(conn, reason, "Connection closed by peer or problem detected.");
            }
        }

        public void Disconnect(SteamConnection conn, int? reason, string debugStr = null)
        {
            if (conn.Disconnected)
            {
                Debug.LogWarning($"Trying to disconnect {conn} but it is already disconnected");
                return;
            }

            InternalDisconnect(conn, reason, debugStr ?? "Disconnected by server");
        }

        protected override void InternalDisconnect(SteamConnection conn, int? reasonNullable, string debugString)
        {
            var reason = reasonNullable ?? (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Generic;
            var connId = conn.ConnId;

            if (conn.Disconnected)
                return;

            connections.Remove(connId);
            conn.Disconnected = true;

            if (GameServer)
            {
                SteamGameServerNetworkingSockets.SetConnectionPollGroup(connId, HSteamNetPollGroup.Invalid);
                SteamGameServerNetworkingSockets.CloseConnection(connId, reason, debugString, false);
            }
            else
            {
                SteamNetworkingSockets.SetConnectionPollGroup(connId, HSteamNetPollGroup.Invalid);
                SteamNetworkingSockets.CloseConnection(connId, reason, debugString, false);
            }

            Debug.Log($"Connection id {conn} disconnected with reason: {debugString}");
            // InternalDisconnect might be called inside Tick or from high level Disconnect, so queue disconnect event till Tick
            _pendingDisconnections.Enqueue(conn);
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

        public override unsafe void Tick()
        {
            while (_connectionStatusChanges.Count > 0)
            {
                ProcessConnectionStatusChanged(_connectionStatusChanges.Dequeue());
            }

            while (_pendingDisconnections.Count > 0)
            {
                var conn = _pendingDisconnections.Dequeue();
                TryCallOnDisconnected(conn);
            }

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
                        var span = new ReadOnlySpan<byte>(msg.m_pData.ToPointer(), msg.m_cbSize);
                        CallOnData(conn, span);
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to find connection for {msg.m_conn}");
                    }
                }
                finally
                {
                    // Release memory back to Steam after the event returns
                    SteamNetworkingMessage_t.Release(receivePtrs[i]);
                }
            }
        }

        private void TryCallOnDisconnected(SteamConnection conn)
        {
            if (!conn.DisconnectedEventCalled)
            {
                CallOnDisconnected(conn);
                conn.DisconnectedEventCalled = true;
            }
        }

        public override void Send(SteamConnection connection, ReadOnlySpan<byte> span, Channel channelId)
        {
            if (connection.Disconnected)
                Debug.LogWarning("Send called after Disconnected");

            var res = SendSocket(connection.ConnId, span, channelId);

            if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
            {
                Debug.Log($"Connection to {connection} was lost.");
                InternalDisconnect(connection, null, "No Connection");
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

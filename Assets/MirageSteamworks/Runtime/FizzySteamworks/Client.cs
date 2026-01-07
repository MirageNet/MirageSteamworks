using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Client : Common
    {
        private enum State
        {
            New,
            Connecting,
            Connected,
            Disconnected
        }

        private readonly float timeoutSeconds;

        private State state;
        private SteamConnection connection;
        private double connectingTimeout;
        public int DisconnectReason;
        public string DisconnectDebugString;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        private readonly Queue<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChanges = new Queue<SteamNetConnectionStatusChangedCallback_t>();

        public Client(float timeoutSeconds, bool gameServer, int maxBufferSize, bool noNagle) : base(gameServer, maxBufferSize, noNagle)
        {
            this.timeoutSeconds = Math.Max(1f, timeoutSeconds);
        }

        public SteamConnection Connect(CSteamID hostSteamID)
        {
            if (state == State.Connecting)
                throw new InvalidOperationException("Connect called while already Connecting");
            if (state == State.Connected)
                throw new InvalidOperationException("Connect called while already Connected");

            state = State.Connecting;
            connection = new SteamConnection(this, hostSteamID, hConn: default);

            try
            {
                if (GameServer)
                    SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
                else
                    SteamNetworkingUtils.InitRelayNetworkAccess();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start RelayNetwork: {ex}");
                throw new Exception($"Failed to connect to steam: {ex}");
            }

            try
            {
                c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                var steamIdentity = new SteamNetworkingIdentity();
                steamIdentity.SetSteamID(connection.SteamID);

                var options = new SteamNetworkingConfigValue_t[] { };
                var connId = SteamNetworkingSockets.ConnectP2P(ref steamIdentity, 0, options.Length, options);
                connectingTimeout = Time.timeAsDouble + timeoutSeconds;

                connection.SteamNetworkingIdentity = steamIdentity;
                connection.ConnId = connId;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to run ConnectP2P: {ex}");
                throw new Exception($"Failed to connect to steam: {ex}");
            }

            return connection;
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            // queue event and process it in ReceiveData
            _connectionStatusChanges.Enqueue(param);
        }

        private void ProcessConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            var state = param.m_info.m_eState;
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    OnSteamConnected();
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    OnSteamDisconnected(param);
                    break;

                default:
                    Debug.Log($"Connection state changed: {state} - EndDebug:{param.m_info.m_szEndDebug}");
                    break;
            }
        }

        private void OnSteamConnected()
        {
            // need to invoke callbacks during Tick not when steam invokes callback
            if (state == State.Connecting)
            {
                Debug.Log("Connection established.");
                state = State.Connected;
                CallOnConnected(connection);
            }
            else
            {
                Debug.LogWarning($"Steam changed to Connected, but state is {state}");
            }
        }

        private void OnSteamDisconnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            DisconnectReason = param.m_info.m_eEndReason;
            DisconnectDebugString = param.m_info.m_szEndDebug;

            Debug.LogWarning($"Connection was closed by peer, Reason: {DisconnectReason}, Debug: {DisconnectDebugString}");

            var reason = param.m_info.m_eEndReason;
            InternalDisconnect(connection, reason, "Connection closed by peer or problem detected.");
        }

        protected void Dispose()
        {
            if (c_onConnectionChange != null)
            {
                c_onConnectionChange.Dispose();
                c_onConnectionChange = null;
            }
        }

        public void Disconnect(int? reason = null, string debugStr = null)
        {
            if (state == State.Connected || state == State.Connecting)
            {
                debugStr ??= (state == State.Connected ? "Disconnect called" : "Disconnect called while Connecting");
                InternalDisconnect(connection, reason, debugStr);
            }
        }

        protected override void InternalDisconnect(SteamConnection conn, int? reasonNullable, string debugString)
        {
            var reason = reasonNullable ?? (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Generic;
            if (state == State.Disconnected)
                return;

            Debug.Assert(connection == conn);
            state = State.Disconnected;

            connection.Disconnected = true;
            SteamNetworkingSockets.CloseConnection(connection.ConnId, reason, debugString, false);

            Debug.Log($"Connection id {connection} disconnected with reason: {debugString}");
            Dispose();
        }

        public override unsafe void Tick()
        {
            while (_connectionStatusChanges.Count > 0)
            {
                ProcessConnectionStatusChanged(_connectionStatusChanges.Dequeue());
            }

            if (state == State.Connecting && Time.timeAsDouble > connectingTimeout)
            {
                Debug.LogError($"Connection attempt timed out.");
                InternalDisconnect(connection, k_ESteamNetConnectionEnd_App_Timeout, "Connecting Timeout");
            }

            // note: not else, we can start receiving after state changes to connected in this frame
            if (state == State.Connected)
            {
                var messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.ConnId, receivePtrs, receivePtrs.Length);
                for (var i = 0; i < messageCount; i++)
                {
                    try
                    {
                        var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(receivePtrs[i]);
                        var span = new ReadOnlySpan<byte>(msg.m_pData.ToPointer(), msg.m_cbSize);
                        CallOnData(connection, span);
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(receivePtrs[i]);
                    }
                }
            }

            if (state == State.Disconnected)
            {
                TryCallOnDisconnected();
            }
        }

        private void TryCallOnDisconnected()
        {
            if (!connection.DisconnectedEventCalled)
            {
                CallOnDisconnected(connection);
                connection.DisconnectedEventCalled = true;
            }
        }

        public override void Send(SteamConnection inConn, ReadOnlySpan<byte> span, Channel channelId)
        {
            if (state == State.Disconnected)
            {
                Debug.LogWarning("Send called after Disconnected");
                return;
            }

            // assert connection is the same
            Debug.Assert(connection == inConn);
            // but use the field connection, because that for sure is the one to the host
            base.Send(connection, span, channelId);
        }

        public override void FlushData()
        {
            if (connection != null && !connection.Disconnected)
            {
                SteamNetworkingSockets.FlushMessagesOnConnection(connection.ConnId);
            }
        }

        public override void Shutdown() => Disconnect();
    }
}

using System;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Client : Common
    {
        private enum ConnectResult
        {
            None,
            Success,
            Timeout,
            Cancelled,
            Failed,
        }
        private enum State
        {
            New,
            Connecting,
            Connected,
            Disconnected
        }

        private readonly float timeoutSeconds;

        private State state;
        private ConnectResult connectResult;
        private SteamConnection connection;
        private double connectingTimeout;
        public int DisconnectReason;
        public string DisconnectDebugString;
        private bool disconnectEventCalled;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

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
                var steamId = connection.SteamID;
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

        private void EndConnecting(ConnectResult result)
        {
            Debug.Assert(state == State.Connecting);
            Debug.Assert(result != ConnectResult.None);

            switch (result)
            {
                case ConnectResult.Success:
                    state = State.Connected;
                    CallOnConnected(connection);
                    break;

                case ConnectResult.Timeout:
                    Debug.LogError($"Connection attempt timed out.");

                    InternalDisconnect(connection, k_ESteamNetConnectionEnd_App_Timeout, "Connecting Timeout");
                    state = State.Disconnected;
                    TryCallOnDisconnected();
                    break;

                case ConnectResult.Failed:
                    Debug.LogWarning($"Connection attempt failed");

                    state = State.Disconnected;
                    TryCallOnDisconnected();
                    break;

                case ConnectResult.Cancelled:
                    Debug.LogWarning($"Connection attempt was cancelled or rejected.");

                    state = State.Disconnected;
                    TryCallOnDisconnected();
                    break;
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            //var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                // need to invoke callbacks during Tick not when steam invokes callback
                if (state == State.Connecting)
                {
                    Debug.Log("Connection established.");
                    connectResult = ConnectResult.Success;
                }
                else
                {
                    Debug.LogWarning($"Steam changed to Connected, but state is {state}");
                }
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                DisconnectReason = param.m_info.m_eEndReason;
                DisconnectDebugString = param.m_info.m_szEndDebug;

                Debug.LogWarning($"Connection was closed by peer, {DisconnectReason}: {DisconnectDebugString}");
                if (state == State.Connecting && connectResult == ConnectResult.None)
                    connectResult = ConnectResult.Failed;

                InternalDisconnect(connection, null, "Closed or problem");
            }
            else
            {
                Debug.Log($"Connection state changed: {param.m_info.m_eState} - EndDebug:{param.m_info.m_szEndDebug}");
            }
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
            if (state == State.Connecting)
                connectResult = ConnectResult.Cancelled;

            if (state == State.Connected || state == State.Connecting)
            {
                debugStr ??= (state == State.Connected ? "Disconnect called" : "Disconnect called while Connecting");
                InternalDisconnect(connection, reason, debugStr);
            }
        }

        protected override void InternalDisconnect(SteamConnection conn, int? reasonNullable, string debugString)
        {
            var reason = reasonNullable ?? (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Generic;
            Debug.Assert(connection == conn);
            state = State.Disconnected;

            connection.Disconnected = true;
            SteamNetworkingSockets.CloseConnection(connection.ConnId, reason, debugString, false);

            Debug.Log($"Connection id {connection} disconnected with reason: {debugString}");
            Dispose();
        }

        public override unsafe void ReceiveData()
        {
            // if we fail to connect, it will set the state to Disconnected after setting connectResult
            if (state == State.Connecting || state == State.Disconnected)
            {
                // check if result was set first,
                // then check timeout
                if (connectResult != ConnectResult.None)
                {
                    EndConnecting(connectResult);
                }
                else if (connectingTimeout > Time.timeAsDouble)
                {
                    connectResult = ConnectResult.Timeout;
                    EndConnecting(connectResult);
                }
            }

            // note: not else, we can start receiving after EndConnecting is called
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
            else if (state == State.Disconnected)
            {
                TryCallOnDisconnected();
            }
        }

        private void TryCallOnDisconnected()
        {
            if (!disconnectEventCalled)
            {
                CallOnDisconnected(connection);
                disconnectEventCalled = true;
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
            SteamNetworkingSockets.FlushMessagesOnConnection(connection.ConnId);
        }

        public override void Shutdown() => Disconnect();
    }
}

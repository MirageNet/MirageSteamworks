using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Client : Common
    {
        public bool Connecting { get; private set; }
        public bool Connected { get; private set; }

        private readonly TimeSpan ConnectionTimeout;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

        private enum ConnectTaskResult
        {
            None,
            Success,
            Timeout,
            Cancelled,
        }
        private TaskCompletionSource<ConnectTaskResult> connectTask;
        private SteamConnection connection;

        public Client(float timeoutSeconds, bool gameServer, int maxBufferSize) : base(gameServer, maxBufferSize)
        {
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1f, timeoutSeconds));
        }

        public void Connect(CSteamID hostSteamID)
        {
            Connect(new SteamConnection(default, hostSteamID));
        }

        public void Connect(SteamConnection connection)
        {
            Debug.Assert(!Connecting, "Connect called while already Connecting");
            Debug.Assert(!Connected, "Connect called while already Connected");
            Connecting = true;
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
                Debug.LogError($"Failed to start RelayNetwork: {ex}");
                OnConnectionFailed();
                return;
            }

            this.connection = connection;
            ConnectAsync();
        }

        private async void ConnectAsync()
        {
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            bool success;
            try
            {
                success = await TryConnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected exception: {ex.Message}");
                success = false;
            }

            if (!success)
            {
                Debug.LogError("Connection failed.");
                OnConnectionFailed();
            }
        }

        private async Task<bool> TryConnectAsync()
        {
            connectTask = new TaskCompletionSource<ConnectTaskResult>();
            var smi = new SteamNetworkingIdentity();
            var steamId = connection.SteamID;
            smi.SetSteamID(connection.SteamID);

            var options = new SteamNetworkingConfigValue_t[] { };
            var connId = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);
            connection.ConnId = connId;

            ConnectTimeout(connectTask, ConnectionTimeout);

            var result = await connectTask.Task;

            if (result == ConnectTaskResult.Success)
                return true;

            switch (result)
            {
                case ConnectTaskResult.None:
                    Debug.LogError($"Connect taks finished with no result");
                    break;
                case ConnectTaskResult.Timeout:
                    Debug.LogError($"Connection to {steamId} timed out.");
                    break;

                case ConnectTaskResult.Cancelled:
                    Debug.LogError($"The connection attempt was cancelled.");
                    break;
            }
            return false;
        }

        private static async void ConnectTimeout(TaskCompletionSource<ConnectTaskResult> connectedComplete, TimeSpan connectionTimeout)
        {
            await Task.Delay(connectionTimeout);
            SetConnectTaskResult(connectedComplete, ConnectTaskResult.Timeout);
        }
        private static void SetConnectTaskResult(TaskCompletionSource<ConnectTaskResult> connectedComplete, ConnectTaskResult result)
        {
            if (result == ConnectTaskResult.None)
            {
                Debug.LogError("Should never be setting result to None");
                return;
            }

            if (connectedComplete == null)
                return;

            var didSet = connectedComplete.TrySetResult(result);
            // log if setting result to Success after it was already set to timeout/cancel
            if (!didSet && result == ConnectTaskResult.Success)
                Debug.LogWarning($"Failed to set result to Success because it was already set");
        }



        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            //var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                Connecting = false;
                Connected = true;
                CallOnConnected(connection);
                SetConnectTaskResult(connectTask, ConnectTaskResult.Success);
                Debug.Log("Connection established.");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                Debug.Log($"Connection was closed by peer, {param.m_info.m_szEndDebug}");
                Disconnect();
            }
            else
            {
                Debug.Log($"Connection state changed: {param.m_info.m_eState.ToString()} - {param.m_info.m_szEndDebug}");
            }
        }

        public void Disconnect()
        {
            SetConnectTaskResult(connectTask, ConnectTaskResult.Cancelled);
            Dispose();

            if (Connected)
            {
                InternalDisconnect(connection, "Disconnect called");
            }
            if (Connecting)
            {
                InternalDisconnect(connection, "Disconnect called while Connecting");
            }

            if (connection != null)
            {
                Debug.Log("Sending Disconnect message");
                SteamNetworkingSockets.CloseConnection(connection.ConnId, 0, "Graceful disconnect", false);
                connection = null;
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

        protected override void InternalDisconnect(SteamConnection inConn, string reason)
        {
            Debug.Assert(connection == inConn);
            Connected = false;

            connection.Disconnected = true;
            SteamNetworkingSockets.CloseConnection(connection.ConnId, 0, "Disconnected", false);

            Debug.Log($"Connection id {connection} disconnected with reason: {reason}");
            CallOnDisconnected(connection);
        }

        public override void ReceiveData()
        {
            if (!Connected)
                return;

            var messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.ConnId, receivePtrs, receivePtrs.Length);
            for (var i = 0; i < messageCount; i++)
            {
                try
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(receivePtrs[i]);
                    var buffer = CopyToBuffer(msg);
                    CallOnData(connection, buffer);
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(receivePtrs[i]);
                }
            }
        }

        public override void Send(SteamConnection inConn, ArraySegment<byte> data, Channel channelId)
        {
            if (!Connected && !Connecting)
                Debug.LogWarning("Send called after Disconnected");

            // assert connection is the same
            Debug.Assert(connection == inConn);
            // but use the field connection, because that for sure is the eone to the host
            base.Send(connection, data, channelId);
        }

        private void OnConnectionFailed()
        {
            // OnConnectionFailed might be called after connection is set to null,
            // in that case we can skip this
            if (connection != null)
            {
                connection.Disconnected = true;
                CallOnDisconnected(connection);
            }
        }

        public override void FlushData()
        {
            SteamNetworkingSockets.FlushMessagesOnConnection(connection.ConnId);
        }

        public override void Shutdown() => Disconnect();
    }
}

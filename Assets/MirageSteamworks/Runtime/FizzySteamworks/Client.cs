using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Client : Common
    {
        public bool Connecting { get; private set; }
        public bool Connected { get; private set; }
        public bool Error { get; private set; }

        private readonly TimeSpan ConnectionTimeout;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

        private CancellationTokenSource cancelToken;
        private TaskCompletionSource<bool> connectedComplete;
        private SteamConnection connection;

        public Client(float timeoutSeconds, bool gameServer) : base(gameServer)
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
                this.connection = connection;
                ConnectAsync();
            }
            catch (FormatException)
            {
                Debug.LogError($"Connection string was not in the right format. Did you enter a SteamId?");
                Error = true;
                OnConnectionFailed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected exception: {ex.Message}");
                Error = true;
                OnConnectionFailed();
            }
        }

        private async void ConnectAsync()
        {
            cancelToken = new CancellationTokenSource();
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            try
            {
                connectedComplete = new TaskCompletionSource<bool>();
                var smi = new SteamNetworkingIdentity();
                smi.SetSteamID(connection.SteamID);

                var options = new SteamNetworkingConfigValue_t[] { };
                var connId = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);
                connection.ConnId = connId;

                Task connectedCompleteTask = connectedComplete.Task;
                var timeOutTask = Task.Delay(ConnectionTimeout, cancelToken.Token);

                if (await Task.WhenAny(connectedCompleteTask, timeOutTask) != connectedCompleteTask)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        Debug.LogError($"The connection attempt was cancelled.");
                    }
                    else if (timeOutTask.IsCompleted)
                    {
                        Debug.LogError($"Connection to {connection.SteamID} timed out.");
                    }

                    OnConnectionFailed();
                }
            }
            catch (FormatException)
            {
                Debug.LogError($"Connection string was not in the right format. Did you enter a SteamId?");
                Error = true;
                OnConnectionFailed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected exception: {ex.Message}");
                Error = true;
                OnConnectionFailed();
            }
            finally
            {
                if (Error)
                {
                    Debug.LogError("Connection failed.");
                    OnConnectionFailed();
                }
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            var clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                Connecting = false;
                Connected = true;
                CallOnConnected(connection);
                connectedComplete.SetResult(true);
                Debug.Log("Connection established.");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
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
            cancelToken?.Cancel();
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
            connection.Disconnected = true;
            CallOnDisconnected(connection);
        }
        public override void FlushData()
        {
            SteamNetworkingSockets.FlushMessagesOnConnection(connection.ConnId);
        }

        public override void Shutdown() => Disconnect();
    }
}

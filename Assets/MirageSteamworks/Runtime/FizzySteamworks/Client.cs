using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Client : Common
    {
        public bool Connected { get; private set; }
        public bool Error { get; private set; }

        private readonly TimeSpan ConnectionTimeout;

        public event Action<byte[], Channel> OnReceivedData;
        public event Action OnConnected;
        public event Action OnDisconnected;
        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

        private CancellationTokenSource cancelToken;
        private TaskCompletionSource<Task> connectedComplete;
        private CSteamID hostSteamID = CSteamID.Nil;
        private HSteamNetConnection HostConnection;
        private readonly List<Action> BufferedData;

        public Client(float timeoutSeconds)
        {
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1f, timeoutSeconds));
            BufferedData = new List<Action>();
        }

        public void Connect(CSteamID hostSteamID)
        {
            try
            {
#if UNITY_SERVER
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
                SteamNetworkingUtils.InitRelayNetworkAccess();
#endif
                ConnectAsync(hostSteamID);
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

        private async void ConnectAsync(CSteamID hostSteamID)
        {
            cancelToken = new CancellationTokenSource();
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            try
            {
                connectedComplete = new TaskCompletionSource<Task>();
                OnConnected += SetConnectedComplete;

                var smi = new SteamNetworkingIdentity();
                smi.SetSteamID(hostSteamID);

                var options = new SteamNetworkingConfigValue_t[] { };
                HostConnection = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);

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
                        Debug.LogError($"Connection to {hostSteamID} timed out.");
                    }

                    OnConnected -= SetConnectedComplete;
                    OnConnectionFailed();
                }

                OnConnected -= SetConnectedComplete;
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
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                Connected = true;
                OnConnected.Invoke();
                Debug.Log("Connection established.");

                if (BufferedData.Count > 0)
                {
                    Debug.Log($"{BufferedData.Count} received before connection was established. Processing now.");
                    {
                        foreach (Action a in BufferedData)
                        {
                            a();
                        }
                    }
                }
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
                InternalDisconnect();
            }

            if (HostConnection.m_HSteamNetConnection != 0)
            {
                Debug.Log("Sending Disconnect message");
                SteamNetworkingSockets.CloseConnection(HostConnection, 0, "Graceful disconnect", false);
                HostConnection.m_HSteamNetConnection = 0;
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

        private void InternalDisconnect()
        {
            Connected = false;
            OnDisconnected.Invoke();
            Debug.Log("Disconnected.");
            SteamNetworkingSockets.CloseConnection(HostConnection, 0, "Disconnected", false);
        }

        public void ReceiveData()
        {
            var ptrs = new IntPtr[MAX_MESSAGES];
            int messageCount;

            if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(HostConnection, ptrs, MAX_MESSAGES)) > 0)
            {
                for (int i = 0; i < messageCount; i++)
                {
                    (byte[] data, Channel ch) = ProcessMessage(ptrs[i]);
                    if (Connected)
                    {
                        OnReceivedData(data, ch);
                    }
                    else
                    {
                        BufferedData.Add(() => OnReceivedData(data, ch));
                    }
                }
            }
        }

        public void Send(byte[] data, Channel channelId)
        {
            try
            {
                EResult res = SendSocket(HostConnection, data, channelId);

                if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
                {
                    Debug.Log($"Connection to server was lost.");
                    InternalDisconnect();
                }
                else if (res != EResult.k_EResultOK)
                {
                    Debug.LogError($"Could not send: {res.ToString()}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SteamNetworking exception during Send: {ex.Message}");
                InternalDisconnect();
            }
        }

        private void SetConnectedComplete() => connectedComplete.SetResult(connectedComplete.Task);
        private void OnConnectionFailed() => OnDisconnected.Invoke();
        public void FlushData()
        {
            SteamNetworkingSockets.FlushMessagesOnConnection(HostConnection);
        }
    }
}

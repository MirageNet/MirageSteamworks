using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public delegate void DataReceivedHandler(SteamConnection connection, ReadOnlySpan<byte> data);

    public abstract class Common
    {
        protected const int MAX_MESSAGES = 256;
        public const int k_ESteamNetConnectionEnd_App_Generic = (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min;
        public const int k_ESteamNetConnectionEnd_App_RejectedCallback = (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min + 1;
        public const int k_ESteamNetConnectionEnd_App_RejectedPeer = (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min + 2;
        public const int k_ESteamNetConnectionEnd_App_Timeout = (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min + 3;

        public enum Channel
        {
            Reliable = 0,
            Unreliable = 1,
        }

        /// <summary>
        /// if the <see cref="SteamGameServerNetworkingSockets"/> should be used instead of <see cref="SteamNetworkingSockets"/>
        /// </summary>
        protected readonly bool GameServer;
        protected readonly int maxBufferSize;
        protected readonly bool noNagle;
        protected readonly IntPtr[] receivePtrs = new IntPtr[MAX_MESSAGES];

        public event Action<SteamConnection> OnConnected;
        public event DataReceivedHandler OnData;
        public event Action<SteamConnection> OnDisconnected;

        protected Common(bool gameServer, int maxBufferSize, bool noNagle)
        {
            GameServer = gameServer;
            this.maxBufferSize = maxBufferSize;
            this.noNagle = noNagle;
        }

        protected void CallOnConnected(SteamConnection connection)
        {
            try
            {
                OnConnected?.Invoke(connection);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in OnConnected subscriber: {e}");
            }
        }

        protected void CallOnData(SteamConnection connection, ReadOnlySpan<byte> span)
        {
            try
            {
                OnData?.Invoke(connection, span);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in OnData subscriber: {e}");
            }
        }

        protected void CallOnDisconnected(SteamConnection connection)
        {
            try
            {
                OnDisconnected?.Invoke(connection);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in OnDisconnected subscriber: {e}");
            }
        }

        private int ChannelToSteamConst(Channel channel)
        {
            switch (channel)
            {
                case Channel.Reliable:
                    return noNagle
                        ? Constants.k_nSteamNetworkingSend_ReliableNoNagle
                        : Constants.k_nSteamNetworkingSend_Reliable;
                case Channel.Unreliable:
                    return noNagle
                        ? Constants.k_nSteamNetworkingSend_UnreliableNoDelay
                        : Constants.k_nSteamNetworkingSend_Unreliable;
                default:
                    throw new InvalidEnumArgumentException(nameof(channel), (int)channel, typeof(Channel));
            }
        }

        public static Channel SteamConstToChannel(int steamConst)
        {
            // The flags are a bitmask, so we check for the reliable flag.
            // If it's not present, we assume unreliable. This handles all
            // combinations like NoNagle and NoDelay.
            if ((steamConst & Constants.k_nSteamNetworkingSend_Reliable) != 0)
            {
                return Channel.Reliable;
            }
            else
            {
                return Channel.Unreliable;
            }
        }

        protected unsafe EResult SendSocket(HSteamNetConnection conn, ReadOnlySpan<byte> span, Channel channel)
        {
            var length = span.Length;
            if (length > maxBufferSize)
                throw new ArgumentException($"Data is over maxBufferSize, Size={span.Length}, Max={maxBufferSize}");

            fixed (byte* spanPtr = span)
            {
                var sendFlag = ChannelToSteamConst(channel);

                var intPtr = new IntPtr(spanPtr);

                EResult res;
                if (GameServer)
                {
                    res = SteamGameServerNetworkingSockets.SendMessageToConnection(conn, intPtr, (uint)length, sendFlag, out var _);
                }
                else
                {
                    res = SteamNetworkingSockets.SendMessageToConnection(conn, intPtr, (uint)length, sendFlag, out var _);
                }

                // When using NoDelay, k_EResultIgnored is an expected result, not an error.
                if (res != EResult.k_EResultOK && res != EResult.k_EResultIgnored)
                {
                    Debug.LogWarning($"Send issue: {res}");
                }
                return res;
            }
        }

        public virtual void Send(SteamConnection connection, ReadOnlySpan<byte> data, Channel channelId)
        {
            if (connection == null)
            {
                Debug.LogError("Steam Send called with null connection");
                return;
            }

            try
            {
                if (connection.Disconnected)
                    Debug.LogWarning("Send called after Disconnected");

                var res = SendSocket(connection.ConnId, data, channelId);

                if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
                {
                    Debug.Log($"Connection to {connection} was lost.");
                    InternalDisconnect(connection, null, "No Connection");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SteamNetworking exception during Send: {ex}");
                InternalDisconnect(connection, null, "Unexpected Error");
            }
        }

        public abstract void Tick();
        public abstract void FlushData();
        public abstract void Shutdown();

        protected abstract void InternalDisconnect(SteamConnection conn, int? reasonNullable, string debugString);
    }
}

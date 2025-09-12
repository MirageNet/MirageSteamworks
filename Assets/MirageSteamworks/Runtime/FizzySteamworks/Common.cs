using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public class Buffer : IDisposable
    {
        public readonly byte[] Array;
        public int Size;
        private readonly Pool<Buffer> _pool;

        public Buffer(int bufferSize, Pool<Buffer> pool)
        {
            Array = new byte[bufferSize];
            _pool = pool;
        }

        public static Buffer CreateNew(int bufferSize, Pool<Buffer> pool)
        {
            return new Buffer(bufferSize, pool);
        }

        public void Release()
        {
            _pool?.Put(this);
        }
        void IDisposable.Dispose() => Release();
    }

    public abstract class Common
    {
        protected const int MAX_MESSAGES = 256;

        public enum Channel
        {
            Reliable = 0,
            Unreliable = 1,
        }

        /// <summary>
        /// if the <see cref="SteamGameServerNetworkingSockets"/> should be used instead of <see cref="SteamNetworkingSockets"/>
        /// </summary>
        protected readonly bool GameServer;
        protected readonly Pool<Buffer> pool;
        protected readonly int maxBufferSize;
        protected readonly bool noNagle;
        protected readonly IntPtr[] receivePtrs = new IntPtr[MAX_MESSAGES];

        public event Action<SteamConnection> OnConnected;
        public event Action<SteamConnection, Buffer> OnData;
        public event Action<SteamConnection> OnDisconnected;

        protected Common(bool gameServer, int maxBufferSize, bool noNagle)
        {
            GameServer = gameServer;
            this.maxBufferSize = maxBufferSize;
            this.noNagle = noNagle;
            pool = new Pool<Buffer>(Buffer.CreateNew, this.maxBufferSize, 100, 1000, null);
        }

        protected void CallOnConnected(SteamConnection connection) => OnConnected?.Invoke(connection);
        protected void CallOnData(SteamConnection connection, Buffer buffer) => OnData?.Invoke(connection, buffer);
        protected void CallOnDisconnected(SteamConnection connection) => OnDisconnected?.Invoke(connection);

        private int ChanelToSteamConst(Channel channel)
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

        protected unsafe EResult SendSocket(HSteamNetConnection conn, ArraySegment<byte> data, Channel channel)
        {
            var length = data.Count;
            if (length > maxBufferSize)
                throw new ArgumentException($"Data is over maxBufferSize, Size={data.Count}, Max={maxBufferSize}");
            var array = data.Array;

            fixed (byte* arrayPtr = array)
            {
                var sendFlag = this.ChanelToSteamConst(channel);

                var intPtr = new IntPtr(arrayPtr + data.Offset);

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

        protected Buffer CopyToBuffer(SteamNetworkingMessage_t msg)
        {
            Buffer buffer;
            if (msg.m_cbSize > maxBufferSize)
            {
                Debug.LogWarning($"Steam message was greater tha buffer pool, Size={msg.m_cbSize}, Max={maxBufferSize}");
                buffer = new Buffer(msg.m_cbSize, null);
            }
            else
            {
                buffer = pool.Take();
            }
            Marshal.Copy(msg.m_pData, buffer.Array, 0, msg.m_cbSize);
            buffer.Size = msg.m_cbSize;
            return buffer;
        }

        public virtual void Send(SteamConnection connection, ArraySegment<byte> data, Channel channelId)
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
                    InternalDisconnect(connection, "No Connection");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SteamNetworking exception during Send: {ex}");
                InternalDisconnect(connection, "Unexpected Error");
            }
        }

        public abstract void ReceiveData();
        public abstract void FlushData();
        public abstract void Shutdown();

        protected abstract void InternalDisconnect(SteamConnection connection, string reason);
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Buffer : IDisposable
    {
        public readonly byte[] array;
        public int Size;
        private readonly Pool<Buffer> _pool;

        public Buffer(int bufferSize, Pool<Buffer> pool)
        {
            array = new byte[bufferSize];
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
        private readonly Pool<Buffer> pool;
        private readonly int maxBufferSize;
        protected readonly Queue<(SteamConnection conn, Buffer buffer)> receiveQueue = new Queue<(SteamConnection conn, Buffer buffer)>();
        protected readonly IntPtr[] receivePtrs = new IntPtr[MAX_MESSAGES];

        protected Common(bool gameServer)
        {
            GameServer = gameServer;
            // -1 because we store channel
            maxBufferSize = Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend - 1;
            pool = new Pool<Buffer>(Buffer.CreateNew, maxBufferSize, 100, 1000, null);
        }

        public static int ChanelToSteamConst(Channel channel)
        {
            switch (channel)
            {
                case Channel.Reliable:
                    return Constants.k_nSteamNetworkingSend_Reliable;
                case Channel.Unreliable:
                    return Constants.k_nSteamNetworkingSend_Unreliable;
                default:
                    throw new InvalidEnumArgumentException(nameof(channel), (int)channel, typeof(Channel));
            }
        }
        public static Channel SteamConstToChannel(int steamConst)
        {
            switch (steamConst)
            {
                case Constants.k_nSteamNetworkingSend_Reliable:
                    return Channel.Reliable;
                case Constants.k_nSteamNetworkingSend_Unreliable:
                    return Channel.Unreliable;
                default:
                    throw new ArgumentException("Enum value not found", nameof(steamConst));
            }
        }

        protected unsafe EResult SendSocket(HSteamNetConnection conn, ArraySegment<byte> data, Channel channel)
        {
            int length = data.Count;
            if (length > maxBufferSize)
                throw new ArgumentException("Data is over maxBufferSize, Size={data.m_cbSize}, Max={maxBufferSize}");
            byte[] array = data.Array;

            fixed (byte* arrayPtr = array)
            {
                int sendFlag = ChanelToSteamConst(channel);

                var intPtr = new IntPtr(arrayPtr + data.Offset);

                EResult res;
                if (GameServer)
                {
                    res = SteamGameServerNetworkingSockets.SendMessageToConnection(conn, intPtr, (uint)length, sendFlag, out long _);
                }
                else
                {
                    res = SteamNetworkingSockets.SendMessageToConnection(conn, intPtr, (uint)length, sendFlag, out long _);
                }

                if (res != EResult.k_EResultOK)
                {
                    Debug.LogWarning($"Send issue: {res}");
                }
                return res;
            }
        }

        protected Buffer CopyToBuffer(SteamNetworkingMessage_t data)
        {
            Buffer buffer;
            if (data.m_cbSize > maxBufferSize)
            {
                Debug.LogWarning($"Steam message was greater tha buffer pool, Size={data.m_cbSize}, Max={maxBufferSize}");
                buffer = new Buffer(data.m_cbSize, null);
            }
            else
            {
                buffer = pool.Take();
            }
            Marshal.Copy(data.m_pData, buffer.array, 0, data.m_cbSize);
            return buffer;
        }

        public bool Poll()
        {
            if (!CanPoll())
                return false;

            if (receiveQueue.Count == 0)
                ReceiveData();

            return receiveQueue.Count > 0;
        }

        protected abstract bool CanPoll();
        protected abstract void ReceiveData();

        public int Receive(byte[] buffer, out SteamConnection connection)
        {
            (SteamConnection conn, Buffer data) = receiveQueue.Dequeue();
            int size = data.Size;

            System.Buffer.BlockCopy(data.array, 0, buffer, 0, size);
            data.Release();

            connection = conn;
            return data.Size;
        }

        public abstract void Send(SteamConnection conn, ArraySegment<byte> data, Channel channelId);
        public abstract void Shutdown();
    }
}

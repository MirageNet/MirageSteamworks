using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mirage.Logging;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public static class SocketHelper
    {
        public static int CreateDisconnectPacket(byte[] buffer, byte? extraByte = null)
        {
            buffer[0] = (byte)PacketType.Command;
            buffer[1] = (byte)Commands.Disconnect;
            if (extraByte.HasValue)
            {
                buffer[2] = extraByte.Value;
                return 3;
            }
            else
            {
                return 2;
            }
        }
    }

    public class SteamworksSocketFactory : SocketFactory
    {
        public bool GameServer;
        public float ConnectTimeout = 60;
        [Tooltip("Use a max packet size smaller than steams built in one, steams default is 524288 bytes. 1211 is the 1280 MTU minus the headers for ipv4, udp and steam (maybe 41 bytes?) or use 1191 for ipv6")]
        [SerializeField] private int maxPacketSize = 1211;

        public override int MaxPacketSize => Mathf.Min(maxPacketSize, Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend);

        public override ISocket CreateServerSocket()
        {
            var server = new Server(GameServer, MaxPacketSize);
            return new SteamSocket(server);
        }

        public override ISocket CreateClientSocket()
        {
            var client = new Client(ConnectTimeout, GameServer, MaxPacketSize);
            return new SteamSocket(client);
        }

        public override IEndPoint GetBindEndPoint()
        {
            return new SteamEndPoint();
        }

        public override IEndPoint GetConnectEndPoint(string address = null, ushort? port = null)
        {
            var id = ulong.Parse(address);
            var steamId = new CSteamID(id);
            if (steamId.IsValid())
                return new SteamEndPoint(steamId);
            else
                throw new ArgumentException("SteamId is Invalid");
        }
    }

    public class SteamSocket : ISocket
    {
        private static readonly ILogger logger = LogFactory.GetLogger("Mirage.SteamworksSocket.SteamSocket");
        private static readonly ILogger verbose = LogFactory.GetLogger("Mirage.SteamworksSocket.SteamSocket_Verbose");

        private readonly Common common;
        private SteamEndPoint receiveEndPoint;
        private readonly Queue<(SteamConnection conn, Buffer buffer)> receiveQueue = new Queue<(SteamConnection conn, Buffer buffer)>();
        private readonly CancellationTokenSource flushCancelSource = new CancellationTokenSource();

        public SteamSocket(Common common)
        {
            this.common = common;
            this.common.OnData += OnData;
            this.common.OnDisconnected += Common_OnDisconnected;
            FlushLoop(flushCancelSource.Token).Forget();
        }

        private void Common_OnDisconnected(SteamConnection conn)
        {
            var buffer = new Buffer(3, null);
            buffer.Size = SocketHelper.CreateDisconnectPacket(buffer.Array);
            receiveQueue.Enqueue((conn, buffer));
        }

        private async UniTask FlushLoop(CancellationToken token)
        {
            while (true)
            {
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                if (token.IsCancellationRequested)
                    return;

                if (verbose.LogEnabled()) verbose.Log($"Calling FlushData");
                common.FlushData();
            }
        }

        private void OnData(SteamConnection conn, Buffer buffer)
        {
            if (verbose.LogEnabled()) verbose.Log("Data event called");
            receiveQueue.Enqueue((conn, buffer));
        }

        public void Connect(IEndPoint endPoint)
        {
            receiveEndPoint = (SteamEndPoint)endPoint;
            if (logger.LogEnabled()) logger.Log($"Connect to {receiveEndPoint.Connection.SteamID}");
            ((Client)common).Connect(receiveEndPoint.Connection);
        }

        public void Bind(IEndPoint endPoint)
        {
            receiveEndPoint = new SteamEndPoint();
            if (logger.LogEnabled()) logger.Log($"Starting server");
            ((Server)common).Start();
        }

        public void Close()
        {
            if (logger.LogEnabled()) logger.Log($"Closing Socket");
            flushCancelSource.Cancel();
            common.Shutdown();
        }

        public bool Poll()
        {
            if (receiveQueue.Count == 0)
            {
                if (verbose.LogEnabled()) verbose.Log($"Calling ReceiveData");
                common.ReceiveData();
            }

            return receiveQueue.Count > 0;
        }

        public int Receive(byte[] outArray, out IEndPoint endPoint)
        {
            var (conn, buffer) = receiveQueue.Dequeue();
            var size = buffer.Size;

            receiveEndPoint.Connection = conn;
            endPoint = receiveEndPoint;

            System.Buffer.BlockCopy(buffer.Array, 0, outArray, 0, size);
            buffer.Release();
            if (logger.LogEnabled()) logger.Log($"Received {size} bytes from {conn}");
            return size;
        }

        public void Send(IEndPoint endPoint, byte[] packet, int length)
        {
            var conn = ((SteamEndPoint)endPoint).Connection;
            // TODO need channel passthrough for Mirage
            if (logger.LogEnabled()) logger.Log($"Sending {length} bytes to {conn}");
            common.Send(conn, new ArraySegment<byte>(packet, 0, length), Common.Channel.Unreliable);
        }
    }
}


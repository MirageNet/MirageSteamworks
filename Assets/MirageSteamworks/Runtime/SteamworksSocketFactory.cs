using System;
using Mirage.SocketLayer;
using Mirage.SteamworksSocket;
using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamworksSocketFactory : SocketFactory
    {
        public bool GameServer;
        public float ConnectTimeout;

        public override int MaxPacketSize => Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend;

        public override ISocket CreateServerSocket()
        {
            var server = new Server(GameServer);
            return new SteamSocket(server);
        }

        public override ISocket CreateClientSocket()
        {
            var client = new Client(ConnectTimeout, GameServer);
            return new SteamSocket(client);
        }

        public override IEndPoint GetBindEndPoint()
        {
            return new SteamEndPoint();
        }

        public override IEndPoint GetConnectEndPoint(string address = null, ushort? port = null)
        {
            ulong id = ulong.Parse(address);
            var steamId = new CSteamID(id);
            if (steamId.IsValid())
                return new SteamEndPoint(steamId);
            else
                throw new ArgumentException("SteamId is Invalid");
        }
    }

    public class SteamSocket : ISocket
    {
        private readonly Common common;
        private SteamEndPoint receiveEndPoint = new SteamEndPoint();

        public SteamSocket(Common common)
        {
            this.common = common;
        }

        public void Connect(IEndPoint endPoint) => throw new NotSupportedException();

        public void Bind(IEndPoint endPoint)
        {
            ((Server)common).Start();
        }

        public void Close()
        {
            common.Shutdown();
        }

        public bool Poll()
        {
            return common.Poll();
        }

        public int Receive(byte[] buffer, out IEndPoint endPoint)
        {
            int size = common.Receive(buffer, out SteamConnection conn);
            receiveEndPoint.Connection = conn;
            endPoint = receiveEndPoint;
            return size;
        }

        public void Send(IEndPoint endPoint, byte[] packet, int length)
        {
            SteamConnection conn = ((SteamEndPoint)endPoint).Connection;
            // TODO need channel passthrough for Mirage
            common.Send(conn, packet, Common.Channel.Unreliable);
        }
    }
}


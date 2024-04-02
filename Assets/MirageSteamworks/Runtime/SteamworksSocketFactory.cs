using Mirage.SocketLayer;

namespace Mirage.SteamworksSocket
{
    public class SteamworksSocketFactory : SocketFactory
    {
        public override int MaxPacketSize => throw new System.NotImplementedException();

        public override ISocket CreateServerSocket()
        {
            throw new System.NotImplementedException();
        }

        public override ISocket CreateClientSocket()
        {
            throw new System.NotImplementedException();
        }

        public override IEndPoint GetBindEndPoint()
        {
            throw new System.NotImplementedException();
        }

        public override IEndPoint GetConnectEndPoint(string address = null, ushort? port = null)
        {
            throw new System.NotImplementedException();
        }
    }
}


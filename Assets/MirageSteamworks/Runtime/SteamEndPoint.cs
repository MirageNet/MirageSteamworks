using System;
using Mirage.SocketLayer;
using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamEndPoint : IEndPoint
    {
        public SteamConnection Connection;
        public CSteamID HostId;

        public SteamEndPoint() { }
        public SteamEndPoint(CSteamID hostId)
        {
            HostId = hostId;
        }
        private SteamEndPoint(SteamConnection conn)
        {
            Connection = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        IEndPoint IEndPoint.CreateCopy()
        {
            return new SteamEndPoint(Connection);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is SteamEndPoint other))
                return false;

            // both null
            if (Connection == null && other.Connection == null)
                return true;

            return Connection.Equals(other.Connection);
        }

        public override int GetHashCode()
        {
            if (Connection != null)
            {
                return Connection.GetHashCode();
            }
            else
            {
                return HostId.GetHashCode();
            }
        }
    }
}

using System;
using Mirage.SocketLayer;
using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamEndPoint : IEndPoint
    {
        public SteamConnection Connection;

        public SteamEndPoint() { }
        public SteamEndPoint(CSteamID hostId)
        {
            Connection = new SteamConnection(default, hostId);
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
            return Connection != null
                ? Connection.GetHashCode()
                : 0;
        }

        public override string ToString()
        {
            return Connection != null
                 ? Connection.ToString()
                 : "NULL";
        }
    }
}

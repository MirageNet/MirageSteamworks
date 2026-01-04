using System;
using Mirage.SocketLayer;
using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamConnection : IConnectionHandle
    {
        public readonly Common Owner;
        public readonly CSteamID SteamID;
        public HSteamNetConnection ConnId;
        public bool Disconnected;
        /// <summary>Identity used on client to connect to server or host</summary>
        public SteamNetworkingIdentity SteamNetworkingIdentity;

        public SteamConnection(Common owner, CSteamID cSteamID, HSteamNetConnection hConn)
        {
            Owner = owner;
            ConnId = hConn;
            SteamID = cSteamID;
        }

        public override string ToString()
        {
            return $"SteamConnection({SteamID})";
        }

        bool IConnectionHandle.IsStateful => true;
        ISocketLayerConnection IConnectionHandle.SocketLayerConnection { get; set; }
        bool IConnectionHandle.SupportsGracefulDisconnect => true;
        void IConnectionHandle.Disconnect(string gracefulDisconnectReason)
        {
            switch (Owner)
            {
                case Server server:
                    server.Disconnect(this, null, gracefulDisconnectReason);
                    break;
                case Client client:
                    client.Disconnect(null, gracefulDisconnectReason);
                    break;
            }
        }

        IConnectionHandle IConnectionHandle.CreateCopy() => throw new NotSupportedException("Create copy should not be called for Stateful connections");

        public override bool Equals(object obj)
        {
            // each connection should have its own c# object
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode()
        {
            return ConnId.GetHashCode();
        }
    }
}

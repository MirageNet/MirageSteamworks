using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamConnection
    {
        public HSteamNetConnection ConnId;
        public CSteamID SteamID;
        public bool Disconnected;
        /// <summary>Identity used on client to connect to server or host</summary>
        public SteamNetworkingIdentity SteamNetworkingIdentity;

        public SteamConnection(HSteamNetConnection hConn, CSteamID cSteamID)
        {
            ConnId = hConn;
            SteamID = cSteamID;
        }

        public override string ToString()
        {
            return $"SteamConnection({SteamID})";
        }
    }
}

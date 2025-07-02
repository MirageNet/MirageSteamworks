using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamConnection
    {
        public HSteamNetConnection ConnId;
        public CSteamID SteamID;
        public bool Disconnected;

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

using Steamworks;

namespace Mirage.SteamworksSocket
{
    public class SteamConnection
    {
        public readonly HSteamNetConnection id;
        public readonly CSteamID cSteamID;
        public bool Disconnected;

        public SteamConnection(HSteamNetConnection hConn, CSteamID cSteamID)
        {
            id = hConn;
            this.cSteamID = cSteamID;
        }

        public override string ToString()
        {
            return $"SteamConnection({cSteamID})";
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public abstract class Common
    {
        protected const int MAX_MESSAGES = 256;

        public enum Channel
        {
            Reliable = 0,
            Unreliable = 1,
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

        protected EResult SendSocket(HSteamNetConnection conn, byte[] data, Channel channelId)
        {
            // todo why do we append this to end?? (bad allocations)
            Array.Resize(ref data, data.Length + 1);
            data[data.Length - 1] = (byte)channelId;

            var pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr pData = pinnedArray.AddrOfPinnedObject();
            int sendFlag = ChanelToSteamConst(channelId);
#if UNITY_SERVER
            EResult res = SteamGameServerNetworkingSockets.SendMessageToConnection(conn, pData, (uint)data.Length, sendFlag, out long _);
#else
            EResult res = SteamNetworkingSockets.SendMessageToConnection(conn, pData, (uint)data.Length, sendFlag, out long _);
#endif
            if (res != EResult.k_EResultOK)
            {
                Debug.LogWarning($"Send issue: {res}");
            }

            pinnedArray.Free();
            return res;
        }

        protected (byte[], Channel) ProcessMessage(IntPtr ptrs)
        {
            SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs);
            byte[] managedArray = new byte[data.m_cbSize];
            Marshal.Copy(data.m_pData, managedArray, 0, data.m_cbSize);
            SteamNetworkingMessage_t.Release(ptrs);

            var channel = (Channel)managedArray[managedArray.Length - 1];
            Array.Resize(ref managedArray, managedArray.Length - 1);
            return (managedArray, channel);
        }
    }
}

using System;
using Mirage.Logging;
using Mirage.SocketLayer;
using Steamworks;
using UnityEngine;

namespace Mirage.SteamworksSocket
{
    public static class SocketHelper
    {
        public static ReadOnlySpan<byte> CreateDisconnectPacket(Span<byte> buffer, byte? extraByte = null)
        {
            buffer[0] = (byte)PacketType.Command;
            buffer[1] = (byte)Commands.Disconnect;
            if (extraByte.HasValue)
            {
                buffer[2] = extraByte.Value;
                return buffer;
            }
            else
            {
                return buffer[..2];
            }
        }

        public static bool TryReadByte(ReadOnlySpan<byte> packet, int length, int index, out byte value)
        {
            if (length > index)
            {
                value = packet[index];
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
        public static bool IsByteEqual(ReadOnlySpan<byte> packet, int length, int index, byte value)
        {
            return TryReadByte(packet, length, index, out var b) && b == value;
        }
    }

    public class SteamworksSocketFactory : SocketFactory
    {
        public bool GameServer;
        public float ConnectTimeout = 60;
        [Tooltip("Enable to use k_nSteamNetworkingSend_UnreliableNoNagle, which disables the Nagle-like algorithm for unreliable packets. Mirage controls a lot of the flow of packets, so it is better to have this flag as true")]
        public bool noNagle = true;
        [Tooltip("Use a max packet size smaller than steams built in one, steams default is 524288 bytes. 1198 is the 1280 MTU minus the headers for ipv4, udp and steam (largest steam header seems to be 54) or use 1178 for ipv6")]
        [SerializeField] private int maxPacketSize = 1198;

        /// <summary>
        /// Accept callback for steam connections. Used on server.
        /// <para>Must be set before server is started</para>
        /// </summary>
        public Server.AcceptConnectionCallback AcceptCallback;

        public override int MaxPacketSize => Mathf.Min(maxPacketSize, Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend);

        public override ISocket CreateServerSocket()
        {
            var server = new Server(GameServer, MaxPacketSize, noNagle, AcceptCallback);
            return new SteamSocket(server);
        }

        public override ISocket CreateClientSocket()
        {
            var client = new Client(ConnectTimeout, GameServer, MaxPacketSize, noNagle);
            return new SteamSocket(client);
        }

        public override IBindEndPoint GetBindEndPoint()
        {
            return new SteamBindEndPoint();
        }

        public override IConnectEndPoint GetConnectEndPoint(string address = null, ushort? port = null)
        {
            var id = ulong.Parse(address);
            var steamId = new CSteamID(id);
            if (steamId.IsValid())
                return new SteamConnectEndPoint(steamId);
            else
                throw new ArgumentException("SteamId is Invalid");
        }

        private bool? _isSupported;
        public override bool IsSupported
        {
            get
            {
                // Result not cached load it now
                if (!_isSupported.HasValue)
                {
                    try
                    {
                        // Use Steam's internal platform guard
                        // will throw if not supported
                        InteropHelp.TestIfPlatformSupported();
                        _isSupported = true;
                    }
                    catch
                    {
                        _isSupported = false;
                    }
                }

                return _isSupported.Value;
            }
        }
    }

    public class SteamBindEndPoint : IBindEndPoint { }

    public class SteamConnectEndPoint : IConnectEndPoint
    {
        public readonly CSteamID SteamId;

        public SteamConnectEndPoint(CSteamID steamId)
        {
            SteamId = steamId;
        }
    }

    public class SteamSocket : ISocket
    {
        private static readonly ILogger logger = LogFactory.GetLogger("Mirage.SteamworksSocket.SteamSocket");
        private static readonly ILogger verbose = LogFactory.GetLogger("Mirage.SteamworksSocket.SteamSocket_Verbose");

        private readonly Common common;
        private OnData onData;
        private OnDisconnect onDisconnect;

        public SteamSocket(Common common)
        {
            this.common = common;
            this.common.OnConnected += OnConnected;
            this.common.OnData += OnData;
            this.common.OnDisconnected += OnDisconnected;
        }

        private void OnConnected(SteamConnection conn)
        {
            // nothing
            if (verbose.LogEnabled()) verbose.Log("Connected event called");
        }

        private void OnData(SteamConnection conn, ReadOnlySpan<byte> span)
        {
            if (verbose.LogEnabled()) verbose.Log("Data event called");
            if (logger.LogEnabled()) logger.Log($"Received {span.Length} bytes from {conn}");
            onData.Invoke(conn, span);
        }

        private void OnDisconnected(SteamConnection conn)
        {
            Span<byte> span = stackalloc byte[3];
            var data = SocketHelper.CreateDisconnectPacket(span);
            onDisconnect.Invoke(conn, data, null);
        }

        public void Flush()
        {
            if (verbose.LogEnabled()) verbose.Log($"Calling FlushData");
            common.FlushData();
        }

        public IConnectionHandle Connect(IConnectEndPoint endPoint)
        {
            var steamEndPoint = (SteamConnectEndPoint)endPoint;
            if (logger.LogEnabled()) logger.Log($"Connect to {steamEndPoint.SteamId}");
            return ((Client)common).Connect(steamEndPoint.SteamId);
        }

        public void Bind(IBindEndPoint endPoint)
        {
            if (logger.LogEnabled()) logger.Log($"Starting server");
            ((Server)common).Start();
        }

        public void Close()
        {
            if (logger.LogEnabled()) logger.Log($"Closing Socket");
            common.Shutdown();
        }

        public void SetTickEvents(int maxPacketSize, OnData onData, OnDisconnect onDisconnect)
        {
            this.onData = onData;
            this.onDisconnect = onDisconnect;
        }
        public void Tick()
        {
            if (verbose.LogEnabled()) verbose.Log($"Calling {common.GetType().Name}.Tick");
            common.Tick();
        }

        public bool Poll()
        {
            return false;
        }

        public int Receive(Span<byte> outBuffer, out IConnectionHandle handle) => throw new NotSupportedException("Should be using Tick instead of Receive");

        public void Send(IConnectionHandle handle, ReadOnlySpan<byte> packet)
        {
            var conn = (SteamConnection)handle;

            // TODO need channel passthrough for Mirage
            if (logger.LogEnabled()) logger.Log($"Sending {packet.Length} bytes to {conn}");
            common.Send(conn, packet, Common.Channel.Unreliable);
        }
    }
}


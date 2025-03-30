using Korn.Pipes;
using System;

namespace Korn.Service
{
    public class Client
    {
        public Client(ServerConfiguration configuration)
        {
            Configuration = configuration;

            ConnectionID = new ConnectionID(configuration);

            ConnectPipe = new InputPipe(configuration.ConnectConfiguration)
            {
                Connected = OnConnectPipeConnected
            };

            Connection = new ServerConnection(ConnectionID)
            {
                Received = OnReceived,
            };
        }

        public readonly ServerConfiguration Configuration;
        public readonly ConnectionID ConnectionID;
        public readonly ServerConnection Connection;
        public readonly InputPipe ConnectPipe;

        public Action<ServerPacket, uint> Received;

        void OnConnectPipeConnected()
        {
            var bytes = BitConverter.GetBytes(ConnectionID.Indentitifer);
            ConnectPipe.Send(bytes);
        }

        void OnReceived(byte[] bytes)
        {
            PacketSerializer.DeserializeServerPacket(Configuration, bytes, out var packet, out var id);

            Received?.Invoke(packet, id);
        }

        public void Send(ClientPacket packet)
        {
            var bytes = PacketSerializer.SerializeClientPacket(Configuration, packet);
            Connection.Send(bytes);
        }
    }
}
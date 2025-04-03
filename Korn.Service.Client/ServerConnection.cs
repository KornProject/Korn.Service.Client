using Korn.Pipes;
using System;

namespace Korn.Service
{
    public class ServerConnection : Connection
    {
        public ServerConnection(ConnectionID identifier) : base(identifier)
        {
            ConnectionID = identifier;

            InputPipe = new InputPipe(identifier.InServerConfiguration)
            {
                Connected = OnPipeConnected,
                Disconnected = OnPipeDisconnected,
            };

            OutputPipe = new OutputPipe(identifier.InClientConfiguration)
            {
                Received = bytes =>
                {
                    if (Received != null)
                        Received.Invoke(bytes);
                },
                Connected = OnPipeConnected,
                Disconnected = OnPipeDisconnected,
            };
        }

        public readonly ConnectionID ConnectionID;

        public Action<byte[]> Received;
    }    
}
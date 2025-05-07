using Korn.Pipes;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Korn.Service
{
    public class Client
    {
        static Random random = new Random();

        public Client(ServerConfiguration configuration)
        {
            Configuration = configuration;
            ConnectionID = new ConnectionID(configuration);

            InitializeRegisteredPackets();

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

        public Action<ServerPacket> Received;

        void OnConnectPipeConnected()
        {
            var bytes = BitConverter.GetBytes(ConnectionID.Indentitifer);
            ConnectPipe.Send(bytes);
        }

        void OnReceived(byte[] bytes)
        {
            PacketSerializer.DeserializeServerPacket(Configuration, bytes, out var packet, out var id);

            OnReceived(packet, id);
        }

        void OnReceived(ServerPacket packet, uint packetId)
        {
            Received?.Invoke(packet);

            if (packet is ServerCallbackPacket callbackPacket)
            {
                HandleCallback(callbackPacket);
            }
            else
            {
                var handlers = registeredPackets[packetId];
                if (handlers != null)
                    foreach (var handler in handlers)
                        handler.DynamicInvoke(packet);
            }
        }

        public void Send(ClientPacket packet)
        {
            var bytes = PacketSerializer.SerializeClientPacket(Configuration, packet);
            Connection.Send(bytes);
        }

        public void Send<TServerPacket>(ClientCallbackablePacket<TServerPacket> packet, Action<TServerPacket> handler) where TServerPacket : ServerCallbackPacket
        {
            packet.CallbackID = random.Next();
            RegisterCallback(packet, handler);
            Send(packet);
        }

        static TimeSpan callbackWaitTime = TimeSpan.FromSeconds(30);
        public void SendAndWait<TServerPacket>(ClientCallbackablePacket<TServerPacket> packet, Action<TServerPacket> handler) where TServerPacket : ServerCallbackPacket
            => SendAndWait(packet, handler, callbackWaitTime);

        public void SendAndWait<TServerPacket>(ClientCallbackablePacket<TServerPacket> packet, Action<TServerPacket> handler, TimeSpan timeOut) where TServerPacket : ServerCallbackPacket
        {
            packet.CallbackID = random.Next();

            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;
            RegisterCallback(packet, inPacket =>
            {
                handler(inPacket);
                cancellationTokenSource.Cancel();
            });
            Send(packet);

            cancellationToken.WaitHandle.WaitOne(timeOut);
        }

        public void Callback(ServerCallbackablePacket serverPacket, ClientCallbackPacket clientPacket)
        {
            clientPacket.ApplyCallback(serverPacket);
            Send(clientPacket);
        }

        List<Delegate>[] registeredPackets;
        void InitializeRegisteredPackets() => registeredPackets = new List<Delegate>[Configuration.ServerPackets.Count];

        public void UnregisterAll() => InitializeRegisteredPackets();

        public Client Register<T>(Action<T> handler) where T : ServerPacket
        {
            var type = typeof(T);
            var id = Configuration.GetClientPacketID(type);
            if (registeredPackets[id] == null)
                registeredPackets[id] = new List<Delegate>();

            registeredPackets[id].Add(handler);
            return this;
        }

        List<CallbackDelegate> callbacks = new List<CallbackDelegate>();
        void RegisterCallback<TServerPacket>(ClientCallbackablePacket<TServerPacket> callbackablePacket, Action<TServerPacket> handler)
            where TServerPacket : ServerCallbackPacket => RegisterCallback(CallbackDelegate.Create(callbackablePacket.CallbackID, handler));

        void RegisterCallback(CallbackDelegate callback)
        {
            lock (callbacks)
                callbacks.Add(callback);

            EnsureCallbacksChecked();
        }

        void EnsureCallbacksChecked()
        {
            var now = DateTime.Now;
            if (lastCallbacksCheck - now < callbackCheckDelay)
            {
                CheckCallbacks();
                lastCallbacksCheck = now;
            }
        }

        static TimeSpan callbackCheckDelay = TimeSpan.FromMinutes(1);
        DateTime lastCallbacksCheck;
        static TimeSpan callbackLifetime = TimeSpan.FromMinutes(5);
        public void CheckCallbacks()
        {
            var now = DateTime.Now;
            var deadline = now - callbackLifetime;
            for (var i = 0; i < callbacks.Count; i++)
                if (callbacks[i].CreatedAt < deadline)
                    lock (callbacks)
                        callbacks.RemoveRange(i, callbacks.Count - i);
        }

        public void HandleCallback(ServerCallbackPacket callbackPacket)
        {
            var callbackId = callbackPacket.CallbackID;
            for (var i = 0; i < callbacks.Count; i++)
            {
                var callback = callbacks[i];
                if (callback.CallbackID == callbackId)
                {
                    lock (callbacks)
                        callbacks.RemoveAt(i);

                    callback.Invoke(callbackPacket);

                    break;
                }
            }
        }

        public class CallbackDelegate
        {
            CallbackDelegate() { }

            public int CallbackID { get; private set; }
            public Delegate PacketHandler { get; private set; }
            public DateTime CreatedAt { get; private set; } = DateTime.Now;

            public void Invoke(ServerCallbackPacket packet)
            {
                if (PacketHandler != null)
                    PacketHandler.DynamicInvoke(packet);
            }

            public static CallbackDelegate Create<TServerPacket>(int callbackId, Action<TServerPacket> handler) where TServerPacket : ServerCallbackPacket
            {
                var callback = new CallbackDelegate()
                {
                    CallbackID = callbackId,
                    PacketHandler = handler
                };

                return callback;
            }
        }
    }
}
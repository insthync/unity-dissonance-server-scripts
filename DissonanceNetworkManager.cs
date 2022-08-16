using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using LnlM = LiteNetLibManager.LiteNetLibManager;

namespace DissonanceServer
{
    public class DissonanceNetworkManager : LnlM
    {
        [System.Serializable]
        public struct ClientData : INetSerializable
        {
            public long connectionId;
            public string roomName;
            public Vector3 position;

            public void Deserialize(NetDataReader reader)
            {
                connectionId = reader.GetPackedLong();
                roomName = reader.GetString();
                position = reader.GetVector3();
            }

            public void Serialize(NetDataWriter writer)
            {
                writer.PutPackedLong(connectionId);
                writer.Put(roomName);
                writer.PutVector3(position);
            }
        }

        public const ushort OPCODE_JOIN = 1;
        public const ushort OPCODE_SET_POSITION = 2;
        public const ushort OPCODE_SYNC_CLIENTS = 3;
        private readonly ConcurrentDictionary<long, DissonanceClientInstance> clientInstances = new ConcurrentDictionary<long, DissonanceClientInstance>();
        private readonly ConcurrentDictionary<long, ClientData> joinedClients = new ConcurrentDictionary<long, ClientData>();
        private readonly ConcurrentDictionary<string, HashSet<long>> clientsByRoomName = new ConcurrentDictionary<string, HashSet<long>>();

        public DissonanceClientInstance clientInstancePrefab;

        protected override void RegisterMessages()
        {
            RegisterServerMessage(OPCODE_JOIN, OnJoinAtServer);
            RegisterServerMessage(OPCODE_SET_POSITION, OnSetPositionAtServer);
            RegisterClientMessage(OPCODE_SYNC_CLIENTS, OnSyncClientsAtClient);
        }

        public override void OnPeerDisconnected(long connectionId, DisconnectInfo disconnectInfo)
        {
            base.OnPeerDisconnected(connectionId, disconnectInfo);
            RemoveClient(connectionId);
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            // Sync clients position
            foreach (string roomName in clientsByRoomName.Keys)
            {
                // Prepare sending data
                HashSet<long> sendToConnections = clientsByRoomName[roomName];
                ClientData[] filteredClients = new ClientData[sendToConnections.Count];
                int index = 0;
                foreach (long connectionId in sendToConnections)
                {
                    filteredClients[index++] = joinedClients[connectionId];
                }
                NetDataWriter writer = new NetDataWriter();
                TransportHandler.WritePacket(writer, OPCODE_SYNC_CLIENTS);
                writer.PutArray<ClientData>(filteredClients);
                foreach (long connectionId in sendToConnections)
                {
                    ServerSendMessage(connectionId, 0, DeliveryMethod.ReliableOrdered, writer);
                }
            }
        }

        private void RemoveClient(long connectionId)
        {
            if (joinedClients.TryRemove(connectionId, out ClientData client) && clientsByRoomName.ContainsKey(client.roomName))
                clientsByRoomName[client.roomName].Remove(connectionId);
        }

        private void OnJoinAtServer(MessageHandlerData netMsg)
        {
            try
            {
                RemoveClient(netMsg.ConnectionId);
                string roomName = netMsg.Reader.GetString();
                Vector3 position = netMsg.Reader.GetVector3();
                joinedClients[netMsg.ConnectionId] = new ClientData()
                {
                    connectionId = netMsg.ConnectionId,
                    roomName = roomName,
                    position = position,
                };
                if (!clientsByRoomName.ContainsKey(roomName))
                    clientsByRoomName.TryAdd(roomName, new HashSet<long>());
                clientsByRoomName[roomName].Add(netMsg.ConnectionId);
            }
            catch (System.Exception ex)
            {
                Logging.LogException(ex);
                RemoveClient(netMsg.ConnectionId);
            }
        }

        private void OnSetPositionAtServer(MessageHandlerData netMsg)
        {
            if (joinedClients.TryGetValue(netMsg.ConnectionId, out ClientData client))
            {
                client.position = netMsg.Reader.GetVector3();
                joinedClients[netMsg.ConnectionId] = client;
            }
        }

        private void OnSyncClientsAtClient(MessageHandlerData netMsg)
        {
            ClientData[] clients = netMsg.Reader.GetArray<ClientData>();
            HashSet<long> removingClients = new HashSet<long>(clientInstances.Keys);
            ClientData syncingClient;
            // Add/Update client instances by synced client data
            for (int i = 0; i < clients.Length; ++i)
            {
                syncingClient = clients[i];
                if (!clientInstances.ContainsKey(syncingClient.connectionId))
                {
                    // Instantiate new instance for voice chat triggering
                    clientInstances[syncingClient.connectionId] = Instantiate(clientInstancePrefab, syncingClient.position, Quaternion.identity).Setup(syncingClient.connectionId);
                }
                else
                {
                    // Update client data
                    clientInstances[syncingClient.connectionId] = clientInstances[syncingClient.connectionId].SetPosition(syncingClient.position);
                }
                // Remove added/updated client data from removing collection
                removingClients.Remove(syncingClient.connectionId);
            }
            // Remove client instances by entries in removing collection
            foreach (long connectionId in removingClients)
            {
                if (clientInstances.TryRemove(connectionId, out DissonanceClientInstance clientInstance))
                {
                    // Destroy instance
                    Destroy(clientInstance.gameObject);
                }
            }
        }

        /// <summary>
        /// Join with roomName (can use sceneName as roomName) and position
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="position"></param>
        public void Join(string roomName, Vector3 position)
        {
            ClientSendPacket(0, DeliveryMethod.ReliableOrdered, OPCODE_JOIN, (writer) =>
            {
                writer.Put(roomName);
                writer.PutVector3(position);
            });
        }

        /// <summary>
        /// Set client's position
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            ClientSendPacket(0, DeliveryMethod.ReliableOrdered, OPCODE_SET_POSITION, (writer) =>
            {
                writer.PutVector3(position);
            });
        }
    }
}

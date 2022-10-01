using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
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
            public Quaternion rotation;

            public void Deserialize(NetDataReader reader)
            {
                connectionId = reader.GetPackedLong();
                roomName = reader.GetString();
                position = reader.GetVector3();
                rotation = reader.GetQuaternion();
            }

            public void Serialize(NetDataWriter writer)
            {
                writer.PutPackedLong(connectionId);
                writer.Put(roomName);
                writer.PutVector3(position);
                writer.PutQuaternion(rotation);
            }
        }

        public const ushort OPCODE_JOIN = 1;
        public const ushort OPCODE_SET_TRANSFORM = 2;
        public const ushort OPCODE_SYNC_CLIENTS = 3;
        // Client Vars
        public readonly ConcurrentDictionary<long, DissonanceClientInstance> clientInstances = new ConcurrentDictionary<long, DissonanceClientInstance>();
        // Server Vars
        public readonly ConcurrentDictionary<long, ClientData> joinedClients = new ConcurrentDictionary<long, ClientData>();
        public readonly ConcurrentDictionary<string, HashSet<long>> clientsByRoomName = new ConcurrentDictionary<string, HashSet<long>>();
        // Properties
        public static DissonanceNetworkManager Instance { get; private set; }
        // Settings
        public bool nonSingleton;
        public bool startServerOnStart;
        public DissonanceClientInstance clientInstancePrefab;

        private void Awake()
        {
            if (nonSingleton)
                return;
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
        }

        protected override async void Start()
        {
            base.Start();
            if (startServerOnStart)
                await StartServerWithConfig();
        }

        public async Task<bool> StartServerWithConfig()
        {
            var json = await Utils.LoadTextFromStreamingAssets("dissonanceServerConfig.json");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<Config>(json);
                    if (data.networkPort.HasValue)
                    {
                        networkPort = data.networkPort.Value;
                        Logging.Log("Network Port set to " + networkPort);
                    }
                    if (data.maxConnections.HasValue)
                    {
                        maxConnections = data.maxConnections.Value;
                        Logging.Log("Max Connections set to " + maxConnections);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[DissonanceNetworkManager] Unable to read config.");
                    Debug.LogException(ex);
                }
            }
#if UNITY_SERVER
            Application.runInBackground = true;
            Application.targetFrameRate = 10;
#endif
            return StartServer();
        }

        protected override void RegisterMessages()
        {
            RegisterServerMessage(OPCODE_JOIN, OnJoinAtServer);
            RegisterServerMessage(OPCODE_SET_TRANSFORM, OnSetTransformAtServer);
            RegisterClientMessage(OPCODE_SYNC_CLIENTS, OnSyncClientsAtClient);
        }

        public override void OnStartServer()
        {
            Logging.Log("Dissonance Server Started");
            base.OnStartServer();
        }

        public override void OnStopServer()
        {
            Logging.Log("Dissonance Server Stopped");
            base.OnStopServer();
            joinedClients.Clear();
            clientsByRoomName.Clear();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            foreach (DissonanceClientInstance clientInstance in clientInstances.Values)
            {
                Destroy(clientInstance.gameObject);
            }
            clientInstances.Clear();
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
                Quaternion rotation = netMsg.Reader.GetQuaternion();
                joinedClients[netMsg.ConnectionId] = new ClientData()
                {
                    connectionId = netMsg.ConnectionId,
                    roomName = roomName,
                    position = position,
                    rotation = rotation,
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

        private void OnSetTransformAtServer(MessageHandlerData netMsg)
        {
            if (joinedClients.TryGetValue(netMsg.ConnectionId, out ClientData client))
            {
                client.position = netMsg.Reader.GetVector3();
                client.rotation = netMsg.Reader.GetQuaternion();
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
                    clientInstances[syncingClient.connectionId] = Instantiate(clientInstancePrefab, syncingClient.position, syncingClient.rotation).Setup(syncingClient.connectionId);
                }
                else
                {
                    // Update client data
                    clientInstances[syncingClient.connectionId] = clientInstances[syncingClient.connectionId].SetTransform(syncingClient.position, syncingClient.rotation);
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
        /// <param name="rotation"></param>
        public void Join(string roomName, Vector3 position, Quaternion rotation)
        {
            ClientSendPacket(0, DeliveryMethod.ReliableOrdered, OPCODE_JOIN, (writer) =>
            {
                writer.Put(roomName);
                writer.PutVector3(position);
                writer.PutQuaternion(rotation);
            });
        }

        /// <summary>
        /// Set client's position and rotation
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            ClientSendPacket(0, DeliveryMethod.ReliableOrdered, OPCODE_SET_TRANSFORM, (writer) =>
            {
                writer.PutVector3(position);
                writer.PutQuaternion(rotation);
            });
        }
    }
}

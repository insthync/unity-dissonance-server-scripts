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
        public const ushort OPCODE_JOIN = 1;
        public const ushort OPCODE_SET_TRANSFORM = 2;
        public const ushort OPCODE_SYNC_CLIENTS = 3;
        public const ushort OPCODE_LEAVE = 4;
        public readonly ConcurrentDictionary<string, DissonanceClientInstance> clientInstances = new ConcurrentDictionary<string, DissonanceClientInstance>();
        public readonly ConcurrentDictionary<long, Dictionary<string, ClientData>> joinedClients = new ConcurrentDictionary<long, Dictionary<string, ClientData>>();
        public readonly ConcurrentDictionary<string, HashSet<long>> clientsByRoomName = new ConcurrentDictionary<string, HashSet<long>>();
        public static DissonanceNetworkManager Instance { get; private set; }

        public bool startServerOnStart;
        public DissonanceClientInstance clientInstancePrefab;

        private void Awake()
        {
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
            RegisterServerMessage(OPCODE_LEAVE, OnLeaveAtServer);
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
                    filteredClients[index++] = joinedClients[connectionId][roomName];
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
            if (!joinedClients.TryRemove(connectionId, out Dictionary<string, ClientData> joinedRooms))
                return;
            foreach (string roomName in joinedRooms.Keys)
            {
                clientsByRoomName[roomName].Remove(connectionId);
            }
        }

        private void OnJoinAtServer(MessageHandlerData netMsg)
        {
            try
            {
                RemoveClient(netMsg.ConnectionId);
                string roomName = netMsg.Reader.GetString();
                Vector3 position = netMsg.Reader.GetVector3();
                Quaternion rotation = netMsg.Reader.GetQuaternion();
                if (!clientsByRoomName.ContainsKey(roomName))
                    clientsByRoomName.TryAdd(roomName, new HashSet<long>());
                clientsByRoomName[roomName].Add(netMsg.ConnectionId);
                if (!joinedClients.ContainsKey(netMsg.ConnectionId))
                    joinedClients[netMsg.ConnectionId] = new Dictionary<string, ClientData>();
                joinedClients[netMsg.ConnectionId][roomName] = new ClientData()
                {
                    connectionId = netMsg.ConnectionId,
                    roomName = roomName,
                    position = position,
                    rotation = rotation,
                };
            }
            catch (System.Exception ex)
            {
                Logging.LogException(ex);
                RemoveClient(netMsg.ConnectionId);
            }
        }

        private void OnSetTransformAtServer(MessageHandlerData netMsg)
        {
            if (!joinedClients.ContainsKey(netMsg.ConnectionId))
                return;
            string roomName = netMsg.Reader.GetString();
            if (!joinedClients[netMsg.ConnectionId].TryGetValue(roomName, out ClientData client))
                return;
            client.position = netMsg.Reader.GetVector3();
            client.rotation = netMsg.Reader.GetQuaternion();
            joinedClients[netMsg.ConnectionId][roomName] = client;
        }

        private void OnLeaveAtServer(MessageHandlerData netMsg)
        {
            if (!joinedClients.ContainsKey(netMsg.ConnectionId))
                return;
            string roomName = netMsg.Reader.GetString();
            if (!joinedClients[netMsg.ConnectionId].ContainsKey(roomName))
                return;
            joinedClients[netMsg.ConnectionId].Remove(roomName);
        }

        public string GetClientInstanceId(long connectionId, string roomName)
        {
            return $"{connectionId}:{roomName}";
        }

        private void OnSyncClientsAtClient(MessageHandlerData netMsg)
        {
            ClientData[] clients = netMsg.Reader.GetArray<ClientData>();
            HashSet<string> removingClients = new HashSet<string>(clientInstances.Keys);
            ClientData tempClient;
            string tempClientInstanceId;
            // Add/Update client instances by synced client data
            for (int i = 0; i < clients.Length; ++i)
            {
                tempClient = clients[i];
                tempClientInstanceId = GetClientInstanceId(tempClient.connectionId, tempClient.roomName);
                if (!clientInstances.ContainsKey(tempClientInstanceId))
                {
                    // Instantiate new instance for voice chat triggering
                    clientInstances[tempClientInstanceId] = Instantiate(clientInstancePrefab, tempClient.position, tempClient.rotation).Setup(tempClient);
                }
                else
                {
                    // Update client data
                    clientInstances[tempClientInstanceId] = clientInstances[tempClientInstanceId].SetTransform(tempClient.position, tempClient.rotation);
                }
                // Remove added/updated client data from removing collection
                removingClients.Remove(tempClientInstanceId);
            }
            // Remove client instances by entries in removing collection
            foreach (string clientInstanceId in removingClients)
            {
                if (clientInstances.TryRemove(clientInstanceId, out DissonanceClientInstance clientInstance))
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
        /// <param name="roomName"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        public void SetTransform(string roomName, Vector3 position, Quaternion rotation)
        {
            ClientSendPacket(0, DeliveryMethod.Unreliable, OPCODE_SET_TRANSFORM, (writer) =>
            {
                writer.Put(roomName);
                writer.PutVector3(position);
                writer.PutQuaternion(rotation);
            });
        }

        /// <summary>
        /// Leave from joined room
        /// </summary>
        /// <param name="roomName"></param>
        public void Leave(string roomName)
        {
            ClientSendPacket(0, DeliveryMethod.ReliableOrdered, OPCODE_LEAVE, (writer) =>
            {
                writer.Put(roomName);
            });
        }
    }
}

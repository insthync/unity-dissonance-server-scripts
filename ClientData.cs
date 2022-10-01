using LiteNetLib.Utils;
using UnityEngine;

namespace DissonanceServer
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
}

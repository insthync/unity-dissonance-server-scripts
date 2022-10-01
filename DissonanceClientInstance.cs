using Dissonance;
using Dissonance.Integrations.LiteNetLibManager;
using System.Collections.Generic;
using UnityEngine;

namespace DissonanceServer
{
    public class DissonanceClientInstance : MonoBehaviour, IDissonancePlayer, ILnlMPlayer
    {
        public static HashSet<string> JoinedRoomNames { get; private set; } = new HashSet<string>();

        public ClientData ClientData { get; private set; }
        public long ConnectionId { get { return ClientData.connectionId; } }

        public bool IsOwnerClient
        {
            get
            {
                if (playerFunc == null)
                    return false;
                return playerFunc.IsOwnerClient;
            }
        }

        public string PlayerId
        {
            get
            {
                if (playerFunc == null)
                    return string.Empty;
                return playerFunc.PlayerId;
            }
        }

        public Vector3 Position
        {
            get { return transform.position; }
        }

        public Quaternion Rotation
        {
            get { return transform.rotation; }
        }

        public NetworkPlayerType Type
        {
            get
            {
                if (playerFunc == null)
                    return NetworkPlayerType.Unknown;
                return playerFunc.Type;
            }
        }

        public bool IsTracking
        {
            get
            {
                if (playerFunc == null)
                    return false;
                return playerFunc.IsTracking;
            }
        }

        private LnlMPlayerFunc playerFunc;

        public DissonanceClientInstance Setup(ClientData clientData)
        {
            ClientData = clientData;
            playerFunc = new LnlMPlayerFunc(FindObjectOfType<DissonanceComms>(), FindObjectOfType<LnlMCommsNetwork>(), this);
            playerFunc.onSetPlayerId = OnSetPlayerId;
            gameObject.SetActive(true);
            return this;
        }

        public void OnSetPlayerId(bool isOwnerClient, string id)
        {
            if (isOwnerClient)
                JoinedRoomNames.Add(ClientData.roomName);
            gameObject.name = id;
        }

        public DissonanceClientInstance SetTransform(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
            return this;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (playerFunc != null)
                playerFunc.OnEnable();
        }

        private void OnDisable()
        {
            if (playerFunc != null)
                playerFunc.OnDisable();
        }

        private void OnDestroy()
        {
            if (IsOwnerClient)
                JoinedRoomNames.Remove(ClientData.roomName);
            if (playerFunc != null)
                playerFunc.OnDestroy();
        }
    }
}

using Dissonance;
using Dissonance.Integrations.LiteNetLibManager;
using UnityEngine;

namespace DissonanceServer
{
    public class DissonanceClientInstance : MonoBehaviour, IDissonancePlayer, ILnlMPlayer
    {
        public static bool IsJoined { get; private set; }
        private LnlMPlayerFunc playerFunc;

        public long ConnectionId { get; private set; }

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

        public DissonanceClientInstance Setup(long connectionId)
        {
            ConnectionId = connectionId;
            playerFunc = new LnlMPlayerFunc(FindObjectOfType<DissonanceComms>(), FindObjectOfType<LnlMCommsNetwork>(), this);
            playerFunc.onSetPlayerId = OnSetPlayerId;
            gameObject.SetActive(true);
            return this;
        }

        public void OnSetPlayerId(bool isOwnerClient, string id)
        {
            if (isOwnerClient)
                IsJoined = true;
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
                IsJoined = false;
            if (playerFunc != null)
                playerFunc.OnDestroy();
        }
    }
}

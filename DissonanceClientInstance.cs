using Dissonance;
using Dissonance.Integrations.LiteNetLibManager;
using UnityEngine;

namespace DissonanceServer
{
    public class DissonanceClientInstance : MonoBehaviour, IDissonancePlayer
    {
        private LnlMPlayerFunc player;

        public string PlayerId => player.PlayerId;

        public Vector3 Position => player.Position;

        public Quaternion Rotation => player.Rotation;

        public NetworkPlayerType Type => player.Type;

        public bool IsTracking => player.IsTracking;

        public DissonanceClientInstance Setup(long connectionId)
        {
            player = new LnlMPlayerFunc(FindObjectOfType<DissonanceComms>(), FindObjectOfType<LnlMCommsNetwork>(), transform, connectionId);
            player.onSetPlayerId = OnSetPlayerId;
            gameObject.SetActive(true);
            return this;
        }

        public void OnSetPlayerId(string id)
        {
            gameObject.name = id;
        }

        public DissonanceClientInstance SetPosition(Vector3 position)
        {
            transform.position = position;
            return this;
        }

        private void OnEnable()
        {
            if (player != null)
                player.OnEnable();
        }

        private void OnDisable()
        {
            if (player != null)
                player.OnDisable();
        }

        private void OnDestroy()
        {
            if (player != null)
                player.OnDestroy();
        }
    }
}

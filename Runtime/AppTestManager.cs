using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DissonanceServer
{
    public class AppTestManager : MonoBehaviour
    {
        public DissonanceNetworkManager networkManager;
        public string roomName;
        public Vector3 position;
        public Vector3 rotation;
        public bool join;
        public bool setPosition;
#if UNITY_EDITOR
        private void Update()
        {
            if (join)
            {
                join = false;
                networkManager.Join(roomName, position, Quaternion.Euler(rotation), string.Empty);
            }

            if (setPosition)
            {
                setPosition = false;
                networkManager.SetTransform(position, Quaternion.Euler(rotation));
            }
        }
#endif
    }
}

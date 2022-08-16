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
        public bool join;
        public bool setPosition;

        private void Update()
        {
            if (join)
            {
                join = false;
                networkManager.Join(roomName, position);
            }

            if (setPosition)
            {
                setPosition = false;
                networkManager.SetPosition(position);
            }
        }
    }
}

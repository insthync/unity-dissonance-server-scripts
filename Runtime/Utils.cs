using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DissonanceServer
{
    public static class Utils
    {
        public static async Task<string> LoadTextFromStreamingAssets(string fileName)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, fileName);
            Debug.Log("[AppUtils] Loading file from: " + filePath);
            string text;
            if (filePath.Contains("://") || filePath.Contains(":///"))
            {
                var request = UnityWebRequest.Get(filePath);
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    await Task.Yield();
                }
                if (request.IsError())
                {
                    Debug.LogError("[AppUtils] Can't download: " + filePath + " " + request.error);
                    return string.Empty;
                }
                text = request.downloadHandler.text;
            }
            else
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError("[AppUtils] Can't find: " + filePath);
                    return string.Empty;
                }
                text = File.ReadAllText(filePath);
            }
            return text;
        }

        public static bool IsError(this UnityWebRequest unityWebRequest)
        {
#if UNITY_2020_2_OR_NEWER
            var result = unityWebRequest.result;
            return (result == UnityWebRequest.Result.ConnectionError)
                || (result == UnityWebRequest.Result.DataProcessingError)
                || (result == UnityWebRequest.Result.ProtocolError);
#else
            return unityWebRequest.isHttpError || unityWebRequest.isNetworkError;
#endif
        }
    }
}

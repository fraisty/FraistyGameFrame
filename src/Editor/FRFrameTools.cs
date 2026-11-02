#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace FRFrame.Editor
{
    public class FRFrameTools
    {
        [MenuItem("FRFrame/Print/PersistentData Path")]
        public static void PrintPersistentDataPath()
        {
            Debug.Log($"FRFrameTools, Application.persistentDataPath:{Application.persistentDataPath}");
        }
        
        [MenuItem("FRFrame/Print/Data Path")]
        public static void PrintDataPath()
        {
            Debug.Log($"FRFrameTools, Application.dataPath:{Application.dataPath}");
        }
        
        [MenuItem("FRFrame/Print/StreamingAssets Path")]
        public static void PrintStreamingAssetsPath()
        {
            Debug.Log($"FRFrameTools, Application.streamingAssetsPath:{Application.streamingAssetsPath}");
        }
        
        [MenuItem("FRFrame/Print/Open PersistentData Path")]
        public static void OpenPersistentDataPath()
        {
            EditorUtility.OpenWithDefaultApp(Application.persistentDataPath);
        }
    }
}
#endif
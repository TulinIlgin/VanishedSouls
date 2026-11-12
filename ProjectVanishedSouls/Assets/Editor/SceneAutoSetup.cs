#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class SceneAutoSetup
{
    static SceneAutoSetup()
    {
        EditorApplication.delayCall += RunAutoSetup;
    }

    [MenuItem("Tools/Vanished Souls/Sahneyi ve NavMesh'i Güncelle")]
    public static void RunAutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // Sahneyi ve NavMesh'i güncelle
        EnemyAI.RebuildSceneNavMesh();

        foreach (var surface in Object.FindObjectsByType<NavMeshSurface>())
        {
            if (surface != null && surface.gameObject.activeInHierarchy)
                EditorUtility.SetDirty(surface);
        }
    }
}
#endif

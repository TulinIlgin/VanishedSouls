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

        // FBX altındaki "Enemy" objesine yanlışlıkla eklenmiş NavMeshAgent ve EnemyAI varsa kaldır
        foreach (var ai in Object.FindObjectsByType<EnemyAI>())
        {
            if (ai != null && ai.visualModel == null && ai.transform.parent != null && ai.gameObject.name == "Enemy")
            {
                var badAgent = ai.GetComponent<NavMeshAgent>();
                Object.DestroyImmediate(ai);
                if (badAgent != null) Object.DestroyImmediate(badAgent);
            }
        }

        EnemyAI.RebuildSceneNavMesh();

        foreach (var surface in Object.FindObjectsByType<NavMeshSurface>())
        {
            if (surface != null && surface.gameObject.activeInHierarchy)
                EditorUtility.SetDirty(surface);
        }
    }
}
#endif

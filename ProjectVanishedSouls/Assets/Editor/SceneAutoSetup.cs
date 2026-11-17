#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        // Fog of War sistemini kur ve bağla
        SetupFogOfWar();

        foreach (var surface in Object.FindObjectsByType<NavMeshSurface>())
        {
            if (surface != null && surface.gameObject.activeInHierarchy)
                EditorUtility.SetDirty(surface);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Vanished Souls/Fog of War'ı Kur ve Bağla")]
    public static void SetupFogOfWar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Debug.Log("<color=cyan>[FogOfWar]</color> Fog of War sistemi kontrol ediliyor ve kuruluyor...");

        // 1. Materyal ve Mesh varlıklarını yükle
        Material fogMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FogOfWar.mat");
        if (fogMat == null)
            fogMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Fogg.mat");

        Mesh fogMeshAsset = null;
        GameObject fbxObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FogMesh.fbx");
        if (fbxObj != null)
        {
            var mf = fbxObj.GetComponentInChildren<MeshFilter>();
            if (mf != null) fogMeshAsset = mf.sharedMesh;
        }

        if (fogMeshAsset == null)
        {
            // Alt varlık olarak ara
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/FogMesh.fbx"))
            {
                if (asset is Mesh m)
                {
                    fogMeshAsset = m;
                    break;
                }
            }
        }

        // 2. Sahnede FogPlane objesini bul veya oluştur
        GameObject fogPlaneObj = GameObject.Find("FogPlane");
        if (fogPlaneObj == null)
        {
            fogPlaneObj = new GameObject("FogPlane");
            Undo.RegisterCreatedObjectUndo(fogPlaneObj, "Create FogPlane");
            Debug.Log("<color=green>[FogOfWar]</color> Sahnede FogPlane oluşturuldu.");
        }

        // Katman ve Transform ayarları (Haritanın üzerinde yatay)
        fogPlaneObj.transform.position = new Vector3(0f, 1.25f, 0f);
        fogPlaneObj.transform.rotation = Quaternion.identity;
        fogPlaneObj.transform.localScale = Vector3.one;

        // MeshFilter
        var meshFilter = fogPlaneObj.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = fogPlaneObj.AddComponent<MeshFilter>();
        if (fogMeshAsset != null) meshFilter.sharedMesh = fogMeshAsset;

        // MeshRenderer
        var meshRenderer = fogPlaneObj.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = fogPlaneObj.AddComponent<MeshRenderer>();
        if (fogMat != null) meshRenderer.sharedMaterial = fogMat;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // MeshCollider varsa kaldır (kullanıcı collider istemiyor)
        var meshCol = fogPlaneObj.GetComponent<Collider>();
        if (meshCol != null) Object.DestroyImmediate(meshCol);

        // 3. FogOfWar bileşeni
        var fogOfWar = fogPlaneObj.GetComponent<FogOfWar>();
        if (fogOfWar == null) fogOfWar = fogPlaneObj.AddComponent<FogOfWar>();

        var serializedFow = new SerializedObject(fogOfWar);
        var fowMeshFilterProp = serializedFow.FindProperty("fogPlaneMeshFilter");
        if (fowMeshFilterProp != null) fowMeshFilterProp.objectReferenceValue = meshFilter;

        // 4. Takım Üyeleri (Aria ve Lumen) yapılandırması
        var aria = Object.FindAnyObjectByType<AriaController>();
        var lumen = Object.FindAnyObjectByType<LumenController>();

        var teamMembersProp = serializedFow.FindProperty("teamMembers");
        if (teamMembersProp != null)
        {
            teamMembersProp.ClearArray();
            int index = 0;

            if (aria != null)
            {
                teamMembersProp.InsertArrayElementAtIndex(index);
                var elem = teamMembersProp.GetArrayElementAtIndex(index);
                elem.FindPropertyRelative("name").stringValue = "Aria";
                elem.FindPropertyRelative("character").objectReferenceValue = aria.transform;
                elem.FindPropertyRelative("visionRadius").floatValue = 12f;
                elem.FindPropertyRelative("visionCenterOffset").vector3Value = new Vector3(1f, 0f, -1f);
                index++;
            }

            if (lumen != null)
            {
                teamMembersProp.InsertArrayElementAtIndex(index);
                var elem = teamMembersProp.GetArrayElementAtIndex(index);
                elem.FindPropertyRelative("name").stringValue = "Lumen";
                elem.FindPropertyRelative("character").objectReferenceValue = lumen.transform;
                elem.FindPropertyRelative("visionRadius").floatValue = lumen.fogVisionRadius > 0f ? lumen.fogVisionRadius : 6f;
                elem.FindPropertyRelative("visionCenterOffset").vector3Value = new Vector3(1f, 0f, -1f);
                index++;
            }
        }

        serializedFow.ApplyModifiedProperties();
        EditorUtility.SetDirty(fogOfWar);
        EditorUtility.SetDirty(fogPlaneObj);

        // 5. Lambalara FogRevealer ekle
        foreach (var light in Object.FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional)
            {
                var rev = light.GetComponent<FogRevealer>();
                if (rev == null)
                {
                    rev = light.gameObject.AddComponent<FogRevealer>();
                    rev.Radius = light.range > 0f ? light.range : 8f;
                    EditorUtility.SetDirty(light.gameObject);
                }
            }
        }

        // 6. Düşmanlara FogVisibility ekle
        foreach (var enemy in Object.FindObjectsByType<EnemyAI>())
        {
            var vis = enemy.GetComponent<FogVisibility>();
            if (vis == null)
            {
                vis = enemy.gameObject.AddComponent<FogVisibility>();
                EditorUtility.SetDirty(enemy.gameObject);
            }
        }

        // 7. LumenController ayarları
        if (lumen != null)
        {
            if (lumen.fogVisionRadius <= 0f) lumen.fogVisionRadius = 6f;
            if (lumen.redSilhouetteDetectionRadius <= 0f) lumen.redSilhouetteDetectionRadius = 25f;
            EditorUtility.SetDirty(lumen.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("<color=green>[FogOfWar]</color> Fog of War kurulumu başarıyla tamamlandı!");
    }
}
#endif

using UnityEditor;
using UnityEngine;

/// <summary>
/// Outils de setup rapide pour le projet IPSSI-WAR.
/// Menu : IPSSI-WAR > ...
/// </summary>
public static class IPSSISetup
{
    // ────────────────────────────────────────────────────────────────────
    // 1) Crée le prefab HealthPickup
    // ────────────────────────────────────────────────────────────────────
    [MenuItem("IPSSI-WAR/Create HealthPickup Prefab")]
    private static void CreateHealthPickupPrefab()
    {
        // Crée un GameObject sphère verte
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "HealthPickup";
        go.transform.localScale = Vector3.one * 0.6f;

        // Couleur verte
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.1f, 0.85f, 0.25f);
        mat.SetFloat("_Metallic", 0.3f);
        mat.SetFloat("_Glossiness", 0.7f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Collider en trigger
        var col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;

        // Rigidbody cinématique (obligatoire pour OnTriggerEnter côté NGO)
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Scripts réseau
        go.AddComponent<Unity.Netcode.NetworkObject>();
        go.AddComponent<HealthPickup>();

        // Sauvegarde le prefab
        const string folder = "Assets/Prefabs";
        if (!System.IO.Directory.Exists(folder))
            System.IO.Directory.CreateDirectory(folder);

        string prefabPath = folder + "/HealthPickup.prefab";
        bool success;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out success);
        Object.DestroyImmediate(go);

        if (!success)
        {
            Debug.LogError("[IPSSI-WAR] Echec création prefab HealthPickup.");
            return;
        }

        // Enregistre le matériau séparément pour éviter les refs perdues
        AssetDatabase.AddObjectToAsset(mat, prefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[IPSSI-WAR] Prefab créé : {prefabPath}");

        // Sélectionne le prefab dans le Project panel
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);

        EditorUtility.DisplayDialog(
            "HealthPickup créé !",
            "Prefab sauvegardé dans Assets/Prefabs/HealthPickup.prefab\n\n" +
            "Prochaine étape :\n" +
            "1. Ouvre le NetworkManager dans la scène\n" +
            "2. Dans Network Prefabs List, ajoute HealthPickup.prefab\n" +
            "3. Assigne le prefab dans HealthPickupSpawner > m_HealthPickupPrefab",
            "OK");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2) Corrige la HP de départ du prefab Tank
    // ────────────────────────────────────────────────────────────────────
    [MenuItem("IPSSI-WAR/Fix Tank HP (500)")]
    private static void FixTankHP()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab Tank");
        int fixed_count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var health = prefab.GetComponent<TankHealth>();
            if (health == null) continue;
            if (health.m_StartingHealth != 500f)
            {
                health.m_StartingHealth = 500f;
                EditorUtility.SetDirty(prefab);
                fixed_count++;
                Debug.Log($"[IPSSI-WAR] Tank HP mis à 500 : {path}");
            }
        }
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Fix Tank HP", $"{fixed_count} prefab(s) mis à jour (HP = 500).", "OK");
    }
}

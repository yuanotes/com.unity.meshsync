using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.MeshSync.Editor.Tests {
internal class MeshSyncPrefabExportTests {
    private const string AssetDir = "Assets/MeshSyncPrefabExportTests/Assets";
    private const string MeshDir = "Assets/MeshSyncPrefabExportTests/Meshes";
    private const string MaterialDir = "Assets/MeshSyncPrefabExportTests/Materials";
    private const string PrefabDir = "Assets/MeshSyncPrefabExportTests/Prefabs";

    [SetUp]
    public void SetUp() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        AssetDatabase.DeleteAsset("Assets/MeshSyncPrefabExportTests");
        AssetDatabase.CreateFolder("Assets", "MeshSyncPrefabExportTests");
        AssetDatabase.CreateFolder("Assets/MeshSyncPrefabExportTests", "Assets");
        AssetDatabase.CreateFolder("Assets/MeshSyncPrefabExportTests", "Meshes");
        AssetDatabase.CreateFolder("Assets/MeshSyncPrefabExportTests", "Materials");
        AssetDatabase.CreateFolder("Assets/MeshSyncPrefabExportTests", "Prefabs");
    }

    [TearDown]
    public void TearDown() {
        AssetDatabase.DeleteAsset("Assets/MeshSyncPrefabExportTests");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
    }

    [Test]
    public void NewServerKeepsAssetDirAndDefaultExportDirs() {
        GameObject serverObject = new GameObject("MeshSyncServer");
        MeshSyncServer server = serverObject.AddComponent<MeshSyncServer>();
        server.Init(AssetDir);

        Assert.AreEqual(AssetDir, server.GetAssetsFolder());
        Assert.AreEqual(MeshSyncConstants.DEFAULT_MESHES_PATH, server.GetExportMeshesDir());
        Assert.AreEqual(MeshSyncConstants.DEFAULT_MATERIALS_PATH, server.GetExportMaterialsDir());
        Assert.AreEqual(MeshSyncConstants.DEFAULT_PREFABS_PATH, server.GetExportPrefabDir());
    }

    [Test]
    public void ExportPrefabsCreatesPrefabReferencingExportedMeshAndMaterialAssets() {
        MeshSyncServer server = CreateServer();
        GameObject model = CreateSyncedModel(server, "CampModel");

        server.ExportPrefabs();

        string prefabPath = Path.Combine(PrefabDir, "CampModel.prefab").Replace("\\", "/");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.IsNotNull(prefab);

        MeshFilter prefabMeshFilter = prefab.GetComponent<MeshFilter>();
        MeshRenderer prefabRenderer = prefab.GetComponent<MeshRenderer>();
        Assert.IsNotNull(prefabMeshFilter);
        Assert.IsNotNull(prefabRenderer);
        Assert.That(AssetDatabase.GetAssetPath(prefabMeshFilter.sharedMesh), Does.StartWith(MeshDir));
        Assert.That(AssetDatabase.GetAssetPath(prefabRenderer.sharedMaterial), Does.StartWith(MaterialDir));
        Assert.AreSame(model.GetComponent<MeshFilter>().sharedMesh, prefabMeshFilter.sharedMesh);
        Assert.AreSame(model.GetComponent<MeshRenderer>().sharedMaterial, prefabRenderer.sharedMaterial);
        Assert.AreEqual(PrefabInstanceStatus.NotAPrefab, PrefabUtility.GetPrefabInstanceStatus(model));
    }

    [Test]
    public void ExportMeshesAndMaterialsUseConfiguredExportDirs() {
        MeshSyncServer server = CreateServer();
        CreateSyncedModel(server, "SplitExportModel");

        server.ExportMeshes();
        server.ExportMaterials();

        Mesh exportedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Path.Combine(MeshDir, "SplitExportModelMesh.asset").Replace("\\", "/"));
        Material exportedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(MaterialDir, "SplitExportModelMaterial.mat").Replace("\\", "/"));
        Assert.IsNotNull(exportedMesh);
        Assert.IsNotNull(exportedMaterial);
    }

    [Test]
    public void ExportMeshesAndMaterialsCopyExistingAssetsIntoConfiguredExportDirs() {
        MeshSyncServer server = CreateServer();
        GameObject model = CreateSyncedModel(server, "ExistingAssetModel");

        Mesh sourceMesh = model.GetComponent<MeshFilter>().sharedMesh;
        Material sourceMaterial = model.GetComponent<MeshRenderer>().sharedMaterial;
        string sourceMeshPath = Path.Combine(AssetDir, "ExistingAssetModelMesh.asset").Replace("\\", "/");
        string sourceMaterialPath = Path.Combine(AssetDir, "ExistingAssetModelMaterial.mat").Replace("\\", "/");
        AssetDatabase.CreateAsset(sourceMesh, sourceMeshPath);
        AssetDatabase.CreateAsset(sourceMaterial, sourceMaterialPath);
        AssetDatabase.SaveAssets();

        server.ExportMeshes();
        server.ExportMaterials();

        string exportedMeshPath = Path.Combine(MeshDir, "ExistingAssetModelMesh.asset").Replace("\\", "/");
        string exportedMaterialPath = Path.Combine(MaterialDir, "ExistingAssetModelMaterial.mat").Replace("\\", "/");
        Mesh exportedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(exportedMeshPath);
        Material exportedMaterial = AssetDatabase.LoadAssetAtPath<Material>(exportedMaterialPath);
        Assert.IsNotNull(exportedMesh);
        Assert.IsNotNull(exportedMaterial);
        Assert.AreSame(exportedMesh, model.GetComponent<MeshFilter>().sharedMesh);
        Assert.AreSame(exportedMaterial, model.GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void ExportMeshesSavesDirtyMeshAlreadyInExportMeshesDir() {
        MeshSyncServer server = CreateServer();
        GameObject model = CreateSyncedModel(server, "DirtyExistingMeshModel");
        Mesh mesh = model.GetComponent<MeshFilter>().sharedMesh;
        string meshPath = Path.Combine(MeshDir, "DirtyExistingMeshModelMesh.asset").Replace("\\", "/");
        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();

        EditorUtility.SetDirty(mesh);
        Assert.IsTrue(EditorUtility.IsDirty(mesh));

        server.ExportMeshes();

        Assert.IsFalse(EditorUtility.IsDirty(mesh));
    }

    [Test]
    public void ExportPrefabsSkipsExistingPrefabWithMatchingModelName() {
        MeshSyncServer server = CreateServer();
        CreateSyncedModel(server, "ExistingModel");

        string prefabPath = Path.Combine(PrefabDir, "ExistingModel.prefab").Replace("\\", "/");
        GameObject existingRoot = new GameObject("ManualExistingRoot");
        new GameObject("ManualOnlyChild").transform.SetParent(existingRoot.transform, false);
        PrefabUtility.SaveAsPrefabAsset(existingRoot, prefabPath);
        Object.DestroyImmediate(existingRoot);

        server.ExportPrefabs();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.IsNotNull(prefab);
        Assert.IsNotNull(prefab.transform.Find("ManualOnlyChild"));
        Assert.IsNull(prefab.GetComponent<MeshFilter>());

        Transform displayedModel = server.GetRootObject().Find("ExistingModel");
        Assert.IsNotNull(displayedModel);
        Assert.AreEqual(PrefabInstanceStatus.NotAPrefab, PrefabUtility.GetPrefabInstanceStatus(displayedModel.gameObject));
        Assert.IsNotNull(displayedModel.GetComponent<MeshFilter>());
    }

    [Test]
    public void AfterUpdateSceneWarnsWhenRootChildrenSharePrefabPath() {
        MeshSyncServer server = CreateServer();
        new GameObject("DuplicateName").transform.SetParent(server.GetRootObject(), false);
        new GameObject(" DuplicateName ").transform.SetParent(server.GetRootObject(), false);

        LogAssert.Expect(LogType.Warning, new Regex("DuplicateName\\.prefab"));

        server.AfterUpdateScene();
    }

    private static MeshSyncServer CreateServer() {
        GameObject serverObject = new GameObject("MeshSyncServer");
        MeshSyncServer server = serverObject.AddComponent<MeshSyncServer>();
        server.Init(AssetDir);
        server.SetExportMeshesDir(MeshDir);
        server.SetExportMaterialsDir(MaterialDir);
        server.SetExportPrefabDir(PrefabDir);
        return server;
    }

    private static GameObject CreateSyncedModel(MeshSyncServer server, string name) {
        GameObject model = new GameObject(name);
        model.transform.SetParent(server.GetRootObject(), false);

        Mesh mesh = new Mesh { name = name + "Mesh" };
        mesh.vertices = new[] {
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateNormals();

        MeshFilter meshFilter = model.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        Material material = new Material(Shader.Find("Standard")) { name = name + "Material" };
        MeshRenderer meshRenderer = model.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;

        server.materialList.Add(new MaterialHolder {
            id = 0,
            index = 0,
            name = material.name,
            material = material
        });

        server.GetClientObjects().Add("/" + name, new EntityRecord {
            dataType = EntityType.Mesh,
            index = 0,
            go = model,
            trans = model.transform,
            mesh = mesh,
            meshFilter = meshFilter,
            meshRenderer = meshRenderer,
            materialIDs = new[] { 0 },
            recved = true
        });

        return model;
    }
}
} //end namespace

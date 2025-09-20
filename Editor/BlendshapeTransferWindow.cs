// BlendshapeTransferWindow.cs
// Unity Editor tool for transferring blendshapes with adjustable search transform
// Place this file in an "Editor" folder in your Unity project.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class BlendshapeTransferWindow : EditorWindow
{
    SkinnedMeshRenderer sourceRenderer;
    SkinnedMeshRenderer targetRenderer;
    string outputMeshName = "TargetMesh_WithBlendshapes";

    // Basic params
    float maxSearchDistance = 0.1f;
    bool useKDTree = true;

    // Search adjustment parameters (user-tweakable)
    Vector3 searchOffset = Vector3.zero;         // in world meters, added to target world position
    Vector3 searchRotationEuler = Vector3.zero; // degrees applied around origin (world space)
    Vector3 searchScale = Vector3.one;          // scale multiplier applied in world space

    Dictionary<string, Vector3> perShapeOffsets = new Dictionary<string, Vector3>();

    // UI state
    Vector2 scrollPos;
    bool[] selectedBlendshapes;

    // Persistence keys
    const string PREF_PREFIX = "BlendshapeTransfer_";

    [MenuItem("Tools/Blendshape Transfer")]
    static void OpenWindow()
    {
        var w = GetWindow<BlendshapeTransferWindow>("Blendshape Transfer");
        w.minSize = new Vector2(480, 460);
    }

    void OnEnable()
    {
        // load saved prefs
        searchOffset.x = EditorPrefs.GetFloat(PREF_PREFIX + "SearchOffsetX", 0f);
        searchOffset.y = EditorPrefs.GetFloat(PREF_PREFIX + "SearchOffsetY", 0f);
        searchOffset.z = EditorPrefs.GetFloat(PREF_PREFIX + "SearchOffsetZ", 0f);
        searchRotationEuler.x = EditorPrefs.GetFloat(PREF_PREFIX + "SearchRotX", 0f);
        searchRotationEuler.y = EditorPrefs.GetFloat(PREF_PREFIX + "SearchRotY", 0f);
        searchRotationEuler.z = EditorPrefs.GetFloat(PREF_PREFIX + "SearchRotZ", 0f);
        searchScale.x = EditorPrefs.GetFloat(PREF_PREFIX + "SearchScaleX", 1f);
        searchScale.y = EditorPrefs.GetFloat(PREF_PREFIX + "SearchScaleY", 1f);
        searchScale.z = EditorPrefs.GetFloat(PREF_PREFIX + "SearchScaleZ", 1f);
    }

    void OnDisable()
    {
        // nothing
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Source (Blendshapeを持つメッシュ)", EditorStyles.boldLabel);
        var newSource = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(sourceRenderer, typeof(SkinnedMeshRenderer), true);
        if (newSource != sourceRenderer)
        {
            sourceRenderer = newSource;
            if (sourceRenderer != null && sourceRenderer.sharedMesh != null)
                selectedBlendshapes = new bool[sourceRenderer.sharedMesh.blendShapeCount];
            else
                selectedBlendshapes = null;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target (Blendshapeを付与するメッシュ)", EditorStyles.boldLabel);
        targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(targetRenderer, typeof(SkinnedMeshRenderer), true);

        EditorGUILayout.Space();
        outputMeshName = EditorGUILayout.TextField("出力メッシュ名", outputMeshName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("探索設定 (探索位置を手動補正できます)", EditorStyles.boldLabel);
        maxSearchDistance = EditorGUILayout.FloatField(new GUIContent("最大探索距離 (m)", "最近傍探索での最大距離。大きすぎると誤マッチ、小さすぎると未割当てになります。"), maxSearchDistance);
        useKDTree = EditorGUILayout.Toggle("KD-Tree を使用", useKDTree);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("探索補正", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Target 頂点の探索位置に対してオフセット/回転/スケールを適用します。転送結果を見ながら微調整してください。", MessageType.Info);

        searchOffset = EditorGUILayout.Vector3Field(new GUIContent("探索オフセット (m)", "ワールド座標に加算されるオフセット"), searchOffset);
        searchRotationEuler = EditorGUILayout.Vector3Field(new GUIContent("探索回転 (deg)", "ワールド座標に適用する回転 (オイラー)"), searchRotationEuler);
        searchScale = EditorGUILayout.Vector3Field(new GUIContent("探索スケール", "ワールド座標に適用するスケール (乗算)"), searchScale);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("補正値保存")) SaveSearchParams();
        if (GUILayout.Button("補正値リセット")) ResetSearchParams();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        if (sourceRenderer != null && sourceRenderer.sharedMesh != null)
        {
            EditorGUILayout.LabelField("転送するシェイプキー", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(140));
            var mesh = sourceRenderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                selectedBlendshapes[i] = EditorGUILayout.ToggleLeft(mesh.GetBlendShapeName(i), selectedBlendshapes[i]);
                if (selectedBlendshapes[i])
                {
                    if (!perShapeOffsets.ContainsKey(mesh.GetBlendShapeName(i))) perShapeOffsets[mesh.GetBlendShapeName(i)] = Vector3.zero;
                    perShapeOffsets[mesh.GetBlendShapeName(i)] = EditorGUILayout.Vector3Field("  Offset", perShapeOffsets[mesh.GetBlendShapeName(i)]);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(sourceRenderer == null || targetRenderer == null || selectedBlendshapes == null);
        if (GUILayout.Button("シェイプキー転送 実行") )
        {
            TransferBlendshapes_AdjustableSearch();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("注意: トポロジー差や大きな座標差は転送精度に影響します。補正値を少しずつ変えて結果を確認してください。", MessageType.Info);
    }

    void SaveSearchParams()
    {
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchOffsetX", searchOffset.x);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchOffsetY", searchOffset.y);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchOffsetZ", searchOffset.z);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchRotX", searchRotationEuler.x);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchRotY", searchRotationEuler.y);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchRotZ", searchRotationEuler.z);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchScaleX", searchScale.x);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchScaleY", searchScale.y);
        EditorPrefs.SetFloat(PREF_PREFIX + "SearchScaleZ", searchScale.z);
        EditorUtility.DisplayDialog("保存", "補正値を保存しました。", "OK");
    }

    void ResetSearchParams()
    {
        searchOffset = Vector3.zero;
        searchRotationEuler = Vector3.zero;
        searchScale = Vector3.one;
    }

    // ------------------ Core Transfer Logic ------------------
    void TransferBlendshapes_AdjustableSearch()
    {
        Mesh meshA = sourceRenderer.sharedMesh;
        Mesh meshB = targetRenderer.sharedMesh;

        if (meshA == null || meshB == null)
        {
            EditorUtility.DisplayDialog("エラー", "Source または Target の Mesh が見つかりません。", "OK");
            return;
        }

        Vector3[] srcVertices = meshA.vertices;
        Vector3[] tgtVertices = meshB.vertices;

        // Build searcher from source vertices (source-local coords)

        IPointSearcher searcher = useKDTree ? (IPointSearcher)new KDTree(srcVertices) : new BruteForceSearcher(srcVertices);

        // Prepare copy of target mesh
        Mesh newMesh = Instantiate(meshB);
        string folder = "Assets/BlendshapeTransfer_Output";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "BlendshapeTransfer_Output");
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, outputMeshName + ".asset"));
        AssetDatabase.CreateAsset(newMesh, path);

        int blendCount = meshA.blendShapeCount;
        for (int bi = 0; bi < blendCount; bi++)
        {
            if (!selectedBlendshapes[bi]) continue;

            string shapeName = meshA.GetBlendShapeName(bi);
            int frameCount = meshA.GetBlendShapeFrameCount(bi);
            int useFrame = Mathf.Max(0, frameCount - 1);
            float frameWeight = meshA.GetBlendShapeFrameWeight(bi, useFrame);

            Vector3[] deltaSrc = new Vector3[meshA.vertexCount];
            Vector3[] dn = new Vector3[meshA.vertexCount];
            Vector3[] dt = new Vector3[meshA.vertexCount];
            meshA.GetBlendShapeFrameVertices(bi, useFrame, deltaSrc, dn, dt);

            Vector3[] deltaTgt = new Vector3[newMesh.vertexCount];

            EditorUtility.DisplayProgressBar("シェイプ転送", $"Blendshape: {shapeName}", 0f);
            for (int ti = 0; ti < newMesh.vertexCount; ti++)
            {
                if ((ti & 255) == 0) EditorUtility.DisplayProgressBar("シェイプ転送", $"処理中 {ti}/{newMesh.vertexCount}", (float)ti / newMesh.vertexCount);

                // Target local -> world
                Vector3 tgtLocal = tgtVertices[ti];
                Vector3 worldPos = targetRenderer.transform.TransformPoint(tgtLocal);

                // Apply user search-space adjustments (in world space)
                Vector3 adjustedWorld = ApplySearchTransform(worldPos);

                // Convert adjusted world to source local
                Vector3 srcSpacePos = sourceRenderer.transform.InverseTransformPoint(adjustedWorld);

                // Find nearest source index within maxSearchDistance

                int nearest = searcher.FindNearest(srcSpacePos, maxSearchDistance);
                if (nearest >= 0 && deltaSrc[nearest].magnitude > 1e-6)
                {
                    // copy delta from source nearest
                    Vector3 offset = perShapeOffsets[meshA.GetBlendShapeName(bi)];
                    offset = new Vector3(offset.x * deltaSrc[nearest].x, offset.y * deltaSrc[nearest].y, offset.z * deltaSrc[nearest].z);
                    deltaTgt[ti] = deltaSrc[nearest] + offset;
                }
                else
                {
                    deltaTgt[ti] = Vector3.zero;
                }
            }
            EditorUtility.ClearProgressBar();

            Vector3[] zeroNormals = new Vector3[newMesh.vertexCount];
            Vector3[] zeroTangents = new Vector3[newMesh.vertexCount];
            newMesh.AddBlendShapeFrame(shapeName, frameWeight, deltaTgt, zeroNormals, zeroTangents);
        }

        EditorUtility.SetDirty(newMesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完了", "出力メッシュを作成しました: " + path, "OK");
    }

    Vector3 ApplySearchTransform(Vector3 worldPos)
    {
        // Apply offset
        Vector3 pos = worldPos + searchOffset;
        // Apply rotation around origin (world) using Euler
        Quaternion rot = Quaternion.Euler(searchRotationEuler);
        pos = rot * pos;
        // Apply scale (around origin)
        pos = Vector3.Scale(pos, searchScale);
        return pos;
    }

    // ------------------ Simple Point Searchers ------------------
    interface IPointSearcher
    {
        int FindNearest(Vector3 p, float maxDistance);
        Vector3 GetPoint(int index);
    }

    class BruteForceSearcher : IPointSearcher
    {
        Vector3[] pts;
        public BruteForceSearcher(Vector3[] pts) { this.pts = pts; }
        public int FindNearest(Vector3 p, float maxDistance)
        {
            float sqMax = maxDistance * maxDistance;
            int idx = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = (pts[i] - p).sqrMagnitude;
                if (d < best) { best = d; idx = i; }
            }
            if (best <= sqMax) return idx; return -1;
        }
        public Vector3 GetPoint(int index) => pts[index];
    }

    class KDTree : IPointSearcher
    {
        KDNode root;
        Vector3[] pts;
        public KDTree(Vector3[] pts)
        {
            this.pts = pts;
            List<int> indices = new List<int>(pts.Length);
            for (int i = 0; i < pts.Length; i++) indices.Add(i);
            root = Build(pts, indices, 0);
        }

        KDNode Build(Vector3[] ptsArr, List<int> indices, int depth)
        {
            if (indices.Count == 0) return null;
            int axis = depth % 3;
            indices.Sort((a, b) => ptsArr[a][axis].CompareTo(ptsArr[b][axis]));
            int mid = indices.Count / 2;
            KDNode node = new KDNode();
            node.index = indices[mid];
            node.point = ptsArr[node.index];
            var leftIdx = indices.GetRange(0, mid);
            var rightIdx = indices.GetRange(mid + 1, indices.Count - (mid + 1));
            node.left = Build(ptsArr, leftIdx, depth + 1);
            node.right = Build(ptsArr, rightIdx, depth + 1);
            return node;
        }

        public int FindNearest(Vector3 p, float maxDistance)
        {
            float sqMax = maxDistance * maxDistance;
            int bestIdx = -1; float bestDist = float.PositiveInfinity;
            Search(root, p, 0, ref bestIdx, ref bestDist);
            if (bestDist <= sqMax) return bestIdx; return -1;
        }

        void Search(KDNode node, Vector3 p, int depth, ref int bestIdx, ref float bestDist)
        {
            if (node == null) return;
            float d = (node.point - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; bestIdx = node.index; }
            int axis = depth % 3;
            float diff = p[axis] - node.point[axis];
            KDNode first = diff < 0 ? node.left : node.right;
            KDNode second = diff < 0 ? node.right : node.left;
            if (first != null) Search(first, p, depth + 1, ref bestIdx, ref bestDist);
            if (second != null && diff * diff < bestDist) Search(second, p, depth + 1, ref bestIdx, ref bestDist);
        }

        public Vector3 GetPoint(int index) => pts[index];

        class KDNode { public int index; public Vector3 point; public KDNode left, right; }
    }
}

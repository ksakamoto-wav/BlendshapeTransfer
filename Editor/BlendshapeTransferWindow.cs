// BlendshapeTransfer_Unity2022.cs
// Unity 2020/2021/2022 Editor tool with selectable blendshapes
// Place this file in an "Editor" folder in your Unity project.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class BlendshapeTransferWindow : EditorWindow
{
    SkinnedMeshRenderer sourceRenderer;
    SkinnedMeshRenderer targetRenderer;
    string outputMeshName = "TargetMesh_WithBlendshapes";
    float maxSearchDistance = 0.1f;
    bool useKDTree = true;

    Vector2 scrollPos;
    bool[] selectedBlendshapes;

    [MenuItem("Tools/Blendshape Transfer")] 
    static void OpenWindow()
    {
        var w = GetWindow<BlendshapeTransferWindow>("Blendshape Transfer");
        w.minSize = new Vector2(400, 400);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Source (Model A の衣装 - シェイプキーを持つ)", EditorStyles.boldLabel);
        var newSource = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(sourceRenderer, typeof(SkinnedMeshRenderer), true);
        if (newSource != sourceRenderer)
        {
            sourceRenderer = newSource;
            if (sourceRenderer != null)
            {
                var mesh = sourceRenderer.sharedMesh;
                selectedBlendshapes = new bool[mesh.blendShapeCount];
            }
            else
            {
                selectedBlendshapes = null;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target (Model B の衣装 - 変形を付与する)", EditorStyles.boldLabel);
        targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(targetRenderer, typeof(SkinnedMeshRenderer), true);

        EditorGUILayout.Space();
        outputMeshName = EditorGUILayout.TextField("出力メッシュ名", outputMeshName);
        maxSearchDistance = EditorGUILayout.FloatField(new GUIContent("最大探索距離 (m)", "最近傍探索での最大距離。大きすぎると誤マッチ、小さすぎると未割当てになります。"), maxSearchDistance);
        useKDTree = EditorGUILayout.Toggle("KD-Tree を使用", useKDTree);

        EditorGUILayout.Space();
        if (sourceRenderer != null && sourceRenderer.sharedMesh != null)
        {
            EditorGUILayout.LabelField("転送するシェイプキー", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
            var mesh = sourceRenderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                selectedBlendshapes[i] = EditorGUILayout.ToggleLeft(mesh.GetBlendShapeName(i), selectedBlendshapes[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(sourceRenderer == null || targetRenderer == null || selectedBlendshapes == null);
        if (GUILayout.Button("シェイプキー転送 実行"))
        {
            TransferBlendshapes();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("注意: トポロジーが異なる場合は補間誤差が発生します。法線/タンジェントの自動補正は行いません。", MessageType.Info);
    }

    void TransferBlendshapes()
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

        // build search structure for source vertices
        IPointSearcher searcher;
        if (useKDTree)
            searcher = new KDTree(srcVertices);
        else
            searcher = new BruteForceSearcher(srcVertices);

        // prepare target mesh copy (so we don't modify original asset)
        Mesh newMesh = Instantiate(meshB);
        string folder = "Assets/BlendshapeTransfer_Output";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "BlendshapeTransfer_Output");

        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, outputMeshName + ".asset"));
        AssetDatabase.CreateAsset(newMesh, path);

        int blendCount = meshA.blendShapeCount;
        for (int bi = 0; bi < blendCount; bi++)
        {
            if (!selectedBlendshapes[bi]) continue; // only process selected shapes

            string shapeName = meshA.GetBlendShapeName(bi);
            int frameCount = meshA.GetBlendShapeFrameCount(bi);
            int useFrame = Mathf.Max(0, frameCount - 1);
            float frameWeight = meshA.GetBlendShapeFrameWeight(bi, useFrame);

            Vector3[] deltaSrc = new Vector3[meshA.vertexCount];
            Vector3[] deltaN = new Vector3[meshA.vertexCount];
            Vector3[] deltaT = new Vector3[meshA.vertexCount];
            meshA.GetBlendShapeFrameVertices(bi, useFrame, deltaSrc, deltaN, deltaT);

            Vector3[] deltaTgt = new Vector3[newMesh.vertexCount];
            for (int ti = 0; ti < newMesh.vertexCount; ti++)
            {
                Vector3 worldPosTgt = targetRenderer.transform.TransformPoint(tgtVertices[ti]);
                Vector3 srcSpacePos = sourceRenderer.transform.InverseTransformPoint(worldPosTgt);
                int nearestIndex = searcher.FindNearest(srcSpacePos, maxSearchDistance);
                if (nearestIndex >= 0)
                {
                    deltaTgt[ti] = deltaSrc[nearestIndex];
                }
                else
                {
                    deltaTgt[ti] = Vector3.zero;
                }
            }

            Vector3[] zeroNormals = new Vector3[newMesh.vertexCount];
            Vector3[] zeroTangents = new Vector3[newMesh.vertexCount];

            newMesh.AddBlendShapeFrame(shapeName, frameWeight, deltaTgt, zeroNormals, zeroTangents);
        }

        EditorUtility.SetDirty(newMesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完了", "出力メッシュを作成しました: " + path, "OK");
    }

    interface IPointSearcher
    {
        int FindNearest(Vector3 p, float maxDistance);
    }

    class BruteForceSearcher : IPointSearcher
    {
        Vector3[] pts;
        public BruteForceSearcher(Vector3[] pts) { this.pts = pts; }
        public int FindNearest(Vector3 p, float maxDistance)
        {
            float sqMax = maxDistance * maxDistance;
            int idx = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = (pts[i] - p).sqrMagnitude;
                if (d < best)
                {
                    best = d; idx = i;
                }
            }
            if (best <= sqMax) return idx;
            return -1;
        }
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
            int bestIdx = -1;
            float bestDist = float.PositiveInfinity;
            Search(root, p, 0, ref bestIdx, ref bestDist);
            if (bestDist <= sqMax) return bestIdx;
            return -1;
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

        class KDNode
        {
            public int index;
            public Vector3 point;
            public KDNode left, right;
        }
    }
}

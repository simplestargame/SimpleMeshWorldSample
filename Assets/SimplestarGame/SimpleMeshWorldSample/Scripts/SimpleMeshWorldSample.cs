using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SimplestarGame
{
    public class SimpleMeshWorldSample : MonoBehaviour
    {
        [SerializeField] string meshFileName = "BasicCube.caw";
        [SerializeField] string dataFileName = "world000.gz";
        [Range(0, 3)][SerializeField] int chunkCount = 0;
        [SerializeField] Material material;
        [SerializeField] Transform[] parents;

        int paretentIndex = 0;
        const int dataEdgeCubeCount = 256;

        class WorldData
        {
            public NativeArray<byte> voxelData;
            public NativeArray<XYZ> xyz;
            public NativeArray<int> countOffsets;
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            GraphicsSettings.useScriptableRenderPipelineBatching = true;
        }

        async void Start()
        {
            // キューブのメッシュデータソースを読み込み
            var cubeData = await Task.Run(() => CAWFile.ReadCAWFile(Path.Combine(Application.streamingAssetsPath, this.meshFileName)));
            // ワールドのボクセルデータ読み込み
            var compressedData = await Task.Run(() => File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, this.dataFileName)));
            // ワールドのデータ解凍
            var worldDataBytes = await Task.Run(() => GZipCompressor.Unzip(compressedData));
            // Job用NativeArray確保
            var edgeCubeCount = Mathf.RoundToInt(16 * Mathf.Pow(2, this.chunkCount));
            var worldData = await this.AllocateDataAsync(worldDataBytes, edgeCubeCount);
            // Meshオブジェクト作成
            List<GameObject> meshObjectList = new List<GameObject>();
            var edgeChunkCount = dataEdgeCubeCount / edgeCubeCount;
            for (int chunkX = 0; chunkX < edgeChunkCount; chunkX++)
            {
                for (int chunkY = 0; chunkY < edgeChunkCount; chunkY++)
                {
                    for (int chunkZ = 0; chunkZ < edgeChunkCount; chunkZ++)
                    {
                        var chunkInt3 = new Vector3Int(chunkX, chunkY, chunkZ);
                        meshObjectList.Add(await this.CreateChunkObjectAsync(worldData, cubeData, chunkInt3, edgeCubeCount));
                    }
                }
            }
            // 確保したものを開放
            worldData.countOffsets.Dispose();
            worldData.xyz.Dispose();
            worldData.voxelData.Dispose();
            cubeData.fileVertexData.Dispose();
            cubeData.vertexCounts.Dispose();
            // BakeMesh
            await this.BakeMeshAsync(meshObjectList);
            // 配置換え
            this.paretentIndex++;
            if (this.parents.Length == this.paretentIndex)
            {
                this.paretentIndex = 0;
            }
        }

        void Update()
        {
            // Space キーを押すと、配置換えしてワールドを構築し直します
            if (Input.GetKeyDown(KeyCode.Space))
            {
                this.Start();
            }
        }

        /// <summary>
        /// 計算で毎回使うバッファ、使いまわすために最初に確保
        /// </summary>
        /// <param name="worldDataBytes">世界データ</param>
        /// <param name="edgeCubeCount">チャンクの辺キューブ数</param>
        /// <returns>確保したバッファ</returns>
        async Task<WorldData> AllocateDataAsync(byte[] worldDataBytes, int edgeCubeCount)
        {
            return await Task.Run(() => {
                var voxelData = new NativeArray<byte>(worldDataBytes, Allocator.Persistent);
                var xyz = new NativeArray<XYZ>(edgeCubeCount * edgeCubeCount * edgeCubeCount, Allocator.Persistent);
                var countOffsets = new NativeArray<int>(xyz.Length, Allocator.Persistent);
                for (byte x = 0; x < edgeCubeCount; x++)
                {
                    for (byte y = 0; y < edgeCubeCount; y++)
                    {
                        for (byte z = 0; z < edgeCubeCount; z++)
                        {
                            var index = x * edgeCubeCount * edgeCubeCount + y * edgeCubeCount + z;
                            xyz[index] = new XYZ { x = x, y = y, z = z };
                            countOffsets[index] = 0;
                        }
                    }
                }
                return new WorldData { voxelData = voxelData, xyz = xyz, countOffsets = countOffsets };
            });
        }

        /// <summary>
        /// メッシュの作成
        /// </summary>
        /// <param name="meshDataArray">データ設定済みメッシュデータ</param>
        /// <param name="vertexIndexCount">インデックス数=頂点数</param>
        /// <param name="bounds">バウンディングボックス情報</param>
        /// <returns>作成したメッシュ</returns>
        async Task<Mesh> CreateMesh(Mesh.MeshDataArray meshDataArray, int vertexIndexCount, float3x2 bounds)
        {
            var newMesh = new Mesh();
            newMesh.name = "CustomLayoutMesh";
            var meshBounds = newMesh.bounds = new Bounds((bounds.c0 + bounds.c1) * 0.5f, bounds.c1 - bounds.c0);
            await Task.Run(() => {
                meshDataArray[0].SetSubMesh(0, new SubMeshDescriptor
                {
                    topology = MeshTopology.Triangles,
                    vertexCount = vertexIndexCount,
                    indexCount = vertexIndexCount,
                    baseVertex = 0,
                    firstVertex = 0,
                    indexStart = 0,
                    bounds = meshBounds
                }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            });
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, new[] { newMesh },
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            return newMesh;
        }

        /// <summary>
        /// メッシュオブジェクト作成
        /// </summary>
        /// <param name="worldData">ワールド全体データ</param>
        /// <param name="cubeData">キューブの頂点情報</param>
        /// <param name="chunkOffset">ワールド内のローカル塊オフセット</param>
        /// <param name="edgeCubeCount">塊の辺キューブ数</param>
        /// <returns>作成したゲームオブジェクト</returns>
        async Task<GameObject> CreateChunkObjectAsync(
            WorldData worldData,
            CAWFile.CubeData cubeData,
            Vector3Int chunkOffset,
            int edgeCubeCount)
        {
            // カウント
            await Task.Run(() => {
                var countJobHandle = new CountChunkVertexJob()
                {
                    vertexCounts = cubeData.vertexCounts,
                    voxelData = worldData.voxelData,
                    xyz = worldData.xyz,
                    results = worldData.countOffsets,
                    heightDepth = edgeCubeCount * edgeCubeCount,
                    width = edgeCubeCount,
                    height = edgeCubeCount,
                    depth = edgeCubeCount,
                    chunkOffset = chunkOffset,
                    dataEdgeCubeCount = dataEdgeCubeCount,
                }.Schedule(worldData.xyz.Length, 8);
                countJobHandle.Complete(); });
            // 集計
            var vertexIndexCount = await Task.Run(() =>
            {
                int vertexIndexCount = 0;
                for (int index = 0; index < worldData.countOffsets.Length; index++)
                {
                    var counts = worldData.countOffsets[index];
                    worldData.countOffsets[index] = vertexIndexCount;
                    vertexIndexCount += counts;
                }
                return vertexIndexCount;
            });
            if (vertexIndexCount == 0)
            {
                return null;
            }
            // 確保
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = meshDataArray[0];
            meshData.subMeshCount = 1;
            meshData.SetVertexBufferParams(vertexIndexCount, CustomLayoutMesh.VERTEX_ATTRIBUTE_DESCRIPTORS);
            meshData.SetIndexBufferParams(vertexIndexCount, IndexFormat.UInt32);
            NativeArray<int> indexData = meshData.GetIndexData<int>();
            // インデックス書き込み
            var indexJobHandle = new WriteIndexDataJob() { indexData = indexData }.Schedule(indexData.Length, 128);
            indexJobHandle.Complete();
            // 頂点データ書き込み
            NativeArray<CustomVertexLayout> vertexData = meshData.GetVertexData<CustomVertexLayout>(stream: 0);
            await Task.Run(() => {
                var writeJobHandle = new WriteChunkDataJob()
                {
                    vertexCounts = cubeData.vertexCounts,
                    voxelData = worldData.voxelData,
                    xyz = worldData.xyz,
                    countOffsets = worldData.countOffsets,
                    width = edgeCubeCount,
                    height = edgeCubeCount,
                    depth = edgeCubeCount,
                    fileVertexData = cubeData.fileVertexData,
                    vertexData = vertexData,
                    chunkOffset = chunkOffset,
                    dataEdgeCubeCount = dataEdgeCubeCount,
                }.Schedule(worldData.xyz.Length, 8);
                writeJobHandle.Complete(); });
            // バウンディングボックス
            float3x2 bounds = new float3x2();
            bounds.c0 = math.min(bounds.c0, new float3(-0.5f, -0.5f, -0.5f));
            bounds.c1 = math.max(bounds.c1, new float3(edgeCubeCount + 0.5f, edgeCubeCount + 0.5f, edgeCubeCount + 0.5f));
            // オブジェクト作成
            Mesh newMesh = await this.CreateMesh(meshDataArray, vertexIndexCount, bounds);
            vertexData.Dispose();
            indexData.Dispose();
            GameObject newGameObject = new GameObject("TestCubeMeshObject");
            newGameObject.transform.SetParent(this.parents[this.paretentIndex], false);
            newGameObject.transform.localPosition = chunkOffset * edgeCubeCount;
            newGameObject.isStatic = true;
            newGameObject.AddComponent<MeshFilter>().sharedMesh = newMesh;
            newGameObject.AddComponent<MeshRenderer>().sharedMaterial = this.material;
            return newGameObject;
        }

        /// <summary>
        /// MeshCollider 作成
        /// </summary>
        /// <param name="meshObjectList">MeshFilter の sharedMesh を入力に MeshCollider を計算します</param>
        /// <returns>Task</returns>
        async Task BakeMeshAsync(List<GameObject> meshObjectList)
        {
            NativeArray<int> meshIds = new NativeArray<int>(meshObjectList.Count, Allocator.Persistent);
            var meshIdx = 0;
            foreach (var meshObject in meshObjectList)
            {
                var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                meshIds[meshIdx++] = mesh.GetInstanceID();
            }
            await Task.Run(() =>
            {
                var bakeMeshJob = new BakeMeshJob(meshIds);
                var bakeMeshJobHandle = bakeMeshJob.Schedule(meshIds.Length, 1);
                bakeMeshJobHandle.Complete();
                meshIds.Dispose();
            });
            // Set MeshCollider
            foreach (var meshObject in meshObjectList)
            {
                meshObject.AddComponent<MeshCollider>().sharedMesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            }
        }
    }
}
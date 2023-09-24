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
            // �L���[�u�̃��b�V���f�[�^�\�[�X��ǂݍ���
            var cubeData = await Task.Run(() => CAWFile.ReadCAWFile(Path.Combine(Application.streamingAssetsPath, this.meshFileName)));
            // ���[���h�̃{�N�Z���f�[�^�ǂݍ���
            var compressedData = await Task.Run(() => File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, this.dataFileName)));
            // ���[���h�̃f�[�^��
            var worldDataBytes = await Task.Run(() => GZipCompressor.Unzip(compressedData));
            // Job�pNativeArray�m��
            var edgeCubeCount = Mathf.RoundToInt(16 * Mathf.Pow(2, this.chunkCount));
            var worldData = await this.AllocateDataAsync(worldDataBytes, edgeCubeCount);
            // Mesh�I�u�W�F�N�g�쐬
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
            // �m�ۂ������̂��J��
            worldData.countOffsets.Dispose();
            worldData.xyz.Dispose();
            worldData.voxelData.Dispose();
            cubeData.fileVertexData.Dispose();
            cubeData.vertexCounts.Dispose();
            // BakeMesh
            await this.BakeMeshAsync(meshObjectList);
            // �z�u����
            this.paretentIndex++;
            if (this.parents.Length == this.paretentIndex)
            {
                this.paretentIndex = 0;
            }
        }

        void Update()
        {
            // Space �L�[�������ƁA�z�u�������ă��[���h���\�z�������܂�
            if (Input.GetKeyDown(KeyCode.Space))
            {
                this.Start();
            }
        }

        /// <summary>
        /// �v�Z�Ŗ���g���o�b�t�@�A�g���܂킷���߂ɍŏ��Ɋm��
        /// </summary>
        /// <param name="worldDataBytes">���E�f�[�^</param>
        /// <param name="edgeCubeCount">�`�����N�̕ӃL���[�u��</param>
        /// <returns>�m�ۂ����o�b�t�@</returns>
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
        /// ���b�V���̍쐬
        /// </summary>
        /// <param name="meshDataArray">�f�[�^�ݒ�ς݃��b�V���f�[�^</param>
        /// <param name="vertexIndexCount">�C���f�b�N�X��=���_��</param>
        /// <param name="bounds">�o�E���f�B���O�{�b�N�X���</param>
        /// <returns>�쐬�������b�V��</returns>
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
        /// ���b�V���I�u�W�F�N�g�쐬
        /// </summary>
        /// <param name="worldData">���[���h�S�̃f�[�^</param>
        /// <param name="cubeData">�L���[�u�̒��_���</param>
        /// <param name="chunkOffset">���[���h���̃��[�J����I�t�Z�b�g</param>
        /// <param name="edgeCubeCount">��̕ӃL���[�u��</param>
        /// <returns>�쐬�����Q�[���I�u�W�F�N�g</returns>
        async Task<GameObject> CreateChunkObjectAsync(
            WorldData worldData,
            CAWFile.CubeData cubeData,
            Vector3Int chunkOffset,
            int edgeCubeCount)
        {
            // �J�E���g
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
            // �W�v
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
            // �m��
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = meshDataArray[0];
            meshData.subMeshCount = 1;
            meshData.SetVertexBufferParams(vertexIndexCount, CustomLayoutMesh.VERTEX_ATTRIBUTE_DESCRIPTORS);
            meshData.SetIndexBufferParams(vertexIndexCount, IndexFormat.UInt32);
            NativeArray<int> indexData = meshData.GetIndexData<int>();
            // �C���f�b�N�X��������
            var indexJobHandle = new WriteIndexDataJob() { indexData = indexData }.Schedule(indexData.Length, 128);
            indexJobHandle.Complete();
            // ���_�f�[�^��������
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
            // �o�E���f�B���O�{�b�N�X
            float3x2 bounds = new float3x2();
            bounds.c0 = math.min(bounds.c0, new float3(-0.5f, -0.5f, -0.5f));
            bounds.c1 = math.max(bounds.c1, new float3(edgeCubeCount + 0.5f, edgeCubeCount + 0.5f, edgeCubeCount + 0.5f));
            // �I�u�W�F�N�g�쐬
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
        /// MeshCollider �쐬
        /// </summary>
        /// <param name="meshObjectList">MeshFilter �� sharedMesh ����͂� MeshCollider ���v�Z���܂�</param>
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
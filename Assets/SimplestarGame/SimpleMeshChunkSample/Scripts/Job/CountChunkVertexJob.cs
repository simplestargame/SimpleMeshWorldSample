using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace SimplestarGame
{
    /// <summary>
    /// Chunk結合したメッシュの総頂点数を予め調べる処理
    /// バッファを固定長で確保してから、そのバッファにデータを書き込んでいく処理がその先に待つ
    /// 今回は InsetCube のため、+X と -Y, -Z 方向の面側のカリングをしない判定となっている
    /// </summary>
    [BurstCompile]
    public struct CountChunkVertexJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<XYZ> xyz;
        [ReadOnly] public NativeArray<int> vertexCounts;
        [ReadOnly] public NativeArray<byte> voxelData;
        public NativeArray<int> results;
        public int heightDepth;
        public int width;
        public int height;
        public int depth;
        public Vector3Int chunkOffset;
        public int dataEdgeCubeCount;

        /// <summary>
        /// ボクセルの x, y, z 座標指定での値取得
        /// </summary>
        /// <param name="voxelData">ボクセルデータ</param>
        /// <param name="x">幅座標</param>
        /// <param name="y">高さ座標</param>
        /// <param name="z">奥行き座標</param>
        /// <returns></returns>
        byte GetVoxelValue(ref NativeArray<byte> voxelData, int x, int y, int z)
        {
            var offsetCube = (x + chunkOffset.x * this.width) * this.dataEdgeCubeCount * this.dataEdgeCubeCount 
                + (y + chunkOffset.y * this.height) * this.dataEdgeCubeCount 
                + (z + chunkOffset.z * this.depth);
            return voxelData[offsetCube];
        }

        public void Execute(int index)
        {
            this.results[index] = 0;
            var xyz = this.xyz[index];
            var x = xyz.x;
            var y = xyz.y;
            var z = xyz.z;

            if (this.GetVoxelValue(ref voxelData, x, y, z) == 255)
            {
                for (int c = 0; c < this.vertexCounts.Length; c++)
                {
                    var count = this.vertexCounts[c];
                    if (c == CAWFile.PLUS_X)
                    {
                        if (x + chunkOffset.x * this.width + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x + 1, y, z) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.PLUS_Y)
                    {
                        if (y + chunkOffset.y * this.height + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y + 1, z) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.PLUS_Z)
                    {
                        if (z + chunkOffset.z * this.depth + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y, z + 1) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.MINUS_X)
                    {
                        if (x + chunkOffset.x * this.width != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x - 1, y, z) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.MINUS_Y)
                    {
                        if (y + chunkOffset.y * this.height != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y - 1, z) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.MINUS_Z)
                    {
                        if (z + chunkOffset.z * this.depth != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y, z - 1) != 255)
                            {
                                this.results[index] += count;
                            }
                        }
                        else
                        {
                            this.results[index] += count;
                        }
                    }
                    else if (c == CAWFile.REMAIN)
                    {
                        this.results[index] += count;
                    }
                }
            }
        }
    }
}
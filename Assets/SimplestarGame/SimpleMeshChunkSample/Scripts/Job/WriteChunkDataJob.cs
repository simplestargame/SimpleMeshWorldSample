﻿using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SimplestarGame
{
    /// <summary>
    /// 頂点カウントのジョブと処理が重なるが、こちらは確保済みのバッファに対してデータを書き込む処理
    /// fileVertexData が今回は InsetCube である場合を見越して、頂点カウントと同様に
    /// +X と -Y, -Z 方向の面側のカリングをしない判定となっている
    /// それ以外は頂点データをバッファに書き込んでいる
    /// </summary>
    [BurstCompile]
    public struct WriteChunkDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<XYZ> xyz;
        [ReadOnly] public NativeArray<int> vertexCounts;
        [ReadOnly] public NativeArray<byte> voxelData;
        [ReadOnly] public NativeArray<int> countOffsets;
        public int width;
        public int height;
        public int depth;
        [ReadOnly] public NativeArray<CustomVertexLayout> fileVertexData;
        [NativeDisableParallelForRestriction] public NativeArray<CustomVertexLayout> vertexData;
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
            var offsetCube = (x + chunkOffset.x * this.width) * dataEdgeCubeCount * dataEdgeCubeCount 
                + (y + chunkOffset.y * this.height) * dataEdgeCubeCount 
                + (z + chunkOffset.z * this.depth);
            return voxelData[offsetCube];
        }

        unsafe int WriteVertexData(
            int writeOffset, int readOffset, int count,
            int x, int y, int z)
        {
            for (int v = 0; v < count; v++)
            {
                var vertex = this.fileVertexData[readOffset + v];
                var newPos = vertex.pos;
                newPos.x = (half)(newPos.x + (half)x);
                newPos.y = (half)(newPos.y + (half)y);
                newPos.z = (half)(newPos.z + (half)z);
                vertex.pos = newPos;
                this.vertexData[writeOffset + v] = vertex;
            }
            return count;
        }

        public void Execute(int index)
        {
            var xyz = this.xyz[index];
            var x = xyz.x;
            var y = xyz.y;
            var z = xyz.z;
            if (this.GetVoxelValue(ref voxelData, x, y, z) == 255)
            {
                int readOffset = 0;
                var writeOffset = this.countOffsets[index];
                for (int c = 0; c < this.vertexCounts.Length; c++)
                {
                    var count = this.vertexCounts[c];
                    if (c == CAWFile.PLUS_X)
                    {
                        if (x + chunkOffset.x * this.width + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x + 1, y, z) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.PLUS_Y)
                    {
                        if (y + chunkOffset.y * this.height + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y + 1, z) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.PLUS_Z)
                    {
                        if (z + chunkOffset.z * this.depth + 1 != this.dataEdgeCubeCount)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y, z + 1) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.MINUS_X)
                    {
                        if (x + chunkOffset.x * this.width != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x - 1, y, z) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.MINUS_Y)
                    {
                        if (y + chunkOffset.y * this.height != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y - 1, z) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.MINUS_Z)
                    {
                        if (z + chunkOffset.z * this.depth != 0)
                        {
                            if (this.GetVoxelValue(ref voxelData, x, y, z - 1) != 255)
                            {
                                writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                            }
                        }
                        else
                        {
                            writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                        }
                    }
                    else if (c == CAWFile.REMAIN)
                    {
                        writeOffset += this.WriteVertexData(writeOffset, readOffset, count, x, y, z);
                    }
                    readOffset += count;
                }
            }
        }
    }
}

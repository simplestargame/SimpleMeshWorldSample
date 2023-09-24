using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SimplestarGame
{
    /// <summary>
    /// .caw ファイル
    /// </summary>
    public class CAWFile
    {
        /// <summary>
        /// データの読み込み
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <returns>キューブデータ</returns>
        public static CubeData ReadCAWFile(string filePath)
        {
            NativeArray<int> vertexCounts;
            NativeArray<CustomVertexLayout> fileVertexData;
            if (!UnsafeReadCAWFile(filePath, out vertexCounts, out fileVertexData))
            {
                return null;
            }
            return new CubeData
            {
                vertexCounts = vertexCounts,
                fileVertexData = fileVertexData
            };
        }

        /// <summary>
        /// ファイルから頂点データの読み込み
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="vertexCounts">成功時、Disposeする必要あり</param>
        /// <param name="vertexData">成功時、Disposeする必要あり</param>
        /// <returns>成功時true</returns>
        static unsafe bool UnsafeReadCAWFile(string filePath, out NativeArray<int> vertexCounts, out NativeArray<CustomVertexLayout> vertexData)
        {
            vertexCounts = new NativeArray<int>(7, Allocator.Persistent);
            int fileSize = 0;
            try
            {
                fileSize = (int)new FileInfo(filePath).Length;
            }
            catch (FileNotFoundException e)
            {
                Debug.LogError($"指定したファイルがありません: {e.Message}");
                vertexData = new NativeArray<CustomVertexLayout>(1, Allocator.Persistent);
                vertexData.Dispose();
                vertexCounts.Dispose();
                return false;
            }
            var headerByteCount = 4 + 7 * sizeof(int);
            var vertexCount = (fileSize - headerByteCount) / sizeof(CustomVertexLayout);
            var magicCode = new NativeArray<byte>(4, Allocator.Persistent);
            vertexData = new NativeArray<CustomVertexLayout>(vertexCount, Allocator.Persistent);
            var success = false;
            using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
            {
                reader.Read(new Span<byte>(magicCode.GetUnsafePtr(), magicCode.Length));
                if (magicCode[0] == 'c' && magicCode[1] == 'a' && magicCode[2] == 'w')
                {
                    reader.Read(new Span<byte>(vertexCounts.GetUnsafePtr(), vertexCounts.Length * sizeof(int)));
                    reader.Read(new Span<byte>(vertexData.GetUnsafePtr(), vertexData.Length * sizeof(CustomVertexLayout)));
                    success = true;
                }
                else
                {
                    vertexData.Dispose();
                    vertexCounts.Dispose();
                }
            }
            magicCode.Dispose();
            return success;
        }

        public class CubeData
        {
            public NativeArray<int> vertexCounts;
            public NativeArray<CustomVertexLayout> fileVertexData;
        }

        /// <summary>
        /// キューブの +X 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int PLUS_X = 0;
        /// <summary>
        /// キューブの +Y 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int PLUS_Y = 1;
        /// <summary>
        /// キューブの +Z 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int PLUS_Z = 2;
        /// <summary>
        /// キューブの -X 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int MINUS_X = 3;
        /// <summary>
        /// キューブの -Y 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int MINUS_Y = 4;
        /// <summary>
        /// キューブの -Z 方向に面するう頂点で作られている三角形リストを意味する
        /// </summary>
        public const int MINUS_Z = 5;
        /// <summary>
        /// キューブの上記いずれの方向にも面していない頂点で作られている三角形リストを意味する
        /// </summary>
        public const int REMAIN = 6;
    }
}

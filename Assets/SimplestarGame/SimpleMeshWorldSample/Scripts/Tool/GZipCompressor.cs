using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

// 使い方
//var compressedData = GZipCompressor.Compress(rawData);
//var rawData = GZipCompressor.Unzip(compressedData);

namespace SimplestarGame
{
    /// <summary>
    /// gzip で byte[] の圧縮や解凍を行うクラス
    /// </summary>
    public static class GZipCompressor
    {
        /// <summary>
        /// 圧縮
        /// </summary>
        /// <param name="rawData">生データ</param>
        /// <returns>圧縮された byte 配列</returns>
        public static byte[] Compress(byte[] rawData)
        {
            using (MemoryStream compressedStream = new MemoryStream())
            {
                using (GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Compress))
                using (BinaryWriter writer = new BinaryWriter(gzipStream))
                {
                    writer.Write(rawData);
                }

                byte[] compressedData = compressedStream.ToArray();
                return compressedData;
            }
        }

        /// <summary>
        /// 解凍
        /// </summary>
        /// <param name="compressedData">圧縮された byte 配列</param>
        /// <returns>生データ</returns>
        public static byte[] Unzip(byte[] compressedData)
        {
            using (var compressedStream = new MemoryStream(compressedData))
            using (GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (MemoryStream decompressedStream = new MemoryStream())
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while ((bytesRead = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    decompressedStream.Write(buffer, 0, bytesRead);
                }
                byte[] decompressedData = decompressedStream.ToArray();
                return decompressedData;
            }
        }
    }
}
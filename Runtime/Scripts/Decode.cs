using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Assertions;

[assembly: InternalsVisibleTo("Unity.Meshopt.Decompress.Tests")]

namespace Meshoptimizer
{
    /// <summary>
    /// The Decode class provides static methods for decoding/decompressing meshoptimizer compressed
    /// vertex and index buffers.
    /// </summary>
    public static class Decode
    {
        internal const byte k_IndexHeader = 0xe0;
        internal const byte k_SequenceHeader = 0xd0;
        internal const int k_DecodeIndexVersion = 1;

        internal const uint k_VertexBlockSizeBytes = 8192;
        internal const uint k_VertexBlockMaxSize = 256;
        internal const uint k_ByteGroupSize = 16;
        internal const uint k_ByteGroupDecodeLimit = 24;

        /// <summary>
        /// Creates a C# job that decompresses the provided source buffer into destination
        /// </summary>
        /// <param name="returnCode">An array with a length of one. The job's return code will end up at index 0</param>
        /// <param name="destination">Destination buffer where the source will be decompressed into</param>
        /// <param name="count">Number of elements (vertices/indices) to decode</param>
        /// <param name="size">Size of elements (vertex/index) in bytes</param>
        /// <param name="source">Source buffer</param>
        /// <param name="mode">Compression mode</param>
        /// <param name="filter">In case of <see cref="Mode.Attributes"/> mode, filter to be applied</param>
        /// <returns>JobHandle for the created C# job</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown upon invalid mode/filter</exception>
        /// <exception cref="ArgumentException">Thrown if destination is smaller than count * size bytes</exception>
        [Obsolete("Use the overload that accepts a NativeArray<byte> source data")]
        public static JobHandle DecodeGltfBuffer(
            NativeSlice<int> returnCode,
            NativeArray<byte> destination,
            int count,
            int size,
            NativeSlice<byte> source,
            Mode mode,
            Filter filter = Filter.None
        )
        {
            return DecodeGltfBuffer(
                returnCode.AsNativeArray(),
                destination,
                count,
                size,
                source.AsNativeArray().AsReadOnly(),
                mode,
                filter
                );
        }

        /// <summary>
        /// Creates a C# job that decompresses the provided source buffer into destination
        /// </summary>
        /// <param name="returnCode">An array with a length of one. The job's return code will end up at index 0</param>
        /// <param name="destination">Destination buffer where the source will be decompressed into</param>
        /// <param name="count">Number of elements (vertices/indices) to decode</param>
        /// <param name="size">Size of elements (vertex/index) in bytes</param>
        /// <param name="source">Source buffer</param>
        /// <param name="mode">Compression mode</param>
        /// <param name="filter">In case of <see cref="Mode.Attributes"/> mode, filter to be applied</param>
        /// <returns>JobHandle for the created C# job</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown upon invalid mode/filter</exception>
        /// <exception cref="ArgumentException">Thrown if destination is smaller than count * size bytes</exception>
        public static JobHandle DecodeGltfBuffer(
            NativeArray<int> returnCode,
            NativeArray<byte> destination,
            int count,
            int size,
            NativeArray<byte>.ReadOnly source,
            Mode mode,
            Filter filter = Filter.None
        )
        {
            Assert.AreEqual(1, returnCode.Length);
            // decoders write through raw pointers, so the capacity has to be validated upfront
            if (count < 0 || size < 0 || destination.Length < (long)count * size)
            {
                throw new ArgumentException(
                    $"Destination ({destination.Length} bytes) is too small for {count} elements of {size} bytes",
                    nameof(destination)
                );
            }
            returnCode[0] = int.MinValue;
            switch (mode)
            {
                case Mode.Attributes:
                {
                    var job = new DecodeVertexJob
                    {
                        destination = destination,
                        vertexCount = (uint)count,
                        vertexSize = (uint)size,
                        source = source,
                        filter = filter,
                        returnCode = returnCode
                    };
                    return job.Schedule();
                }
                case Mode.Triangles:
                {
                    var job = new DecodeIndexTrianglesJob
                    {
                        destination = destination,
                        indexCount = count,
                        indexSize = size,
                        source = source,
                        returnCode = returnCode
                    };
                    return job.Schedule();
                }
                case Mode.Indices:
                {
                    var job = new DecodeIndexSequenceJob
                    {
                        destination = destination,
                        indexCount = count,
                        indexSize = size,
                        source = source,
                        returnCode = returnCode
                    };
                    return job.Schedule();
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        /// <summary>
        /// Synchronous variant of <see cref="DecodeGltfBuffer(NativeSlice{int},NativeArray{byte},int,int,NativeSlice{byte},Mode,Filter)"/> (decodes on the current thread)
        /// </summary>
        /// <param name="destination">Destination buffer where the source will be decompressed into</param>
        /// <param name="count">Number of elements (vertices/indices) to decode</param>
        /// <param name="size">Size of elements (vertex/index) in bytes</param>
        /// <param name="source">Source buffer</param>
        /// <param name="mode">Compression mode</param>
        /// <param name="filter">In case of <see cref="Mode.Attributes"/> mode, filter to be applied</param>
        /// <returns>Return code that is 0 in case of success</returns>
        /// <exception cref="ArgumentException">Thrown if destination is smaller than count * size bytes</exception>
        [Obsolete("Use the overload that accepts a NativeArray<byte>.ReadOnly source data")]
        public static int DecodeGltfBufferSync(
            NativeArray<byte> destination,
            int count,
            int size,
            NativeSlice<byte> source,
            Mode mode,
            Filter filter = Filter.None
        )
        {
            using var returnCode = new NativeArray<int>(1, Allocator.TempJob);
            var jobHandle = DecodeGltfBuffer(
                returnCode,
                destination,
                count,
                size,
                source,
                mode,
                filter
            );
            jobHandle.Complete();
            return returnCode[0];
        }

        /// <summary>
        /// Synchronous variant of <see cref="DecodeGltfBuffer(NativeArray{int},NativeArray{byte},int,int,NativeArray{byte}.ReadOnly,Mode,Filter)"/> (decodes on the current thread)
        /// </summary>
        /// <param name="destination">Destination buffer where the source will be decompressed into</param>
        /// <param name="count">Number of elements (vertices/indices) to decode</param>
        /// <param name="size">Size of elements (vertex/index) in bytes</param>
        /// <param name="source">Source buffer</param>
        /// <param name="mode">Compression mode</param>
        /// <param name="filter">In case of <see cref="Mode.Attributes"/> mode, filter to be applied</param>
        /// <returns>Return code that is 0 in case of success</returns>
        /// <exception cref="ArgumentException">Thrown if destination is smaller than count * size bytes</exception>
        public static int DecodeGltfBufferSync(
            NativeArray<byte> destination,
            int count,
            int size,
            NativeArray<byte>.ReadOnly source,
            Mode mode,
            Filter filter = Filter.None
        )
        {
            using var returnCode = new NativeArray<int>(1, Allocator.TempJob);
            var jobHandle = DecodeGltfBuffer(
                returnCode,
                destination,
                count,
                size,
                source,
                mode,
                filter
            );
            jobHandle.Complete();
            return returnCode[0];
        }

        internal static sbyte UnZigZag8(byte v)
        {
            return (sbyte)(-(v & 1) ^ (v >> 1));
        }

        internal static ushort UnZigZag16(ushort v)
        {
            return (ushort)(-(v & 1) ^ (v >> 1));
        }

        internal static unsafe uint DecodeVByte(ref byte* data)
        {
            var lead = *data++;

            // fast path: single byte
            if (lead < 128)
                return lead;

            // slow path: up to 4 extra bytes
            // note that this loop always terminates, which is important for malformed data
            var result = (uint)lead & 127;
            var shift = 7;

            for (var i = 0; i < 4; ++i)
            {
                var group = *data++;
                result |= (uint)((group & 127) << shift);
                shift += 7;

                if (group < 128)
                    break;
            }

            return result;
        }
    }
}

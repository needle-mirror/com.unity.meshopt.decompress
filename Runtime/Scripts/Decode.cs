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
    /// Vertex attribute filter to be applied
    /// </summary>
    public enum Filter
    {
        /// <summary>
        /// Don't use this value as parameter directly!
        /// It's for deserialization purpose only.
        /// </summary>
        Undefined,
        /// <summary>
        /// No filter should be applied
        /// </summary>
        None,
        /// <summary>
        /// Apply octahedral filter, usually for normals
        /// </summary>
        Octahedral,
        /// <summary>
        /// Apply quaternion filter, usually for rotations
        /// </summary>
        Quaternion,
        /// <summary>
        /// Apply exponential filter, usually for positional data
        /// </summary>
        Exponential
    }

    /// <summary>
    /// Mode defines the type of buffer to decode
    /// </summary>
    public enum Mode
    {
        /// <summary>
        /// Don't use this value as parameter directly!
        /// It's for deserialization purpose only.
        /// </summary>
        Undefined,
        /// <summary>
        /// Vertex attributes
        /// </summary>
        Attributes,
        /// <summary>
        /// Triangle indices buffer
        /// </summary>
        Triangles,
        /// <summary>
        /// Index sequence
        /// </summary>
        Indices,
    }

    /// <summary>
    /// The Decode class provides static methods for decoding/decompressing meshoptimizer compressed
    /// vertex and index buffers.
    /// </summary>
    public static class Decode
    {

        #region Constants
        internal const byte indexHeader = 0xe0;
        internal const byte sequenceHeader = 0xd0;

        internal const uint kVertexBlockSizeBytes = 8192;
        internal const uint kVertexBlockMaxSize = 256;
        internal const uint kByteGroupSize = 16;
        internal const uint kByteGroupDecodeLimit = 24;
        #endregion Constants

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
            Assert.AreEqual(1, returnCode.Length);
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
                            returnCode = returnCode,
                            triangleWriter = DecodeIndexTrianglesJob.GetTriangleWriter(size)
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
        /// Synchronous variant of <seealso cref="DecodeGltfBuffer"/> (decodes on the current thread)
        /// </summary>
        /// <param name="destination">Destination buffer where the source will be decompressed into</param>
        /// <param name="count">Number of elements (vertices/indices) to decode</param>
        /// <param name="size">Size of elements (vertex/index) in bytes</param>
        /// <param name="source">Source buffer</param>
        /// <param name="mode">Compression mode</param>
        /// <param name="filter">In case of <see cref="Mode.Attributes"/> mode, filter to be applied</param>
        /// <returns>Return code that is 0 in case of success</returns>
        public static int DecodeGltfBufferSync(
            NativeArray<byte> destination,
            int count,
            int size,
            NativeSlice<byte> source,
            Mode mode,
            Filter filter = Filter.None
        )
        {
            using (var returnCode = new NativeArray<int>(1, Allocator.TempJob))
            {
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
        }

        internal static sbyte UnZigZag8(byte v)
        {
            return (sbyte)(-(v & 1) ^ (v >> 1));
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

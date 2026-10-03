using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Meshoptimizer
{

    [BurstCompile]
    unsafe struct DecodeVertexJob : IJob
    {
        const float k_SqrtHalf = 0.707106781186548f;
        const byte k_VertexHeader = 0xa0;
        const int k_DecodeVertexVersion = 1;
        const uint k_TailMinSizeV0 = 32;
        const uint k_TailMinSizeV1 = 24;

        // [WriteOnly] // TODO: Make filtering a separate job and add WriteOnly attribute
        public NativeArray<byte> destination;

        [ReadOnly]
        public NativeArray<byte>.ReadOnly source;

        public uint vertexCount;
        public uint vertexSize;
        public Filter filter;

        // Safety restrictions are lifted so that one NativeArray can be used
        // for multiple DecodeVertexJob jobs via GetSubArray().
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public NativeArray<int> returnCode;

        public void Execute()
        {
            Assert.IsTrue(vertexSize > 0 && vertexSize <= 256);
            Assert.AreEqual(0, vertexSize % 4);

            var vertexData = (byte*)destination.GetUnsafePtr();

            var data = (byte*)source.GetUnsafeReadOnlyPtr();
            var dataEnd = data + source.Length;

            if (dataEnd - data < 1)
            {
                returnCode[0] = -2;
                return;
            }

            var dataHeader = *data++;

            if ((dataHeader & 0xf0) != k_VertexHeader)
            {
                returnCode[0] = -1;
                return;
            }

            var version = dataHeader & 0x0f;
            if (version > k_DecodeVertexVersion)
            {
                returnCode[0] = -1;
                return;
            }

            var tailSize = vertexSize + (version == 0 ? 0 : vertexSize / 4);
            var tailSizeMin = version == 0 ? k_TailMinSizeV0 : k_TailMinSizeV1;
            var tailSizePad = tailSize < tailSizeMin ? tailSizeMin : tailSize;

            if ((uint)(dataEnd - data) < tailSizePad)
            {
                returnCode[0] = -2;
                return;
            }

            var tail = dataEnd - tailSize;

            var lastVertex = stackalloc byte[256];
            UnsafeUtility.MemCpy(lastVertex, tail, vertexSize);

            var channels = version == 0 ? null : tail + vertexSize;

            var buffer = stackalloc byte[(int)Decode.k_VertexBlockMaxSize * 4];
            var transposed = stackalloc byte[(int)Decode.k_VertexBlockSizeBytes];

            var vertexBlockSize = GetVertexBlockSize(vertexSize);

            uint vertexOffset = 0;

            while (vertexOffset < vertexCount)
            {
                var blockSize = (vertexOffset + vertexBlockSize < vertexCount) ? vertexBlockSize : vertexCount - vertexOffset;

                data = DecodeBlock(
                    data,
                    dataEnd,
                    vertexData + vertexOffset * vertexSize,
                    blockSize,
                    vertexSize,
                    lastVertex,
                    channels,
                    version,
                    buffer,
                    transposed
                    );
                if (data == null)
                {
                    returnCode[0] = -2;
                    return;
                }
                vertexOffset += blockSize;
            }

            if ((uint)(dataEnd - data) != tailSizePad)
            {
                returnCode[0] = -3;
                return;
            }

            switch (filter)
            {
                // Filters - only applied if filter isn't undefined or NONE
                case Filter.Octahedral:
                    Assert.IsTrue(vertexSize == 4 || vertexSize == 8);
                    if (vertexSize == 4)
                    {
                        ApplyOctahedralFilterOct8(destination, vertexCount);
                    }
                    else
                    {
                        ApplyOctahedralFilterOct12(destination, vertexCount);
                    }
                    break;
                case Filter.Quaternion:
                    Assert.AreEqual(vertexSize, 8);
                    ApplyQuaternionFilter(destination, vertexCount);
                    break;
                case Filter.Exponential:
                    Assert.AreEqual(0x00, (vertexSize & 0x03));
                    ApplyExponentialFilter(destination, vertexCount, vertexSize);
                    break;
                case Filter.Color:
                    Assert.IsTrue(vertexSize == 4 || vertexSize == 8);
                    if (vertexSize == 4)
                    {
                        ApplyColorFilter8(destination, vertexCount);
                    }
                    else
                    {
                        ApplyColorFilter16(destination, vertexCount);
                    }
                    break;
                case Filter.None:
                case Filter.Undefined:
                    break;
                default:
                    returnCode[0] = -4;
                    return;
            }

            returnCode[0] = 0;
        }

        static byte* DecodeBytesGroup(byte* data, byte* buffer, int bits)
        {

            byte v;
            byte* dataVar;

            // reverses the bit order of v; 1-bit groups are stored in reverse order
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static byte Reverse(byte v)
            {
                return (byte)((((v * 0x80200802ul) & 0x0884422110ul) * 0x0101010101ul) >> 32);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Next(byte nextBits, ref byte* dataVar, ref byte* buffer, ref byte v)
            {
                var enc = (byte)(v >> (8 - nextBits));
                v <<= nextBits;
                var encV = *dataVar;
                *buffer++ = (enc == (1 << nextBits) - 1) ? encV : enc;
                dataVar += (enc == (1 << nextBits) - 1) ? 1 : 0;
            }

            switch (bits)
            {
                case 0:
                    UnsafeUtility.MemSet(buffer, 0, Decode.k_ByteGroupSize);
                    return data;
                case 1:
                    dataVar = data + 2;

                    // 2 groups with 8 1-bit values in each byte (reversed from the order in other groups)
                    for (var i = 0; i < 2; i++)
                    {
                        v = Reverse(*data++);
                        for (var j = 0; j < 8; j++)
                            Next(1, ref dataVar, ref buffer, ref v);
                    }

                    return dataVar;
                case 2:
                    dataVar = data + 4;

                    // 4 groups with 4 2-bit values in each byte
                    for (var i = 0; i < 4; i++)
                    {
                        v = *data++;
                        for (var j = 0; j < 4; j++)
                            Next(2, ref dataVar, ref buffer, ref v);
                    }

                    return dataVar;
                case 4:
                    dataVar = data + 8;

                    // 8 groups with 2 4-bit values in each byte
                    for (var i = 0; i < 8; i++)
                    {
                        v = *data++;
                        for (var j = 0; j < 2; j++)
                            Next(4, ref dataVar, ref buffer, ref v);
                    }

                    return dataVar;
                default:
                    // 8 bits; bits is always one of 0, 1, 2, 4 or 8
                    UnsafeUtility.MemCpy(buffer, data, Decode.k_ByteGroupSize);
                    return data + Decode.k_ByteGroupSize;
            }
        }

        /// <summary>
        /// Bit count of byte groups for version 0 streams: 0, 2, 4, 8
        /// </summary>
        static int GetBitsV0(int index)
        {
            return index == 3 ? 8 : index * 2;
        }

        /// <summary>
        /// Bit count of byte groups for version 1 streams: 0, 1, 2, 4, 8
        /// </summary>
        static int GetBitsV1(int index)
        {
            return index == 0 ? 0 : 1 << (index - 1);
        }

        static byte* DecodeBytes(byte* data, byte* dataEnd, byte* buffer, uint bufferSize, int version, int ctrl)
        {

            Assert.AreEqual(0, bufferSize % Decode.k_ByteGroupSize);

            // round number of groups to 4 to get number of header bytes
            var headerSize = (bufferSize / Decode.k_ByteGroupSize + 3) / 4;

            if ((uint)(dataEnd - data) < headerSize)
                return null;

            var header = data;
            data += headerSize;

            for (uint i = 0; i < bufferSize; i += Decode.k_ByteGroupSize)
            {
                if ((uint)(dataEnd - data) < Decode.k_ByteGroupDecodeLimit)
                    return null;

                var headerOffset = i / Decode.k_ByteGroupSize;

                var bitsk = (header[headerOffset / 4] >> (int)((headerOffset % 4) * 2)) & 3;
                var bits = version == 0 ? GetBitsV0(bitsk) : GetBitsV1(ctrl + bitsk);

                data = DecodeBytesGroup(data, buffer + i, bits);
            }

            return data;
        }

        static void DecodeDeltas8(byte* buffer, byte* transposed, uint vertexCount, uint vertexSize, byte* lastVertex)
        {
            for (uint k = 0; k < 4; ++k)
            {
                var vertexOffset = k;

                var p = lastVertex[k];

                for (uint i = 0; i < vertexCount; ++i)
                {
                    var v = (byte)(Decode.UnZigZag8(buffer[i]) + p);

                    transposed[vertexOffset] = v;
                    p = v;

                    vertexOffset += vertexSize;
                }

                buffer += vertexCount;
            }
        }

        static void DecodeDeltas16(byte* buffer, byte* transposed, uint vertexCount, uint vertexSize, byte* lastVertex)
        {
            for (uint k = 0; k < 4; k += 2)
            {
                var vertexOffset = k;

                var p = (ushort)(lastVertex[k] | (lastVertex[k + 1] << 8));

                for (uint i = 0; i < vertexCount; ++i)
                {
                    var v = (ushort)(buffer[i] | (buffer[i + vertexCount] << 8));

                    v = (ushort)(Decode.UnZigZag16(v) + p);

                    transposed[vertexOffset] = (byte)v;
                    transposed[vertexOffset + 1] = (byte)(v >> 8);

                    p = v;

                    vertexOffset += vertexSize;
                }

                buffer += vertexCount * 2;
            }
        }

        static void DecodeDeltasXor32(byte* buffer, byte* transposed, uint vertexCount, uint vertexSize, byte* lastVertex, int rot)
        {
            var p = lastVertex[0] | ((uint)lastVertex[1] << 8) | ((uint)lastVertex[2] << 16) | ((uint)lastVertex[3] << 24);

            uint vertexOffset = 0;

            for (uint i = 0; i < vertexCount; ++i)
            {
                var v = buffer[i]
                    | ((uint)buffer[i + vertexCount] << 8)
                    | ((uint)buffer[i + vertexCount * 2] << 16)
                    | ((uint)buffer[i + vertexCount * 3] << 24);

                v = Rotate(v, rot) ^ p;

                transposed[vertexOffset] = (byte)v;
                transposed[vertexOffset + 1] = (byte)(v >> 8);
                transposed[vertexOffset + 2] = (byte)(v >> 16);
                transposed[vertexOffset + 3] = (byte)(v >> 24);

                p = v;

                vertexOffset += vertexSize;
            }
        }

        static uint Rotate(uint v, int r)
        {
            return (v << r) | (v >> ((32 - r) & 31));
        }

        static byte* DecodeBlock(
            byte* data,
            byte* dataEnd,
            byte* vertexData,
            uint vertexCount,
            uint vertexSize,
            byte* lastVertex,
            byte* channels,
            int version,
            byte* buffer,
            byte* transposed
            )
        {
            Assert.IsTrue(vertexCount > 0 && vertexCount <= Decode.k_VertexBlockMaxSize);

            var vertexCountAligned = (vertexCount + Decode.k_ByteGroupSize - 1) & ~(Decode.k_ByteGroupSize - 1);

            var controlSize = version == 0 ? 0 : vertexSize / 4;
            if ((uint)(dataEnd - data) < controlSize)
                return null;

            var control = data;
            data += controlSize;

            for (uint k = 0; k < vertexSize; k += 4)
            {
                var ctrlByte = version == 0 ? 0 : control[k / 4];

                for (uint j = 0; j < 4; ++j)
                {
                    var ctrl = (ctrlByte >> (int)(j * 2)) & 3;

                    if (ctrl == 3)
                    {
                        // literal encoding
                        if ((uint)(dataEnd - data) < vertexCount)
                            return null;

                        UnsafeUtility.MemCpy(buffer + j * vertexCount, data, vertexCount);
                        data += vertexCount;
                    }
                    else if (ctrl == 2)
                    {
                        // zero encoding
                        UnsafeUtility.MemSet(buffer + j * vertexCount, 0, vertexCount);
                    }
                    else
                    {
                        data = DecodeBytes(data, dataEnd, buffer + j * vertexCount, vertexCountAligned, version, ctrl);
                        if (data == null)
                            return null;
                    }
                }

                var channel = version == 0 ? 0 : channels[k / 4];

                switch (channel & 3)
                {
                    case 0:
                        DecodeDeltas8(buffer, transposed + k, vertexCount, vertexSize, lastVertex + k);
                        break;
                    case 1:
                        DecodeDeltas16(buffer, transposed + k, vertexCount, vertexSize, lastVertex + k);
                        break;
                    case 2:
                        DecodeDeltasXor32(buffer, transposed + k, vertexCount, vertexSize, lastVertex + k, (32 - (channel >> 4)) & 31);
                        break;
                    default:
                        // invalid channel type
                        return null;
                }
            }

            UnsafeUtility.MemCpy(vertexData, transposed, vertexCount * vertexSize);
            UnsafeUtility.MemCpy(lastVertex, transposed + vertexSize * (vertexCount - 1), vertexSize);

            return data;
        }

        static uint GetVertexBlockSize(uint vertexSize)
        {
            // make sure the entire block fits into the scratch buffer and is aligned to byte group size
            // note: the block size is implicitly part of the format, so we can't change it without breaking compatibility
            var result = (Decode.k_VertexBlockSizeBytes / vertexSize) & ~(Decode.k_ByteGroupSize - 1);

            return (result < Decode.k_VertexBlockMaxSize) ? result : Decode.k_VertexBlockMaxSize;
        }

        internal static void ApplyExponentialFilter(NativeArray<byte> target, uint vertexCount, uint vertexSize)
        {
            var data = target.Reinterpret<uint>(sizeof(byte));
            var count = (vertexSize * vertexCount) / 4;
            for (var i = 0; i < count; i++)
            {
                var v = data[i];

                // decode mantissa and exponent
                var m = (int)(v << 8) >> 8;
                var e = (int)v >> 24;

                // optimized version of ldexp(float(m), e)
                var f = math.asfloat((uint)(e + 127) << 23) * m;

                data[i] = math.asuint(f);
            }
        }

        internal static void ApplyQuaternionFilter(NativeArray<byte> target, uint vertexCount)
        {
            // 32767 / sqrt(2)
            const float scale = 32767f * k_SqrtHalf;

            var dst = target.Reinterpret<short>(sizeof(byte));

            for (var i = 0; i < vertexCount; ++i)
            {
                // recover scale from the high byte/word of the component
                var sf = dst[i * 4 + 3] | 3;
                var s = (float)sf;

                // convert x/y/z to floating point (unscaled! implied scale of 1/sqrt(2) * 1/sf)
                float x = dst[i * 4 + 0];
                float y = dst[i * 4 + 1];
                float z = dst[i * 4 + 2];

                // reconstruct w as a square root (unscaled); we clamp to 0f to avoid NaN due to precision errors
                var ws = s * s;
                var ww = ws * 2f - x * x - y * y - z * z;
                var w = math.sqrt(ww >= 0f ? ww : 0f);

                // compute final scale; note that all computations above are unscaled
                // we need to divide by sf to get out of fixed point, divide by sqrt(2) to renormalize and multiply by 32767 to get to int16 range
                var ss = scale / s;

                // rounded signed float->int
                var xf = (int)(x * ss + (x >= 0f ? .5f : -.5f));
                var yf = (int)(y * ss + (y >= 0f ? .5f : -.5f));
                var zf = (int)(z * ss + (z >= 0f ? .5f : -.5f));
                var wf = (int)(w * ss + .5f);

                var qc = dst[i * 4 + 3] & 3;

                // output order is dictated by input index
                dst[i * 4 + ((qc + 1) & 3)] = (short)xf;
                dst[i * 4 + ((qc + 2) & 3)] = (short)yf;
                dst[i * 4 + ((qc + 3) & 3)] = (short)zf;
                dst[i * 4 + ((qc + 0) & 3)] = (short)wf;
            }
        }

        internal static void ApplyOctahedralFilterOct8(NativeArray<byte> target, uint vertexCount)
        {
            const float max = sbyte.MaxValue;

            var dst = target.Reinterpret<sbyte>(sizeof(byte));

            for (var i = 0; i < 4 * vertexCount; i += 4)
            {
                // convert x and y to floats and reconstruct z; this assumes zf encodes 1f at the same bit count
                float x = dst[i + 0];
                float y = dst[i + 1];
                var z = dst[i + 2] - math.abs(x) - math.abs(y);

                // fixup octahedral coordinates for z<0
                var t = (z >= 0f) ? 0f : z;

                x += (x >= 0f) ? t : -t;
                y += (y >= 0f) ? t : -t;

                // compute normal length & scale
                var l = math.sqrt(x * x + y * y + z * z);
                var s = max / l;

                // rounded signed float->int
                dst[i + 0] = (sbyte)(int)(x * s + (x >= 0f ? .5f : -.5f));
                dst[i + 1] = (sbyte)(int)(y * s + (y >= 0f ? .5f : -.5f));
                dst[i + 2] = (sbyte)(int)(z * s + (z >= 0f ? .5f : -.5f));
                // keep dst[i + 3] as is
            }
        }

        internal static void ApplyOctahedralFilterOct12(NativeArray<byte> target, uint vertexCount)
        {
            const float max = short.MaxValue;

            var dst = target.Reinterpret<short>(sizeof(byte));

            for (var i = 0; i < 4 * vertexCount; i += 4)
            {
                // convert x and y to floats and reconstruct z; this assumes zf encodes 1f at the same bit count
                float x = dst[i + 0];
                float y = dst[i + 1];
                var z = dst[i + 2] - math.abs(x) - math.abs(y);

                // fixup octahedral coordinates for z<0
                var t = (z >= 0f) ? 0f : z;

                x += (x >= 0f) ? t : -t;
                y += (y >= 0f) ? t : -t;

                // compute normal length & scale
                var l = math.sqrt(x * x + y * y + z * z);
                var s = max / l;

                // rounded signed float->int
                dst[i + 0] = (short)(int)(x * s + (x >= 0f ? .5f : -.5f));
                dst[i + 1] = (short)(int)(y * s + (y >= 0f ? .5f : -.5f));
                dst[i + 2] = (short)(int)(z * s + (z >= 0f ? .5f : -.5f));
                // keep dst[i + 3] as is
            }
        }

        internal static void ApplyColorFilter8(NativeArray<byte> target, uint vertexCount)
        {
            const float max = byte.MaxValue;

            for (var i = 0; i < 4 * vertexCount; i += 4)
            {
                // recover scale from alpha high bit
                int alphaScale = target[i + 3];
                alphaScale |= alphaScale >> 1;
                alphaScale |= alphaScale >> 2;
                alphaScale |= alphaScale >> 4;

                // convert to RGB in fixed point (co/cg are sign extended)
                int y = target[i + 0];
                int co = (sbyte)target[i + 1];
                int cg = (sbyte)target[i + 2];

                var r = y + co - cg;
                var g = y + cg;
                var b = y - co - cg;

                // expand alpha by one bit to match other components
                int a = target[i + 3];
                a = ((a << 1) & alphaScale) | (a & 1);

                // compute scaling factor
                var ss = max / alphaScale;

                // rounded float->int
                target[i + 0] = (byte)(int)(r * ss + .5f);
                target[i + 1] = (byte)(int)(g * ss + .5f);
                target[i + 2] = (byte)(int)(b * ss + .5f);
                target[i + 3] = (byte)(int)(a * ss + .5f);
            }
        }

        internal static void ApplyColorFilter16(NativeArray<byte> target, uint vertexCount)
        {
            const float max = ushort.MaxValue;

            var dst = target.Reinterpret<ushort>(sizeof(byte));

            for (var i = 0; i < 4 * vertexCount; i += 4)
            {
                // recover scale from alpha high bit
                int alphaScale = dst[i + 3];
                alphaScale |= alphaScale >> 1;
                alphaScale |= alphaScale >> 2;
                alphaScale |= alphaScale >> 4;
                alphaScale |= alphaScale >> 8;

                // convert to RGB in fixed point (co/cg are sign extended)
                int y = dst[i + 0];
                int co = (short)dst[i + 1];
                int cg = (short)dst[i + 2];

                var r = y + co - cg;
                var g = y + cg;
                var b = y - co - cg;

                // expand alpha by one bit to match other components
                int a = dst[i + 3];
                a = ((a << 1) & alphaScale) | (a & 1);

                // compute scaling factor
                var ss = max / alphaScale;

                // rounded float->int
                dst[i + 0] = (ushort)(int)(r * ss + .5f);
                dst[i + 1] = (ushort)(int)(g * ss + .5f);
                dst[i + 2] = (ushort)(int)(b * ss + .5f);
                dst[i + 3] = (ushort)(int)(a * ss + .5f);
            }
        }
    }
}

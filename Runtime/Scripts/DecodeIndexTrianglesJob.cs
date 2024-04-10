using System;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Assertions;

namespace Meshoptimizer
{

    [BurstCompile]
    unsafe struct DecodeIndexTrianglesJob : IJob
    {

        [WriteOnly]
        public NativeArray<byte> destination;

        [ReadOnly]
        public NativeSlice<byte> source;

        public int indexCount;
        public int indexSize;

        [WriteOnly]
        [NativeDisableContainerSafetyRestriction]
        public NativeSlice<int> returnCode;

        public FunctionPointer<WriteTriangleDelegate> triangleWriter;

        public void Execute()
        {

            Assert.AreEqual(0, indexCount % 3);
            Assert.IsTrue(indexSize == 2 || indexSize == 4);

            // the minimum valid encoding is header, 1 byte per triangle and a 16-byte codeAux table
            if (source.Length < 1 + indexCount / 3 + 16)
            {
                returnCode[0] = -2;
                return;
            }

            var firstByte = source[0];
            if ((firstByte & 0xf0) != Decode.indexHeader)
            {
                returnCode[0] = -1;
                return;
            }

            var version = (byte)(firstByte & 0x0f);
            if (version > 1)
            {
                returnCode[0] = -1;
                return;
            }

            var edgeFifo = InitFifo(32);
            var vertexFifo = InitFifo(16);

            uint edgeFifoOffset = 0;
            uint vertexFifoOffset = 0;

            uint next = 0;
            uint last = 0;

            var fecMax = version >= 1 ? (byte)13 : (byte)15;

            var buffer = (byte*)source.GetUnsafeReadOnlyPtr();
            // since we store 16-byte codeAux table at the end, triangle data has to begin before dataSafeEnd
            var code = buffer + 1;
            var data = code + indexCount / 3;
            var dataSafeEnd = buffer + source.Length - 16;

            var destinationPtr = destination.GetUnsafePtr();

            for (uint i = 0; i < indexCount; i += 3)
            {
                // make sure we have enough data to read for a triangle
                // each triangle reads at most 16 bytes of data: 1b for codeAux and 5b for each free index
                // after this we can be sure we can read without extra bounds checks
                if (data > dataSafeEnd)
                {
                    returnCode[0] = -2;
                    return;
                }

                var codeTri = *code++;

                if (codeTri < 0xf0)
                {
                    var fe = (byte)(codeTri >> 4);

                    // fifo reads are wrapped around 16 entry buffer
                    // var tmpIndex = ;
                    var fifoIndex = (edgeFifoOffset - 1 - fe) & 0xf;
                    var a = edgeFifo[(int)fifoIndex];
                    var b = edgeFifo[(int)fifoIndex | 0x10];

                    var fec = (byte)(codeTri & 15);

                    // note: this is the most common path in the entire decoder
                    // inside this if we try to stay branch-less since these aren't predictable
                    if (fec < fecMax)
                    {
                        // fifo reads are wrapped around 16 entry buffer
                        var cf = vertexFifo[(int)((vertexFifoOffset - 1 - fec) & 15)];
                        var c = (fec == 0) ? next : cf;

                        var fec0 = fec == 0 ? 1u : 0u;
                        next += fec0;

                        // output triangle
                        triangleWriter.Invoke(destinationPtr, i, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(ref vertexFifo, c, ref vertexFifoOffset, fec0);

                        PushEdgeFifo(ref edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, a, c, ref edgeFifoOffset);
                    }
                    else
                    {
                        uint c;

                        // fec - (fec ^ 3) decodes 13, 14 into -1, 1
                        // note that we need to update the last index since free indices are delta-encoded
                        last = c = (fec != 15) ? (uint)(last + (fec - (fec ^ 3))) : DecodeIndex(ref data, last);

                        // output triangle
                        triangleWriter.Invoke(destinationPtr, i, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(ref vertexFifo, c, ref vertexFifoOffset);

                        PushEdgeFifo(ref edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, a, c, ref edgeFifoOffset);
                    }
                }
                else
                {
                    // fast path: read codeAux from the table
                    if (codeTri < 0xfe)
                    {
                        var codeAux = dataSafeEnd[codeTri & 15];

                        // note: table can't contain feb/fec=15
                        var feb = codeAux >> 4;
                        var fec = codeAux & 15;

                        // fifo reads are wrapped around 16 entry buffer
                        // also note that we increment next for all three vertices before decoding indices - this matches encoder behavior
                        var a = next++;

                        var bf = vertexFifo[(int)((vertexFifoOffset - feb) & 15)];
                        var b = (feb == 0) ? next : bf;

                        var feb0 = feb == 0 ? 1u : 0u;
                        next += feb0;

                        var cf = vertexFifo[(int)((vertexFifoOffset - fec) & 15)];
                        var c = (fec == 0) ? next : cf;

                        var fec0 = fec == 0 ? 1u : 0u;
                        next += fec0;

                        // output triangle
                        triangleWriter.Invoke(destinationPtr, i, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(ref vertexFifo, a, ref vertexFifoOffset);
                        PushVertexFifo(ref vertexFifo, b, ref vertexFifoOffset, feb0);
                        PushVertexFifo(ref vertexFifo, c, ref vertexFifoOffset, fec0);

                        PushEdgeFifo(ref edgeFifo, b, a, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, a, c, ref edgeFifoOffset);
                    }
                    else
                    {
                        // slow path: read a full byte for codeAux instead of using a table lookup
                        var codeAux = *data++;

                        var fea = codeTri == 0xfe ? 0 : 15;
                        var feb = codeAux >> 4;
                        var fec = codeAux & 15;

                        // reset: codeAux is 0 but encoded as not-a-table
                        if (codeAux == 0)
                            next = 0;

                        // fifo reads are wrapped around 16 entry buffer
                        // also note that we increment next for all three vertices before decoding indices - this matches encoder behavior
                        var a = (fea == 0) ? next++ : 0;
                        var b = (feb == 0) ? next++ : vertexFifo[(int)((vertexFifoOffset - feb) & 15)];
                        var c = (fec == 0) ? next++ : vertexFifo[(int)((vertexFifoOffset - fec) & 15)];

                        // note that we need to update the last index since free indices are delta-encoded
                        if (fea == 15)
                            last = a = DecodeIndex(ref data, last);

                        if (feb == 15)
                            last = b = DecodeIndex(ref data, last);

                        if (fec == 15)
                            last = c = DecodeIndex(ref data, last);

                        // output triangle
                        triangleWriter.Invoke(destinationPtr, i, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(ref vertexFifo, a, ref vertexFifoOffset);
                        PushVertexFifo(ref vertexFifo, b, ref vertexFifoOffset, (feb == 0) || (feb == 15) ? 1u : 0u);
                        PushVertexFifo(ref vertexFifo, c, ref vertexFifoOffset, (fec == 0) || (fec == 15) ? 1u : 0u);

                        PushEdgeFifo(ref edgeFifo, b, a, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(ref edgeFifo, a, c, ref edgeFifoOffset);
                    }
                }
            }

            edgeFifo.Dispose();
            vertexFifo.Dispose();

            // we should've read all data bytes and stopped at the boundary between data and codeAux table
            if (data != dataSafeEnd)
            {
                returnCode[0] = -3;
                return;
            }

            returnCode[0] = 0;
        }

        static uint DecodeIndex(ref byte* data, uint last)
        {
            var v = Decode.DecodeVByte(ref data);
            var d = (uint)((v >> 1) ^ -(int)(v & 1));

            return last + d;
        }

        public delegate void WriteTriangleDelegate(void* dst, uint offset, uint a, uint b, uint c);
        static FunctionPointer<WriteTriangleDelegate> s_WriteTriangleUInt16Method;
        static FunctionPointer<WriteTriangleDelegate> s_WriteTriangleUInt32Method;

        internal static FunctionPointer<WriteTriangleDelegate> GetTriangleWriter(int indexSize)
        {
            if (indexSize == 2)
            {
                if (!s_WriteTriangleUInt16Method.IsCreated)
                {
                    s_WriteTriangleUInt16Method = BurstCompiler.CompileFunctionPointer<WriteTriangleDelegate>(WriteTriangleUInt16);
                }
                return s_WriteTriangleUInt16Method;
            }

            if (!s_WriteTriangleUInt32Method.IsCreated)
            {
                s_WriteTriangleUInt32Method = BurstCompiler.CompileFunctionPointer<WriteTriangleDelegate>(WriteTriangleUInt32);
            }
            return s_WriteTriangleUInt32Method;
        }

        [BurstCompile, MonoPInvokeCallback(typeof(WriteTriangleDelegate))]
        static void WriteTriangleUInt16(void* dst, uint offset, uint a, uint b, uint c)
        {
            ((ushort*)dst)[(int)offset] = (ushort)a;
            ((ushort*)dst)[(int)(offset + 1)] = (ushort)b;
            ((ushort*)dst)[(int)(offset + 2)] = (ushort)c;
        }

        [BurstCompile, MonoPInvokeCallback(typeof(WriteTriangleDelegate))]
        static void WriteTriangleUInt32(void* dst, uint offset, uint a, uint b, uint c)
        {
            ((uint*)dst)[(int)offset] = a;
            ((uint*)dst)[(int)(offset + 1)] = b;
            ((uint*)dst)[(int)(offset + 2)] = c;
        }

        static NativeArray<uint> InitFifo(uint length)
        {
            var fifo = new NativeArray<uint>((int)length, Allocator.Temp);
            for (var i = 0; i < fifo.Length; i++)
            {
                fifo[i] = uint.MaxValue;
            }
            return fifo;
        }

        static void PushEdgeFifo(ref NativeArray<uint> fifo, uint a, uint b, ref uint offset)
        {
            fifo[(int)offset] = a;
            fifo[(int)(offset | 0x10)] = b;
            offset = (offset + 1) & 15;
        }

        static void PushVertexFifo(ref NativeArray<uint> fifo, uint v, ref uint offset, uint cond = 1)
        {
            fifo[(int)offset] = v;
            offset = (offset + cond) & 15;
        }
    }
}

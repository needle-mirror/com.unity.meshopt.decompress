using System;
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
        public NativeArray<byte>.ReadOnly source;

        public int indexCount;
        public int indexSize;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public NativeArray<int> returnCode;

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
            if ((firstByte & 0xf0) != Decode.k_IndexHeader)
            {
                returnCode[0] = -1;
                return;
            }

            var version = (byte)(firstByte & 0x0f);
            if (version > Decode.k_DecodeIndexVersion)
            {
                returnCode[0] = -1;
                return;
            }

            // edge fifo: 16 entries of edge start (a) followed by 16 entries of edge end (b)
            var edgeFifo = stackalloc uint[32];
            var vertexFifo = stackalloc uint[16];
            UnsafeUtility.MemSet(edgeFifo, 0xff, 32 * sizeof(uint));
            UnsafeUtility.MemSet(vertexFifo, 0xff, 16 * sizeof(uint));

            uint edgeFifoOffset = 0;
            uint vertexFifoOffset = 0;

            uint next = 0;
            uint last = 0;

            var fecMax = version >= 1 ? 13 : 15;

            var buffer = (byte*)source.GetUnsafeReadOnlyPtr();
            // since we store 16-byte codeAux table at the end, triangle data has to begin before dataSafeEnd
            var code = buffer + 1;
            var codeEnd = code + indexCount / 3;
            var data = codeEnd;
            var dataSafeEnd = buffer + source.Length - 16;

            var codeAuxTable = dataSafeEnd;

            var destinationPtr = (byte*)destination.GetUnsafePtr();

            // each triangle reads at most 16 bytes of data: 1b for codeAux and 5b for each free index
            while (code < codeEnd)
            {
                var codeTri = *code++;

                if (codeTri < 0xf0)
                {
                    var fe = codeTri >> 4;

                    // fifo reads are wrapped around 16 entry buffer
                    var fifoIndex = (edgeFifoOffset - 1 - (uint)fe) & 15;
                    var a = edgeFifo[fifoIndex];
                    var b = edgeFifo[fifoIndex | 0x10];
                    uint c;

                    var fec = codeTri & 15;

                    // note: this is the most common path in the entire decoder
                    // inside this if we try to stay branch-less since these aren't predictable
                    if (fec < fecMax)
                    {
                        // fifo reads are wrapped around 16 entry buffer
                        var cf = vertexFifo[(vertexFifoOffset - 1 - (uint)fec) & 15];
                        c = (fec == 0) ? next : cf;

                        var fec0 = fec == 0 ? 1u : 0u;
                        next += fec0;

                        // push vertex fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, c, ref vertexFifoOffset, fec0);
                    }
                    else
                    {
                        // make sure we have enough data to read for a triangle; this check covers worst case advance
                        if (data > dataSafeEnd)
                        {
                            returnCode[0] = -2;
                            return;
                        }

                        // fec * 2 - 27 decodes 13, 14 into -1, 1
                        // note that we need to update the last index since free indices are delta-encoded
                        last = c = (fec != 15) ? (uint)(last + (fec * 2 - 27)) : DecodeIndex(ref data, last);

                        // push vertex fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, c, ref vertexFifoOffset);
                    }

                    // push edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                    PushEdgeFifo(edgeFifo, c, b, ref edgeFifoOffset);
                    PushEdgeFifo(edgeFifo, a, c, ref edgeFifoOffset);

                    // output triangle
                    destinationPtr = WriteTriangle(destinationPtr, indexSize, a, b, c);
                }
                else
                {
                    // fast path: read codeAux from the table
                    if (codeTri < 0xfe)
                    {
                        var codeAux = codeAuxTable[codeTri & 15];

                        // note: table can't contain feb/fec=15
                        var feb = codeAux >> 4;
                        var fec = codeAux & 15;

                        // fifo reads are wrapped around 16 entry buffer
                        // also note that we increment next for all three vertices before decoding indices - this matches encoder behavior
                        var a = next++;

                        var bf = vertexFifo[(vertexFifoOffset - (uint)feb) & 15];
                        var b = (feb == 0) ? next : bf;

                        var feb0 = feb == 0 ? 1u : 0u;
                        next += feb0;

                        var cf = vertexFifo[(vertexFifoOffset - (uint)fec) & 15];
                        var c = (fec == 0) ? next : cf;

                        var fec0 = fec == 0 ? 1u : 0u;
                        next += fec0;

                        // output triangle
                        destinationPtr = WriteTriangle(destinationPtr, indexSize, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, a, ref vertexFifoOffset);
                        PushVertexFifo(vertexFifo, b, ref vertexFifoOffset, feb0);
                        PushVertexFifo(vertexFifo, c, ref vertexFifoOffset, fec0);

                        PushEdgeFifo(edgeFifo, b, a, ref edgeFifoOffset);
                        PushEdgeFifo(edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(edgeFifo, a, c, ref edgeFifoOffset);
                    }
                    else
                    {
                        // make sure we have enough data to read for a triangle; this check covers worst case advance
                        if (data > dataSafeEnd)
                        {
                            returnCode[0] = -2;
                            return;
                        }

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
                        var b = (feb == 0) ? next++ : vertexFifo[(vertexFifoOffset - (uint)feb) & 15];
                        var c = (fec == 0) ? next++ : vertexFifo[(vertexFifoOffset - (uint)fec) & 15];

                        // note that we need to update the last index since free indices are delta-encoded
                        if (fea == 15)
                            last = a = DecodeIndex(ref data, last);

                        if (feb == 15)
                            last = b = DecodeIndex(ref data, last);

                        if (fec == 15)
                            last = c = DecodeIndex(ref data, last);

                        // output triangle
                        destinationPtr = WriteTriangle(destinationPtr, indexSize, a, b, c);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, a, ref vertexFifoOffset);
                        PushVertexFifo(vertexFifo, b, ref vertexFifoOffset, (feb == 0) || (feb == 15) ? 1u : 0u);
                        PushVertexFifo(vertexFifo, c, ref vertexFifoOffset, (fec == 0) || (fec == 15) ? 1u : 0u);

                        PushEdgeFifo(edgeFifo, b, a, ref edgeFifoOffset);
                        PushEdgeFifo(edgeFifo, c, b, ref edgeFifoOffset);
                        PushEdgeFifo(edgeFifo, a, c, ref edgeFifoOffset);
                    }
                }
            }

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

        static byte* WriteTriangle(byte* destination, int indexSize, uint a, uint b, uint c)
        {
            if (indexSize == 2)
            {
                var tri = (ushort*)destination;
                tri[0] = (ushort)a;
                tri[1] = (ushort)b;
                tri[2] = (ushort)c;

                return (byte*)(tri + 3);
            }
            else
            {
                var tri = (uint*)destination;
                tri[0] = a;
                tri[1] = b;
                tri[2] = c;

                return (byte*)(tri + 3);
            }
        }

        static void PushEdgeFifo(uint* fifo, uint a, uint b, ref uint offset)
        {
            fifo[offset] = a;
            fifo[offset | 0x10] = b;
            offset = (offset + 1) & 15;
        }

        static void PushVertexFifo(uint* fifo, uint v, ref uint offset, uint cond = 1)
        {
            fifo[offset] = v;
            offset = (offset + cond) & 15;
        }
    }
}

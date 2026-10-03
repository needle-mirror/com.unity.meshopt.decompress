using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace Meshoptimizer
{

    [BurstCompile]
    unsafe struct DecodeIndexSequenceJob : IJob
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

            // the minimum valid encoding is header, 1 byte per index and a 4-byte tail
            if (source.Length < 1 + indexCount + 4)
            {
                returnCode[0] = -2;
                return;
            }

            if ((source[0] & 0xf0) != Decode.k_SequenceHeader)
            {
                returnCode[0] = -1;
                return;
            }

            var version = source[0] & 0x0f;
            if (version > Decode.k_DecodeIndexVersion)
            {
                returnCode[0] = -1;
                return;
            }

            var buffer = (byte*)source.GetUnsafeReadOnlyPtr();
            var data = buffer + 1;
            var dataSafeEnd = buffer + source.Length - 4;

            var destinationPtr = destination.GetUnsafePtr();

            var last = stackalloc uint[2];
            last[0] = 0;
            last[1] = 0;

            if (indexSize == 2)
            {
                for (var i = 0; i < indexCount; ++i)
                {
                    // make sure we have enough data to read
                    // each index reads at most 5 bytes of data; there's a 4 byte tail after dataSafeEnd
                    // after this we can be sure we can read without extra bounds checks
                    if (data >= dataSafeEnd)
                    {
                        returnCode[0] = -2;
                        return;
                    }

                    var v = Decode.DecodeVByte(ref data);

                    // decode the index of the last baseline
                    var current = v & 1;
                    v >>= 1;

                    // reconstruct index as a delta
                    var d = (uint)((v >> 1) ^ -(int)(v & 1));
                    var index = last[current] + d;

                    // update last for the next iteration that uses it
                    last[current] = index;

                    ((ushort*)destinationPtr)[i] = (ushort)index;
                }
            }
            else
            {
                for (var i = 0; i < indexCount; ++i)
                {
                    // make sure we have enough data to read
                    // each index reads at most 5 bytes of data; there's a 4 byte tail after dataSafeEnd
                    // after this we can be sure we can read without extra bounds checks
                    if (data >= dataSafeEnd)
                    {
                        returnCode[0] = -2;
                        return;
                    }

                    var v = Decode.DecodeVByte(ref data);

                    // decode the index of the last baseline
                    var current = v & 1;
                    v >>= 1;

                    // reconstruct index as a delta
                    var d = (uint)((v >> 1) ^ -(int)(v & 1));
                    var index = last[current] + d;

                    // update last for the next iteration that uses it
                    last[current] = index;

                    ((uint*)destinationPtr)[i] = index;
                }
            }

            // we should've read all data bytes and stopped at the boundary between data and tail
            if (data != dataSafeEnd)
            {
                returnCode[0] = -3;
                return;
            }

            returnCode[0] = 0;
        }
    }
}

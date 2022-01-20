using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Meshoptimizer {
    
	[BurstCompile]
	unsafe struct DecodeVertexJob : IJob {
	    
	    #region Constants
	    const float k_SqrtHalf = 0.707106781186548f;
	    const byte k_VertexHeader = 0xa0;
	    const uint k_KTailMaxSize = 32;
	    #endregion

	    // [WriteOnly] // TODO: Make filtering a separate job and add WriteOnly attribute
	    public NativeArray<byte> destination;
	    
	    [ReadOnly]
	    public NativeSlice<byte> source;
	    
	    public uint vertexCount;
	    public uint vertexSize;
	    public Filter filter;
	    
	    [WriteOnly]
	    [NativeDisableContainerSafetyRestriction]
	    public NativeSlice<int> returnCode;

        public void Execute() {

	        Assert.IsTrue(vertexSize > 0 && vertexSize <= 256);
	        Assert.AreEqual(0, vertexSize % 4);
	        
	        var vertexData = (byte*) destination.GetUnsafePtr();
	        
	        var data = (byte*) source.GetUnsafeReadOnlyPtr();
	        var dataEnd = data + source.Length;

	        if ((uint)(dataEnd - data) < 1 + vertexSize) {
		        returnCode[0] = -2;
		        return;
	        }

	        var dataHeader = *data++;

	        if ((dataHeader & 0xf0) != k_VertexHeader) {
		        returnCode[0] = -1;
		        return;
	        }

	        var version = dataHeader & 0x0f;
	        if (version > 0) {
		        returnCode[0] = -1;
		        return;
	        }

	        var lastVertex = new NativeArray<byte>(256,Allocator.Temp);
	        UnsafeUtility.MemCpy(lastVertex.GetUnsafePtr(),dataEnd - vertexSize, vertexSize);

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
			        (byte*) lastVertex.GetUnsafePtr()
			        );
		        if (data == null) {
			        returnCode[0] = -2;
			        return;
		        }
		        vertexOffset += blockSize;
	        }

	        lastVertex.Dispose();

	        var tailSize = vertexSize < k_KTailMaxSize ? k_KTailMaxSize : vertexSize;

	        if ((uint)(dataEnd - data) != tailSize) {
		        returnCode[0] = -3;
		        return;
	        }

            switch (filter) {
	            // Filters - only applied if filter isn't undefined or NONE
	            case Filter.Octahedral:
		            Assert.IsTrue( vertexSize == 4 || vertexSize == 8);
		            if (vertexSize == 4) {
						ApplyOctahedralFilterOct8(destination, vertexCount);
		            } else {
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
	            case Filter.None:
	            case Filter.Undefined:
		            break;
	            default:
		            returnCode[0] = -4;
		            return;
            }

            returnCode[0] = 0;
        }
        
        
		static byte* DecodeBytesGroup(byte* data, byte* buffer, int bitsLog2) {

			byte v;
			byte enc;
		  	byte encV;
			byte* dataVar;

			void Read() {
				v = *data++;
			}

			void Next(byte bits) {
				enc = (byte)(v >> (8 - bits));
				v <<= bits;
				encV = *dataVar;
				* buffer++ = (enc == (1 << bits) - 1) ? encV : enc;
				dataVar += (enc == (1 << bits) - 1) ? 1 : 0;
			}
			
			switch (bitsLog2) {
			case 0:
				UnsafeUtility.MemSet(buffer,0,Decode.kByteGroupSize);
				return data;
			case 1:
				dataVar = data + 4;

				// 4 groups with 4 2-bit values in each byte
				Read(); Next(2); Next(2); Next(2); Next(2);
				Read(); Next(2); Next(2); Next(2); Next(2);
				Read(); Next(2); Next(2); Next(2); Next(2);
				Read(); Next(2); Next(2); Next(2); Next(2);

				return dataVar;
			case 2:
				dataVar = data + 8;

				// 8 groups with 2 4-bit values in each byte
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);
				Read(); Next(4); Next(4);

				return dataVar;
			case 3:
				UnsafeUtility.MemCpy(buffer, data, Decode.kByteGroupSize);
				return data + Decode.kByteGroupSize;
			default:
				return null;
			}
		}

		static byte* DecodeBytes( byte* data, byte* dataEnd, byte* buffer, uint bufferSize) {

	        Assert.AreEqual(0, bufferSize % Decode.kByteGroupSize);

	        var header = data;

	        // round number of groups to 4 to get number of header bytes
	        var headerSize = (bufferSize / Decode.kByteGroupSize + 3) / 4;

	        if ((uint)(dataEnd - data) < headerSize)
		        return null;

	        data += headerSize;

	        for (uint i = 0; i < bufferSize; i += Decode.kByteGroupSize)
	        {
		        if ((uint)(dataEnd - data) < Decode.kByteGroupDecodeLimit)
			        return null;

		        var headerOffset = i / Decode.kByteGroupSize;

		        var bitsLog2 = (header[headerOffset / 4] >> (int) (((headerOffset % 4) * 2)) & 3);

		        data = DecodeBytesGroup(data, buffer + i, bitsLog2);
		        if (data == null) {
			        return null;
		        }
	        }

	        return data;
        }

        static byte* DecodeBlock(byte* data, byte* dataEnd, byte* vertexData, uint vertexCount, uint vertexSize, byte* lastVertex) {
	        Assert.IsTrue(vertexCount > 0 && vertexCount <= Decode.kVertexBlockMaxSize);

	        var buffer = new NativeArray<byte>((int)Decode.kVertexBlockMaxSize,Allocator.Temp);
	        var transposed = new NativeArray<byte>((int)Decode.kVertexBlockSizeBytes,Allocator.Temp);

	        var vertexCountAligned = (vertexCount + Decode.kByteGroupSize - 1) & ~(Decode.kByteGroupSize - 1);

	        for (uint k = 0; k < vertexSize; ++k)
	        {
		        data = DecodeBytes(data, dataEnd, (byte*)buffer.GetUnsafePtr(), vertexCountAligned);
		        if (data == null) {
			        buffer.Dispose();
				    transposed.Dispose();
			        return null;
		        }

		        var vertexOffset = k;

		        var p = lastVertex[k];

		        for (uint i = 0; i < vertexCount; ++i)
		        {
			        var v = (byte) (Decode.UnZigZag8(buffer[(int)i]) + p);

			        transposed[(int)vertexOffset] = v;
			        p = v;

			        vertexOffset += vertexSize;
		        }
	        }

	        var transposedPtr = (byte*) transposed.GetUnsafeReadOnlyPtr();
	        UnsafeUtility.MemCpy(vertexData, transposedPtr, vertexCount * vertexSize);
	        UnsafeUtility.MemCpy(lastVertex, transposedPtr + vertexSize * (vertexCount - 1), vertexSize);

	        buffer.Dispose();
	        transposed.Dispose();
	        return data;
		}

        static uint GetVertexBlockSize(uint vertexSize) {
	        // make sure the entire block fits into the scratch buffer
	        var result = Decode.kVertexBlockSizeBytes / vertexSize;

	        // align to byte group size; we encode each byte as a byte group
	        // if vertex block is misaligned, it results in wasted bytes, so just truncate the block size
	        result &= ~(Decode.kByteGroupSize - 1);

	        return (result < Decode.kVertexBlockMaxSize) ? result : Decode.kVertexBlockMaxSize;
        }

        internal static void ApplyExponentialFilter(NativeArray<byte> target, uint vertexCount, uint vertexSize) {
	        var src = target.Reinterpret<uint>(sizeof(byte));
	        var dst = target.Reinterpret<float>(sizeof(byte));
	        for (var i = 0; i < (vertexSize * vertexCount) / 4; i++) {
		        var v = src[i];
		        var exp = (int) v >> 24;
		        var mantissa = (int) (v << 8) >> 8;
		        dst[i] = math.pow(2.0f, exp) * mantissa;
	        }
        }

        internal static void ApplyQuaternionFilter(NativeArray<byte> target, uint vertexCount) {
	        var dst = target.Reinterpret<short>(sizeof(byte));

	        for (var i = 0; i < vertexCount; ++i)
	        {
		        // recover scale from the high byte of the component
		        var sf = dst[i * 4 + 3] | 3;
		        var ss = k_SqrtHalf / sf;
	        
		        // convert x/y/z to [-1..1] (scaled...)
		        var x = dst[i * 4 + 0] * ss;
		        var y = dst[i * 4 + 1] * ss;
		        var z = dst[i * 4 + 2] * ss;
	        
		        // reconstruct w as a square root; we clamp to 0f to avoid NaN due to precision errors
		        var ww = 1f - x * x - y * y - z * z;
		        var w = math.sqrt( math.max(0, ww) );
	        
		        // rounded signed float->int
		        var xf = (int)(x * 32767f + (x >= 0f ? .5f : -.5f));
		        var yf = (int)(y * 32767f + (y >= 0f ? .5f : -.5f));
		        var zf = (int)(z * 32767f + (z >= 0f ? .5f : -.5f));
		        var wf = (int)(w * 32767f + 0.5f);
	        
		        var qc = dst[i * 4 + 3] & 3;
	        
		        // output order is dictated by input index
		        dst[i * 4 + ((qc + 1) & 3)] = (short)xf;
		        dst[i * 4 + ((qc + 2) & 3)] = (short)yf;
		        dst[i * 4 + ((qc + 3) & 3)] = (short)zf;
		        dst[i * 4 + ((qc + 0) & 3)] = (short)wf;
	        }
        }

        internal static void ApplyOctahedralFilterOct8(NativeArray<byte> target, uint vertexSize) {
	        var dst = target.Reinterpret<sbyte>(sizeof(byte));
	        
	        for (var i = 0; i < 4 * vertexSize; i += 4) {
		        var x = (float) dst[i + 0];
		        var y = (float) dst[i + 1];
		        var one = (float) dst[i + 2];
		        x /= one;
		        y /= one;
		        var z = 1.0f - math.abs(x) - math.abs(y);
		        var t = math.max(-z, 0.0f);
		        x -= (x >= 0) ? t : -t;
		        y -= (y >= 0) ? t : -t;
		        var h = sbyte.MaxValue / math.sqrt(x*x + y*y + z*z);
		        dst[i + 0] = (sbyte) math.round(x * h);
		        dst[i + 1] = (sbyte) math.round(y * h);
		        dst[i + 2] = (sbyte) math.round(z * h);
		        // keep dst[i + 3] as is
	        }
        }

        internal static void ApplyOctahedralFilterOct12(NativeArray<byte> target, uint vertexSize) {
	        var dst = target.Reinterpret<short>(sizeof(byte));
	        
	        for (var i = 0; i < 4 * vertexSize; i += 4) {
		        var x = (float) dst[i + 0];
		        var y = (float) dst[i + 1];
		        var one = (float) dst[i + 2];
		        x /= one;
		        y /= one;
		        var z = 1.0f - math.abs(x) - math.abs(y);
		        var t = math.max(-z, 0.0f);
		        x -= (x >= 0) ? t : -t;
		        y -= (y >= 0) ? t : -t;
		        var h = short.MaxValue / math.sqrt(x*x + y*y + z*z);
		        dst[i + 0] = (short) math.round(x * h);
		        dst[i + 1] = (short) math.round(y * h);
		        dst[i + 2] = (short) math.round(z * h);
		        // keep dst[i + 3] as is
	        }
        }
    }
}
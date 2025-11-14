using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Meshoptimizer
{
    static class NativeSliceExtensions
    {
        [Obsolete("Use the overload that accepts a NativeArray<byte>.ReadOnly data")]
        public static unsafe NativeArray<T> AsNativeArray<T>(this NativeSlice<T> src) where T : unmanaged
        {
            if (src.Stride != UnsafeUtility.SizeOf<T>())
            {
                throw new InvalidDataException("Only NativeSlice with a stride equal to the member type size is supported!");
            }
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                src.GetUnsafeReadOnlyPtr(),
                src.Length,
                Allocator.None
            );
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(array: ref array, safetyHandle);
#endif
            return array;
        }
    }
}

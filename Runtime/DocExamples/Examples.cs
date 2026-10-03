using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace Meshoptimizer.Documentation.Examples
{
    class Examples : MonoBehaviour
    {
        #region DecodeGltfBufferExampleAsync
        async Task DecodeGltfBufferExampleAsync(NativeArray<byte>.ReadOnly inputBuffer)
        {

            // - The size (in bytes) and number of elements (indices/vertices)
            const int elementSize = 24;
            const int elementCount = 100;

            // - Type of buffer
            const Mode mode = Mode.Attributes; // Vertex attributes (position/normal) in this case

            // - (optional) Type of filter (in case of `Attributes` mode)
            const Filter filter = Filter.Exponential;

            // - Destination/output buffer
            //   Its size is determined by the element size and count
            var outputBuffer = new NativeArray<byte>(elementSize * elementCount, Allocator.TempJob);

            // - A NativeArray container for the return code
            //   After decoding, this first (and only) member of this array is the
            //   return code indicating if the decoding was successful (in which case
            //   it is `0`)
            var returnCode = new NativeArray<int>(1, Allocator.TempJob);

            // This creates a Job that decodes on a thread and returns
            // the JobHandle
            var jobHandle = Decode.DecodeGltfBuffer(
                returnCode,
                outputBuffer,
                elementCount,
                elementSize,
                inputBuffer,
                mode,
                filter
            );

            // This loop will wait for the job to complete.
            while (!jobHandle.IsCompleted)
            {
                await Task.Yield();
            }

            // Important! `Complete` has to be called on the jobHandle to release
            // its resources.
            jobHandle.Complete();

            // Check the returnValue for errors
            if (returnCode[0] == 0)
            {
                // You can now access the outputBuffer
            }
            else
            {
                Debug.LogError("Meshopt decoding failed");
            }

            // Make sure you finally dispose all resources
            outputBuffer.Dispose();
            returnCode.Dispose();
        }
        #endregion

        #region DecodeGltfBufferExample
        void DecodeGltfBufferExample(NativeArray<byte>.ReadOnly inputBuffer)
        {

            // The information you need upfront is idendical, except you don't need a return code
            // container:
            const int elementSize = 24;
            const int elementCount = 100;
            const Mode mode = Mode.Attributes;
            const Filter filter = Filter.Exponential;
            var outputBuffer = new NativeArray<byte>(elementSize * elementCount, Allocator.TempJob);

            // This executes the decoding on the main thread and returns
            // the return code directly
            var returnCode = Decode.DecodeGltfBufferSync(
                outputBuffer,
                elementCount,
                elementSize,
                inputBuffer,
                mode,
                filter
            );

            // Check the returnValue for errors
            if (returnCode == 0)
            {
                // You can now access the outputBuffer
            }
            else
            {
                Debug.LogError("Meshopt decoding failed");
            }

            // Make sure you finally dispose all resources
            outputBuffer.Dispose();
        }
        #endregion
    }
}

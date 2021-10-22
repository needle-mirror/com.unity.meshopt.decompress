# About meshoptimizer decompression for Unity

Use the *meshoptimizer decompression for Unity* package to decode [meshoptimizer][meshopt] compressed index/vertex buffers efficiently in Burst-compiled C# Jobs off the main thread.

It is a port of the original [meshoptimizer compression][meshopt-compression] by
[Arseny Kapoulkine (zeux)][zeux].

This package is available as a pre-release package, so it is still in the process of becoming stable enough to release. The features and documentation in this package might change before it is ready for release. 

<a name="Installation"></a>

## Installation

To install this package, refer to the instructions that match your Unity Editor version:

### Version 2021.1 and later

To install this package, make sure you [enable pre-release packages](https://docs.unity3d.com/2021.1/Documentation/Manual/class-PackageManager.html#advanced_preview) in the Unity Editor's Package Manager, and then follow the [installation instructions in the Unity User Manual](https://docs.unity3d.com/Documentation/Manual/upm-ui-install.html). 

### Version 2020.3 

To install this package, make sure you [show preview packages](https://docs.unity3d.com/2020.3/Documentation/Manual/class-PackageManager.html#advanced_preview) in the Unity Editor's Package Manager, and then follow the [installation instructions in the Unity User Manual](https://docs.unity3d.com/2020.3/Documentation/Manual/upm-ui-install.html).

### Version 2019.4 

To install this package, make sure you [show preview packages](https://docs.unity3d.com/2019.4/Documentation/Manual/upm-ui-list.html#ShowPreview) in the Unity Editor's Package Manager, and then follow the [installation instructions in the Unity User Manual](https://docs.unity3d.com/2019.4/Documentation/Manual/upm-ui-install.html).

## Requirements

This version of *meshoptimizer decompression for Unity* is compatible with the following versions of the Unity Editor:

* 2018.4 and later

## Helpful links

If you are new to *meshoptimizer decompression for Unity*, or have a question after reading the documentation, you can:

* Join our [support forum](https://forum.unity.com/forums/scripting.12/).
* Follow us on [Twitter](http://www.twitter.com/unity3d).

<a name="UsingPackageName"></a>

# Using *meshoptimizer decompression for Unity*

Here's a pseudo-code example how to decode a meshoptmizer buffer

```csharp
IEnumerator Start() {

   // The information you need upfront is:
   // - The compressed input buffer ready as NativeArray<byte> (or NativeSlice<byte>)
   var inputBuffer = new NativeArray<byte>(...);

   // - The size (in bytes) and number of elements (indices/vertices)
   var elementSize = 24;
   var elementCount = 100;

   // - Type of buffer
   var mode = Decompress.Mode.Attributes; // Vertex attributes (position/normal) in this case

   // - (optional) Type of filter (in case of `Attributes` mode)
   var filter = Decompress.Filter.Exponential;

   // - Destination/output buffer
   //   Its size is determined by the element size and count
   var outputBuffer = new NativeArray<byte>( elementSize * elementCount, Allocator.TempJob);

   // - A NativeArray container for the return code
   //   After decompression, this first (and only) member of this array is the
   //   return code indicating if the decompression was successful (in which case 
   //   it is `0`)
   var returnCode = new NativeArray<int>(1,Allocator.TempJob);

   // This creates a Job that does the decompression on a thread and returns
   // the JobHandle
   var jobHandle = DecodeGltfBuffer(
      returnCode,
      outputBuffer,
      elementCount,
      elementSize,
      inputBuffer,
      mode,
      filter
      );

   // This loop will wait for the job to complete.
   while(!jobHandle.IsCompleted) {
      yield return null;
   }

   // Important! `Complete` has to be called on the jobHandle to release
   // its resources.
   jobHandle.Complete();

   // Check the returnValue for errors
   if(returnCode[0]==0) {
      // You can now access the outputBuffer
      ...
   } else {
      Debug.LogError("Meshopt decompression failed");
   }

   // Make sure you finally dispose all resources
   inputBuffer.Dispose();
   outputBuffer.Dispose();
   returnCode.Dispose();
}

```

An alternative method is to decompress synchronously on the main thread, which has a bit less boilerplate code (but is slower).


```csharp
void Start() {

   // The information you need upfront is idendical, except you don't need a return code
   // container:
   var inputBuffer = new NativeArray<byte>(...);
   var elementSize = 24;
   var elementCount = 100;
   var mode = Decompress.Mode.Attributes;
   var filter = Decompress.Filter.Exponential;
   var outputBuffer = new NativeArray<byte>( elementSize * elementCount, Allocator.TempJob);

   // This executes the decompression on the main thread and returns
   // the return code directly
   var returnCode = DecodeGltfBufferSync(
      returnCode,
      outputBuffer,
      elementCount,
      elementSize,
      inputBuffer,
      mode,
      filter
      );

   // Check the returnValue for errors
   if(returnCode==0) {
      // You can now access the outputBuffer
      ...
   } else {
      Debug.LogError("Meshopt decompression failed");
   }

   // Make sure you finally dispose all resources
   inputBuffer.Dispose();
   outputBuffer.Dispose();
}

```

<a name="Workflows"></a>
# *meshoptimizer decompression for Unity* workflows

A common use-case for meshoptimizer decompression is loading [glTF][gltf] files that utilize it via the [EXT_meshopt_compression][EXT_meshopt_compression] extension. The [glTFast][gltfast] package uses *meshoptimizer decompression for Unity* for this purpose. Consult it as a reference use-case.


[EXT_meshopt_compression]: https://github.com/KhronosGroup/glTF/tree/master/extensions/2.0/Vendor/EXT_meshopt_compression
[gltf]: https://www.khronos.org/gltf
[gltfast]: https://github.com/atteneder/glTFast
[meshopt]: https://github.com/zeux/meshoptimizer
[meshopt-compression]: https://github.com/zeux/meshoptimizer#vertexindex-buffer-compression
[zeux]: https://github.com/zeux


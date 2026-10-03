# About meshoptimizer mesh compression for Unity

Use the *meshoptimizer mesh compression for Unity* package to decode [meshoptimizer][meshopt] compressed index/vertex buffers efficiently in Burst-compiled C# Jobs off the main thread.

It is a port of the original [meshoptimizer compression][meshopt-compression] by
[Arseny Kapoulkine (zeux)][zeux].

> [!IMPORTANT]
> This package is available as an experimental package, so it is not ready for production use. The features and documentation in this package might change before it is verified for release.

## Installation

> [!TIP]
> Click the following link to fast-track the installation: [com.unity.meshopt.decompress](com.unity3d.kharma:upmpackage/com.unity.meshopt.decompress)

To install this package, follow the instructions for [adding a package by name](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-quick.html) in the Unity Editor and use the name `com.unity.meshopt.decompress`.

## Requirements

The *meshoptimizer mesh compression for Unity* package is compatible with Unity version of 6000.0 or later.

## Helpful links

If you are new to *meshoptimizer mesh compression for Unity*, or have a question after reading the documentation, you can:

* Join our [support forum](https://discussions.unity.com/c/unity-engine/52).

## Using *meshoptimizer mesh compression for Unity*

Here's a pseudo-code example how to decode a meshoptmizer buffer using [DecodeGltfBuffer](xref:Meshoptimizer.Decode.DecodeGltfBuffer*).

[!code-cs [extra-data](../Runtime/DocExamples/Examples.cs#DecodeGltfBufferExampleAsync)]

An alternative method is to decompress synchronously on the main thread via [DecodeGltfBufferSync](xref:Meshoptimizer.Decode.DecodeGltfBufferSync*), which has a bit less boilerplate code (but is slower).

[!code-cs [extra-data](../Runtime/DocExamples/Examples.cs#DecodeGltfBufferExample)]

## *meshoptimizer mesh compression for Unity* workflows

A common use-case for meshoptimizer mesh decoding is loading [glTF][gltf] files that utilize it via the [KHR_meshopt_compression] (or [EXT_meshopt_compression]) extension. The [glTFast][gltfast] package uses *meshoptimizer mesh compression for Unity* for this purpose. Consult it as a reference use-case.

## Apple privacy manifest

To publish applications for iOS, iPadOS, tvOS, and visionOS platforms on the App Store, you must include a [privacy manifest file](https://developer.apple.com/documentation/bundleresources/privacy_manifest_files) in your application as per [Apple’s privacy policy](https://www.apple.com/legal/privacy/en-ww/).

> [!NOTE]
> For information on creating a privacy manifest file to include in your application, refer to [Apple’s privacy manifest policy requirements](https://docs.unity3d.com/Manual/apple-privacy-manifest-policy.html).

The [UnityMeshOpt.xcprivacy](../Plugins/UnityMeshOpt.xcprivacy) manifest file outlines the required information, ensuring transparency in accordance with user privacy practices. This file lists the [types of data](https://developer.apple.com/documentation/bundleresources/privacy_manifest_files/describing_data_use_in_privacy_manifests) that your Unity applications, third-party SDKs, packages, and plug-ins collect, and the reasons for using certain [required reason API](https://developer.apple.com/documentation/bundleresources/privacy_manifest_files/describing_use_of_required_reason_api) (Apple documentation) categories. Apple also requires that certain domains be declared as [tracking](https://developer.apple.com/app-store/user-privacy-and-data-use/) (Apple documentation); these domains might be blocked unless a user provides consent.

> [!WARNING]
> If your privacy manifest doesn’t declare the use of the required reason API by you or third-party SDKs, the App Store might reject your application. Read more about the [required reason API](https://developer.apple.com/documentation/bundleresources/privacy_manifest_files/describing_use_of_required_reason_api) in Apple’s documentation.

The *meshoptimizer mesh compression for Unity* package does not collect data or engage in any data practices requiring disclosure in a privacy manifest file.

> [!NOTE]
> Note: The *meshoptimizer mesh compression for Unity* package is dependent on the following services. Refer to their manifest files for applicable data practices.
>
> * `com.unity.burst`
> * `com.unity.mathematics`

[EXT_meshopt_compression]: https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Vendor/EXT_meshopt_compression/README.md
[gltf]: https://www.khronos.org/gltf
[KHR_meshopt_compression]: https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_meshopt_compression/README.md
[gltfast]: https://github.com/atteneder/glTFast
[meshopt]: https://github.com/zeux/meshoptimizer
[meshopt-compression]: https://github.com/zeux/meshoptimizer#mesh-compression
[zeux]: https://github.com/zeux

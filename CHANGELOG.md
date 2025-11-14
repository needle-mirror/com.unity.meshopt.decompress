# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.2.0-exp.1] - 2025-11-14

### Added
- [DecodeGltfBuffer](xref:Meshoptimizer.Decode.DecodeGltfBuffer*) and [DecodeGltfBufferSync](xref:Meshoptimizer.Decode.DecodeGltfBufferSync*) overloads that allow loading from [NativeArray&lt;byte&gt;.ReadOnly](xref:Unity.Collections.NativeArray`1.ReadOnly). This makes it easier to load meshopt compressed data from existing, native memory directly without the need to create a copy in managed memory (e.g. by using [DownloadHandler.GetNativeData](xref:UnityEngine.Networking.DownloadHandler.GetNativeData) or [ConvertExistingDataToNativeArray](xref:Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray(System.Void*,System.Int32,Unity.Collections.Allocator))).
- [EditorConfig](https://editorconfig.org/) for keeping a consistent code-style.
- (CI) Automatically generated CI jobs (via Wrench/RecipeEngine; required for PackageWorks).
- (CI) Code coverage.
- (Test) DecodeIndexSequenceJob performance test.
- (Test) Package coherence tests.

### Changed
- Renamed package to *meshoptimizer mesh compression for Unity*
- Licensed under Unity Terms of Service (see <https://unity.com/legal>)
- Raised minimum required Unity version to 2022 xLTS.
- Raised dependency versions
  - [Mathematics][Mathematics] to 1.2.6
  - [Burst][Burst] to 1.8.24
- Changed source code repository structure to [monorepo](https://en.wikipedia.org/wiki/Monorepo).
- (CI) Code format check is performed by dotnet format now.
- Increased performance of index sequence decoding around 10%.

### Fixed
- Removed invalid tag `seealso` from xml doc summary.
- Invalid code references in XML docs.
- Broken links in package documentation.
- Example code compilation.
- Broken link to Apple privacy manifest in documentation.

### Deprecated
- [DecodeGltfBuffer](xref:Meshoptimizer.Decode.DecodeGltfBuffer*) and [DecodeGltfBufferSync](xref:Meshoptimizer.Decode.DecodeGltfBufferSync*) overloads that use [NativeSlice<byte>](xref:Unity.Collections.NativeSlice`1) as parameters for no good reason.

## [0.1.0-preview.7] - 2024-05-16

### Added
- Added Apple Privacy Manifest documentation.

## [0.1.0-preview.6] - 2024-04-10

### Added
- Added Apple Privacy Manifest file to `/Plugins` directory.
- (CI) Code format checks.

### Changed
- Code formatting now follows Unity coding standards.
- Updated and improved CI scripts

### Removed
- Obsolete CI script code

## [0.1.0-preview.5] - 2022-03-03

### Fixed
- Installation instructions

## [0.1.0-preview.4] - 2022-01-20

### Fixed
- Crash on invalid bit length. Removes compiler warning about throwing exception in C# job.

## [0.1.0-preview.3] - 2021-12-22

### Fixed
- Exponential filter decoding

## [0.1.0-preview.2] - 2021-10-22

### Added
- More documentation
- Performance test for quaternion filtering
- Link to original project in third party notices

### Changed
- Unity 2019.4 is the minimum required version now
- Converted editor tests to runtime tests

### Fixed
- Quaternion filtering
- Test assembly setup
- CI related cleanups

## [0.1.0-preview] - 2021-09-20

This is the initial release of Unity package *meshoptimizer decompression for Unity*

Use the *meshoptimizer decompression for Unity* package to decode meshoptimizer compressed index/vertex buffers efficiently in Burst-compiled C# Jobs off the main thread.

[Mathematics]: https://docs.unity3d.com/Packages/com.unity.mathematics@latest/
[Burst]: https://docs.unity3d.com/Packages/com.unity.burst@latest/

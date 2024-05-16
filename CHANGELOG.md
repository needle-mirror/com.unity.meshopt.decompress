# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0-preview.7] - 2024-05-16

### Added
- Added Apple Privacy Manifest documentation.

## [0.1.0-preview.6] - 2024-04-10

### Added
- Added Apple Privacy Manifest file to `/Plugins` directory.
- (CI) Code format checks.

## Changed
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

### This is the first release of *Unity Package \<meshoptimizer decompression\>*

Use the *meshoptimizer decompression for Unity* package to decode meshoptimizer compressed index/vertex buffers efficiently in Burst-compiled C# Jobs off the main thread.

using System;

namespace Meshoptimizer
{
    /// <summary>
    /// Vertex attribute filter to be applied
    /// </summary>
    public enum Filter
    {
        /// <summary>
        /// Don't use this value as parameter directly!
        /// It's for deserialization purpose only.
        /// </summary>
        Undefined,
        /// <summary>
        /// No filter should be applied
        /// </summary>
        None,
        /// <summary>
        /// Apply octahedral filter, usually for normals
        /// </summary>
        Octahedral,
        /// <summary>
        /// Apply quaternion filter, usually for rotations
        /// </summary>
        Quaternion,
        /// <summary>
        /// Apply exponential filter, usually for positional data
        /// </summary>
        Exponential
    }
}

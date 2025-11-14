using System;

namespace Meshoptimizer
{
    /// <summary>
    /// Mode defines the type of buffer to decode
    /// </summary>
    public enum Mode
    {
        /// <summary>
        /// Don't use this value as parameter directly!
        /// It's for deserialization purpose only.
        /// </summary>
        Undefined,
        /// <summary>
        /// Vertex attributes
        /// </summary>
        Attributes,
        /// <summary>
        /// Triangle indices buffer
        /// </summary>
        Triangles,
        /// <summary>
        /// Index sequence
        /// </summary>
        Indices,
    }
}

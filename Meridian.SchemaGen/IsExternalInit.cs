// Polyfill for netstandard2.0 to support init accessors (C# 10+)
namespace System.Runtime.CompilerServices
{
#if !NET5_0_OR_GREATER
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal sealed class IsExternalInit
    {
    }
#endif
}

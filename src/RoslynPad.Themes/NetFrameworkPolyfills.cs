// iCore fork: polyfills for BCL APIs that are missing on netstandard2.0 / .NET Framework 4.8,
// so upstream code can stay unmodified.
#if !NET
namespace RoslynPad.Themes;

internal static class NetFrameworkPolyfills
{
    public static bool TryPop<T>(this Stack<T> stack, out T result)
    {
        if (stack.Count > 0)
        {
            result = stack.Pop();
            return true;
        }

        result = default!;
        return false;
    }
}
#endif

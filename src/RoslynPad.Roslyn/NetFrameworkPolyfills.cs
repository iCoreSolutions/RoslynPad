// iCore fork: polyfills for BCL APIs that are missing on netstandard2.0 / .NET Framework 4.8,
// so upstream code can stay unmodified.
#if !NET
using System.Runtime.CompilerServices;

namespace RoslynPad.Roslyn
{
    internal static class NetFrameworkPolyfills
    {
        extension(ArgumentNullException)
        {
            public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
            {
                if (argument is null)
                {
                    throw new ArgumentNullException(paramName);
                }
            }
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source) => new(source);
    }
}
#endif

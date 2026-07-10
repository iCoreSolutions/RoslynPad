// iCore fork: polyfills for BCL APIs that are missing on .NET Framework 4.8, so upstream code
// can stay unmodified.
#if !NET
using System.Runtime.CompilerServices;

namespace RoslynPad.Editor;

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

    extension(ArgumentOutOfRangeException)
    {
        public static void ThrowIfNotEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IEquatable<T>?
        {
            if (!EqualityComparer<T>.Default.Equals(value, other))
            {
                throw new ArgumentOutOfRangeException(paramName, value, $"Value must be equal to '{other}'.");
            }
        }
    }

    public static Task CancelAsync(this CancellationTokenSource cancellationTokenSource)
    {
        cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;
}
#endif

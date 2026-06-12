using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynPad.Roslyn.Diagnostics
{
    /// <summary>
    /// Process-wide cache of the latest analyzer-pass diagnostics per document, populated by
    /// <see cref="DiagnosticsEngine"/>. <c>CodeFixService</c> reads it so analyzer-driven quick fixes
    /// (e.g. remove unnecessary usings) are available without re-running every analyzer over the
    /// compilation on each lightbulb request. Compiler (syntax/semantic) diagnostics for fixes are
    /// recomputed fresh from the current SemanticModel, so only the expensive analyzer set is cached.
    /// </summary>
    internal static class DiagnosticsCache
    {
        private static readonly ConcurrentDictionary<DocumentId, ImmutableArray<Diagnostic>> AnalyzerDiagnostics =
            new ConcurrentDictionary<DocumentId, ImmutableArray<Diagnostic>>();

        public static void UpdateAnalyzers(DocumentId documentId, ImmutableArray<Diagnostic> diagnostics) =>
            AnalyzerDiagnostics[documentId] = diagnostics.IsDefault ? ImmutableArray<Diagnostic>.Empty : diagnostics;

        public static ImmutableArray<Diagnostic> GetAnalyzerDiagnostics(DocumentId documentId) =>
            AnalyzerDiagnostics.TryGetValue(documentId, out var diagnostics)
                ? diagnostics
                : ImmutableArray<Diagnostic>.Empty;

        public static void Clear(DocumentId documentId) => AnalyzerDiagnostics.TryRemove(documentId, out _);
    }
}

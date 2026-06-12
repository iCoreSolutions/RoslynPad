using System;

namespace RoslynPad.Roslyn
{
    /// <summary>
    /// Which push-diagnostics passes a workspace should run. Plain flags now that the engine no
    /// longer delegates to Roslyn's internal DiagnosticProvider. Existing callers that pass
    /// <c>Semantic | Syntax</c> get full parity (the analyzer pass rides along with Semantic).
    /// </summary>
    [Flags]
    public enum DiagnosticOptions
    {
        None = 0,

        /// <summary>
        /// Include syntax errors.
        /// </summary>
        Syntax = 1,

        /// <summary>
        /// Include semantic errors (and, by default, analyzer suggestions).
        /// </summary>
        Semantic = 2,

        /// <summary>
        /// Explicitly include analyzer diagnostics (also implied by <see cref="Semantic"/>).
        /// </summary>
        Analyzers = 4,
    }
}

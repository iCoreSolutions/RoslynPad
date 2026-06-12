using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPad.Roslyn.CodeFixes
{
    /// <summary>
    /// The fixes contributed by a single <c>CodeFixProvider</c> for a span. Plain data holder on
    /// public types (the 1.2.0-icore fork wrapped Roslyn's internal CodeFixCollection).
    /// </summary>
    public sealed class CodeFixCollection
    {
        public object Provider { get; }

        public TextSpan TextSpan { get; }

        public ImmutableArray<CodeFix> Fixes { get; }

        internal CodeFixCollection(object provider, TextSpan textSpan, ImmutableArray<CodeFix> fixes)
        {
            Provider = provider;
            TextSpan = textSpan;
            Fixes = fixes;
        }
    }
}

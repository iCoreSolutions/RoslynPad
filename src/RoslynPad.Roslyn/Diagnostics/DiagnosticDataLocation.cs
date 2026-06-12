using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPad.Roslyn.Diagnostics
{
    /// <summary>
    /// Location data for a <see cref="DiagnosticData"/>, computed from a public Roslyn
    /// <see cref="Location"/>. Preserves the member surface of the 1.2.0-icore fork
    /// (which wrapped Roslyn's internal DiagnosticDataLocation) so iCIS consumers compile unchanged.
    /// </summary>
    public sealed class DiagnosticDataLocation
    {
        public DocumentId? DocumentId { get; }

        public TextSpan? SourceSpan { get; }

        public string? MappedFilePath { get; }
        public int MappedStartLine { get; }
        public int MappedStartColumn { get; }
        public int MappedEndLine { get; }
        public int MappedEndColumn { get; }
        public string? OriginalFilePath { get; }
        public int OriginalStartLine { get; }
        public int OriginalStartColumn { get; }
        public int OriginalEndLine { get; }
        public int OriginalEndColumn { get; }

        internal DiagnosticDataLocation(Location location, DocumentId? documentId)
        {
            DocumentId = documentId;
            SourceSpan = location.IsInSource ? location.SourceSpan : (TextSpan?)null;

            // Original (unmapped) line span - what iCIS CodeDiagnosticService binds to.
            var lineSpan = location.GetLineSpan();
            OriginalFilePath = lineSpan.Path;
            OriginalStartLine = lineSpan.StartLinePosition.Line;
            OriginalStartColumn = lineSpan.StartLinePosition.Character;
            OriginalEndLine = lineSpan.EndLinePosition.Line;
            OriginalEndColumn = lineSpan.EndLinePosition.Character;

            // Mapped span honours #line directives.
            var mapped = location.GetMappedLineSpan();
            MappedFilePath = mapped.HasMappedPath ? mapped.Path : null;
            MappedStartLine = mapped.StartLinePosition.Line;
            MappedStartColumn = mapped.StartLinePosition.Character;
            MappedEndLine = mapped.EndLinePosition.Line;
            MappedEndColumn = mapped.EndLinePosition.Character;
        }
    }
}

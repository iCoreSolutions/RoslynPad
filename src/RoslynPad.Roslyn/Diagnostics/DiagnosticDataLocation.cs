using Microsoft.CodeAnalysis;

namespace RoslynPad.Roslyn.Diagnostics;

/// <summary>
/// Location data for a <see cref="DiagnosticData"/>. Preserves the member surface of the
/// 4.4.0-icore fork so iCIS consumers (CodeDiagnosticService error list) compile unchanged.
/// At Roslyn 5.6 it wraps the internal DiagnosticDataLocation's file spans.
/// </summary>
public sealed class DiagnosticDataLocation
{
    public DocumentId? DocumentId { get; }

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

    internal DiagnosticDataLocation(Microsoft.CodeAnalysis.Diagnostics.DiagnosticDataLocation inner, DocumentId? documentId)
    {
        DocumentId = documentId;

        // Original (unmapped) line span - what iCIS CodeDiagnosticService binds to.
        var unmapped = inner.UnmappedFileSpan;
        OriginalFilePath = unmapped.Path;
        OriginalStartLine = unmapped.StartLinePosition.Line;
        OriginalStartColumn = unmapped.StartLinePosition.Character;
        OriginalEndLine = unmapped.EndLinePosition.Line;
        OriginalEndColumn = unmapped.EndLinePosition.Character;

        // Mapped span honours #line directives.
        var mapped = inner.MappedFileSpan;
        MappedFilePath = mapped.HasMappedPath ? mapped.Path : null;
        MappedStartLine = mapped.StartLinePosition.Line;
        MappedStartColumn = mapped.StartLinePosition.Character;
        MappedEndLine = mapped.EndLinePosition.Line;
        MappedEndColumn = mapped.EndLinePosition.Character;
    }
}

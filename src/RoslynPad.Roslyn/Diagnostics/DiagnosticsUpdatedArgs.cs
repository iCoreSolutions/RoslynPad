using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynPad.Roslyn.Diagnostics
{
    public class DiagnosticsUpdatedArgs : UpdatedEventArgs
    {
        public DiagnosticsUpdatedKind Kind { get; }
        public Solution? Solution { get; }
        public ImmutableArray<DiagnosticData> Diagnostics { get; }

        public DiagnosticsUpdatedArgs(object id, Workspace workspace, Solution? solution, ProjectId? projectId,
            DocumentId? documentId, DiagnosticsUpdatedKind kind, ImmutableArray<DiagnosticData> diagnostics)
            : base(id, workspace, projectId, documentId)
        {
            Solution = solution;
            Kind = kind;
            Diagnostics = diagnostics;
        }

        public DiagnosticsUpdatedArgs WithDiagnostics(ImmutableArray<DiagnosticData> diagnostics) =>
            new DiagnosticsUpdatedArgs(Id, Workspace, Solution, ProjectId, DocumentId, Kind, diagnostics);
    }
}

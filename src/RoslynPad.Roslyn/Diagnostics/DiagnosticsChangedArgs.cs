using Microsoft.CodeAnalysis;

namespace RoslynPad.Roslyn.Diagnostics;

// iCore fork: IReadOnlyCollection instead of IReadOnlySet (net5+) — not available on net48.
public record DiagnosticsChangedArgs(DocumentId DocumentId, IReadOnlyCollection<DiagnosticData> AddedDiagnostics, IReadOnlyCollection<DiagnosticData> RemovedDiagnostics);

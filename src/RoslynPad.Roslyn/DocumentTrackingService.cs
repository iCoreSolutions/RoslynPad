using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace RoslynPad.Roslyn;

[ExportWorkspaceServiceFactory(typeof(IDocumentTrackingService), ServiceLayer.Host)]
internal sealed class DocumentTrackingServiceFactory : IWorkspaceServiceFactory
{
    private class DocumentTrackingService(Workspace workspace) : IDocumentTrackingService
    {
        // iCore fork: hosts can plug in Workspace subclasses that are not RoslynWorkspace (e.g.
        // iCIS's WebApiCodeWorkspace). Roslyn 5.6 resolves this service on ordinary semantic-model
        // paths (Solution.OnSemanticModelObtained), so a hard cast crashes those hosts; fall back
        // to "no tracking" instead.
        private readonly RoslynWorkspace? _workspace = workspace as RoslynWorkspace;

        public bool SupportsDocumentTracking => _workspace != null;

        public DocumentId GetActiveDocument() => _workspace?.OpenDocumentId ?? throw new InvalidOperationException("No active document");

        public DocumentId? TryGetActiveDocument() => _workspace?.OpenDocumentId;

        public ImmutableArray<DocumentId> GetVisibleDocuments() => _workspace?.OpenDocumentId != null ? [_workspace.OpenDocumentId] : [];

        public event EventHandler<DocumentId?>? ActiveDocumentChanged = delegate { };

        public event EventHandler<EventArgs>? NonRoslynBufferTextChanged = delegate { };
    }

    public IWorkspaceService CreateService(HostWorkspaceServices workspaceServices) =>
        new DocumentTrackingService(workspaceServices.Workspace);
}

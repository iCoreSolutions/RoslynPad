using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Threading;

namespace RoslynPad.Roslyn.Diagnostics;

public class DiagnosticsUpdater : IDiagnosticsUpdater, IDisposable
{
    private readonly Workspace _workspace;
    private readonly IDiagnosticAnalyzerService _diagnosticAnalyzerService;
    private readonly object _lock = new();
    private readonly AsyncBatchingWorkQueue<DocumentId> _workQueue;
    private readonly CancellationTokenSource _cts;

    // iCore fork: tracked PER DOCUMENT. Upstream kept a single workspace-global set, which is
    // only correct when a workspace hosts exactly one document (the RoslynPad app model). In a
    // multi-document workspace (iCIS WebApiCodeWorkspace) a global set leaks document A's
    // diagnostics into document B's removal delta, corrupting every consumer's folded state.
    private readonly Dictionary<DocumentId, HashSet<DiagnosticData>> _currentDiagnostics;

    public ImmutableHashSet<string> DisabledDiagnostics { get; set; } = [];

    [ExportWorkspaceServiceFactory(typeof(IDiagnosticsUpdater))]
    internal class Factory : IWorkspaceServiceFactory
    {
        public IWorkspaceService CreateService(HostWorkspaceServices workspaceServices)
        {
            return new DiagnosticsUpdater(workspaceServices.Workspace, workspaceServices.GetRequiredService<IDiagnosticAnalyzerService>());
        }
    }

    [ImportingConstructor]
    public DiagnosticsUpdater(Workspace workspace, IDiagnosticAnalyzerService diagnosticAnalyzerService)
    {
        workspace.RegisterDocumentOpenedHandler(OnDocumentOpened);
        workspace.RegisterDocumentActiveContextChangedHandler(OnDocumentActiveContextChanged);
        workspace.RegisterWorkspaceChangedHandler(OnWorkspaceChanged);
        foreach (var document in workspace.CurrentSolution.Projects.SelectMany(p => p.Documents))
        {
            ConnectDocument(document);
        }

        _workspace = workspace;
        _diagnosticAnalyzerService = diagnosticAnalyzerService;
        _currentDiagnostics = new Dictionary<DocumentId, HashSet<DiagnosticData>>();
        _cts = new CancellationTokenSource();

        _workQueue = new AsyncBatchingWorkQueue<DocumentId>(DelayTimeSpan.Short, ProcessWorkQueueAsync, new AsynchronousOperationListener(), _cts.Token);
    }

    private async ValueTask ProcessWorkQueueAsync(ImmutableSegmentedList<DocumentId> documentIds, CancellationToken cancellationToken)
    {
        foreach (var documentId in documentIds)
        {
            if (await _workspace.CurrentSolution.GetDocumentAsync(documentId, cancellationToken: cancellationToken).ConfigureAwait(false) is { } document)
            {
                await UpdateDiagnosticsAsync(document, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // iCore fork: the document is gone (closed/removed) - retire its diagnostics so
                // consumers drop stale squiggles/error entries.
                RemoveDocumentDiagnostics(documentId);
            }
        }
    }

    private void RemoveDocumentDiagnostics(DocumentId documentId)
    {
        lock (_lock)
        {
            if (_currentDiagnostics.TryGetValue(documentId, out var previous) && previous.Count > 0)
            {
                _currentDiagnostics.Remove(documentId);
                DiagnosticsChanged?.Invoke(new DiagnosticsChangedArgs(documentId, [], previous));
            }
            else
            {
                _currentDiagnostics.Remove(documentId);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
    }

    private void OnDocumentOpened(DocumentEventArgs args) => ConnectDocument(args.Document);

    private void OnDocumentActiveContextChanged(DocumentActiveContextChangedEventArgs e) => _workQueue.AddWork(e.NewActiveContextDocumentId);

    private void OnWorkspaceChanged(WorkspaceChangeEventArgs e)
    {
        if (e.DocumentId is { } documentId)
        {
            _workQueue.AddWork(documentId);
        }
    }

    private void ConnectDocument(Document document)
    {
        if (document.TryGetText(out var text))
        {
            text.Container.TextChanged += (o, e) => _workQueue.AddWork(document.Id);
        }

        _workQueue.AddWork(document.Id);
    }

    private async Task UpdateDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        var diagnostics = await GetDiagnostics(document, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            // iCore fork: compare against THIS document's previous set only (see field comment).
            var previousDiagnostics = _currentDiagnostics.TryGetValue(document.Id, out var previous) ? previous : new HashSet<DiagnosticData>();

            var addedDiagnostics = diagnostics.Where(d => !previousDiagnostics.Contains(d) && !DisabledDiagnostics.Contains(d.Id)).ToHashSet();
            previousDiagnostics.ExceptWith(diagnostics);
            var removedDiagnostics = previousDiagnostics;

            var newDiagnostics = new HashSet<DiagnosticData>();
            foreach (var diag in diagnostics)
            {
                newDiagnostics.Add(diag);
            }

            _currentDiagnostics[document.Id] = newDiagnostics;

            cancellationToken.ThrowIfCancellationRequested();

            if (addedDiagnostics.Count > 0 || removedDiagnostics.Count > 0)
            {
                DiagnosticsChanged?.Invoke(new DiagnosticsChangedArgs(document.Id, addedDiagnostics, removedDiagnostics));
            }
        }
    }

    private async Task<ImmutableArray<DiagnosticData>> GetDiagnostics(Document document, CancellationToken cancellationToken)
    {
        try
        {
            return await _diagnosticAnalyzerService.GetDiagnosticsForSpanAsync(document, range: null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public event Action<DiagnosticsChangedArgs>? DiagnosticsChanged;
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynPad.Roslyn.Diagnostics
{
    /// <summary>
    /// Per-<see cref="Workspace"/> push-diagnostics engine built on public Roslyn APIs.
    /// Replaces Roslyn's internal solution-crawler based DiagnosticProvider, whose service
    /// implementations moved to the netcoreapp-only LanguageServer assembly at 4.4 and are
    /// unloadable on net48 (the original WebAPIEditor startup crash).
    ///
    /// On each document edit it runs staged passes - syntax, then semantic, then (after a longer
    /// debounce, cancellable by the next edit) analyzers - and publishes one
    /// <see cref="DiagnosticsUpdatedArgs"/> per (document, pass) onto <see cref="DiagnosticsUpdatedBus"/>.
    /// The args carry a stable per-(document, pass) <see cref="UpdateArgsId"/> so consumers can
    /// replace previous markers / error-list entries for that pass (replace-by-id semantics).
    /// </summary>
    internal sealed class DiagnosticsEngine : IDisposable
    {
        // Wait this long after an edit before the syntax/semantic passes, so a fast typist does not
        // trigger a re-parse on every keystroke. Tune for responsiveness vs. churn.
        private const int EditDebounceMs = 400;

        // Additional delay before the (expensive, full-analyzer-set) analyzer pass, so it only runs
        // once typing settles (~1s total); a new edit cancels it. Keep larger than EditDebounceMs.
        private const int AnalyzerExtraDebounceMs = 600;

        private readonly Workspace _workspace;
        private readonly bool _runSyntax;
        private readonly bool _runSemantic;
        private readonly bool _runAnalyzers;

        private readonly object _gate = new object();
        private readonly Dictionary<DocumentId, CancellationTokenSource> _perDocument =
            new Dictionary<DocumentId, CancellationTokenSource>();
        private readonly HashSet<DocumentId> _openDocuments = new HashSet<DocumentId>();
        private bool _disposed;

        public DiagnosticsEngine(Workspace workspace, DiagnosticOptions options)
        {
            _workspace = workspace;
            _runSyntax = (options & DiagnosticOptions.Syntax) != 0;
            _runSemantic = (options & DiagnosticOptions.Semantic) != 0;
            // Full parity: analyzer suggestions ride along with semantic analysis (and the explicit flag).
            _runAnalyzers = (options & (DiagnosticOptions.Semantic | DiagnosticOptions.Analyzers)) != 0;

            _workspace.WorkspaceChanged += OnWorkspaceChanged;
            _workspace.DocumentOpened += OnDocumentOpened;
            _workspace.DocumentClosed += OnDocumentClosed;
        }

        public void Dispose()
        {
            _workspace.WorkspaceChanged -= OnWorkspaceChanged;
            _workspace.DocumentOpened -= OnDocumentOpened;
            _workspace.DocumentClosed -= OnDocumentClosed;

            // Set _disposed and drain under the same lock that Schedule takes, so a workspace event
            // racing teardown either observes _disposed (and bails) or has its CTS cancelled here.
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                foreach (var cts in _perDocument.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                _perDocument.Clear();

                // Evict cached analyzer diagnostics for documents that may never see a close event
                // (e.g. the workspace is disposed with documents still open) to avoid leaking them
                // (and the Compilation/SemanticModel graphs they retain) for the process lifetime.
                foreach (var documentId in _openDocuments)
                {
                    DiagnosticsCache.Clear(documentId);
                }

                _openDocuments.Clear();
            }
        }

        private void OnDocumentOpened(object? sender, DocumentEventArgs e)
        {
            lock (_gate)
            {
                _openDocuments.Add(e.Document.Id);
            }

            Schedule(e.Document.Id);
        }

        private void OnDocumentClosed(object? sender, DocumentEventArgs e)
        {
            lock (_gate)
            {
                _openDocuments.Remove(e.Document.Id);
            }

            CancelPending(e.Document.Id);
            RaiseRemoved(e.Document.Id);
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceChangeEventArgs e)
        {
            switch (e.Kind)
            {
                case WorkspaceChangeKind.DocumentChanged:
                case WorkspaceChangeKind.DocumentAdded:
                case WorkspaceChangeKind.DocumentReloaded:
                    if (e.DocumentId != null)
                    {
                        Schedule(e.DocumentId);
                    }

                    break;

                case WorkspaceChangeKind.DocumentRemoved:
                    if (e.DocumentId != null)
                    {
                        lock (_gate)
                        {
                            _openDocuments.Remove(e.DocumentId);
                        }

                        CancelPending(e.DocumentId);
                        RaiseRemoved(e.DocumentId);
                    }

                    break;

                case WorkspaceChangeKind.ProjectChanged:
                case WorkspaceChangeKind.ProjectReloaded:
                    RescheduleOpenDocuments(e.ProjectId);
                    break;

                case WorkspaceChangeKind.SolutionChanged:
                case WorkspaceChangeKind.SolutionReloaded:
                case WorkspaceChangeKind.SolutionAdded:
                    RescheduleOpenDocuments(null);
                    break;
            }
        }

        private void RescheduleOpenDocuments(ProjectId? projectId)
        {
            DocumentId[] documents;
            lock (_gate)
            {
                documents = projectId == null
                    ? _openDocuments.ToArray()
                    : _openDocuments.Where(d => d.ProjectId == projectId).ToArray();
            }

            foreach (var documentId in documents)
            {
                Schedule(documentId);
            }
        }

        private void Schedule(DocumentId documentId)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_perDocument.TryGetValue(documentId, out var existing))
                {
                    existing.Cancel();
                    existing.Dispose();
                }

                var cts = new CancellationTokenSource();
                _perDocument[documentId] = cts;

                // Capture the token and queue the work while still holding the lock, so Dispose
                // (which disposes _perDocument's CTSs under the same lock) cannot dispose this CTS
                // before we read its token. CancellationToken.None is the scheduler token;
                // cooperative cancellation flows through the captured body token.
                var token = cts.Token;
                _ = Task.Run(() => AnalyzeAsync(documentId, token), CancellationToken.None);
            }
        }

        private void CancelPending(DocumentId documentId)
        {
            lock (_gate)
            {
                if (_perDocument.TryGetValue(documentId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _perDocument.Remove(documentId);
                }
            }
        }

        private async Task AnalyzeAsync(DocumentId documentId, CancellationToken ct)
        {
            try
            {
                await Task.Delay(EditDebounceMs, ct).ConfigureAwait(false);

                var solution = _workspace.CurrentSolution;
                var document = solution.GetDocument(documentId);
                if (document == null)
                {
                    return;
                }

                var projectId = document.Project.Id;

                // Pass 1 - syntax (parse) diagnostics.
                if (_runSyntax)
                {
                    var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                    if (tree != null)
                    {
                        RaiseCreated(documentId, projectId, solution, PassKind.Syntax, tree.GetDiagnostics(ct));
                    }
                }

                ct.ThrowIfCancellationRequested();

                SemanticModel? model = null;
                if (_runSemantic || _runAnalyzers)
                {
                    model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                }

                // Pass 2 - semantic diagnostics only (declaration + method body). Deliberately excludes
                // syntax diagnostics, which SemanticModel.GetDiagnostics would also return, so that a
                // syntax error is not squiggled / listed twice under two different pass ids.
                if (_runSemantic && model != null)
                {
                    var semantic = model.GetDeclarationDiagnostics(null, ct)
                        .AddRange(model.GetMethodBodyDiagnostics(null, ct));
                    RaiseCreated(documentId, projectId, solution, PassKind.Semantic, semantic);
                }

                // Pass 3 - analyzers (full parity). Longer debounce; a new edit cancels it.
                if (_runAnalyzers && model != null)
                {
                    await Task.Delay(AnalyzerExtraDebounceMs, ct).ConfigureAwait(false);
                    var analyzerDiagnostics = await ComputeAnalyzerDiagnosticsAsync(document, model, ct)
                        .ConfigureAwait(false);
                    // Cache for CodeFixService (analyzer-driven quick fixes) and always raise - an empty
                    // result must still clear prior analyzer markers/error-list entries (replace-by-id).
                    DiagnosticsCache.UpdateAnalyzers(documentId, analyzerDiagnostics);
                    RaiseCreated(documentId, projectId, solution, PassKind.Analyzers, analyzerDiagnostics);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer edit (or disposed) - drop silently.
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // Diagnostics are best-effort editor adornments; never bring down the host on a
                // transient analysis failure (e.g. a project mid-reload). Trace so the failure is
                // observable when debugging instead of silently producing no diagnostics.
                System.Diagnostics.Trace.WriteLine(
                    $"RoslynPad: diagnostics analysis failed for document {documentId}: {ex}");
            }
        }

        private static async Task<ImmutableArray<Diagnostic>> ComputeAnalyzerDiagnosticsAsync(
            Document document, SemanticModel model, CancellationToken ct)
        {
            var project = document.Project;
            var analyzers = project.AnalyzerReferences
                .SelectMany(r => r.GetAnalyzers(LanguageNames.CSharp))
                // Exclude the compiler's own DiagnosticAnalyzer: its CSxxxx diagnostics are already
                // produced by the syntax/semantic passes, and re-reporting them here (under the
                // Analyzers pass id) would duplicate every compiler error/warning in the editor and
                // error list. Keep only genuine IDE/style analyzers.
                .Where(a => !IsCompilerDiagnosticAnalyzer(a))
                .ToImmutableArray();
            if (analyzers.IsDefaultOrEmpty)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            var options = new CompilationWithAnalyzersOptions(
                project.AnalyzerOptions,
                onAnalyzerException: static (ex, analyzer, _) => System.Diagnostics.Trace.WriteLine(
                    $"RoslynPad: analyzer '{analyzer}' threw during analysis: {ex}"),
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false);
            var withAnalyzers = compilation.WithAnalyzers(analyzers, options);

            // Scope analysis to this document's tree/model so typing in one file does not re-run
            // every analyzer over the whole compilation.
            var syntaxDiagnostics = await withAnalyzers
                .GetAnalyzerSyntaxDiagnosticsAsync(model.SyntaxTree, ct).ConfigureAwait(false);
            var semanticDiagnostics = await withAnalyzers
                .GetAnalyzerSemanticDiagnosticsAsync(model, null, ct).ConfigureAwait(false);

            return syntaxDiagnostics.AddRange(semanticDiagnostics);
        }

        private static bool IsCompilerDiagnosticAnalyzer(DiagnosticAnalyzer analyzer)
        {
            // The compiler's own DiagnosticAnalyzer (e.g. CSharpCompilerDiagnosticAnalyzer, pulled in
            // because the compiler assemblies are registered as analyzer references) re-surfaces the
            // CSxxxx compiler diagnostics through the analyzer pipeline. Those are already produced by
            // the syntax/semantic passes; including it here would double every compiler error/warning.
            var typeName = analyzer.GetType().FullName ?? analyzer.GetType().Name;
            return typeName.EndsWith("CompilerDiagnosticAnalyzer", StringComparison.Ordinal);
        }

        private void RaiseCreated(DocumentId documentId, ProjectId projectId, Solution solution, PassKind kind,
            IEnumerable<Diagnostic> diagnostics)
        {
            var data = diagnostics
                .Select(d => new DiagnosticData(d, documentId, projectId))
                .ToImmutableArray();
            var args = new DiagnosticsUpdatedArgs(new UpdateArgsId(documentId, kind), _workspace, solution,
                projectId, documentId, DiagnosticsUpdatedKind.DiagnosticsCreated, data);
            DiagnosticsUpdatedBus.Raise(args);
        }

        private void RaiseRemoved(DocumentId documentId)
        {
            DiagnosticsCache.Clear(documentId);

            foreach (PassKind kind in new[] { PassKind.Syntax, PassKind.Semantic, PassKind.Analyzers })
            {
                var args = new DiagnosticsUpdatedArgs(new UpdateArgsId(documentId, kind), _workspace, null,
                    documentId.ProjectId, documentId, DiagnosticsUpdatedKind.DiagnosticsRemoved,
                    ImmutableArray<DiagnosticData>.Empty);
                DiagnosticsUpdatedBus.Raise(args);
            }
        }
    }

    internal enum PassKind
    {
        Syntax = 0,
        Semantic = 1,
        Analyzers = 2,
    }

    /// <summary>
    /// Stable, value-equal identity for a (document, pass) diagnostics update. Mirrors Roslyn's old
    /// internal DefaultUpdateArgsId (keyed on document + analysis kind) so that successive updates for
    /// the same document and pass replace each other in editor markers and the error list, while
    /// syntax / semantic / analyzer results coexist.
    /// </summary>
    internal sealed class UpdateArgsId : IEquatable<UpdateArgsId>
    {
        private readonly DocumentId _documentId;
        private readonly PassKind _kind;

        public UpdateArgsId(DocumentId documentId, PassKind kind)
        {
            _documentId = documentId;
            _kind = kind;
        }

        public bool Equals(UpdateArgsId? other) =>
            other != null && _kind == other._kind && _documentId.Equals(other._documentId);

        public override bool Equals(object? obj) => Equals(obj as UpdateArgsId);

        public override int GetHashCode() => unchecked((_documentId.GetHashCode() * 397) ^ (int)_kind);

        public override string ToString() => $"{_kind}:{_documentId.Id}";
    }
}

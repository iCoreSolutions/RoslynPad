using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPad.Roslyn.Diagnostics
{
    /// <summary>
    /// Public projection of a Roslyn <see cref="Diagnostic"/>. Re-authored on public APIs for
    /// Roslyn 4.4 (the 1.2.0-icore fork wrapped Roslyn's internal DiagnosticData, which moved to the
    /// netcoreapp-only LanguageServer assembly). Member surface is preserved so iCIS consumers
    /// (CodeDiagnosticService, RoslynCodeEditor, WebAPICodeEditor) compile and behave unchanged.
    /// </summary>
    public sealed class DiagnosticData
    {
        private readonly Diagnostic _diagnostic;
        private readonly Lazy<IReadOnlyList<string>> _customTags;

        public string Id => _diagnostic.Id;
        public string Category => _diagnostic.Descriptor.Category;
        public string? Message => _diagnostic.GetMessage();
        public string? Description => _diagnostic.Descriptor.Description.ToString();
        public string? Title => _diagnostic.Descriptor.Title.ToString();
        public string? HelpLink => _diagnostic.Descriptor.HelpLinkUri;
        public DiagnosticSeverity Severity => _diagnostic.Severity;
        public DiagnosticSeverity DefaultSeverity => _diagnostic.DefaultSeverity;
        public bool IsEnabledByDefault => _diagnostic.Descriptor.IsEnabledByDefault;
        public int WarningLevel => _diagnostic.WarningLevel;
        public IReadOnlyList<string> CustomTags => _customTags.Value;
        public ImmutableDictionary<string, string?> Properties => _diagnostic.Properties;
        public bool IsSuppressed => _diagnostic.IsSuppressed;
        public ProjectId? ProjectId { get; }
        public DocumentId? DocumentId { get; }

        public bool HasTextSpan => _diagnostic.Location.IsInSource;

        public DiagnosticDataLocation? DataLocation { get; }
        public IReadOnlyCollection<DiagnosticDataLocation> AdditionalLocations { get; }

        /// <summary>Span in the document tree, or null when the diagnostic has no in-source location.</summary>
        public TextSpan? GetTextSpan() =>
            _diagnostic.Location.IsInSource ? _diagnostic.Location.SourceSpan : (TextSpan?)null;

        /// <summary>Span clamped to <paramref name="sourceText"/> bounds (the editor may lag the model).</summary>
        public TextSpan? GetTextSpan(SourceText sourceText)
        {
            if (!_diagnostic.Location.IsInSource)
            {
                return null;
            }

            var span = _diagnostic.Location.SourceSpan;
            var start = Math.Max(0, Math.Min(span.Start, sourceText.Length));
            var end = Math.Max(0, Math.Min(span.End, sourceText.Length));
            if (start > end)
            {
                (start, end) = (end, start);
            }

            return TextSpan.FromBounds(start, end);
        }

        internal DiagnosticData(Diagnostic diagnostic, DocumentId? documentId, ProjectId? projectId)
        {
            _diagnostic = diagnostic;
            DocumentId = documentId;
            ProjectId = projectId;

            var location = diagnostic.Location;
            DataLocation = location.IsInSource ? new DiagnosticDataLocation(location, documentId) : null;
            AdditionalLocations = diagnostic.AdditionalLocations
                .Where(l => l.IsInSource)
                .Select(l => new DiagnosticDataLocation(l, documentId))
                .ToImmutableArray();

            _customTags = new Lazy<IReadOnlyList<string>>(() => diagnostic.Descriptor.CustomTags.ToImmutableArray());
        }
    }
}

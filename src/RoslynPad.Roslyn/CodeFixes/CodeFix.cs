using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace RoslynPad.Roslyn.CodeFixes
{
    /// <summary>
    /// A single code fix (a <see cref="CodeAction"/> plus the diagnostics it addresses).
    /// Re-authored as a plain data holder on public Roslyn types for 4.4 (the 1.2.0-icore fork
    /// wrapped Roslyn's internal CodeFix). Member surface is unchanged so iCIS App.xaml templates and
    /// WebApiContextActionProvider keep binding to Action / Fixes / Provider.
    /// </summary>
    public sealed class CodeFix
    {
        public Project Project { get; }

        public CodeAction Action { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public Diagnostic PrimaryDiagnostic { get; }

        internal CodeFix(Project project, CodeAction action, ImmutableArray<Diagnostic> diagnostics, Diagnostic primaryDiagnostic)
        {
            Project = project;
            Action = action;
            Diagnostics = diagnostics;
            PrimaryDiagnostic = primaryDiagnostic;
        }
    }
}

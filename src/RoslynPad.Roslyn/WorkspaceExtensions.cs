using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using RoslynPad.Roslyn.Diagnostics;

namespace RoslynPad.Roslyn
{
    /// <summary>
    /// Turns push-diagnostics analysis on/off for a workspace. At Roslyn 4.4 the internal
    /// DiagnosticProvider/solution-crawler this used to delegate to is unavailable on net48
    /// (its service implementations are in the netcoreapp-only LanguageServer assembly), so a
    /// <see cref="DiagnosticsEngine"/> is attached per workspace instead.
    /// </summary>
    public static class WorkspaceExtensions
    {
        private static readonly ConditionalWeakTable<Workspace, DiagnosticsEngine> Engines =
            new ConditionalWeakTable<Workspace, DiagnosticsEngine>();

        public static void EnableDiagnostics(this Workspace workspace, DiagnosticOptions options)
        {
            // Replace any existing engine so repeated EnableDiagnostics calls don't double-analyze.
            DisableDiagnostics(workspace);
            Engines.Add(workspace, new DiagnosticsEngine(workspace, options));
        }

        public static void DisableDiagnostics(this Workspace workspace)
        {
            if (Engines.TryGetValue(workspace, out var engine))
            {
                engine.Dispose();
                Engines.Remove(workspace);
            }
        }
    }
}

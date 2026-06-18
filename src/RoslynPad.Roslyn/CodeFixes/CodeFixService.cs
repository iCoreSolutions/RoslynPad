using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Roslyn.Diagnostics;

namespace RoslynPad.Roslyn.CodeFixes
{
    /// <summary>
    /// Quick-fix service re-authored on public Roslyn APIs for 4.4. The internal ICodeFixService it
    /// used to wrap moved to the netcoreapp-only LanguageServer assembly (unloadable on net48).
    ///
    /// CodeFixProviders are discovered once by reflecting the public [ExportCodeFixProvider] attribute
    /// over the composed Features assemblies and instantiating those with a usable parameterless ctor
    /// (providers needing MEF-injected dependencies are skipped). Fixes are produced on demand from the
    /// diagnostics intersecting the requested span. iCIS only enumerates fixes and applies single
    /// actions, so FixAll / suppression / provider ordering are intentionally not implemented.
    /// </summary>
    [Export(typeof(ICodeFixService)), Shared]
    internal sealed class CodeFixService : ICodeFixService
    {
        // Static: the provider set derives from RoslynHost.DefaultCompositionAssemblies (a fixed
        // static set), so it is identical for every CodeFixService/RoslynHost. Building it once per
        // process avoids re-reflecting the Features assemblies on each project open.
        private static readonly Lazy<ImmutableDictionary<string, ImmutableArray<CodeFixProvider>>> ProvidersByDiagnosticId =
            new Lazy<ImmutableDictionary<string, ImmutableArray<CodeFixProvider>>>(
                BuildProviderMap, LazyThreadSafetyMode.ExecutionAndPublication);

        [ImportingConstructor]
        public CodeFixService()
        {
        }

        public async Task<IEnumerable<CodeFixCollection>> GetFixesAsync(Document document, TextSpan textSpan,
            bool includeSuppressionFixes, CancellationToken cancellationToken)
        {
            var result = new List<CodeFixCollection>();

            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null)
            {
                return result;
            }

            // Fresh compiler (syntax + semantic) diagnostics in the span, plus cached analyzer
            // diagnostics (see DiagnosticsCache) so analyzer-driven fixes are offered too.
            var diagnostics = new List<Diagnostic>();
            diagnostics.AddRange(model.GetDiagnostics(textSpan, cancellationToken));
            foreach (var analyzerDiagnostic in DiagnosticsCache.GetAnalyzerDiagnostics(document.Id))
            {
                if (analyzerDiagnostic.Location.IsInSource &&
                    analyzerDiagnostic.Location.SourceSpan.IntersectsWith(textSpan))
                {
                    diagnostics.Add(analyzerDiagnostic);
                }
            }

            if (diagnostics.Count == 0)
            {
                return result;
            }

            var providerMap = ProvidersByDiagnosticId.Value;

            // Map each contributing provider to the diagnostics (in span) it declares it can fix.
            var diagnosticsByProvider = new Dictionary<CodeFixProvider, List<Diagnostic>>();
            foreach (var diagnostic in diagnostics)
            {
                if (!diagnostic.Location.IsInSource)
                {
                    continue;
                }

                if (!providerMap.TryGetValue(diagnostic.Id, out var providers))
                {
                    continue;
                }

                foreach (var provider in providers)
                {
                    if (!diagnosticsByProvider.TryGetValue(provider, out var list))
                    {
                        list = new List<Diagnostic>();
                        diagnosticsByProvider[provider] = list;
                    }

                    list.Add(diagnostic);
                }
            }

            foreach (var entry in diagnosticsByProvider)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = entry.Key;
                var fixes = new List<CodeFix>();

                foreach (var diagnostic in entry.Value)
                {
                    var captured = new List<(CodeAction Action, ImmutableArray<Diagnostic> Diagnostics)>();
                    var context = new CodeFixContext(document, diagnostic,
                        (action, diags) => captured.Add((action, diags)), cancellationToken);

                    try
                    {
                        await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // A misbehaving provider must not break the whole lightbulb.
                        continue;
                    }

                    foreach (var (action, diags) in captured)
                    {
                        fixes.Add(new CodeFix(document.Project, action,
                            diags.IsDefaultOrEmpty ? ImmutableArray.Create(diagnostic) : diags, diagnostic));
                    }
                }

                if (fixes.Count > 0)
                {
                    result.Add(new CodeFixCollection(provider, textSpan, fixes.ToImmutableArray()));
                }
            }

            return result;
        }

        // iCIS always requests includeSuppressionFixes: false; suppression UI is not wired in WebAPIEditor.
        public CodeFixProvider? GetSuppressionFixer(string language, IEnumerable<string> diagnosticIds) => null;

        private static ImmutableDictionary<string, ImmutableArray<CodeFixProvider>> BuildProviderMap()
        {
            var map = new Dictionary<string, List<CodeFixProvider>>();
            var skipped = new List<string>();

            foreach (var assembly in RoslynHost.DefaultCompositionAssemblies)
            {
                foreach (var type in GetExportedCodeFixProviderTypes(assembly))
                {
                    var provider = TryCreateProvider(type);
                    if (provider == null)
                    {
                        // No usable parameterless ctor (e.g. requires MEF-injected services).
                        skipped.Add(type.FullName ?? type.Name);
                        continue;
                    }

                    ImmutableArray<string> fixableIds;
                    try
                    {
                        fixableIds = provider.FixableDiagnosticIds;
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var id in fixableIds)
                    {
                        if (!map.TryGetValue(id, out var list))
                        {
                            list = new List<CodeFixProvider>();
                            map[id] = list;
                        }

                        list.Add(provider);
                    }
                }
            }

            if (skipped.Count > 0)
            {
                // Observable signal: as Roslyn moves more built-in fixers to constructor injection,
                // this set grows and quick-fix coverage silently shrinks.
                System.Diagnostics.Trace.WriteLine(
                    $"RoslynPad: {skipped.Count} CodeFixProvider(s) skipped (no usable parameterless ctor): {string.Join(", ", skipped)}");
            }

            return map.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray());
        }

        private static IEnumerable<Type> GetExportedCodeFixProviderTypes(Assembly assembly)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type))
                {
                    continue;
                }

                ExportCodeFixProviderAttribute? export;
                try
                {
                    export = type.GetCustomAttribute<ExportCodeFixProviderAttribute>();
                }
                catch
                {
                    continue;
                }

                if (export == null)
                {
                    continue;
                }

                if (export.Languages is { Length: > 0 } languages && !languages.Contains(LanguageNames.CSharp))
                {
                    continue;
                }

                yield return type;
            }
        }

        private static CodeFixProvider? TryCreateProvider(Type type)
        {
            try
            {
                return (CodeFixProvider?)Activator.CreateInstance(type, nonPublic: true);
            }
            catch
            {
                // Providers with an [ImportingConstructor] that requires MEF-injected services have no
                // usable parameterless ctor - skip them rather than failing discovery.
                return null;
            }
        }
    }
}

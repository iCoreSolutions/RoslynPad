using System;
using System.Collections.Generic;
using System.Composition;

namespace RoslynPad.Roslyn.Diagnostics
{
    /// <summary>
    /// MEF-exported <see cref="IDiagnosticService"/> consumed by RoslynHost / iCIS WebAPICodeHost.
    /// At Roslyn 4.4 the push-diagnostics service implementation lives only in the netcoreapp-only
    /// LanguageServer assembly, so this no longer wraps a Roslyn service. Instead it relays events
    /// from the per-workspace <see cref="DiagnosticsEngine"/> instances via a process-wide bus.
    /// A workspace (e.g. iCIS WebApiCodeWorkspace) is created outside RoslynHost.AddDocument and
    /// attaches its engine through <see cref="WorkspaceExtensions.EnableDiagnostics"/>, so a direct
    /// reference from engine to this MEF singleton is not available - hence the static bus.
    /// </summary>
    [Export(typeof(IDiagnosticService)), Shared]
    internal sealed class DiagnosticsService : IDiagnosticService
    {
        [ImportingConstructor]
        public DiagnosticsService()
        {
            DiagnosticsUpdatedBus.Subscribe(this);
        }

        public event EventHandler<DiagnosticsUpdatedArgs>? DiagnosticsUpdated;

        internal void Raise(DiagnosticsUpdatedArgs args) => DiagnosticsUpdated?.Invoke(this, args);
    }

    /// <summary>
    /// Process-wide relay between <see cref="DiagnosticsEngine"/> producers and the MEF-exported
    /// <see cref="DiagnosticsService"/> consumers. Subscribers are held weakly and pruned on raise,
    /// so a service from a disposed RoslynHost/MEF container does not leak.
    /// </summary>
    internal static class DiagnosticsUpdatedBus
    {
        private static readonly object Gate = new object();
        private static readonly List<WeakReference<DiagnosticsService>> Subscribers =
            new List<WeakReference<DiagnosticsService>>();

        public static void Subscribe(DiagnosticsService service)
        {
            lock (Gate)
            {
                Subscribers.Add(new WeakReference<DiagnosticsService>(service));
            }
        }

        public static void Raise(DiagnosticsUpdatedArgs args)
        {
            List<DiagnosticsService> alive = new List<DiagnosticsService>();
            lock (Gate)
            {
                for (var i = Subscribers.Count - 1; i >= 0; i--)
                {
                    if (Subscribers[i].TryGetTarget(out var service))
                    {
                        alive.Add(service);
                    }
                    else
                    {
                        Subscribers.RemoveAt(i);
                    }
                }
            }

            foreach (var service in alive)
            {
                service.Raise(args);
            }
        }
    }
}

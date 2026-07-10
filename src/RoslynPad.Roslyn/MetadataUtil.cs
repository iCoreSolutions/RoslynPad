using System.Reflection;

namespace RoslynPad.Roslyn;

// iCore fork: LoadTypesBy/LoadTypesByNamespaces removed — they relied on
// Assembly.TryGetRawMetadata, which does not exist on .NET Framework 4.8, and their only
// caller (RoslynHost.GetDiagnosticCompositionTypes) is redundant at Roslyn 5.6 because the
// Features assembly is already part of DefaultCompositionAssemblies.
internal class MetadataUtil
{
    public static string GetAssemblyPath(Assembly assembly) => Path.Combine(AppContext.BaseDirectory, assembly.GetName().Name + ".dll");
}

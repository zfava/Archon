using System.Reflection;
using System.Runtime.Loader;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Plugins;

public static class PluginRegistrationServiceCollectionExtensions
{
    public static IServiceCollection AddArchonAIPlugins(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PluginOptions>()
            .Bind(configuration.GetSection(PluginOptions.SectionName));

        PluginOptions options = new();
        configuration.GetSection(PluginOptions.SectionName).Bind(options);

        var pluginPaths = BuildPluginPathMap(options);

        RegisterPluginsFromAssemblies<IAgent>(services, pluginPaths[PluginType.Agent]);
        RegisterPluginsFromAssemblies<IAgentTool>(services, pluginPaths[PluginType.Tool]);
        RegisterPluginsFromAssemblies<IConnector>(services, pluginPaths[PluginType.Connector]);
        RegisterPluginsFromAssemblies<IEvaluationEngine>(services, pluginPaths[PluginType.Evaluation]);

        return services;
    }

    private static Dictionary<PluginType, IReadOnlyList<string>> BuildPluginPathMap(PluginOptions options)
    {
        var discoveredPaths = options.AutoDiscoverInDirectories
            ? DiscoverAssembliesFromDirectories(options.PluginDirectories)
            : Array.Empty<string>();

        return new Dictionary<PluginType, IReadOnlyList<string>>
        {
            [PluginType.Agent] = MergePaths(options.AgentAssemblies, discoveredPaths),
            [PluginType.Tool] = MergePaths(options.ToolAssemblies, discoveredPaths),
            [PluginType.Connector] = MergePaths(options.ConnectorAssemblies, discoveredPaths),
            [PluginType.Evaluation] = MergePaths(options.EvaluationAssemblies, discoveredPaths)
        };
    }

    private static IReadOnlyList<string> MergePaths(IEnumerable<string> explicitPaths, IEnumerable<string> discoveredPaths)
    {
        return explicitPaths
            .Concat(discoveredPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> DiscoverAssembliesFromDirectories(IEnumerable<string> directories)
    {
        var assemblyPaths = new List<string>();

        foreach (string directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            assemblyPaths.AddRange(Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly));
        }

        return assemblyPaths;
    }

    private static void RegisterPluginsFromAssemblies<TContract>(IServiceCollection services, IEnumerable<string> assemblyPaths)
    {
        Type contract = typeof(TContract);
        foreach (string assemblyPath in assemblyPaths)
        {
            Assembly? assembly = TryLoadAssembly(assemblyPath);
            if (assembly is null)
            {
                continue;
            }

            foreach (Type pluginType in GetConcreteTypes(assembly).Where(contract.IsAssignableFrom))
            {
                services.AddSingleton(contract, pluginType);
            }
        }
    }

    private static Assembly? TryLoadAssembly(string assemblyPath)
    {
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<Type> GetConcreteTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false });
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .Cast<Type>();
        }
    }

    private enum PluginType
    {
        Agent,
        Tool,
        Connector,
        Evaluation
    }
}

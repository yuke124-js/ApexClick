using System.Reflection;
using System.Runtime.Loader;

namespace ApexClick.Services.Plugins;

public sealed class PluginRegistry : IDisposable
{
    private readonly Dictionary<string, IGraphNodePlugin> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginLoadContext> _contexts = new();
    private bool _disposed;

    public IReadOnlyCollection<IGraphNodePlugin> Items => _items.Values;

    public void Register(IGraphNodePlugin plugin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.TypeId)) throw new ArgumentException("Plugin TypeId is required.", nameof(plugin));
        _items[plugin.TypeId] = plugin;
    }

    public bool TryGet(string id, out IGraphNodePlugin? plugin) => _items.TryGetValue(id, out plugin);

    public int LoadFromDirectory(string directory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Directory.Exists(directory)) return 0;
        int loaded=0;
        foreach(var dll in Directory.EnumerateFiles(directory,"*.dll",SearchOption.TopDirectoryOnly))
            loaded += LoadAssembly(dll);
        return loaded;
    }

    public int LoadAssembly(string assemblyPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context=new PluginLoadContext(assemblyPath);
        try
        {
            var asm=context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            int loaded=0;
            foreach(var type in asm.GetTypes())
            {
                if(type.IsAbstract || !typeof(IGraphNodePlugin).IsAssignableFrom(type)) continue;
                if(Activator.CreateInstance(type) is IGraphNodePlugin plugin){ Register(plugin); loaded++; }
            }
            if(loaded>0) _contexts.Add(context); else context.Unload();
            return loaded;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    public void Dispose()
    {
        if(_disposed) return; _disposed=true;
        foreach(var plugin in _items.Values.OfType<IDisposable>())
        {
            try { plugin.Dispose(); } catch { }
        }
        _items.Clear();
        foreach(var c in _contexts) c.Unload();
        _contexts.Clear();
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        public PluginLoadContext(string path) : base(Path.GetFileNameWithoutExtension(path), isCollectible:true) => _resolver=new AssemblyDependencyResolver(Path.GetFullPath(path));
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path=_resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

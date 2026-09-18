namespace ApexClick.Contracts;

public interface IModuleRegistry
{
    IReadOnlyList<IFunctionModule> Modules { get; }
}

public sealed class ModuleRegistry : IModuleRegistry
{
    public IReadOnlyList<IFunctionModule> Modules { get; }

    public ModuleRegistry(IEnumerable<IFunctionModule> modules)
        => Modules = modules.OrderBy(m => m.SortOrder).ToList();

}

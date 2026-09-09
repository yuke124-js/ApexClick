namespace ApexClick.Contracts;

public interface IFunctionModule
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    int SortOrder { get; }

    object ContentViewModel { get; }
}

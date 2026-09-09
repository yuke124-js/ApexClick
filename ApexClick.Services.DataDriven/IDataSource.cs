namespace ApexClick.Services.DataDriven;

public interface IDataSource
{
    IReadOnlyList<string> ColumnNames { get; }
    IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadRowsAsync(CancellationToken cancellationToken = default);
}

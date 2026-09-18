using System.Runtime.CompilerServices;
using System.Text;

namespace ApexClick.Services.DataDriven;

public sealed class CsvDataSource : IDataSource
{
    private readonly string _filePath;
    private readonly char _delimiter;

    public IReadOnlyList<string> ColumnNames { get; }

    public CsvDataSource(string filePath, char delimiter = ',')
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("CSV-файл не найден.", filePath);

        _filePath = filePath;
        _delimiter = delimiter;

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        string? headerLine = ReadLogicalLine(reader)
            ?? throw new InvalidDataException("CSV-файл пуст — нет строки заголовка.");

        ColumnNames = ParseLine(headerLine, delimiter);
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(_filePath, Encoding.UTF8);
        ReadLogicalLine(reader);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = ReadLogicalLine(reader);
            if (line is null) yield break;
            if (line.Length == 0) continue;

            var values = ParseLine(line, _delimiter);
            var row = new Dictionary<string, string>(ColumnNames.Count);
            for (int i = 0; i < ColumnNames.Count; i++)
                row[ColumnNames[i]] = i < values.Count ? values[i] : string.Empty;

            yield return row;
            await Task.Yield();
        }
    }

    private static string? ReadLogicalLine(StreamReader reader)
    {
        string? line = reader.ReadLine();
        if (line is null) return null;

        while (CountUnescapedQuotes(line) % 2 != 0)
        {
            string? next = reader.ReadLine();
            if (next is null) break;
            line += "\n" + next;
        }
        return line;
    }

    private static int CountUnescapedQuotes(string line) => line.Count(c => c == '"');

    private static List<string> ParseLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}

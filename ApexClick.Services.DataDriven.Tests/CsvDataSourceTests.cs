using ApexClick.Services.DataDriven;

namespace ApexClick.Services.DataDriven.Tests;

public class CsvDataSourceTests
{
    [Fact]
    public async Task ReadsSimpleRows()
    {
        string path = WriteTemp("name,age\nAlice,30\nBob,25\n");
        try
        {
            var source = new CsvDataSource(path);
            Assert.Equal(new[] { "name", "age" }, source.ColumnNames);

            var rows = await CollectAsync(source);
            Assert.Equal(2, rows.Count);
            Assert.Equal("Alice", rows[0]["name"]);
            Assert.Equal("30", rows[0]["age"]);
            Assert.Equal("Bob", rows[1]["name"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HandlesQuotedFieldWithEmbeddedComma()
    {
        string path = WriteTemp("name,note\n\"Smith, John\",\"hello\"\n");
        try
        {
            var rows = await CollectAsync(new CsvDataSource(path));
            Assert.Equal("Smith, John", rows[0]["name"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HandlesEscapedQuoteInsideField()
    {
        string path = WriteTemp("quote\n\"She said \"\"hi\"\"\"\n");
        try
        {
            var rows = await CollectAsync(new CsvDataSource(path));
            Assert.Equal("She said \"hi\"", rows[0]["quote"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HandlesEmbeddedNewlineInsideQuotedField()
    {
        string path = WriteTemp("text\n\"line one\nline two\"\n");
        try
        {
            var rows = await CollectAsync(new CsvDataSource(path));
            Assert.Equal("line one\nline two", rows[0]["text"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ShorterRow_FillsMissingColumnsWithEmptyString()
    {
        string path = WriteTemp("a,b,c\n1,2\n");
        try
        {
            var rows = await CollectAsync(new CsvDataSource(path));
            Assert.Equal("1", rows[0]["a"]);
            Assert.Equal("2", rows[0]["b"]);
            Assert.Equal(string.Empty, rows[0]["c"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new CsvDataSource(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    private static async Task<List<IReadOnlyDictionary<string, string>>> CollectAsync(CsvDataSource source)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        await foreach (var row in source.ReadRowsAsync())
            rows.Add(row);
        return rows;
    }

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}

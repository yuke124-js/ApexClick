using Microsoft.Data.Sqlite;

namespace ApexClick.LicenseServer;

public sealed record OrderRecord(string Id, string Email, string Tier, string Status, string? PaymentId, string? LicenseKey);

public sealed class OrderStore
{
    private readonly string _connectionString;

    public OrderStore(IConfiguration config)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexClick", "LicenseServer");
        Directory.CreateDirectory(dir);
        var dbPath = config["APEXCLICK_LICENSE_DB_PATH"] ?? Path.Combine(dir, "orders.db");
        _connectionString = $"Data Source={dbPath}";
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Orders (
                Id TEXT PRIMARY KEY,
                Email TEXT NOT NULL,
                Tier TEXT NOT NULL,
                Status TEXT NOT NULL,
                PaymentId TEXT,
                LicenseKey TEXT,
                CreatedUtc TEXT NOT NULL,
                FulfilledUtc TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Orders_PaymentId ON Orders(PaymentId);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void CreatePending(string orderId, string email, string tier)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Orders (Id, Email, Tier, Status, CreatedUtc) VALUES ($id, $email, $tier, 'Pending', $now)";
        cmd.Parameters.AddWithValue("$id", orderId);
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$tier", tier);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void AttachPayment(string orderId, string paymentId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Orders SET PaymentId = $pid WHERE Id = $id";
        cmd.Parameters.AddWithValue("$pid", paymentId);
        cmd.Parameters.AddWithValue("$id", orderId);
        cmd.ExecuteNonQuery();
    }

    public OrderRecord? Find(string orderId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Email, Tier, Status, PaymentId, LicenseKey FROM Orders WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", orderId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new OrderRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5));
    }

    public OrderRecord? FindByPaymentId(string paymentId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Email, Tier, Status, PaymentId, LicenseKey FROM Orders WHERE PaymentId = $pid";
        cmd.Parameters.AddWithValue("$pid", paymentId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new OrderRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5));
    }

    public bool TryFulfill(string orderId, string licenseKey)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT Status FROM Orders WHERE Id = $id";
            check.Parameters.AddWithValue("$id", orderId);
            var status = check.ExecuteScalar() as string;
            if (status != "Pending") { tx.Rollback(); return false; }
        }
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Orders SET Status = 'Paid', LicenseKey = $key, FulfilledUtc = $now WHERE Id = $id";
            upd.Parameters.AddWithValue("$key", licenseKey);
            upd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("$id", orderId);
            upd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }
}

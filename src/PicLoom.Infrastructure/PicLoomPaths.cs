using Microsoft.Data.Sqlite;

namespace PicLoom.Infrastructure;

public sealed class PicLoomPaths
{
    public PicLoomPaths(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("data_directory_required", nameof(dataDirectory));

        DataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        DatabasePath = Path.Combine(DataDirectory, "picloom.db");
        AssetsDirectory = Path.Combine(DataDirectory, "assets");
        ReferencesDirectory = Path.Combine(DataDirectory, "references");
        SecretsDirectory = Path.Combine(DataDirectory, "secrets");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string AssetsDirectory { get; }
    public string ReferencesDirectory { get; }
    public string SecretsDirectory { get; }
    public string ConnectionString { get; }
}

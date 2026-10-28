namespace PicLoom.Api;

public sealed record PicLoomOptions(int Port, string DataDirectory, bool AllowDevProxy)
{
    public string Origin => $"http://127.0.0.1:{Port}";
    public const string DevOrigin = "http://127.0.0.1:5173";
}

public sealed class DataDirectoryLease : IDisposable
{
    private readonly FileStream stream;
    public DataDirectoryLease(string directory)
    {
        Directory.CreateDirectory(directory);
        stream = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() => stream.Dispose();
}

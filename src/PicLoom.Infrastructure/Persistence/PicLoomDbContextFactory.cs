using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PicLoom.Infrastructure.Persistence;

// Design-time defaults stay separate from the user's application database.
public sealed class PicLoomDbContextFactory : IDesignTimeDbContextFactory<PicLoomDbContext>
{
    public PicLoomDbContext CreateDbContext(string[] args)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("PICLOOM_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
            dataDirectory = Path.Combine(Path.GetTempPath(), "picloom-design");

        var paths = new PicLoomPaths(dataDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        var options = new DbContextOptionsBuilder<PicLoomDbContext>()
            .UseSqlite(paths.ConnectionString)
            .Options;
        return new PicLoomDbContext(options);
    }
}

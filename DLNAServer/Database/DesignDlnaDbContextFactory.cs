using DLNAServer.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DLNAServer.Database
{
    /// <summary>
    /// Factory class for creating instances of <see cref="DlnaDbContext"/> at design time. <br/>
    /// This is used by Entity Framework Core tooling (e.g., for migrations) to configure 
    /// the database context outside the main application's dependency injection system.
    /// </summary>
    public class DesignDlnaDbContextFactory : IDesignTimeDbContextFactory<DlnaDbContext>
    {
        public DlnaDbContext CreateDbContext(string[] args)
        {
            // Build configuration to load appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Ensure correct path
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Retrieve connection string from appsettings.json
            var connectionString = configuration.GetConnectionString("DefaultConnection")!;

            var optionsBuilder = new DbContextOptionsBuilder<DlnaDbContext>();
            _ = optionsBuilder.UseSqlite(connectionString);

            using var loggerFactory = LoggerFactory.Create(static (builder) =>
            {
                _ = builder.AddConsole();
            });
            var logger = loggerFactory.CreateLogger<DlnaDbContext>();

            var serverConfig = new ServerConfig();

            return new DlnaDbContext(optionsBuilder.Options, logger, serverConfig);
        }
    }
}

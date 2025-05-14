﻿using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Entities.Configurations;
using DLNAServer.Helpers.Interfaces;
using DLNAServer.Helpers.Logger;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DLNAServer.Database
{
    public class DlnaDbContext : DbContext, ITerminateAble
    {
        private readonly ILogger<DlnaDbContext> _logger;
        private readonly ServerConfig _serverConfig;

        public DlnaDbContext(DbContextOptions<DlnaDbContext> options, ILogger<DlnaDbContext> logger, ServerConfig serverConfig)
            : base(options)
        {
            _logger = logger;
            _serverConfig = serverConfig;
        }
        public DbSet<DirectoryEntity> DirectoryEntities { get; set; }
        public DbSet<FileEntity> FileEntities { get; set; }
        public DbSet<MediaAudioEntity> MediaAudioEntities { get; set; }
        public DbSet<MediaSubtitleEntity> MediaSubtitleEntities { get; set; }
        public DbSet<MediaVideoEntity> MediaVideoEntities { get; set; }
        public DbSet<ServerEntity> ServerEntities { get; set; }
        public DbSet<ThumbnailDataEntity> ThumbnailDataEntities { get; set; }
        public DbSet<ThumbnailEntity> ThumbnailEntities { get; set; }
        public async Task<bool> CheckDbSetsOk(CancellationToken cancellationToken)
        {
            try
            {
                _ = await DirectoryEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await FileEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await MediaAudioEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await MediaSubtitleEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await MediaVideoEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await ServerEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await ThumbnailDataEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                _ = await ThumbnailEntities.OrderBy(static (e) => e.Id).FirstOrDefaultAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public async Task<bool> OptimizeDatabase(CancellationToken cancellationToken)
        {
            // see DLNAServer.Database.SQLitePragmaInterceptor too 
            try
            {
                StringBuilder sb = new();
                // SQLite stores data in fixed-size pages (default 4096 bytes on modern systems).
                _ = sb.Append("PRAGMA page_size=4096; ");

                // Changes the journaling mode to WAL, improves performance for concurrent reads/writes
                // WAL = Write-Ahead Logging
                _ = sb.Append("PRAGMA journal_mode=WAL; ");

                // reduces fsync() calls on disk writes, making transactions faster
                // trade durability for speed
                _ = sb.Append("PRAGMA synchronous=NORMAL; ");

                // runs internal optimizations like reindexing and clearing unused pages
                _ = sb.Append("PRAGMA optimize; ");

                // rebuilds the database file, removing fragmentation and reducing its size
                // !!! locks the database while running command !!!
                _ = sb.Append("VACUUM; ");

                _ = await this.Database.ExecuteSqlRawAsync(sb.ToString(), cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task TerminateAsync()
        {
            await base.Database.CloseConnectionAsync();
            await base.DisposeAsync();
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var entityTypes = typeof(BaseEntity)
                .Assembly
                .GetTypes()
                .Where(static (t) => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t));

            foreach (var entityType in entityTypes)
            {
                var configurationType = typeof(BaseEntityConfiguration<>).MakeGenericType([entityType]);
                var configurationInstance = Activator.CreateInstance(configurationType);

                modelBuilder.ApplyConfiguration((dynamic)configurationInstance!);
            }
        }
        public async Task<IDictionary<string, long>> GetAllTablesRowCountAsync()
        {

            var rowCounts = new Dictionary<string, long>();
            var connection = this.Database.GetDbConnection();

            try
            {
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var entityTypes = this.Model.GetEntityTypes();
                var tableNames = entityTypes
                    .Where(static (et) => !string.IsNullOrWhiteSpace(et.GetTableName()))
                    .Select(static (et) => et.GetTableName()!)
                    .ToArray();

                // Count rows for each table
                foreach (var tableName in tableNames)
                {
                    var countCommand = connection.CreateCommand();
                    countCommand.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\";";
                    var count = (long?)await countCommand.ExecuteScalarAsync() ?? -1;
                    rowCounts[tableName] = count;
                }
            }
            finally
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }

            return rowCounts;
        }

        #region Save change modify
        public override int SaveChanges()
        {
            try
            {
                ChangeTrackerModify();
                return base.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            try
            {
                ChangeTrackerModify();
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            try
            {
                ChangeTrackerModify();
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                ChangeTrackerModify();
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }

        private void ChangeTrackerModify()
        {
            var maxDegreeOfParallelism = Math.Max(Math.Min(ChangeTracker.Entries().Count(), (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

            _ = Parallel.ForEach(
                ChangeTracker.Entries<BaseEntity>(),
                parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                static (entry) =>
                {
                    switch (entry.State)
                    {
                        case EntityState.Added:
                            entry.Entity.CreatedInDB = DateTime.Now;
                            break;
                        case EntityState.Modified:
                            entry.Entity.ModifiedInDB = DateTime.Now;
                            break;
                            //case EntityState.Deleted:
                            //    break;
                            //case EntityState.Unchanged:
                            //    break;
                    }
                });
        }
        #endregion
    }
}

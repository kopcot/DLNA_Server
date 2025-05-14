using DLNAServer.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog.Core;
using System.Data.Common;
using System.Text;

namespace DLNAServer.Database.Interceptors
{
    public class PerformanceInterceptor : DbCommandInterceptor, IDisposable
    {
        private readonly Logger _Logger;
        private readonly ServerConfig _serverConfig;
        private readonly TimeSpan _querySlowThreshold;

        public PerformanceInterceptor(Logger serilogLogger, TimeSpan querySlowThreshold, ServerConfig serverConfig)
        {
            _querySlowThreshold = querySlowThreshold;
            _Logger = serilogLogger;
            _serverConfig = serverConfig;
        }
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            var originalResult = base.ReaderExecuted(command, eventData, result);

            if (eventData.Duration > _querySlowThreshold && _serverConfig.ServerLogDatabaseSlowQuery)
            {
                StringBuilder sb = new();
                _ = sb.AppendLine("Parameters:");
                for (var i = 0; i < command.Parameters.Count; i++)
                {
                    _ = sb
                        .Append(command.Parameters[i].ParameterName)
                        .Append(" = ")
                        .AppendLine($"{command.Parameters[i].Value}");
                }
                _Logger.Warning($"Slow {nameof(ReaderExecuted)} Detected\nDuration: {eventData.Duration.TotalMilliseconds,6:0.00} ms\nCommand text: {command.CommandText}\n{sb}\n{new string('-', 20)}");
            }

            return originalResult;
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var originalResult = await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);

            if (eventData.Duration > _querySlowThreshold && _serverConfig.ServerLogDatabaseSlowQuery)
            {
                StringBuilder sb = new();
                _ = sb.AppendLine("Parameters:");
                for (var i = 0; i < command.Parameters.Count; i++)
                {
                    _ = sb
                        .Append(command.Parameters[i].ParameterName)
                        .Append(" = ")
                        .AppendLine($"{command.Parameters[i].Value}");
                }
                _Logger.Warning($"Slow {nameof(ReaderExecuted)} Detected\nDuration: {eventData.Duration.TotalMilliseconds,6:0.00} ms\nCommand text: {command.CommandText}\n{sb}\n{new string('-', 20)}");
            }

            return originalResult;
        }

        #region Dispose
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                _Logger.Dispose();

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~PerformanceInterceptor()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}

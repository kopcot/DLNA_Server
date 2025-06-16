using DLNAServer.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog.Core;
using System.Data.Common;
using System.Text;

namespace DLNAServer.Database.Interceptors
{
    public class PerformanceInterceptor : DbCommandInterceptor
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
    }
}

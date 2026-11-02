using Microsoft.Extensions.Logging;
using NetPilot.Platform.Windows.Diagnostics;

namespace NetPilot.Service;

internal sealed class NetPilotFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopeProvider;

    public ILogger CreateLogger(string categoryName) =>
        new NetPilotFileLogger(categoryName, () => _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class NetPilotFileLogger(
        string categoryName,
        Func<IExternalScopeProvider?> scopeProviderAccessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            scopeProviderAccessor()?.Push(state) ?? NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var correlationId = FindCorrelationId(scopeProviderAccessor());
            var code = logLevel switch
            {
                LogLevel.Critical => "NP-SVC-CRIT-001",
                LogLevel.Error => "NP-SVC-ERR-001",
                LogLevel.Warning => "NP-SVC-WARN-001",
                _ => "NP-SVC-INFO-001"
            };

            var level = logLevel switch
            {
                LogLevel.Critical => NetPilotDiagnosticLevel.Critical,
                LogLevel.Error => NetPilotDiagnosticLevel.Error,
                LogLevel.Warning => NetPilotDiagnosticLevel.Warning,
                _ => NetPilotDiagnosticLevel.Information
            };

            SafeDiagnosticLog.WriteService(
                "service",
                level,
                code,
                formatter(state, exception),
                correlationId,
                exception,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Category"] = categoryName,
                    ["EventId"] = eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        private static string? FindCorrelationId(IExternalScopeProvider? scopeProvider)
        {
            if (scopeProvider is null)
            {
                return null;
            }

            string? correlationId = null;
            scopeProvider.ForEachScope(
                static (scope, state) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                    {
                        foreach (var pair in values)
                        {
                            if (pair.Key.Equals("CorrelationId", StringComparison.Ordinal)
                                && pair.Value is not null)
                            {
                                state.Value = pair.Value.ToString();
                            }
                        }
                    }
                },
                new CorrelationHolder(value => correlationId = value));

            return correlationId;
        }

        private sealed class CorrelationHolder(Action<string?> setter)
        {
            public string? Value
            {
                set => setter(value);
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System.Runtime.CompilerServices;

namespace Core
{
    public static class LoggerService
    {
        private static TelemetryClient _telemetry;

        public static void Configure(TelemetryClient telemetryClient)
        {
            _telemetry = telemetryClient;
        }

        public static void LogInformation(string message,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log("INFO", message, callerName, callerFile, sourceLineNumber);
        }

        public static void LogWarning(string message,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log("WARN", message, callerName, callerFile, sourceLineNumber);
        }

        public static void LogErrorMensaje(string message,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log("ERROR", message, callerName, callerFile, sourceLineNumber);
        }

        public static void LogError(string message, Exception ex,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log("ERROR", message, callerName, callerFile, sourceLineNumber, ex);

        }

        private static void Log(string level, string message, string callerName, string callerFile, int sourceLineNumber, Exception ex = null)
        {
            var fullMessage = $"Mensaje: {message} | Método: {callerName} | Archivo: {callerFile} | Línea: {sourceLineNumber}";

            var props = new Dictionary<string, string>
            {
                { "Caller", callerName },
                { "File", callerFile },
                { "Line", sourceLineNumber.ToString() }
            };

            switch (level)
            {
                case "INFO":
                    _telemetry.TrackTrace(message, SeverityLevel.Information, props);
                    break;
                case "WARN":
                    _telemetry.TrackTrace(message, SeverityLevel.Warning, props);
                    break;
                case "ERROR":
                    var errorProps = new Dictionary<string, string>(props)
                    {
                        { "Exception", ex?.ToString() ?? string.Empty },
                    };

                    _telemetry.TrackTrace(message, SeverityLevel.Error, props);
                    _telemetry.TrackException(ex, errorProps);
                    break;
            }
        }
    }
}

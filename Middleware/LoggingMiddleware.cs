using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http.Extensions;
using System.Diagnostics;
using System.Text;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TelemetryClient _telemetryClient;

    public LoggingMiddleware(RequestDelegate next, TelemetryClient telemetryClient)
    {
        _next = next;
        _telemetryClient = telemetryClient;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var activity = new Activity("HttpRequestTrace");
        activity.Start();

        var traceId = activity.TraceId.ToString();
        var stopwatch = Stopwatch.StartNew();
        context.Response.Headers["X-Trace-Id"] = traceId;

        context.Request.EnableBuffering();

        string requestBody = "";
        if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
        {
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        var operationName = $"{context.Request.Path}";

        _telemetryClient.Context.Operation.Id = traceId;
        _telemetryClient.Context.Operation.Name = operationName;

        var commonProps = new Dictionary<string, string>
        {
            { "TraceId", traceId },
            { "OperationName", operationName },
            { "Method", context.Request.Method },
            { "QueryString", context.Request.QueryString.ToString() },
            { "RequestBody", Truncate(requestBody, 10000) }
        };

        _telemetryClient.TrackTrace("➡️ HTTP Request Started", SeverityLevel.Information, commonProps);

        var originalBody = context.Response.Body;
        using var tempBody = new MemoryStream();
        context.Response.Body = tempBody;

        try
        {
            await _next(context);

            tempBody.Position = 0;
            var responseBody = await new StreamReader(tempBody).ReadToEndAsync();
            tempBody.Position = 0;
            await tempBody.CopyToAsync(originalBody);

            stopwatch.Stop();

            var salidaProps = new Dictionary<string, string>(commonProps)
            {
                { "StatusCode", context.Response.StatusCode.ToString() },
                { "ResponseBody", Truncate(responseBody, 10000) },
                { "DurationMs", stopwatch.ElapsedMilliseconds.ToString() }
            };

            _telemetryClient.TrackTrace("⬅️ HTTP Request Completed", SeverityLevel.Information, salidaProps);

            // TrackRequest para registrar la operación formal con status y duración
            var requestTelemetry = new RequestTelemetry
            {
                Name = $"{context.Request.Method} {context.Request.Path}",
                Timestamp = DateTimeOffset.UtcNow - stopwatch.Elapsed,
                Duration = stopwatch.Elapsed,
                ResponseCode = context.Response.StatusCode.ToString(),
                Success = context.Response.StatusCode < 400,
                Url = new Uri(context.Request.GetEncodedUrl())
            };

            requestTelemetry.Context.Operation.Id = traceId;
            requestTelemetry.Context.Operation.Name = operationName;

            requestTelemetry.Properties["RequestBody"] = Truncate(requestBody, 10000);
            requestTelemetry.Properties["QueryString"] = context.Request.QueryString.ToString();
            requestTelemetry.Properties["ResponseBody"] = Truncate(responseBody, 10000);

            _telemetryClient.TrackRequest(requestTelemetry);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var errorProps = new Dictionary<string, string>(commonProps)
            {
                { "Exception", ex.ToString() },
                { "StatusCode", context.Response?.StatusCode.ToString() ?? "N/A" },
                { "DurationMs", stopwatch.ElapsedMilliseconds.ToString() }
            };

            _telemetryClient.TrackException(ex, errorProps);

            // También puedes registrar una request fallida si quieres:
            var failedRequestTelemetry = new RequestTelemetry
            {
                Name = $"{context.Request.Method} {context.Request.Path}",
                Timestamp = DateTimeOffset.UtcNow - stopwatch.Elapsed,
                Duration = stopwatch.Elapsed,
                ResponseCode = context.Response?.StatusCode.ToString() ?? "500",
                Success = false,
                Url = new Uri(context.Request.GetEncodedUrl())
            };

            failedRequestTelemetry.Context.Operation.Id = traceId;
            failedRequestTelemetry.Context.Operation.Name = operationName;
            failedRequestTelemetry.Properties["RequestBody"] = Truncate(requestBody, 10000);
            failedRequestTelemetry.Properties["QueryString"] = context.Request.QueryString.ToString();
            failedRequestTelemetry.Properties["ResponseBody"] = Truncate(ex.ToString(), 10000);
           
            _telemetryClient.TrackRequest(failedRequestTelemetry);

            throw;
        }
        finally
        {
            activity.Stop();
            context.Response.Body = originalBody;
        }
    }

    private string Truncate(string value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? "" :
            value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...(truncated)";
    }
}

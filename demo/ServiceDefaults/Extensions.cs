using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// サービス検出、HTTP resilience、ヘルスチェック、OpenTelemetry を共通設定する。
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(options =>
            {
                // Agent の tool call を含む推論に備え、既定の10秒 timeout を延長する。
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(4);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                // Circuit Breaker の判定窓は Attempt timeout の2倍以上にする。
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(8);
                options.Retry.DisableForUnsafeHttpMethods();
            });

            // HTTP client でサービス検出を有効にする。
            http.AddServiceDiscovery();
        });

        // 必要に応じて、サービス検出で許可する scheme を制限する。
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // AgentClient の独自 span も Aspire Dashboard に出力する。
                tracing.AddSource(builder.Environment.ApplicationName, "EnvReporter.Web.AgentClient")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // health check の要求を trace 対象から除外する。
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // 必要な package を追加すると gRPC client の計測を有効にできる。
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // 必要な package を追加すると Azure Monitor exporter を有効にできる。
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // アプリの応答性を確認する既定の liveness check を追加する。
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // 本番環境での公開にはセキュリティ上の影響があるため、health check endpoint は開発環境だけで公開する。
        // 詳細: https://aka.ms/aspire/healthchecks
        if (app.Environment.IsDevelopment())
        {
            // 全 health check の成功を traffic 受付可能の条件とする。
            app.MapHealthChecks(HealthEndpointPath);

            // live タグ付き check の成功で稼働中と判定する。
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}

using Subtitles.Infrastructure.Data;
using Subtitles.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSubtitlesData(builder.Configuration);
builder.Services.AddHostedService<PollingHostedService>();

// Real IPipelineStage implementations register here as they're built (P1.x) — see
// docs/Architecture.md §2.3 for the stage sequence.

var host = builder.Build();
host.Run();

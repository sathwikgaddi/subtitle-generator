using Subtitles.Infrastructure.Ai;
using Subtitles.Infrastructure.Data;
using Subtitles.Infrastructure.Pipeline;
using Subtitles.Infrastructure.Storage;
using Subtitles.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSubtitlesData(builder.Configuration);
builder.Services.AddSubtitlesStorage(builder.Configuration);
builder.Services.AddSubtitlesAi(builder.Configuration);
builder.Services.AddSubtitlesPipeline();
builder.Services.AddHostedService<PollingHostedService>();

// More IPipelineStage implementations register here as they're built (P1.x) — see
// docs/Architecture.md §2.3 for the stage sequence.

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<PromptSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

host.Run();

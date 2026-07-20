using Microsoft.EntityFrameworkCore;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data;

public class SubtitlesDbContext(DbContextOptions<SubtitlesDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<SubtitleTrack> SubtitleTracks => Set<SubtitleTrack>();
    public DbSet<SubtitleCue> SubtitleCues => Set<SubtitleCue>();
    public DbSet<Word> Words => Set<Word>();
    public DbSet<Export> Exports => Set<Export>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SubtitlesDbContext).Assembly);
    }
}

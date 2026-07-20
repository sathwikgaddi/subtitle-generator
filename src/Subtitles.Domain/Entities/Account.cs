namespace Subtitles.Domain.Entities;

public class Account
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Informational only in MVP — not enforced until Phase 3 billing. See docs/Database.md §3.</summary>
    public string PlanTier { get; set; } = "free";

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Video> Videos { get; set; } = new List<Video>();
}

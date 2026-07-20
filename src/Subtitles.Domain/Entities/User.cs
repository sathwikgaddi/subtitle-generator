namespace Subtitles.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}

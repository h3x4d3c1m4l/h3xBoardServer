namespace H3xBoardServer.Data.Entities;

[Table("users")]
public class UserEntity
{
    [PrimaryKey, Identity]
    [Column("id")]
    public int Id { get; set; }

    [Column("username"), NotNull]
    public string Username { get; set; } = null!;

    [Column("email"), NotNull]
    public string Email { get; set; } = null!;

    [Column("password_hash"), NotNull]
    public string PasswordHash { get; set; } = null!;

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at"), NotNull]
    public DateTime UpdatedAt { get; set; }
}

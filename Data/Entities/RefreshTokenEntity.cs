namespace H3xBoardServer.Data.Entities;

[Table("refresh_tokens")]
public class RefreshTokenEntity
{
    [PrimaryKey, Identity]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id"), NotNull]
    public int UserId { get; set; }

    [Column("token"), NotNull]
    public string Token { get; set; } = null!;

    [Column("expires_at"), NotNull]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("is_revoked"), NotNull]
    public bool IsRevoked { get; set; }
}

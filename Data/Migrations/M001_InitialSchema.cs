using FluentMigrator;

namespace H3xBoardServer.Data.Migrations;

[Migration(1, "Initial schema — users, boards")]
public class M001_InitialSchema : Migration
{
    public override void Up()
    {
        Create.Table("users")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("email").AsString(255).NotNullable().Unique()
            .WithColumn("password_hash").AsString(255).NotNullable()
            .WithColumn("created_at").AsString(30).NotNullable()
            .WithColumn("updated_at").AsString(30).NotNullable();

        Create.Table("boards")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("user_id").AsString(36).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("data").AsString(int.MaxValue).NotNullable()
            .WithColumn("created_at").AsString(30).NotNullable()
            .WithColumn("updated_at").AsString(30).NotNullable();

        Create.Index("ix_boards_user_id").OnTable("boards").OnColumn("user_id");
    }

    public override void Down()
    {
        Delete.Table("boards");
        Delete.Table("users");
    }
}

using FluentMigrator;

namespace H3xBoardServer.Data.Migrations;

[Migration(5, "Add user_settings table — owner-scoped key/value preferences")]
public class M005_AddUserSettings : Migration
{
    public override void Up()
    {
        // One row per (user_id, key). The composite primary key gives the owner-scoped lookup index
        // for free (every query filters by user_id) and makes the per-key upsert natural.
        Create.Table("user_settings")
            .WithColumn("user_id").AsString(36).NotNullable().PrimaryKey()
            .WithColumn("key").AsString(128).NotNullable().PrimaryKey()
            .WithColumn("value").AsString(int.MaxValue).NotNullable()  // raw JSON text
            .WithColumn("created_at").AsString(30).NotNullable()
            .WithColumn("updated_at").AsString(30).NotNullable();
    }

    public override void Down()
    {
        Delete.Table("user_settings");
    }
}

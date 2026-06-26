using FluentMigrator;

namespace H3xBoardServer.Data.Migrations;

[Migration(3, "Add files table — owner-scoped blob metadata")]
public class M003_AddFilesTable : Migration
{
    public override void Up()
    {
        Create.Table("files")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("owner_scope").AsString(20).NotNullable()
            .WithColumn("owner_id").AsString(36).NotNullable()
            .WithColumn("storage_key").AsString(512).NotNullable()
            .WithColumn("path").AsString(1024).NotNullable()
            .WithColumn("file_name").AsString(255).NotNullable()
            .WithColumn("content_type").AsString(255).NotNullable()
            .WithColumn("size_bytes").AsInt64().NotNullable()
            .WithColumn("created_at").AsString(30).NotNullable()
            .WithColumn("updated_at").AsString(30).NotNullable();

        // Browse queries filter by owner and a path prefix.
        Create.Index("ix_files_owner_path").OnTable("files")
            .OnColumn("owner_scope").Ascending()
            .OnColumn("owner_id").Ascending()
            .OnColumn("path").Ascending();
    }

    public override void Down()
    {
        Delete.Table("files");
    }
}

using FluentMigrator;

namespace H3xBoardServer.Data.Migrations;

[Migration(4, "Add file kind discriminator and board screenshot link")]
public class M004_AddBoardScreenshots : Migration
{
    public override void Up()
    {
        // Classifies stored files. Existing rows are all user uploads, so default them to "user";
        // board screenshots use "board-screenshot" and are hidden from the generic file API.
        Alter.Table("files")
            .AddColumn("kind").AsString(40).NotNullable().WithDefaultValue("user");

        // 1:1 link from a board to its screenshot file (null = none yet).
        Alter.Table("boards")
            .AddColumn("screenshot_file_id").AsString(36).Nullable();
    }

    public override void Down()
    {
        Delete.Column("screenshot_file_id").FromTable("boards");
        Delete.Column("kind").FromTable("files");
    }
}

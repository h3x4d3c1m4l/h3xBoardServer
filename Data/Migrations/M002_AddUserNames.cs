using FluentMigrator;

namespace H3xBoardServer.Data.Migrations;

[Migration(2, "Add optional first_name, last_name to users")]
public class M002_AddUserNames : Migration
{
    public override void Up()
    {
        Alter.Table("users")
            .AddColumn("first_name").AsString(255).Nullable()
            .AddColumn("last_name").AsString(255).Nullable();
    }

    public override void Down()
    {
        Delete.Column("first_name").FromTable("users");
        Delete.Column("last_name").FromTable("users");
    }
}

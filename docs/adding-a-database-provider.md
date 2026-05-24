# Adding a database provider

1. Add the NuGet packages — e.g. for MySQL:
   ```xml
   <PackageReference Include="linq2db.MySql"                 Version="..." />
   <PackageReference Include="FluentMigrator.Runner.MySql"   Version="..." />
   ```
2. Uncomment the relevant `case` blocks in `Program.cs`
3. Set `Database:Provider` to `"MySQL"` in config

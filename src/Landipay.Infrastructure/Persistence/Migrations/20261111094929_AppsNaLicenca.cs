using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Landipay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppsNaLicenca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppsHabilitados",
                table: "ConfigLicencas",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppsHabilitados",
                table: "ConfigLicencas");
        }
    }
}

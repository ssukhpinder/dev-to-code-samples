using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NamedDefaultConstraints.Migrations
{
    /// <inheritdoc />
    public partial class NameDefaultConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Jobs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "queued",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldDefaultValue: "queued")
                .Annotation("Relational:DefaultConstraintName", "DF_Jobs_Status");

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "Jobs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0)
                .Annotation("Relational:DefaultConstraintName", "DF_Jobs_RetryCount");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedUtc",
                table: "Jobs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "SYSUTCDATETIME()")
                .Annotation("Relational:DefaultConstraintName", "DF_Jobs_CreatedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Jobs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "queued",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldDefaultValue: "queued")
                .OldAnnotation("Relational:DefaultConstraintName", "DF_Jobs_Status");

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "Jobs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0)
                .OldAnnotation("Relational:DefaultConstraintName", "DF_Jobs_RetryCount");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedUtc",
                table: "Jobs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "SYSUTCDATETIME()")
                .OldAnnotation("Relational:DefaultConstraintName", "DF_Jobs_CreatedUtc");
        }
    }
}

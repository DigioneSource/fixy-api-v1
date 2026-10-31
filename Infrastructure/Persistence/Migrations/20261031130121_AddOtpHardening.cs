using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsumedAt",
                table: "UserOtps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                table: "UserOtps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "UserOtps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "UserOtps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifyTokenHash",
                table: "UserOtps",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("a1f7d8c1-3e21-4a8c-9b11-2d7f4c5e1001"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 1, 21, 121, DateTimeKind.Utc).AddTicks(5050));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("b2e8c9d2-4f32-4b9d-8c22-3e8f5d6f2002"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 1, 21, 121, DateTimeKind.Utc).AddTicks(5050));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("c3f9d0e3-5a43-4cad-9d33-4f9a6e7f3003"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 1, 21, 121, DateTimeKind.Utc).AddTicks(5050));

            migrationBuilder.CreateIndex(
                name: "IX_UserOtps_VerifyTokenHash",
                table: "UserOtps",
                column: "VerifyTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserOtps_VerifyTokenHash",
                table: "UserOtps");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "UserOtps");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                table: "UserOtps");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "UserOtps");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "UserOtps");

            migrationBuilder.DropColumn(
                name: "VerifyTokenHash",
                table: "UserOtps");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("a1f7d8c1-3e21-4a8c-9b11-2d7f4c5e1001"),
                column: "CreatedDate",
                value: new DateTime(2026, 8, 25, 3, 44, 9, 542, DateTimeKind.Utc).AddTicks(3489));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("b2e8c9d2-4f32-4b9d-8c22-3e8f5d6f2002"),
                column: "CreatedDate",
                value: new DateTime(2026, 8, 25, 3, 44, 9, 542, DateTimeKind.Utc).AddTicks(3502));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("c3f9d0e3-5a43-4cad-9d33-4f9a6e7f3003"),
                column: "CreatedDate",
                value: new DateTime(2026, 8, 25, 3, 44, 9, 542, DateTimeKind.Utc).AddTicks(3507));
        }
    }
}

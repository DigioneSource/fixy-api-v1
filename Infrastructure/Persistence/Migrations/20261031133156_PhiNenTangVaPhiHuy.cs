using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhiNenTangVaPhiHuy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CancellationFee",
                table: "Bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HeldPlatformFee",
                table: "Bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlatformFee",
                table: "Bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlatformFeeVat",
                table: "Bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WorkerNet",
                table: "Bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("a1f7d8c1-3e21-4a8c-9b11-2d7f4c5e1001"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 31, 55, 739, DateTimeKind.Utc).AddTicks(5820));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("b2e8c9d2-4f32-4b9d-8c22-3e8f5d6f2002"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 31, 55, 739, DateTimeKind.Utc).AddTicks(5830));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("c3f9d0e3-5a43-4cad-9d33-4f9a6e7f3003"),
                column: "CreatedDate",
                value: new DateTime(2026, 10, 31, 13, 31, 55, 739, DateTimeKind.Utc).AddTicks(5830));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationFee",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "HeldPlatformFee",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformFee",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformFeeVat",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "WorkerNet",
                table: "Bookings");

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
        }
    }
}

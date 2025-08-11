using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Add_FiscalYear_CommissionSession_User_Role_Seed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "CreatedAt", "IsActive", "IsDeleted", "IsSystem", "Name", "UpdatedAt" },
                values: new object[] { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(3619), new TimeSpan(0, 0, 0, 0, 0)), true, false, true, "Admin", null });

            migrationBuilder.InsertData(
                table: "FiscalYears",
                columns: new[] { "Id", "CreatedAt", "EndDate", "IsActive", "IsDeleted", "Name", "StartDate", "UpdatedAt" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555555"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 519, DateTimeKind.Unspecified).AddTicks(8640), new TimeSpan(0, 0, 0, 0, 0)), new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), true, false, "", new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), null });

            migrationBuilder.InsertData(
                table: "AppUsers",
                columns: new[] { "Id", "CreatedAt", "FullName", "IsActive", "IsDeleted", "LastLoginDate", "PasswordHash", "RoleId", "UpdatedAt", "Username" },
                values: new object[] { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(6045), new TimeSpan(0, 0, 0, 0, 0)), "System Administrator", true, false, null, "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S", new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Username",
                table: "AppUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Currencies_Code",
                table: "Currencies");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_Username",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles");

            migrationBuilder.DeleteData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

            migrationBuilder.DeleteData(
                table: "FiscalYears",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"));

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppRoles");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "AppRoles");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id");
        }
    }
}

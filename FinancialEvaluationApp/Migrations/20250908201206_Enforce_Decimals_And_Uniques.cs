using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialEvaluationApp.Migrations
{
    public partial class Enforce_Decimals_And_Uniques : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_AppRoles_RoleId",
                table: "AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_FxRates_Currencies_BaseCurrencyId",
                table: "FxRates");

            migrationBuilder.DropForeignKey(
                name: "FK_FxRates_Currencies_QuoteCurrencyId",
                table: "FxRates");

            migrationBuilder.DropForeignKey(
                name: "FK_Proposals_Currencies_ForeignCurrencyId",
                table: "Proposals");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            // اگر قبلاً ایندکس روی Code هست، قبل از تغییر ستون باید حذف شود:
            migrationBuilder.DropIndex(
                name: "IX_Currencies_Code",
                table: "Currencies");

            // تغییر طول‌ها مطابق مدل
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Currencies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "AppUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            // ایندکس یونیک Code را دوباره بساز
            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            // FK ها را با رفتار حذفِ Non-Cascade برگردان
            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_AppRoles_RoleId",
                table: "AppUsers",
                column: "RoleId",
                principalTable: "AppRoles",
                principalColumn: "Id"); // NoAction/Restrict

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId",
                principalTable: "Companies",
                principalColumn: "Id"); // NoAction/Restrict

            // ⬇️ مشکل‌دارها: هر دو به NoAction تغییر کردند
            migrationBuilder.AddForeignKey(
                name: "FK_FxRates_Currencies_BaseCurrencyId",
                table: "FxRates",
                column: "BaseCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_FxRates_Currencies_QuoteCurrencyId",
                table: "FxRates",
                column: "QuoteCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_Proposals_Currencies_ForeignCurrencyId",
                table: "Proposals",
                column: "ForeignCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id"); // NoAction/Restrict

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id"); // NoAction/Restrict
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // برای تقارن: اول ایندکس را حذف کن
            migrationBuilder.DropIndex(
                name: "IX_Currencies_Code",
                table: "Currencies");

            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_AppRoles_RoleId",
                table: "AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_FxRates_Currencies_BaseCurrencyId",
                table: "FxRates");

            migrationBuilder.DropForeignKey(
                name: "FK_FxRates_Currencies_QuoteCurrencyId",
                table: "FxRates");

            migrationBuilder.DropForeignKey(
                name: "FK_Proposals_Currencies_ForeignCurrencyId",
                table: "Proposals");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Currencies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "AppUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            // ساختن دوبارهٔ ایندکس در Down
            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            // FK ها به حالت قبلی (Restrict) برگردند
            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_AppRoles_RoleId",
                table: "AppUsers",
                column: "RoleId",
                principalTable: "AppRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FxRates_Currencies_BaseCurrencyId",
                table: "FxRates",
                column: "BaseCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FxRates_Currencies_QuoteCurrencyId",
                table: "FxRates",
                column: "QuoteCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Proposals_Currencies_ForeignCurrencyId",
                table: "Proposals",
                column: "ForeignCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

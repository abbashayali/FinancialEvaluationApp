using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegistrationNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ParentCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Companies_ParentCompanyId",
                        column: x => x.ParentCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultFxRateToIRR = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bidders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bidders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bidders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tenders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosingDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BaseEstimateAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_BaseCurrencyId",
                        column: x => x.BaseCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_QuoteCurrencyId",
                        column: x => x.QuoteCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BidderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FinalScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationResults_Bidders_BidderId",
                        column: x => x.BidderId,
                        principalTable: "Bidders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationResults_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BidderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RialAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ForeignAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ForeignCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    ProposalNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NormalizedAmountIRR = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proposals_Bidders_BidderId",
                        column: x => x.BidderId,
                        principalTable: "Bidders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Proposals_Currencies_ForeignCurrencyId",
                        column: x => x.ForeignCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proposals_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "DefaultFxRateToIRR", "IsActive", "IsDefault", "Name", "SortOrder", "Symbol" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "IRR", null, true, true, "Iranian Rial", 1, "﷼" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "USD", null, true, false, "US Dollar", 2, "$" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "EUR", null, true, false, "Euro", 3, "€" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "AED", null, true, false, "UAE Dirham", 4, "AED" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bidders_CompanyId",
                table: "Bidders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_BidderId",
                table: "EvaluationResults",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_TenderId",
                table: "EvaluationResults",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_BaseCurrencyId",
                table: "FxRates",
                column: "BaseCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_QuoteCurrencyId",
                table: "FxRates",
                column: "QuoteCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_BidderId",
                table: "Proposals",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_ForeignCurrencyId",
                table: "Proposals",
                column: "ForeignCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_TenderId",
                table: "Proposals",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_CompanyId",
                table: "Tenders",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationResults");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "Proposals");

            migrationBuilder.DropTable(
                name: "Bidders");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "Tenders");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}

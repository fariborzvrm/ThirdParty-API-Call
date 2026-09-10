using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InquiryService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CacheKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestPayload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ServingProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsBusinessError = table.Column<bool>(type: "bit", nullable: false),
                    BusinessErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BusinessErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TechnicalError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inquiries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InquiryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AttemptOrder = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutcomeDetail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ElapsedMs = table.Column<long>(type: "bigint", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderAttempts_Inquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "Inquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_CacheKey",
                table: "Inquiries",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderAttempts_InquiryId",
                table: "ProviderAttempts",
                column: "InquiryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderAttempts");

            migrationBuilder.DropTable(
                name: "Inquiries");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cuentitas.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecurringAndInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentCount",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallmentGroup",
                table: "Transactions",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentNumber",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurringItemId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    VariableAmount = table.Column<bool>(type: "INTEGER", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemindDaysBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringItems", x => x.Id);
                    table.CheckConstraint("CK_Recurring_Amount", "EstimatedAmount >= 0");
                    table.ForeignKey(
                        name: "FK_RecurringItems_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringItems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSkips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecurringItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSkips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSkips_RecurringItems_RecurringItemId",
                        column: x => x.RecurringItemId,
                        principalTable: "RecurringItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_InstallmentGroup",
                table: "Transactions",
                column: "InstallmentGroup");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringItemId_DueDate",
                table: "Transactions",
                columns: new[] { "RecurringItemId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_AccountId",
                table: "RecurringItems",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_CategoryId",
                table: "RecurringItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSkips_RecurringItemId_DueDate",
                table: "RecurringSkips",
                columns: new[] { "RecurringItemId", "DueDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_RecurringItems_RecurringItemId",
                table: "Transactions",
                column: "RecurringItemId",
                principalTable: "RecurringItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_RecurringItems_RecurringItemId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "RecurringSkips");

            migrationBuilder.DropTable(
                name: "RecurringItems");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_InstallmentGroup",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringItemId_DueDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstallmentCount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstallmentGroup",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstallmentNumber",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringItemId",
                table: "Transactions");
        }
    }
}

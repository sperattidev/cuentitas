using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cuentitas.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Goals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Color = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TargetAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Tracking = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    AdjustForInflation = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlannedMonthly = table.Column<long>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.CheckConstraint("CK_Goal_Target", "TargetAmount > 0");
                    table.ForeignKey(
                        name: "FK_Goals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GoalContributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoalId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalContributions_Goals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalContributions_GoalId",
                table: "GoalContributions",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_AccountId",
                table: "Goals",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalContributions");

            migrationBuilder.DropTable(
                name: "Goals");
        }
    }
}

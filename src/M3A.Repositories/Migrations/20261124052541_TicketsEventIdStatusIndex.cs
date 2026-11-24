using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace M3A.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class TicketsEventIdStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_EventId",
                table: "tickets");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_EventId_Status",
                table: "tickets",
                columns: new[] { "EventId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_EventId_Status",
                table: "tickets");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_EventId",
                table: "tickets",
                column: "EventId");
        }
    }
}

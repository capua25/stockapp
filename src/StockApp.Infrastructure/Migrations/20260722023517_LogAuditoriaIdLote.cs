using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogAuditoriaIdLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdLote",
                table: "LogsAuditoria",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogsAuditoria_IdLote",
                table: "LogsAuditoria",
                column: "IdLote");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogsAuditoria_IdLote",
                table: "LogsAuditoria");

            migrationBuilder.DropColumn(
                name: "IdLote",
                table: "LogsAuditoria");
        }
    }
}

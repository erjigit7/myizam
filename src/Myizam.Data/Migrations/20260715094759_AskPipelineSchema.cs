using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Myizam.Data.Migrations
{
    /// <inheritdoc />
    public partial class AskPipelineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_query_log_client_hash",
                table: "query_log");

            migrationBuilder.DropColumn(
                name: "sources",
                table: "query_log");

            migrationBuilder.RenameColumn(
                name: "ts",
                table: "query_log",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "lang_detected",
                table: "query_log",
                newName: "guard_model");

            migrationBuilder.RenameColumn(
                name: "guard_mode",
                table: "query_log",
                newName: "answer_lang");

            migrationBuilder.RenameIndex(
                name: "IX_query_log_ts",
                table: "query_log",
                newName: "IX_query_log_created_at");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_usd",
                table: "query_log",
                type: "numeric(8,5)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,6)");

            migrationBuilder.AddColumn<long[]>(
                name: "cited_chunk_ids",
                table: "query_log",
                type: "bigint[]",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "guard_grounded",
                table: "query_log",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "question_lang",
                table: "query_log",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "laws",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "article_title",
                table: "chunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "chapter",
                table: "chunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "section",
                table: "chunks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "answer_cache",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    answer_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer_cache", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_query_log_client_hash_created_at",
                table: "query_log",
                columns: new[] { "client_hash", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_cache");

            migrationBuilder.DropIndex(
                name: "IX_query_log_client_hash_created_at",
                table: "query_log");

            migrationBuilder.DropColumn(
                name: "cited_chunk_ids",
                table: "query_log");

            migrationBuilder.DropColumn(
                name: "guard_grounded",
                table: "query_log");

            migrationBuilder.DropColumn(
                name: "question_lang",
                table: "query_log");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "laws");

            migrationBuilder.DropColumn(
                name: "article_title",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "chapter",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "section",
                table: "chunks");

            migrationBuilder.RenameColumn(
                name: "guard_model",
                table: "query_log",
                newName: "lang_detected");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "query_log",
                newName: "ts");

            migrationBuilder.RenameColumn(
                name: "answer_lang",
                table: "query_log",
                newName: "guard_mode");

            migrationBuilder.RenameIndex(
                name: "IX_query_log_created_at",
                table: "query_log",
                newName: "IX_query_log_ts");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_usd",
                table: "query_log",
                type: "numeric(10,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,5)");

            migrationBuilder.AddColumn<string>(
                name: "sources",
                table: "query_log",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_query_log_client_hash",
                table: "query_log",
                column: "client_hash");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Myizam.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "embedding_cache",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_cache", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "laws",
                columns: table => new
                {
                    document_code = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    official_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<string>(type: "text", nullable: false),
                    edition_date = table.Column<DateOnly>(type: "date", nullable: false),
                    edition_id = table.Column<long>(type: "bigint", nullable: false),
                    article_count = table.Column<int>(type: "integer", nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laws", x => x.document_code);
                });

            migrationBuilder.CreateTable(
                name: "query_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_hash = table.Column<string>(type: "text", nullable: false),
                    lang_detected = table.Column<string>(type: "text", nullable: true),
                    question = table.Column<string>(type: "text", nullable: false),
                    question_ru = table.Column<string>(type: "text", nullable: true),
                    answer = table.Column<string>(type: "text", nullable: true),
                    sources = table.Column<string>(type: "jsonb", nullable: true),
                    top_similarity = table.Column<double>(type: "double precision", nullable: true),
                    guard_score = table.Column<double>(type: "double precision", nullable: true),
                    guard_mode = table.Column<string>(type: "text", nullable: true),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false),
                    refused = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_query_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chunks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    law_code = table.Column<string>(type: "text", nullable: false),
                    article_number = table.Column<string>(type: "text", nullable: true),
                    part = table.Column<int>(type: "integer", nullable: false),
                    part_count = table.Column<int>(type: "integer", nullable: false),
                    header = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    lang = table.Column<string>(type: "text", nullable: false, defaultValue: "ru"),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_chunks_laws_law_code",
                        column: x => x.law_code,
                        principalTable: "laws",
                        principalColumn: "document_code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chunks_content_hash",
                table: "chunks",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chunks_law_code_article_number",
                table: "chunks",
                columns: new[] { "law_code", "article_number" });

            migrationBuilder.CreateIndex(
                name: "IX_query_log_client_hash",
                table: "query_log",
                column: "client_hash");

            migrationBuilder.CreateIndex(
                name: "IX_query_log_ts",
                table: "query_log",
                column: "ts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunks");

            migrationBuilder.DropTable(
                name: "embedding_cache");

            migrationBuilder.DropTable(
                name: "query_log");

            migrationBuilder.DropTable(
                name: "laws");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Myizam.Data;

public sealed class MyizamDbContext : DbContext
{
    /// <summary>
    /// Размерность вектора зашивается в схему на этапе генерации миграции.
    /// Меняется через env EMBEDDING_DIM (1024 = bge-m3, 1536 = text-embedding-3-small);
    /// смена размерности = новая миграция + полная переиндексация (ТЗ v2.1 §1.1).
    /// </summary>
    public static int EmbeddingDim { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("EMBEDDING_DIM"), out var d) ? d : 1024;

    public DbSet<LawEntity> Laws => Set<LawEntity>();
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();
    public DbSet<EmbeddingCacheEntity> EmbeddingCache => Set<EmbeddingCacheEntity>();
    public DbSet<QueryLogEntity> QueryLog => Set<QueryLogEntity>();

    public MyizamDbContext(DbContextOptions<MyizamDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.HasPostgresExtension("vector");

        mb.Entity<LawEntity>(e =>
        {
            e.ToTable("laws");
            e.HasKey(x => x.DocumentCode);
            e.Property(x => x.DocumentCode).HasColumnName("document_code");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.OfficialName).HasColumnName("official_name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.StatusCode).HasColumnName("status_code");
            e.Property(x => x.EditionDate).HasColumnName("edition_date");
            e.Property(x => x.EditionId).HasColumnName("edition_id");
            e.Property(x => x.ArticleCount).HasColumnName("article_count");
            e.Property(x => x.IngestedAt).HasColumnName("ingested_at");
        });

        mb.Entity<ChunkEntity>(e =>
        {
            e.ToTable("chunks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LawCode).HasColumnName("law_code");
            e.Property(x => x.ArticleNumber).HasColumnName("article_number");
            e.Property(x => x.Part).HasColumnName("part");
            e.Property(x => x.PartCount).HasColumnName("part_count");
            e.Property(x => x.Header).HasColumnName("header");
            e.Property(x => x.Text).HasColumnName("text");
            e.Property(x => x.ContentHash).HasColumnName("content_hash");
            e.Property(x => x.Lang).HasColumnName("lang").HasDefaultValue("ru");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType($"vector({EmbeddingDim})");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.ContentHash).IsUnique();
            e.HasIndex(x => new { x.LawCode, x.ArticleNumber });
            e.HasOne(x => x.Law).WithMany(l => l.Chunks).HasForeignKey(x => x.LawCode);
        });

        mb.Entity<EmbeddingCacheEntity>(e =>
        {
            e.ToTable("embedding_cache");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType($"vector({EmbeddingDim})");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<QueryLogEntity>(e =>
        {
            e.ToTable("query_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Ts).HasColumnName("ts");
            e.Property(x => x.ClientHash).HasColumnName("client_hash");
            e.Property(x => x.LangDetected).HasColumnName("lang_detected");
            e.Property(x => x.Question).HasColumnName("question");
            e.Property(x => x.QuestionRu).HasColumnName("question_ru");
            e.Property(x => x.Answer).HasColumnName("answer");
            e.Property(x => x.SourcesJson).HasColumnName("sources").HasColumnType("jsonb");
            e.Property(x => x.TopSimilarity).HasColumnName("top_similarity");
            e.Property(x => x.GuardScore).HasColumnName("guard_score");
            e.Property(x => x.GuardMode).HasColumnName("guard_mode");
            e.Property(x => x.TokensIn).HasColumnName("tokens_in");
            e.Property(x => x.TokensOut).HasColumnName("tokens_out");
            e.Property(x => x.CostUsd).HasColumnName("cost_usd").HasColumnType("numeric(10,6)");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.CacheHit).HasColumnName("cache_hit");
            e.Property(x => x.Refused).HasColumnName("refused");
            e.Property(x => x.Error).HasColumnName("error");
            e.HasIndex(x => x.Ts);
            e.HasIndex(x => x.ClientHash);
        });
    }
}

/// <summary>Фабрика для dotnet-ef (генерация миграций без живой БД).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MyizamDbContext>
{
    public MyizamDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? "Host=localhost;Port=5432;Database=myizam;Username=myizam;Password=myizam";
        var options = new DbContextOptionsBuilder<MyizamDbContext>()
            .UseNpgsql(cs, o => o.UseVector())
            .Options;
        return new MyizamDbContext(options);
    }
}

using DocumentManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentManagement.Api.Data;

/// <summary>
/// EF Core database context for document metadata.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();

        document.HasKey(item => item.Id);
        document.Property(item => item.OriginalFileName).HasMaxLength(255).IsRequired();
        document.Property(item => item.StoredFileName).HasMaxLength(100).IsRequired();
        document.HasIndex(item => item.StoredFileName).IsUnique();
        document.Property(item => item.ContentType).HasMaxLength(127).IsRequired();
        document.Property(item => item.FileSize).IsRequired();
        document.Property(item => item.UploadedAt).IsRequired();

        base.OnModelCreating(modelBuilder);
    }
}

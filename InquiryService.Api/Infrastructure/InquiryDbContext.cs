using InquiryService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace InquiryService.Api.Infrastructure;

public sealed class InquiryDbContext(DbContextOptions<InquiryDbContext> options) : DbContext(options)
{
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<ProviderAttempt> ProviderAttempts => Set<ProviderAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var inquiry = modelBuilder.Entity<Inquiry>();
        inquiry.ToTable("Inquiries");
        inquiry.HasKey(i => i.Id);
        inquiry.Property(i => i.CacheKey).HasMaxLength(64).IsRequired();
        inquiry.HasIndex(i => i.CacheKey).IsUnique().HasDatabaseName("IX_Inquiries_CacheKey");
        inquiry.Property(i => i.RequestPayload).HasMaxLength(4000).IsRequired();
        inquiry.Property(i => i.ResultPayload).HasMaxLength(8000);
        inquiry.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        inquiry.Property(i => i.ServingProvider).HasMaxLength(64);
        inquiry.Property(i => i.BusinessErrorCode).HasMaxLength(64);
        inquiry.Property(i => i.BusinessErrorMessage).HasMaxLength(500);
        inquiry.Property(i => i.TechnicalError).HasMaxLength(2000);
        inquiry.HasMany(i => i.Attempts)
            .WithOne(a => a.Inquiry)
            .HasForeignKey(a => a.InquiryId)
            .OnDelete(DeleteBehavior.Cascade);

        var attempt = modelBuilder.Entity<ProviderAttempt>();
        attempt.ToTable("ProviderAttempts");
        attempt.HasKey(a => a.Id);
        attempt.Property(a => a.ProviderName).HasMaxLength(64).IsRequired();
        attempt.Property(a => a.Outcome).HasMaxLength(20).IsRequired();
        attempt.Property(a => a.OutcomeDetail).HasMaxLength(2000);
        attempt.HasIndex(a => a.InquiryId);
    }
}
using Microsoft.EntityFrameworkCore;
using BulkEmailSender.Api.Data.Entities;

namespace BulkEmailSender.Api.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<SendOperationEntity> SendOperations =>
        Set<SendOperationEntity>();

    public DbSet<SendWorkItemEntity> SendWorkItems =>
        Set<SendWorkItemEntity>();
    public DbSet<GoogleOAuthConnectionEntity> GoogleOAuthConnections => Set<GoogleOAuthConnectionEntity>();
    public DbSet<GoogleOAuthStateEntity> GoogleOAuthStates => Set<GoogleOAuthStateEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<GoogleOAuthConnectionEntity>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.HasIndex(x => x.GoogleSubjectId);
            entity.Property(x => x.EmailAddress).IsRequired();
            entity.Property(x => x.AccessTokenProtected).IsRequired();
        });
        modelBuilder.Entity<GoogleOAuthStateEntity>(entity =>
        {
            entity.HasKey(x => x.StateHash);
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        modelBuilder.Entity<SendOperationEntity>(
            entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Status)
                    .IsRequired();

                entity.Property(x => x.Subject)
                    .IsRequired();

                entity.Property(x => x.Body)
                    .IsRequired();

                entity.Property(x => x.AttachmentsJson)
                    .IsRequired();
                    
                entity.Property(x => x.EmailJson)
                    .IsRequired();

                entity.HasMany(x => x.WorkItems)
                    .WithOne(x => x.Operation)
                    .HasForeignKey(x => x.OperationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<SendWorkItemEntity>(
            entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ValuesJson)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .IsRequired();

                entity.HasIndex(x => new
                {
                    x.OperationId,
                    x.Status
                });
            });
    }
}

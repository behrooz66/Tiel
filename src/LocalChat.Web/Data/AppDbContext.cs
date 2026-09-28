using LocalChat.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LocalChat.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Model> Models => Set<Model>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<MessageRole>().HaveConversion<string>();
        configurationBuilder.Properties<MessageStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(project =>
        {
            project.Property(p => p.Id).ValueGeneratedNever();
            project.Property(p => p.Name).HasMaxLength(100).UseCollation("NOCASE");
            project.Property(p => p.Description).HasMaxLength(500);
            project.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.Property(c => c.Id).ValueGeneratedNever();
            conversation.Property(c => c.Title).HasMaxLength(200);
            conversation.HasOne(c => c.Project)
                .WithMany(p => p.Conversations)
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            conversation.HasOne(c => c.Model)
                .WithMany()
                .HasForeignKey(c => c.ModelId)
                .OnDelete(DeleteBehavior.Restrict);
            conversation.HasIndex(c => new { c.ProjectId, c.UpdatedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<Message>(message =>
        {
            message.Property(m => m.Id).ValueGeneratedNever();
            message.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            message.HasOne(m => m.Model)
                .WithMany()
                .HasForeignKey(m => m.ModelId)
                .OnDelete(DeleteBehavior.Restrict);
            message.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();
        });

        modelBuilder.Entity<Model>(model =>
        {
            model.Property(m => m.Id).ValueGeneratedNever();
            model.Property(m => m.DisplayName).HasMaxLength(100);
            model.HasIndex(m => m.Tag).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(setting =>
        {
            setting.Property(s => s.Key).HasMaxLength(100);
            setting.HasIndex(s => s.Key).IsUnique();
        });
    }

    /// <summary>Stores UTC and reads values back with <see cref="DateTimeKind.Utc"/>; SQLite keeps no kind.</summary>
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

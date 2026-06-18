using Aethernet.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.Data;

public class AethernetDbContext : DbContext
{
    public AethernetDbContext(DbContextOptions<AethernetDbContext> options) : base(options) { }

    public DbSet<UserEntity>          Users          => Set<UserEntity>();
    public DbSet<PairEntity>          Pairs          => Set<PairEntity>();
    public DbSet<GroupEntity>         Groups         => Set<GroupEntity>();
    public DbSet<GroupPairEntity>     GroupPairs     => Set<GroupPairEntity>();
    public DbSet<GroupBanEntity>      GroupBans      => Set<GroupBanEntity>();
    public DbSet<FileCacheEntity>     FileCache      => Set<FileCacheEntity>();
    public DbSet<RefreshTokenEntity>  RefreshTokens  => Set<RefreshTokenEntity>();
    public DbSet<ProfileReportEntity> ProfileReports => Set<ProfileReportEntity>();
    public DbSet<AuditLogEntity>      AuditLog       => Set<AuditLogEntity>();
    public DbSet<BannedUserEntity>    BannedUsers    => Set<BannedUserEntity>();
    public DbSet<BlockEntity>         Blocks         => Set<BlockEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<UserEntity>(e =>
        {
            e.HasIndex(x => x.Alias).IsUnique();
            e.HasIndex(x => x.DiscordUserId);
            e.HasIndex(x => x.LastSeenAt);
        });

        b.Entity<PairEntity>(e =>
        {
            e.HasIndex(x => new { x.OwnerUid, x.OtherUid }).IsUnique();
            e.HasIndex(x => x.OtherUid);
            e.HasOne(x => x.Owner)
                .WithMany(u => u.InitiatedPairs)
                .HasForeignKey(x => x.OwnerUid)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Other)
                .WithMany(u => u.ReceivedPairs)
                .HasForeignKey(x => x.OtherUid)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GroupEntity>(e =>
        {
            e.HasIndex(x => x.Alias).IsUnique();
            e.HasIndex(x => x.OwnerUid);
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUid)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<GroupPairEntity>(e =>
        {
            e.HasIndex(x => new { x.Gid, x.Uid }).IsUnique();
            e.HasIndex(x => x.Uid);
            e.HasOne(x => x.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(x => x.Gid)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(x => x.Uid)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GroupBanEntity>(e =>
        {
            e.HasIndex(x => new { x.Gid, x.UidBanned }).IsUnique();
            e.HasOne(x => x.Group)
                .WithMany(g => g.Bans)
                .HasForeignKey(x => x.Gid)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FileCacheEntity>(e =>
        {
            e.HasIndex(x => x.FirstUploaderUid);
            e.HasIndex(x => x.OrphanedAt);
        });

        b.Entity<RefreshTokenEntity>(e =>
        {
            e.HasIndex(x => x.Uid);
            e.HasIndex(x => x.ExpiresAt);
        });

        b.Entity<ProfileReportEntity>(e =>
        {
            e.HasIndex(x => x.ReportedUid);
            e.HasIndex(x => x.Resolved);
        });

        b.Entity<AuditLogEntity>(e =>
        {
            e.HasIndex(x => x.ActorUid);
            e.HasIndex(x => x.Action);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<BlockEntity>(e =>
        {
            e.HasIndex(x => new { x.OwnerUid, x.OtherUid }).IsUnique();
            e.HasIndex(x => x.OtherUid);
        });
    }
}

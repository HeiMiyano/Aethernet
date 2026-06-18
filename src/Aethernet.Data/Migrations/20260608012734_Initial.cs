using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aethernet.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TargetGid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BannedUsers",
                columns: table => new
                {
                    Uid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    BannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedUsers", x => x.Uid);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OtherUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileCache",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReferenceCount = table.Column<long>(type: "bigint", nullable: false),
                    FirstUploaderUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTouchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrphanedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    IsForbidden = table.Column<bool>(type: "boolean", nullable: false),
                    ForbiddenReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileCache", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "ProfileReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReporterUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReportedUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolutionNote = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    TokenId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    Uid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.TokenId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Uid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SecretKeyHash = table.Column<string>(type: "text", nullable: false),
                    RecoverySecretHash = table.Column<string>(type: "text", nullable: true),
                    DiscordUserId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IsModerator = table.Column<bool>(type: "boolean", nullable: false),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false),
                    BanReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProfileDescription = table.Column<string>(type: "text", nullable: true),
                    ProfilePictureBase64 = table.Column<string>(type: "text", nullable: true),
                    ProfileIsNsfw = table.Column<bool>(type: "boolean", nullable: false),
                    ProfileIsFlagged = table.Column<bool>(type: "boolean", nullable: false),
                    FileQuotaBytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Uid);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Gid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnerUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Permissions = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    DefaultUserPermissions = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    MemberLimit = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PasswordRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Gid);
                    table.ForeignKey(
                        name: "FK_Groups_Users_OwnerUid",
                        column: x => x.OwnerUid,
                        principalTable: "Users",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pairs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OtherUid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnPermissions = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pairs_Users_OtherUid",
                        column: x => x.OtherUid,
                        principalTable: "Users",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Pairs_Users_OwnerUid",
                        column: x => x.OwnerUid,
                        principalTable: "Users",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupBans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Gid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UidBanned = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UidBannedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    BannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupBans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupBans_Groups_Gid",
                        column: x => x.Gid,
                        principalTable: "Groups",
                        principalColumn: "Gid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupPairs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Gid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Uid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserPermissions = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Role = table.Column<byte>(type: "smallint", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupPairs_Groups_Gid",
                        column: x => x.Gid,
                        principalTable: "Groups",
                        principalColumn: "Gid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupPairs_Users_Uid",
                        column: x => x.Uid,
                        principalTable: "Users",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Action",
                table: "AuditLog",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorUid",
                table: "AuditLog",
                column: "ActorUid");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_CreatedAt",
                table: "AuditLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_OtherUid",
                table: "Blocks",
                column: "OtherUid");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_OwnerUid_OtherUid",
                table: "Blocks",
                columns: new[] { "OwnerUid", "OtherUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileCache_FirstUploaderUid",
                table: "FileCache",
                column: "FirstUploaderUid");

            migrationBuilder.CreateIndex(
                name: "IX_FileCache_OrphanedAt",
                table: "FileCache",
                column: "OrphanedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GroupBans_Gid_UidBanned",
                table: "GroupBans",
                columns: new[] { "Gid", "UidBanned" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPairs_Gid_Uid",
                table: "GroupPairs",
                columns: new[] { "Gid", "Uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPairs_Uid",
                table: "GroupPairs",
                column: "Uid");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Alias",
                table: "Groups",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerUid",
                table: "Groups",
                column: "OwnerUid");

            migrationBuilder.CreateIndex(
                name: "IX_Pairs_OtherUid",
                table: "Pairs",
                column: "OtherUid");

            migrationBuilder.CreateIndex(
                name: "IX_Pairs_OwnerUid_OtherUid",
                table: "Pairs",
                columns: new[] { "OwnerUid", "OtherUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReports_ReportedUid",
                table: "ProfileReports",
                column: "ReportedUid");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReports_Resolved",
                table: "ProfileReports",
                column: "Resolved");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Uid",
                table: "RefreshTokens",
                column: "Uid");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Alias",
                table: "Users",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DiscordUserId",
                table: "Users",
                column: "DiscordUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastSeenAt",
                table: "Users",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BannedUsers");

            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropTable(
                name: "FileCache");

            migrationBuilder.DropTable(
                name: "GroupBans");

            migrationBuilder.DropTable(
                name: "GroupPairs");

            migrationBuilder.DropTable(
                name: "Pairs");

            migrationBuilder.DropTable(
                name: "ProfileReports");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

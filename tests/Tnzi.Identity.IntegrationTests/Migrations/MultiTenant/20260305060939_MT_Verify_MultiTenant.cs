using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tnzi.Identity.IntegrationTests.Migrations.MultiTenant
{
    /// <inheritdoc />
    public partial class MT_Verify_MultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Identity_LoginLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_LoginLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Identity_Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Remark = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeleterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_Organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_Organizations_Identity_Organizations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Identity_Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Identity_PasskeyData",
                columns: table => new
                {
                    PublicKey = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SignCount = table.Column<uint>(type: "INTEGER", nullable: false),
                    Transports = table.Column<string>(type: "TEXT", nullable: true),
                    IsUserVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBackupEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttestationObject = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ClientDataJson = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "Identity_Role",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Identity_Tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ExpiredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Remark = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeleterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_Tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EventData = table.Column<string>(type: "TEXT", nullable: false),
                    EventTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsProcessed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValueSql: "0"),
                    ProcessedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Identity_User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_User", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_User_Identity_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Identity_Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Identity_RoleClaim",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_RoleClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_RoleClaim_Identity_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Identity_Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_AuthTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeleterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_AuthTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_AuthTokens_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_PasswordHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_PasswordHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_PasswordHistories_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_TwoFactorCode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_TwoFactorCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_TwoFactorCode_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserClaim",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_UserClaim_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AvatarId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    Birthday = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Bio = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeleterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_UserDetails_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserLogin",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserLogin", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_Identity_UserLogin_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserRole",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserRole", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_Identity_UserRole_Identity_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Identity_Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Identity_UserRole_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceInfo = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identity_UserSessions_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Identity_UserToken",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identity_UserToken", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_Identity_UserToken_Identity_User_UserId",
                        column: x => x.UserId,
                        principalTable: "Identity_User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Identity_AuthTokens_UserId_LoginProvider_Name",
                table: "Identity_AuthTokens",
                columns: new[] { "UserId", "LoginProvider", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identity_LoginLogs_CreationTime",
                table: "Identity_LoginLogs",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_LoginLogs_Status",
                table: "Identity_LoginLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_LoginLogs_UserId",
                table: "Identity_LoginLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Organizations_ParentId",
                table: "Identity_Organizations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Organizations_Path",
                table: "Identity_Organizations",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Organizations_TenantId",
                table: "Identity_Organizations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Organizations_TenantId_Code",
                table: "Identity_Organizations",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_PasswordHistories_UserId_CreationTime",
                table: "Identity_PasswordHistories",
                columns: new[] { "UserId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Role_TenantId",
                table: "Identity_Role",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Identity_Role",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "RoleTenantNameIndex",
                table: "Identity_Role",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identity_RoleClaim_RoleId",
                table: "Identity_RoleClaim",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Tenant_Code",
                table: "Identity_Tenant",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Tenant_ExpiredAt",
                table: "Identity_Tenant",
                column: "ExpiredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Tenant_IsEnabled",
                table: "Identity_Tenant",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_Tenant_Name",
                table: "Identity_Tenant",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_TwoFactorCode_Code",
                table: "Identity_TwoFactorCode",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_TwoFactorCode_ExpiresAt",
                table: "Identity_TwoFactorCode",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_TwoFactorCode_UserId_Type_CreationTime",
                table: "Identity_TwoFactorCode",
                columns: new[] { "UserId", "Type", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TwoFactorCode_Address_Type_CreationTime",
                table: "Identity_TwoFactorCode",
                columns: new[] { "Address", "Type", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Identity_User",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_User_Email",
                table: "Identity_User",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_User_OrganizationId",
                table: "Identity_User",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_User_PhoneNumber",
                table: "Identity_User",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_User_TenantId",
                table: "Identity_User",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Identity_User",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserClaim_UserId",
                table: "Identity_UserClaim",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserDetails_TenantId",
                table: "Identity_UserDetails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserDetails_UserId",
                table: "Identity_UserDetails",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserLogin_UserId",
                table: "Identity_UserLogin",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserRole_RoleId",
                table: "Identity_UserRole",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserSessions_LastActivityTime",
                table: "Identity_UserSessions",
                column: "LastActivityTime");

            migrationBuilder.CreateIndex(
                name: "IX_Identity_UserSessions_UserId_IsRevoked_CreationTime",
                table: "Identity_UserSessions",
                columns: new[] { "UserId", "IsRevoked", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_IsProcessed_CreationTime",
                table: "OutboxMessage",
                columns: new[] { "IsProcessed", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Identity_AuthTokens");

            migrationBuilder.DropTable(
                name: "Identity_LoginLogs");

            migrationBuilder.DropTable(
                name: "Identity_PasskeyData");

            migrationBuilder.DropTable(
                name: "Identity_PasswordHistories");

            migrationBuilder.DropTable(
                name: "Identity_RoleClaim");

            migrationBuilder.DropTable(
                name: "Identity_Tenant");

            migrationBuilder.DropTable(
                name: "Identity_TwoFactorCode");

            migrationBuilder.DropTable(
                name: "Identity_UserClaim");

            migrationBuilder.DropTable(
                name: "Identity_UserDetails");

            migrationBuilder.DropTable(
                name: "Identity_UserLogin");

            migrationBuilder.DropTable(
                name: "Identity_UserRole");

            migrationBuilder.DropTable(
                name: "Identity_UserSessions");

            migrationBuilder.DropTable(
                name: "Identity_UserToken");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "Identity_Role");

            migrationBuilder.DropTable(
                name: "Identity_User");

            migrationBuilder.DropTable(
                name: "Identity_Organizations");
        }
    }
}

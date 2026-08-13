using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwordMaster.Host.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerEquipmentGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sm_player_equipment_grants",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnedItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConfiguredItemId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GrantedByAdministratorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    GrantReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantIdempotencyKey = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GrantRequestFingerprint = table.Column<string>(type: "char(64)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    GrantResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    GrantIpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantUserAgent = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantCorrelationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RolledBackByAdministratorId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RolledBackAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RollbackReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollbackIdempotencyKey = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RollbackRequestFingerprint = table.Column<string>(type: "char(64)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    RollbackResultingRevision = table.Column<long>(type: "bigint", nullable: true),
                    RollbackIpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollbackUserAgent = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollbackCorrelationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_player_equipment_grants", x => x.GrantId);
                    table.CheckConstraint("ck_sm_player_equipment_grants_grant_revision", "`GrantResultingRevision` > 0");
                    table.CheckConstraint("ck_sm_player_equipment_grants_rollback_complete", "((`Status` = 1 AND `RolledBackByAdministratorId` IS NULL AND `RolledBackAtUtc` IS NULL AND `RollbackReason` IS NULL AND `RollbackIdempotencyKey` IS NULL AND `RollbackRequestFingerprint` IS NULL AND `RollbackResultingRevision` IS NULL AND `RollbackIpAddress` IS NULL AND `RollbackUserAgent` IS NULL AND `RollbackCorrelationId` IS NULL) OR (`Status` = 2 AND `RolledBackByAdministratorId` IS NOT NULL AND `RolledBackAtUtc` IS NOT NULL AND `RollbackReason` IS NOT NULL AND `RollbackIdempotencyKey` IS NOT NULL AND `RollbackRequestFingerprint` IS NOT NULL AND `RollbackResultingRevision` IS NOT NULL))");
                    table.CheckConstraint("ck_sm_player_equipment_grants_rollback_revision", "`RollbackResultingRevision` IS NULL OR `RollbackResultingRevision` > 0");
                    table.CheckConstraint("ck_sm_player_equipment_grants_rollback_time", "`RolledBackAtUtc` IS NULL OR `RolledBackAtUtc` >= `GrantedAtUtc`");
                    table.CheckConstraint("ck_sm_player_equipment_grants_status", "`Status` IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_sm_player_equipment_grants_player",
                        column: x => x.PlayerId,
                        principalTable: "sm_player_assets_profiles",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_sm_player_equipment_grants_player_granted",
                table: "sm_player_equipment_grants",
                columns: new[] { "PlayerId", "GrantedAtUtc", "GrantId" });

            migrationBuilder.CreateIndex(
                name: "ux_sm_player_equipment_grants_player_grant_key",
                table: "sm_player_equipment_grants",
                columns: new[] { "PlayerId", "GrantIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sm_player_equipment_grants_player_item",
                table: "sm_player_equipment_grants",
                columns: new[] { "PlayerId", "OwnedItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sm_player_equipment_grants_player_rollback_key",
                table: "sm_player_equipment_grants",
                columns: new[] { "PlayerId", "RollbackIdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sm_player_equipment_grants");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwordMaster.Host.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlayerAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_player_asset_command_receipts",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IdempotencyKey = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequestFingerprint = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_player_asset_command_receipts", x => new { x.PlayerId, x.IdempotencyKey });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_player_assets_profiles",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_player_assets_profiles", x => x.PlayerId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_equipment_assignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CharacterId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slot = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnedItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConfiguredItemId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigurationVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModifierSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_equipment_assignments", x => new { x.PlayerId, x.AssignmentId });
                    table.ForeignKey(
                        name: "FK_sm_equipment_assignments_sm_player_assets_profiles_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "sm_player_assets_profiles",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_owned_equipment_items",
                columns: table => new
                {
                    OwnedItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConfiguredItemId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_owned_equipment_items", x => new { x.PlayerId, x.OwnedItemId });
                    table.ForeignKey(
                        name: "FK_sm_owned_equipment_items_sm_player_assets_profiles_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "sm_player_assets_profiles",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_player_characters",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CharacterId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_player_characters", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_sm_player_characters_sm_player_assets_profiles_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "sm_player_assets_profiles",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_player_character_base_attributes",
                columns: table => new
                {
                    StatCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_0900_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlayerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StatValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_player_character_base_attributes", x => new { x.PlayerId, x.StatCode });
                    table.ForeignKey(
                        name: "FK_sm_player_character_base_attributes_sm_player_characters_Pla~",
                        column: x => x.PlayerId,
                        principalTable: "sm_player_characters",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_sm_equipment_assignments_player_character",
                table: "sm_equipment_assignments",
                columns: new[] { "PlayerId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "ux_sm_equipment_assignments_player_item",
                table: "sm_equipment_assignments",
                columns: new[] { "PlayerId", "OwnedItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sm_equipment_assignments_player_slot",
                table: "sm_equipment_assignments",
                columns: new[] { "PlayerId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sm_player_characters_character_id",
                table: "sm_player_characters",
                column: "CharacterId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sm_equipment_assignments");

            migrationBuilder.DropTable(
                name: "sm_owned_equipment_items");

            migrationBuilder.DropTable(
                name: "sm_player_asset_command_receipts");

            migrationBuilder.DropTable(
                name: "sm_player_character_base_attributes");

            migrationBuilder.DropTable(
                name: "sm_player_characters");

            migrationBuilder.DropTable(
                name: "sm_player_assets_profiles");
        }
    }
}

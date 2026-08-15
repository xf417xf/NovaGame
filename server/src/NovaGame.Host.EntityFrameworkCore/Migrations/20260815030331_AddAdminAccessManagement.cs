using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaGame.Host.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAccessManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_admin_role_memberships_role_admin",
                table: "admin_role_memberships",
                columns: new[] { "RoleId", "AdministratorId" });

            migrationBuilder.DropIndex(
                name: "ix_admin_role_memberships_role",
                table: "admin_role_memberships");

            migrationBuilder.DropIndex(
                name: "ix_admin_refresh_sessions_family_revoked",
                table: "admin_refresh_token_sessions");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_action_time",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_admin_time",
                table: "admin_audit_records");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "admin_roles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "admin_roles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "admin_roles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "admin_roles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "admin_roles",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "admin_role_permissions",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "PermissionName",
                table: "admin_audit_records",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "admin_audit_records",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "admin_audit_records",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(48)",
                oldMaxLength: 48)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "SessionFamilyId",
                table: "admin_audit_records",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "TargetRevision",
                table: "admin_audit_records",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUserName",
                table: "admin_administrators",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "admin_administrators",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<sbyte>(
                name: "BuiltInRoleSlot",
                table: "admin_roles",
                type: "tinyint",
                nullable: true,
                computedColumnSql: "CASE WHEN `IsBuiltIn` = 1 THEN 1 ELSE NULL END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "admin_access_command_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActorAdministratorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CommandKind = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin"),
                    RequestFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    TargetResourceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_access_command_receipts", x => x.Id);
                    table.CheckConstraint("ck_admin_access_receipts_command_kind", "`CommandKind` IN ('AdministratorCreate', 'AdminRoleCreate', 'AdministratorPasswordReset')");
                    table.CheckConstraint("ck_admin_access_receipts_revision", "`ResultingRevision` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_admin_roles_active_list",
                table: "admin_roles",
                columns: new[] { "IsActive", "IsBuiltIn", "NormalizedName", "Id" },
                descending: new[] { false, true, false, false });

            migrationBuilder.CreateIndex(
                name: "ix_admin_roles_list",
                table: "admin_roles",
                columns: new[] { "IsBuiltIn", "NormalizedName", "Id" },
                descending: new[] { true, false, false });

            migrationBuilder.CreateIndex(
                name: "ux_admin_roles_built_in_slot",
                table: "admin_roles",
                column: "BuiltInRoleSlot",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_roles_built_in_identity",
                table: "admin_roles",
                sql: "`IsBuiltIn` = 0 OR (`IsActive` = 1 AND BINARY `Name` = BINARY 'Administrators' AND BINARY `NormalizedName` = BINARY 'ADMINISTRATORS')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_roles_revision",
                table: "admin_roles",
                sql: "`Revision` > 0");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_sessions_admin_family_order",
                table: "admin_refresh_token_sessions",
                columns: new[] { "AdministratorId", "FamilyId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_sessions_family_order",
                table: "admin_refresh_token_sessions",
                columns: new[] { "FamilyId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_sessions_family_replacement",
                table: "admin_refresh_token_sessions",
                columns: new[] { "FamilyId", "ReplacedBySessionId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_refresh_sessions_revocation",
                table: "admin_refresh_token_sessions",
                sql: "(`RevokedAtUtc` IS NULL AND `RevocationReason` IS NULL AND `ReplacedBySessionId` IS NULL) OR (`RevokedAtUtc` IS NOT NULL AND `RevocationReason` IS NOT NULL AND ((`RevocationReason` = 'Rotated' AND `ReplacedBySessionId` IS NOT NULL) OR (`RevocationReason` <> 'Rotated' AND `ReplacedBySessionId` IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_action_time_id",
                table: "admin_audit_records",
                columns: new[] { "Action", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_actor_time_id",
                table: "admin_audit_records",
                columns: new[] { "ActorAdministratorId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_correlation_time_id",
                table: "admin_audit_records",
                columns: new[] { "CorrelationId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_role_time_id",
                table: "admin_audit_records",
                columns: new[] { "RoleId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_subject_time_id",
                table: "admin_audit_records",
                columns: new[] { "AdministratorId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_success_time_id",
                table: "admin_audit_records",
                columns: new[] { "Succeeded", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_time_id",
                table: "admin_audit_records",
                columns: new[] { "OccurredAtUtc", "Id" },
                descending: new bool[0]);

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_audit_records_target_revision",
                table: "admin_audit_records",
                sql: "`TargetRevision` IS NULL OR `TargetRevision` > 0");

            migrationBuilder.CreateIndex(
                name: "ix_admin_administrators_active_name_id",
                table: "admin_administrators",
                columns: new[] { "IsActive", "NormalizedUserName", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_administrators_revision",
                table: "admin_administrators",
                sql: "`Revision` > 0");

            migrationBuilder.CreateIndex(
                name: "ux_admin_access_receipts_actor_kind_key",
                table: "admin_access_command_receipts",
                columns: new[] { "ActorAdministratorId", "CommandKind", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_access_command_receipts");

            migrationBuilder.CreateIndex(
                name: "ix_admin_role_memberships_role",
                table: "admin_role_memberships",
                column: "RoleId");

            migrationBuilder.DropIndex(
                name: "ix_admin_roles_active_list",
                table: "admin_roles");

            migrationBuilder.DropIndex(
                name: "ix_admin_roles_list",
                table: "admin_roles");

            migrationBuilder.DropIndex(
                name: "ux_admin_roles_built_in_slot",
                table: "admin_roles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_roles_built_in_identity",
                table: "admin_roles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_roles_revision",
                table: "admin_roles");

            migrationBuilder.DropIndex(
                name: "ix_admin_role_memberships_role_admin",
                table: "admin_role_memberships");

            migrationBuilder.DropIndex(
                name: "ix_admin_refresh_sessions_admin_family_order",
                table: "admin_refresh_token_sessions");

            migrationBuilder.DropIndex(
                name: "ix_admin_refresh_sessions_family_order",
                table: "admin_refresh_token_sessions");

            migrationBuilder.DropIndex(
                name: "ix_admin_refresh_sessions_family_replacement",
                table: "admin_refresh_token_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_refresh_sessions_revocation",
                table: "admin_refresh_token_sessions");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_action_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_actor_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_correlation_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_role_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_subject_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_success_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_audit_records_time_id",
                table: "admin_audit_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_audit_records_target_revision",
                table: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ix_admin_administrators_active_name_id",
                table: "admin_administrators");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_administrators_revision",
                table: "admin_administrators");

            migrationBuilder.DropColumn(
                name: "BuiltInRoleSlot",
                table: "admin_roles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "admin_roles");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "admin_roles");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "admin_roles");

            migrationBuilder.DropColumn(
                name: "SessionFamilyId",
                table: "admin_audit_records");

            migrationBuilder.DropColumn(
                name: "TargetRevision",
                table: "admin_audit_records");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "admin_administrators");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "admin_roles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "admin_roles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "admin_role_permissions",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "PermissionName",
                table: "admin_audit_records",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "admin_audit_records",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "admin_audit_records",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(48)",
                oldMaxLength: 48,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUserName",
                table: "admin_administrators",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_sessions_family_revoked",
                table: "admin_refresh_token_sessions",
                columns: new[] { "FamilyId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_action_time",
                table: "admin_audit_records",
                columns: new[] { "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_admin_time",
                table: "admin_audit_records",
                columns: new[] { "AdministratorId", "OccurredAtUtc" });
        }
    }
}

using System.Globalization;
using MySqlConnector;
using SwordMaster.Application.Contracts.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.RuntimeComposition;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SwordMasterRuntimeDatabaseCollection
{
    public const string Name = "SwordMaster runtime databases";
}

[Collection(SwordMasterRuntimeDatabaseCollection.Name)]
public sealed class SwordMasterRuntimeDatabaseTests
{
    private static readonly Guid SeedPlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeedCharacterId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeedOwnedItemId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly string _novaGameConnectionString;
    private readonly string _swordMasterConnectionString;

    public SwordMasterRuntimeDatabaseTests()
    {
        _novaGameConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING");
        _swordMasterConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING");
    }

    [Fact]
    public async Task MigrationHistoriesAndSchemasContainOnlyTheirOwningObjects()
    {
        var novaGameMigrations = await ReadStringsAsync(
            _novaGameConnectionString,
            "SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId");
        var swordMasterMigrations = await ReadStringsAsync(
            _swordMasterConnectionString,
            "SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId");

        Assert.Equal(
            [
                "20260723160000_InitialMySqlBaseline",
                "20260725101129_AddAdministratorAccess",
                "20260725122154_AddGameConfig"
            ],
            novaGameMigrations);
        Assert.Equal(
            [
                "20260801121309_InitialPlayerAssets",
                "20260810105902_AddPlayerEquipmentGrants"
            ],
            swordMasterMigrations);

        var novaGameTables = await ReadStringsAsync(
            _novaGameConnectionString,
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME");
        var swordMasterTables = await ReadStringsAsync(
            _swordMasterConnectionString,
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME");

        Assert.DoesNotContain(
            novaGameTables,
            table => table.StartsWith("sm_", StringComparison.Ordinal));
        Assert.DoesNotContain(
            swordMasterTables,
            table => table.StartsWith("admin_", StringComparison.Ordinal) ||
                table.StartsWith("game_config_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfiguredSeedsRemainSingleAfterTheMigratorIsRunTwice()
    {
        await using var novaGameConnection = new MySqlConnection(_novaGameConnectionString);
        await novaGameConnection.OpenAsync();
        Assert.Equal(
            1,
            await CountAsync(
                novaGameConnection,
                "SELECT COUNT(*) FROM admin_administrators WHERE NormalizedUserName = @normalizedUserName",
                ("@normalizedUserName", "RUNTIME-ADMIN")));
        Assert.Equal(
            1,
            await CountAsync(
                novaGameConnection,
                "SELECT COUNT(*) FROM admin_role_permissions AS permission INNER JOIN admin_roles AS role ON role.Id = permission.RoleId WHERE role.NormalizedName = 'ADMINISTRATORS' AND permission.Name = @permissionName",
                ("@permissionName", PlayerEquipmentGrantPermissions.Manage)));
        Assert.Equal(
            1,
            await CountAsync(
                novaGameConnection,
                "SELECT COUNT(*) FROM admin_audit_records AS audit INNER JOIN admin_roles AS role ON role.Id = audit.RoleId WHERE role.NormalizedName = 'ADMINISTRATORS' AND audit.Action = 'PermissionGranted' AND audit.Succeeded = 1 AND audit.PermissionName = @permissionName",
                ("@permissionName", PlayerEquipmentGrantPermissions.Manage)));

        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        var playerId = SeedPlayerId.ToString("D");
        var characterId = SeedCharacterId.ToString("D");
        var ownedItemId = SeedOwnedItemId.ToString("D");

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM sm_player_assets_profiles WHERE PlayerId = @playerId",
                ("@playerId", playerId)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM sm_player_characters WHERE PlayerId = @playerId AND CharacterId = @characterId",
                ("@playerId", playerId),
                ("@characterId", characterId)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM sm_owned_equipment_items WHERE PlayerId = @playerId AND OwnedItemId = @ownedItemId AND ConfiguredItemId = 'training-sword'",
                ("@playerId", playerId),
                ("@ownedItemId", ownedItemId)));

        Assert.Equal(
            [
                new SeedAttribute("attack", 10),
                new SeedAttribute("health", 100)
            ],
            await ReadSeedAttributesAsync(connection, playerId));
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException(
                $"{name} is required after the combined SwordMaster migrator has been run twice.");
    }

    private static async Task<List<string>> ReadStringsAsync(
        string connectionString,
        string commandText)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(commandText, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<long> CountAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(commandText, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<List<SeedAttribute>> ReadSeedAttributesAsync(
        MySqlConnection connection,
        string playerId)
    {
        await using var command = new MySqlCommand(
            "SELECT StatCode, StatValue FROM sm_player_character_base_attributes WHERE PlayerId = @playerId ORDER BY StatCode",
            connection);
        command.Parameters.AddWithValue("@playerId", playerId);
        await using var reader = await command.ExecuteReaderAsync();
        var attributes = new List<SeedAttribute>();
        while (await reader.ReadAsync())
        {
            attributes.Add(new SeedAttribute(reader.GetString(0), reader.GetInt32(1)));
        }

        return attributes;
    }

    private sealed record SeedAttribute(string Code, int Value);
}

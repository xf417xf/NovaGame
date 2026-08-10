using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class GameConfigSnapshotValidatorTests
{
    private const string ExpectedConfigurationSetValue = "content-set";
    private const string ExpectedSchemaVersion = "catalog.v1";
    private const string ValidCreatedAtUtc = "2026-08-10T12:34:56.1234567Z";
    private const string MinimalCanonicalContent =
        "{\"collections\":{\"records\":[{\"id\":\"entry\"}]}}";

    private static readonly Guid ConfigurationVersionId =
        Guid.Parse("abcdefab-cdef-abcd-efab-cdefabcdefab");

    private static readonly GameConfigSetKey ExpectedConfigurationSet =
        GameConfigSetKey.Create(ExpectedConfigurationSetValue);

    [Fact]
    public void ValidProducerSnapshotReturnsVerifiedNeutralMetadataAndCanonicalUnicodeContent()
    {
        const string sourceContent = """
            {
              "collections": {
                "catalog": [
                  { "label": "\u96ea", "id": "beta", "details": { "z": 2, "a": "é" } },
                  { "label": "雪", "id": "alpha", "details": { "z": 1, "a": "\u00e9" } }
                ]
              }
            }
            """;
        const string expectedCanonicalContent =
            "{\"collections\":{\"catalog\":[{\"details\":{\"a\":\"\\u00E9\",\"z\":1},\"id\":\"alpha\",\"label\":\"\\u96EA\"},{\"details\":{\"a\":\"\\u00E9\",\"z\":2},\"id\":\"beta\",\"label\":\"\\u96EA\"}]}}";
        var processor = new GameConfigContentProcessor();
        var processingResult = processor.Process(ExpectedSchemaVersion, sourceContent);
        var processedContent = Assert.IsType<GameConfigProcessedContent>(processingResult.Content);
        Assert.True(processingResult.Validation.IsValid);
        Assert.Equal(expectedCanonicalContent, processedContent.CanonicalJson);

        var createdAtUtc = new DateTime(2026, 8, 10, 12, 34, 56, DateTimeKind.Utc)
            .AddTicks(1_234_567);
        var produced = new GameConfigSnapshotBuilder().Build(
            ConfigurationVersionId,
            ExpectedConfigurationSet,
            createdAtUtc,
            processedContent);
        var sourceBytes = Encoding.UTF8.GetBytes(produced.Json);
        var validator = CreateValidator();

        var isValid = validator.TryValidate(
            sourceBytes,
            produced.SnapshotHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            ConfigurationVersionId,
            out var snapshot);

        Assert.True(isValid);
        var validated = Assert.IsType<ValidatedGameConfigSnapshot>(snapshot);
        Assert.Equal(ConfigurationVersionId, validated.ConfigurationVersionId);
        Assert.Equal(ExpectedConfigurationSet, validated.ConfigurationSet);
        Assert.Equal(ExpectedSchemaVersion, validated.SchemaVersion);
        Assert.Equal(createdAtUtc, validated.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, validated.CreatedAtUtc.Kind);
        Assert.Equal(produced.SnapshotHash, validated.SnapshotHash);
        Assert.Equal(produced.ContentHash, validated.ContentHash);
        Assert.Equal(expectedCanonicalContent, validated.CanonicalContentJson);

        Array.Fill(sourceBytes, (byte)0);
        Assert.Equal(expectedCanonicalContent, validated.CanonicalContentJson);
    }

    [Fact]
    public void SnapshotAndContentHashesCoverTheExactUnchangedBytes()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();
        var differentTrustedHash = valid.TrustedHash[0] == '0'
            ? $"1{valid.TrustedHash[1..]}"
            : $"0{valid.TrustedHash[1..]}";

        AssertInvalid(validator, valid with { TrustedHash = differentTrustedHash }, "whole snapshot hash");
        AssertInvalid(
            validator,
            CreateSnapshot(contentHashOverride: new string('0', 64)),
            "embedded content hash");
    }

    [Fact]
    public void SnapshotsLargerThanTheProducerBoundAreRejected()
    {
        var bytes = new byte[GameConfigConstants.SnapshotMaximumByteLength + 1];

        var isValid = CreateValidator().TryValidate(
            bytes,
            new string('0', 64),
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            null,
            out var snapshot);

        Assert.False(isValid);
        Assert.Null(snapshot);
    }

    [Fact]
    public void Sha256ValuesRequireExactly64LowercaseHexadecimalCharacters()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();
        var uppercaseTrustedHash = valid.TrustedHash.ToUpperInvariant();
        Assert.NotEqual(valid.TrustedHash, uppercaseTrustedHash);

        foreach (var trustedHash in new[]
                 {
                     uppercaseTrustedHash,
                     valid.TrustedHash[..63],
                     $"{valid.TrustedHash}0",
                     new string('g', 64),
                     $" {valid.TrustedHash}",
                     $"{valid.TrustedHash} "
                 })
        {
            AssertInvalid(
                validator,
                valid with { TrustedHash = trustedHash },
                $"trusted hash '{trustedHash}'");
        }

        var contentHash = ComputeHash(Encoding.UTF8.GetBytes(MinimalCanonicalContent));
        foreach (var embeddedHash in new[]
                 {
                     contentHash.ToUpperInvariant(),
                     contentHash[..63],
                     $"{contentHash}0",
                     new string('g', 64),
                     $" {contentHash}",
                     $"{contentHash} "
                 })
        {
            AssertInvalid(
                validator,
                CreateSnapshot(contentHashOverride: embeddedHash),
                $"embedded hash '{embeddedHash}'");
        }
    }

    [Fact]
    public void EncodingRequiresStrictUtf8WithoutAByteOrderMark()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();
        var withBom = Encoding.UTF8.GetPreamble()
            .Concat(valid.Bytes)
            .ToArray();
        var malformedUtf8 = valid.Bytes.ToArray();
        malformedUtf8[2] = 0xff;

        AssertInvalid(validator, Rehash(withBom), "UTF-8 byte-order mark");
        AssertInvalid(validator, Rehash(malformedUtf8), "malformed UTF-8");
    }

    [Fact]
    public void JsonDocumentRejectsOuterTriviaTrailingDataCommentsAndTrailingCommas()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();
        var mutations = new Dictionary<string, string>
        {
            ["leading trivia"] = $" {valid.Json}",
            ["trailing trivia"] = $"{valid.Json}\r\n",
            ["trailing data"] = $"{valid.Json}{{}}",
            ["comment"] = valid.Json.Replace(
                "{\"formatVersion\"",
                "{/*comment*/\"formatVersion\"",
                StringComparison.Ordinal),
            ["trailing comma"] = $"{valid.Json[..^1]},}}"
        };

        foreach (var (name, json) in mutations)
        {
            AssertInvalid(validator, Rehash(json), name);
        }
    }

    [Fact]
    public void DuplicatePropertiesAreRejectedRecursively()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();
        var duplicateRoot = valid.Json.Replace(
            "{\"formatVersion\":\"1\",",
            "{\"formatVersion\":\"1\",\"formatVersion\":\"1\",",
            StringComparison.Ordinal);
        var duplicateIntegrity = valid.Json.Replace(
            "\"integrity\":{\"algorithm\":\"SHA-256\",",
            "\"integrity\":{\"algorithm\":\"SHA-256\",\"algorithm\":\"SHA-256\",",
            StringComparison.Ordinal);
        const string duplicateNestedContent =
            "{\"collections\":{\"records\":[{\"id\":\"entry\",\"metadata\":{\"value\":1,\"value\":1}}]}}";

        AssertInvalid(validator, Rehash(duplicateRoot), "duplicate root property");
        AssertInvalid(validator, Rehash(duplicateIntegrity), "duplicate integrity property");
        AssertInvalid(
            validator,
            CreateSnapshot(contentJson: duplicateNestedContent),
            "duplicate nested content property");
    }

    [Fact]
    public void EnvelopeRequiresExactFieldsTypesAndProtocolConstants()
    {
        var validator = CreateValidator();
        var contentHash = ComputeHash(Encoding.UTF8.GetBytes(MinimalCanonicalContent));
        var integrityProperties = CreateIntegrityProperties(contentHash);
        var integrityJson = JoinObject(integrityProperties);
        var rootProperties = CreateRootProperties(MinimalCanonicalContent, integrityJson);

        for (var index = 0; index < rootProperties.Length; index++)
        {
            AssertInvalid(
                validator,
                FromRootProperties(rootProperties.Where((_, itemIndex) => itemIndex != index)),
                $"missing root field at index {index}");
        }

        AssertInvalid(
            validator,
            FromRootProperties([.. rootProperties, "\"unexpected\":true"]),
            "extra root field");

        for (var index = 0; index < integrityProperties.Length; index++)
        {
            AssertInvalid(
                validator,
                CreateSnapshot(
                    integrityJson: JoinObject(
                        integrityProperties.Where((_, itemIndex) => itemIndex != index))),
                $"missing integrity field at index {index}");
        }

        AssertInvalid(
            validator,
            CreateSnapshot(integrityJson: JoinObject([.. integrityProperties, "\"unexpected\":true"])),
            "extra integrity field");

        var invalidRootTypes = new (int Index, string JsonValue, string Name)[]
        {
            (0, "1", "formatVersion number"),
            (1, "true", "schemaVersion boolean"),
            (2, "[]", "configurationSet array"),
            (3, "3", "configurationVersion number"),
            (4, "null", "createdAtUtc null"),
            (5, "\"integrity\"", "integrity string")
        };
        foreach (var (index, jsonValue, name) in invalidRootTypes)
        {
            var properties = rootProperties.ToArray();
            var propertyName = GetPropertyName(properties[index]);
            properties[index] = $"{JsonString(propertyName)}:{jsonValue}";
            AssertInvalid(validator, FromRootProperties(properties), name);
        }

        AssertInvalid(
            validator,
            CreateSnapshot(contentJson: "\"not-an-object\""),
            "content string");
        AssertInvalid(validator, Rehash("[]"), "non-object envelope");

        var invalidIntegrityTypes = new (int Index, string JsonValue, string Name)[]
        {
            (0, "1", "algorithm number"),
            (1, "null", "canonicalization null"),
            (2, "false", "contentHash boolean")
        };
        foreach (var (index, jsonValue, name) in invalidIntegrityTypes)
        {
            var properties = integrityProperties.ToArray();
            var propertyName = GetPropertyName(properties[index]);
            properties[index] = $"{JsonString(propertyName)}:{jsonValue}";
            AssertInvalid(
                validator,
                CreateSnapshot(integrityJson: JoinObject(properties)),
                name);
        }

        AssertInvalid(validator, CreateSnapshot(formatVersion: "2"), "format version constant");
        AssertInvalid(validator, CreateSnapshot(algorithm: "sha-256"), "hash algorithm constant");
        AssertInvalid(
            validator,
            CreateSnapshot(canonicalization: "Another.CanonicalJson.v1"),
            "canonicalization constant");
    }

    [Fact]
    public void CallerSuppliedSetSchemaAndOptionalVersionMustMatch()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();

        Assert.True(validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            expectedConfigurationVersionId: null,
            out var withoutVersionExpectation));
        Assert.NotNull(withoutVersionExpectation);

        Assert.True(validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            ConfigurationVersionId,
            out var exact));
        Assert.NotNull(exact);

        AssertInvalid(
            validator,
            valid,
            "configuration set mismatch",
            expectedConfigurationSet: GameConfigSetKey.Create("another-set"));
        AssertInvalid(
            validator,
            valid,
            "schema mismatch",
            expectedSchemaVersion: "another.v1");
        AssertInvalid(
            validator,
            valid,
            "configuration version mismatch",
            expectedConfigurationVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public void InvalidCallerExpectationsAreProgrammerErrors()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot();

        Assert.ThrowsAny<ArgumentException>(() => validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            null!,
            ExpectedSchemaVersion,
            null,
            out _));

        foreach (var schemaVersion in new[] { null, string.Empty, " catalog.v1", "catalog/v1" })
        {
            Assert.ThrowsAny<ArgumentException>(() => validator.TryValidate(
                valid.Bytes,
                valid.TrustedHash,
                ExpectedConfigurationSet,
                schemaVersion!,
                null,
                out _));
        }

        Assert.ThrowsAny<ArgumentException>(() => validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            Guid.Empty,
            out _));
    }

    [Fact]
    public void ConfigurationVersionRequiresALowercaseNonzeroDGuid()
    {
        var validator = CreateValidator();
        var lowercaseD = ConfigurationVersionId.ToString("D");
        var valid = CreateSnapshot(configurationVersion: lowercaseD);

        Assert.True(validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            ConfigurationVersionId,
            out var snapshot));
        Assert.Equal(ConfigurationVersionId, snapshot!.ConfigurationVersionId);

        foreach (var invalidVersion in new[]
                 {
                     lowercaseD.ToUpperInvariant(),
                     ConfigurationVersionId.ToString("N"),
                     ConfigurationVersionId.ToString("B"),
                     Guid.Empty.ToString("D"),
                     $" {lowercaseD}",
                     $"{lowercaseD} "
                 })
        {
            AssertInvalid(
                validator,
                CreateSnapshot(configurationVersion: invalidVersion),
                $"configuration version '{invalidVersion}'");
        }
    }

    [Fact]
    public void CreatedAtRequiresAnExactSevenFractionalDigitUtcCalendarTimestamp()
    {
        var validator = CreateValidator();
        var valid = CreateSnapshot(createdAtUtc: ValidCreatedAtUtc);

        Assert.True(validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            null,
            out var snapshot));
        Assert.Equal(
            new DateTime(2026, 8, 10, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_234_567),
            snapshot!.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, snapshot.CreatedAtUtc.Kind);

        foreach (var invalidTimestamp in new[]
                 {
                     "2026-08-10T12:34:56Z",
                     "2026-08-10T12:34:56.123456Z",
                     "2026-08-10T12:34:56.12345678Z",
                     "2026-08-10T12:34:56.1234567z",
                     "2026-08-10T12:34:56.1234567+00:00",
                     "2026-08-10 12:34:56.1234567Z",
                     "2026-02-29T12:34:56.1234567Z",
                     "2026-13-10T12:34:56.1234567Z",
                     "2026-08-10T24:00:00.0000000Z",
                     "2026-08-10T12:34:60.0000000Z"
                 })
        {
            AssertInvalid(
                validator,
                CreateSnapshot(createdAtUtc: invalidTimestamp),
                $"createdAtUtc '{invalidTimestamp}'");
        }
    }

    [Fact]
    public void RawContentHashAndCanonicalityPreserveExactUnicodeEscapes()
    {
        const string canonicalContent =
            "{\"collections\":{\"records\":[{\"id\":\"entry\",\"label\":\"\\u96EA\"}]}}";
        var validator = CreateValidator();
        var valid = CreateSnapshot(contentJson: canonicalContent);

        Assert.True(validator.TryValidate(
            valid.Bytes,
            valid.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            null,
            out var snapshot));
        Assert.Equal(canonicalContent, snapshot!.CanonicalContentJson);

        var semanticallyEquivalentRawContent = canonicalContent.Replace(
            "\\u96EA",
            "雪",
            StringComparison.Ordinal);
        var unchangedEmbeddedHash = Rehash(valid.Json.Replace(
            canonicalContent,
            semanticallyEquivalentRawContent,
            StringComparison.Ordinal));
        AssertInvalid(
            validator,
            unchangedEmbeddedHash,
            "raw content bytes changed without changing contentHash");
        AssertInvalid(
            validator,
            CreateSnapshot(contentJson: semanticallyEquivalentRawContent),
            "raw content hash matches but content is not producer-canonical");
    }

    [Fact]
    public void ProducerCanonicalExpansionBeyondTheDraftLimitRemainsConsumable()
    {
        var label = new string('\u00e9', 700_000);
        var sourceContent =
            $"{{\"collections\":{{\"records\":[{{\"id\":\"entry\",\"label\":\"{label}\"}}]}}}}";
        Assert.True(sourceContent.Length < GameConfigConstants.RawContentMaximumLength);

        var processor = new GameConfigContentProcessor();
        var processing = processor.Process(ExpectedSchemaVersion, sourceContent);
        var content = Assert.IsType<GameConfigProcessedContent>(processing.Content);
        Assert.True(processing.Validation.IsValid);
        Assert.True(
            Encoding.UTF8.GetByteCount(content.CanonicalJson) >
            GameConfigConstants.RawContentMaximumLength);

        var produced = new GameConfigSnapshotBuilder().Build(
            ConfigurationVersionId,
            ExpectedConfigurationSet,
            DateTime.Parse(ValidCreatedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
            content);
        var snapshotBytes = Encoding.UTF8.GetBytes(produced.Json);
        Assert.True(snapshotBytes.Length <= GameConfigConstants.SnapshotMaximumByteLength);

        Assert.True(CreateValidator().TryValidate(
            snapshotBytes,
            produced.SnapshotHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            ConfigurationVersionId,
            out var snapshot));
        Assert.Equal(content.CanonicalJson, snapshot!.CanonicalContentJson);
    }

    [Fact]
    public void CanonicalContentPreservesLosslessNumericLexemes()
    {
        const string content =
            "{\"collections\":{\"records\":[{\"id\":\"entry\",\"large\":123456789012345678901234567890,\"scaled\":1.2300,\"scientific\":1e+09}]}}";
        var fixture = CreateSnapshot(contentJson: content);

        Assert.True(CreateValidator().TryValidate(
            fixture.Bytes,
            fixture.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            null,
            out var snapshot));
        Assert.Equal(content, snapshot!.CanonicalContentJson);
    }

    [Fact]
    public void ContentMustBeProducerCanonicalAndSatisfyTheNeutralContract()
    {
        var invalidContents = new Dictionary<string, string>
        {
            ["content whitespace"] =
                "{\"collections\": {\"records\":[{\"id\":\"entry\"}]}}",
            ["collection order"] =
                "{\"collections\":{\"z\":[{\"id\":\"entry\"}],\"a\":[{\"id\":\"entry\"}]}}",
            ["record order"] =
                "{\"collections\":{\"records\":[{\"id\":\"z\"},{\"id\":\"a\"}]}}",
            ["nested property order"] =
                "{\"collections\":{\"records\":[{\"id\":\"entry\",\"metadata\":{\"z\":1,\"a\":2}}]}}",
            ["missing collections"] = "{}",
            ["unexpected content property"] =
                "{\"collections\":{\"records\":[{\"id\":\"entry\"}]},\"unexpected\":true}",
            ["collection is not an array"] =
                "{\"collections\":{\"records\":{}}}",
            ["record is not an object"] =
                "{\"collections\":{\"records\":[1]}}",
            ["duplicate record identifier"] =
                "{\"collections\":{\"records\":[{\"id\":\"entry\"},{\"id\":\"entry\"}]}}",
            ["unresolved reference"] =
                "{\"collections\":{\"records\":[{\"id\":\"entry\",\"target\":{\"$ref\":\"records/missing\"}}]}}"
        };

        var validator = CreateValidator();
        foreach (var (reason, content) in invalidContents)
        {
            AssertInvalid(validator, CreateSnapshot(contentJson: content), reason);
        }
    }

    [Fact]
    public void EnvelopeAllows65LevelsWhileContentRejectsMoreThan64()
    {
        var processor = new GameConfigContentProcessor();
        var validator = CreateValidator();
        var contentAtLimit = CreateNestedContent(additionalArrayDepth: 60);
        var contentPastLimit = CreateNestedContent(additionalArrayDepth: 61);

        var processingAtLimit = processor.Process(ExpectedSchemaVersion, contentAtLimit);
        Assert.True(processingAtLimit.Validation.IsValid);
        Assert.Equal(contentAtLimit, processingAtLimit.Content!.CanonicalJson);
        Assert.False(processor.Process(ExpectedSchemaVersion, contentPastLimit).Validation.IsValid);

        var snapshotAtLimit = CreateSnapshot(contentJson: contentAtLimit);
        Assert.True(validator.TryValidate(
            snapshotAtLimit.Bytes,
            snapshotAtLimit.TrustedHash,
            ExpectedConfigurationSet,
            ExpectedSchemaVersion,
            null,
            out var validated));
        Assert.Equal(contentAtLimit, validated!.CanonicalContentJson);

        AssertInvalid(
            validator,
            CreateSnapshot(contentJson: contentPastLimit),
            "content depth 65 / envelope depth 66");
    }

    private static GameConfigSnapshotValidator CreateValidator()
    {
        return new GameConfigSnapshotValidator(new GameConfigContentProcessor());
    }

    private static void AssertInvalid(
        GameConfigSnapshotValidator validator,
        SnapshotFixture fixture,
        string reason,
        GameConfigSetKey? expectedConfigurationSet = null,
        string expectedSchemaVersion = ExpectedSchemaVersion,
        Guid? expectedConfigurationVersionId = null)
    {
        var isValid = validator.TryValidate(
            fixture.Bytes,
            fixture.TrustedHash,
            expectedConfigurationSet ?? ExpectedConfigurationSet,
            expectedSchemaVersion,
            expectedConfigurationVersionId,
            out var snapshot);

        Assert.False(isValid, reason);
        Assert.Null(snapshot);
    }

    private static SnapshotFixture CreateSnapshot(
        string contentJson = MinimalCanonicalContent,
        string formatVersion = GameConfigConstants.SnapshotFormatVersion,
        string schemaVersion = ExpectedSchemaVersion,
        string configurationSet = ExpectedConfigurationSetValue,
        string? configurationVersion = null,
        string createdAtUtc = ValidCreatedAtUtc,
        string algorithm = GameConfigConstants.ContentHashAlgorithm,
        string canonicalization = GameConfigConstants.CanonicalizationProfile,
        string? contentHashOverride = null,
        string? integrityJson = null)
    {
        var contentHash = contentHashOverride ?? ComputeHash(Encoding.UTF8.GetBytes(contentJson));
        integrityJson ??= JoinObject(
            CreateIntegrityProperties(contentHash, algorithm, canonicalization));
        var rootProperties = CreateRootProperties(
            contentJson,
            integrityJson,
            formatVersion,
            schemaVersion,
            configurationSet,
            configurationVersion ?? ConfigurationVersionId.ToString("D"),
            createdAtUtc);
        return FromRootProperties(rootProperties);
    }

    private static string[] CreateRootProperties(
        string contentJson,
        string integrityJson,
        string formatVersion = GameConfigConstants.SnapshotFormatVersion,
        string schemaVersion = ExpectedSchemaVersion,
        string configurationSet = ExpectedConfigurationSetValue,
        string? configurationVersion = null,
        string createdAtUtc = ValidCreatedAtUtc)
    {
        return
        [
            $"\"formatVersion\":{JsonString(formatVersion)}",
            $"\"schemaVersion\":{JsonString(schemaVersion)}",
            $"\"configurationSet\":{JsonString(configurationSet)}",
            $"\"configurationVersion\":{JsonString(configurationVersion ?? ConfigurationVersionId.ToString("D"))}",
            $"\"createdAtUtc\":{JsonString(createdAtUtc)}",
            $"\"integrity\":{integrityJson}",
            $"\"content\":{contentJson}"
        ];
    }

    private static string[] CreateIntegrityProperties(
        string contentHash,
        string algorithm = GameConfigConstants.ContentHashAlgorithm,
        string canonicalization = GameConfigConstants.CanonicalizationProfile)
    {
        return
        [
            $"\"algorithm\":{JsonString(algorithm)}",
            $"\"canonicalization\":{JsonString(canonicalization)}",
            $"\"contentHash\":{JsonString(contentHash)}"
        ];
    }

    private static SnapshotFixture FromRootProperties(IEnumerable<string> properties)
    {
        return Rehash(JoinObject(properties));
    }

    private static SnapshotFixture Rehash(string json)
    {
        return Rehash(Encoding.UTF8.GetBytes(json), json);
    }

    private static SnapshotFixture Rehash(byte[] bytes)
    {
        return Rehash(bytes, string.Empty);
    }

    private static SnapshotFixture Rehash(byte[] bytes, string json)
    {
        return new SnapshotFixture(bytes, ComputeHash(bytes), json);
    }

    private static string JoinObject(IEnumerable<string> properties)
    {
        return $"{{{string.Join(',', properties)}}}";
    }

    private static string JsonString(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static string GetPropertyName(string property)
    {
        var separatorIndex = property.IndexOf(':', StringComparison.Ordinal);
        return JsonSerializer.Deserialize<string>(property[..separatorIndex])!;
    }

    private static string CreateNestedContent(int additionalArrayDepth)
    {
        return "{\"collections\":{\"records\":[{\"id\":\"entry\",\"payload\":" +
            new string('[', additionalArrayDepth) +
            "null" +
            new string(']', additionalArrayDepth) +
            "}]}}";
    }

    private static string ComputeHash(byte[] value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private sealed record SnapshotFixture(byte[] Bytes, string TrustedHash, string Json);
}

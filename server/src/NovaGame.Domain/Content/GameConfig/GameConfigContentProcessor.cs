using System.Buffers;
using System.Text;
using System.Text.Json;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigContentProcessor
{
    public GameConfigContentProcessingResult Process(string? schemaVersion, string? rawContentJson)
    {
        var normalizedSchemaVersion = schemaVersion?.Trim() ?? string.Empty;
        var issues = new List<GameConfigValidationIssue>();

        if (!string.Equals(
                normalizedSchemaVersion,
                GameConfigConstants.SupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.UnsupportedSchemaVersion,
                string.Empty,
                $"Schema version '{normalizedSchemaVersion}' is not supported."));
        }

        var content = rawContentJson ?? string.Empty;
        if (content.Length > GameConfigConstants.RawContentMaximumLength)
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.ContentTooLarge,
                string.Empty,
                $"Configuration content cannot exceed {GameConfigConstants.RawContentMaximumLength} characters."));
            return CreateResult(normalizedSchemaVersion, issues, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
        }
        catch (JsonException exception)
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.InvalidJson,
                string.Empty,
                "Configuration content must be valid JSON.",
                exception.LineNumber,
                exception.BytePositionInLine));
            return CreateResult(normalizedSchemaVersion, issues, null);
        }

        using (document)
        {
            var root = document.RootElement;
            FindDuplicateProperties(root, string.Empty, issues);
            ValidateDocument(root, issues);

            if (issues.Count != 0)
            {
                return CreateResult(normalizedSchemaVersion, issues, null);
            }

            var canonicalJson = Canonicalize(root);
            return CreateResult(
                normalizedSchemaVersion,
                issues,
                new GameConfigProcessedContent(
                    normalizedSchemaVersion,
                    GameConfigConstants.CanonicalizationProfile,
                    canonicalJson));
        }
    }

    private static GameConfigContentProcessingResult CreateResult(
        string schemaVersion,
        IEnumerable<GameConfigValidationIssue> issues,
        GameConfigProcessedContent? content)
    {
        return new GameConfigContentProcessingResult(
            new GameConfigValidationResult(
                schemaVersion,
                GameConfigConstants.ValidationProfile,
                issues),
            content);
    }

    private static void ValidateDocument(
        JsonElement root,
        ICollection<GameConfigValidationIssue> issues)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.RootObjectRequired,
                string.Empty,
                "Configuration content must be a JSON object."));
            return;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "collections", StringComparison.Ordinal))
            {
                issues.Add(new GameConfigValidationIssue(
                    GameConfigValidationCodes.UnexpectedProperty,
                    AppendPath(string.Empty, property.Name),
                    "The configuration root only supports the 'collections' property."));
            }
        }

        if (!root.TryGetProperty("collections", out var collections))
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.CollectionsRequired,
                "/collections",
                "The 'collections' property is required."));
            return;
        }

        if (collections.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new GameConfigValidationIssue(
                GameConfigValidationCodes.CollectionsMustBeObject,
                "/collections",
                "The 'collections' property must be a JSON object."));
            return;
        }

        var records = new HashSet<(string Collection, string Id)>();
        var references = new List<GameConfigReference>();

        foreach (var collection in collections.EnumerateObject())
        {
            var collectionPath = AppendPath("/collections", collection.Name);
            var collectionNameIsValid = GameConfigSlugRules.IsValid(
                collection.Name,
                GameConfigConstants.CollectionNameMaximumLength);
            if (!collectionNameIsValid)
            {
                issues.Add(new GameConfigValidationIssue(
                    GameConfigValidationCodes.InvalidCollectionName,
                    collectionPath,
                    "Collection names must be lowercase safe slugs."));
            }

            if (collection.Value.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new GameConfigValidationIssue(
                    GameConfigValidationCodes.CollectionMustBeArray,
                    collectionPath,
                    "Each collection must be a JSON array."));
                continue;
            }

            var recordIndex = 0;
            foreach (var record in collection.Value.EnumerateArray())
            {
                var recordPath = AppendPath(collectionPath, recordIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                recordIndex++;

                if (record.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new GameConfigValidationIssue(
                        GameConfigValidationCodes.RecordMustBeObject,
                        recordPath,
                        "Configuration records must be JSON objects."));
                    continue;
                }

                if (!record.TryGetProperty("id", out var idProperty) ||
                    idProperty.ValueKind != JsonValueKind.String ||
                    string.IsNullOrEmpty(idProperty.GetString()))
                {
                    issues.Add(new GameConfigValidationIssue(
                        GameConfigValidationCodes.RecordIdRequired,
                        AppendPath(recordPath, "id"),
                        "Each configuration record requires a string 'id'."));
                }
                else
                {
                    var recordId = idProperty.GetString()!;
                    if (!GameConfigSlugRules.IsValid(recordId, GameConfigConstants.RecordIdMaximumLength))
                    {
                        issues.Add(new GameConfigValidationIssue(
                            GameConfigValidationCodes.InvalidRecordId,
                            AppendPath(recordPath, "id"),
                            "Record identifiers must be lowercase safe slugs."));
                    }
                    else if (collectionNameIsValid && !records.Add((collection.Name, recordId)))
                    {
                        issues.Add(new GameConfigValidationIssue(
                            GameConfigValidationCodes.DuplicateRecordId,
                            AppendPath(recordPath, "id"),
                            $"Record identifier '{recordId}' is duplicated in collection '{collection.Name}'."));
                    }
                }

                FindReferences(record, recordPath, references, issues);
            }
        }

        foreach (var reference in references)
        {
            if (!records.Contains((reference.Collection, reference.Id)))
            {
                issues.Add(new GameConfigValidationIssue(
                    GameConfigValidationCodes.ReferenceNotFound,
                    reference.Location,
                    $"Reference '{reference.Collection}/{reference.Id}' does not resolve to a configuration record."));
            }
        }
    }

    private static void FindDuplicateProperties(
        JsonElement element,
        string path,
        ICollection<GameConfigValidationIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = AppendPath(path, property.Name);
                if (!propertyNames.Add(property.Name))
                {
                    issues.Add(new GameConfigValidationIssue(
                        GameConfigValidationCodes.DuplicateProperty,
                        propertyPath,
                        $"Property '{property.Name}' is duplicated."));
                }

                FindDuplicateProperties(property.Value, propertyPath, issues);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicateProperties(
                    item,
                    AppendPath(path, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    issues);
                index++;
            }
        }
    }

    private static void FindReferences(
        JsonElement element,
        string path,
        ICollection<GameConfigReference> references,
        ICollection<GameConfigValidationIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            var referenceProperties = properties
                .Where(property => property.NameEquals("$ref"))
                .ToArray();
            if (referenceProperties.Length != 0)
            {
                var referenceProperty = referenceProperties[0];
                var referenceLocation = AppendPath(path, "$ref");
                var referenceValue = referenceProperty.Value.ValueKind == JsonValueKind.String
                    ? referenceProperty.Value.GetString()
                    : null;
                if (properties.Length != 1 ||
                    referenceProperties.Length != 1 ||
                    !TryParseReference(referenceValue, out var collection, out var id))
                {
                    issues.Add(new GameConfigValidationIssue(
                        GameConfigValidationCodes.InvalidReference,
                        referenceLocation,
                        "A reference must be an exact object in the form {'$ref':'collection/id'}."));
                    return;
                }

                references.Add(new GameConfigReference(collection, id, referenceLocation));
                return;
            }

            foreach (var property in properties)
            {
                FindReferences(property.Value, AppendPath(path, property.Name), references, issues);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindReferences(
                    item,
                    AppendPath(path, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    references,
                    issues);
                index++;
            }
        }
    }

    private static bool TryParseReference(
        string? value,
        out string collection,
        out string id)
    {
        collection = string.Empty;
        id = string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separatorIndex = value.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf('/') || separatorIndex == value.Length - 1)
        {
            return false;
        }

        collection = value[..separatorIndex];
        id = value[(separatorIndex + 1)..];
        return GameConfigSlugRules.IsValid(collection, GameConfigConstants.CollectionNameMaximumLength) &&
            GameConfigSlugRules.IsValid(id, GameConfigConstants.RecordIdMaximumLength);
    }

    private static string Canonicalize(JsonElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WritePropertyName("collections");
        WriteCollections(writer, root.GetProperty("collections"));
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCollections(Utf8JsonWriter writer, JsonElement collections)
    {
        writer.WriteStartObject();
        foreach (var collection in collections.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(collection.Name);
            writer.WriteStartArray();
            foreach (var record in collection.Value
                         .EnumerateArray()
                         .OrderBy(item => item.GetProperty("id").GetString(), StringComparer.Ordinal))
            {
                WriteCanonicalValue(writer, record);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonicalValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind '{value.ValueKind}'.");
        }
    }

    private static string AppendPath(string path, string segment)
    {
        var escaped = segment.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
        return $"{path}/{escaped}";
    }

    private sealed record GameConfigReference(string Collection, string Id, string Location);
}

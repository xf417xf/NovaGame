namespace NovaGame.Domain.Content.GameConfig;

public enum GameConfigValidationStatus
{
    NotValidated = 0,
    Valid = 1,
    Invalid = 2
}

public sealed class GameConfigValidationIssue
{
    private GameConfigValidationIssue()
    {
    }

    public GameConfigValidationIssue(
        string code,
        string location,
        string message,
        long? lineNumber = null,
        long? bytePositionInLine = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A validation issue code is required.", nameof(code));
        }

        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        Code = code;
        Location = location ?? string.Empty;
        Message = message;
        LineNumber = lineNumber;
        BytePositionInLine = bytePositionInLine;
    }

    public string Code { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public int Sequence { get; private set; }

    public long? LineNumber { get; private set; }

    public long? BytePositionInLine { get; private set; }

    internal GameConfigValidationIssue Copy(int sequence)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return new GameConfigValidationIssue(
            Code,
            Location,
            Message,
            LineNumber,
            BytePositionInLine)
        {
            Sequence = sequence
        };
    }
}

public sealed class GameConfigValidationResult
{
    public GameConfigValidationResult(
        string schemaVersion,
        string validationProfile,
        IEnumerable<GameConfigValidationIssue> issues)
    {
        SchemaVersion = schemaVersion;
        ValidationProfile = validationProfile;
        Issues = issues.ToArray();
    }

    public string SchemaVersion { get; }

    public string ValidationProfile { get; }

    public IReadOnlyList<GameConfigValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;
}

public sealed class GameConfigProcessedContent
{
    internal GameConfigProcessedContent(
        string schemaVersion,
        string canonicalizationProfile,
        string canonicalJson)
    {
        SchemaVersion = schemaVersion;
        CanonicalizationProfile = canonicalizationProfile;
        CanonicalJson = canonicalJson;
    }

    public string SchemaVersion { get; }

    public string CanonicalizationProfile { get; }

    public string CanonicalJson { get; }
}

public sealed class GameConfigContentProcessingResult
{
    internal GameConfigContentProcessingResult(
        GameConfigValidationResult validation,
        GameConfigProcessedContent? content)
    {
        Validation = validation;
        Content = content;
    }

    public GameConfigValidationResult Validation { get; }

    public GameConfigProcessedContent? Content { get; }
}

namespace Core.Models;

public sealed class ValidationError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

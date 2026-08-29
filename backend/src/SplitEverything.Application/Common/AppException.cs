namespace SplitEverything.Application.Common;

/// <summary>Base for failures the API turns into a deliberate status code rather than a 500.</summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public virtual string Code => GetType().Name.Replace("Exception", string.Empty);
}

public sealed class NotFoundException(string what) : AppException($"{what} was not found.")
{
    public override int StatusCode => 404;
}

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException(message)
{
    public override int StatusCode => 403;
}

public sealed class ValidationException(string message) : AppException(message)
{
    public override int StatusCode => 400;
}

/// <summary>Raised when a write targets an archived group, which is frozen by design.</summary>
public sealed class GroupArchivedException(string message = "This group is archived and cannot be modified.")
    : AppException(message)
{
    public override int StatusCode => 409;
}

/// <summary>Raised when an incoming sync revision is concurrent with the stored one.</summary>
public sealed class SyncConflictException(string message = "This change conflicts with a newer edit.")
    : AppException(message)
{
    public override int StatusCode => 409;
}

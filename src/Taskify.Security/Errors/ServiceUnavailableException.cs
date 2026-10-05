namespace Taskify.Security.Errors;

/// <summary>
/// A service this one depends on (for example the Projects API) cannot be reached. The global exception handler
/// turns it into a generic <c>503</c> instead of a <c>500</c>, and never includes the inner details in the response.
/// </summary>
public sealed class ServiceUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ServiceUnavailableException()
    {
    }

    /// <summary>Creates the exception with a message for the server log.</summary>
    /// <param name="message">What was unavailable. It is logged on the server and never sent to the caller.</param>
    public ServiceUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">What was unavailable. It is logged on the server and never sent to the caller.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace Todo.Host.Helper;

/// <summary>
/// No helper is connected, or the one that was went away mid-call. An HttpRequestException on purpose:
/// the Ollama client already reads that as "not ready", and the two sources need only one catch in
/// front of the general one to give it its own code, helper.offline.
/// </summary>
public sealed class HelperOfflineException()
    : HttpRequestException("The helper on the PC is not connected, so nothing outside the host can be reached.");

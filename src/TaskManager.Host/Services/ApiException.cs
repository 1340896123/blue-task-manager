namespace TaskManager.Host.Services;

public sealed class ApiException(int status, string code, string message, int? nativeErrorCode = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public int? NativeErrorCode { get; } = nativeErrorCode;
}

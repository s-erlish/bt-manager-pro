namespace BluetoothManagerPro.Models;

/// <summary>Result of a device operation, carrying a message fit to show in the UI.</summary>
public readonly record struct OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "") => new(true, message);

    public static OperationResult Fail(string message) => new(false, message);
}

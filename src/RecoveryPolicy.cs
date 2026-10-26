namespace AsterVPN;

public static class RecoveryPolicy
{
    // Keep retrying after long outages, without overflowing or hammering the server.
    public static int RetryDelaySeconds(int attempts) => Math.Min(300, 15 * (1 << Math.Clamp(attempts - 1, 0, 5)));
}

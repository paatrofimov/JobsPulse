namespace JobsPulse.Host.Models;

public enum HostRole
{
    All,
    Bot,
    Webhook,
    WebhookSetup,
    Polling,
    Registry,
    Discovery,
    Cleanup,
    Digest,
    HistoryRepair
}

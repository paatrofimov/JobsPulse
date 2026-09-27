namespace JobsPulse.Host.Models;

public enum HostRole
{
    All,
    Bot,
    Webhook,
    WebhookSetup,
    Release,
    Polling,
    Registry,
    Discovery,
    Cleanup
}

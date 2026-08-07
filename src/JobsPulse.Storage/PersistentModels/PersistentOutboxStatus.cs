namespace JobsPulse.Storage.PersistentModels;

internal enum PersistentOutboxStatus
{
    Unknown = 0,
    Pending = 1,
    Leased = 2,
    Delivered = 3,
    Dead = 4,
}
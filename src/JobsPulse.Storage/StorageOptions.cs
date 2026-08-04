namespace JobsPulse.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Путь к файлу SQLite. На этапе реестра (~1 млн строк) сюда приедет Postgres — интерфейсы не изменятся.</summary>
    public string DatabasePath { get; set; } = "jobs_pulse.db";
}

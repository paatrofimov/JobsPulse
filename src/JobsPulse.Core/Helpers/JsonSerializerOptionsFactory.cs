using System.Text.Json;

namespace JobsPulse.Core.Helpers;

public sealed class JsonSerializerOptionsFactory
{
    public static readonly JsonSerializerOptions Instance = CreateJsonOptions();

    public static JsonSerializerOptions CreateJsonOptions(Action<JsonSerializerOptions>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        configure?.Invoke(options);
        return options;
    }
}
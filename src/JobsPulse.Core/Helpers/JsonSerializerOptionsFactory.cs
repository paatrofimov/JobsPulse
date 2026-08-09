using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobsPulse.Core.Helpers;

public sealed class JsonSerializerOptionsFactory
{
    public static readonly JsonSerializerOptions Instance = CreateJsonOptions();

    public static JsonSerializerOptions CreateJsonOptions(Action<JsonSerializerOptions>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        configure?.Invoke(options);
        return options;
    }
}
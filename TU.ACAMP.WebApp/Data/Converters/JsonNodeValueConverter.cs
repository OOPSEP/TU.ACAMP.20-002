using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json.Nodes;

namespace TU.ACAMP.WebApp.Data.Converters;

public class JsonNodeValueConverter : ValueConverter<JsonNode, string>
{
    public JsonNodeValueConverter() : base(
        v => ConvertToJsonString(v),
        v => ParseJson(v)
    )
    {
    }

    private static string ConvertToJsonString(JsonNode? node)
    {
        return node == null ? "[]" : node.ToJsonString(null);
    }

    private static JsonNode ParseJson(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) 
                ? JsonNode.Parse("[]", null, default)! 
                : JsonNode.Parse(json, null, default)!;
        }
        catch
        {
            return JsonNode.Parse("[]", null, default)!;
        }
    }
}

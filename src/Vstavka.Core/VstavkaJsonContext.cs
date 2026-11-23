using System.Text.Json.Serialization;

namespace Vstavka.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(HistoryStore.HistoryDocument))]
[JsonSerializable(typeof(ClipCollection))]
[JsonSerializable(typeof(List<ClipCollection>), TypeInfoPropertyName = "Collections")]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "LocalizationCatalog")]
internal sealed partial class VstavkaJsonContext : JsonSerializerContext;

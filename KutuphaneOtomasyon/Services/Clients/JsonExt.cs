using System.Text.Json;

namespace KutuphaneOtomasyon.Services.Clients
{
    public static class JsonExt
    {
        // Boş JsonElement döndürmek için sabit bir "{}" dokümanı
        private static readonly JsonDocument s_emptyObjDoc = JsonDocument.Parse("{}");
        private static JsonElement EmptyObject => s_emptyObjDoc.RootElement;

        // --- Yeni isimler (GoogleBooksClient’ın kullandıkları) ---

        // el["name"] bir object ise onu döndür, yoksa boş obje
        public static JsonElement GetObjectProp(this JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return EmptyObject;
            return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : EmptyObject;
        }

        // el["name"]’i string’e çevir (number/bool ise ToString, object ise "value" alt alanını dener)
        public static string GetStringProp(this JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return "";
            if (!el.TryGetProperty(name, out var v)) return "";

            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.ToString(),
                JsonValueKind.True or JsonValueKind.False => v.GetBoolean().ToString(),
                JsonValueKind.Object =>
                    (v.TryGetProperty("value", out var inner) && inner.ValueKind == JsonValueKind.String)
                        ? (inner.GetString() ?? "")
                        : v.GetRawText(),
                _ => ""
            };
        }

        // el["name"] bir dizi ise string listesi döndür (non-string öğeleri de makul şekilde stringe çevir)
        public static IEnumerable<string> GetArrayStrings(this JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return Enumerable.Empty<string>();
            if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Enumerable.Empty<string>();

            return arr.EnumerateArray()
                      .Select(x => x.ValueKind switch
                      {
                          JsonValueKind.String => x.GetString() ?? "",
                          JsonValueKind.Number => x.ToString(),
                          JsonValueKind.True or JsonValueKind.False => x.GetBoolean().ToString(),
                          JsonValueKind.Object =>
                              (x.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                                  ? (val.GetString() ?? "")
                                  : x.GetRawText(),
                          _ => x.ToString()
                      })
                      .Where(s => !string.IsNullOrWhiteSpace(s));
        }

        public static int? GetInt32OrNull(this JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty(name, out var v)) return null;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
            return null;
        }

        // --- Geriye uyumluluk (OpenLibraryClient veya eski çağrılar için) ---

        public static JsonElement GetObjectOrEmpty(this JsonElement el, string name)
            => GetObjectProp(el, name);

        public static string GetStringOrEmpty(this JsonElement el, string name)
            => GetStringProp(el, name);

        public static IEnumerable<string> GetArrayOrEmpty(this JsonElement el, string name)
            => GetArrayStrings(el, name);

        // Eski imza: isObject=true ise raw object JSON string’i döndürür
        public static string GetPropertyOrDefault(this JsonElement el, string name, bool isObject = false)
            => isObject ? GetObjectProp(el, name).GetRawText() : GetStringProp(el, name);
    }
}

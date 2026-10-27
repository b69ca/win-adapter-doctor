using System.Text.Json.Nodes;
namespace WinDiagnostics;
public static class V
{
    public static string Text(JsonObject row, string key)
    {
        if (row[key] is not JsonValue v || !v.TryGetValue<string>(out var s) || s.Length > 8192)
            throw new ArgumentException($"'{key}' must be a string of at most 8192 characters.");
        return s;
    }
    public static long Int(JsonObject row, string key, long min, long max)
    {
        if (row[key] is not JsonValue v || !long.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new ArgumentException($"'{key}' must be an integer from {min} to {max}.");
        return n;
    }
    public static void Real(JsonObject row, string key, double min, double max, bool nullable = false)
    {
        if (!row.ContainsKey(key)) throw new ArgumentException($"Missing '{key}'.");
        if (nullable && row[key] is null) return;
        if (row[key] is not JsonValue v || !double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < min || n > max)
            throw new ArgumentException($"'{key}' is outside its numeric range.");
    }
    public static void Bool(JsonObject row, string key)
    {
        if (row[key] is not JsonValue v || !v.TryGetValue<bool>(out _)) throw new ArgumentException($"'{key}' must be boolean.");
    }
    public static void Rows(JsonObject e, string key, int maximum)
    {
        J.RequireRows(e, key);
        if (((JsonArray)e[key]!).Count > maximum) throw new ArgumentException($"'{key}' exceeds {maximum} rows.");
    }
    public static void Strings(JsonObject row, string key)
    {
        if (row[key] is not JsonArray a || a.Count > 4096 || a.Any(x => x is not JsonValue v || !v.TryGetValue<string>(out _)))
            throw new ArgumentException($"'{key}' must be an array of strings (at most 4096).");
    }
}

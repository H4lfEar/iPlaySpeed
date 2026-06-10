using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPlaySpeed.Core;

/// <summary>
/// 사람이 읽을 수 있는 JSON 파일로 데이터를 저장/로드하는 단순 저장소.
/// 데이터가 많아지면 나중에 SQLite로 교체할 수 있도록 인터페이스를 좁게 유지한다.
/// </summary>
public sealed class JsonStore
{
    private readonly string _baseDir;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonStore(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    public string PathFor(string fileName) => Path.Combine(_baseDir, fileName);

    /// <summary>파일을 읽어 역직렬화. 없으면 fallback 반환.</summary>
    public T Load<T>(string fileName, T fallback)
    {
        string path = PathFor(fileName);
        if (!File.Exists(path))
            return fallback;
        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return fallback;
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch
        {
            // 손상된 파일이면 백업해 두고 기본값으로 복구.
            TryBackupCorrupt(path);
            return fallback;
        }
    }

    /// <summary>객체를 임시 파일에 쓴 뒤 교체(원자적)하여 저장 중 손상을 방지.</summary>
    public void Save<T>(string fileName, T value)
    {
        string path = PathFor(fileName);
        string tmp = path + ".tmp";
        string json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            string bak = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(path, bak, overwrite: true);
        }
        catch { /* 백업 실패는 무시 */ }
    }
}

using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace ClaudeAccountSwitcher.Localization;

/// <summary>
/// 언어별 JSON(Localization/&lt;culture&gt;.json)에서 문자열을 읽어 제공한다.
/// 지원 언어 목록은 하드코딩하지 않고, exe 에 임베드된 Localization/*.json 들을 런타임에 스캔해 구성한다.
/// 각 json 은 자신의 메타데이터("_culture", "_name")를 담는다. 새 언어 = json 파일 추가만 하면 끝.
/// 인덱서(this[key])에 OneWay 바인딩하면(LocExtension) 언어 변경 시 실시간으로 갱신된다.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string Fallback = "en-US";
    private const string MetaCulture = "_culture";
    private const string MetaName = "_name";

    public static LocalizationManager Instance { get; } = new();

    /// <summary>지원 언어: (컬처 코드, 표시 이름). Localization/*.json 스캔으로 구성된다.</summary>
    public static (string Culture, string Display)[] Available => Instance._available;

    private readonly Dictionary<string, Dictionary<string, string>> _all =
        new(StringComparer.OrdinalIgnoreCase);
    private (string Culture, string Display)[] _available = [];
    private string _culture = Fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>언어가 바뀌어 코드로 만든 문자열(트레이 메뉴/상태 등)도 다시 그려야 할 때 발생.</summary>
    public event Action? LanguageChanged;

    private LocalizationManager()
    {
        var found = new List<(string Culture, string Display)>();
        foreach (var (key, dict) in DiscoverLocales())
        {
            string culture = dict.TryGetValue(MetaCulture, out var c) && !string.IsNullOrWhiteSpace(c)
                ? c.Trim()
                : CultureFromResourceKey(key);
            if (string.IsNullOrEmpty(culture)) continue;

            string display = dict.TryGetValue(MetaName, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n.Trim()
                : culture;

            _all[culture] = dict;
            found.Add((culture, display));
        }

        // 표시 이름 기준으로 결정적 정렬.
        _available = found.OrderBy(x => x.Display, StringComparer.CurrentCulture).ToArray();

        // 스캔이 실패하더라도 앱이 죽지 않도록 최소한의 폴백을 보장한다.
        if (!_all.ContainsKey(Fallback)) _all[Fallback] = new();
        if (_available.Length == 0) _available = [(Fallback, "English")];
    }

    /// <summary>현재 컬처. 설정 시 UI 스레드 컬처도 갱신하고 모든 바인딩을 새로고침한다.</summary>
    public string Culture
    {
        get => _culture;
        set
        {
            if (string.IsNullOrEmpty(value) || !_all.ContainsKey(value) || value == _culture) return;
            _culture = value;

            try
            {
                var ci = new CultureInfo(value);
                CultureInfo.CurrentUICulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
            }
            catch { /* 표시 문자열만 바꾸면 되므로 스레드 컬처 실패는 무시 */ }

            // 인덱서 바인딩 + 전체 갱신
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>키에 해당하는 번역. 없으면 영어 폴백, 그래도 없으면 키 자체.</summary>
    public string this[string key]
    {
        get
        {
            if (_all.TryGetValue(_culture, out var d) && d.TryGetValue(key, out var v)) return v;
            if (_all.TryGetValue(Fallback, out var f) && f.TryGetValue(key, out var fv)) return fv;
            return key;
        }
    }

    /// <summary>코드에서 즉시 조회(바인딩 아님). 포맷 인자 지원.</summary>
    public string Tr(string key, params object[] args)
    {
        var s = this[key];
        return args.Length == 0 ? s : string.Format(s, args);
    }

    /// <summary>exe 에 임베드된 Localization/*.json 들을 (리소스 키, 파싱된 사전)으로 열거한다.</summary>
    private static IEnumerable<(string Key, Dictionary<string, string> Dict)> DiscoverLocales()
    {
        var asm = Assembly.GetExecutingAssembly();
        // WPF 'Resource' 빌드 항목들은 <AssemblyName>.g.resources 안에 들어간다.
        var resName = asm.GetName().Name + ".g.resources";
        Stream? stream = null;
        ResourceReader? reader = null;
        try
        {
            stream = asm.GetManifestResourceStream(resName);
            if (stream is null) yield break;
            reader = new ResourceReader(stream);
        }
        catch
        {
            stream?.Dispose();
            yield break;
        }

        using (reader)
        using (stream)
        {
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key) continue;
                if (!key.StartsWith("localization/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                Dictionary<string, string>? dict = null;
                try
                {
                    if (entry.Value is Stream s)
                    {
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ms.ToArray());
                    }
                }
                catch { dict = null; }

                if (dict is not null) yield return (key, dict);
            }
        }
    }

    /// <summary>리소스 키(예: "localization/en-us.json")에서 컬처 코드를 복원한다.</summary>
    private static string CultureFromResourceKey(string key)
    {
        var file = Path.GetFileNameWithoutExtension(key);
        try { return CultureInfo.GetCultureInfo(file).Name; }
        catch { return file; }
    }

    /// <summary>첫 실행 기본 언어: 윈도우 UI 언어와 가장 잘 맞는 지원 언어. 없으면 en-US.</summary>
    public static string ResolveDefaultCulture()
    {
        try
        {
            var ci = CultureInfo.InstalledUICulture;

            // 1) 정확히 일치하는 컬처(예: ja-JP)
            foreach (var (culture, _) in Available)
                if (culture.Equals(ci.Name, StringComparison.OrdinalIgnoreCase))
                    return culture;

            // 2) 중국어는 간체/번체 구분
            if (ci.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                bool traditional = ci.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                    || ci.Name.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
                    || ci.Name.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
                    || ci.Name.EndsWith("MO", StringComparison.OrdinalIgnoreCase);
                var want = traditional ? "zh-TW" : "zh-CN";
                if (Available.Any(a => a.Culture.Equals(want, StringComparison.OrdinalIgnoreCase)))
                    return want;
            }

            // 3) 두 글자 언어 코드로 매칭(예: fr-CA → fr-FR)
            foreach (var (culture, _) in Available)
                if (culture.StartsWith(ci.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase))
                    return culture;
        }
        catch { /* 아래 폴백 */ }
        return Fallback;
    }
}

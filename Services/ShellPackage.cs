using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace ClaudeAccountSwitcher.Services;

/// <summary><see cref="ShellPackage"/> 작업 결과. Cancelled = 사용자가 UAC 를 거절(오류로 알리지 않는다).</summary>
public enum ShellPackageOutcome
{
    Done,
    Cancelled,
    Failed,
}

/// <summary>
/// Windows 11 새 우클릭 메뉴용 스파스 패키지(설치 폴더 <c>shell\</c> 의 .msix + 셸 확장 DLL) 등록/해제.
/// <para>
/// 새 메뉴는 패키지 ID 가 있는 앱의 IExplorerCommand 만 보여 준다(레지스트리 동사는 "더 많은 옵션 표시" 뒤로 밀리고,
/// 2026-11 업데이트부터는 그 항목마저 기본으로 꺼져 있다). 그래서 설치 폴더를 외부 위치로 삼는 작은 패키지를
/// 현재 사용자에게 등록한다. 패키지는 자체 서명이라, 처음 한 번은 관리자 권한으로 인증서를
/// <c>LocalMachine\TrustedPeople</c> 에 넣어야 한다(배포 서비스가 SYSTEM 으로 돌아 사용자 저장소는 보지 않는다).
/// </para>
/// 메뉴 내용(제목·계정 목록)은 여기서 다루지 않는다 — 셸 확장이 <see cref="ExplorerMenu"/> 가 쓴 레지스트리를 읽는다.
/// </summary>
public static class ShellPackage
{
    /// <summary>관리자 권한으로 다시 띄운 자신에게 "인증서만 신뢰 등록하고 끝내라"고 알리는 인자.</summary>
    public const string TrustCertificateArgument = "--trust-package-cert";

    private const string RepositoryKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string CertificateResource = "PackageSigning.cer";
    private const int Windows11Build = 22000;
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: UAC 거절
    private const int CommandTimeoutMs = 90_000;

    private static string InstallDir => AppContext.BaseDirectory.TrimEnd('\\');
    private static string PackagePath => Path.Combine(InstallDir, "shell", "Claude-Account-Switcher.msix");
    private static string HandlerPath => Path.Combine(InstallDir, "shell", "Claude-Account-Switcher.ShellExtension.dll");

    /// <summary>Windows 11 이고, 이 빌드에 서명된 패키지가 들어 있는지(서명 키 없이 만든 빌드에는 없다).</summary>
    public static bool IsSupported =>
        Environment.OSVersion.Version.Build >= Windows11Build && File.Exists(PackagePath) && File.Exists(HandlerPath);

    /// <summary>버전·위치와 상관없이 현재 사용자에게 패키지가 등록돼 있는지.</summary>
    public static bool IsRegistered => Registered() is not null;

    /// <summary>이 버전이 이 설치 폴더를 가리키게 등록돼 있는지(업데이트·재설치 뒤에는 false → 다시 등록).</summary>
    public static bool IsUpToDate =>
        Registered() is var (version, root)
        && version == Normalize(UpdateService.CurrentVersion)
        && IsInstallDir(root);

    public static bool IsCertificateTrusted
    {
        get
        {
            try
            {
                using var certificate = LoadCertificate();
                using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                return store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count > 0;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// 새 메뉴를 켠다: 인증서가 아직 신뢰되지 않았으면 UAC 로 등록한 뒤 패키지를 등록한다. 오래 걸릴 수 있으니(수 초) UI 스레드 밖에서 부를 것.
    /// </summary>
    public static (ShellPackageOutcome Outcome, string Detail) Enable()
    {
        if (!IsSupported) return (ShellPackageOutcome.Failed, "");
        if (!IsCertificateTrusted)
        {
            try
            {
                using var elevated = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, TrustCertificateArgument) { UseShellExecute = true, Verb = "runas" });
                elevated?.WaitForExit();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { return (ShellPackageOutcome.Cancelled, ""); }
            catch (Exception ex) { return (ShellPackageOutcome.Failed, ex.Message); }

            if (!IsCertificateTrusted) return (ShellPackageOutcome.Failed, "");
        }
        return Register();
    }

    /// <summary>
    /// 패키지를 (다시) 등록한다. 권한 상승 없음 — 인증서가 신뢰돼 있어야 성공한다.
    /// 다른 폴더를 가리키는 등록이 남아 있으면(재설치로 위치가 바뀜) 먼저 지운다 — 같은 버전은 덮어써도 위치가 안 바뀐다.
    /// 더 높은 버전이 남아 있어 추가가 거부되면 지우고 다시 넣는다.
    /// </summary>
    public static (ShellPackageOutcome Outcome, string Detail) Register()
    {
        bool relocated = Registered() is var (_, root) && !IsInstallDir(root);
        return RunPowerShell($$"""
            {{(relocated ? RemoveCommand : "")}}
            try { Add-AppxPackage -Path $env:{{PackagePathVariable}} -ExternalLocation $env:{{InstallDirVariable}} }
            catch {
                {{RemoveCommand}}
                Add-AppxPackage -Path $env:{{PackagePathVariable}} -ExternalLocation $env:{{InstallDirVariable}}
            }
            """);
    }

    /// <summary>패키지 등록을 푼다(인증서는 그대로 둔다 — 다시 켤 때 UAC 가 또 뜨지 않게).</summary>
    public static (ShellPackageOutcome Outcome, string Detail) Unregister() => RunPowerShell(RemoveCommand);

    // 경로는 스크립트 본문에 끼워 넣지 않고 환경 변수로 넘긴다 — 사용자 이름에 따옴표류 문자(' ’ 등)가 있어도 깨지지 않는다.
    private const string PackagePathVariable = "CAS_PACKAGE_PATH";
    private const string InstallDirVariable = "CAS_INSTALL_DIR";
    private const string RemoveCommand = $"Get-AppxPackage -Name '{ExplorerMenuContract.PackageName}' | Remove-AppxPackage";

    private static bool IsInstallDir(string path) => string.Equals(path.TrimEnd('\\'), InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 관리자 권한 인스턴스가 하는 일의 전부: 임베드된 공개 인증서를 LocalMachine\TrustedPeople 에 넣는다. 반환값 = 프로세스 종료 코드.
    /// 파일이 아니라 exe 에 든 인증서만 쓰므로, 설치 폴더의 파일을 바꿔치기해 다른 인증서를 신뢰시키는 일은 생기지 않는다.
    /// </summary>
    public static int TrustCertificateElevated()
    {
        try
        {
            using var certificate = LoadCertificate();
            using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            return 0;
        }
        catch { return 1; }
    }

    private static X509Certificate2 LoadCertificate()
    {
        using var stream = typeof(ShellPackage).Assembly.GetManifestResourceStream(CertificateResource)
            ?? throw new InvalidOperationException("Package signing certificate is not embedded.");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return X509CertificateLoader.LoadCertificate(buffer.ToArray());
    }

    /// <summary>등록된 패키지의 (버전, 외부 위치). 전체 이름은 <c>이름_버전_아키텍처__게시자ID</c>.</summary>
    private static (Version Version, string Root)? Registered()
    {
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(RepositoryKey);
            string? fullName = packages?.GetSubKeyNames()
                .FirstOrDefault(n => n.StartsWith(ExplorerMenuContract.PackageName + "_", StringComparison.OrdinalIgnoreCase));
            if (fullName is null) return null;

            using var package = packages!.OpenSubKey(fullName);
            Version.TryParse(fullName.Split('_').ElementAtOrDefault(1), out var version);
            return (Normalize(version ?? new Version()), package?.GetValue("PackageRootFolder") as string ?? "");
        }
        catch { return null; }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>
    /// Windows PowerShell 의 Appx cmdlet 으로 배포 작업을 돌린다(창 없음). 실패 사유는 표준 출력으로 받아 돌려준다 —
    /// 리디렉션된 stderr 는 CLIXML 로 감싸져 나와 그대로는 읽기 어렵다. 출력은 UTF-8 로 맞춘다(기본은 OEM 코드 페이지라
    /// 현지화된 오류 문구가 깨진다).
    /// </summary>
    private static (ShellPackageOutcome Outcome, string Detail) RunPowerShell(string body)
    {
        string script = $$"""
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            try {
            {{body}}
            }
            catch { [Console]::Out.WriteLine($_.Exception.Message); exit 1 }
            """;
        try
        {
            ProcessStartInfo info = new(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (string arg in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))])
                info.ArgumentList.Add(arg);
            // PowerShell 7 에서 띄워진 경우 물려받은 모듈 경로 때문에 Appx 모듈 로드가 깨질 수 있다 → 기본값으로 되돌린다.
            info.Environment.Remove("PSModulePath");
            info.Environment[PackagePathVariable] = PackagePath;
            info.Environment[InstallDirVariable] = InstallDir;

            using var process = Process.Start(info);
            if (process is null) return (ShellPackageOutcome.Failed, "");
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(CommandTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 이미 끝났으면 무시 */ }
                return (ShellPackageOutcome.Failed, "timeout");
            }
            return process.ExitCode == 0 ? (ShellPackageOutcome.Done, "") : (ShellPackageOutcome.Failed, output.Result.Trim());
        }
        catch (Exception ex) { return (ShellPackageOutcome.Failed, ex.Message); }
    }
}

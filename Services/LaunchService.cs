using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// "이 계정으로 claude 를 띄운다"는 사용자 흐름(스킵권한 결정 → 폴더 결정 → 세션 복사 → 실행)을 한곳에 모은다.
/// 관리 창의 [새 창 실행]·행 메뉴의 최근 세션 이어하기·세션 브라우저·트레이 메뉴가 모두 이 흐름을 공유한다.
/// 저수준 프로세스 실행은 <see cref="Launcher"/>, 여기서는 사용자와의 대화(확인/선택)와 순서를 담당한다.
/// </summary>
public sealed class LaunchService(ProfileStore store, SessionStore sessions, IDialogService dialogs)
{
    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>스킵 권한 값을 결정한다. 기억값이 있으면 그대로, 없으면 다이얼로그로 묻는다. 취소 시 null.</summary>
    public bool? ResolveSkipPermissions(string profileName)
    {
        if (store.Data.SkipPermissions is bool remembered) return remembered;

        var res = dialogs.AskSkipPermissions(profileName);
        if (res is null) return null;

        if (res.Remember)
        {
            store.Data.SkipPermissions = res.SkipPermissions;
            store.Save();
        }
        return res.SkipPermissions;
    }

    /// <summary>작업 폴더를 묻고 이 계정으로 새 창을 띄운다. 어느 단계에서든 취소하면 false.</summary>
    public bool LaunchNewWindow(Profile p)
    {
        string start = store.Data.LastWorkingDir is { } last && Directory.Exists(last) ? last : AppPaths.UserHome;
        string? folder = dialogs.PickFolder(L.Tr("MsgLaunchFolderTitle", p.Name), start);
        return folder is not null && LaunchNewWindow(p, folder);
    }

    /// <summary>지정 폴더에서 이 계정으로 새 창을 띄운다. 스킵권한을 묻는 단계에서 취소하면 false.</summary>
    public bool LaunchNewWindow(Profile p, string workingDir)
    {
        if (ResolveSkipPermissions(p.Name) is not { } skip) return false;

        Launcher.LaunchInProfile(p, workingDir, store.Data.Shell, skip, store.Data.StatusLine, runAsAdmin: store.Data.RunAsAdmin);
        store.Data.LastWorkingDir = workingDir;
        p.LastUsed = DateTime.Now;
        store.Save();
        return true;
    }

    /// <summary>
    /// 세션을 <paramref name="dest"/> 계정으로 이어한다(같은 계정이면 그 자리에서 resume, 다른 계정이면 사본을 만든 뒤).
    /// 원본 작업 폴더가 없으면 번들 복원/폴더 선택을 안내한다. 실행까지 갔으면 true, 어느 단계에서든 취소하면 false.
    /// </summary>
    public async Task<bool> ResumeAsync(SessionEntry entry, Profile dest)
    {
        // resume 은 어떤 작업 폴더에서 실행되고, 세션 파일은 그 폴더의 enc 폴더에서 찾는다.
        // 원본 cwd 가 이 PC 에 있으면 그대로, 없으면(다른 PC 로 옮겨온 경우 등) 번들에 담긴 폴더를 복원하거나
        // 사용자가 폴더를 고르고, 그 폴더 기준으로 enc 를 다시 계산해 복사한다.
        if (await ResolveTargetFolderAsync(entry) is not var (cwd, projectFolder)) return false;

        // 다른 계정으로 이어하면 이후 대화는 대상 계정 사본에 쌓인다(원본 보존). 한 번 확인.
        bool crossAccount = dest.Id != entry.ProfileId;
        if (crossAccount && !dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessResumeConfirm", entry.ProfileName, dest.Name)))
            return false;

        if (ResolveSkipPermissions(dest.Name) is not { } skip) return false;

        // 대상에 같은 세션의 사본이 이미 있고 양쪽이 갈라졌을 때만 물어본다(한쪽만 진행됐으면 자동으로 최신 쪽).
        string id = sessions.ImportInto(entry, dest, projectFolder,
            c => dialogs.Confirm(L["SessResumeTitle"],
                L.Tr("SessReplaceAsk", dest.Name, c.SourceModified.ToString("g"), c.DestModified.ToString("g"))));
        Launcher.LaunchInProfile(dest, cwd, store.Data.Shell, skip, store.Data.StatusLine, resumeSessionId: id, runAsAdmin: store.Data.RunAsAdmin);
        dest.LastUsed = DateTime.Now;
        store.Save();
        return true;
    }

    /// <summary>
    /// 이어할 작업 폴더와 그에 맞는 enc 폴더명을 정한다. 원본 cwd 가 있으면 그대로;
    /// 없고 번들에 폴더가 담겼으면 사용자가 고른 위치로 복원해 그 폴더에서; 그 외엔 폴더를 직접 물어
    /// enc 를 다시 계산한다. 취소하면 null.
    /// </summary>
    private async Task<(string Cwd, string ProjectFolder)?> ResolveTargetFolderAsync(SessionEntry entry)
    {
        // 원본 폴더가 이 PC 에 있으면 그대로 사용(로컬 파일 재사용).
        if (!string.IsNullOrEmpty(entry.Cwd) && Directory.Exists(entry.Cwd))
            return (entry.Cwd, entry.ProjectFolder);

        // (A) 번들에 작업 폴더가 함께 담김 → 복원할지 물어보고, 예면 위치를 골라 복원.
        if (entry.BundleWorkdirPath is { Length: > 0 } bundled && Directory.Exists(bundled))
        {
            string folderName = FolderNameOf(entry.Cwd);
            if (dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessRestoreFolderAsk", folderName)))
            {
                string? parent = dialogs.PickFolder(L["SessRestoreParentTitle"], AppPaths.UserHome);
                if (string.IsNullOrEmpty(parent)) return null;

                string destFolder = Path.Combine(parent, folderName);
                bool ok = await dialogs.RunWithProgress(L["SessResumeTitle"], L.Tr("SessRestoring", folderName),
                    (progress, ct) => Task.Run(() => SessionBundle.RestoreWorkdir(bundled, destFolder, progress, ct), ct));
                return ok ? (destFolder, ProjectFolderEncoder.Encode(destFolder)) : null;
            }
            // 아니오 → 아래 '폴더 직접 선택'으로 넘어간다.
        }

        // (B) 폴더가 안 담겼거나 복원을 원치 않음 → 이어할 폴더를 직접 고르게 안내.
        string shown = string.IsNullOrEmpty(entry.Cwd) ? "?" : entry.Cwd;
        if (!dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessPickFolderAsk", shown))) return null;

        string? folder = dialogs.PickFolder(L["SessPickFolderTitle"], AppPaths.UserHome);
        return string.IsNullOrEmpty(folder) ? null : (folder, ProjectFolderEncoder.Encode(folder));
    }

    /// <summary>작업 폴더 이름(cwd 의 마지막 구성요소). 복원 시 상위 폴더 아래 이 이름으로 만든다.</summary>
    private static string FolderNameOf(string cwd)
    {
        string n = Path.GetFileName(cwd.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(n) ? "project" : n;
    }
}

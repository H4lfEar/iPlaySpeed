using System.IO;

namespace IPlaySpeed.App.Services;

/// <summary>게임 폴더의 총 용량과 최종 수정일을 구한다(패치 감지용).</summary>
public static class FolderScanner
{
    /// <summary>
    /// 폴더를 재귀 스캔해 (총 byte, 최종 수정일)을 반환.
    /// 접근 불가 파일/폴더는 건너뛴다. 폴더가 없으면 (0, null).
    /// 큰 폴더는 느릴 수 있으니 호출자는 백그라운드에서 실행할 것.
    /// </summary>
    public static (long totalBytes, DateOnly? lastWrite) Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return (0, null);

        long total = 0;
        DateTime? last = null;

        var stack = new Stack<string>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (string f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    total += fi.Length;
                    if (last is null || fi.LastWriteTime > last)
                        last = fi.LastWriteTime;
                }
                catch { /* 잠긴/사라진 파일 무시 */ }
            }

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir))
                    stack.Push(sub);
            }
            catch { /* 접근 거부 무시 */ }
        }

        return (total, last is null ? null : DateOnly.FromDateTime(last.Value));
    }
}

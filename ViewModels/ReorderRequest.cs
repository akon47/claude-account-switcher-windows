namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>행 드래그 순서 변경 요청. <see cref="Item"/> 을 <see cref="InsertIndex"/>(0~Count) 앞에 끼워 넣는다.</summary>
public sealed record ReorderRequest(object Item, int InsertIndex);

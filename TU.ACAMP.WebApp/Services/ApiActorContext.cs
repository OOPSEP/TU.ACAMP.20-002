namespace TU.ACAMP.WebApp.Services;

/// <summary>
/// [2026-09-23][Claude] ผู้กระทำ (actor) ของคำขอที่มาจาก REST API ซึ่งไม่มี AuthenticationState ของ Blazor
/// ใช้ AsyncLocal จึงไหลตาม await ภายใน request เดียวกันเท่านั้น
/// ต้องเรียก Begin() ใน action method โดยตรง (ถ้าเรียกใน helper async แล้ว return ค่าจะถูกคืนกลับเมื่อ helper จบ)
/// </summary>
public static class ApiActorContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    /// <summary>กำหนด actor จนกว่าจะ Dispose (ใช้กับ using)</summary>
    public static IDisposable Begin(string actor)
    {
        var previous = _current.Value;
        _current.Value = actor;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}

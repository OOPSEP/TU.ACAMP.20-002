using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services;

public abstract class BaseService
{
    #region class field
    protected readonly IDbContextFactory<ApplicationDbContext> dbFactory;
    protected readonly AuthenticationStateProvider authStateProvider;
    #endregion

    #region class methods
    protected BaseService(IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider authStateProvider)
    {
        #region BaseService [2026-08-15]
        this.dbFactory = dbFactory;
        this.authStateProvider = authStateProvider;
        #endregion
    }

    protected async Task<string> GetCurrentUserIdAsync()
    {
        #region hide [2026-08-15]
        // [2026-09-23][Claude] คำขอจาก REST API ไม่มี Blazor circuit: AuthenticationStateProvider จะ throw
        //     InvalidOperationException ("Do not call GetAuthenticationStateAsync outside of the DI scope for a Razor component")
        //     จึงใช้ actor ที่ controller กำหนดผ่าน ApiActorContext ก่อน และ fallback เป็น "System" เมื่อไม่มี auth state
        if (!string.IsNullOrWhiteSpace(ApiActorContext.Current))
        {
            return ApiActorContext.Current!;
        }

        AuthenticationState authState;
        try
        {
            authState = await authStateProvider.GetAuthenticationStateAsync();
        }
        catch (InvalidOperationException)
        {
            return "System";
        }

        var email = authState.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var userName = authState.User.Identity?.Name;
        return !string.IsNullOrWhiteSpace(email) ? email : (userName ?? "System");
        #endregion
    }

    protected async Task<string> GetCurrentUserEmailAsync()
    {
        #region hide [2026-08-15]
        return await GetCurrentUserIdAsync();
        #endregion
    }

    public async Task<ApplicationDbContext> CreateContextAsync()
    {
        #region hide [2026-08-15]
        var context = await dbFactory.CreateDbContextAsync();
        context.CurrentUsername = await GetCurrentUserIdAsync();
        return context;
        #endregion
    }
    #endregion
}

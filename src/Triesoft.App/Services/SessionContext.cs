using Triesoft.Core.Identity;

namespace Triesoft.App.Services;

/// <summary>User yang sedang login di sesi aplikasi ini saat ini (kalau ada).</summary>
public sealed class SessionContext
{
    public UserAccount? CurrentUser { get; private set; }

    public void SignIn(UserAccount user) => CurrentUser = user;
    public void SignOut() => CurrentUser = null;
}

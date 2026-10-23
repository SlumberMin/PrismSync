// PrismSync native app - elevation check shared by the app and the adapters.
using System.Security.Principal;

namespace PrismSync
{
    internal static class AdminCheck
    {
        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}

namespace TU.ACAMP.WebApp.Components.Pages.Identity.Widgets;

/// <summary>
/// Options for batch subscription activation
/// </summary>
public enum ActivateBatchChoice
{
    /// <summary>
    /// Cancel operation
    /// </summary>
    Cancel = 0,

    /// <summary>
    /// Activate subscription and send notification email to user
    /// </summary>
    WithEmail = 1,

    /// <summary>
    /// Activate subscription without sending notification email
    /// </summary>
    WithoutEmail = 2
}

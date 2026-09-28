using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Identity;

public class EmailSender : IEmailSender<ApplicationUser>
{
    #region class field  
    private readonly ILogger<EmailSender> _logger;
    private readonly IConfiguration _configuration;
    #endregion

    #region class constructor
    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        #region hide [2026-08-15]
        _configuration = configuration;
        _logger = logger;
        #endregion
    }
    #endregion

    #region class method 
    public async Task SendEmailAsync_003(string recipientEmail, string subject, string body, string? ccEmail = null)
    {
        #region hide [2026-08-16]
        try
        {
            var smtpFrom = _configuration["Smtp:from"] ?? _configuration["Smtp:From"] ?? "noreply@acamp.com";
            var smtpHost = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
            var smtpPortStr = _configuration["Smtp:Port"] ?? "587";
            var smtpPort = int.TryParse(smtpPortStr, out var p) ? p : 587;
            var smtpUser = _configuration["Smtp:User"] ?? string.Empty;
            var smtpPass = _configuration["Smtp:Password"] ?? string.Empty;

            // Fail-Fast Validation: Ensure credentials are configured before connecting to SMTP
            if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
            {
                var errorMsg = $"[SMTP Configuration Incomplete] SMTP username or password is not configured (Smtp:User or Smtp:Password is empty). " +
                               $"Please configure via dotnet user-secrets for Development or Environment Variables for Production " +
                               $"(e.g., dotnet user-secrets set \"Smtp:User\" \"example@gmail.com\" && dotnet user-secrets set \"Smtp:Password\" \"16-character-app-password\").";
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            // Verify SMTP port for SmtpClient (System.Net.Mail supports STARTTLS on ports 587/25; does not support Implicit TLS on port 465)
            if (smtpPort == 465)
            {
                var portMsg = $"[SMTP Port Error] Port 465 (Implicit TLS/SSL) is not supported by System.Net.Mail.SmtpClient. " +
                              $"Please use Port 587 with STARTTLS or consider using MailKit for SMTPS on Port 465.";
                _logger.LogError(portMsg);
                throw new NotSupportedException(portMsg);
            }

            // [2026-09-28][Gemini] Safety Guard: ดักจับและป้องกันการส่งอีเมลออกจริงไปยังโดเมนทดสอบหรือ @acamp.com (ซึ่งมีเจ้าของจริงบนอินเทอร์เน็ต)
            var testDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "acamp.com", "example.com", "example.org", "example.net", "test.local", "dummy.com"
            };

            var recipientDomain = recipientEmail.Contains('@') ? recipientEmail.Split('@').Last().Trim() : string.Empty;
            var isRecipientTest = testDomains.Contains(recipientDomain);

            var ccDomain = !string.IsNullOrWhiteSpace(ccEmail) && ccEmail.Contains('@') ? ccEmail.Split('@').Last().Trim() : string.Empty;
            var isCcTest = !string.IsNullOrWhiteSpace(ccDomain) && testDomains.Contains(ccDomain);

            if (isCcTest)
            {
                ccEmail = null;
            }

            if (isRecipientTest)
            {
                if (!string.IsNullOrWhiteSpace(ccEmail))
                {
                    // หากมี CC ที่เป็นอีเมลจริง (เช่น patipath.eia@nectec.or.th) ให้ Redirect ไปส่งหา CC โดยตรง เพื่อให้ผู้พัฒนาตรวจสอบเนื้อหาอีเมลได้โดยไม่ส่งไปยังโดเมนทดสอบ
                    _logger.LogInformation("[EmailSender Guard] Redirected test domain recipient ({Recipient}) to CC ({CcEmail}) for subject '{Subject}'", recipientEmail, ccEmail, subject);
                    recipientEmail = ccEmail.Trim();
                    ccEmail = null;
                    subject = $"[TEST REDIRECT] {subject}";
                }
                else
                {
                    // หากไม่มี CC จริง ให้ Bypass การส่งออกภายนอกเพื่อป้องกันการ Spam หรือข้อผิดพลาดจาก SMTP
                    _logger.LogInformation("[EmailSender Guard] Suppressed outgoing email to test domain: {Recipient} (Subject: {Subject})", recipientEmail, subject);
                    return;
                }
            }

            using var mail = new MailMessage();
            mail.From = new MailAddress(smtpFrom, "ACAMP System", Encoding.UTF8);
            mail.To.Add(new MailAddress(recipientEmail));

            if (!string.IsNullOrWhiteSpace(ccEmail))
            {
                mail.CC.Add(new MailAddress(ccEmail.Trim()));
            }

            mail.Subject = subject;
            mail.SubjectEncoding = Encoding.UTF8;

            // Alternative views
            var plainTextBody = "This is an HTML email. Please enable HTML view.";
            var plainView = AlternateView.CreateAlternateViewFromString(plainTextBody, Encoding.UTF8, MediaTypeNames.Text.Plain);
            var htmlView = AlternateView.CreateAlternateViewFromString(body, Encoding.UTF8, MediaTypeNames.Text.Html);

            mail.AlternateViews.Add(plainView);
            mail.AlternateViews.Add(htmlView);

            mail.BodyEncoding = Encoding.UTF8;
            mail.IsBodyHtml = true;
            mail.Body = body;

            using var smtp = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = smtpPort == 587 || smtpPort == 25,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(smtpUser.Trim(), smtpPass.Trim())
            };

            await smtp.SendMailAsync(mail);
            _logger.LogInformation("Email sent successfully to {Recipient} (Subject: {Subject})", recipientEmail, subject);
        }
        catch (SmtpException smtpEx)
        {
            _logger.LogError(smtpEx,
                "SMTP ERROR: {Message}. Host={Host}, User={User}, From={From}",
                smtpEx.Message,
                _configuration["Smtp:Host"],
                _configuration["Smtp:User"],
                _configuration["Smtp:from"]);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendEmailAsync_003 FAILED: {Message}", ex.Message);
            throw;
        }
        #endregion
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        #region hide [2026-08-15]
        string subject = "Password Reset Code";
        string body = $@"
        <html>
        <body>
            <h2>Password Reset Request</h2>
            <p>Dear {user.Email},</p>
            <p>We received a request to reset your password for your account associated with this email.</p>
            <p><strong>User ID (GUID):</strong> {user.Id}</p>
            <p><strong>Password Reset Code:</strong> <span style='font-weight: bold; color: red;'>{resetCode}</span></p>
            <p>Please use this code to reset your password within the next 30 minutes.</p>
            <p>If you did not request a password reset, please ignore this email. Your account remains secure.</p>
            <p>Best Regards,<br>The ACAMP Team</p>
        </body>
        </html>";

        await SendEmailAsync_003(email, subject, body);
        #endregion
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        #region hide [2026-08-15]
        string subject = "Reset Your Password";
        string body = $"Please click the following link to reset your password: <a href='{resetLink}'>{resetLink}</a>";

        await SendEmailAsync_003(email, subject, body);
        #endregion
    }

    public async Task SendPasswordChangedNotificationAsync(ApplicationUser user, string email)
    {
        #region hide [2026-08-21]
        try
        {
            var targetEmail = !string.IsNullOrWhiteSpace(email) ? email : user?.Email;
            if (string.IsNullOrWhiteSpace(targetEmail)) return;

            string subject = "[ACAMP] Security Alert: Your Password Was Changed";
            var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 8px; background-color: #ffffff;'>
                    <h2 style='color: #059669; border-bottom: 2px solid #059669; padding-bottom: 8px;'>Password Changed Successfully</h2>
                    <p>Dear {targetEmail},</p>
                    <p>This is an automated notification from the <strong>Automatic Carbon Account Management Platform (ACAMP)</strong> to inform you that your account password was changed on <strong>{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</strong>.</p>
                    <p>If you initiated this change, no further action is required.</p>
                    <p style='background-color: #fff3cd; color: #856404; padding: 12px; border-radius: 6px; border-left: 4px solid #ffeba8; font-size: 0.9em; margin-top: 20px;'>
                        <strong>Security Notice:</strong> If you did not authorize this change, please reset your password immediately or contact your administrator.
                    </p>
                    <br>
                    <p style='color: #888; font-size: 0.85em;'>Best regards,<br><strong>The ACAMP Team</strong></p>
                </div>
            </body>
            </html>";

            await SendEmailAsync_003(targetEmail, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password changed notification email.");
        }
        #endregion
    }

    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        #region hide [2026-08-15]
        string subject = "Confirm Your Email";
        var body = $@"
        <html>
        <body>
            <h2>Hello {user.Email},</h2>
            <p>Thank you for registering with the Automatic Carbon Account Management Platform (ACAMP). We are pleased to welcome you.</p>
            <p>To complete your registration, please confirm your email address by clicking the link below:</p>
            <p><a href='{confirmationLink}' style='color: blue; text-decoration: underline;'>Confirm Your Registration</a></p>
            <p>If you did not register for this account, you can safely ignore this email. No further action is required and no account will be activated.</p>
            <p>Best regards,<br>The ACAMP Team</p>
        </body>
        </html>";

        await SendEmailAsync_003(user.Email ?? email, subject, body);
        #endregion
    }

    public async Task SendEmailConfirmedByAdminNotificationAsync(ApplicationUser user, string email, string loginUrl)
    {
        #region hide [2026-08-16]
        string subject = "[ACAMP] Your Email Address Has Been Confirmed";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
            <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 8px; background-color: #ffffff;'>
                <h2 style='color: #1B365D; border-bottom: 2px solid #1B365D; padding-bottom: 8px;'>Email Address Confirmed</h2>
                <p>Dear {user.Email},</p>
                <p>Your email address has been successfully confirmed and approved by an Administrator on the <strong>Automatic Carbon Account Management Platform (ACAMP)</strong>.</p>
                <p>Your account is now fully active. You can sign in to the platform using your credentials by clicking the button below:</p>
                <p style='margin: 25px 0;'>
                    <a href='{loginUrl}' style='background-color: #059669; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold; display: inline-block;'>Sign In to ACAMP</a>
                </p>
                <p style='color: #888; font-size: 0.85em; margin-top: 30px;'>Best regards,<br><strong>The ACAMP Team</strong></p>
            </div>
        </body>
        </html>";

        await SendEmailAsync_003(user.Email ?? email, subject, body);
        #endregion
    }

    public async Task SendSubscriptionExpirationReminderAsync(string recipientEmail, string recipientName, string subscriptionName, string planName, DateTime expiryDate, int daysRemaining)
    {
        #region hide [2026-08-15]
        string subject = daysRemaining <= 0
            ? $"[Reminder] Subscription '{subscriptionName}' has expired"
            : $"[Reminder] Subscription '{subscriptionName}' will expire in {daysRemaining} days";

        string statusText = daysRemaining <= 0
            ? "<span style='color: #d9534f; font-weight: bold;'>Expired</span>"
            : $"<span style='color: #f0ad4e; font-weight: bold;'>Expires in {daysRemaining} days</span>";

        string nameDisplay = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail : recipientName;

        string body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
            <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 8px; background-color: #ffffff;'>
                <h2 style='color: #1B365D; border-bottom: 2px solid #1B365D; padding-bottom: 8px;'>Subscription Expiration Notice</h2>
                <p>Dear {nameDisplay},</p>
                <p>The ACAMP system would like to notify you regarding the expiration status of your subscription as detailed below:</p>
                
                <table style='width: 100%; border-collapse: collapse; margin: 20px 0;'>
                    <tr style='background-color: #f8f9fa;'>
                        <td style='padding: 10px; border: 1px solid #dee2e6; font-weight: bold;'>Subscription / Organization:</td>
                        <td style='padding: 10px; border: 1px solid #dee2e6;'>{subscriptionName}</td>
                    </tr>
                    <tr>
                        <td style='padding: 10px; border: 1px solid #dee2e6; font-weight: bold;'>Plan:</td>
                        <td style='padding: 10px; border: 1px solid #dee2e6;'>{planName}</td>
                    </tr>
                    <tr style='background-color: #f8f9fa;'>
                        <td style='padding: 10px; border: 1px solid #dee2e6; font-weight: bold;'>Expiration Date:</td>
                        <td style='padding: 10px; border: 1px solid #dee2e6;'>{expiryDate:dd/MM/yyyy}</td>
                    </tr>
                    <tr>
                        <td style='padding: 10px; border: 1px solid #dee2e6; font-weight: bold;'>Status:</td>
                        <td style='padding: 10px; border: 1px solid #dee2e6;'>{statusText}</td>
                    </tr>
                </table>

                <p style='color: #555;'>When a subscription expires, permissions to create or modify carbon footprint activity data will automatically be restricted to read-only access.</p>
                <p style='color: #555;'>Please contact the system administration team to renew your subscription.</p>
                <br>
                <p style='color: #888; font-size: 0.9em;'>Best regards,<br><b>The ACAMP Team (Automatic Carbon Account Management Platform)</b></p>
            </div>
        </body>
        </html>";

        await SendEmailAsync_003(recipientEmail, subject, body);
        #endregion
    }

    public async Task SendCustomAdminEmailAsync(string recipientEmail, string subject, string contentHtml)
    {
        #region hide [2026-08-15]
        string body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
            <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 8px; background-color: #ffffff;'>
                <h2 style='color: #1B365D; border-bottom: 2px solid #1B365D; padding-bottom: 8px;'>ACAMP System Announcement</h2>
                <div style='margin: 20px 0;'>
                    {contentHtml}
                </div>
                <hr style='border: 0; border-top: 1px solid #eee; margin-top: 30px;' />
                <p style='color: #888; font-size: 0.85em;'>This email was sent by an ACAMP System Administrator to {recipientEmail}.<br>Automatic Carbon Account Management Platform</p>
            </div>
        </body>
        </html>";

        await SendEmailAsync_003(recipientEmail, subject, body);
        #endregion
    }

    public async Task SendEmailCreateSubscribeAsync(TbSubscription item, string? ccEmail = null)
    {
        // [2026-09-28][Gemini] เปิดการส่ง CC สำหรับการแจ้งเตือนสร้าง Subscription ใหม่ (ส่งถึง Owner และ CC ไปยัง patipath.eia@nectec.or.th)
        var targetCc = !string.IsNullOrWhiteSpace(ccEmail) ? ccEmail : "patipath.eia@nectec.or.th";
        await SendEmailCreateSubscribeAsyncV2(item, IsccEmail: true, ccEmail: targetCc);
    }

    public async Task SendEmailCreateSubscribeAsyncV2(TbSubscription item, bool IsccEmail = false, string ccEmail = "patipath.eia@nectec.or.th")
    {
        #region hide [2026-08-15]
        try
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "Subscription item cannot be null.");

            var recipient = !string.IsNullOrWhiteSpace(item.OwnerEmail)
                ? item.OwnerEmail
                : item.ContactPerson;

            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger?.LogWarning("No valid recipient found for new subscription {Id}", item.Id);
                return;
            }

            var subject = $"[ACAMP] Subscription Created - {item.Title ?? "Untitled"}";

            var body = $@"
            <h3>New Subscription Created</h3>
            <p>Dear {(!string.IsNullOrWhiteSpace(item.ContactPerson) ? item.ContactPerson : (!string.IsNullOrWhiteSpace(item.OwnerName) ? item.OwnerName : "User"))},</p>
            <p>Your subscription has been created successfully in the ACAMP system.</p>
            <table style='border-collapse: collapse; width: 100%; margin: 15px 0;'>
                <tr><td style='padding: 6px; font-weight: bold;'>Title:</td><td style='padding: 6px;'>{item.Title}</td></tr>
                <tr><td style='padding: 6px; font-weight: bold;'>Plan:</td><td style='padding: 6px;'>{item.SubscriptionPlan}</td></tr>
                <tr><td style='padding: 6px; font-weight: bold;'>Start Date:</td><td style='padding: 6px;'>{item.StartDate:yyyy-MM-dd}</td></tr>
                <tr><td style='padding: 6px; font-weight: bold;'>Expiry Date:</td><td style='padding: 6px;'>{item.ExpiryDate:yyyy-MM-dd}</td></tr>
                <tr><td style='padding: 6px; font-weight: bold;'>Contact Email:</td><td style='padding: 6px;'>{item.OwnerEmail}</td></tr>
            </table>
            <p>You can manage this subscription on the ACAMP platform dashboard.</p>
            <hr>
            <p><i>Automatic Carbon Account Management Platform (ACAMP)</i></p>
        ";

            string? targetCc = IsccEmail ? ccEmail : null;
            await SendEmailAsync_003(recipient, subject, body, targetCc);

            _logger?.LogInformation("Sent subscription creation email V2 to {Email} (CC Enabled={IsCC}, CC Email={CC}) for SubscriptionId={Id}", recipient, IsccEmail, targetCc, item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending subscription creation email V2 for SubscriptionId={Id}", item?.Id);
        }
        #endregion
    }

    // [2026-09-25][Antigravity] DTO and method for sending GHG target threshold alerts
    public class GhgTargetAlertEmailModel
    {
        public string OrganizationName { get; set; } = string.Empty;
        public int ReportingYear { get; set; }
        public int BaseYear { get; set; }
        public decimal BaseYearEmissionsTon { get; set; }
        public decimal AnnualTargetCapTon { get; set; }
        public decimal CurrentEmissionsTon { get; set; }
        public decimal ConsumptionPercent { get; set; }
        public decimal Scope1Ton { get; set; }
        public decimal Scope2Ton { get; set; }
        public decimal Scope3Ton { get; set; }
        public string AlertTier { get; set; } = "Warning_80"; // Warning_80, Exceeded_100, Test
        public string? DashboardUrl { get; set; }
    }

    public async Task SendGhgTargetAlertEmailAsync(string recipientEmail, GhgTargetAlertEmailModel model, string? ccEmail = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                _logger?.LogWarning("Cannot send GHG target alert: recipient email is empty.");
                return;
            }

            var isExceeded = model.ConsumptionPercent >= 100m;
            var isTest = model.AlertTier.Equals("Test", StringComparison.OrdinalIgnoreCase);

            string subject;
            string bannerBg;
            string bannerTextColor;
            string bannerBorder;
            string statusBadgeText;
            string statusBadgeBg;

            if (isTest)
            {
                subject = $"[CARBON ONE - TEST] GHG Target Threshold Alert: {model.OrganizationName} ({model.ConsumptionPercent:F1}%)";
                bannerBg = "#eff6ff";
                bannerTextColor = "#1e40af";
                bannerBorder = "#3b82f6";
                statusBadgeText = "TEST NOTIFICATION";
                statusBadgeBg = "#3b82f6";
            }
            else if (isExceeded)
            {
                subject = $"[CARBON ONE - CRITICAL] Annual GHG Target Exceeded: {model.OrganizationName} ({model.ConsumptionPercent:F1}% of Target)";
                bannerBg = "#fef2f2";
                bannerTextColor = "#991b1b";
                bannerBorder = "#ef4444";
                statusBadgeText = "TARGET EXCEEDED";
                statusBadgeBg = "#ef4444";
            }
            else
            {
                subject = $"[CARBON ONE - WARNING] GHG Emissions Approaching Annual Target: {model.OrganizationName} ({model.ConsumptionPercent:F1}% of Target)";
                bannerBg = "#fffbeb";
                bannerTextColor = "#92400e";
                bannerBorder = "#f59e0b";
                statusBadgeText = "TARGET WARNING (>80%)";
                statusBadgeBg = "#f59e0b";
            }

            var progressWidth = Math.Min(100, Math.Max(5, (int)model.ConsumptionPercent));
            var progressBarColor = isExceeded ? "#ef4444" : (model.ConsumptionPercent >= 80m ? "#f59e0b" : "#10b981");
            var dashboardLink = !string.IsNullOrWhiteSpace(model.DashboardUrl) ? model.DashboardUrl : "https://carbon-one.acamp.local/dashboard";

            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{subject}</title>
</head>
<body style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 24px; color: #1e293b;'>
    <div style='max-width: 640px; margin: 0 auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);'>
        <!-- Header -->
        <div style='background-color: #0f172a; padding: 20px 28px; border-bottom: 3px solid #10b981;'>
            <table style='width: 100%; border-collapse: collapse;'>
                <tr>
                    <td>
                        <span style='font-size: 22px; font-weight: 800; letter-spacing: -0.5px;'>
                            <span style='color: #ffffff;'>CARBON</span> <span style='color: #10b981;'>ONE</span>
                        </span>
                        <span style='background-color: #10b981; color: #ffffff; font-size: 11px; font-weight: 700; padding: 2px 6px; border-radius: 4px; margin-left: 6px; vertical-align: middle;'>v2.0</span>
                    </td>
                    <td style='text-align: right;'>
                        <span style='background-color: {statusBadgeBg}; color: #ffffff; font-size: 11px; font-weight: 700; padding: 4px 8px; border-radius: 4px; text-transform: uppercase;'>{statusBadgeText}</span>
                    </td>
                </tr>
            </table>
        </div>

        <!-- Banner -->
        <div style='background-color: {bannerBg}; border-left: 4px solid {bannerBorder}; padding: 16px 24px;'>
            <h3 style='margin: 0 0 6px 0; color: {bannerTextColor}; font-size: 16px; font-weight: 700;'>
                {(isExceeded ? "⚠️ Annual GHG Emission Cap Exceeded" : (isTest ? "ℹ️ Test Notification: GHG Target Alert" : "⚠️ Annual GHG Target Approaching Threshold"))}
            </h3>
            <p style='margin: 0; color: {bannerTextColor}; font-size: 13.5px; line-height: 1.5;'>
                Cumulative greenhouse gas emissions for <strong>{model.OrganizationName}</strong> in reporting year <strong>{model.ReportingYear}</strong> have reached <strong>{model.ConsumptionPercent:F1}%</strong> of the allowable annual reduction target ceiling.
            </p>
        </div>

        <!-- Content Body -->
        <div style='padding: 24px 28px;'>
            <!-- Progress Bar Card -->
            <div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin-bottom: 20px;'>
                <div style='display: flex; justify-content: space-between; margin-bottom: 8px; font-size: 13px; font-weight: 600;'>
                    <span style='color: #475569;'>Annual Target Cap Consumption:</span>
                    <span style='color: {progressBarColor}; font-weight: 700; font-size: 14px;'>{model.ConsumptionPercent:F1}% ({model.CurrentEmissionsTon:N2} / {model.AnnualTargetCapTon:N2} tCO₂e)</span>
                </div>
                <div style='background-color: #e2e8f0; height: 12px; border-radius: 6px; overflow: hidden;'>
                    <div style='background-color: {progressBarColor}; width: {progressWidth}%; height: 100%;'></div>
                </div>
            </div>

            <!-- Details Table -->
            <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 13.5px;'>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Organization:</td>
                    <td style='padding: 10px 4px; font-weight: 600; text-align: right; color: #0f172a;'>{model.OrganizationName}</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Reporting Year:</td>
                    <td style='padding: 10px 4px; font-weight: 600; text-align: right; color: #0f172a;'>{model.ReportingYear}</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Base Year & Emissions:</td>
                    <td style='padding: 10px 4px; font-weight: 600; text-align: right; color: #0f172a;'>Year {model.BaseYear} ({model.BaseYearEmissionsTon:N2} tCO₂e)</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Annual Target Ceiling:</td>
                    <td style='padding: 10px 4px; font-weight: 700; text-align: right; color: #0f172a;'>{model.AnnualTargetCapTon:N2} tCO₂e</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Cumulative Actual Emissions:</td>
                    <td style='padding: 10px 4px; font-weight: 700; text-align: right; color: {progressBarColor};'>{model.CurrentEmissionsTon:N2} tCO₂e</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Scope 1 (Direct):</td>
                    <td style='padding: 10px 4px; text-align: right; color: #334155;'>{model.Scope1Ton:N2} tCO₂e</td>
                </tr>
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 4px; color: #64748b;'>Scope 2 (Electricity):</td>
                    <td style='padding: 10px 4px; text-align: right; color: #334155;'>{model.Scope2Ton:N2} tCO₂e</td>
                </tr>
                <tr>
                    <td style='padding: 10px 4px; color: #64748b;'>Scope 3 (Value Chain):</td>
                    <td style='padding: 10px 4px; text-align: right; color: #334155;'>{model.Scope3Ton:N2} tCO₂e</td>
                </tr>
            </table>

            <!-- Action Button -->
            <div style='text-align: center; margin: 28px 0 16px 0;'>
                <a href='{dashboardLink}' style='background-color: #0f172a; color: #ffffff; font-size: 14px; font-weight: 700; text-decoration: none; padding: 12px 28px; border-radius: 6px; display: inline-block; border-bottom: 2px solid #10b981;'>
                    Open Executive Dashboard
                </a>
            </div>
            <p style='text-align: center; margin: 0; font-size: 12px; color: #94a3b8;'>
                Review facility-level activity data, hotspot analytics, and mitigation pathways.
            </p>
        </div>

        <!-- Footer -->
        <div style='background-color: #f8fafc; padding: 16px 28px; border-top: 1px solid #e2e8f0; font-size: 11.5px; color: #64748b; line-height: 1.5;'>
            <p style='margin: 0 0 4px 0;'>
                This is an automated notification from <strong>CARBON ONE</strong> (Automatic Carbon Account Management Platform).
            </p>
            <p style='margin: 0;'>
                Standards compliance: GHG Protocol Corporate Standard | ISO 14064-1:2018 | SBTi Pathway Alignment.
            </p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync_003(recipientEmail, subject, body, ccEmail);
            _logger?.LogInformation("Sent GHG target alert email ({Tier}) to {Email} for {Org} ({Ratio:F1}%)",
                model.AlertTier, recipientEmail, model.OrganizationName, model.ConsumptionPercent);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send GHG target alert email to {Email}", recipientEmail);
            throw;
        }
    }
    #endregion
}

namespace Bigant.Didox;

public sealed class DidoxOptions
{
    public const string Section = "Didox";

    /// <summary>false = tizim Didox'siz ishlaydi (hozirgi holat).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Test stend:  https://testapi3.didox.uz/
    /// Prod (partner): https://api-partners.didox.uz/
    /// Partner token account manager orqali olinadi (@Didox_account).
    /// </summary>
    public string BaseUrl { get; set; } = "https://testapi3.didox.uz";

    /// <summary>Partner token — Didox account manager beradi. user-secrets da saqlang.</summary>
    public string? PartnerToken { get; set; }

    public string? Login { get; set; }
    /// <summary>PAROLNI shu yerga yozmang — user-secrets yoki environment variable ishlating.</summary>
    public string? Password { get; set; }
    public string? CompanyInn { get; set; }

    /// <summary>Kiruvchi hujjatlarni necha daqiqada bir tortish.</summary>
    public int InboxPollMinutes { get; set; } = 10;
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int MaxSendAttempts { get; set; } = 8;

    /// <summary>
    /// Bir marta nechta hujjatning holati so'raladi (har 5 daqiqada).
    /// Didox so'rov chegarasi qo'ysa shu raqamni kamaytiring.
    /// </summary>
    public int StatusBatchSize { get; set; } = 200;
}

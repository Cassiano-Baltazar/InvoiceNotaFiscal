namespace InvoiceApp.Web.Data.Entities;

/// <summary>
/// What the user entered for one month on the withdrawal screen: revenue from notes issued outside
/// the app (e.g. the city hall portal before the app existed), the amount of the real DAS guide and the
/// pró-labore really declared, which decides the Fator R.
/// </summary>
public class MonthlyTaxRecord
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal OtherRevenueBrl { get; set; }
    public decimal? DasGuideAmount { get; set; }

    /// <summary>Pró-labore declared for the month (0 when none was). Empty means the % set in Settings.</summary>
    public decimal? ProLaboreDeclared { get; set; }
}

namespace InvoiceApp.Web.Data.Entities;

/// <summary>
/// What the user entered for one month on the withdrawal screen: revenue from notes issued outside
/// the app (e.g. the city hall portal before the app existed) and the amount of the real DAS guide.
/// </summary>
public class MonthlyTaxRecord
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal OtherRevenueBrl { get; set; }
    public decimal? DasGuideAmount { get; set; }
}

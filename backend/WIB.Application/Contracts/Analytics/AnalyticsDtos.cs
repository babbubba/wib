namespace WIB.Application.Contracts.Analytics;

public class SpendingAggregateDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid? StoreId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Amount { get; set; }
}

public class PriceHistoryPointDto
{
    public DateTime Date { get; set; }
    public decimal UnitPrice { get; set; }
}


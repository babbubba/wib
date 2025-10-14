namespace WIB.Domain;

public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public Guid? StoreLocationId { get; set; }
    public StoreLocation? StoreLocation { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal? TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? RawText { get; set; }
    public string? ImageObjectKey { get; set; }
    public int? OcrStoreX { get; set; }
    public int? OcrStoreY { get; set; }
    public int? OcrStoreW { get; set; }
    public int? OcrStoreH { get; set; }
    public List<ReceiptLine> Lines { get; set; } = new();
}

public class ReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string LabelRaw { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    public int SortIndex { get; set; }
    public int? OcrX { get; set; }
    public int? OcrY { get; set; }
    public int? OcrW { get; set; }
    public int? OcrH { get; set; }
    // Peso e prezzo al Kg quando disponibili
    public decimal? WeightKg { get; set; }
    public decimal? PricePerKg { get; set; }
    public Guid? PredictedTypeId { get; set; }
    public Guid? PredictedCategoryId { get; set; }
    public decimal? PredictionConfidence { get; set; }
}

public class Store
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Chain { get; set; }
    public List<Receipt> Receipts { get; set; } = new();
    public List<StoreLocation> Locations { get; set; } = new();
}

public class StoreLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? VatNumber { get; set; }
}

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
}

public class ProductType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? AliasesJson { get; set; }
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? GTIN { get; set; }
    public Guid ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public List<ProductAlias> Aliases { get; set; } = new();
}

public class ProductAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Alias { get; set; } = string.Empty;
}

public class PriceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public DateTime Date { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? PricePerKg { get; set; }
}

public class BudgetMonth
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal LimitAmount { get; set; }
}

public class ExpenseAggregate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid? StoreId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Amount { get; set; }
}

public class LabelingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string LabelRaw { get; set; } = string.Empty;
    public Guid? PredictedTypeId { get; set; }
    public Guid? PredictedCategoryId { get; set; }
    public Guid FinalTypeId { get; set; }
    public Guid? FinalCategoryId { get; set; }
    public decimal Confidence { get; set; }
    public DateTimeOffset WhenUtc { get; set; }
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiry { get; set; }
    public List<Receipt> Receipts { get; set; } = new();
    public List<RefreshToken> RefreshTokens { get; set; } = new();
    public List<UserRole> UserRoles { get; set; } = new();
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRevoked { get; set; } = false;
    public string? RevokedReason { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
}

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<UserRole> UserRoles { get; set; } = new();
}

public class UserRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}

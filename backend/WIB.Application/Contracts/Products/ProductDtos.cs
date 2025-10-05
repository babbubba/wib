using System.ComponentModel.DataAnnotations;

namespace WIB.Application.Contracts.Products;

public class AddAliasRequest
{
    [Required]
    public string Alias { get; set; } = string.Empty;
}


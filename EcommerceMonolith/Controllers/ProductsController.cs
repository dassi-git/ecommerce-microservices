using Microsoft.AspNetCore.Mvc;

namespace EcommerceMonolith.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private static readonly List<Product> Products = new()
    {
        new Product(1, "Laptop", 999.99m, 10),
        new Product(2, "Smartphone", 699.50m, 25),
        new Product(3, "Headphones", 149.99m, 40)
    };

    [HttpGet("GetProducts")]
    public ActionResult<List<Product>> GetProducts()
    {
        return Ok(Products);
    }

    [HttpPost("PlaceOrder")]
    public ActionResult<object> PlaceOrder([FromBody] OrderRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { message = "Quantity must be greater than zero." });
        }

        var product = Products.FirstOrDefault(p => p.Id == request.ProductId);
        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        if (product.Stock < request.Quantity)
        {
            return BadRequest(new { message = "Not enough stock available." });
        }

        var updatedProduct = product with { Stock = product.Stock - request.Quantity };
        var index = Products.FindIndex(p => p.Id == product.Id);
        Products[index] = updatedProduct;

        return Ok(new
        {
            message = $"Order placed successfully for {request.Quantity} unit(s) of {product.Name}.",
            remainingStock = updatedProduct.Stock
        });
    }
}

public record Product(int Id, string Name, decimal Price, int Stock);
public record OrderRequest(int ProductId, int Quantity);

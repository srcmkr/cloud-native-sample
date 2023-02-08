﻿using System.Net;
using System.Text.Json;
using Gateway.Models;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly ILogger<OrdersController> _logger;
    private readonly int _daprHttpPort;
    private readonly HttpClient _httpClient;

    public OrdersController(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<OrdersController> logger)
    {
        _logger = logger;
        _daprHttpPort = configuration.GetValue<int>("DAPR_HTTP_PORT");
        _logger.LogInformation("Using DAPR_HTTP_PORT {actual}", _daprHttpPort);
        _httpClient = httpClientFactory.CreateClient(Constants.HttpClientName);
        _logger.LogInformation("Created named HttpClient {Name}", Constants.HttpClientName);
    }

    [HttpGet]
    [Route("monitor")]
    public async Task<IActionResult> GetOrderMonitorDataAsync()
    {
        var getOrdersUrl = BuildUrl("orders", "orders");
        var getProductsUrl = BuildUrl("products", "products");
        var getOrders = _httpClient.GetAsync(getOrdersUrl);
        var getProducts = _httpClient.GetAsync(getProductsUrl);

        var res = await Task.WhenAll(getOrders, getProducts);
        // handle unauthorized
        if (res[0].StatusCode == HttpStatusCode.Unauthorized || res[1].StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received HTTP 401 from backend service {Url1}={ResponseCode1} {Url2}={ResponseCode2}",
                getOrdersUrl, res[0].StatusCode,
                getProductsUrl, res[1].StatusCode);
            return Unauthorized();
        }
        // treat all other non successful responses as errors
        if (!res[0].IsSuccessStatusCode || !res[1].IsSuccessStatusCode)
        {
            _logger.LogWarning("Received non-successful StatusCode from backend service {Url1}={ResponseCode1} {Url2}={ResponseCode2}",
                getOrdersUrl, res[0].StatusCode,
                getProductsUrl, res[1].StatusCode);
            return StatusCode((int)HttpStatusCode.InternalServerError);
        }
        var orders = await res[0].Content.ReadFromJsonAsync<List<JsonElement>>();
        var products = await res[1].Content.ReadFromJsonAsync<List<JsonElement>>();
        CustomMetrics
            .ProxiedRequests
            .Add(1, new KeyValuePair<string, object?>("kind", "composition"));

        return Ok(orders.Select(o =>
        {
            var res = new OrderMonitorListModel
            {
                Id = o.GetProperty("id").GetGuid(),
                UserId = o.GetProperty("userId").GetString() ?? string.Empty,
                Positions = new List<OrderMonitorPositionModel>()
            };

            foreach (dynamic pos in o.GetProperty("positions").EnumerateArray())
            {
                res.Positions.Add(TransformPosition(products, pos));
            }

            return res;
        }));
    }

    private OrderMonitorPositionModel TransformPosition(List<JsonElement> products, JsonElement pos)
    {
        var found = products.FirstOrDefault(pr =>
            pr.GetProperty("id").GetString() == pos.GetProperty("productId").GetString());

        if (found.ValueKind == JsonValueKind.Undefined)
        {
            return new OrderMonitorPositionModel();
        }

        return new OrderMonitorPositionModel
        {
            ProductId = pos.GetProperty("productId").GetGuid(),
            ProductName = found.GetProperty("name").GetString() ?? string.Empty,
            ProductDescription = found.GetProperty("description").GetString() ?? string.Empty,
            ProductPrice = found.GetProperty("price").GetDouble(),
            Quantity = pos.GetProperty("quantity").GetInt32()
        };
    }

    private string BuildUrl(string service, string path)
    {
        if (_daprHttpPort != 0)
        {
            return $"http://localhost:{_daprHttpPort}/v1.0/invoke/{service}/method/{path}";
        }

        var port = Request.Host.Port;

        return $"http://localhost:{port}/{service}";
    }
}

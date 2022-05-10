﻿using Dapr.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdersService.Configuration;
using OrdersService.Data.Repositories;
using OrdersService.Extensions;
using OrdersService.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace OrdersService.Controllers;

[Authorize("api")]
[ApiController]
[Produces("application/json")]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrdersRepository _repository;
    private readonly DaprClient _dapr;
    private readonly OrdersServiceConfiguration _config;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrdersRepository repository, DaprClient dapr, OrdersServiceConfiguration config, ILogger<OrdersController> logger)
    {
        _repository = repository;
        _dapr = dapr;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    [Route("", Name = "CreateOrder")]
    [SwaggerOperation(OperationId = "CreateOrder", Tags = new[] { "Orders" }, Summary = "Create a new order", Description = "Invoke this endpoint to place a new order")]
    [SwaggerResponse(202, Description = "Order has been accepted")]
    [SwaggerResponse(400)]
    [SwaggerResponse(500)]
    public async Task<IActionResult> CreateOrderAsync([FromBody] CreateOrderModel model)
    {
        var id = Guid.NewGuid();
        var now = DateTime.Now;
        var userName = HttpContext.GetUserName();

        _logger.LogTrace("Order ({Id}) submitted at {Now} by {CustomerName}", id, now.ToShortTimeString(), userName);

        var newOrder = model.ToEntity(id, now, HttpContext.GetUserId(), userName);

        await _repository.AddNewOrderAsync(newOrder);

        // TODO: manually craft message to get real end-to-end tracing
        if (HttpContext.Request.Headers.TryGetValue("traceid", out var traceid))
        {
            _logger.LogInformation($"CreateOrderAsync: traceid={traceid}");
        }
        // curl -X POST http://localhost:3601/v1.0/publish/order-pub-sub/orders -H "Content-Type: application/json" -d '{"orderId": "100"}'
        // curl -X POST http://localhost:3601/v1.0/publish/order-pub-sub/orders -H "Content-Type: application/cloudevents+json" -d '{"specversion" : "1.0", "type" : "com.dapr.cloudevent.sent", "source" : "testcloudeventspubsub", "subject" : "Cloud Events Test", "id" : "someCloudEventId", "time" : "2021-08-02T09:00:00Z", "datacontenttype" : "application/cloudevents+json", "data" : {"orderId": "100"}}'
        //var httpClient = new HttpClient();
        //httpClient.PostAsJsonAsync<

        await _dapr.PublishEventAsync(_config.CreateOrderPubSubName, _config.CreateOrderTopicName, newOrder, CancellationToken.None)!;

        return Accepted(new { OrderId = newOrder.Id });
    }

    [HttpGet]
    [Route("", Name = "GetOrders")]
    [SwaggerOperation(OperationId = "GetOrders", Tags = new[] { "Orders" }, Summary = "Load all orders", Description = "This endpoint returns all orders")]
    [SwaggerResponse(200, Description = "The order", Type = typeof(IEnumerable<OrderListModel>))]
    [SwaggerResponse(400)]
    [SwaggerResponse(500)]
    public async Task<IActionResult> GetOrdersAsync()
    {
        var found = await _repository.GetAllOrdersAsync();

        return Ok(found.Select(f => f.ToListModel()));
    }
}

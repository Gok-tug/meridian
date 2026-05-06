using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Sample.AspNetCoreFlow;

[Route("[controller]/[action]")]
public sealed class OrdersController : Controller
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> Detail(int orderId)
    {
        var result = await _mediator.Send(new GetOrderQuery(orderId));
        return Ok(result);
    }

    [HttpPost("create/{orderId}")]
    public async Task<IActionResult> Create(string orderId)
    {
        var result = await _mediator.Send(new CreateOrderCommand(orderId));
        return Ok(result);
    }

    [HttpGet("~/health")]
    public IActionResult Health()
    {
        return Ok("healthy");
    }

    [HttpGet("/regex/{id:regex(^\\d+$)}")]
    public IActionResult RegexRoute(int id)
    {
        return Ok(id);
    }
}

using System.Net;
using Common.Events;
using Microsoft.AspNetCore.Mvc;
using PaymentMS.Models;
using PaymentMS.Repositories;
using PaymentMS.Services;

namespace PaymentMS.Controllers;

[ApiController]
public class PaymentController : ControllerBase
{

    private readonly IPaymentService paymentService;
    private readonly IPaymentRepository paymentRepository;
    private readonly ILogger<PaymentController> logger;
    
    public PaymentController(IPaymentService paymentService, IPaymentRepository paymentRepository, ILogger<PaymentController> logger)
    {
        this.paymentService = paymentService;
        this.paymentRepository = paymentRepository;
        this.logger = logger;
    }

    [HttpGet]
    [Route("{customerId}/{orderId}")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.NotFound)]
    public ActionResult<IEnumerable<OrderPaymentModel>> GetPaymentByOrderId(int customerId, int orderId)
    {
        var res = this.paymentRepository.GetByOrderId(customerId, orderId);
        return res is not null ? Ok( res ) : NotFound();
    }

    [Route("/cleanup")]
    [HttpPatch]
    [ProducesResponseType((int)HttpStatusCode.Accepted)]
    public ActionResult Cleanup()
    {
        this.logger.LogWarning("Cleanup requested at {0}", DateTime.UtcNow);
        this.paymentService.Cleanup();
        return Ok();
    }

}


using DLNAServer.Features.Subscriptions.Data;
using DLNAServer.Features.Subscriptions.Interfaces;
using DLNAServer.Helpers.Attributes;
using DLNAServer.Helpers.Logger;
using Microsoft.AspNetCore.Mvc;

namespace DLNAServer.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public partial class EventController : Controller
    {
        private readonly ILogger<EventController> _logger;
        private readonly ISubscriptionService _subscriptionService;
        public EventController(
            ILogger<EventController> logger,
            ISubscriptionService subscriptionService)
        {
            _logger = logger;
            _subscriptionService = subscriptionService;
        }
        [HttpSubscribe("eventAction/{serviceID}")]
        public IActionResult SubscribeAction([FromRoute] string serviceID)
        {
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(SubscribeAction),
                this.HttpContext.Connection.RemoteIpAddress,
                this.HttpContext.Connection.RemotePort,
                this.HttpContext.Connection.LocalIpAddress,
                this.HttpContext.Connection.LocalPort,
                this.HttpContext.Request.Path.Value,
                this.HttpContext.Request.Method);

            var callback = Request.Headers.FirstOrDefault(static (h) => h.Key.Contains("Callback", StringComparison.InvariantCultureIgnoreCase)).Value.FirstOrDefault();
            var timeout = Request.Headers.FirstOrDefault(static (h) => h.Key.Contains("Timeout", StringComparison.InvariantCultureIgnoreCase)).Value.FirstOrDefault();
            var sid = Request.Headers.FirstOrDefault(static (h) => h.Key.Contains("SID", StringComparison.InvariantCultureIgnoreCase)).Value.FirstOrDefault();
            var timeoutNumber = int.TryParse(new string(timeout?.Where(static (c) => char.IsDigit(c)).ToArray()), out int number) ? number : -9999;

            if (string.IsNullOrWhiteSpace(callback)
                || string.IsNullOrWhiteSpace(timeout)
                || timeoutNumber == -9999)
            {
                WarningIncorrectRequestParameter(callback, timeout, timeoutNumber);
                return BadRequest();
            }

            if (string.IsNullOrWhiteSpace(sid)
                || _subscriptionService.GetSubscription(sid!) is not Subscription subscription)
            {
                sid = $"uuid:{Guid.NewGuid()}";
                subscription = _subscriptionService.GetOrAddSubscription(sid!, callback!, TimeSpan.FromSeconds(timeoutNumber));
            }
            else if (subscription.IsExpired())
            {
                _subscriptionService.TryRemoveSubscription(subscription.SID);
                sid = $"uuid:{Guid.NewGuid()}";
                subscription = _subscriptionService.GetOrAddSubscription(sid!, callback!, TimeSpan.FromSeconds(timeoutNumber));
            }

            _ = _subscriptionService.UpdateLastNotifyTime(sid!);

            _ = Response.Headers.TryAdd("SID", subscription.SID);
            _ = Response.Headers.TryAdd("TIMEOUT", subscription.Timeout.ToString());

            return Ok("subscribed");
        }
        [HttpUnsubscribe("eventAction/{serviceID}")]
        public IActionResult UnsubscribeAction([FromRoute] string serviceID)
        {
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(UnsubscribeAction),
                this.HttpContext.Connection.RemoteIpAddress,
                this.HttpContext.Connection.RemotePort,
                this.HttpContext.Connection.LocalIpAddress,
                this.HttpContext.Connection.LocalPort,
                this.HttpContext.Request.Path.Value,
                this.HttpContext.Request.Method);

            return Ok("unsubscribed");
        }
    }
}
